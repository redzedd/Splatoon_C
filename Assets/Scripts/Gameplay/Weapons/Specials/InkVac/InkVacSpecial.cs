using System.Collections.Generic;
using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // Ink Vac 的執行期行為(research/04 §2.8、JP wiki キューインキ):
    //   發動(背上吸塵器罐、雙手端噴嘴、上身照開火姿勢對準準心)→ 17F 前隙 →
    //   吸引模式:按住攻擊(人形 12F / 從烏賊 18F 前隙)→ 漩渦錐每幀 +0.3334 u 長到 15 u,吸走敵方墨彈/炸彈/爆炸(InkVacuum;
    //     吸入量 = 傷害量,1100 滿)、漩渦裡的敵人變慢且每幀扣 0.22% 墨(容量每幀 +1.5)、裝置每幀 2 傷;每 20F 在身後塗一片;
    //     放開攻擊 = 停止吸(可以潛游,再按要重新前隙、錐從 0 長)。噴嘴轉向比鏡頭慢。
    //   吸滿或能量 360F 用完(ContinueAfterGaugeEmpty)→ 發射模式:20F 準備 → 放開攻擊發射(沒按著就要按一下再放),
    //     150F 沒放自動發射(最後 1 秒彈道預覽閃爍);反擊彈 = InkBullet(S3 彈道、直擊 220 貫穿角色、對物件 0),
    //     落地/撞到物件 → InkVacBlast 50F 後爆 220(半徑 6 → 11 依吸入量)。發射即結束大招。
    //   走速 0.09 → 0.07 u/F(越吸越重)、發射模式 0.072 u/F(InklingModifiers;跑速能力不影響)。
    public sealed class InkVacSpecial : SpecialWeapon
    {
        private enum Phase
        {
            Startup,
            Suction,
            Transition,
            Aim,
            Done,
        }

        // S3 中量級人形跑速 0.096 u/F(換算走速倍率用)
        private const float BaseRunSpeed = 5.76f;
        private const float EnemyBodyPad = 0.35f;
        private const int GuidePoints = 40;
        private const float ObjectTickSeconds = 0.1f;

        private static readonly string[] FillHints = BuildFillHints();

        private InkVacConfig _config;
        private readonly SpecialHeldModel _nozzle = new SpecialHeldModel();
        private readonly object _poisonSource = new object();
        private InkVacuum _vacuum;
        private InkVacVortex _vortex;
        private GameObject _backpack;
        private Transform _backpackFill;
        private LineRenderer _guide;
        private readonly Vector3[] _guidePoints = new Vector3[GuidePoints];
        private CharacterVisual _visual;
        private WeaponLoadout _loadout;
        private SquidController _squid;
        private AudioLoop _loop;
        private InkBulletImpactHandler _onImpacted;
        private System.Action<Vector3, float> _onSucked;
        private InkBulletDamageFunc _shotDamageFunc;

        private Phase _phase = Phase.Done;
        private float _activatedAt;
        private bool _sucking;
        private float _suckStart;
        private Vector3 _axis = Vector3.forward;
        private float _transitionEnd;
        private float _deadline;
        private bool _armed;
        private float _nextPaintAt;
        private float _objectTick;
        private int _moveStep = -1;
        private float _nextAbsorbSoundAt;
        private float _lastSquidTime = float.NegativeInfinity;
        private int _shotAttackId;
        private float _shotFill;
        private Team _shotTeam;
        private Inkling _shotOwner;

        // bot
        private float _botStrafeSign = 1f;
        private float _botStrafeFlipAt;

        public override SpecialWeaponConfig BaseConfig => _config;
        public InkVacConfig Config => _config;

        public override bool BlocksMainWeapon => true;
        public override bool BlocksSubWeapon => true;

        // 吸引模式沒按攻擊時可以潛游;吸引中、發射模式不行(按攻擊會從烏賊變回人形開始吸)
        public override bool BlocksSquid => _phase != Phase.Suction || _sucking || (Input != null && Input.AttackHeld);

        public override string HudHint
        {
            get
            {
                switch (_phase)
                {
                    case Phase.Suction:
                        return FillHints[Mathf.Clamp(Mathf.FloorToInt(Fill01 * 100f), 0, 100)];
                    case Phase.Transition:
                        return "準備發射";
                    case Phase.Aim:
                        return _armed ? "放開 發射" : "按住再放開 發射";
                    default:
                        return null;
                }
            }
        }

        // ── 狀態(HUD、測試、bot)──
        public float Absorbed => _vacuum != null ? _vacuum.Absorbed : 0f;
        public float Fill01 => _config != null ? Mathf.Clamp01(Absorbed / Mathf.Max(1f, _config.Capacity)) : 0f;
        public bool Sucking => _sucking && _vacuum != null && _vacuum.IsAlive;
        public float VortexLength => _vacuum != null && _vacuum.IsAlive ? _vacuum.Length : 0f;
        public InkVacuum Vacuum => _vacuum;
        public bool InSuctionPhase => IsActive && _phase == Phase.Suction;
        public bool InFireMode => IsActive && (_phase == Phase.Transition || _phase == Phase.Aim);
        public bool CanFireNow => IsActive && _phase == Phase.Aim;
        public float ActivatedAt => _activatedAt;
        public float SuctionStartedAt => _suckStart;
        public float FireModeEnteredAt { get; private set; } = float.NegativeInfinity;
        public float AimStartedAt { get; private set; } = float.NegativeInfinity;
        public float LastShotTime { get; private set; } = float.NegativeInfinity;
        public float LastShotFill { get; private set; }
        public bool LastShotAuto { get; private set; }
        public int ShotsFired { get; private set; }
        public InkBullet LastShot { get; private set; }
        public Vector3 LastMuzzle { get; private set; }
        public Vector3 LastShotDirection { get; private set; }
        public InkVacBlast LastBlast { get; private set; }
        public bool GuideShown => _guide != null && _guide.enabled;
        public int EnemiesInVortex { get; private set; }

        public override void Configure(SpecialWeaponConfig config)
        {
            _config = config as InkVacConfig;
            if (_config == null)
            {
                Debug.LogError($"InkVacSpecial:設定型別不符({config})", this);
                return;
            }
            if (_config.ShotPrefab == null || _config.BlastPrefab == null || _config.VortexPrefab == null
                || _config.SuckFxPrefab == null)
            {
                Debug.LogError("InkVacSpecial:設定缺少反擊彈/爆炸/漩渦/吸入特效 prefab(先跑 Tools/SplatoonC/M7 Setup)", this);
                return;
            }
            SharedPool.Prewarm(_config.ShotPrefab, 1);
            SharedPool.Prewarm(_config.BlastPrefab, 1);
            SharedPool.Prewarm(_config.SuckFxPrefab, 8);
        }

        protected override void Awake()
        {
            base.Awake();
            _visual = GetComponent<CharacterVisual>();
            _loadout = GetComponent<WeaponLoadout>();
            _squid = GetComponent<SquidController>();
            _vacuum = new InkVacuum(Owner);
            _onSucked = OnSucked;
            _vacuum.Sucked = _onSucked;
            _onImpacted = OnShotImpact;
            // 反擊彈落地要在大招結束(甚至死亡)後照樣處理 → 事件在整個元件生命週期都訂閱
            InkBullet.Impacted += _onImpacted;
            _shotDamageFunc = ShotDamage;
        }

        private void OnDestroy()
        {
            InkBullet.Impacted -= _onImpacted;
            _nozzle.Destroy();
            if (_vortex != null)
            {
                Destroy(_vortex.gameObject);
            }
            if (_backpack != null)
            {
                Destroy(_backpack);
            }
        }

        public override void OnActivated()
        {
            if (_config == null || _config.ShotPrefab == null)
            {
                EndSpecial();
                return;
            }
            float now = Time.time;
            _activatedAt = now;
            _phase = Phase.Startup;
            _sucking = false;
            _armed = false;
            _objectTick = 0f;
            _moveStep = -1;
            FireModeEnteredAt = float.NegativeInfinity;
            _vacuum.ResetAbsorbed();
            _vacuum.Disable();
            _axis = AimForward();
            _nozzle.Show(Owner, _config.NozzlePrefab, Vector3.zero, Vector3.zero);
            _guide = _nozzle.Instance != null ? _nozzle.Instance.GetComponentInChildren<LineRenderer>(true) : null;
            if (_guide != null)
            {
                _guide.enabled = false;
            }
            ShowBackpack(true);
            if (_visual != null)
            {
                _visual.AimOverride = true;
            }
            UpdateMoveSpeed();
            GameAudio.PlayFrom(Owner != null && Owner.IsLocalPlayer, _config.ActivateSound, transform.position + Vector3.up,
                1f, 0f);
        }

        // 吸引 360F 到(能量歸 0):轉發射模式,行為繼續到發射
        public override bool ContinueAfterGaugeEmpty()
        {
            if (_phase == Phase.Startup || _phase == Phase.Suction)
            {
                EnterFireMode();
            }
            return _phase == Phase.Transition || _phase == Phase.Aim;
        }

        public override void OnEnded()
        {
            _phase = Phase.Done;
            StopSuction();
            _nozzle.Hide();
            _guide = null;
            ShowBackpack(false);
            RemoveMoveSpeed();
            if (_visual != null)
            {
                _visual.AimOverride = false;
            }
        }

        private void OnDisable()
        {
            if (_phase != Phase.Done)
            {
                _phase = Phase.Done;
            }
            StopSuction();
            _nozzle.Hide();
            ShowBackpack(false);
            RemoveMoveSpeed();
            if (_visual != null)
            {
                _visual.AimOverride = false;
            }
        }

        private void Update()
        {
            if (!IsActive || _config == null || Input == null)
            {
                return;
            }
            float now = Time.time;
            // 「從烏賊開始吸」要看前一兩幀是不是烏賊(按下攻擊的同一幀 SquidController 可能已經先變回人形)
            if (_squid != null && _squid.IsSquid)
            {
                _lastSquidTime = now;
            }
            switch (_phase)
            {
                case Phase.Startup:
                    float available = _activatedAt + _config.ActivationLagFrames / 60f;
                    if (now >= available)
                    {
                        _phase = Phase.Suction;
                        if (Input.AttackHeld && !_sucking)
                        {
                            // 前隙中就按著:從前隙結束那一刻起算吸引前隙(不受幀率量化影響)
                            _sucking = true;
                            _suckStart = available + _config.SuctionLagHuman / 60f;
                            _nextPaintAt = _suckStart;
                            _axis = AimForward();
                        }
                        UpdateSuction(now);
                    }
                    break;
                case Phase.Suction:
                    UpdateSuction(now);
                    break;
                case Phase.Transition:
                    if (now >= _transitionEnd)
                    {
                        _phase = Phase.Aim;
                        AimStartedAt = now;
                        _armed = Input.AttackHeld;
                        _deadline = now + _config.ExhaleWaitFrames / 60f;
                    }
                    UpdateGuide(now);
                    break;
                case Phase.Aim:
                    UpdateGuide(now);
                    if (!_armed && Input.AttackHeld)
                    {
                        _armed = true;
                    }
                    else if (_armed && !Input.AttackHeld)
                    {
                        Fire(false);
                        return;
                    }
                    if (now >= _deadline)
                    {
                        Fire(true);
                        return;
                    }
                    break;
            }
            UpdateMoveSpeed();
            UpdateBackpackFill();
        }

        // ── 吸引 ──

        private void UpdateSuction(float now)
        {
            bool want = Input.AttackHeld;
            if (want && !_sucking)
            {
                _sucking = true;
                bool fromSquid = now - _lastSquidTime <= Time.deltaTime * 2f + 1e-4f;
                _suckStart = now + (fromSquid ? _config.SuctionLagSquid : _config.SuctionLagHuman) / 60f;
                _nextPaintAt = _suckStart;
                _axis = AimForward();
            }
            else if (!want && _sucking)
            {
                StopSuction();
            }
            if (!_sucking || now < _suckStart)
            {
                return;
            }
            float dt = Time.deltaTime;
            Vector3 aim = AimForward();
            _axis = Vector3.RotateTowards(_axis, aim, _config.NozzleTurnDegPerSecond * Mathf.Deg2Rad * dt, 0f).normalized;
            Vector3 apex = Muzzle(_axis);
            float length = ClipByWalls(apex, _axis, InkVacModel.LengthAt((now - _suckStart) * 60f));
            Team team = Owner != null ? Owner.Team : Team.Alpha;
            _vacuum.Enable(team);
            _vacuum.Apex = apex;
            _vacuum.Axis = _axis;
            _vacuum.Length = length;
            EnsureVortex();
            if (_vortex != null)
            {
                _vortex.Show(_vacuum.Apex, _axis, length, team);
            }
            if (_loop == null)
            {
                _loop = GameAudio.StartLoop(_config.SuctionLoop, transform, true, 0.8f);
            }
            AffectEnemies(team, dt);
            AffectObjects(team, dt);
            if (now >= _nextPaintAt)
            {
                _nextPaintAt += _config.PaintSpanFrames / 60f;
                PaintBehind();
            }
            if (_vacuum.Absorbed >= _config.Capacity)
            {
                EnterFireMode();
            }
        }

        private void StopSuction()
        {
            _sucking = false;
            if (_vacuum != null)
            {
                _vacuum.Disable();
            }
            if (_vortex != null)
            {
                _vortex.Hide();
            }
            if (_loop != null)
            {
                _loop.Stop();
                _loop = null;
            }
            EnemiesInVortex = 0;
        }

        // 漩渦裡的敵人:不受傷,毒霧般變慢、停止回墨、每秒扣 13.2% 墨;容量每幀 +1.5
        private void AffectEnemies(Team team, float dt)
        {
            int count = 0;
            var all = Inkling.All;
            for (int i = 0; i < all.Count; i++)
            {
                Inkling enemy = all[i];
                if (enemy == null || enemy.Team == team || !enemy.isActiveAndEnabled)
                {
                    continue;
                }
                var life = enemy.GetComponent<InklingLifecycle>();
                if (life != null && life.IsDead)
                {
                    continue;
                }
                Vector3 chest = enemy.transform.position + Vector3.up * 0.9f;
                if (!_vacuum.Contains(chest, EnemyBodyPad))
                {
                    continue;
                }
                count++;
                _vacuum.AddFill(_config.PlayerFillPerFrame * dt * 60f);
                ModifierSpec poison = ModifierSpec.None
                    .WithRunSpeed(_config.PoisonMoveMultiplier)
                    .WithSwimSpeed(_config.PoisonMoveMultiplier)
                    .WithInkRefill(0f)
                    .WithInkDrain(_config.PoisonInkDrainPerSecond)
                    .WithStatus(StatusFlags.Slowed);
                // 每幀刷新 5F(PoisonMistForPlayer EffectFrame 5):離開漩渦馬上恢復
                enemy.Modifiers.Add(_poisonSource, poison, 5f / 60f);
            }
            EnemiesInVortex = count;
        }

        // S3(Inkipedia Ink Vac):漩渦被實心牆擋住(Splash Wall、Rainmaker 屏障這類屏障照樣穿過)——
        // 錐軸碰到的牆面把錐長截短(地板不截:往下吸時錐照樣伸出去);牆在槍口前 0.3 m 內就只剩槍口球
        private static float ClipByWalls(Vector3 apex, Vector3 axis, float length)
        {
            if (length > 0f && Physics.Raycast(apex, axis, out RaycastHit hit, length, CombatMasks.Terrain,
                    QueryTriggerInteraction.Ignore) && Mathf.Abs(hit.normal.y) < 0.7f)
            {
                return Mathf.Max(0.3f, hit.distance);
            }
            return length;
        }

        // 漩渦裡的敵方場上裝置每幀 2 傷(每 0.1 秒結算一次;Inkipedia:Sprinkler、Beakon、Splash Wall、Wave Breaker、
        // Big Bubbler(弱點與屏障面)、Super Chump 誘餌、傘面);Crab Tank、Booyah 護甲、Rainmaker 屏障不吃
        private void AffectObjects(Team team, float dt)
        {
            _objectTick += dt;
            if (_objectTick < ObjectTickSeconds)
            {
                return;
            }
            float amount = _config.ObjectDamagePerFrame * _objectTick * 60f;
            _objectTick = 0f;
            List<IDamageable> active = DamageableRegistry.Active;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                if (!(active[i] is DeployableObject device) || !device.IsAlive || device.Team == team)
                {
                    continue;
                }
                TargetClass cls = device.TargetClass;
                if (cls == TargetClass.CrabTank || cls == TargetClass.BooyahArmor || cls == TargetClass.RainmakerBarrier)
                {
                    continue;
                }
                Vector3 point = device.ClosestPoint(_vacuum.Apex + _axis * Mathf.Min(_vacuum.Length, 3f));
                if (_vacuum.Contains(point))
                {
                    device.ApplyDamage(new DamageInfo(amount, point, team, Owner, DamageKind.Special, AttackClass.Special));
                }
            }
            // Big Bubbler 的屏障面(BarrierSphere 不在可受傷清單):球面上離槍口最近的點進了漩渦 → 屏障受擊(× 0.64 共用耐久);
            // 槍口在罩內 = 單向屏障從裡面碰不到(ClosestPoint 回傳遠處的點)
            List<BarrierSphere> barriers = BarrierSphere.Active;
            for (int i = barriers.Count - 1; i >= 0; i--)
            {
                BarrierSphere barrier = barriers[i];
                if (barrier == null || !barrier.IsAlive || barrier.Team == team)
                {
                    continue;
                }
                Vector3 point = barrier.ClosestPoint(_vacuum.Apex);
                if (_vacuum.Contains(point))
                {
                    barrier.ApplyDamage(new DamageInfo(amount, point, team, Owner, DamageKind.Special, AttackClass.Special));
                }
            }
        }

        // S3 v8.0.0:吸引中每 20F 在身後(右後方)塗一片 r 1.5
        private void PaintBehind()
        {
            Quaternion yaw = Quaternion.Euler(0f, YawOf(_axis), 0f);
            Vector3 origin = transform.position + yaw * new Vector3(0.3f, 0.7f, -0.7f);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 2.5f, CombatMasks.Terrain,
                    QueryTriggerInteraction.Ignore))
            {
                InkSource source = Owner != null ? Owner.InkSource(false) : new InkSource(Team.Alpha, null, false);
                InkPainter.PaintSphere(hit.point, _config.PaintRadius, 0.7f, source);
            }
        }

        private void OnSucked(Vector3 point, float amount)
        {
            if (_config == null || _config.SuckFxPrefab == null)
            {
                return;
            }
            Transform nozzle = _nozzle.Instance != null ? _nozzle.Instance.transform : transform;
            InkVacSuckFx fx = SharedPool.Spawn(_config.SuckFxPrefab);
            Team enemyTeam = TeamUtil.Opponent(Owner != null ? Owner.Team : Team.Alpha);
            fx.Begin(point, nozzle, new Vector3(0f, 0f, 0.95f), enemyTeam, 0.35f + amount / 250f);
            if (Time.time >= _nextAbsorbSoundAt)
            {
                _nextAbsorbSoundAt = Time.time + 0.06f;
                GameAudio.Play(_config.AbsorbSound, point, 0.7f, 0.1f);
            }
        }

        // ── 發射 ──

        private void EnterFireMode()
        {
            if (_phase == Phase.Transition || _phase == Phase.Aim || _phase == Phase.Done)
            {
                return;
            }
            StopSuction();
            _phase = Phase.Transition;
            FireModeEnteredAt = Time.time;
            _transitionEnd = Time.time + _config.TransitionFrames / 60f;
            _armed = false;
            GameAudio.PlayFrom(Owner != null && Owner.IsLocalPlayer, _config.FullSound, transform.position + Vector3.up,
                1f, 0f);
        }

        private void Fire(bool auto)
        {
            float fill = Fill01;
            Vector3 direction = AimForward();
            Vector3 origin = Muzzle(direction);
            Team team = Owner != null ? Owner.Team : Team.Alpha;
            _shotAttackId = CombatLedger.NewAttackId();
            _shotFill = fill;
            _shotTeam = team;
            _shotOwner = Owner;
            InkBulletSpec spec = _config.BuildShotSpec(fill);
            // S3 SpawnSpeedY 0.08 u/F:出膛時另加的向上速度(m/s)
            Vector3 lift = Vector3.up * (InkVacModel.ShotSpeedY * 60f);
            LastShot = InkBullet.Fire(spec, origin, direction, team, Owner, _shotAttackId, lift, _config.ShotPrefab,
                _shotDamageFunc);
            LastShotTime = Time.time;
            LastShotFill = fill;
            LastShotAuto = auto;
            LastMuzzle = origin;
            LastShotDirection = direction;
            ShotsFired++;
            if (_visual != null)
            {
                _visual.NotifyExternalShot(1.6f);
            }
            GameAudio.PlayFrom(Owner != null && Owner.IsLocalPlayer, _config.FireSound, origin, 1f, 0.03f);
            if (InkSplashFxPool.Instance != null)
            {
                InkSplashFxPool.Instance.Spawn(origin, direction, TeamPalette.ColorOf(team));
            }
            _phase = Phase.Done;
            EndSpecial();
        }

        // 反擊彈直擊:角色(與試射假人)220;場上裝置、屏障 0(S3 對物件的直擊倍率 0,只吃之後的爆炸)
        private static float ShotDamage(InkBullet bullet, IDamageable target, float frameDamage)
        {
            return target is DeployableObject || target is BarrierSphere ? 0f : frameDamage;
        }

        // 反擊彈落地(地形、物件、屏障):在那裡放倒數爆炸;被吸走 / 掉出場外 = 沒有爆炸
        private void OnShotImpact(in InkBulletImpact impact)
        {
            if (_shotAttackId == 0 || impact.AttackId != _shotAttackId)
            {
                return;
            }
            if (impact.End == InkBulletEnd.Pierce)
            {
                return;
            }
            _shotAttackId = 0;
            if (impact.End == InkBulletEnd.Absorbed || impact.End == InkBulletEnd.Expired || _config == null
                || _config.BlastPrefab == null)
            {
                return;
            }
            InkVacBlast blast = SharedPool.Spawn(_config.BlastPrefab);
            blast.Arm(impact.Point, _config.BlastRadius(_shotFill), _config, _shotOwner, _shotTeam);
            LastBlast = blast;
            _shotOwner = null;
        }

        // 發射模式的彈道預覽(只給本機玩家看;最後 1 秒閃爍預告自動發射)
        private void UpdateGuide(float now)
        {
            if (_guide == null)
            {
                return;
            }
            bool show = Owner != null && Owner.IsLocalPlayer;
            if (show && _phase == Phase.Aim && _deadline - now < 1f)
            {
                show = Mathf.Repeat(now * 6f, 1f) < 0.55f;
            }
            if (_guide.enabled != show)
            {
                _guide.enabled = show;
            }
            if (!show)
            {
                return;
            }
            Vector3 direction = AimForward();
            Vector3 origin = Muzzle(direction);
            BulletMotion motion = InkVacModel.ShotMotion(Fill01);
            BulletState state = BulletBallistics.Launch(origin, direction, motion, Vector3.up * InkVacModel.ShotSpeedY);
            int count = 0;
            _guidePoints[count++] = origin;
            Vector3 from = origin;
            // 每 3 步取一點、那一段做一次地形射線(撞到就停在撞點)
            for (int step = 1; step <= 240 && count < GuidePoints; step++)
            {
                BulletBallistics.Step(ref state, motion);
                if (step % 3 != 0)
                {
                    continue;
                }
                Vector3 to = state.Position;
                if (Physics.Linecast(from, to, out RaycastHit hit, CombatMasks.Terrain, QueryTriggerInteraction.Ignore))
                {
                    _guidePoints[count++] = hit.point;
                    break;
                }
                _guidePoints[count++] = to;
                from = to;
            }
            _guide.positionCount = count;
            _guide.SetPositions(_guidePoints);
        }

        // ── 外觀 ──

        private void EnsureVortex()
        {
            if (_vortex == null && _config.VortexPrefab != null)
            {
                _vortex = Instantiate(_config.VortexPrefab);
                _vortex.name = "InkVacVortex_" + name;
            }
        }

        private void ShowBackpack(bool show)
        {
            if (!show)
            {
                if (_backpack != null && _backpack.activeSelf)
                {
                    _backpack.SetActive(false);
                }
                return;
            }
            if (_config.BackpackPrefab == null || _visual == null || !_visual.IsReady)
            {
                return;
            }
            if (_backpack == null)
            {
                Transform tankFill = _visual.Rig.InkTankFill;
                Transform parent = tankFill != null ? tankFill.parent : _visual.Rig.BoneTransform(HumanoidRig.Bone.Chest);
                if (parent == null)
                {
                    return;
                }
                _backpack = Instantiate(_config.BackpackPrefab, parent, false);
                _backpack.name = _config.BackpackPrefab.name;
                Transform t = _backpack.transform;
                t.localPosition = tankFill != null ? tankFill.localPosition : new Vector3(0f, 0.05f, -0.25f);
                t.localRotation = Quaternion.identity;
                float scale = parent.lossyScale.y > 1e-4f ? 1f / parent.lossyScale.y : 1f;
                t.localScale = Vector3.one * scale;
                SetLayer(t, parent.gameObject.layer);
                _backpackFill = FindDeep(t, "InkFill");
                TeamTint.ApplyAll(_backpack.transform, TeamPalette.ColorOf(Owner != null ? Owner.Team : Team.Alpha));
                foreach (Renderer r in _backpack.GetComponentsInChildren<Renderer>(true))
                {
                    if (!r.name.StartsWith("Ink", System.StringComparison.Ordinal))
                    {
                        r.SetPropertyBlock(null);
                    }
                }
            }
            _backpack.SetActive(true);
            UpdateBackpackFill();
        }

        private void UpdateBackpackFill()
        {
            if (_backpackFill != null)
            {
                float y = Mathf.Max(0.02f, Fill01);
                if (Mathf.Abs(_backpackFill.localScale.y - y) > 0.005f)
                {
                    _backpackFill.localScale = new Vector3(1f, y, 1f);
                }
            }
        }

        // ── 幾何 ──

        private Vector3 AimForward()
        {
            IAimView view = Owner != null ? Owner.AimView : null;
            Vector3 forward = view != null ? view.AimForward : transform.forward;
            return forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
        }

        // 噴嘴口:腳底 + 面向(水平)× 偏移(測試用公開)
        public Vector3 MuzzlePoint(Vector3 axis)
        {
            return Muzzle(axis);
        }

        private Vector3 Muzzle(Vector3 axis)
        {
            Quaternion yaw = Quaternion.Euler(0f, YawOf(axis), 0f);
            return transform.position + yaw * _config.MuzzleOffset;
        }

        private static float YawOf(Vector3 direction)
        {
            return Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        }

        // ── 走速(InklingModifiers;這個元件本身當來源鍵,吸入量每 5% 更新一次)──

        private void UpdateMoveSpeed()
        {
            if (Owner == null || _config == null)
            {
                return;
            }
            bool fire = _phase == Phase.Transition || _phase == Phase.Aim;
            int step = fire ? 1000 : Mathf.RoundToInt(Fill01 * 20f);
            if (step == _moveStep)
            {
                return;
            }
            _moveStep = step;
            float target = (fire ? _config.FireMoveSpeed : _config.MoveSpeed(step / 20f)) * 60f;
            WeightClass weight = _loadout != null && _loadout.Kit != null ? _loadout.Kit.Weight : WeightClass.Mid;
            float baseSpeed = BaseRunSpeed * WeightClassUtil.RunMultiplier(weight);
            Owner.Modifiers.Add(this, ModifierSpec.None.WithRunSpeed(target / Mathf.Max(0.01f, baseSpeed)), 0f);
        }

        private void RemoveMoveSpeed()
        {
            _moveStep = -1;
            if (Owner != null)
            {
                Owner.Modifiers.Remove(this);
            }
        }

        private static string[] BuildFillHints()
        {
            var hints = new string[101];
            for (int i = 0; i <= 100; i++)
            {
                hints[i] = $"吸入 {i}%";
            }
            return hints;
        }

        private static Transform FindDeep(Transform parent, string childName)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name == childName)
                {
                    return child;
                }
                Transform found = FindDeep(child, childName);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        private static void SetLayer(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++)
            {
                SetLayer(root.GetChild(i), layer);
            }
        }

        // ── bot:看得到敵人且 ≤ 22 m 就開;吸引時對著敵人按住(吸他的攻擊),發射模式瞄腳下、對準就放開 ──

        public override bool BotWantsActivate(IBotControl bot)
        {
            return bot.HasTarget && bot.TargetVisible && bot.TargetDistance <= 22f;
        }

        public override void BotDrive(IBotControl bot)
        {
            Vector3 aim;
            bool haveTarget = bot.HasTarget;
            if (_phase == Phase.Transition || _phase == Phase.Aim)
            {
                if (haveTarget)
                {
                    float distance = bot.TargetDistance;
                    float lead = distance / (InkVacModel.ShotSpeed(Fill01) * 60f);
                    aim = bot.TargetPosition + bot.TargetVelocity * lead + Vector3.up * 0.3f;
                }
                else
                {
                    aim = bot.Position + bot.EnemyBaseDirection * 18f;
                }
                bot.LookAt(aim);
                // 準備中按住;可以射時對準了(或快自動發射了)就放開
                bool release = _phase == Phase.Aim && (bot.AimError < 5f || Time.time > _deadline - 0.4f);
                bot.HoldAttack(!release);
                return;
            }
            aim = haveTarget ? bot.TargetPosition + Vector3.up * 1f : bot.Position + bot.EnemyBaseDirection * 10f;
            bot.LookAt(aim);
            bot.HoldAttack(haveTarget && bot.TargetDistance < 24f);
            if (Time.time >= _botStrafeFlipAt)
            {
                _botStrafeFlipAt = Time.time + Random.Range(0.8f, 1.8f);
                _botStrafeSign = Random.value < 0.5f ? -1f : 1f;
            }
            Vector3 toAim = aim - bot.Position;
            toAim.y = 0f;
            Vector3 right = toAim.sqrMagnitude > 0.01f ? Vector3.Cross(Vector3.up, toAim.normalized) : Vector3.right;
            bot.Move(right * _botStrafeSign, 0.5f);
        }
    }
}

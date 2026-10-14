using System.Collections.Generic;
using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // Ultra Stamp 的執行期行為(research/04 §2.11、JP wiki ウルトラハンコ):
    //   發動 → 雙手扛起大槌(17F 前隙);拿著沒揮的時候可以潛游、照常走。持續型 450F(能量 = 計時器)。
    //   地上按攻擊:舉起 → 砸下(人形 30F、從烏賊 35F;剛敲完 0.5 秒內再點只要 20F),敲到的瞬間:
    //     直擊 100 = 從肩膀掃到落點(前方 3.6 m)的槌頭膠囊(半徑 1.5)碰到的敵人(有視線);
    //     衝擊波 40(≤ 4.0,同一下已被直擊的不再吃)+ 擊退;落點蓋一個印章(大招的墨不充能)。
    //   按住攻擊 → 連敲:每 13F 一下,自動前進 0.155 u/F(腳下塗不到 0.144),搖桿/鏡頭轉向(150°/s [EST]);
    //     連敲中跳起 = 原地連敲(空中不前進)。放開 → 回到拿著。
    //   空中按攻擊:迴旋揮擊 8F 後打半徑 4.5 內的敵人 100;還按著的話落地 40F 後接連敲。
    //   揮擊中:槌頭擋彈(UltraStampGuard 跟著槌頭,舉起時在頭上後方擋不到)、踩敵墨不扣血。
    //   副武器鍵:42F 起手(不能動、本機顯示拋物線)→ 丟出大槌(InkBullet:直擊 220 貫穿角色、撞到爆炸 220 / 60、塗 5)→ 結束。
    //   能量在起手中用完 → 照樣丟完(ContinueAfterGaugeEmpty)。
    public sealed class UltraStampSpecial : SpecialWeapon, ILocomotionOverride, IFacingOverride, ISpecialPose
    {
        private enum Phase
        {
            Startup,
            Idle,
            Swing,
            Rush,
            AirSwing,
            Windup,
            Done,
        }

        private const float IdleAngle = 18f;
        private const float QuickSwingWindow = 0.5f;
        private const int GuidePoints = 40;
        private const float HitVerticalUp = 1.8f;
        private const float HitVerticalDown = 2.5f;

        private static readonly List<IDamageable> Snapshot = new List<IDamageable>(32);

        private UltraStampConfig _config;
        private readonly SpecialHeldModel _hammer = new SpecialHeldModel();
        private Transform _head;
        private LineRenderer _guide;
        private readonly Vector3[] _guidePoints = new Vector3[GuidePoints];
        private UltraStampGuard _guard;
        private CharacterVisual _visual;
        private CharacterController _controller;
        private SquidController _squid;
        private InkBulletSpec _throwSpec;
        private readonly object _slipSource = new object();

        private Phase _phase = Phase.Done;
        private float _activatedAt;
        private float _phaseStart;
        private float _impactAt;
        private float _lastImpactAt = float.NegativeInfinity;
        private float _yaw;
        private bool _attackWasHeld;
        private bool _subWasHeld;
        private float _lastSquidTime = float.NegativeInfinity;
        private bool _airHit;
        private bool _airWantsRush;
        private float _landedAt = -1f;
        private float _angle = IdleAngle;
        private float _spin;

        // bot
        private bool _botThrow;
        private float _botDecidedAt;

        public override SpecialWeaponConfig BaseConfig => _config;
        public UltraStampConfig Config => _config;

        public override bool BlocksMainWeapon => true;
        public override bool BlocksSubWeapon => true;

        // 拿著沒揮的時候可以潛游(S3「ウルトラハンコは使用中でもセンプクが可能」)
        public override bool BlocksSquid => _phase != Phase.Idle;

        public override string HudHint => _phase == Phase.Windup ? "丟出!" : "右鍵 丟出";

        // ── 狀態(測試、bot)──
        public bool Idle => IsActive && _phase == Phase.Idle;
        public bool Swinging => IsActive && (_phase == Phase.Swing || _phase == Phase.Rush || _phase == Phase.AirSwing);
        public bool Rushing => IsActive && _phase == Phase.Rush;
        public bool WindingUp => IsActive && _phase == Phase.Windup;
        public float ActivatedAt => _activatedAt;
        public int Stamps { get; private set; }
        public int AirSwings { get; private set; }
        public float LastSwingStartedAt { get; private set; } = float.NegativeInfinity;
        public float LastImpactTime => _lastImpactAt;
        public Vector3 LastImpactPoint { get; private set; }
        public int LastDirectHits { get; private set; }
        public float LastWindupStartedAt { get; private set; } = float.NegativeInfinity;
        public float LastThrowTime { get; private set; } = float.NegativeInfinity;
        public InkBullet LastThrown { get; private set; }
        public UltraStampGuard Guard => _guard;
        public bool GuardRaised => _guard != null && _guard.Raised;
        public bool ModelShown => _hammer.Shown;
        public float HammerAngle => _angle;

        public override void Configure(SpecialWeaponConfig config)
        {
            _config = config as UltraStampConfig;
            if (_config == null)
            {
                Debug.LogError($"UltraStampSpecial:設定型別不符({config})", this);
                return;
            }
            if (_config.HammerPrefab == null || _config.GuardPrefab == null || _config.ThrowPrefab == null)
            {
                Debug.LogError("UltraStampSpecial:設定缺少大槌/擋彈/丟出 prefab(先跑 Tools/SplatoonC/M7 Setup)", this);
                return;
            }
            string explode = GameAudio.Has("ultra_stamp_explode") ? "ultra_stamp_explode" : "bomb_explode";
            _throwSpec = _config.BuildThrowSpec(explode);
            SharedPool.Prewarm(_config.GuardPrefab, 1);
            SharedPool.Prewarm(_config.ThrowPrefab, 1);
        }

        protected override void Awake()
        {
            base.Awake();
            _visual = GetComponent<CharacterVisual>();
            _controller = GetComponent<CharacterController>();
            _squid = GetComponent<SquidController>();
        }

        public override void OnActivated()
        {
            if (_config == null || _config.HammerPrefab == null)
            {
                EndSpecial();
                return;
            }
            _activatedAt = Time.time;
            _phase = Phase.Startup;
            _phaseStart = _activatedAt;
            _yaw = ViewYaw();
            _angle = IdleAngle;
            _spin = 0f;
            _attackWasHeld = Input != null && Input.AttackHeld;
            _subWasHeld = Input != null && Input.SubWeaponHeld;
            _hammer.Show(Owner, _config.HammerPrefab, Vector3.zero, Vector3.zero);
            _head = _hammer.Instance != null ? FindDeep(_hammer.Instance.transform, "Head") : null;
            _guide = _hammer.Instance != null ? _hammer.Instance.GetComponentInChildren<LineRenderer>(true) : null;
            if (_guide != null)
            {
                _guide.enabled = false;
            }
            _guard = SharedPool.Spawn(_config.GuardPrefab);
            _guard.Attach(Owner);
            if (_visual != null)
            {
                _visual.SpecialPose = this;
            }
            GameAudio.Play(_config.ActivateSound, transform.position + Vector3.up, 1f, 0f);
        }

        // 能量在丟出起手中用完:照樣丟完才結束
        public override bool ContinueAfterGaugeEmpty()
        {
            return _phase == Phase.Windup;
        }

        public override void OnEnded()
        {
            _phase = Phase.Done;
            Cleanup();
        }

        private void OnDisable()
        {
            _phase = Phase.Done;
            Cleanup();
        }

        private void OnDestroy()
        {
            _hammer.Destroy();
        }

        private void Cleanup()
        {
            _hammer.Hide();
            if (_guide != null)
            {
                _guide.enabled = false;
            }
            if (_guard != null)
            {
                UltraStampGuard guard = _guard;
                _guard = null;
                guard.Detach();
            }
            if (_visual != null && ReferenceEquals(_visual.SpecialPose, this))
            {
                _visual.SpecialPose = null;
            }
            if (Owner != null)
            {
                Owner.Modifiers.Remove(this);
                Owner.Modifiers.Remove(_slipSource);
            }
        }

        private void Update()
        {
            if (!IsActive || _config == null || Input == null)
            {
                return;
            }
            float now = Time.time;
            if (_squid != null && _squid.IsSquid)
            {
                _lastSquidTime = now;
            }
            bool attack = Input.AttackHeld;
            bool attackPressed = attack && !_attackWasHeld;
            _attackWasHeld = attack;
            bool sub = Input.SubWeaponHeld;
            bool subPressed = sub && !_subWasHeld;
            _subWasHeld = sub;
            bool grounded = _controller == null || _controller.isGrounded;

            switch (_phase)
            {
                case Phase.Startup:
                    if (now >= _activatedAt + _config.ActivationLag)
                    {
                        SetPhase(Phase.Idle, now);
                    }
                    break;
                case Phase.Idle:
                    if (subPressed)
                    {
                        StartWindup(now);
                        break;
                    }
                    if (attack)
                    {
                        if (!grounded)
                        {
                            StartAirSwing(now);
                        }
                        else
                        {
                            StartSwing(now);
                        }
                    }
                    break;
                case Phase.Swing:
                    if (subPressed)
                    {
                        StartWindup(now);
                        break;
                    }
                    if (now >= _impactAt)
                    {
                        float impact = _impactAt;
                        Stamp(impact, false);
                        if (attack)
                        {
                            // 按著 → 連敲(下一下在 13F 後)
                            SetPhase(Phase.Rush, impact);
                            _impactAt = impact + _config.RushInterval;
                        }
                        else
                        {
                            SetPhase(Phase.Idle, now);
                        }
                    }
                    break;
                case Phase.Rush:
                    if (subPressed)
                    {
                        StartWindup(now);
                        break;
                    }
                    if (!attack)
                    {
                        SetPhase(Phase.Idle, now);
                        break;
                    }
                    SteerRush(Time.deltaTime);
                    int guard = 0;
                    while (now >= _impactAt && guard++ < 3)
                    {
                        Stamp(_impactAt, true);
                        _impactAt += _config.RushInterval;
                    }
                    break;
                case Phase.AirSwing:
                    UpdateAirSwing(now, attack, grounded);
                    break;
                case Phase.Windup:
                    if (now >= _phaseStart + _config.ThrowWindup)
                    {
                        Throw();
                        return;
                    }
                    UpdateGuide();
                    break;
            }
            UpdateAngle(now);
        }

        private void SetPhase(Phase phase, float at)
        {
            _phase = phase;
            _phaseStart = at;
            bool swinging = phase == Phase.Swing || phase == Phase.Rush || phase == Phase.AirSwing;
            if (Owner != null)
            {
                if (swinging)
                {
                    // 揮擊中踩敵墨不受影響(JP wiki「攻撃動作中は相手インクを踏んでもそのダメージや影響を受けない」)
                    Owner.Modifiers.Add(_slipSource, ModifierSpec.None.WithoutSlipDamage(), 0f);
                }
                else
                {
                    Owner.Modifiers.Remove(_slipSource);
                }
                // 單敲的舉起/砸下中走得很慢
                if (phase == Phase.Swing)
                {
                    Owner.Modifiers.Add(this, ModifierSpec.None.WithRunSpeed(_config.StampMoveMultiplier), 0f);
                }
                else
                {
                    Owner.Modifiers.Remove(this);
                }
            }
        }

        // ── 敲地 ──

        private void StartSwing(float now)
        {
            bool fromSquid = now - _lastSquidTime <= Time.deltaTime * 2f + 1e-4f;
            float lag = fromSquid ? _config.StampLagSquid : _config.StampLagHuman;
            // 剛敲完再點:兩次敲之間 20F(振り間隔)
            if (!fromSquid && now - _lastImpactAt < QuickSwingWindow)
            {
                lag = Mathf.Max(_lastImpactAt + _config.StampInterval - now, 1f / 60f);
            }
            _yaw = ViewYaw();
            SetPhase(Phase.Swing, now);
            _impactAt = now + lag;
            LastSwingStartedAt = now;
            GameAudio.Play(_config.SwingSound, transform.position + Vector3.up * 2f, 0.8f, 0.05f);
        }

        // 敲到地面(at = 敲下的時刻):直擊 100、衝擊波 40、擊退、印章
        private void Stamp(float at, bool rush)
        {
            _lastImpactAt = at;
            Stamps++;
            Team team = Owner != null ? Owner.Team : Team.Alpha;
            Quaternion yaw = Quaternion.Euler(0f, _yaw, 0f);
            Vector3 feet = transform.position;
            Vector3 impact = feet + yaw * new Vector3(0f, 0f, _config.ImpactDistance);
            if (Physics.Raycast(impact + Vector3.up * 2f, Vector3.down, out RaycastHit ground, 4.5f, CombatMasks.Terrain,
                    QueryTriggerInteraction.Ignore))
            {
                impact = ground.point;
            }
            LastImpactPoint = impact;
            int attackId = CombatLedger.NewAttackId();
            Vector3 shoulder = feet + Vector3.up * 1.3f + yaw * new Vector3(0f, 0f, 0.6f);
            Vector3 headPoint = impact + Vector3.up * 0.4f;
            LastDirectHits = HitCapsule(shoulder, headPoint, _config.HeadRadius, team, attackId, feet + Vector3.up * 1.2f);
            // 衝擊波(爆心抬高 0.2;同一下已被直擊的不再吃)
            Explosion.Detonate(_config.BuildSwingBlast(rush), impact + Vector3.up * 0.2f, team, Owner, attackId);
            PaintStamp(impact, yaw, team);
            if (InkSplashFxPool.Instance != null)
            {
                Color color = TeamPalette.ColorOf(team);
                InkSplashFxPool.Instance.Spawn(impact + Vector3.up * 0.1f, Vector3.up, color);
                InkSplashFxPool.Instance.Spawn(impact + yaw * new Vector3(0.8f, 0.1f, 0.3f), Vector3.up, color);
                InkSplashFxPool.Instance.Spawn(impact + yaw * new Vector3(-0.8f, 0.1f, 0.3f), Vector3.up, color);
            }
            GameAudio.Play(_config.StampSound, impact + Vector3.up * 0.5f, rush ? 0.8f : 1f, 0.06f);
            if (_visual != null)
            {
                _visual.NotifyExternalShot(0.8f);
            }
        }

        // 槌頭掃過的膠囊碰到的敵人(有視線、同一下只算一次)各吃 100;回傳打到幾個
        private int HitCapsule(Vector3 a, Vector3 b, float radius, Team team, int attackId, Vector3 eye)
        {
            int hits = 0;
            Snapshot.Clear();
            List<IDamageable> active = DamageableRegistry.Active;
            for (int i = 0; i < active.Count; i++)
            {
                Snapshot.Add(active[i]);
            }
            for (int i = 0; i < Snapshot.Count; i++)
            {
                IDamageable target = Snapshot[i];
                if (target == null || !target.IsAlive || !DamageRules.CanDamage(team, target))
                {
                    continue;
                }
                Vector3 mid = (a + b) * 0.5f;
                Vector3 point = target.ClosestPoint(mid);
                Vector3 onSegment = CapsuleMath.ClosestPoint(a, b, 0f, point);
                if ((point - onSegment).sqrMagnitude > radius * radius)
                {
                    // 目標最近點可能不是離線段最近的:再以線段上的點重新量一次
                    point = target.ClosestPoint(onSegment);
                    onSegment = CapsuleMath.ClosestPoint(a, b, 0f, point);
                    if ((point - onSegment).sqrMagnitude > radius * radius)
                    {
                        continue;
                    }
                }
                // 薄牆後面打不到(S3 v1.2.0)
                if (Physics.Linecast(eye, point, CombatMasks.Terrain, QueryTriggerInteraction.Ignore))
                {
                    continue;
                }
                float damage = CombatLedger.Register(attackId, target, _config.SwingDamage);
                if (damage <= 0f)
                {
                    continue;
                }
                target.ApplyDamage(new DamageInfo(damage, point, team, Owner, DamageKind.Special, AttackClass.UltraStampSwing));
                hits++;
            }
            Snapshot.Clear();
            return hits;
        }

        // 印章(S3 蓋出 SquidForce 標誌 ≈ 4.5p):身體大圓 + 前方頭 + 後方兩條觸手 [EST 形狀近似]
        private void PaintStamp(Vector3 impact, Quaternion yaw, Team team)
        {
            InkSource source = Owner != null ? Owner.InkSource(false) : new InkSource(team, null, false);
            float r = _config.StampPaintRadius;
            InkPainter.PaintSphere(impact, r, 0.8f, source);
            InkPainter.PaintSphere(impact + yaw * new Vector3(0f, 0f, r * 0.9f), r * 0.7f, 0.8f, source);
            InkPainter.PaintSphere(impact + yaw * new Vector3(r * 0.6f, 0f, -r * 0.95f), r * 0.45f, 0.8f, source);
            InkPainter.PaintSphere(impact + yaw * new Vector3(-r * 0.6f, 0f, -r * 0.95f), r * 0.45f, 0.8f, source);
        }

        // ── 連敲轉向 ──

        private void SteerRush(float dt)
        {
            float target = ViewYaw();
            Vector2 input = Input.MoveInput;
            if (input.sqrMagnitude > 0.04f)
            {
                target += Mathf.Atan2(input.x, input.y) * Mathf.Rad2Deg;
            }
            _yaw = Mathf.MoveTowardsAngle(_yaw, target, _config.RushTurnDegPerSecond * dt);
        }

        // ── 空中迴旋 ──

        private void StartAirSwing(float now)
        {
            _yaw = ViewYaw();
            SetPhase(Phase.AirSwing, now);
            _airHit = false;
            _airWantsRush = false;
            _landedAt = -1f;
            AirSwings++;
            LastSwingStartedAt = now;
            GameAudio.Play(_config.SwingSound, transform.position + Vector3.up, 0.9f, 0.05f);
        }

        private void UpdateAirSwing(float now, bool attack, bool grounded)
        {
            if (!_airHit && now >= _phaseStart + _config.AirSwingLag)
            {
                _airHit = true;
                AirSwingHit();
            }
            if (!_airHit)
            {
                return;
            }
            _airWantsRush = attack;
            if (!grounded)
            {
                return;
            }
            if (_landedAt < 0f)
            {
                _landedAt = now;
            }
            if (!_airWantsRush)
            {
                SetPhase(Phase.Idle, now);
                return;
            }
            // 還按著:落地後 40F 接連敲
            if (now >= _phaseStart + _config.AirToRush)
            {
                SetPhase(Phase.Rush, now);
                _impactAt = now + _config.RushInterval;
            }
        }

        // 迴旋:水平半徑 4.5、上 1.8 / 下 2.5 內的敵人 100(有視線);腳下塗一小段
        private void AirSwingHit()
        {
            Team team = Owner != null ? Owner.Team : Team.Alpha;
            Vector3 center = transform.position + Vector3.up * 1f;
            int attackId = CombatLedger.NewAttackId();
            int hits = 0;
            Snapshot.Clear();
            List<IDamageable> active = DamageableRegistry.Active;
            for (int i = 0; i < active.Count; i++)
            {
                Snapshot.Add(active[i]);
            }
            for (int i = 0; i < Snapshot.Count; i++)
            {
                IDamageable target = Snapshot[i];
                if (target == null || !target.IsAlive || !DamageRules.CanDamage(team, target))
                {
                    continue;
                }
                Vector3 point = target.ClosestPoint(center);
                Vector3 d = point - center;
                if (d.y > HitVerticalUp || d.y < -HitVerticalDown
                    || new Vector2(d.x, d.z).sqrMagnitude > _config.AirSwingRadius * _config.AirSwingRadius)
                {
                    continue;
                }
                if (Physics.Linecast(center, point, CombatMasks.Terrain, QueryTriggerInteraction.Ignore))
                {
                    continue;
                }
                float damage = CombatLedger.Register(attackId, target, _config.SwingDamage);
                if (damage > 0f)
                {
                    target.ApplyDamage(new DamageInfo(damage, point, team, Owner, DamageKind.Special,
                        AttackClass.UltraStampSwing));
                    hits++;
                }
            }
            Snapshot.Clear();
            LastDirectHits = hits;
            _lastImpactAt = Time.time;
            if (Physics.Raycast(center, Vector3.down, out RaycastHit ground, 6f, CombatMasks.Terrain,
                    QueryTriggerInteraction.Ignore))
            {
                InkSource source = Owner != null ? Owner.InkSource(false) : new InkSource(team, null, false);
                Quaternion yaw = Quaternion.Euler(0f, _yaw, 0f);
                InkPainter.PaintSphere(ground.point + yaw * new Vector3(0f, 0f, 1f), 0.9f, 0.8f, source);
                InkPainter.PaintSphere(ground.point + yaw * new Vector3(0f, 0f, 2.2f), 0.9f, 0.8f, source);
            }
            GameAudio.Play(_config.StampSound, transform.position + Vector3.up, 0.8f, 0.08f);
        }

        // ── 丟出 ──

        private void StartWindup(float now)
        {
            _yaw = ViewYaw();
            SetPhase(Phase.Windup, now);
            LastWindupStartedAt = now;
            GameAudio.Play(_config.WindupSound, transform.position + Vector3.up * 2f, 1f, 0f);
        }

        private void Throw()
        {
            Vector3 direction = AimForward();
            Vector3 origin = ThrowOrigin(direction);
            Team team = Owner != null ? Owner.Team : Team.Alpha;
            Vector3 lift = Vector3.up * (UltraStampModel.ThrowSpeedY * 60f);
            LastThrown = InkBullet.Fire(_throwSpec, origin, direction, team, Owner, CombatLedger.NewAttackId(), lift,
                _config.ThrowPrefab);
            LastThrowTime = Time.time;
            GameAudio.PlayFrom(Owner != null && Owner.IsLocalPlayer, _config.ThrowSound, origin, 1f, 0.03f);
            _phase = Phase.Done;
            EndSpecial();
        }

        public Vector3 ThrowOrigin(Vector3 direction)
        {
            Quaternion yaw = Quaternion.Euler(0f, Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg, 0f);
            return transform.position + yaw * _config.ThrowOrigin;
        }

        // 起手中顯示丟出的拋物線(只給本機玩家看):與丟出的槌同一套彈道步進
        private void UpdateGuide()
        {
            if (_guide == null)
            {
                return;
            }
            bool show = Owner != null && Owner.IsLocalPlayer;
            if (_guide.enabled != show)
            {
                _guide.enabled = show;
            }
            if (!show)
            {
                return;
            }
            Vector3 direction = AimForward();
            Vector3 origin = ThrowOrigin(direction);
            BulletMotion motion = UltraStampModel.ThrowMotion;
            BulletState state = BulletBallistics.Launch(origin, direction, motion, Vector3.up * UltraStampModel.ThrowSpeedY);
            int count = 0;
            _guidePoints[count++] = origin;
            Vector3 from = origin;
            for (int step = 1; step <= 180 && count < GuidePoints; step++)
            {
                BulletBallistics.Step(ref state, motion);
                if (step % 2 != 0)
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

        // ── 槌的角度與擋彈 ──

        // 槌角度(度):0 = 舉在頭頂正上方、90 = 水平往前、100 = 砸在身前地面;負 = 往後舉
        private void UpdateAngle(float now)
        {
            float target;
            float spin = 0f;
            switch (_phase)
            {
                case Phase.Swing:
                    target = UltraStampModel.SwingAngle((now - _phaseStart) / Mathf.Max(0.01f, _impactAt - _phaseStart));
                    break;
                case Phase.Rush:
                {
                    // 13F 一個循環:前 55% 舉起、後 45% 砸下,在 _impactAt 砸到
                    float t = 1f - Mathf.Clamp01((_impactAt - now) / Mathf.Max(0.01f, _config.RushInterval));
                    target = t < 0.55f ? Mathf.Lerp(UltraStampModel.ImpactAngle, -10f, t / 0.55f)
                        : Mathf.Lerp(-10f, UltraStampModel.ImpactAngle, (t - 0.55f) / 0.45f);
                    break;
                }
                case Phase.AirSwing:
                {
                    float t = Mathf.Clamp01((now - _phaseStart) / Mathf.Max(0.01f, _config.AirSwingLag * 2.2f));
                    target = 90f;
                    spin = Mathf.Lerp(-150f, 150f, t);
                    break;
                }
                case Phase.Windup:
                    target = Mathf.Lerp(IdleAngle, -75f, Mathf.Clamp01((now - _phaseStart) / Mathf.Max(0.01f, _config.ThrowWindup * 0.6f)));
                    break;
                default:
                    // 敲完慢慢收回拿著的角度
                    target = now - _lastImpactAt < 0.18f ? UltraStampModel.ImpactAngle : IdleAngle;
                    break;
            }
            _angle = target;
            _spin = spin;
            if (_guard != null)
            {
                bool raised = (_phase == Phase.Swing || _phase == Phase.Rush || _phase == Phase.AirSwing) && _angle >= 45f;
                Vector3 position = _head != null ? _head.position : transform.position + Vector3.up;
                Quaternion rotation = _head != null ? _head.rotation : transform.rotation;
                _guard.Follow(position, rotation, raised);
            }
        }

        // ── 移動接管 ──

        public bool InterceptJump(Vector2 moveInput, float cameraYawDeg, bool grounded)
        {
            // 起手中不能跳;連敲中可以跳(原地連敲)
            return _phase == Phase.Windup || _phase == Phase.Startup;
        }

        public bool TryGetControlledMotion(float time, float deltaTime, bool grounded, out Vector3 horizontalVelocity,
            out float forcedVerticalSpeed)
        {
            horizontalVelocity = Vector3.zero;
            forcedVerticalSpeed = float.NaN;
            if (!IsActive)
            {
                return false;
            }
            switch (_phase)
            {
                case Phase.Rush:
                    if (!grounded)
                    {
                        // 連敲中跳起 = 原地連敲(空中不前進)
                        return true;
                    }
                    horizontalVelocity = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward * (RushSpeed() * 60f);
                    return true;
                case Phase.Windup:
                case Phase.Startup:
                    return true;
                default:
                    return false;
            }
        }

        // 腳下塗不到(沒有 PaintableSurface)時 0.144 u/F
        private float RushSpeed()
        {
            if (Physics.Raycast(transform.position + Vector3.up * 0.3f, Vector3.down, out RaycastHit hit, 1.2f,
                    CombatMasks.Terrain, QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponent<PaintableSurface>() == null)
            {
                return _config.RushSpeedNoPaint;
            }
            return _config.RushSpeed;
        }

        public void OnLocomotionReset()
        {
        }

        public bool TryGetFacingYaw(out float yawDeg)
        {
            yawDeg = _yaw;
            return IsActive && (_phase == Phase.Swing || _phase == Phase.Rush || _phase == Phase.Windup);
        }

        // ── 雙手握槌的姿勢(ISpecialPose)──

        public bool TryGetSpecialPose(ref SpecialPoseFrame pose)
        {
            if (!IsActive)
            {
                return false;
            }
            float rad = _angle * Mathf.Deg2Rad;
            // 握把方向(胸口座標):0° 朝上、90° 朝前、100° 往前下
            Vector3 along = new Vector3(0f, Mathf.Cos(rad), Mathf.Sin(rad));
            Vector3 thumb = new Vector3(0f, -Mathf.Sin(rad), Mathf.Cos(rad));
            Quaternion socket = Quaternion.LookRotation(along, thumb);
            Vector3 grip = new Vector3(0.04f, 0.3f * Mathf.Cos(rad) - 0.18f, 0.16f + 0.26f * Mathf.Sin(rad));
            pose.RightGrip = grip;
            pose.RightSocket = socket;
            pose.LeftGrip = grip - along * 0.24f + new Vector3(-0.05f, 0f, 0f);
            pose.LeftSocket = socket;
            pose.LeftWeight = 1f;
            pose.RightElbow = new Vector3(0.9f, -0.4f, -0.3f);
            pose.LeftElbow = new Vector3(-0.9f, -0.4f, -0.3f);
            pose.TorsoPitch = Mathf.Lerp(-10f, 26f, Mathf.InverseLerp(-25f, UltraStampModel.ImpactAngle, _angle));
            pose.TorsoYaw = _spin;
            pose.Crouch = Mathf.Lerp(0f, 0.08f, Mathf.InverseLerp(60f, UltraStampModel.ImpactAngle, _angle));
            pose.HalfLife = _phase == Phase.Swing || _phase == Phase.Rush ? 0.015f : 0.05f;
            return true;
        }

        // ── 共用 ──

        private float ViewYaw()
        {
            IAimView view = Owner != null ? Owner.AimView : null;
            return view != null ? view.Yaw : transform.eulerAngles.y;
        }

        private Vector3 AimForward()
        {
            IAimView view = Owner != null ? Owner.AimView : null;
            Vector3 forward = view != null ? view.AimForward : transform.forward;
            return forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
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

        // ── bot:看得到敵人且 ≤ 16 m 開;近(< 10 m)就連敲衝過去,遠(10~24 m)或時間快到就丟 ──

        public override bool BotWantsActivate(IBotControl bot)
        {
            return bot.HasTarget && bot.TargetVisible && bot.TargetDistance <= 16f;
        }

        public override void BotDrive(IBotControl bot)
        {
            float now = Time.time;
            Vector3 aim = bot.HasTarget ? bot.TargetPosition + Vector3.up * 0.8f : bot.Position + bot.EnemyBaseDirection * 12f;
            if (_phase == Phase.Windup)
            {
                bot.LookAt(bot.HasTarget ? bot.TargetPosition + Vector3.up * 1.2f : aim);
                return;
            }
            float remaining = Gauge != null ? Gauge.Points / 100f * _config.ActiveDuration : 0f;
            if (now - _botDecidedAt > 0.8f)
            {
                _botDecidedAt = now;
                bool far = bot.HasTarget && bot.TargetVisible && bot.TargetDistance > 10f && bot.TargetDistance < 24f;
                _botThrow = (far && Random.value < 0.5f) || remaining < 1.5f;
            }
            bot.LookAt(aim);
            if (_botThrow && _phase == Phase.Idle)
            {
                bot.HoldAttack(false);
                bot.HoldSub(true);
                return;
            }
            bot.HoldSub(false);
            bool engage = bot.HasTarget && bot.TargetDistance < 14f;
            bot.HoldAttack(engage);
            Vector3 toward = aim - bot.Position;
            toward.y = 0f;
            bot.Move(toward.sqrMagnitude > 0.01f ? toward.normalized : bot.EnemyBaseDirection, engage ? 1f : 0.7f);
        }
    }
}

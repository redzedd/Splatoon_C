using System.Collections.Generic;
using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.CameraRig;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // Crab Tank 的執行期行為(research/04 §2.12、JP wiki カニタンク):
    //   發動 → 召喚戰車(CrabTankBody 跟著駕駛;45F 內長出來、之後裝甲 500 生效);持續型 480F(能量 = 計時器)。
    //   步行:ILocomotionOverride 接管水平速度——車身座標橫移 0.075 u/F、前後 0.015 u/F,車身以 70°/s [EST] 轉向準心
    //     (IFacingOverride);車體半徑 1.15 擋牆(駕駛的碰撞膠囊只有 0.4)。S3 步行時「轉向與瞄準都很慢」:本機玩家的鏡頭
    //     水平轉速也壓到同一個值(ThirdPersonCameraRig.YawSpeedLimit),準心與砲口永遠同向;滾球時鏡頭不限速。
    //   ZR 連射:間隔 12F → 4F(按住 90F 到最快、放開/砲擊/滾球就重來),左右砲管輪流;兩門都朝準心點發射(彈道補償)。
    //   R(副武器鍵)砲擊:每 75F 一發(按下觸發),砲擊後 32F 不能連射。
    //   ZL(烏賊鍵)滾球:全方向 0.1 u/F(鏡頭座標)、塗得到的牆可以直直往上爬、腳下一直塗、接觸敵人 40(同一人 0.33 s 一次)、
    //     駕駛藏進球裡(外觀)且全身被球擋住、回血同潛墨、不扣敵墨血;放開 → 車身轉向鏡頭、20F 不能射。B = 小跳(一般跳躍)。
    //   時間到(ContinueAfterGaugeEmpty)或被打爆(駕駛彈出)→ 戰車消失、25F 不能攻擊/移動,之後才結束行為。
    //   駕駛倒下:戰車立刻消失(持續型:能量停在死亡瞬間、再減半)。
    public sealed class CrabTankSpecial : SpecialWeapon, ILocomotionOverride, IFacingOverride, ISpecialPose
    {
        private enum Phase
        {
            Summon,
            Walk,
            Ball,
            EndLag,
        }

        private const int MaxContacts = 8;
        private const float TurretZeroDistance = 12f;
        // 準心點的取樣距離(視點到砲口之外再加):S3 砲台有效射程約 28 u(5.7 格線)
        private const float AimReach = 28f;
        private static readonly RaycastHit[] AimHits = new RaycastHit[16];
        private const float ClimbProbe = 1.35f;
        // 駕駛外觀抬高(公尺):S3 駕駛坐在車上、胸口以上露出;受擊膠囊(0~2 m)本來就在,
        // 抬高後看得到的上身(車殼頂 ~1.37 m 以上)正好對上「正面裝甲 1.44 m 以上打得到駕駛」
        private const float RiderLift = 0.5f;

        private CrabTankConfig _config;
        private readonly SpecialHeldModel _grip = new SpecialHeldModel();
        private CrabTankBody _body;
        private CharacterVisual _visual;
        private CharacterController _controller;
        private InkBulletSpec _turretSpec;
        private InkBulletSpec _cannonSpec;
        private AudioLoop _rollLoop;
        private readonly object _ballSource = new object();

        private Phase _phase;
        private float _activatedAt;
        private float _endLagUntil;
        private float _yaw;
        private bool _wasFiring;
        private float _fireHeldSince;
        private float _nextShotAt;
        private int _nextBarrel;
        private float _noShotUntil;
        private float _nextCannonAt;
        private Vector3 _ballVelocity;
        private float _nextBallPaintAt;
        private bool _climbing;
        private readonly Inkling[] _contactTargets = new Inkling[MaxContacts];
        private readonly float[] _contactReadyAt = new float[MaxContacts];
        private int _contactCount;

        // bot
        private float _botStrafeSign = 1f;
        private float _botStrafeFlipAt;
        private float _botNextCannonAt;
        private float _botCannonUntil;
        private float _botBallUntil;

        public override SpecialWeaponConfig BaseConfig => _config;
        public CrabTankConfig Config => _config;

        public override bool BlocksMainWeapon => true;
        public override bool BlocksSubWeapon => true;
        public override bool BlocksSquid => true;

        // ── 狀態(HUD、測試、bot)──
        public CrabTankBody Body => _body;
        public bool Armored => _body != null && _body.Armored && _body.IsAlive;
        public bool BallMode => IsActive && _phase == Phase.Ball;
        public bool InEndLag => IsActive && _phase == Phase.EndLag;
        public float TankYaw => _yaw;
        public float ActivatedAt => _activatedAt;
        public int TurretShots { get; private set; }
        public int CannonShots { get; private set; }
        public float LastTurretShotTime { get; private set; } = float.NegativeInfinity;
        public float LastTurretInterval { get; private set; }
        public float LastCannonTime { get; private set; } = float.NegativeInfinity;
        public InkBullet LastTurretBullet { get; private set; }
        public InkBullet LastCannon { get; private set; }
        public Vector3 LastShotDirection { get; private set; }
        // 最後一發的出膛點與瞄準的準心點(測試用)
        public Vector3 LastShotOrigin { get; private set; }
        public Vector3 LastAimTarget { get; private set; }
        // 砲台散布倍率:1 = S3 散布 3°。AutoTest 量準心對齊時設 0 以免隨機偏角蓋過瞄準誤差;玩家與 bot 從不改
        public float SpreadMultiplier { get; set; } = 1f;
        public int BallContacts { get; private set; }
        public bool Climbing => _climbing;
        public bool Ejected { get; private set; }
        public float EndLagStartedAt { get; private set; } = float.NegativeInfinity;
        public int TankBreaks { get; private set; }

        public override string HudHint
        {
            get
            {
                if (_phase == Phase.Ball)
                {
                    return "滾球中";
                }
                if (_phase == Phase.Walk && Time.time < _nextCannonAt)
                {
                    return "砲彈裝填中";
                }
                return "右鍵 砲擊";
            }
        }

        public override void Configure(SpecialWeaponConfig config)
        {
            _config = config as CrabTankConfig;
            if (_config == null)
            {
                Debug.LogError($"CrabTankSpecial:設定型別不符({config})", this);
                return;
            }
            if (_config.BodyPrefab == null || _config.BulletPrefab == null || _config.CannonPrefab == null)
            {
                Debug.LogError("CrabTankSpecial:設定缺少車體/子彈/砲彈 prefab(先跑 Tools/SplatoonC/M7 Setup)", this);
                return;
            }
            _turretSpec = _config.BuildTurretSpec();
            string explode = GameAudio.Has("crab_tank_cannon_explode") ? "crab_tank_cannon_explode" : "bomb_explode";
            _cannonSpec = _config.BuildCannonSpec(explode);
            SharedPool.Prewarm(_config.BodyPrefab, 1);
            SharedPool.Prewarm(_config.BulletPrefab, 12);
            SharedPool.Prewarm(_config.CannonPrefab, 2);
        }

        protected override void Awake()
        {
            base.Awake();
            _visual = GetComponent<CharacterVisual>();
            _controller = GetComponent<CharacterController>();
        }

        public override void OnActivated()
        {
            if (_config == null || _config.BodyPrefab == null)
            {
                EndSpecial();
                return;
            }
            _activatedAt = Time.time;
            _phase = Phase.Summon;
            _yaw = ViewYaw();
            _wasFiring = false;
            _noShotUntil = 0f;
            _nextCannonAt = 0f;
            _ballVelocity = Vector3.zero;
            _contactCount = 0;
            _climbing = false;
            Ejected = false;
            _body = SharedPool.Spawn(_config.BodyPrefab);
            _body.Summon(Owner, _config, this, _yaw);
            LimitViewTurn(true);
            _grip.Show(Owner, _config.GripPrefab, Vector3.zero, Vector3.zero);
            if (_visual != null)
            {
                _visual.SpecialPose = this;
            }
            // 戰車不陷敵墨、不扣敵墨血(S3:カニタンクは敵インクにはまらない)
            if (Owner != null)
            {
                Owner.Modifiers.Add(this, ModifierSpec.None.WithoutSlipDamage(), 0f);
            }
            GameAudio.Play(_config.ActivateSound, transform.position + Vector3.up, 1f, 0f);
        }

        // 時間到:戰車消失、25F 不能攻擊,之後結束
        public override bool ContinueAfterGaugeEmpty()
        {
            if (_phase != Phase.EndLag)
            {
                BeginEndLag(false);
            }
            return true;
        }

        // 車體耐久歸零(CrabTankBody.OnBroken):駕駛彈出(不算被擊倒)、能量歸 0、25F 硬直
        public void OnTankBroken()
        {
            if (!IsActive || _phase == Phase.EndLag)
            {
                return;
            }
            TankBreaks++;
            Ejected = true;
            // 能量在硬直結束的 EndSpecial 清為 0;駕駛往上彈出
            var locomotion = GetComponent<PlayerLocomotion>();
            if (locomotion != null)
            {
                locomotion.AddImpulse(Vector3.up * 7f);
            }
            BeginEndLag(true);
        }

        private void BeginEndLag(bool broken)
        {
            _phase = Phase.EndLag;
            LimitViewTurn(false);
            EndLagStartedAt = Time.time;
            _endLagUntil = Time.time + _config.EndLagSeconds;
            StopRollLoop();
            if (Owner != null)
            {
                Owner.Modifiers.Remove(_ballSource);
            }
            if (!broken)
            {
                GameAudio.Play(_config.EndSound, transform.position + Vector3.up, 0.9f, 0f);
                if (InkSplashFxPool.Instance != null)
                {
                    InkSplashFxPool.Instance.Spawn(transform.position + Vector3.up, Vector3.up,
                        TeamPalette.ColorOf(Owner != null ? Owner.Team : Team.Alpha));
                }
            }
            DismissBody();
        }

        public override void OnEnded()
        {
            LimitViewTurn(false);
            DismissBody();
            StopRollLoop();
            _grip.Hide();
            if (_visual != null && ReferenceEquals(_visual.SpecialPose, this))
            {
                _visual.SpecialPose = null;
            }
            if (Owner != null)
            {
                Owner.Modifiers.Remove(this);
                Owner.Modifiers.Remove(_ballSource);
            }
            _climbing = false;
        }

        private void OnDisable()
        {
            LimitViewTurn(false);
            DismissBody();
            StopRollLoop();
            _grip.Hide();
            if (_visual != null && ReferenceEquals(_visual.SpecialPose, this))
            {
                _visual.SpecialPose = null;
            }
        }

        private void OnDestroy()
        {
            _grip.Destroy();
        }

        private void DismissBody()
        {
            if (_body != null)
            {
                CrabTankBody body = _body;
                _body = null;
                body.Dismiss();
            }
        }

        private void Update()
        {
            if (!IsActive || _config == null || Input == null)
            {
                return;
            }
            float now = Time.time;
            switch (_phase)
            {
                case Phase.Summon:
                    TurnTowardView(Time.deltaTime);
                    if (now >= _activatedAt + _config.ArmorDelaySeconds)
                    {
                        _phase = Phase.Walk;
                        if (_body != null)
                        {
                            _body.Arm();
                        }
                        _nextShotAt = now;
                    }
                    break;
                case Phase.Walk:
                    TurnTowardView(Time.deltaTime);
                    if (Input.SquidHeld)
                    {
                        EnterBall();
                        break;
                    }
                    UpdateTurret(now);
                    UpdateCannon(now);
                    break;
                case Phase.Ball:
                    if (!Input.SquidHeld)
                    {
                        ExitBall(now);
                        break;
                    }
                    UpdateBall(now);
                    break;
                case Phase.EndLag:
                    if (now >= _endLagUntil)
                    {
                        EndSpecial();
                    }
                    return;
            }
            if (_body != null)
            {
                float remaining = Gauge != null ? Gauge.Points / 100f * _config.ActiveDuration : 99f;
                _body.Follow(transform.position, _yaw, remaining < _config.WarnSeconds);
            }
        }

        private void TurnTowardView(float dt)
        {
            _yaw = Mathf.MoveTowardsAngle(_yaw, ViewYaw(), _config.TurnDegPerSecond * dt);
        }

        // ── ZR 連射 ──

        private void UpdateTurret(float now)
        {
            bool held = Input.AttackHeld && now >= _noShotUntil;
            if (!held)
            {
                _wasFiring = false;
                return;
            }
            if (!_wasFiring)
            {
                _wasFiring = true;
                _fireHeldSince = now;
                _nextShotAt = Mathf.Max(_nextShotAt, now);
            }
            int guard = 0;
            while (now >= _nextShotAt && guard++ < 4)
            {
                float interval = _config.TurretRepeatFrames((_nextShotAt - _fireHeldSince) * 60f);
                FireTurret(_nextShotAt, interval);
                _nextShotAt += interval / 60f;
            }
        }

        private void FireTurret(float firedAt, float interval)
        {
            int barrel = _nextBarrel;
            _nextBarrel = 1 - _nextBarrel;
            Quaternion yaw = Quaternion.Euler(0f, _yaw, 0f);
            Vector3 offset = _config.TurretMuzzle;
            offset.x *= barrel == 0 ? -1f : 1f;
            Vector3 origin = transform.position + yaw * offset;
            // 左右兩門都朝準心點發射(補償減速段下墜):子彈落在畫面中心的準心上,不是和視線平行地偏在砲口那一側
            Vector3 center = transform.position + yaw * new Vector3(0f, offset.y, offset.z);
            LastAimTarget = ReticleTarget(center, _turretSpec.RadiusForField);
            LastShotOrigin = origin;
            Vector3 direction = BulletAim.CompensatedDirection(_turretSpec.Motion, origin, LastAimTarget);
            // S3 拡散 3.0°:圓錐內均勻亂數
            Vector2 jitter = Random.insideUnitCircle * (_config.TurretSpread * SpreadMultiplier);
            direction = Quaternion.AngleAxis(jitter.x, Vector3.up) * Quaternion.AngleAxis(jitter.y,
                Vector3.Cross(Vector3.up, direction).normalized) * direction;
            Team team = Owner != null ? Owner.Team : Team.Alpha;
            LastTurretBullet = InkBullet.Fire(_turretSpec, origin, direction, team, Owner, 0, Vector3.zero,
                _config.BulletPrefab);
            TurretShots++;
            LastTurretInterval = interval;
            LastTurretShotTime = firedAt;
            LastShotDirection = direction;
            if (_visual != null)
            {
                _visual.NotifyExternalShot(0.35f);
            }
            GameAudio.PlayFrom(Owner != null && Owner.IsLocalPlayer, _config.TurretSound, origin, 0.75f, 0.05f);
        }

        // ── R 砲 ──

        // 按住副武器鍵:冷卻(75F)與「不能射」一結束就發(按鍵提早按會等到能射的那一幀)
        private void UpdateCannon(float now)
        {
            if (!Input.SubWeaponHeld || now < _nextCannonAt || now < _noShotUntil)
            {
                return;
            }
            _nextCannonAt = now + _config.CannonInterval;
            // 砲擊打斷連射:32F 不能連射、連射速度重來
            _noShotUntil = now + _config.CannonNoShot;
            _wasFiring = false;
            Quaternion yaw = Quaternion.Euler(0f, _yaw, 0f);
            Vector3 origin = transform.position + yaw * _config.CannonMuzzle;
            // 砲彈也朝準心點:曲射的彈道用實際模擬反解仰角(水平發射近似差到 1 m 以上);準心點無效時退回「準心俯仰 + 上抬」
            Vector3 target = ReticleTarget(origin, Mathf.Max(_cannonSpec.RadiusForField, _cannonSpec.RadiusForFieldEnd));
            LastAimTarget = target;
            LastShotOrigin = origin;
            Vector3 direction = IsAhead(origin, target)
                ? BulletAim.SolvedDirection(_cannonSpec.Motion, origin, target)
                : FireDirection(_config.CannonLiftDeg);
            Team team = Owner != null ? Owner.Team : Team.Alpha;
            LastCannon = InkBullet.Fire(_cannonSpec, origin, direction, team, Owner, 0, Vector3.zero, _config.CannonPrefab);
            CannonShots++;
            LastCannonTime = now;
            LastShotDirection = direction;
            if (_visual != null)
            {
                _visual.NotifyExternalShot(1.2f);
            }
            GameAudio.PlayFrom(Owner != null && Owner.IsLocalPlayer, _config.CannonSound, origin, 1f, 0.03f);
            if (InkSplashFxPool.Instance != null)
            {
                InkSplashFxPool.Instance.Spawn(origin, direction, TeamPalette.ColorOf(team));
            }
        }

        // 準心點:視線(玩家 = 相機中心)射線在「視點到砲口 + 砲台射程」內最先撞到的地形/目標(略過自己的車體),
        // 沒撞到就取該距離上的點。撞到時沿表面法線外推彈的地形判定半徑:彈是有半徑的球,低角度打地面時
        // 球會在彈心到達準心之前就先碰地(半徑 ÷ 斜率,9° 俯角時早 1.5 m),外推後接觸點正好落在準心上。
        // 點落在砲口後方或太近(鏡頭壓低看腳邊)時退回沿準心俯仰平行射出的遠點
        private Vector3 ReticleTarget(Vector3 muzzle, float bulletRadius)
        {
            IAimView view = Owner != null ? Owner.AimView : null;
            if (view == null)
            {
                return muzzle + FireDirection(0f) * TurretZeroDistance;
            }
            Vector3 camPos = view.AimOrigin;
            Vector3 forward = view.AimForward;
            float reach = Vector3.Distance(camPos, muzzle) + AimReach;
            int count = Physics.RaycastNonAlloc(camPos, forward, AimHits, reach, ~GameLayers.InklingMask,
                QueryTriggerInteraction.Ignore);
            float nearest = reach;
            Vector3 normal = Vector3.zero;
            Transform body = _body != null ? _body.transform : null;
            for (int i = 0; i < count; i++)
            {
                if ((body != null && AimHits[i].collider.transform.IsChildOf(body)) || AimHits[i].distance >= nearest)
                {
                    continue;
                }
                nearest = AimHits[i].distance;
                normal = AimHits[i].normal;
            }
            Vector3 target = camPos + forward * nearest + normal * bulletRadius;
            return IsAhead(muzzle, target) ? target : muzzle + FireDirection(0f) * TurretZeroDistance;
        }

        // 目標在砲口前方 1 m 以上(車身朝向)
        private bool IsAhead(Vector3 muzzle, Vector3 target)
        {
            Vector3 flatForward = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
            return Vector3.Dot(target - muzzle, flatForward) >= 1f;
        }

        // 本機玩家的鏡頭水平轉速壓到車身轉速(步行模式);bot 的視線不是鏡頭,不受影響
        private void LimitViewTurn(bool limit)
        {
            if (Owner != null && Owner.AimView is ThirdPersonCameraRig rig)
            {
                rig.YawSpeedLimit = limit && _config != null ? _config.TurnDegPerSecond : 0f;
            }
        }

        // 射擊方向:車身水平朝向 + 準心俯仰(+ 額外上抬)
        private Vector3 FireDirection(float liftDeg)
        {
            IAimView view = Owner != null ? Owner.AimView : null;
            Vector3 forward = view != null ? view.AimForward : transform.forward;
            float pitch = Mathf.Asin(Mathf.Clamp(forward.normalized.y, -1f, 1f)) * Mathf.Rad2Deg + liftDeg;
            return Quaternion.Euler(-Mathf.Clamp(pitch, -60f, 70f), _yaw, 0f) * Vector3.forward;
        }

        // ── ZL 滾球 ──

        private void EnterBall()
        {
            _phase = Phase.Ball;
            LimitViewTurn(false);
            _wasFiring = false;
            _ballVelocity = Vector3.zero;
            _nextBallPaintAt = Time.time;
            if (_body != null)
            {
                _body.SetBall(true);
            }
            if (Owner != null)
            {
                // 球裡:回血同潛墨、不扣敵墨血(另外腳下一直塗)
                Owner.Modifiers.Add(_ballSource, ModifierSpec.None.WithSwimRegen().WithoutSlipDamage(), 0f);
            }
            GameAudio.Play(_config.TransformSound, transform.position + Vector3.up, 0.9f, 0.03f);
            _rollLoop = GameAudio.StartLoop(_config.RollLoop, transform, true, 0.7f);
        }

        private void ExitBall(float now)
        {
            _phase = Phase.Walk;
            _climbing = false;
            // 解除時車身直接轉向鏡頭方向、20F 不能射
            _yaw = ViewYaw();
            LimitViewTurn(true);
            _noShotUntil = now + _config.UnrollNoShot;
            if (_body != null)
            {
                _body.SetBall(false);
            }
            if (Owner != null)
            {
                Owner.Modifiers.Remove(_ballSource);
            }
            StopRollLoop();
            GameAudio.Play(_config.TransformSound, transform.position + Vector3.up, 0.9f, 0.03f);
        }

        private void UpdateBall(float now)
        {
            if (now >= _nextBallPaintAt)
            {
                _nextBallPaintAt = now + 0.1f;
                PaintUnderBall();
            }
            BallContact(now);
        }

        private void PaintUnderBall()
        {
            InkSource source = Owner != null ? Owner.InkSource(false) : new InkSource(Team.Alpha, null, false);
            Vector3 center = transform.position + Vector3.up * 1.1f;
            if (Physics.Raycast(center, Vector3.down, out RaycastHit hit, 2f, CombatMasks.Terrain,
                    QueryTriggerInteraction.Ignore))
            {
                InkPainter.PaintSphere(hit.point, _config.BallPaintRadius, 0.75f, source);
            }
            if (_climbing && _ballVelocity.sqrMagnitude > 0.01f)
            {
                Vector3 flat = new Vector3(_ballVelocity.x, 0f, _ballVelocity.z);
                if (flat.sqrMagnitude < 1e-4f)
                {
                    flat = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
                }
                if (Physics.Raycast(center, flat.normalized, out RaycastHit wall, ClimbProbe + 0.3f, CombatMasks.Terrain,
                        QueryTriggerInteraction.Ignore))
                {
                    InkPainter.PaintSphere(wall.point, _config.BallPaintRadius, 0.75f, source);
                }
            }
        }

        // 滾球接觸:球心 1.1 m、半徑 1.45 內的敵人吃 40 + 推開,同一人 0.33 s 一次
        private void BallContact(float now)
        {
            Team team = Owner != null ? Owner.Team : Team.Alpha;
            Vector3 center = transform.position + Vector3.up * 1.1f;
            var all = Inkling.All;
            for (int i = 0; i < all.Count; i++)
            {
                Inkling enemy = all[i];
                if (enemy == null || enemy.Team == team || !enemy.isActiveAndEnabled)
                {
                    continue;
                }
                var health = enemy.GetComponent<InklingHealth>();
                if (health == null || !health.IsAlive)
                {
                    continue;
                }
                Vector3 closest = health.ClosestPoint(center);
                if ((closest - center).sqrMagnitude > _config.BallContactRadius * _config.BallContactRadius)
                {
                    continue;
                }
                int slot = ContactSlot(enemy);
                if (slot < 0 || now < _contactReadyAt[slot])
                {
                    continue;
                }
                _contactReadyAt[slot] = now + _config.BallContactInterval;
                BallContacts++;
                health.ApplyDamage(new DamageInfo(_config.BallContactDamage, closest, team, Owner, DamageKind.Special,
                    AttackClass.CrabTankBody));
                var locomotion = enemy.GetComponent<PlayerLocomotion>();
                if (locomotion != null && locomotion.isActiveAndEnabled && health.IsAlive)
                {
                    locomotion.AddImpulse(KnockbackModel.Impulse(center, closest, 480f, _config.BallContactRadius + 1f));
                }
            }
        }

        private int ContactSlot(Inkling inkling)
        {
            for (int i = 0; i < _contactCount; i++)
            {
                if (_contactTargets[i] == inkling)
                {
                    return i;
                }
            }
            if (_contactCount >= MaxContacts)
            {
                return -1;
            }
            _contactTargets[_contactCount] = inkling;
            _contactReadyAt[_contactCount] = 0f;
            return _contactCount++;
        }

        private void StopRollLoop()
        {
            if (_rollLoop != null)
            {
                _rollLoop.Stop();
                _rollLoop = null;
            }
        }

        // ── 移動接管(ILocomotionOverride / IFacingOverride)──

        public bool InterceptJump(Vector2 moveInput, float cameraYawDeg, bool grounded)
        {
            // 步行、召喚、硬直不能跳;滾球 = 小跳(一般跳躍)
            return _phase != Phase.Ball;
        }

        public bool TryGetControlledMotion(float time, float deltaTime, bool grounded, out Vector3 horizontalVelocity,
            out float forcedVerticalSpeed)
        {
            horizontalVelocity = Vector3.zero;
            forcedVerticalSpeed = float.NaN;
            if (!IsActive || _config == null || Input == null)
            {
                return false;
            }
            Vector2 input = Vector2.ClampMagnitude(Input.MoveInput, 1f);
            switch (_phase)
            {
                case Phase.Walk:
                {
                    // 車身座標:橫移 0.075、前後 0.015 u/F
                    Vector3 local = new Vector3(input.x * _config.WalkLateral, 0f, input.y * _config.WalkForward) * 60f;
                    horizontalVelocity = BlockByBody(Quaternion.Euler(0f, _yaw, 0f) * local, deltaTime);
                    return true;
                }
                case Phase.Ball:
                {
                    // 鏡頭座標全方向 0.1 u/F(0.12 s 加速)
                    Vector3 wish = Quaternion.Euler(0f, ViewYaw(), 0f) * new Vector3(input.x, 0f, input.y)
                                   * (_config.BallSpeed * 60f);
                    _ballVelocity = Vector3.MoveTowards(_ballVelocity, wish, _config.BallSpeed * 60f / 0.12f * deltaTime);
                    _climbing = false;
                    if (wish.sqrMagnitude > 0.01f && TryClimb(wish, out Vector3 into))
                    {
                        // 塗得到的牆:貼著牆直直往上爬(S3 只能上下)
                        _climbing = true;
                        horizontalVelocity = into * 1.5f;
                        forcedVerticalSpeed = _config.BallSpeed * 60f;
                        return true;
                    }
                    horizontalVelocity = BlockByBody(_ballVelocity, deltaTime);
                    return true;
                }
                default:
                    // 召喚中、結束硬直:原地不動
                    return true;
            }
        }

        public void OnLocomotionReset()
        {
            _ballVelocity = Vector3.zero;
            _climbing = false;
        }

        public bool TryGetFacingYaw(out float yawDeg)
        {
            yawDeg = _yaw;
            return IsActive && _phase != Phase.EndLag;
        }

        // 車體半徑 1.15 擋牆:以球掃過要移動的距離,撞到就扣掉往牆裡的分量(駕駛自己的碰撞膠囊只有 0.4)
        private Vector3 BlockByBody(Vector3 velocity, float deltaTime)
        {
            float speed = velocity.magnitude;
            if (speed < 1e-4f)
            {
                return velocity;
            }
            Vector3 center = transform.position + Vector3.up * 1.2f;
            float radius = _config.BodyRadius - 0.1f;
            if (Physics.SphereCast(center, radius, velocity / speed, out RaycastHit hit, speed * deltaTime + 0.05f,
                    CombatMasks.Terrain, QueryTriggerInteraction.Ignore))
            {
                float into = Vector3.Dot(velocity, hit.normal);
                if (into < 0f)
                {
                    velocity -= hit.normal * into;
                }
            }
            return velocity;
        }

        // 前方有塗得到的牆嗎(水平方向;回傳往牆裡的單位向量)
        private bool TryClimb(Vector3 wish, out Vector3 into)
        {
            into = Vector3.zero;
            Vector3 flat = new Vector3(wish.x, 0f, wish.z);
            if (flat.sqrMagnitude < 1e-4f)
            {
                return false;
            }
            flat.Normalize();
            Vector3 center = transform.position + Vector3.up * 0.9f;
            if (!Physics.Raycast(center, flat, out RaycastHit hit, ClimbProbe, CombatMasks.Terrain,
                    QueryTriggerInteraction.Ignore) || Mathf.Abs(hit.normal.y) > 0.3f
                || hit.collider.GetComponent<PaintableSurface>() == null)
            {
                return false;
            }
            into = -new Vector3(hit.normal.x, 0f, hit.normal.z).normalized;
            return true;
        }

        private float ViewYaw()
        {
            IAimView view = Owner != null ? Owner.AimView : null;
            return view != null ? view.Yaw : transform.eulerAngles.y;
        }

        // ── 駕駛姿勢(ISpecialPose):站在艙口裡(整個人抬高 0.5 m,胸口以上露出車殼、腳藏在殼內)、
        //    雙手握胸前兩支操縱桿、上身微前傾;滾球時人藏進球裡 ──

        public bool TryGetSpecialPose(ref SpecialPoseFrame pose)
        {
            if (!IsActive)
            {
                return false;
            }
            // 召喚的 45F 內跟著車體長高一起升上來;結束硬直中戰車已消失 → 落回地面
            float rise = _phase == Phase.EndLag ? 0f
                : Mathf.Clamp01((Time.time - _activatedAt) / Mathf.Max(0.01f, _config.ArmorDelaySeconds));
            pose.Lift = RiderLift * rise * rise * (3f - 2f * rise);
            float kick = Mathf.Clamp01(1f - (Time.time - LastTurretShotTime) * 8f) * 0.02f;
            pose.RightGrip = new Vector3(0.17f, -0.32f, 0.36f - kick);
            pose.RightSocket = Quaternion.Euler(-70f, 0f, 0f);
            pose.LeftGrip = new Vector3(-0.17f, -0.32f, 0.36f - kick);
            pose.LeftSocket = Quaternion.Euler(-70f, 0f, 0f);
            pose.LeftWeight = 1f;
            pose.RightElbow = new Vector3(0.8f, -0.6f, -0.2f);
            pose.LeftElbow = new Vector3(-0.8f, -0.6f, -0.2f);
            pose.TorsoPitch = 8f;
            pose.HideBody = _phase == Phase.Ball;
            return true;
        }

        // ── bot:看得到敵人且 ≤ 28 m 開(沒敵人時偶爾開來塗地)──
        //   有目標:預判瞄準連射(車身轉得慢,準心與車身差 15° 內就打;S3 連射本來就有 3° 散布)、左右橫移躲子彈;
        //     目標在 5~28 m 且砲彈裝填好 → 按住副武器鍵 0.15 秒開砲(每 1.6~2.6 秒想一次)。
        //   沒目標:朝敵方基地方向前方 10 m 的地面連射塗地、慢慢往前推進,每 3~5 秒往前開一砲(塗 4.5)。
        //   裝甲快破且敵人靠近 → 滾球撞過去(接觸 40、全身受保護)。

        public override bool BotWantsActivate(IBotControl bot)
        {
            if (bot.HasTarget && bot.TargetVisible && bot.TargetDistance <= 28f)
            {
                return true;
            }
            return !bot.HasTarget && Random.value < 0.06f;
        }

        public override void BotDrive(IBotControl bot)
        {
            float now = Time.time;
            bool target = bot.HasTarget;
            // 連射彈頭 7F 約 3.36 u/F、之後減速:以 ~120 m/s 預判
            Vector3 aim = target
                ? bot.TargetPosition + bot.TargetVelocity * (bot.TargetDistance / 120f) + Vector3.up * 0.9f
                : bot.Position + bot.EnemyBaseDirection * 10f;
            bot.LookAt(aim);
            bool wantBall = _body != null && _body.Armored && _body.Normalized < 0.35f && target
                            && bot.TargetDistance < 12f;
            if (wantBall && _botBallUntil < now)
            {
                _botBallUntil = now + 1.5f;
            }
            bool ball = now < _botBallUntil;
            bot.HoldSquid(ball);
            if (ball)
            {
                // 滾向敵人撞(接觸 40)
                Vector3 toward = aim - bot.Position;
                toward.y = 0f;
                bot.HoldAttack(false);
                bot.HoldSub(false);
                bot.Move(toward.sqrMagnitude > 0.01f ? toward.normalized : bot.EnemyBaseDirection, 1f);
                return;
            }
            bool aligned = Mathf.Abs(Mathf.DeltaAngle(_yaw, ViewYaw())) < 15f && bot.AimError < 12f;
            bot.HoldAttack(aligned && (!target || bot.TargetDistance < 32f));
            bool cannonReady = _phase == Phase.Walk && now >= _nextCannonAt && now >= _noShotUntil;
            if (aligned && cannonReady && now >= _botNextCannonAt
                && (!target || (bot.TargetDistance > 5f && bot.TargetDistance < 28f)))
            {
                _botNextCannonAt = now + (target ? Random.Range(1.6f, 2.6f) : Random.Range(3f, 5f));
                _botCannonUntil = now + 0.15f;
            }
            bot.HoldSub(now < _botCannonUntil);
            if (now >= _botStrafeFlipAt)
            {
                _botStrafeFlipAt = now + Random.Range(0.7f, 1.5f);
                _botStrafeSign = Random.value < 0.5f ? -1f : 1f;
            }
            Quaternion yaw = Quaternion.Euler(0f, _yaw, 0f);
            Vector3 right = yaw * Vector3.right;
            Vector3 forward = yaw * Vector3.forward;
            // 有目標:橫移為主(遠了才往前壓);沒目標:往前推進、稍微左右晃
            Vector3 move = target
                ? right * _botStrafeSign + forward * (bot.TargetDistance > 20f ? 0.6f : 0f)
                : forward + right * (_botStrafeSign * 0.35f);
            bot.Move(move.normalized, target ? 0.8f : 1f);
        }
    }
}

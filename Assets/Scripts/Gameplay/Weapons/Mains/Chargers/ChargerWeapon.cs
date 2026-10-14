using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.CameraRig;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 狙擊槍的狀態(HUD、角色姿勢、測試讀取)
    public enum ChargerState : byte
    {
        // 沒在蓄力
        Idle = 0,
        // 人形蓄力中(含「提早放開、蓄到 8F 那一刻出手」)
        Charging = 1,
        // 按住攻擊潛墨保留滿蓄(S3 KeepChargeFullFrame 75F)
        Keeping = 2,
        // 帶著保留的滿蓄浮出:25F 雷射回來、31F 才能開槍
        Surfacing = 3,
    }

    // 狙擊槍主武器(Splat Charger / E-liter 4K;S3 v11.3.0 參數見 S3ChargerParams,research 02 §7):
    //   按住蓄力(Splat 60F / E-liter 92F 滿蓄;空中或墨不夠目前蓄力等級時以 1/3 速率),放開射出一發直線高速彈:
    //     飛到「這個蓄力等級的射程」為止(含槍口 10.75 → 25.75 / 30.76 u),彈速 2.4 → 4.8(E-liter 滿蓄 9.6)u/F;
    //     部分蓄力傷害 40 → 80(打不死人)、滿蓄 160 / 180 並貫穿角色;至少蓄 8F 才出手(點一下 = 蓄到 8F 射最小蓄力彈);
    //     耗墨 2.25% → 18% / 25%;墨量鎖:蓄力 / 保留中持續鎖、開槍後 20F;開槍後 15F 內不能潛墨。
    //   子彈 = InkBullet(直線無重力、MaxDistance、PiercePlayers);沿彈道往下落的墨滴(半寬 0.78 → 1.56)在子彈結束時一次塗上
    //     (高速彈一步可達 9.6 m,InkBullet 撞牆那一步不落墨滴,由本元件從槍口塗到終點),終點圓(0.906 → 3.263)、牆面圓 1.2 → 1.8。
    //   儲存蓄力:滿蓄時按住攻擊變烏賊 → 保留 75F;浮出後 25F 雷射回來、31F 可開槍(提早放開 = 31F 那一刻出手);
    //     潛伏中頭上白圈(兩隊都看得到、bot 也看得到)。放開攻擊或超時 = 蓄力消失。
    //   雷射瞄準線(兩隊都看得到):蓄力中從槍口畫到目前射程 / 牆 / 敵人,亮度隨蓄力,滿蓄最亮。
    //   走速:未蓄 0.072 → 滿蓄 0.02 / 0.015 u/F;滿蓄起跳高度 × 0.405(初速 0.07 vs 0.11)。
    // bot:看到敵人就蓄;射程外按住等;滿蓄且雷射掃到敵人才放(或憋太久且準心大致對上);貼近時部分蓄力就放;
    //   塗地時蓄滿打向遠處地面;偏好高處(BotHighGroundBias);撤退時同一套蓄 / 放。
    [DisallowMultipleComponent]
    public sealed class ChargerWeapon : MainWeapon, ILongGunPose
    {
        private const float AirGraceSeconds = 0.06f;
        private const float TimeEpsilon = 1e-4f;
        private const string FallbackFireSound = "shoot_shooter";
        // 看不到目標後,已經在蓄的 bot 繼續按住多久(等對方再露頭)
        private const float BotHoldWithoutSightSeconds = 1.5f;
        // 塗地打完一發後,先沿著剛塗的墨線跑 / 潛游一段再蓄下一發(蓄力中只能 1.2 m/s 慢走,邊走邊蓄會拖慢整個移動)
        public const float BotPaintRepositionSeconds = 1.2f;
        private const float BotPaintRepositionDistance = 6f;
        // 蓄力拉近:剛開始蓄時的 FOV(比人形 55° 大 → 不影響),滿蓄到設定值
        private const float ZoomStartFov = 56f;
        private const int TargetBufferSize = 8;
        private const int InFlightSlots = 4;
        private const float DropReach = 30f;

        private static readonly RaycastHit[] TargetHits = new RaycastHit[TargetBufferSize];

        // 飛行中的一發(子彈結束時塗沿線墨滴、補塗停在人身上的終點)
        private struct ShotRecord
        {
            public int AttackId;
            public Vector3 Origin;
            public Vector3 Direction;
            public float ChargeFrames;
        }

        [SerializeField, Tooltip("狙擊槍設定資產(Assets/Resources/Weapons/Main_<id>)")]
        private ChargerConfig _config;

        [SerializeField, Tooltip("瞄準射線圖層(排除角色)")]
        private LayerMask _aimMask = ~GameLayers.InklingMask;

        [SerializeField, Tooltip("瞄準射線最大距離(公尺)")]
        private float _aimMaxDistance = 60f;

        private readonly ShotRecord[] _inFlight = new ShotRecord[InFlightSlots];
        private int _inFlightNext;
        private InkBulletImpactHandler _onImpact;

        private ChargerState _state;
        private float _chargeFrames;
        private bool _releasePending;
        private float _airTime;
        private float _keepStartTime = float.NegativeInfinity;
        private float _surfaceTime = float.NegativeInfinity;
        private float _lastRecoil = 1f;
        private Transform _visualRoot;
        private Transform _muzzle;
        private ChargerModel _model;
        private ChargerLaser _laser;
        private ChargerReticle _reticle;
        private AudioLoop _chargeLoop;
        private ThirdPersonCameraRig _zoomRig;
        private string _fireSound = FallbackFireSound;
        private string _partialFireSound = FallbackFireSound;

        // 雷射(本幀):是否掃到敵方角色、掃到哪一個
        private bool _laserOnEnemy;
        private IDamageable _laserTarget;

        // bot
        private float _botLastSeen = float.NegativeInfinity;
        private float _botPaintShotTime = float.NegativeInfinity;
        private Vector3 _botPaintShotAt;

        public ChargerConfig Config => _config;
        public override MainWeaponConfig BaseConfig => _config;

        // 攻擊中 = 蓄力 / 保留 / 浮出(走速、面向、加速度加倍、持槍姿勢)
        public override bool IsAttacking => _state != ChargerState.Idle;

        // S3:開槍後 15F 內不能變烏賊
        public override bool BlocksSquid =>
            _config != null && Time.time - LastShotTime < _config.S3.PostShotNoSquidFrames / 60f - TimeEpsilon;

        // 蓄力中的走速:未蓄 0.072 → 滿蓄 0.02(Splat)/ 0.015(E-liter)u/F
        public override float MoveSpeedWhileFiring =>
            _config != null ? _config.S3.MoveSpeed(Charge01) * 60f : 4.32f;

        // 滿蓄起跳高度 × 0.405(人形蓄力 / 浮出中才有)
        public override float JumpHeightFactor =>
            _config != null && (_state == ChargerState.Charging || _state == ChargerState.Surfacing)
                ? _config.S3.JumpHeightFactor(Charge01)
                : 1f;

        // 潛伏中儲存蓄力 → 頭上白圈,藏不住
        public override bool RevealsSubmerged => _state == ChargerState.Keeping;

        // ── 狀態(HUD、角色姿勢、測試)──
        public ChargerState State => _state;
        public float ChargeFrames => _chargeFrames;
        public float Charge01 => _config != null ? _config.S3.Charge01(_chargeFrames) : 0f;
        public bool IsCharging => _state == ChargerState.Charging || _state == ChargerState.Surfacing;
        public bool IsFullyCharged => _config != null && _state != ChargerState.Idle && _config.S3.IsFull(_chargeFrames);
        public bool IsKeepingCharge => _state == ChargerState.Keeping;
        public bool ReleasePending => _releasePending;

        // 保留中剩下的比例(1 → 0;不在保留 = 0)
        public float KeepRemaining01 => _state == ChargerState.Keeping && _config != null
            ? Mathf.Clamp01(1f - (Time.time - _keepStartTime) / (_config.S3.KeepChargeFrames / 60f))
            : 0f;

        public ChargerModel Model => _model;
        public ChargerLaser Laser => _laser;
        public ChargerReticle Reticle => _reticle;
        public bool LaserOnEnemy => _laserOnEnemy;
        public IDamageable LaserTarget => _laserTarget;

        // 統計(測試 / 除錯)
        public int ShotsFired { get; private set; }
        public int FullShots { get; private set; }
        public int FailedShots { get; private set; }
        public int LowInkShots { get; private set; }
        public int CancelCount { get; private set; }
        public int KeepCount { get; private set; }
        public int KeepExpiredCount { get; private set; }
        public int HitCount { get; private set; }
        public float ChargeStartTime { get; private set; } = float.NegativeInfinity;
        public float FullChargeTime { get; private set; } = float.NegativeInfinity;
        public float KeepStartTime => _keepStartTime;
        public float SurfaceTime => _surfaceTime;
        public float LastShotTime { get; private set; } = float.NegativeInfinity;
        public float LastShotChargeFrames { get; private set; }
        public bool LastShotFull { get; private set; }
        public float LastShotDamage { get; private set; }
        public float LastShotMaxDistance { get; private set; }
        public float LastShotReach { get; private set; }
        public float LastShotSpeed { get; private set; }
        public float LastShotInk { get; private set; }
        public int LastShotAttackId { get; private set; }
        public Vector3 LastShotOrigin { get; private set; }
        public Vector3 LastShotDirection { get; private set; }
        public Vector3 LastShotFeet { get; private set; }
        public int LastTrailDrops { get; private set; }
        public Vector3 LastTrailEnd { get; private set; }
        public bool LastShotFromKeep { get; private set; }

        // ── ILongGunPose(CharacterVisual:槍托靠肩、左手托護木)──
        public bool LongGunActive => _model != null && _model.isActiveAndEnabled && isActiveAndEnabled;
        public Vector3 LongGunGripOffset => _config != null ? _config.GripOffset : new Vector3(0.035f, -0.075f, 0.2f);
        public Vector3 LongGunSupportOffset => _config != null ? _config.SupportOffset : new Vector3(-0.015f, -0.05f, 0.3f);
        public float LongGunRecoilScale => _lastRecoil;

        protected override void Awake()
        {
            base.Awake();
            // 子彈結束事件在元件存在期間一直訂閱(不跟著 OnEnable / OnDisable):被擊倒時 InklingLifecycle 會停用主武器,
            // 但已經射出的子彈照樣飛完(S3 互擊:兩邊同時倒下,兩發的墨線都在),它的沿線墨滴仍要由這裡塗上
            _onImpact = OnImpact;
            InkBullet.Impacted += _onImpact;
            _visualRoot = FindChild("Visual");
            _muzzle = FindChild("Muzzle");
            if (_config != null)
            {
                Configure(_config);
            }
        }

        public override void Configure(MainWeaponConfig config)
        {
            var charger = config as ChargerConfig;
            if (charger == null)
            {
                Debug.LogError($"ChargerWeapon:設定型別不符({config})", this);
                return;
            }
            if (Owner == null)
            {
                CacheReferences();
            }
            if (_visualRoot == null)
            {
                _visualRoot = FindChild("Visual");
            }
            if (_muzzle == null)
            {
                _muzzle = FindChild("Muzzle");
            }
            bool modelChanged = _config != charger || _model == null || _model.SourcePrefab != charger.ModelPrefab;
            _config = charger;
            _fireSound = GameAudio.Has(_config.FireSound) ? _config.FireSound : FallbackFireSound;
            _partialFireSound = GameAudio.Has(_config.PartialFireSound) ? _config.PartialFireSound : _fireSound;
            if (modelChanged)
            {
                AttachModel();
            }
            EnsureLaser();
            ResetCharge();
        }

        private void AttachModel()
        {
            if (_model != null)
            {
                _model.Detach();
                _model = null;
            }
            if (_config.ModelPrefab != null)
            {
                _model = ChargerModel.Attach(transform, _config.ModelPrefab, Owner);
            }
        }

        private void EnsureLaser()
        {
            if (_laser == null && _config != null && _config.LaserMaterial != null)
            {
                _laser = ChargerLaser.Create(transform, _config.LaserMaterial, _config.LaserWidth);
            }
        }

        private void OnDisable()
        {
            // 被擊倒(InklingLifecycle 停用主武器)/ 換裝:蓄力、雷射、白圈、循環音、拉近一起收掉
            //(子彈結束事件不退訂:飛行中的那一發照樣塗墨線)
            ResetCharge();
            UpdatePresentation(Time.time);
        }

        private void OnDestroy()
        {
            if (_onImpact != null)
            {
                InkBullet.Impacted -= _onImpact;
                _onImpact = null;
            }
            StopChargeLoop();
            ApplyZoom(0f);
            if (_model != null)
            {
                _model.Detach();
                _model = null;
            }
            if (_laser != null)
            {
                Destroy(_laser.gameObject);
                _laser = null;
            }
            if (_reticle != null)
            {
                Destroy(_reticle.gameObject);
                _reticle = null;
            }
        }

        // ── 每幀 ──

        private void Update()
        {
            if (_config == null || Input == null)
            {
                return;
            }
            if (_model == null && _config.ModelPrefab != null)
            {
                // 角色外觀比武器晚就緒(Awake 順序):第一次 Update 補掛
                AttachModel();
            }
            EnsureLaser();
            if (_reticle == null && Owner != null && Owner.IsLocalPlayer)
            {
                _reticle = ChargerReticle.Create(this);
            }

            ref readonly S3ChargerParams p = ref _config.S3;
            float now = Time.time;
            float dt = Time.deltaTime;
            bool squid = Squid != null && Squid.IsSquid;
            bool held = Input.AttackHeld;
            bool allowed = CanUseMainWeapon;
            bool grounded = Controller == null || !Controller.enabled || Controller.isGrounded;
            _airTime = grounded ? 0f : _airTime + dt;
            float vy = Controller != null && Controller.enabled ? Controller.velocity.y : 0f;
            bool airborne = _airTime > AirGraceSeconds || (_airTime > 0f && vy > 1f);

            switch (_state)
            {
                case ChargerState.Idle:
                    if (held && allowed && !squid)
                    {
                        BeginCharge(now);
                    }
                    break;
                case ChargerState.Charging:
                    if (squid)
                    {
                        // S3:滿蓄 + 按住攻擊潛墨 = 保留蓄力;部分蓄力變烏賊就沒了
                        if (held && p.IsFull(_chargeFrames))
                        {
                            BeginKeep(now);
                        }
                        else
                        {
                            Cancel();
                        }
                        break;
                    }
                    if (!allowed)
                    {
                        // 按住副武器、大招佔用:蓄力取消
                        Cancel();
                        break;
                    }
                    if (!held)
                    {
                        _releasePending = true;
                    }
                    AdvanceCharge(p, dt, airborne);
                    if (_releasePending && p.CanRelease(_chargeFrames))
                    {
                        Fire(now, false);
                    }
                    break;
                case ChargerState.Keeping:
                    LockInk(p);
                    if (!held)
                    {
                        // 潛伏中放開攻擊 = 蓄力消失(不出手)
                        Cancel();
                        break;
                    }
                    if (now - _keepStartTime >= p.KeepChargeFrames / 60f - TimeEpsilon)
                    {
                        KeepExpiredCount++;
                        Cancel();
                        break;
                    }
                    if (!squid)
                    {
                        if (!allowed)
                        {
                            Cancel();
                            break;
                        }
                        _state = ChargerState.Surfacing;
                        _surfaceTime = now;
                    }
                    break;
                case ChargerState.Surfacing:
                    if (squid)
                    {
                        // 浮出前搖中又潛回去:繼續保留(保留計時不重來)
                        _state = ChargerState.Keeping;
                        break;
                    }
                    if (!allowed)
                    {
                        Cancel();
                        break;
                    }
                    LockInk(p);
                    if (!held)
                    {
                        _releasePending = true;
                    }
                    if (now - _surfaceTime >= p.KeepFireDelayFrames / 60f - TimeEpsilon)
                    {
                        // 31F:可以開槍了(之前放開的這一刻出手;還按著 = 回到一般滿蓄)
                        _state = ChargerState.Charging;
                        if (_releasePending)
                        {
                            Fire(now, true);
                        }
                    }
                    break;
            }
            UpdatePresentation(now);
        }

        private void BeginCharge(float now)
        {
            _state = ChargerState.Charging;
            _chargeFrames = 0f;
            _releasePending = false;
            ChargeStartTime = now;
            FullChargeTime = float.NegativeInfinity;
            LockInk(_config.S3);
            StartChargeLoop();
        }

        // 蓄力:每幀 dt × 60 幀 × 速率(空中 / 墨不夠目前蓄力等級 = 1/3);蓄力中持續上墨量鎖
        private void AdvanceCharge(in S3ChargerParams p, float dt, bool airborne)
        {
            LockInk(p);
            if (p.IsFull(_chargeFrames))
            {
                return;
            }
            float step = dt * 60f;
            bool lowInk = Tank != null && !Tank.InfiniteInk
                          && TankStandardFraction() + 1e-5f < p.InkConsume(Mathf.Min(p.FullChargeFrames, _chargeFrames + step));
            _chargeFrames = Mathf.Min(p.FullChargeFrames, _chargeFrames + step * p.ChargeRate(airborne, lowInk));
            if (p.IsFull(_chargeFrames))
            {
                _chargeFrames = p.FullChargeFrames;
                FullChargeTime = Time.time;
                PlaySound(_config.FullChargeSound, 0.9f);
            }
        }

        // 墨槽剩下多少(標準墨槽的分數;Jr. 等大墨槽換算回標準單位)
        private float TankStandardFraction()
        {
            return Tank != null ? Tank.Normalized * Tank.CapacityScale : 1f;
        }

        private void LockInk(in S3ChargerParams p)
        {
            if (Tank != null)
            {
                Tank.LockRefill(p.InkRecoverStop / 60f);
            }
        }

        private void BeginKeep(float now)
        {
            _state = ChargerState.Keeping;
            _keepStartTime = now;
            _releasePending = false;
            KeepCount++;
            StopChargeLoop();
        }

        private void Cancel()
        {
            CancelCount++;
            ResetCharge();
        }

        private void ResetCharge()
        {
            _state = ChargerState.Idle;
            _chargeFrames = 0f;
            _releasePending = false;
            StopChargeLoop();
        }

        // ── 開槍 ──

        private void Fire(float now, bool fromKeep)
        {
            ref readonly S3ChargerParams p = ref _config.S3;
            float frames = Mathf.Max(_chargeFrames, p.MinChargeFrames);
            bool full = p.IsFull(frames);
            float cost = p.InkConsume(frames);
            float spent = cost;
            if (Tank != null)
            {
                // S3:開槍後 20F 才回墨(動作因墨不足失敗也上鎖)
                Tank.LockRefill(p.InkRecoverStop / 60f);
                if (!Tank.TryConsume(cost))
                {
                    float available = TankStandardFraction();
                    if (available + 1e-6f < p.InkConsumeMin)
                    {
                        // 連最小蓄力都付不起:不出手(TryConsume 已發低墨提示)
                        FailedShots++;
                        ResetCharge();
                        return;
                    }
                    // 墨不夠這一發:以 1/3 速率蓄到的等級照樣打出,剩下的墨全部用掉 [EST]
                    spent = available;
                    Tank.Drain(Tank.Normalized);
                    LowInkShots++;
                }
            }
            if (!ComputeShot(frames, out Vector3 origin, out Vector3 direction, out float maxDistance))
            {
                ResetCharge();
                return;
            }
            InkBulletSpec spec = BuildShotSpec(p, frames, full, maxDistance);
            int attackId = CombatLedger.NewAttackId();
            RememberShot(attackId, origin, direction, frames);
            InkBullet.Fire(spec, origin, direction, Owner != null ? Owner.Team : SplatoonC.Core.Team.Alpha, Owner,
                attackId, Vector3.zero, _config.BulletPrefab);

            ShotsFired++;
            if (full)
            {
                FullShots++;
            }
            LastShotTime = now;
            LastShotChargeFrames = frames;
            LastShotFull = full;
            LastShotDamage = spec.DamageMax;
            LastShotMaxDistance = maxDistance;
            LastShotReach = p.ReachFromPlayer(frames);
            LastShotSpeed = spec.Motion.SpawnSpeed;
            LastShotInk = spent;
            LastShotAttackId = attackId;
            LastShotOrigin = origin;
            LastShotDirection = direction;
            LastShotFeet = transform.position;
            LastShotFromKeep = fromKeep;
            _lastRecoil = full ? 2.2f : Mathf.Lerp(0.7f, 1.3f, p.PartialT(frames));
            PlaySound(full ? _fireSound : _partialFireSound, full ? 1f : Mathf.Lerp(0.55f, 0.85f, p.PartialT(frames)));
            if (InkSplashFxPool.Instance != null)
            {
                Vector3 tip = _model != null && _model.Visible ? _model.MuzzleTip.position : origin;
                InkSplashFxPool.Instance.Spawn(tip, direction, TeamPalette.ColorOf(Owner != null ? Owner.Team : SplatoonC.Core.Team.Alpha));
            }
            NotifyAttack();
            ResetCharge();
        }

        // 一發的彈規格(每發依蓄力幀算;結構體複製、零配置):直線等速、無重力,飛到 maxDistance 為止;
        // 傷害固定(不隨飛行幀衰減)、滿蓄貫穿角色;落地 = 終點圓、打牆 = 牆面圓、射程盡頭 = 往下落的終點圓;
        // 沿線墨滴不交給 InkBullet(高速彈撞牆那一步不落墨滴),子彈結束時由 PaintTrail 從槍口塗到終點
        private InkBulletSpec BuildShotSpec(in S3ChargerParams p, float frames, bool full, float maxDistance)
        {
            float speed = p.Speed(frames);
            float damage = p.Damage(frames);
            float end = p.EndPaintRadius(frames);
            var spec = new InkBulletSpec
            {
                Motion = new BulletMotion
                {
                    SpawnSpeed = speed,
                    StraightFrames = 100000,
                    StraightEndMaxSpeed = 1000f,
                    BrakeToFreeVelocityY = BulletMotion.NoYLimit,
                },
                MaxDistance = maxDistance,
                LifetimeFrames = Mathf.Max(30, p.FlightFrames(frames) + 20),
                RadiusForPlayer = p.PlayerRadius,
                RadiusForField = p.FieldRadius,
                PiercePlayers = full,
                DamageMax = damage,
                DamageMin = damage,
                ReduceStartFrame = -1,
                AttackClass = full ? _config.FullAttackClass : _config.PartialAttackClass,
                Kind = DamageKind.Main,
                PaintWidthHalfNear = end,
                PaintWidthHalfFar = end,
                PaintDepthScaleMax = 1f,
                PaintDepthScaleMin = 1f,
                PaintDepthScaleBreakFree = 1f,
                PaintRadiusShock = p.WallPaintRadius(frames),
                PaintHardness = _config.PaintHardness,
                EndPaintRadius = end,
                VisualScale = full ? 1f : Mathf.Lerp(0.55f, 0.85f, p.PartialT(frames)),
                TrailTime = _config.BulletTrailSeconds,
                HitSoundVolume = full ? 0.7f : 0.5f,
            };
            return spec;
        }

        // 一發的出膛點、方向、從槍口量的最大飛行距離:
        // 準星 = TPS 視線上「角色 + 射程」處的點(射程內先撞到東西用命中點),槍口朝它直線射出(無重力,不用補償);
        // 飛行距離 = 從角色量的射程(含 S3 槍口 1.71 u)− 槍口在角色前方的距離 → 終點離角色 10.75 → 25.75 / 30.76 u。
        // 槍口被牆擋住(貼牆開槍)改從同高度的角色中心出發,子彈直接撞牆,不穿牆
        public bool ComputeShot(float frames, out Vector3 origin, out Vector3 direction, out float maxDistance)
        {
            origin = Vector3.zero;
            direction = Vector3.forward;
            maxDistance = 0f;
            IAimView view = Owner != null ? Owner.AimView : null;
            if (_config == null || view == null)
            {
                return false;
            }
            ref readonly S3ChargerParams p = ref _config.S3;
            origin = MuzzlePoint();
            Vector3 camPos = view.AimOrigin;
            Vector3 camForward = view.AimForward;
            float reachFromPlayer = p.ReachFromPlayer(frames);
            float reach = Mathf.Min(Vector3.Distance(camPos, origin) + reachFromPlayer, _aimMaxDistance);
            var ray = new Ray(camPos, camForward);
            Vector3 target = Physics.Raycast(ray, out RaycastHit hit, reach, _aimMask, QueryTriggerInteraction.Ignore)
                ? hit.point
                : ray.GetPoint(reach);
            Vector3 toTarget = target - origin;
            direction = toTarget.sqrMagnitude > 0.25f ? toTarget.normalized : camForward;
            Vector3 pivot = transform.position;
            pivot.y = origin.y;
            if (Physics.Linecast(pivot, origin, CombatMasks.Terrain, QueryTriggerInteraction.Ignore))
            {
                origin = pivot;
            }
            Vector3 fromPivot = origin - pivot;
            maxDistance = Mathf.Max(0.5f, reachFromPlayer - Vector3.Dot(fromPivot, direction));
            return true;
        }

        // 出膛點(玩法):角色的 Muzzle(與射擊槍同一個出膛平面);沒有就用 Visual 前方 1.05 m、高 1.05 m
        private Vector3 MuzzlePoint()
        {
            if (_muzzle != null)
            {
                return _muzzle.position;
            }
            Transform facing = _visualRoot != null ? _visualRoot : transform;
            return transform.position + Vector3.up * 1.05f + facing.forward * 1.05f;
        }

        // ── 子彈結束:沿線墨滴、停在人身上的終點、命中聲 ──

        private void RememberShot(int attackId, Vector3 origin, Vector3 direction, float frames)
        {
            _inFlight[_inFlightNext] = new ShotRecord
            {
                AttackId = attackId, Origin = origin, Direction = direction, ChargeFrames = frames,
            };
            _inFlightNext = (_inFlightNext + 1) % InFlightSlots;
        }

        private void OnImpact(in InkBulletImpact impact)
        {
            if (_config == null || impact.AttackId <= 0)
            {
                return;
            }
            int slot = -1;
            for (int i = 0; i < InFlightSlots; i++)
            {
                if (_inFlight[i].AttackId == impact.AttackId)
                {
                    slot = i;
                    break;
                }
            }
            if (slot < 0)
            {
                return;
            }
            if (impact.Damage > 0f && impact.Target is InklingHealth)
            {
                HitCount++;
                if (Owner != null && Owner.IsLocalPlayer)
                {
                    // S3:狙擊槍命中角色有專屬的高音命中聲(射手自己聽)
                    GameAudio.Play2D(_config.HitSound, 0.9f);
                }
            }
            if (impact.End == InkBulletEnd.Pierce)
            {
                return;
            }
            ShotRecord shot = _inFlight[slot];
            _inFlight[slot].AttackId = 0;
            ref readonly S3ChargerParams p = ref _config.S3;
            float travelled = Mathf.Max(0f, Vector3.Dot(impact.Point - shot.Origin, shot.Direction));
            if (impact.End == InkBulletEnd.MaxDistance || impact.End == InkBulletEnd.Expired)
            {
                travelled = impact.Travelled;
            }
            PaintTrail(p, shot, travelled);
            if (impact.End == InkBulletEnd.Target)
            {
                // 停在人 / 裝置身上:終點的墨往下落(射程盡頭與落地由 InkBullet 塗)
                PaintBelow(impact.Point, p.EndPaintRadius(shot.ChargeFrames));
            }
        }

        // 沿彈道往下落的墨滴:從槍口 TrailNearestSkip(1.2 u)起每「半寬」一滴、半徑 = 半寬(部分蓄力 0.78 → 滿蓄 1.56),
        // 連成一條寬 2 × 半寬的墨線;最後一滴 × LastSplashRate(E-liter 1.5)。墨滴從彈道正下方落到地面(最多 30 m)
        private void PaintTrail(in S3ChargerParams p, in ShotRecord shot, float travelled)
        {
            float w = p.TrailWidthHalf(shot.ChargeFrames);
            if (w <= 0f || travelled <= p.TrailNearestSkip)
            {
                LastTrailDrops = 0;
                return;
            }
            InkSource source = InkShooter.InkSourceOf(Owner);
            float hardness = _config.PaintHardness;
            float spacing = Mathf.Max(0.3f, w);
            float start = Mathf.Max(p.TrailNearestSkip, w * 0.5f);
            int drops = 0;
            for (float d = start; d <= travelled + 1e-3f; d += spacing)
            {
                bool last = d + spacing > travelled + 1e-3f;
                float radius = last ? w * Mathf.Max(1f, p.LastSplashRate) : w;
                if (DropAt(shot.Origin + shot.Direction * d, radius, hardness, source))
                {
                    drops++;
                }
            }
            LastTrailDrops = drops;
            LastTrailEnd = shot.Origin + shot.Direction * travelled;
        }

        private static bool DropAt(Vector3 point, float radius, float hardness, in InkSource source)
        {
            if (!Physics.Raycast(point + Vector3.up * 0.05f, Vector3.down, out RaycastHit hit, DropReach,
                    CombatMasks.Terrain, QueryTriggerInteraction.Ignore))
            {
                return false;
            }
            InkPainter.PaintSphere(hit.point, radius, hardness, source);
            return true;
        }

        private void PaintBelow(Vector3 point, float radius)
        {
            if (radius <= 0f)
            {
                return;
            }
            DropAt(point, radius, _config.PaintHardness, InkShooter.InkSourceOf(Owner));
        }

        // ── 外觀:蓄力計、雷射、白圈、準星拉近、循環音 ──

        private void UpdatePresentation(float now)
        {
            if (_config == null)
            {
                return;
            }
            ref readonly S3ChargerParams p = ref _config.S3;
            bool alive = isActiveAndEnabled;
            bool full = _state != ChargerState.Idle && p.IsFull(_chargeFrames);
            float charge = _state == ChargerState.Idle ? 0f : Charge01;
            if (_model != null)
            {
                _model.SetCharge(charge, full);
            }

            bool laserOn = alive && (_state == ChargerState.Charging
                                     || (_state == ChargerState.Surfacing
                                         && now - _surfaceTime >= p.KeepLaserDelayFrames / 60f - TimeEpsilon));
            _laserOnEnemy = false;
            _laserTarget = null;
            if (laserOn && ComputeShot(Mathf.Max(_chargeFrames, p.MinChargeFrames), out Vector3 origin,
                    out Vector3 direction, out float maxDistance))
            {
                float length = TraceLaser(p, origin, direction, maxDistance, full);
                if (_laser != null)
                {
                    bool tip = _model != null && _model.Visible;
                    _laser.SetLaser(true, tip ? _model.MuzzleTip : null, tip ? _model.MuzzleTip.position : origin,
                        origin + direction * length,
                        TeamPalette.ColorOf(Owner != null ? Owner.Team : SplatoonC.Core.Team.Alpha), charge, full);
                }
            }
            else if (_laser != null && _laser.LaserVisible)
            {
                _laser.SetLaser(false, Vector3.zero, Vector3.zero, Color.white, 0f, false);
            }
            if (_laser != null)
            {
                bool ring = alive && _state == ChargerState.Keeping;
                if (ring || _laser.RingVisible)
                {
                    _laser.SetRing(ring, transform.position, KeepRemaining01);
                }
            }

            if (_chargeLoop != null)
            {
                _chargeLoop.SetVolume(full ? 0.35f : 0.25f + 0.75f * charge);
            }
            ApplyZoom(alive && IsCharging && _config.ChargeZoomFov > 0f
                ? Mathf.Lerp(ZoomStartFov, _config.ChargeZoomFov, charge)
                : 0f);
        }

        // 雷射長度:先撞到的牆,或部分蓄力時先碰到的敵方目標(子彈會停在那);滿蓄貫穿角色,只停在牆 / 裝置上。
        // 同時記下「雷射有沒有掃到敵方角色」(bot 放槍時機)
        private float TraceLaser(in S3ChargerParams p, Vector3 origin, Vector3 direction, float maxDistance, bool full)
        {
            float length = maxDistance;
            if (Physics.Raycast(origin, direction, out RaycastHit wall, maxDistance, CombatMasks.Terrain,
                    QueryTriggerInteraction.Ignore))
            {
                length = wall.distance;
            }
            // 敵方 Big Bubbler 球罩:子彈停在球面上,雷射也是
            SplatoonC.Core.Team team = Owner != null ? Owner.Team : SplatoonC.Core.Team.Alpha;
            if (BarrierSphere.Raycast(team, origin, direction, length, p.FieldRadius, out BarrierHit barrier))
            {
                length = barrier.Distance;
            }
            int count = Physics.SphereCastNonAlloc(origin, Mathf.Max(0.01f, p.PlayerRadius), direction, TargetHits,
                Mathf.Max(1e-3f, length), CombatMasks.Targets, QueryTriggerInteraction.Ignore);
            float nearestStop = length;
            float nearestEnemy = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = TargetHits[i];
                if (hit.collider == null || hit.collider.transform.root == transform.root)
                {
                    continue;
                }
                var target = hit.collider.GetComponentInParent<IDamageable>();
                if (target == null || !target.IsAlive || !DamageRules.CanDamage(team, target))
                {
                    continue;
                }
                bool player = target is InklingHealth;
                if (player && hit.distance < nearestEnemy)
                {
                    nearestEnemy = hit.distance;
                    _laserTarget = target;
                    _laserOnEnemy = true;
                }
                // 部分蓄力停在第一個目標上;滿蓄貫穿角色,只停在裝置上
                if ((!full || !player) && hit.distance > 0f && hit.distance < nearestStop)
                {
                    nearestStop = hit.distance;
                }
            }
            for (int i = 0; i < count; i++)
            {
                TargetHits[i] = default;
            }
            return nearestStop;
        }

        private void ApplyZoom(float fov)
        {
            if (_zoomRig == null)
            {
                if (fov <= 0f || Owner == null || !Owner.IsLocalPlayer)
                {
                    return;
                }
                _zoomRig = Owner.AimView as ThirdPersonCameraRig;
                if (_zoomRig == null)
                {
                    return;
                }
            }
            if (Owner == null || !Owner.IsLocalPlayer || !ReferenceEquals(Owner.AimView, _zoomRig))
            {
                // 不再是本機玩家 / 視線換成別的(測試用固定視線):把拉近還原
                _zoomRig.SetWeaponZoom(0f);
                _zoomRig = null;
                return;
            }
            if (!Mathf.Approximately(_zoomRig.WeaponZoomFov, fov))
            {
                _zoomRig.SetWeaponZoom(fov);
            }
        }

        // ── 聲音 ──

        private void PlaySound(string id, float volume)
        {
            if (string.IsNullOrEmpty(id))
            {
                return;
            }
            GameAudio.PlayFrom(Owner != null && Owner.IsLocalPlayer, id, transform.position + Vector3.up * 1.1f, volume,
                0.03f);
        }

        private void StartChargeLoop()
        {
            StopChargeLoop();
            _chargeLoop = GameAudio.StartLoop(_config.ChargeLoopSound, transform, Owner == null || !Owner.IsLocalPlayer, 0.6f);
            if (_chargeLoop != null)
            {
                _chargeLoop.SetVolume(0.25f);
            }
        }

        private void StopChargeLoop()
        {
            if (_chargeLoop != null)
            {
                _chargeLoop.Stop();
                _chargeLoop = null;
            }
        }

        // ── bot 用法 ──

        // 看到敵人(反應過)就開始蓄;射程外按住等;滿蓄後雷射掃到敵方角色才放,憋超過 BotMaxHoldFullSeconds 且準心
        // 大致對上也放;貼近(BotCloseRange 內)蓄過最小蓄力 + 2F、準心在容許內就放(部分蓄力逼退 / 補刀);
        // 看不到目標:已經在蓄的繼續按住 1.5 秒(等對方再露頭),沒在蓄就不按
        public override bool BotAttackIntent(in BotCombatContext context)
        {
            if (_config == null)
            {
                return false;
            }
            float now = Time.time;
            if (context.TargetVisible)
            {
                _botLastSeen = now;
            }
            bool charging = _state == ChargerState.Charging || _state == ChargerState.Surfacing;
            if (!context.TargetVisible || !context.Reacted)
            {
                return charging && now - _botLastSeen < BotHoldWithoutSightSeconds;
            }
            if (!charging)
            {
                return true;
            }
            ref readonly S3ChargerParams p = ref _config.S3;
            if (context.Distance <= _config.BotCloseRange && _chargeFrames >= p.MinChargeFrames + 2f
                && context.AimError < context.FireTolerance)
            {
                return false;
            }
            if (!p.IsFull(_chargeFrames) || _state == ChargerState.Surfacing)
            {
                return true;
            }
            if (context.Distance > EffectiveRange * 1.02f)
            {
                return true;
            }
            if (_laserOnEnemy)
            {
                return false;
            }
            return !(now - FullChargeTime >= _config.BotMaxHoldFullSeconds
                     && context.AimError < Mathf.Min(context.FireTolerance, 2.5f));
        }

        // 撤退中回頭補槍:同一套蓄 / 放
        public override bool BotRetreatAttackIntent(in BotCombatContext context)
        {
            return BotAttackIntent(context);
        }

        // 狙擊手站遠:想維持 0.8 倍射程(Splat 20.6 m、E-liter 24.6 m);看到後在射程 + 6 m 內就開始交戰(先蓄著等)
        public override float BotPreferredDistance => EffectiveRange * 0.8f;
        public override float BotEngageRange => EffectiveRange + 6f;
        public override float BotSwimApproachDistance => EffectiveRange * 1.15f;

        // 塗地:蓄滿打向遠處地面,一發塗一條長線(Splat 18 m、E-liter 20 m 處落地)
        public override float BotPaintAimDistance => Mathf.Min(EffectiveRange * 0.7f, 20f);

        // 塗地:沒在蓄 → 按(開始蓄);蓄滿 → 放(開槍);墨不夠一發滿蓄的一半就不起手(留墨打人)。
        // 打完一發先放開 BotPaintRepositionSeconds 秒(或走開 6 m):全速跑 / 在剛塗的墨線上潛游過去,再蓄下一發
        public override bool BotPaintIntent(float inkRatio)
        {
            if (_config == null)
            {
                return inkRatio > 0.05f;
            }
            ref readonly S3ChargerParams p = ref _config.S3;
            if (_state == ChargerState.Idle)
            {
                bool repositioning = Time.time - _botPaintShotTime < BotPaintRepositionSeconds
                                     && (transform.position - _botPaintShotAt).sqrMagnitude
                                     < BotPaintRepositionDistance * BotPaintRepositionDistance;
                return !repositioning && inkRatio > p.InkConsumeFull * 0.5f + 0.02f;
            }
            if (p.IsFull(_chargeFrames))
            {
                _botPaintShotTime = Time.time;
                _botPaintShotAt = transform.position;
                return false;
            }
            return true;
        }

        // 回墨前往腳邊塗一點:按一下 → 蓄到最小蓄力就放(9 m 的細線 + 終點圓,夠潛下去)
        public override bool BotFeetPaintIntent(float inkRatio)
        {
            if (_config == null)
            {
                return inkRatio > 0.02f;
            }
            if (_state == ChargerState.Idle)
            {
                return inkRatio > _config.S3.InkConsumeMin;
            }
            return !_config.S3.CanRelease(_chargeFrames);
        }

        // 偏好高處(挑塗地 / 移動目標時每 3 m 高低差加分)
        public override float BotHighGroundBias => _config != null ? _config.BotHighGroundBias : 0f;
    }
}

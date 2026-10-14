using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 滾筒的動作階段(CharacterVisual 依此擺姿勢、HUD / 測試讀取)
    public enum RollerAction : byte
    {
        // 拿著(沒在攻擊)
        Idle = 0,
        // 舉起蓄勢(SwingFrame 幀):地面 = 高舉過頭(橫甩)、空中 = 往右後拉(直甩)
        Windup = 1,
        // 甩出後的收招(還按著 → RollStartDelay 後推滾;放開 → 甩動循環結束回 Idle)
        FollowThrough = 2,
        // 推滾
        Rolling = 3,
    }

    // 滾筒主武器(Splat / Carbon / Dynamo Roller;S3 v11.3.0 參數見 S3RollerParams)。
    //   點按:舉起 SwingFrame 幀後甩墨——地面橫甩(寬扇形,Splat 12+1 顆)、空中直甩(縱向一列 5 顆,射程較長);
    //         墨團 = InkBullet(RollerFamily 彈道:直線 → 減速 0.1 / 0.04),對人判定在前幾幀長大(Splat 0.12 → 1.02 / 4F);
    //         傷害依飛行距離分段(中央 / 側邊兩條曲線)再乘飛行幀數的時間衰減;同一甩對同一目標只算最高的一顆。
    //         墨不足:改甩弱甩(耗墨 × 0.5、顆數變少、初速 × 0.5 / 0.7、傷害 × 0.25 / 0.5);連弱甩都不夠就只有動作。
    //   連點:放開後再按 = 等甩動循環(CycleFrames)結束再甩下一下,不會變成推滾。
    //   按住:甩完後推滾——速度 SpeedNormal、連續推滾 DashFrame 幀後 SpeedDash(急轉掉速);沿路塗一條
    //         2 × PaintHalfWidth(速度)寬的墨(Splat 衝刺 5.6 m);滾筒碰到敵人 = 接觸傷害(Splat / Dynamo 125、Carbon 70),
    //         沒被擊倒時對方被推開、自己被彈回;耗墨隨速度 0.02–0.1%/F,墨量鎖 20F。
    // 走速:甩動中 SwingMoveSpeed、推滾中推滾速度(比跑速快,AttackMoveSpeedCanExceedRun)。
    // bot:BotPreferredDistance 短(伏擊/繞後)、一擊必殺距離內點按甩墨、貼身按住輾過去、塗地時按住推滾(瞄腳前)。
    public sealed class RollerWeapon : MainWeapon
    {
        private const int MaxContactTargets = 8;
        private const int OverlapBufferSize = 16;
        private const float AimReachPadding = 6f;
        private const float AirGraceSeconds = 0.06f;
        // 甩出瞬間聲音 / 動作:墨團從滾筒甩出去的視覺節奏
        private const float FlingAnimationSeconds = 0.28f;
        private const string FallbackFlickSound = "bomb_throw";
        private const string FallbackRollLoopSound = "squid_climb_loop";

        private static readonly Collider[] OverlapBuffer = new Collider[OverlapBufferSize];

        [SerializeField, Tooltip("滾筒設定資產(Assets/Resources/Weapons/Main_<id>)")]
        private RollerConfig _config;

        // 甩墨墨團規格與傷害委派:[甩法 0 = 橫 / 1 = 直][單位][弱甩 0/1],換裝時建好(執行期零配置)
        private InkBulletSpec[][] _specs = new InkBulletSpec[2][];
        private InkBulletSpec[][] _depletedSpecs = new InkBulletSpec[2][];
        private RollerGlobDamage[][] _damageInside = new RollerGlobDamage[2][];
        private RollerGlobDamage[][] _damageOutside = new RollerGlobDamage[2][];
        private RollerGlobDamage[][] _damageInsideDepleted = new RollerGlobDamage[2][];
        private RollerGlobDamage[][] _damageOutsideDepleted = new RollerGlobDamage[2][];
        private InkBulletSpec _verticalLeadSpec;
        private InkBulletSpec _verticalLeadDepletedSpec;

        private RollerAction _action;
        private bool _vertical;
        // 這一甩從按下起一直按著(S3:按住才會甩完接推滾;中途放開再按 = 等甩動循環結束後再甩一下)
        private bool _heldSinceSwing;
        private float _swingStart = float.NegativeInfinity;
        private float _flingTime = float.NegativeInfinity;
        private float _nextSwingReady;
        private float _airTime;
        private RollerDashTracker _dash;
        private bool _dashTurning;
        private float _rollSpeedCap;
        private float _lastYaw;
        private bool _yawValid;
        private bool _dry;
        private Vector3 _lastStamp;
        private bool _hasStamp;
        private int _stampIndex;
        private float _rollSpeed;
        private Transform _visualRoot;
        private RollerModel _model;
        private AudioLoop _rollLoop;
        private string _flickSound = FallbackFlickSound;
        private string _rollLoopSound = FallbackRollLoopSound;
        private readonly IDamageable[] _contactTargets = new IDamageable[MaxContactTargets];
        private readonly float[] _contactTimes = new float[MaxContactTargets];
        private int _contactNext;

        public RollerConfig Config => _config;
        public override MainWeaponConfig BaseConfig => _config;

        // 攻擊中 = 舉起 / 收招 / 推滾(走速、面向、加速度加倍、角色姿勢)
        public override bool IsAttacking => _action != RollerAction.Idle;

        // 甩動中 SwingMoveSpeed、推滾中推滾速度上限(普通 / 衝刺 / 急轉)
        public override float MoveSpeedWhileFiring
        {
            get
            {
                if (_config == null)
                {
                    return 4.32f;
                }
                switch (_action)
                {
                    case RollerAction.Rolling:
                        return _rollSpeedCap * 60f;
                    case RollerAction.Windup:
                    case RollerAction.FollowThrough:
                        return _config.Swing(_vertical).MoveSpeedPerSecond;
                    default:
                        return _config.MoveSpeedWhileFiring;
                }
            }
        }

        public override bool AttackMoveSpeedCanExceedRun => _action == RollerAction.Rolling;

        // ── 狀態(角色姿勢、HUD、測試)──
        public RollerAction Action => _action;
        public bool CurrentSwingVertical => _vertical;
        public bool IsRolling => _action == RollerAction.Rolling;
        public bool IsDashing => IsRolling && _config != null && _dash.IsDashing(_config.Roll);
        public bool IsDashTurning => _dashTurning;
        // 推滾中沒墨(滾筒不塗墨、不傷人)
        public bool IsDry => _dry;
        // 實際水平速度(公尺/秒)
        public float RollSpeed => _rollSpeed;
        public float RollSpeedNormalized => _config != null && _config.Roll.SpeedDash > 0f
            ? Mathf.Clamp01(_rollSpeed / (_config.Roll.SpeedDash * 60f))
            : 0f;
        // 舉起進度 0 → 1(甩出瞬間 = 1)
        public float WindupProgress => _action == RollerAction.Windup && _config != null
            ? Mathf.Clamp01((Time.time - _swingStart) / Mathf.Max(0.001f, _config.Swing(_vertical).SwingSeconds))
            : (_action == RollerAction.Idle ? 0f : 1f);
        // 甩出後的動作進度 0 → 1(FlingAnimationSeconds;不在甩出後 = 1)
        public float FlingProgress => Mathf.Clamp01((Time.time - _flingTime) / FlingAnimationSeconds);
        public float DashFrames => _dash.RollFrames;
        public RollerModel Model => _model;
        // 甩出後還按著(滾筒落地後就要推滾;角色姿勢收到推滾而不是拿著)
        public bool RollPending => _action == RollerAction.FollowThrough && _heldSinceSwing && Input != null
                                   && Input.AttackHeld && CanUseMainWeapon;

        // 統計(測試 / 除錯)
        public int SwingCount { get; private set; }
        public int FlickCount { get; private set; }
        public int VerticalFlickCount { get; private set; }
        public int DepletedFlickCount { get; private set; }
        public int FailedFlickCount { get; private set; }
        public int LastFlingBulletCount { get; private set; }
        public int LastFlingAttackId { get; private set; }
        public bool LastFlingVertical { get; private set; }
        public bool LastFlingDepleted { get; private set; }
        public float LastSwingStartTime => _swingStart;
        public float LastFlingTime => _flingTime;
        public Vector3 LastFlingOrigin { get; private set; }
        public Vector3 LastFlingForward { get; private set; }
        public int RollStartCount { get; private set; }
        public float RollStartTime { get; private set; } = float.NegativeInfinity;
        public float DashStartTime { get; private set; } = float.NegativeInfinity;
        public float RolledDistance { get; private set; }
        public int RollStamps { get; private set; }
        public float LastStampHalfWidth { get; private set; }
        public float LastRollInkTime { get; private set; } = float.NegativeInfinity;
        public int ContactHits { get; private set; }
        public float LastContactDamage { get; private set; }
        public bool LastContactSplatted { get; private set; }

        protected override void Awake()
        {
            base.Awake();
            _visualRoot = FindChild("Visual");
            if (_config != null)
            {
                Configure(_config);
            }
        }

        public override void Configure(MainWeaponConfig config)
        {
            var roller = config as RollerConfig;
            if (roller == null)
            {
                Debug.LogError($"RollerWeapon:設定型別不符({config})", this);
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
            bool modelChanged = _config != roller || _model == null || _model.SourcePrefab != roller.ModelPrefab;
            _config = roller;
            BuildSpecs();
            _flickSound = GameAudio.Has(_config.FireSound) ? _config.FireSound : FallbackFlickSound;
            _rollLoopSound = GameAudio.Has(_config.RollLoopSound) ? _config.RollLoopSound : FallbackRollLoopSound;
            _rollSpeedCap = _config.Roll.SpeedNormal;
            if (modelChanged)
            {
                AttachModel();
            }
            ResetToIdle();
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
                _model = RollerModel.Attach(transform, _config.ModelPrefab, Owner);
            }
        }

        // ── 墨團規格(S3 單位參數 → InkBulletSpec)──

        private void BuildSpecs()
        {
            for (int s = 0; s < 2; s++)
            {
                bool vertical = s == 1;
                RollerSwingParams swing = _config.Swing(vertical);
                int units = swing.Units != null ? swing.Units.Length : 0;
                _specs[s] = new InkBulletSpec[units];
                _depletedSpecs[s] = new InkBulletSpec[units];
                _damageInside[s] = new RollerGlobDamage[units];
                _damageOutside[s] = new RollerGlobDamage[units];
                _damageInsideDepleted[s] = new RollerGlobDamage[units];
                _damageOutsideDepleted[s] = new RollerGlobDamage[units];
                for (int u = 0; u < units; u++)
                {
                    RollerFlingUnit unit = swing.Units[u];
                    _specs[s][u] = BuildSpec(swing, unit, false);
                    _depletedSpecs[s][u] = BuildSpec(swing, unit, true);
                    float rate = unit.DamageRate;
                    _damageInside[s][u] = new RollerGlobDamage(swing.Damage, true, false, rate);
                    _damageOutside[s][u] = new RollerGlobDamage(swing.Damage, false, false, rate);
                    _damageInsideDepleted[s][u] = new RollerGlobDamage(swing.Damage, true, true, rate);
                    _damageOutsideDepleted[s][u] = new RollerGlobDamage(swing.Damage, false, true, rate);
                }
            }
            // 直甩沿線墨滴(SpawnSplash):掛在最快的第一顆上。S3 是 Num 個沿飛行方向拉長 DepthScale(3.0)的橢圓,
            // 首尾相接成一條連續墨線;這裡以半寬 w 的圓每 w 公尺滴一滴覆蓋同一段(第一顆橢圓的後緣起,到最後一顆的前緣)
            RollerSwingParams v = _config.Vertical;
            _verticalLeadSpec = v.Units != null && v.Units.Length > 0 ? _specs[1][0] : default;
            if (v.LineSplashNum > 0 && v.LineSplashWidthHalf > 0f && v.Units != null && v.Units.Length > 0)
            {
                float w = v.LineSplashWidthHalf;
                float halfLength = w * Mathf.Max(1f, v.LineSplashDepthScale);
                float start = Mathf.Max(0.3f, v.LineSplashFirst - halfLength + w);
                float end = v.LineSplashFirst + (v.LineSplashNum - 1) * v.LineSplashBetween + halfLength - w;
                _verticalLeadSpec.DripRadius = w;
                _verticalLeadSpec.DripSpacing = w;
                _verticalLeadSpec.DripStartDistance = start;
                _verticalLeadSpec.DripMaxCount = Mathf.Max(1, Mathf.CeilToInt((end - start) / w) + 1);
                _verticalLeadDepletedSpec = _depletedSpecs[1][0];
                _verticalLeadDepletedSpec.DripRadius = w * 0.5f;
                _verticalLeadDepletedSpec.DripSpacing = w * 0.5f;
                _verticalLeadDepletedSpec.DripStartDistance = start;
                _verticalLeadDepletedSpec.DripMaxCount = _verticalLeadSpec.DripMaxCount;
            }
            else
            {
                _verticalLeadDepletedSpec = v.Units != null && v.Units.Length > 0 ? _depletedSpecs[1][0] : default;
            }
        }

        private InkBulletSpec BuildSpec(in RollerSwingParams swing, in RollerFlingUnit unit, bool depleted)
        {
            float radiusRate = depleted && unit.DepletionRadiusRate > 0f ? unit.DepletionRadiusRate : 1f;
            float paintRate = depleted && unit.DepletionPaintRate > 0f ? unit.DepletionPaintRate : 1f;
            InkBulletSpec spec = InkBulletSpec.RollerFamily(unit.SpawnSpeed, unit.StraightFrames,
                _config.Swing(false).Damage.Inside.MaxValue, 0f, -1, 0,
                unit.PlayerRadiusInit * radiusRate, unit.PlayerRadiusEnd * radiusRate, unit.PlayerRadiusFrames,
                _config.AttackClass);
            spec.RadiusForField = unit.FieldRadiusInit * radiusRate;
            spec.RadiusForFieldEnd = unit.FieldRadiusEnd * radiusRate;
            spec.RadiusForFieldChangeFrames = unit.FieldRadiusFrames;
            // 同一甩的所有墨團共用 attackId:InkBullet 端只做累加(上限給大值),「只算最高的一顆」由 RollerGlobDamage 補差額
            spec.PerAttackDamageCap = 1000f;
            spec.PaintWidthHalfNear = unit.PaintWidthHalfNear * paintRate;
            spec.PaintWidthHalfFar = unit.PaintWidthHalfFar * paintRate;
            // S3 PaintParam:半寬在 1.1 u → DistanceFar(Splat 橫 12)之間近→遠;飛久了再縮(20 → 50F 縮到 × 0.6)
            spec.PaintDistanceFar = swing.PaintDistanceFar;
            spec.PaintWidthShrinkRate = swing.PaintShrinkRate;
            spec.PaintWidthShrinkStartFrame = swing.PaintShrinkStartFrame;
            spec.PaintWidthShrinkEndFrame = swing.PaintShrinkEndFrame;
            spec.PaintDepthScaleMax = unit.PaintDepthScaleMax;
            spec.PaintDepthScaleMin = unit.PaintDepthScaleMin;
            spec.PaintDepthScaleBreakFree = unit.PaintDepthScaleMin;
            spec.PaintRadiusShock = unit.WallPaintRadius * paintRate;
            spec.PaintHardness = _config.GlobPaintHardness;
            // 墨團 prefab 的本體直徑 = 0.6 m(DrawSizeParam 半徑 0.3 → 倍率 1)
            spec.VisualScale = Mathf.Max(0.3f, unit.DrawRadius / 0.3f) * (depleted ? 0.75f : 1f);
            spec.TrailTime = 0.03f;
            spec.LifetimeFrames = 240;
            return spec;
        }

        // ── 每幀 ──

        private void OnDisable()
        {
            // 被擊倒(InklingLifecycle 停用主武器)/ 換裝:動作與循環音效一起停
            ResetToIdle();
        }

        private void OnDestroy()
        {
            StopRollLoop();
            if (_model != null)
            {
                _model.Detach();
                _model = null;
            }
        }

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
            float now = Time.time;
            float dt = Time.deltaTime;
            bool allowed = CanUseMainWeapon;
            bool held = allowed && Input.AttackHeld;
            bool grounded = Controller == null || Controller.isGrounded;
            _airTime = grounded ? 0f : _airTime + dt;
            Vector3 velocity = Controller != null && Controller.enabled ? Controller.velocity : Vector3.zero;
            _rollSpeed = new Vector2(velocity.x, velocity.z).magnitude;

            // 變烏賊、按住副武器、大招佔用:甩到一半取消、推滾停止(S3 甩出前換成烏賊就不出墨也不耗墨)
            if (!allowed && _action != RollerAction.Idle)
            {
                ResetToIdle();
            }

            if (!held)
            {
                _heldSinceSwing = false;
            }
            switch (_action)
            {
                case RollerAction.Idle:
                    if (held && now >= _nextSwingReady)
                    {
                        BeginSwing(now, IsAirborne(velocity));
                    }
                    break;
                case RollerAction.Windup:
                    if (now - _swingStart >= _config.Swing(_vertical).SwingSeconds - 1e-4f)
                    {
                        Fling(now, velocity);
                    }
                    break;
                case RollerAction.FollowThrough:
                    if (held && _heldSinceSwing)
                    {
                        // 從按下一直按著:滾筒落地後開始推滾(在空中甩的 → 等落地)
                        if (grounded && now - _flingTime >= _config.RollStartDelaySeconds)
                        {
                            BeginRoll(now);
                        }
                    }
                    else if (now >= _nextSwingReady)
                    {
                        // 甩動循環結束:放開 → 待機;放開後又按下 → 直接甩下一下(連點連甩)
                        _action = RollerAction.Idle;
                        if (held)
                        {
                            BeginSwing(now, IsAirborne(velocity));
                        }
                    }
                    break;
                case RollerAction.Rolling:
                    if (!held)
                    {
                        EndRoll();
                        _action = RollerAction.Idle;
                        break;
                    }
                    TickRoll(dt, grounded, velocity);
                    break;
            }
            if (_model != null)
            {
                _model.SetSpin(_action == RollerAction.Rolling && grounded ? ForwardSpeed(velocity) : 0f);
            }
        }

        // CharacterController.isGrounded 在台階/斜坡會閃一兩幀:離地超過 AirGraceSeconds 或明顯往上跳才算空中
        private bool IsAirborne(Vector3 velocity)
        {
            return _airTime > AirGraceSeconds || (_airTime > 0f && velocity.y > 1f);
        }

        private void ResetToIdle()
        {
            if (_action == RollerAction.Rolling)
            {
                EndRoll();
            }
            StopRollLoop();
            _action = RollerAction.Idle;
            _dash.Reset();
            _dashTurning = false;
            _dry = false;
            if (_config != null)
            {
                _rollSpeedCap = _config.Roll.SpeedNormal;
            }
            if (_model != null)
            {
                _model.SetSpin(0f);
            }
        }

        // ── 甩墨 ──

        private void BeginSwing(float now, bool airborne)
        {
            _vertical = airborne;
            _action = RollerAction.Windup;
            _heldSinceSwing = true;
            _swingStart = now;
            RollerSwingParams swing = _config.Swing(_vertical);
            _nextSwingReady = now + swing.CycleFrames / 60f;
            SwingCount++;
            PlaySound(_config.WindupSound, 0.8f);
        }

        private void Fling(float now, Vector3 velocity)
        {
            _action = RollerAction.FollowThrough;
            _flingTime = now;
            RollerSwingParams swing = _config.Swing(_vertical);
            bool depleted = false;
            if (Tank != null)
            {
                // S3:墨量鎖從甩出起算;墨不夠也照樣上鎖。不夠整甩 → 弱甩(耗墨 × 0.5);連弱甩都不夠 → 空甩
                Tank.LockRefill(swing.InkLockSeconds);
                if (!Tank.TryConsume(swing.InkConsume))
                {
                    float depletedCost = swing.InkConsume * (swing.DepletionInkRate > 0f ? swing.DepletionInkRate : 0.5f);
                    if (!Tank.TryConsume(depletedCost))
                    {
                        FailedFlickCount++;
                        LastFlingBulletCount = 0;
                        return;
                    }
                    depleted = true;
                }
            }

            ComputeFlingFrame(swing, out Vector3 origin, out Vector3 forward, out Quaternion aim);
            int attackId = CombatLedger.NewAttackId();
            // 玩家速度的前向分量 × 2.0 疊到墨團初速(spl__SpawnBulletAdditionMovePlayerParam ZRate 2.0;m/s)
            Vector3 inherit = Vector3.Project(new Vector3(velocity.x, 0f, velocity.z), forward) * 2f;
            int s = _vertical ? 1 : 0;
            int fired = 0;
            int units = swing.Units != null ? swing.Units.Length : 0;
            Vector3 right = aim * Vector3.right;
            Vector3 up = Vector3.up;
            InkBullet prefab = _config.GlobPrefab;
            for (int u = 0; u < units; u++)
            {
                RollerFlingUnit unit = swing.Units[u];
                int count = unit.CountFor(depleted);
                for (int i = 0; i < count; i++)
                {
                    RollerGlobSpawn spawn = RollerMotion.Layout(unit, i, count, depleted, Random.value, Random.value);
                    Vector3 direction = aim * (Quaternion.Euler(-spawn.PitchDeg, spawn.YawDeg, 0f) * Vector3.forward);
                    Vector3 jitter = new Vector3(Random.value - 0.5f, Random.value - 0.5f, Random.value - 0.5f) * 0.1f;
                    Vector3 position = origin + right * spawn.Lateral + up * spawn.Height + jitter;
                    InkBulletSpec spec;
                    if (_vertical && u == 0 && i == 0)
                    {
                        spec = depleted ? _verticalLeadDepletedSpec : _verticalLeadSpec;
                    }
                    else
                    {
                        spec = depleted ? _depletedSpecs[s][u] : _specs[s][u];
                    }
                    spec.Motion.SpawnSpeed = spawn.Speed;
                    // 只讓中央一顆出落地聲(一甩十幾顆同時落地)
                    spec.Silent = !(u == 0 && i == count / 2);
                    bool inside = _config.Swing(_vertical).Damage.IsInside(spawn.YawDeg);
                    RollerGlobDamage damage = depleted
                        ? (inside ? _damageInsideDepleted[s][u] : _damageOutsideDepleted[s][u])
                        : (inside ? _damageInside[s][u] : _damageOutside[s][u]);
                    InkBullet bullet = InkBullet.Fire(spec, position, direction, Team, Owner, attackId, inherit, prefab,
                        damage.Func);
                    if (bullet != null)
                    {
                        fired++;
                    }
                }
            }
            PaintFeetSplash(swing, forward, depleted);

            LastFlingBulletCount = fired;
            LastFlingAttackId = attackId;
            LastFlingVertical = _vertical;
            LastFlingDepleted = depleted;
            LastFlingOrigin = origin;
            LastFlingForward = forward;
            FlickCount++;
            if (_vertical)
            {
                VerticalFlickCount++;
            }
            if (depleted)
            {
                DepletedFlickCount++;
            }
            PlaySound(_flickSound, depleted ? 0.6f : 1f);
            if (InkSplashFxPool.Instance != null)
            {
                InkSplashFxPool.Instance.Spawn(origin + forward * 0.4f, forward, TeamPalette.ColorOf(Team));
            }
            NotifyAttack();
        }

        // 甩墨基準點與方向:水平朝向 = 瞄準方向;仰角 = 基準點 → 準心射線上「一擊必殺距離附近」的點(看地面就甩向地面)
        private void ComputeFlingFrame(in RollerSwingParams swing, out Vector3 origin, out Vector3 forward,
            out Quaternion aim)
        {
            IAimView view = Owner != null ? Owner.AimView : null;
            Vector3 aimForward = view != null ? view.AimForward : (_visualRoot != null ? _visualRoot.forward : transform.forward);
            Vector3 flat = new Vector3(aimForward.x, 0f, aimForward.z);
            if (flat.sqrMagnitude < 1e-6f)
            {
                flat = _visualRoot != null ? _visualRoot.forward : transform.forward;
                flat.y = 0f;
            }
            forward = flat.normalized;
            float height = _vertical ? _config.VerticalSpawnHeight : _config.HorizontalSpawnHeight;
            float ahead = _vertical ? _config.VerticalSpawnForward : _config.HorizontalSpawnForward;
            origin = transform.position + Vector3.up * height + forward * ahead;

            float pitch = 0f;
            if (view != null)
            {
                Vector3 eye = view.AimOrigin;
                float reach = Vector3.Distance(eye, origin) + swing.LethalDistance + AimReachPadding;
                Vector3 target = Physics.Raycast(eye, aimForward, out RaycastHit hit, reach, CombatMasks.Terrain,
                    QueryTriggerInteraction.Ignore)
                    ? hit.point
                    : eye + aimForward * reach;
                Vector3 toTarget = target - origin;
                float horizontal = new Vector2(toTarget.x, toTarget.z).magnitude;
                pitch = Mathf.Atan2(toTarget.y, Mathf.Max(0.5f, horizontal)) * Mathf.Rad2Deg;
            }
            pitch = Mathf.Clamp(pitch, -40f, 50f);
            aim = Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(-pitch, 0f, 0f);
        }

        // 腳前濺墨(SplashNearestParam:腳前 0.5 u、往下找 2 u 內的地面、半寬 × 拉長 DepthScale):兩顆球沿前方排開近似橢圓
        private void PaintFeetSplash(in RollerSwingParams swing, Vector3 forward, bool depleted)
        {
            float w = swing.FeetSplashWidthHalf * (depleted ? 0.5f : 1f);
            if (w <= 0f)
            {
                return;
            }
            Vector3 center = transform.position + forward * 0.5f;
            float offset = w * Mathf.Max(0f, swing.FeetSplashDepthScale - 1f);
            InkSource source = InkShooter.InkSourceOf(Owner);
            PaintAtFloor(center + forward * offset, w, 2f, _config.GlobPaintHardness, source, false);
            if (offset > 0.05f)
            {
                PaintAtFloor(center - forward * offset, w, 2f, _config.GlobPaintHardness, source, false);
            }
        }

        // ── 推滾 ──

        private void BeginRoll(float now)
        {
            _action = RollerAction.Rolling;
            _dash.Reset();
            _dashTurning = false;
            _hasStamp = false;
            _yawValid = false;
            _dry = false;
            _rollSpeedCap = _config.Roll.SpeedNormal;
            RollStartCount++;
            RollStartTime = now;
            DashStartTime = float.NegativeInfinity;
            StartRollLoop();
        }

        private void EndRoll()
        {
            StopRollLoop();
            _dash.Reset();
            _dashTurning = false;
            if (_model != null)
            {
                _model.SetSpin(0f);
            }
        }

        private void TickRoll(float dt, bool grounded, Vector3 velocity)
        {
            RollerRollParams roll = _config.Roll;
            float frames = dt * 60f;
            float speedPerFrame = _rollSpeed / 60f;
            Vector3 forward = Facing();
            float yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            float turn = _yawValid && frames > 1e-4f ? Mathf.DeltaAngle(_lastYaw, yaw) / frames : 0f;
            _lastYaw = yaw;
            _yawValid = true;

            bool wasDashing = _dash.IsDashing(roll);
            // 空中不推滾:衝刺計時歸零、不塗不傷(落地後重新累積)
            _rollSpeedCap = grounded
                ? _dash.Advance(roll, frames, speedPerFrame, turn, out _dashTurning)
                : ResetDashInAir(roll);
            if (!wasDashing && _dash.IsDashing(roll))
            {
                DashStartTime = Time.time;
            }

            float loopLevel = 0f;
            if (grounded && speedPerFrame >= RollerRollParams.StillSpeed)
            {
                // 耗墨隨速度(0.02–0.1%/F);每幀都上 20F 墨量鎖。沒墨 = 滾筒乾了:不塗不傷
                if (Tank != null)
                {
                    Tank.LockRefill(roll.InkLockSeconds);
                    _dry = !Tank.TryConsume(roll.InkPerFrame(speedPerFrame) * frames);
                    LastRollInkTime = Time.time;
                }
                if (!_dry)
                {
                    RolledDistance += _rollSpeed * dt;
                    PaintTrail(forward, speedPerFrame);
                    loopLevel = Mathf.Clamp01(0.35f + 0.65f * RollSpeedNormalized);
                }
            }
            else
            {
                _hasStamp = false;
            }
            // 接觸傷害:滾筒在推(有速度,或推著搖桿但被對方身體擋住)才算;放著不動的滾筒不傷人
            bool pushing = speedPerFrame >= RollerRollParams.StillSpeed || Input.MoveInput.sqrMagnitude > 0.09f;
            if (grounded && pushing && !_dry)
            {
                CheckContacts(forward, Mathf.Max(speedPerFrame, roll.SpeedNormal * 0.5f));
            }
            if (_rollLoop != null)
            {
                _rollLoop.SetVolume(loopLevel);
            }
        }

        private float ResetDashInAir(in RollerRollParams roll)
        {
            _dash.Reset();
            _dashTurning = false;
            return roll.SpeedNormal;
        }

        // 角色面向(推滾時 = 瞄準方向,PlayerLocomotion 轉 Visual)
        private Vector3 Facing()
        {
            Vector3 forward = _visualRoot != null ? _visualRoot.forward : transform.forward;
            forward.y = 0f;
            return forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
        }

        private float ForwardSpeed(Vector3 velocity)
        {
            return Vector3.Dot(new Vector3(velocity.x, 0f, velocity.z), Facing());
        }

        // 沿路塗墨:滾筒軸心下方一條寬 2 × PaintHalfWidth(速度)的帶子。每前進約 0.27 × 半寬放一組三顆球
        //(中央 + 左右,各半徑 0.45 × 半寬):中央 = 滾筒本體的接觸塗墨(牆也塗),左右 = 兩側濺墨(只塗地面)
        private void PaintTrail(Vector3 forward, float speedPerFrame)
        {
            RollerRollParams roll = _config.Roll;
            float halfWidth = roll.PaintHalfWidth(speedPerFrame);
            float spacing = Mathf.Max(0.3f, halfWidth * 0.27f);
            Vector3 center = transform.position + forward * _config.RollForward;
            if (_hasStamp)
            {
                Vector3 delta = center - _lastStamp;
                delta.y = 0f;
                if (delta.sqrMagnitude < spacing * spacing)
                {
                    return;
                }
            }
            _lastStamp = center;
            _hasStamp = true;
            LastStampHalfWidth = halfWidth;
            RollStamps++;
            InkSource source = InkShooter.InkSourceOf(Owner);
            float hardness = _config.RollPaintHardness;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            float radius = halfWidth * 0.45f;
            PaintAtFloor(center, radius, 1.4f, hardness, source, false);
            PaintAtFloor(center + right * (halfWidth * 0.55f), radius, 0.9f, hardness, source, true);
            PaintAtFloor(center - right * (halfWidth * 0.55f), radius, 0.9f, hardness, source, true);
            // 兩側濺墨的視覺:每隔一組在左右輪流噴一小團
            _stampIndex++;
            if (InkSplashFxPool.Instance != null && (_stampIndex & 1) == 0)
            {
                float side = (_stampIndex & 2) == 0 ? 1f : -1f;
                InkSplashFxPool.Instance.Spawn(center + right * (side * halfWidth * 0.8f) + Vector3.up * 0.1f,
                    Vector3.up, TeamPalette.ColorOf(Team));
            }
        }

        // 從 point 上方往下找地面塗墨;floorOnly = 只塗水平面(濺墨),reach = 往下找多深(不塗懸崖下)
        private static void PaintAtFloor(Vector3 point, float radius, float reach, float hardness, in InkSource source,
            bool floorOnly)
        {
            if (!Physics.Raycast(point + Vector3.up * 0.8f, Vector3.down, out RaycastHit hit, 0.8f + reach,
                    CombatMasks.Terrain, QueryTriggerInteraction.Ignore))
            {
                return;
            }
            if (floorOnly && hit.normal.y < PaintableSurface.FloorNormalY)
            {
                return;
            }
            InkPainter.PaintSphere(hit.point, radius, hardness, source);
        }

        // 接觸傷害:滾筒軸心的膠囊(半寬 BodyWidthHalf、半徑 BodyRadius)碰到的敵方目標
        private void CheckContacts(Vector3 forward, float speedPerFrame)
        {
            RollerRollParams roll = _config.Roll;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 center = transform.position + forward * _config.RollForward + Vector3.up * roll.BodyRadius;
            float half = Mathf.Max(0f, roll.BodyWidthHalf - roll.BodyRadius);
            int count = Physics.OverlapCapsuleNonAlloc(center - right * half, center + right * half, roll.BodyRadius,
                OverlapBuffer, CombatMasks.Targets, QueryTriggerInteraction.Ignore);
            SplatoonC.Core.Team team = Team;
            float now = Time.time;
            for (int i = 0; i < count; i++)
            {
                Collider collider = OverlapBuffer[i];
                OverlapBuffer[i] = null;
                var target = collider != null ? collider.GetComponentInParent<IDamageable>() : null;
                if (target == null || !target.IsAlive || !DamageRules.CanDamage(team, target)
                    || ReferenceEquals(collider.transform.root, transform.root) || RecentlyHit(target, now))
                {
                    continue;
                }
                RememberHit(target, now);
                var health = target as InklingHealth;
                bool damaged = health == null || !health.Invulnerable;
                Vector3 point = target.ClosestPoint(center);
                target.ApplyDamage(new DamageInfo(roll.Damage, point, team, Owner, DamageKind.Main,
                    SplatoonC.Core.Combat.AttackClass.RollerContact));
                bool splatted = !target.IsAlive;
                ContactHits++;
                LastContactDamage = roll.Damage;
                LastContactSplatted = splatted;
                PlaySound(_config.ContactSound, 1f);
                if (InkSplashFxPool.Instance != null)
                {
                    InkSplashFxPool.Instance.Spawn(point, -forward, TeamPalette.ColorOf(team));
                }
                if (!splatted)
                {
                    ApplyKnockback(target, forward, speedPerFrame, damaged);
                }
            }
        }

        // 沒被擊倒:對手沿推滾方向被推開(KnockBackOpponent)、自己被彈回(KnockBackRollerPlayerDamageOn / Off)
        private void ApplyKnockback(IDamageable target, Vector3 forward, float speedPerFrame, bool damaged)
        {
            RollerRollParams roll = _config.Roll;
            float speedNormalized = roll.SpeedDash > 0f ? speedPerFrame / roll.SpeedDash : 1f;
            if (target is Component component)
            {
                var locomotion = component.GetComponent<PlayerLocomotion>();
                if (locomotion != null)
                {
                    float push = roll.OpponentKnockbackSpeed(speedNormalized);
                    locomotion.AddImpulse(forward * push + Vector3.up * (push * KnockbackModel.UpwardRatio));
                }
            }
            var self = GetComponent<PlayerLocomotion>();
            if (self != null)
            {
                float back = roll.SelfKnockbackSpeed(speedNormalized, damaged);
                self.AddImpulse(-forward * back);
            }
        }

        private bool RecentlyHit(IDamageable target, float now)
        {
            float window = _config.ContactRehitSeconds;
            for (int i = 0; i < MaxContactTargets; i++)
            {
                if (ReferenceEquals(_contactTargets[i], target) && now - _contactTimes[i] < window)
                {
                    return true;
                }
            }
            return false;
        }

        private void RememberHit(IDamageable target, float now)
        {
            for (int i = 0; i < MaxContactTargets; i++)
            {
                if (ReferenceEquals(_contactTargets[i], target))
                {
                    _contactTimes[i] = now;
                    return;
                }
            }
            _contactTargets[_contactNext] = target;
            _contactTimes[_contactNext] = now;
            _contactNext = (_contactNext + 1) % MaxContactTargets;
        }

        private SplatoonC.Core.Team Team => Owner != null ? Owner.Team : SplatoonC.Core.Team.Alpha;

        // ── 聲音 ──

        private void PlaySound(string id, float volume)
        {
            if (string.IsNullOrEmpty(id))
            {
                return;
            }
            GameAudio.PlayFrom(Owner != null && Owner.IsLocalPlayer, id, transform.position + Vector3.up, volume, 0.05f);
        }

        private void StartRollLoop()
        {
            StopRollLoop();
            _rollLoop = GameAudio.StartLoop(_rollLoopSound, transform, Owner == null || !Owner.IsLocalPlayer, 0.7f);
        }

        private void StopRollLoop()
        {
            if (_rollLoop != null)
            {
                _rollLoop.Stop();
                _rollLoop = null;
            }
        }

        // ── bot 用法 ──

        // 伏擊/繞後型:貼身(BotRollCommitDistance 內)按住 → 甩完直接推滾輾過去;
        // 一擊必殺距離內點按甩墨(只在可以開始新的一甩時按一幀,甩完放開 = 不推滾);其餘不按
        public override bool BotAttackIntent(in BotCombatContext context)
        {
            if (_config == null || !context.TargetVisible || !context.Reacted)
            {
                return false;
            }
            if (context.Distance <= _config.BotRollCommitDistance)
            {
                return true;
            }
            if (context.Distance > EffectiveRange * 1.05f)
            {
                // 還在推滾(塗地時發現敵人):繼續滾過去(比跑快、沿路鋪自家墨),進到必殺距離再放開改甩
                return IsRolling && context.Distance <= BotEngageRange;
            }
            // 扇形 ±18°:準心誤差放寬到容許值的 1.5 倍
            return _action == RollerAction.Idle && Time.time >= _nextSwingReady
                   && context.AimError < context.FireTolerance * 1.5f;
        }

        // 交戰時想維持的距離:一擊必殺距離的 45%(Splat 3.2 m、Carbon 2.1 m、Dynamo 5.8 m)
        public override float BotPreferredDistance => EffectiveRange * 0.45f;

        // 伏擊型:看到敵人就在一擊必殺距離 + 8 m 內開始接近(Splat 15.2 m、Carbon 12.7 m、Dynamo 20.9 m;
        // 射擊槍 11.56 × 1.35 = 15.6 m),遠的時候 BotBrain 會在自家墨裡潛游過去,進到必殺距離再甩
        public override float BotEngageRange => EffectiveRange + 8f;

        // 伏擊:自家墨裡潛游到一擊必殺距離的 85% 才浮出(浮出當下就在甩墨範圍內)
        public override float BotSwimApproachDistance => EffectiveRange * 0.85f;

        // 塗地時看腳前(推滾塗的就是腳下那一條)
        public override float BotPaintAimDistance => 1.6f;

        // 塗地時按住推滾:甩一下(Dynamo 21%)之後推滾耗墨很少,有墨就滾;墨低於一甩時先別按(免得起手弱甩)
        public override bool BotPaintIntent(float inkRatio)
        {
            if (_config == null)
            {
                return inkRatio > 0.05f;
            }
            if (IsRolling)
            {
                return inkRatio > 0.02f;
            }
            return inkRatio > Mathf.Max(0.08f, _config.Horizontal.InkConsume);
        }
    }
}

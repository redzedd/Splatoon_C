using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 雙槍(Dualies)主武器(S3 Splat Dualies,Leanny v11.3.0 Maneuver_Normal;research 02 §10、DESIGN_REFERENCE §3.3/§3.5):
    //
    // 開火:按住 ZR 每 RepeatFrame(5F)一發,左右兩把輪流出膛;一般模式兩把槍各朝自己的準星(左右平行各偏半個槍距)射,
    //   S3 散布(地面 2.0°、空中 7.5°、b 0.01 → 0.25);第一發人形 3F、剛從烏賊態出來 13F;每發 0.72%、墨量鎖 20F。
    //   墨彈是 S3 三狀態彈道 InkBullet(2.37 u/F × 3F → 減速 2.3425),傷害 30 → 15(第 7 → 15 飛行幀),4 發擊倒。
    // 閃避翻滾:開火中推方向 + 跳 → 往搖桿方向翻滾(PlayerLocomotion 的 ILocomotionOverride 接管位移):
    //   4F 起手 → 12F 滾 4.0 u → 滑 1.0 u;滾完 4F 後才能開火,再定身 28F(硬直 32F 內不能走、不能變烏賊);
    //   位移結束後可再接一滾(最多連 2 滾),硬直走完次數重置。每滾 7%、墨量鎖 70F、落點塗半寬 1.8。
    //   空中翻滾往下急墜、水平距離很短(過不了溝)。
    // 砲台模式:翻滾停下後雙槍併攏(LapOver):射擊間隔 4F、完全不散布、對人判定 0.335 → TTK 12F;
    //   持續到放開扳機或硬直結束後開始移動。
    // bot:BotAttackIntent 照一般連射規則;交戰中被打或每隔幾秒就沿橫移方向翻滾(與玩家同一條翻滾路徑、同樣耗墨)。
    [DisallowMultipleComponent]
    public sealed class DualiesWeapon : MainWeapon, ILocomotionOverride, IDualWieldPose
    {
        // 音效 id 還沒合成時退回既有音效,不要整把槍無聲
        private const string DefaultFireSound = "shoot_shooter";
        private const string DefaultRollSound = "squid_dive";
        // 幀邊界的浮點誤差容許(0.1 ms)
        private const float TimeEpsilon = 1e-4f;
        private const float BotRollPendingSeconds = 0.3f;

        [SerializeField, Tooltip("雙槍設定資產(Assets/Resources/Weapons/Main_splat_dualies)")]
        private DualiesConfig _config;

        [SerializeField, Tooltip("瞄準射線圖層(排除角色)")]
        private LayerMask _aimMask = ~GameLayers.InklingMask;

        [SerializeField, Tooltip("瞄準射線最大距離(公尺)")]
        private float _aimMaxDistance = 40f;

        private Transform _visual;
        private Vector3 _muzzleLocal = new Vector3(0f, 1.05f, 1.05f);
        private CharacterVisual _characterVisual;
        private DualiesGunPair _guns;
        private DualiesReticle _reticle;
        private InklingHealth _health;
        private string _fireSound = DefaultFireSound;
        private string _rollSound = DefaultRollSound;

        // ── 射擊 ──
        private SpreadModel _spreadModel;
        private bool _wasAirborne;
        private float _takeoffTime;
        private float _nextShotTime;
        private bool _wasTrigger;
        private float _lastSquidTime = float.NegativeInfinity;
        // 下一發由哪隻手打(0 = 左、1 = 右;S3 由右手先 [EST])
        private int _nextHand = 1;
        private int _shotIndex;
        private int _leftShots;
        private int _rightShots;
        private bool _attacking;

        // ── 翻滾 / 砲台 ──
        private bool _rolling;
        private float _rollStartTime;
        private Vector3 _rollDirection = Vector3.right;
        private bool _airRoll;
        private int _rollsInChain;
        private bool _landingPainted;
        private bool _slidePainted;
        private bool _turret;

        // ── bot ──
        private bool _botRollPending;
        private float _botRollPendingUntil;
        private float _nextBotRollAt;

        public DualiesConfig Config => _config;
        public override MainWeaponConfig BaseConfig => _config;
        public override bool IsAttacking => _attacking;

        // 翻滾整段時間軸(含硬直)與開火後 PostDelay 內都不能變烏賊(S3 UnrelaxFrameNoSquid 32、PostDelayFrame 4)
        public override bool BlocksSquid =>
            RollActiveAt(Time.time)
            || (_config != null && Time.time - LastShotTime < _config.PostDelayFrames / 60f - TimeEpsilon);

        // 翻滾時間軸(含硬直)在 now 時是否還沒走完。以時間判斷而不是只看 _rolling:PlayerLocomotion / SquidController
        // 可能在本元件 Update 收尾前就先詢問,時間軸剛好走完的那一幀不該再多吞一次跳躍或多擋一幀烏賊化
        private bool RollActiveAt(float now)
        {
            return _rolling && _config != null
                   && (now - _rollStartTime) * 60f < DodgeRollModel.TotalFrames(_config.Roll) - TimeEpsilon * 60f;
        }

        // ── 射擊狀態(HUD、測試量測)──
        public int ShotsFired => _shotIndex;
        public float LastShotTime { get; private set; } = float.NegativeInfinity;
        // 最後一發:哪隻手(0 左 / 1 右)、出膛點、方向、水平偏角、是否砲台、對人判定半徑
        public int LastHand { get; private set; } = -1;
        public Vector3 LastShotOrigin { get; private set; }
        public Vector3 LastShotDirection { get; private set; }
        public float LastYawOffsetDeg { get; private set; }
        public bool LastShotTurret { get; private set; }
        // 最後一發是否在空中打出(跳射:S3 偏向 b 固定 0.4、偏角 7.5° 於起跳後 25~70F 收回 2.0°)
        public bool LastShotAirborne { get; private set; }
        public float LastBulletRadius { get; private set; }
        public float SpreadBias => _spreadModel.CurrentBias;
        // 這次按住扳機的起點、最後一次處於烏賊態的時間(第一發延遲的量測基準)
        public float TriggerStartTime { get; private set; } = float.NegativeInfinity;
        public float LastSquidTime => _lastSquidTime;
        // 散布倍率:1 = S3。AutoTest 量射程/TTK 時設 0;玩家與 bot 從不改
        public float SpreadMultiplier { get; set; } = 1f;
        public DualiesGunPair Guns => _guns;
        public DualiesReticle Reticle => _reticle;

        // ── 翻滾狀態 ──
        public bool IsTurret => _turret;
        public bool IsRolling => _rolling;
        public int RollsInChain => _rollsInChain;
        public int RollsRemaining => _config != null ? Mathf.Max(0, _config.Roll.MaxRolls - _rollsInChain) : 0;
        public int RollCount { get; private set; }
        public float RollStartTime => _rollStartTime;
        public bool IsAirRoll => _airRoll;
        public Vector3 LastRollDirection => _rollDirection;
        public Vector3 LastRollPaintPoint { get; private set; }
        public int RollPaintCount { get; private set; }

        // 翻滾開始後經過的幀數(沒在翻滾 = −1)
        public float RollFrames => _rolling ? (Time.time - _rollStartTime) * 60f : -1f;
        public DodgeRollPhase RollPhase =>
            _rolling && _config != null ? DodgeRollModel.PhaseAt(_config.Roll, RollFrames) : DodgeRollPhase.None;

        // ── IDualWieldPose(CharacterVisual 讀)──
        public bool DualWieldActive => _config != null && isActiveAndEnabled;
        public int LeftShots => _leftShots;
        public int RightShots => _rightShots;
        public bool TurretStance => _turret;
        public Vector3 RollDirection => _rollDirection;

        public float RollProgress
        {
            get
            {
                if (!_rolling || _config == null)
                {
                    return -1f;
                }
                float end = DodgeRollModel.MoveEndFrame(_config.Roll);
                float f = RollFrames;
                return f < end && end > 0f ? Mathf.Clamp01(f / end) : -1f;
            }
        }

        protected override void Awake()
        {
            base.Awake();
            _health = GetComponent<InklingHealth>();
            _characterVisual = GetComponent<CharacterVisual>();
            CacheVisual();
            if (_config != null)
            {
                ApplyPresentation();
            }
            _nextBotRollAt = Time.time + Random.Range(1f, 3f);
        }

        public override void Configure(MainWeaponConfig config)
        {
            var dualies = config as DualiesConfig;
            if (dualies == null)
            {
                Debug.LogError($"DualiesWeapon:設定型別不符({config})", this);
                return;
            }
            if (dualies != _config)
            {
                _spreadModel = default;
            }
            _config = dualies;
            if (Owner == null)
            {
                CacheReferences();
            }
            if (_health == null)
            {
                _health = GetComponent<InklingHealth>();
            }
            if (_characterVisual == null)
            {
                _characterVisual = GetComponent<CharacterVisual>();
            }
            CacheVisual();
            ApplyPresentation();
        }

        private void CacheVisual()
        {
            if (_visual == null)
            {
                _visual = transform.Find("Visual");
            }
            // 槍口高度與前距沿用角色 Muzzle(與射擊槍同一個出膛平面;左右位置由雙槍自己決定)
            Transform muzzle = FindChild("Muzzle");
            if (muzzle != null && _visual != null && muzzle.parent == _visual)
            {
                _muzzleLocal = muzzle.localPosition;
            }
        }

        // 外觀與音效:兩把槍模(沒有就等角色外觀就緒再掛)、本機玩家的雙準星、音效 id(未合成則退回既有)
        private void ApplyPresentation()
        {
            _fireSound = GameAudio.Has(_config.FireSound) ? _config.FireSound : DefaultFireSound;
            _rollSound = GameAudio.Has(_config.RollSound) ? _config.RollSound : DefaultRollSound;
            if (_guns != null && _guns.SourcePrefab != _config.GunModelPrefab)
            {
                _guns.Detach();
                _guns = null;
            }
            TryAttachGuns();
            if (_reticle == null && Owner != null && Owner.IsLocalPlayer)
            {
                _reticle = DualiesReticle.Create(this);
            }
        }

        private void TryAttachGuns()
        {
            if (_guns != null || _config == null || _config.GunModelPrefab == null)
            {
                return;
            }
            if (_characterVisual == null || !_characterVisual.IsReady)
            {
                return;
            }
            _guns = DualiesGunPair.Attach(this, _config.GunModelPrefab, Owner);
        }

        private void OnDisable()
        {
            // 陣亡(InklingLifecycle 停用主武器)/ 換裝:放棄進行中的翻滾與砲台
            CancelRoll();
            _attacking = false;
            _wasTrigger = false;
            _botRollPending = false;
        }

        private void OnDestroy()
        {
            if (_guns != null)
            {
                _guns.Detach();
                _guns = null;
            }
            if (_reticle != null)
            {
                Destroy(_reticle.gameObject);
                _reticle = null;
            }
        }

        private void Update()
        {
            if (_config == null || Input == null)
            {
                _attacking = false;
                return;
            }
            float now = Time.time;
            if (Squid != null && Squid.IsSquid)
            {
                _lastSquidTime = now;
            }
            if (_guns == null)
            {
                TryAttachGuns();
            }
            else
            {
                _guns.Tick();
            }
            // 比賽流程可能在換裝之後才指定本機玩家:那時再補雙準星
            if (_reticle == null && Owner != null && Owner.IsLocalPlayer)
            {
                _reticle = DualiesReticle.Create(this);
            }

            bool grounded = Controller == null || Controller.isGrounded;
            // 起跳時刻每幀追蹤(不是等到空中第一發才記):S3 跳射偏角從起跳後第 25F 開始收、第 70F 回到地面值,
            // 起跳後隔一段時間才開火的第一發也要用已經收回的偏角
            if (!grounded && !_wasAirborne)
            {
                _takeoffTime = now;
            }
            _wasAirborne = !grounded;
            UpdateRoll(now, grounded);
            // 按著烏賊鍵 = 想潛入:不再出新的一發,PostDelay(4F)過後就讓 SquidController 變身
            //(否則連射間隔 ≤ PostDelay 時會一直擋住變身;敵墨上本來就變不了,照常開火)
            bool wantsSquid = Input.SquidHeld && (Squid == null || !Squid.OnEnemyInk);
            bool trigger = WantsAttack && !wantsSquid;
            _attacking = trigger;
            UpdateTurret(trigger, grounded);
            HandleBotRoll(now, grounded);
            FireLoop(trigger, now);
        }

        // ───────────────────────── 開火 ─────────────────────────

        private void FireLoop(bool trigger, float now)
        {
            if (!trigger)
            {
                // 放開時把時鐘追到現在(不累積欠帳);冷卻未到則保留(點放不能超速)
                if (_nextShotTime < now)
                {
                    _nextShotTime = now;
                }
                _wasTrigger = false;
                return;
            }
            if (!_wasTrigger)
            {
                // 第一發延遲:人形 3F;剛從烏賊態出來則從解除烏賊起算 13F(S3 9 + 4 − 0)
                float humanoidReady = now + _config.FirstShotFrames / 60f;
                float squidReady = _lastSquidTime + _config.SquidFirstShotFrames / 60f;
                _nextShotTime = Mathf.Max(_nextShotTime, Mathf.Max(humanoidReady, squidReady));
                _wasTrigger = true;
                TriggerStartTime = now;
            }
            if (_rolling)
            {
                // 翻滾起手、位移與之後 4F 不能開火(S3 共 20F)
                float resume = _rollStartTime + DodgeRollModel.FireResumeFrame(_config.Roll) / 60f;
                if (now + TimeEpsilon < resume)
                {
                    _nextShotTime = Mathf.Max(_nextShotTime, resume);
                    return;
                }
            }
            float interval = _turret ? _config.TurretFireIntervalSeconds : _config.FireIntervalSeconds;
            int shots = 0;
            while (_nextShotTime <= now + TimeEpsilon && shots < _config.MaxShotsPerFrame)
            {
                Fire();
                _nextShotTime += interval;
                shots++;
            }
            if (_nextShotTime <= now)
            {
                // 補射打頂仍有欠帳:丟棄,從現在起算冷卻
                _nextShotTime = now + interval;
            }
        }

        private void Fire()
        {
            // S3:每發都上墨量鎖;墨不夠 = 乾扣扳機(不出彈、節奏照走、照樣上鎖)
            if (Tank != null)
            {
                Tank.LockRefill(_config.InkLockSeconds);
                if (!Tank.TryConsume(_config.InkConsume))
                {
                    return;
                }
            }
            bool turret = _turret;
            int hand = _nextHand;
            if (!TryComputeShot(hand, turret, out Vector3 origin, out Vector3 direction))
            {
                return;
            }
            _nextHand = 1 - hand;

            float yawOffset = 0f;
            if (!turret)
            {
                // 一般模式:S3 散布(只偏水平;跳射 b 固定 0.4、偏角由 7.5° 在 25~70F 收回 2.0°)
                bool airborne = _wasAirborne;
                float now = Time.time;
                yawOffset = _spreadModel.NextYawOffset(now, _config.Spread, airborne, now - _takeoffTime,
                    Random.value, Random.value) * SpreadMultiplier;
            }
            else if (_config.TurretSwerveDeg > 0f)
            {
                float angle = SpreadModel.AngleFor(_config.TurretSwerveDeg, 0.5f, Random.value);
                yawOffset = (Random.value < 0.5f ? -angle : angle) * SpreadMultiplier;
            }
            direction = Quaternion.AngleAxis(yawOffset, Vector3.up) * direction;

            InkBulletSpec spec = turret ? _config.TurretBullet : _config.Bullet;
            // 墨滴:每發 1 滴(S3 SpawnNum 1.0),SplitNum 種落點相位逐發輪替、鋪在彈能飛到的距離內(沿路填滿)
            if (spec.DripSpacing > 0f)
            {
                spec.DripStartDistance = S3DualiesParams.DropletPhase(_shotIndex, _config.DropletPatterns, spec.DripSpacing,
                    _config.EffectiveRange - _config.Bullet.RadiusForPlayer);
            }
            SplatoonC.Core.Team team = Owner != null ? Owner.Team : SplatoonC.Core.Team.Alpha;
            InkBullet.Fire(spec, origin, direction, team, Owner, 0, InheritVelocity(direction), _config.BulletPrefab);

            LastHand = hand;
            LastShotOrigin = origin;
            LastShotDirection = direction;
            LastYawOffsetDeg = yawOffset;
            LastShotTurret = turret;
            LastShotAirborne = _wasAirborne;
            LastBulletRadius = spec.RadiusForPlayer;
            LastShotTime = Time.time;
            if (hand == 0)
            {
                _leftShots++;
            }
            else
            {
                _rightShots++;
            }
            _shotIndex++;
            GameAudio.PlayFrom(Owner != null && Owner.IsLocalPlayer, _fireSound, origin, 0.9f, 0.06f);
            NotifyAttack();
        }

        // 出膛點(相對 Visual 的朝向;高度/前距 = 角色 Muzzle):一般模式左右各半個槍距,砲台模式併攏且壓低
        public Vector3 MuzzlePoint(int hand, bool turret)
        {
            Quaternion yaw = FacingRotation();
            float half = turret ? _config.TurretMuzzleHalfSpacing : _config.MuzzleHalfSpacing;
            float side = hand == 0 ? -1f : 1f;
            var local = new Vector3(side * half, _muzzleLocal.y - (turret ? _config.TurretMuzzleDrop : 0f), _muzzleLocal.z);
            return transform.position + yaw * local;
        }

        // 兩把槍口中點(準星射線的參考點)
        public Vector3 MuzzleCenter(bool turret)
        {
            Quaternion yaw = FacingRotation();
            var local = new Vector3(0f, _muzzleLocal.y - (turret ? _config.TurretMuzzleDrop : 0f), _muzzleLocal.z);
            return transform.position + yaw * local;
        }

        private Quaternion FacingRotation()
        {
            Transform facing = _visual != null ? _visual : transform;
            return Quaternion.Euler(0f, facing.eulerAngles.y, 0f);
        }

        // 一發的出膛點與方向:TPS 準心射線上「彈飛完有效射程」處的點(射程內先撞到東西用命中點)為準星;
        // 一般模式每把槍的準星在它自己那一側(平行偏移半個槍距 = S3 的兩個準星),砲台模式兩把都對準同一個準星;
        // 再以 S3 三狀態彈道補償減速段的下墜(BulletAim)。
        public bool TryComputeShot(int hand, bool turret, out Vector3 origin, out Vector3 direction)
        {
            origin = Vector3.zero;
            direction = Vector3.forward;
            IAimView view = Owner != null ? Owner.AimView : null;
            if (_config == null || view == null)
            {
                return false;
            }
            origin = MuzzlePoint(hand, turret);
            Vector3 target = AimTarget(view, turret);
            if (!turret)
            {
                float side = hand == 0 ? -1f : 1f;
                target += Quaternion.Euler(0f, view.Yaw, 0f) * Vector3.right * (side * _config.MuzzleHalfSpacing);
            }
            direction = BulletAim.CompensatedDirection(_config.Bullet.Motion, origin, target);
            return true;
        }

        // 準心目標點(兩把槍準星的中心)
        public Vector3 AimTarget(IAimView view, bool turret)
        {
            Vector3 center = MuzzleCenter(turret);
            Vector3 camPos = view.AimOrigin;
            Vector3 camForward = view.AimForward;
            // 取樣上限 = 視點到槍口 + 彈在 ShotGuideFrame 內飛的距離(有效射程 − 對人半徑)
            float reach = Mathf.Min(Vector3.Distance(camPos, center) + _config.EffectiveRange - _config.Bullet.RadiusForPlayer,
                _aimMaxDistance);
            var ray = new Ray(camPos, camForward);
            return Physics.Raycast(ray, out RaycastHit hit, reach, _aimMask, QueryTriggerInteraction.Ignore)
                ? hit.point
                : ray.GetPoint(reach);
        }

        // 玩家速度疊加到墨彈(S3 前向 ×2.0、橫向 ×0.4;只算水平)
        private Vector3 InheritVelocity(Vector3 direction)
        {
            if (Controller == null || !Controller.enabled)
            {
                return Vector3.zero;
            }
            Vector3 velocity = Controller.velocity;
            velocity.y = 0f;
            Vector3 forward = new Vector3(direction.x, 0f, direction.z);
            if (forward.sqrMagnitude < 1e-6f || velocity.sqrMagnitude < 1e-6f)
            {
                return Vector3.zero;
            }
            forward.Normalize();
            float along = Vector3.Dot(velocity, forward);
            Vector3 side = velocity - forward * along;
            return forward * (along * _config.InheritForwardRate) + side * _config.InheritSideRate;
        }

        // ───────────────────────── 翻滾 ─────────────────────────

        // ILocomotionOverride:開火中推方向 + 跳 = 翻滾;翻滾/硬直中的跳一律吞掉(可接滾時改成下一滾)
        public bool InterceptJump(Vector2 moveInput, float cameraYawDeg, bool grounded)
        {
            if (_config == null || !isActiveAndEnabled)
            {
                return false;
            }
            DodgeRollParams p = _config.Roll;
            float now = Time.time;
            if (_rolling && !RollActiveAt(now))
            {
                // 時間軸已走完、Update 還沒收尾:先結束這一串(次數重置),這次按鍵照新的一滾/一般跳處理
                _rolling = false;
                _rollsInChain = 0;
            }
            if (_rolling)
            {
                float frames = (now - _rollStartTime) * 60f;
                if (DodgeRollModel.CanChainAt(p, frames) && _rollsInChain < p.MaxRolls && WantsAttack
                    && moveInput.magnitude >= p.MinStick)
                {
                    TryStartRoll(StickToWorld(moveInput, cameraYawDeg), grounded, now);
                }
                return true;
            }
            if (!WantsAttack || moveInput.magnitude < p.MinStick)
            {
                return false;
            }
            return TryStartRoll(StickToWorld(moveInput, cameraYawDeg), grounded, now);
        }

        public bool TryGetControlledMotion(float time, float deltaTime, bool grounded, out Vector3 horizontalVelocity,
            out float forcedVerticalSpeed)
        {
            horizontalVelocity = Vector3.zero;
            forcedVerticalSpeed = float.NaN;
            if (!_rolling || _config == null)
            {
                return false;
            }
            DodgeRollParams p = _config.Roll;
            float frames = (time - _rollStartTime) * 60f;
            if (frames >= DodgeRollModel.TotalFrames(p))
            {
                return false;
            }
            // 本幀位移 = 時間軸位移差(總位移與幀率無關);空中翻滾水平距離縮短、往下急墜
            float previous = Mathf.Max(0f, frames - deltaTime * 60f);
            float scale = _airRoll ? p.AirDistanceScale : 1f;
            float step = (DodgeRollModel.DistanceAt(p, frames) - DodgeRollModel.DistanceAt(p, previous)) * scale;
            if (deltaTime > 1e-6f)
            {
                horizontalVelocity = _rollDirection * (step / deltaTime);
            }
            if (_airRoll && !grounded && frames >= p.StartupFrames)
            {
                forcedVerticalSpeed = -p.AirSlamSpeed * 60f;
            }
            return true;
        }

        public void OnLocomotionReset()
        {
            CancelRoll();
        }

        // 開始一滾(玩家跳鍵攔截與 bot 共用):扣 7%、上 70F 墨量鎖;墨不夠就不滾(照樣上鎖,呼叫端改成一般跳)
        private bool TryStartRoll(Vector3 direction, bool grounded, float now)
        {
            DodgeRollParams p = _config.Roll;
            if (Tank != null)
            {
                Tank.LockRefill(p.InkRecoverStopFrames / 60f);
                if (!Tank.TryConsume(p.InkConsume))
                {
                    return false;
                }
            }
            direction.y = 0f;
            _rollDirection = direction.sqrMagnitude > 1e-6f ? direction.normalized : FacingRotation() * Vector3.right;
            _rolling = true;
            _rollStartTime = now;
            _airRoll = !grounded;
            _rollsInChain++;
            RollCount++;
            _landingPainted = false;
            _slidePainted = false;
            _turret = false;
            _botRollPending = false;
            GameAudio.PlayFrom(Owner != null && Owner.IsLocalPlayer, _rollSound, transform.position + Vector3.up * 0.4f,
                0.9f, 0.05f);
            return true;
        }

        // 時間軸推進:位移結束後在落點塗墨、架起砲台;滑行停下再塗一筆;硬直走完重置可翻滾次數
        private void UpdateRoll(float now, bool grounded)
        {
            if (!_rolling)
            {
                return;
            }
            DodgeRollParams p = _config.Roll;
            float frames = (now - _rollStartTime) * 60f;
            float moveEnd = DodgeRollModel.MoveEndFrame(p);
            if (frames >= moveEnd)
            {
                if (!_landingPainted && grounded)
                {
                    PaintRollPool();
                    _landingPainted = true;
                }
                else if (_landingPainted && !_slidePainted && frames >= moveEnd + p.SlideFrames && grounded)
                {
                    PaintRollPool();
                    _slidePainted = true;
                }
                if (!_turret && WantsAttack)
                {
                    _turret = true;
                }
            }
            if (frames >= DodgeRollModel.TotalFrames(p))
            {
                // 硬直結束:可以走了、翻滾次數重置(砲台模式在 UpdateTurret 裡等玩家移動或放開扳機才解除)
                _rolling = false;
                _rollsInChain = 0;
            }
        }

        // 落點塗一灘(SplashSlideParam.PaintWidthHalf 1.8;滑行起點與終點各一筆 → 沿滑行方向的長圓)
        private void PaintRollPool()
        {
            Vector3 feet = transform.position;
            if (Physics.Raycast(feet + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 3f, CombatMasks.Terrain,
                    QueryTriggerInteraction.Ignore))
            {
                InkPainter.PaintSphere(hit.point, _config.Roll.PaintWidthHalf, 0.7f, InkShooter.InkSourceOf(Owner));
                LastRollPaintPoint = hit.point;
                RollPaintCount++;
                if (InkSplashFxPool.Instance != null)
                {
                    SplatoonC.Core.Team team = Owner != null ? Owner.Team : SplatoonC.Core.Team.Alpha;
                    InkSplashFxPool.Instance.Spawn(hit.point + Vector3.up * 0.05f, Vector3.up, TeamPalette.ColorOf(team));
                }
            }
        }

        private void CancelRoll()
        {
            _rolling = false;
            _rollsInChain = 0;
            _turret = false;
        }

        // 砲台模式:翻滾停下且按著扳機時架起;放開扳機、或硬直結束後開始移動/離地就解除
        private void UpdateTurret(bool trigger, bool grounded)
        {
            if (!_turret)
            {
                return;
            }
            if (!trigger)
            {
                _turret = false;
                return;
            }
            if (!_rolling && (!grounded || Input.MoveInput.sqrMagnitude > 0.01f))
            {
                _turret = false;
            }
        }

        private Vector3 StickToWorld(Vector2 stick, float yawDeg)
        {
            Vector3 world = Quaternion.Euler(0f, yawDeg, 0f) * new Vector3(stick.x, 0f, stick.y);
            return world.sqrMagnitude > 1e-6f ? world.normalized : FacingRotation() * Vector3.right;
        }

        // ───────────────────────── bot ─────────────────────────

        // 連射規則同射擊槍;另外在交戰中決定要不要翻滾:剛被打、或每隔 2.5~5 秒(S3 雙槍的邊打邊滾)
        public override bool BotAttackIntent(in BotCombatContext context)
        {
            bool fire = base.BotAttackIntent(context);
            if (_config == null || !context.TargetVisible || !context.Reacted || _rolling
                || context.Distance > EffectiveRange * 1.2f)
            {
                return fire;
            }
            float now = Time.time;
            bool hurt = _health != null && now - _health.LastDamageTime <= _config.BotRollWhenHurtWindow;
            bool ink = Tank == null || Tank.Normalized * Tank.CapacityScale >= _config.Roll.InkConsume + 0.1f;
            if ((hurt || now >= _nextBotRollAt) && ink)
            {
                _botRollPending = true;
                _botRollPendingUntil = now + BotRollPendingSeconds;
                Vector2 interval = _config.BotRollInterval;
                _nextBotRollAt = now + Random.Range(interval.x, interval.y);
            }
            // 要翻滾時扳機必須按著(S3:開火中才能滾)
            return fire || _botRollPending;
        }

        // bot 的翻滾:沿目前的橫移方向(沒在動就隨機往左或右),只在地上滾
        private void HandleBotRoll(float now, bool grounded)
        {
            if (!_botRollPending)
            {
                return;
            }
            if (now > _botRollPendingUntil)
            {
                _botRollPending = false;
                return;
            }
            if (_rolling || !grounded || !WantsAttack)
            {
                return;
            }
            IAimView view = Owner != null ? Owner.AimView : null;
            float yaw = view != null ? view.Yaw : FacingRotation().eulerAngles.y;
            Vector2 stick = Input.MoveInput;
            if (stick.sqrMagnitude < 0.1f)
            {
                stick = new Vector2(Random.value < 0.5f ? -1f : 1f, 0f);
            }
            TryStartRoll(StickToWorld(stick, yaw), true, now);
            _botRollPending = false;
        }

        // 塗地時不翻滾;交戰距離沿用 0.72 × 有效射程
        public bool BotRollPending => _botRollPending;
    }
}

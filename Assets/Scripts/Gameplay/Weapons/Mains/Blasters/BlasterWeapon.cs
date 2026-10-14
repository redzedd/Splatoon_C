using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 爆破槍(Blaster / Luna Blaster)主武器(S3 Leanny v11.3.0 Blaster_Middle / Blaster_Short;research 02 §12、
    // DESIGN_REFERENCE §3.3/§3.5/§3.6):
    //
    // 扳機(BlasterShotClock):按下 = 確定一發(點一下也會打出去),預備 14F 後出膛(剛從烏賊態出來 24F);按住每 RepeatFrame
    //   一發(Blaster 50F = 1.2 發/秒、Luna 40F),每發前都有 14F 預備——下一發最早在出膛 36F / 26F 後開始預備,那時還按著
    //   (或 10F 先行入力內按過)才確定,射完就放開不會被下一發綁住;出膛後 PostDelay(22F / 26F)內不能變烏賊、不能丟副武器,
    //   預備中也不能。
    //   預備 + 按住 + 硬直期間走速 = 開火走速(0.045 / 0.05 u/F = 2.7 / 3.0 m/s)。每發 10% / 7.5%、墨量鎖 57F / 55F。
    // 墨彈:S3 三狀態彈道(0.945 u/F × 9F → 減速,Luna 0.87 × 7F),直擊 125(不衰減,一擊必殺);第 BurstFrame 幀
    //   (13F / 11F ≈ 9.86 / 7.35 m)空爆,途中撞到地形 / 目標提早爆;爆風線性 70 → 50(1.025 → 3.385 m;Luna 1.7 → 3.9),
    //   撞地形時爆風半徑 × 0.4234(Luna 0.4166);直擊目標不再吃同一發的爆風;擊退 700。以上由 I1 的 InkBullet / Explosion 結算。
    // 散布:地面 0°(完全不散);跳射偏角上限 10°(Luna 5.5°)、偏向 b 固定 0.5,起跳 25F 後開始收、70F 回到 0°。
    // 塗墨:腳前一滴(每發都有,S3 WidthHalfNearest)、沿路每 1.5 m 一滴(最多 7 / 6 滴,InkBullet 墨滴)、
    //   爆炸塗墨依結束方式:空爆 = 爆心球形 + 正下方地面大圓(SplashPaintRadius / SplashDropPaintRadius)、
    //   撞地形 = 較小的一組(…ShotColHit);爆心水平四周 3 m 內的牆噴一圈(SplashWallDropPaintParam 1.2)。
    //   墨彈本身落地不塗橢圓(S3 PaintParam 全 0),所以 InkBullet 規格的落點塗墨關閉,由本元件依 InkBullet.Impacted 補上。
    // bot:空爆距離以內瞄目標腳下的地面(直擊或腳邊撞地爆)→ 接近空爆距離瞄胸口(空爆包住目標),並補上預備 14F 的提前量;
    //   目標躲到掩體後 2 秒內,找「越過掩體、在目標上方空爆」的瞄準點照樣開火;交戰距離 ≈ 0.85 × 空爆距離;
    //   塗地時瞄空爆點 1.5 倍遠的地面(彈貼地空爆,地面塗墨最大)。
    [DisallowMultipleComponent]
    public sealed class BlasterWeapon : MainWeapon, IGunStancePose
    {
        // 音效 id 還沒合成時退回既有音效,不要整把槍無聲
        private const string DefaultFireSound = "shoot_shooter";
        private const string DefaultBurstSound = "bomb_explode";
        private const float DefaultBurstVolume = 0.7f;
        private const int MaxTrackedShells = 16;
        private const int WallRayCount = 6;
        private const float CoverPlanInterval = 0.15f;
        private const float CoverMemorySeconds = 2f;
        // bot 為預備時間補的提前量上限(公尺;冷卻中不要瞄到很遠的地方)
        private const float MaxWindupLead = 2.5f;
        // 腳前墨滴往下找地面的距離(跳射時墨滴落到腳下的地面)
        private const float NearestReach = 12f;

        [SerializeField, Tooltip("爆破槍設定資產(Assets/Resources/Weapons/Main_blaster / Main_luna_blaster)")]
        private BlasterConfig _config;

        [SerializeField, Tooltip("瞄準射線圖層(排除角色)")]
        private LayerMask _aimMask = ~GameLayers.InklingMask;

        [SerializeField, Tooltip("瞄準射線最大距離(公尺)")]
        private float _aimMaxDistance = 40f;

        private Transform _muzzle;
        private CharacterVisual _characterVisual;
        private BlasterGunModel _gun;
        private string _fireSound = DefaultFireSound;
        // 執行期的墨彈規格(設定的複本;爆炸音效 id 未合成時換成既有音效)
        private InkBulletSpec _spec;
        private BlasterTiming _timing;

        private BlasterShotClock _clock;
        private SpreadModel _spreadModel;
        private bool _wasAirborne;
        private float _takeoffTime;
        private float _lastSquidTime = float.NegativeInfinity;
        private bool _attacking;
        private int _shotIndex;

        // 自己射出、還在飛的墨彈(attackId 環形表;InkBullet.Impacted 是全域事件,靠它認出自己的彈)
        private readonly int[] _shellIds = new int[MaxTrackedShells];
        private int _shellCursor;
        private InkBulletImpactHandler _onImpact;
        private bool _subscribed;

        // bot:掩體空爆的瞄準計畫(每 0.15 秒重算)
        private bool _coverShotPlanned;
        private float _coverPlanUntil;
        private Vector3 _coverAimPoint;

        public BlasterConfig Config => _config;
        public override MainWeaponConfig BaseConfig => _config;

        // 攻擊中(走速 = 開火走速、角色舉槍):按著扳機、預備中、出膛後硬直中
        public override bool IsAttacking => _attacking;

        // 預備中(已確定一發、還沒出膛)與出膛後 PostDelay 內不能變烏賊 / 丟副武器
        public override bool BlocksSquid => _config != null && (_clock.Pending || _clock.InPostDelay(Time.time, _timing));
        public override bool BlocksSubWeapon => BlocksSquid;

        // ── 射擊狀態(HUD、測試量測)──
        public int ShotsFired => _shotIndex;
        // 最後一發實際出膛的時刻(幀時間)與排定的出膛時刻(節奏以排定時刻計)
        public float LastShotTime { get; private set; } = float.NegativeInfinity;
        public float LastScheduledShotTime => _clock.LastShotTime;
        // 這一發按下(確定)的時刻、最後一次處於烏賊態的時刻(第一發延遲的量測基準)
        public float TriggerStartTime => _clock.CommitTime;
        public float LastSquidTime => _lastSquidTime;
        public Vector3 LastShotOrigin { get; private set; }
        public Vector3 LastShotDirection { get; private set; }
        public float LastYawOffsetDeg { get; private set; }
        public bool LastShotAirborne { get; private set; }
        public int LastAttackId { get; private set; }
        // 墨不夠的乾扣扳機次數(節奏照走、不出彈)
        public int DryFires { get; private set; }
        // 散布倍率:1 = S3。AutoTest 量射程時設 0;玩家與 bot 從不改
        public float SpreadMultiplier { get; set; } = 1f;
        public bool IsWindingUp => _clock.Pending;
        // 預備進度 0 → 1(沒在預備 = −1)、冷卻進度 0 → 1(1 = 可再出膛)
        public float WindupProgress => _clock.WindupProgress(Time.time);
        public float CooldownProgress => _config != null ? _clock.CooldownProgress(Time.time, _timing) : 1f;
        public bool InPostDelay => _config != null && _clock.InPostDelay(Time.time, _timing);
        public float BurstDistance => _config != null ? _config.BurstDistance : 0f;
        public BlasterGunModel Gun => _gun;

        // ── 爆炸 / 塗墨統計(測試量測)──
        public int BurstCount { get; private set; }
        public int AirBurstCount { get; private set; }
        public int TerrainBurstCount { get; private set; }
        public int DirectHits { get; private set; }
        // 被敵方 Ink Vac 吸走的彈數(D2;吸走 = 不爆炸、不塗墨,不算一次爆發)
        public int AbsorbedCount { get; private set; }
        public InkBulletImpact LastBurst { get; private set; }
        public float LastBurstPaintedArea { get; private set; }
        public Vector3 LastDropPaintPoint { get; private set; }
        public float LastDropPaintRadius { get; private set; }
        public int NearestPaintCount { get; private set; }
        public Vector3 LastNearestPaintPoint { get; private set; }
        public bool CoverShotPlanned => _coverShotPlanned;
        public Vector3 CoverAimPoint => _coverAimPoint;

        // ── IGunStancePose(CharacterVisual 讀)──
        public bool GunStanceActive => _config != null && isActiveAndEnabled && _gun != null;
        public Vector3 StanceGripOffset => _config.GripOffset;
        public Vector3 StanceSupportOffset => _config.SupportOffset;
        public float StanceRecoilScale => _config.RecoilScale;
        public float StanceWindup => _clock.WindupProgress(Time.time);

        protected override void Awake()
        {
            base.Awake();
            _characterVisual = GetComponent<CharacterVisual>();
            _muzzle = FindChild("Muzzle");
            _onImpact = OnBulletImpact;
            if (_config != null)
            {
                ApplyConfig();
            }
        }

        public override void Configure(MainWeaponConfig config)
        {
            var blaster = config as BlasterConfig;
            if (blaster == null)
            {
                Debug.LogError($"BlasterWeapon:設定型別不符({config})", this);
                return;
            }
            if (blaster != _config)
            {
                _spreadModel = default;
                _clock.Cancel();
            }
            _config = blaster;
            if (Owner == null)
            {
                CacheReferences();
            }
            if (_characterVisual == null)
            {
                _characterVisual = GetComponent<CharacterVisual>();
            }
            if (_muzzle == null)
            {
                _muzzle = FindChild("Muzzle");
            }
            if (_onImpact == null)
            {
                _onImpact = OnBulletImpact;
            }
            ApplyConfig();
        }

        // 設定 → 執行期複本、音效 id(未合成則退回既有)、槍模、訂閱墨彈事件
        private void ApplyConfig()
        {
            _timing = _config.Timing;
            _spec = _config.Bullet;
            if (!GameAudio.Has(_spec.Explosion.Sound))
            {
                _spec.Explosion.Sound = DefaultBurstSound;
                _spec.Explosion.SoundVolume = DefaultBurstVolume;
            }
            _fireSound = GameAudio.Has(_config.FireSound) ? _config.FireSound : DefaultFireSound;
            if (_gun != null && _gun.SourcePrefab != _config.GunModelPrefab)
            {
                _gun.Detach();
                _gun = null;
            }
            TryAttachGun();
            if (!_subscribed)
            {
                InkBullet.Impacted += _onImpact;
                _subscribed = true;
            }
        }

        private void TryAttachGun()
        {
            if (_gun != null || _config == null || _config.GunModelPrefab == null)
            {
                return;
            }
            if (_characterVisual == null || !_characterVisual.IsReady)
            {
                return;
            }
            _gun = BlasterGunModel.Attach(this, _config.GunModelPrefab, Owner);
        }

        private void OnDisable()
        {
            // 陣亡(InklingLifecycle 停用主武器)/ 換裝:放棄還沒出膛的那一發(已射出的彈照樣飛、照樣爆、照樣塗)
            _clock.Cancel();
            _attacking = false;
            _coverShotPlanned = false;
        }

        private void OnDestroy()
        {
            if (_subscribed)
            {
                InkBullet.Impacted -= _onImpact;
                _subscribed = false;
            }
            if (_gun != null)
            {
                _gun.Detach();
                _gun = null;
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
            bool squid = Squid != null && Squid.IsSquid;
            if (squid)
            {
                _lastSquidTime = now;
            }
            if (_gun == null)
            {
                TryAttachGun();
            }
            else
            {
                _gun.Tick();
            }

            bool grounded = Controller == null || Controller.isGrounded;
            // 起跳時刻每幀追蹤:S3 跳射偏角從起跳後第 25F 開始收、第 70F 回到地面值
            if (!grounded && !_wasAirborne)
            {
                _takeoffTime = now;
            }
            _wasAirborne = !grounded;

            // 已確定的那一發:變烏賊(不該發生,預備中擋著)或大招佔用主武器 → 放棄
            if (_clock.Pending && (squid || SpecialBlocksMain))
            {
                _clock.Cancel();
            }
            // 按著烏賊鍵 = 想潛入:不再確定新的一發,硬直過後就讓 SquidController 變身(敵墨上變不了,照常開火)
            bool wantsSquid = Input.SquidHeld && (Squid == null || !Squid.OnEnemyInk);
            bool trigger = WantsAttack && !wantsSquid;
            if (_clock.Tick(trigger, now, _lastSquidTime, _timing, out _))
            {
                Fire();
            }
            _attacking = trigger || _clock.Pending || _clock.InPostDelay(now, _timing);
        }

        private bool SpecialBlocksMain
        {
            get
            {
                SpecialWeapon special = SpecialGauge != null ? SpecialGauge.ActiveSpecial : null;
                return special != null && special.BlocksMainWeapon;
            }
        }

        // ───────────────────────── 出膛 ─────────────────────────

        private void Fire()
        {
            // S3:每發都上墨量鎖;墨不夠 = 乾扣扳機(不出彈、節奏照走、照樣上鎖)
            if (Tank != null)
            {
                Tank.LockRefill(_config.InkLockSeconds);
                if (!Tank.TryConsume(_config.InkConsume))
                {
                    DryFires++;
                    return;
                }
            }
            if (!TryComputeShot(out Vector3 origin, out Vector3 direction))
            {
                return;
            }
            float now = Time.time;
            // 散布:地面 0°;空中 b 固定 0.5、偏角由 10°(Luna 5.5°)在起跳後 25~70F 收回 0°(只偏水平)
            bool airborne = _wasAirborne;
            float yawOffset = _spreadModel.NextYawOffset(now, _config.Spread, airborne, now - _takeoffTime,
                Random.value, Random.value) * SpreadMultiplier;
            direction = Quaternion.AngleAxis(yawOffset, Vector3.up) * direction;

            int attackId = CombatLedger.NewAttackId();
            TrackShell(attackId);
            SplatoonC.Core.Team team = Owner != null ? Owner.Team : SplatoonC.Core.Team.Alpha;
            InkBullet.Fire(_spec, origin, direction, team, Owner, attackId, InheritVelocity(direction),
                _config.BulletPrefab);
            PaintNearest(direction);

            LastShotOrigin = origin;
            LastShotDirection = direction;
            LastYawOffsetDeg = yawOffset;
            LastShotAirborne = airborne;
            LastAttackId = attackId;
            LastShotTime = now;
            _shotIndex++;
            if (InkSplashFxPool.Instance != null)
            {
                Vector3 fxAt = _gun != null && _gun.Muzzle != null && _gun.Visible ? _gun.Muzzle.position : origin;
                InkSplashFxPool.Instance.Spawn(fxAt, direction, TeamPalette.ColorOf(team));
            }
            GameAudio.PlayFrom(Owner != null && Owner.IsLocalPlayer, _fireSound, origin, 1f, 0.05f);
            NotifyAttack();
        }

        // 一發的出膛點與方向:TPS 準心射線上「彈飛到空爆點」處(射程內先撞到東西用命中點)為準星,
        // 再以 S3 三狀態彈道補償減速段的下墜(BulletAim)——彈會穿過準星;準星比空爆點遠時就在準心線附近空爆
        public bool TryComputeShot(out Vector3 origin, out Vector3 direction)
        {
            origin = Vector3.zero;
            direction = Vector3.forward;
            IAimView view = Owner != null ? Owner.AimView : null;
            if (_config == null || view == null || _muzzle == null)
            {
                return false;
            }
            origin = _muzzle.position;
            Vector3 camPos = view.AimOrigin;
            Vector3 camForward = view.AimForward;
            float reach = Mathf.Min(Vector3.Distance(camPos, origin) + _config.BurstDistance, _aimMaxDistance);
            var ray = new Ray(camPos, camForward);
            Vector3 target = Physics.Raycast(ray, out RaycastHit hit, reach, _aimMask, QueryTriggerInteraction.Ignore)
                ? hit.point
                : ray.GetPoint(reach);
            direction = BulletAim.CompensatedDirection(_spec.Motion, origin, target);
            return true;
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

        // 腳前墨滴(S3 爆破槍每發都有;SpawnNearestLength 處、半寬 WidthHalfNearest):從角色前方往下落到地面
        private void PaintNearest(Vector3 direction)
        {
            Vector3 flat = new Vector3(direction.x, 0f, direction.z);
            if (flat.sqrMagnitude < 1e-6f || _config.NearestPaintRadius <= 0f)
            {
                return;
            }
            Vector3 probe = transform.position + flat.normalized * _config.NearestDistance + Vector3.up * 0.5f;
            if (Physics.Raycast(probe, Vector3.down, out RaycastHit hit, NearestReach, CombatMasks.Terrain,
                    QueryTriggerInteraction.Ignore))
            {
                InkPainter.PaintSphere(hit.point, _config.NearestPaintRadius, 0.7f, InkShooter.InkSourceOf(Owner));
                LastNearestPaintPoint = hit.point;
                NearestPaintCount++;
            }
        }

        // ───────────────────────── 爆炸塗墨 ─────────────────────────

        private void TrackShell(int attackId)
        {
            _shellIds[_shellCursor] = attackId;
            _shellCursor = (_shellCursor + 1) % MaxTrackedShells;
        }

        private bool TakeShell(int attackId)
        {
            if (attackId == 0)
            {
                return false;
            }
            for (int i = 0; i < MaxTrackedShells; i++)
            {
                if (_shellIds[i] == attackId)
                {
                    _shellIds[i] = 0;
                    return true;
                }
            }
            return false;
        }

        // 全域墨彈事件:只處理自己的彈的結束(空爆 / 撞地形 / 直擊 / 打到屏障)
        private void OnBulletImpact(in InkBulletImpact impact)
        {
            if (impact.End == InkBulletEnd.Pierce || !TakeShell(impact.AttackId) || _config == null)
            {
                return;
            }
            if (impact.End == InkBulletEnd.Absorbed)
            {
                // 被敵方 Ink Vac 吸走(D2):S3 吸走的彈不爆炸、不塗墨 → 不補爆發塗墨 / 閃光,只記數
                AbsorbedCount++;
                return;
            }
            bool terrain = impact.End == InkBulletEnd.Terrain;
            BurstCount++;
            if (terrain)
            {
                TerrainBurstCount++;
            }
            else if (impact.End == InkBulletEnd.AirBurst)
            {
                AirBurstCount++;
            }
            SplatoonC.Core.Team team = impact.Team;
            Color color = TeamPalette.ColorOf(team);
            // 爆風範圍閃光:外圈半徑(撞地形 × 0.4234),讓人看得出炸到多大
            float scale = terrain && _spec.TerrainHitExplosionScale > 0f ? _spec.TerrainHitExplosionScale : 1f;
            ExplosionFlash.Spawn(impact.Point, _spec.Explosion.FarRadius * scale, color);
            LastBurstPaintedArea = _spec.NoPaint ? 0f : PaintBurst(impact.Point, terrain, team, color);
            LastBurst = impact;

            bool direct = impact.End == InkBulletEnd.Target && impact.Damage > 0f
                          && (impact.Target is InklingHealth || impact.Target is TargetDummy);
            if (direct)
            {
                DirectHits++;
                if (Owner != null && Owner.IsLocalPlayer)
                {
                    GameAudio.Play2D(_config.DirectHitSound, 0.9f);
                }
            }
        }

        // 爆心附近球形塗墨 + 正下方地面大圓 + 水平四周的牆;空爆 / 撞地形各一組半徑(S3 BlasterBurstParam)
        private float PaintBurst(Vector3 center, bool terrain, SplatoonC.Core.Team team, Color color)
        {
            // 擁有者可能已經被擊倒 / 換裝:墨照樣算在這一隊(沒有擁有者就不回報塗地點數)
            InkSource source = Owner != null ? Owner.InkSource(true) : new InkSource(team);
            float sphere = terrain ? _config.TerrainPaintRadius : _config.BurstPaintRadius;
            float drop = terrain ? _config.TerrainDropPaintRadius : _config.BurstDropPaintRadius;
            float area = InkPainter.PaintSphere(center, sphere, 0.7f, source);
            InkSplashFxPool fx = InkSplashFxPool.Instance;

            // 正下方地面:空爆的墨往下落(落差上限 BurstDropReach);撞地形只塗緊鄰的地面
            float reach = terrain ? drop + 0.5f : _config.BurstDropReach;
            LastDropPaintRadius = 0f;
            if (Physics.Raycast(center + Vector3.up * 0.1f, Vector3.down, out RaycastHit ground, reach + 0.1f,
                    CombatMasks.Terrain, QueryTriggerInteraction.Ignore))
            {
                area += InkPainter.PaintSphere(ground.point, drop, 0.7f, source);
                LastDropPaintPoint = ground.point;
                LastDropPaintRadius = drop;
                if (fx != null)
                {
                    fx.Spawn(ground.point + Vector3.up * 0.05f, ground.normal, color);
                }
            }

            // 水平四周的牆(爆風外圈以內):噴一小片(SplashWallDropPaintParam.PaintRadiusShock)
            float wallReach = _spec.Explosion.FarRadius * (terrain ? Mathf.Max(0.3f, _spec.TerrainHitExplosionScale) : 1f);
            float offset = Random.value * 360f;
            for (int i = 0; i < WallRayCount; i++)
            {
                float angle = (offset + i * 360f / WallRayCount) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                if (!Physics.Raycast(center, dir, out RaycastHit wall, wallReach, CombatMasks.Terrain,
                        QueryTriggerInteraction.Ignore) || wall.normal.y >= PaintableSurface.FloorNormalY)
                {
                    continue;
                }
                area += InkPainter.PaintSphere(wall.point, _config.WallPaintRadius, 0.7f, source);
                if (fx != null && i < 2)
                {
                    fx.Spawn(wall.point, wall.normal, color);
                }
            }
            return area;
        }

        // ───────────────────────── bot ─────────────────────────

        // 開火:看得到 → 反應過、在空爆 + 外圈八成以內、準心對上(爆風寬容,容許 1.3 倍);
        // 看不到(剛躲到掩體後)→ 只有算得出「越過掩體空爆」的瞄準點時才開(BotAimPoint 先算好)
        public override bool BotAttackIntent(in BotCombatContext context)
        {
            if (_config == null || !context.Reacted)
            {
                return false;
            }
            if (!context.TargetVisible)
            {
                return _coverShotPlanned && context.AimError < context.FireTolerance * 1.5f;
            }
            float reach = _config.BurstDistance + _config.Explosion.FarRadius * 0.8f;
            return context.Distance <= reach && context.AimError < context.FireTolerance * 1.3f;
        }

        // 瞄準點:看得到 → 近距離腳邊、接近空爆距離胸口(BlasterAim.AimHeight);
        // 剛躲到掩體後 → 掩體上方空爆的瞄準點(算不出來就照 BotBrain 的預設,不開火)
        public override Vector3 BotAimPoint(in BotAimContext context)
        {
            if (_config == null)
            {
                return context.AimPoint;
            }
            float burst = _config.BurstDistance;
            if (context.TargetVisible)
            {
                _coverShotPlanned = false;
                // BotBrain 的提前量只算彈飛行時間;爆破槍出膛前還有預備(14F)/ 冷卻,再補「到出膛為止」目標會走多遠
                Vector3 velocity = context.TargetVelocity;
                velocity.y = 0f;
                float wait = _clock.SecondsUntilShot(Time.time, _lastSquidTime, _timing);
                Vector3 lead = Vector3.ClampMagnitude(velocity * wait, MaxWindupLead);
                return context.TargetFeet + lead + Vector3.up * BlasterAim.AimHeight(context.Distance, burst);
            }
            if (context.SecondsSinceSeen > CoverMemorySeconds)
            {
                _coverShotPlanned = false;
                return context.AimPoint;
            }
            float now = Time.time;
            if (now >= _coverPlanUntil)
            {
                _coverPlanUntil = now + CoverPlanInterval;
                _coverShotPlanned = PlanCoverShot(context.TargetFeet, out _coverAimPoint);
            }
            return _coverShotPlanned ? _coverAimPoint : context.AimPoint;
        }

        // 掩體空爆:由低到高試幾個「目標腳底上方」的瞄準點,取第一個 槍口 → 空爆點 沒被地形擋、
        // 空爆點離目標胸口在外圈半徑內、且空爆點 → 目標胸口的爆風視線沒被擋的
        public bool PlanCoverShot(Vector3 targetFeet, out Vector3 aimPoint)
        {
            aimPoint = targetFeet + Vector3.up * BlasterAim.ChestAimHeight;
            if (_config == null || _muzzle == null)
            {
                return false;
            }
            Vector3 origin = _muzzle.position;
            Vector3 chest = targetFeet + Vector3.up * BlasterAim.ChestAimHeight;
            float burst = _config.BurstDistance;
            float far = _config.Explosion.FarRadius;
            // 彈對地形的判定半徑(0.2)再留一點餘裕:擦過掩體上緣就會撞地形小爆
            float clearance = _spec.RadiusForField + 0.05f;
            for (int i = 0; i < BlasterAim.CoverAimCandidates; i++)
            {
                Vector3 candidate = targetFeet + Vector3.up * BlasterAim.CoverAimHeight(i);
                Vector3 burstPoint = BlasterAim.BurstPoint(origin, candidate, burst);
                if (!BlasterAim.BurstReaches(burstPoint, chest, far))
                {
                    continue;
                }
                Vector3 path = burstPoint - origin;
                float length = path.magnitude;
                if (length < 1e-3f || Physics.SphereCast(origin, clearance, path / length, out _, length,
                        CombatMasks.Terrain, QueryTriggerInteraction.Ignore))
                {
                    continue;
                }
                if (Physics.Linecast(burstPoint, chest, CombatMasks.Terrain, QueryTriggerInteraction.Ignore))
                {
                    continue;
                }
                aimPoint = candidate;
                return true;
            }
            return false;
        }

        // 交戰距離:比空爆點稍近(直擊不中時彈飛過目標、在身後空爆仍炸得到)
        public override float BotPreferredDistance =>
            _config != null ? _config.BurstDistance * _config.BotPreferredBurstRatio : base.BotPreferredDistance;

        // 塗地:瞄空爆點再遠一點的地面 → 彈在貼近地面處空爆(地面塗墨 = 空爆的大圓)
        public override float BotPaintAimDistance =>
            _config != null ? _config.BurstDistance * _config.BotPaintBurstRatio : base.BotPaintAimDistance;

        // 塗地時墨夠一發才按(不乾扣扳機浪費硬直)
        public override bool BotPaintIntent(float inkRatio)
        {
            float cost = _config != null ? _config.InkConsume : 0.1f;
            float capacity = Tank != null ? Tank.CapacityScale : 1f;
            return inkRatio * capacity >= cost + 0.02f;
        }
    }
}

using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;
using UnityEngine.Pool;

namespace SplatoonC.Gameplay.Combat
{
    // 射擊槍(Shooter)主武器:AttackHeld 意圖 → FireClock 節奏 → 池化墨彈。
    // 墨彈:設定有 S3 墨彈規格時發 InkBullet(S3 三狀態彈道,與雙槍/爆破槍等共用;沿路墨滴有落下動畫),
    // 否則發舊的兩段式 InkProjectile。
    // 瞄準:視線(IAimView;玩家 = 相機中心射線)的落點當目標,槍口朝目標發射(TPS 標準做法)。
    // 可由 WeaponLoadout 在執行期加上並 Configure(prefab 參照從 WeaponConfig 取)。
    public sealed class InkShooter : MainWeapon
    {
        // 設定的射擊音效 id 還沒合成時(新變體)退回通用射擊聲,不要整把槍無聲
        private const string DefaultFireSound = "shoot_shooter";

        [SerializeField, Tooltip("武器設定資產(Assets/Data/WeaponConfig)")]
        private WeaponConfig _config;

        [SerializeField, Tooltip("槍口位置;留空自動找子物件 Muzzle")]
        private Transform _muzzle;

        [SerializeField, Tooltip("墨彈 prefab;留空用 WeaponConfig 的")]
        private InkProjectile _projectilePrefab;

        [SerializeField, Tooltip("墨滴 prefab;留空用 WeaponConfig 的")]
        private InkDrip _dripPrefab;

        [SerializeField, Tooltip("瞄準射線圖層(排除角色)")]
        private LayerMask _aimMask = ~GameLayers.InklingMask;

        [SerializeField, Tooltip("瞄準射線最大距離;沒打中東西就朝這個距離的遠點射")]
        private float _aimMaxDistance = 40f;

        [SerializeField, Tooltip("物件池預熱數量")]
        private int _poolPrewarm = 24;

        [SerializeField, Tooltip("墨滴池預熱數量(每發最多滴 3 滴,需求量是墨彈的數倍)")]
        private int _dripPoolPrewarm = 64;

        [SerializeField, Tooltip("發射點沿瞄準方向前移(公尺);_muzzle 已在槍口時設 0")]
        private float _muzzleForwardOffset;

        [SerializeField, Tooltip("發射點下移(公尺);_muzzle 已在槍口時設 0")]
        private float _muzzleDropOffset;

        private SpreadModel _spreadModel;
        private bool _wasAirborne;
        private float _takeoffTime;
        private FireClock _fireClock;
        private ObjectPool<InkProjectile> _pool;
        private ObjectPool<InkDrip> _dripPool;
        private Transform _poolRoot;
        private Transform _dripPoolRoot;
        // 遞增的發射序號:滴墨相位與槍口噴濺距離都靠它逐發輪替
        private int _shotIndex;
        private bool _attacking;
        private string _fireSound = DefaultFireSound;
        private ShooterGunModel _gunModel;

        public WeaponConfig Config => _config;
        public override MainWeaponConfig BaseConfig => _config;
        public override bool IsAttacking => _attacking;

        // ── 射擊狀態(HUD 準星擴散、測試量測用)──
        // 實際出膛的發數(乾扣扳機不算)與最後一發的時間
        public int ShotsFired => _shotIndex;
        public float LastShotTime { get; private set; } = float.NegativeInfinity;
        // 最後一發的水平偏角(度,帶正負號)與目前的散布偏向 b(S3 模型;連射上升、停火回復)
        public float LastYawOffsetDeg { get; private set; }
        public float SpreadBias => _spreadModel.CurrentBias;
        // 散布倍率:1 = S3 散布。AutoTest 量射程/TTK 時設 0 以免隨機偏角打偏;玩家與 bot 從不改
        public float SpreadMultiplier { get; set; } = 1f;
        public ShooterGunModel GunModel => _gunModel;

        protected override void Awake()
        {
            base.Awake();
            _fireClock = FireClock.CreateReady();
            if (_muzzle == null)
            {
                _muzzle = FindChild("Muzzle");
            }
            if (_config != null)
            {
                if (!_config.UsesInkBullet)
                {
                    BuildPools();
                }
                ApplyVariantPresentation();
            }
        }

        public override void Configure(MainWeaponConfig config)
        {
            var shooter = config as WeaponConfig;
            if (shooter == null)
            {
                Debug.LogError($"InkShooter:設定型別不符({config})", this);
                return;
            }
            bool prefabChanged = _pool == null
                || (shooter.ProjectilePrefab != null && shooter.ProjectilePrefab != _projectilePrefab)
                || (shooter.DripPrefab != null && shooter.DripPrefab != _dripPrefab);
            if (shooter != _config)
            {
                // 換成另一把槍:散布偏向從頭算
                _spreadModel = default;
            }
            _config = shooter;
            if (Owner == null)
            {
                CacheReferences();
            }
            if (_muzzle == null)
            {
                _muzzle = FindChild("Muzzle");
            }
            if (_config.UsesInkBullet)
            {
                // S3 墨彈走共用池(SharedPool):不需要自己的舊彈池
                CombatAssets assets = CombatAssets.Instance;
                SharedPool.Prewarm(assets != null ? assets.InkBulletPrefab : null, 12);
            }
            else if (prefabChanged)
            {
                BuildPools();
            }
            ApplyVariantPresentation();
        }

        // 變體外觀與音效:槍模(沒有就用角色內建槍身)、射擊音效 id(未合成則退回通用聲)
        private void ApplyVariantPresentation()
        {
            _fireSound = GameAudio.Has(_config.FireSound) ? _config.FireSound : DefaultFireSound;
            GameObject prefab = _config.GunModelPrefab;
            if (_gunModel != null && _gunModel.SourcePrefab == prefab)
            {
                return;
            }
            if (_gunModel != null)
            {
                _gunModel.Detach();
                _gunModel = null;
            }
            if (prefab != null)
            {
                _gunModel = ShooterGunModel.Attach(transform, prefab, Owner);
            }
        }

        private void BuildPools()
        {
            if (_config.ProjectilePrefab != null)
            {
                _projectilePrefab = _config.ProjectilePrefab;
            }
            if (_config.DripPrefab != null)
            {
                _dripPrefab = _config.DripPrefab;
            }
            if (_projectilePrefab == null)
            {
                Debug.LogError("InkShooter:缺少墨彈 prefab", this);
                return;
            }
            if (_poolRoot == null)
            {
                _poolRoot = new GameObject("InkProjectilePool").transform;
            }
            // 換成另一把槍的彈 prefab:舊池閒置的彈先銷毀(飛行中的照舊回收到舊池,數量有限)
            _pool?.Clear();
            _dripPool?.Clear();
            _pool = new ObjectPool<InkProjectile>(
                () => Instantiate(_projectilePrefab, _poolRoot),
                p => p.gameObject.SetActive(true),
                p => p.gameObject.SetActive(false),
                p => Destroy(p.gameObject),
                collectionCheck: true,
                defaultCapacity: _poolPrewarm);
            Prewarm(_pool, _poolPrewarm);

            if (_dripPrefab == null)
            {
                Debug.LogWarning("InkShooter:缺少墨滴 prefab,沿路滴墨會停用", this);
                return;
            }
            if (_dripPoolRoot == null)
            {
                _dripPoolRoot = new GameObject("InkDripPool").transform;
            }
            _dripPool = new ObjectPool<InkDrip>(
                () => Instantiate(_dripPrefab, _dripPoolRoot),
                d => d.gameObject.SetActive(true),
                d => d.gameObject.SetActive(false),
                d => Destroy(d.gameObject),
                collectionCheck: true,
                defaultCapacity: _dripPoolPrewarm);
            Prewarm(_dripPool, _dripPoolPrewarm);
        }

        // 預熱:先生成再全部回收,避免第一輪連射時 Instantiate 尖峰。
        private static void Prewarm<T>(ObjectPool<T> pool, int count) where T : class
        {
            var warm = new T[count];
            for (int i = 0; i < count; i++)
            {
                warm[i] = pool.Get();
            }
            for (int i = 0; i < count; i++)
            {
                pool.Release(warm[i]);
            }
        }

        private void OnDestroy()
        {
            // 換裝時主武器元件會被移除:槍模換回內建槍身、池根物件一起清掉
            if (_gunModel != null)
            {
                _gunModel.Detach();
                _gunModel = null;
            }
            if (_poolRoot != null)
            {
                Destroy(_poolRoot.gameObject);
            }
            if (_dripPoolRoot != null)
            {
                Destroy(_dripPoolRoot.gameObject);
            }
        }

        private void Update()
        {
            if (_config == null || Input == null || _muzzle == null || (!_config.UsesInkBullet && _pool == null))
            {
                _attacking = false;
                return;
            }

            // 烏賊態不可射擊;按住副武器(顯示拋物線)或大招佔用時主武器也不出彈(Splatoon 規則)。皆視為未按住,冷卻照走。
            bool attackHeld = WantsAttack;
            _attacking = attackHeld;
            int shots = _fireClock.ConsumeShots(
                attackHeld, Time.time, _config.FireInterval, _config.MaxShotsPerFrame);
            for (int i = 0; i < shots; i++)
            {
                Fire();
            }
        }

        // 瞄準射線(槍口 → 準心目標),含彈道補償:
        // 目標 = 準心射線上「射程極限處」的點(射程內先撞到東西就用命中點),
        // 發射角略微上抬以抵銷直飛段的微重力下墜,讓彈在射程極限時剛好抵達準心那條線。
        public bool TryComputeAim(out Vector3 origin, out Vector3 direction)
        {
            origin = Vector3.zero;
            direction = Vector3.forward;
            if (_muzzle == null)
            {
                return false;
            }
            IAimView view = Owner != null ? Owner.AimView : null;
            if (view == null)
            {
                return false;
            }

            origin = _muzzle.position - Vector3.up * _muzzleDropOffset;
            if (_config.UsesInkBullet)
            {
                return ComputeBulletAim(view, ref origin, out direction);
            }

            // 準心射線的取樣上限 = 視點到槍口的距離 + 武器射程,如此射線上的目標點
            // 正好對應「彈從槍口飛完射程」的位置。
            Vector3 camPos = view.AimOrigin;
            Vector3 camForward = view.AimForward;
            float camToMuzzle = Vector3.Distance(camPos, origin);
            float aimReach = Mathf.Min(camToMuzzle + _config.StraightRange, _aimMaxDistance);

            var aimRay = new Ray(camPos, camForward);
            Vector3 target = Physics.Raycast(aimRay, out RaycastHit hit, aimReach, _aimMask,
                QueryTriggerInteraction.Ignore)
                ? hit.point
                : aimRay.GetPoint(aimReach);

            // 重力補償:抬高瞄準點,補上飛到目標所需時間內的下墜量。
            // 迭代兩次讓飛行時間與抬升量收斂(抬升會略微改變飛行距離)。
            float gravity = Mathf.Abs(_config.StraightGravity);
            float speed = Mathf.Max(_config.MuzzleSpeed, 0.01f);
            Vector3 compensated = target;
            for (int i = 0; i < 2; i++)
            {
                float flightTime = Vector3.Distance(origin, compensated) / speed;
                compensated = target + Vector3.up * (0.5f * gravity * flightTime * flightTime);
            }

            direction = (compensated - origin).normalized;
            origin += direction * _muzzleForwardOffset;
            return true;
        }

        // S3 墨彈的瞄準(同雙槍):準心射線上「彈飛完有效射程」處的點(射程內先撞到東西用命中點)為目標,
        // 再以 S3 三狀態彈道補償減速段的下墜(BulletAim)
        private bool ComputeBulletAim(IAimView view, ref Vector3 origin, out Vector3 direction)
        {
            Vector3 camPos = view.AimOrigin;
            Vector3 camForward = view.AimForward;
            InkBulletSpec spec = _config.Bullet;
            float reach = Mathf.Min(Vector3.Distance(camPos, origin) + _config.EffectiveRange - spec.RadiusForPlayer,
                _aimMaxDistance);
            var aimRay = new Ray(camPos, camForward);
            Vector3 target = Physics.Raycast(aimRay, out RaycastHit hit, reach, _aimMask, QueryTriggerInteraction.Ignore)
                ? hit.point
                : aimRay.GetPoint(reach);
            direction = BulletAim.CompensatedDirection(spec.Motion, origin, target);
            origin += direction * _muzzleForwardOffset;
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

        // 沒掛 Inkling 的舊測試物件:當 Alpha 隊、無回報對象
        internal static InkSource InkSourceOf(Inkling owner, bool chargesSpecial = true)
        {
            return owner != null ? owner.InkSource(chargesSpecial) : new InkSource(SplatoonC.Core.Team.Alpha);
        }

        private void Fire()
        {
            // 空墨不發射(整發判定,FireClock 的節奏槽照走 = 乾扣扳機)。
            // S3:每發都上墨量鎖——連射期間不回墨,乾扣扳機也一樣鎖住
            if (Tank != null)
            {
                Tank.LockRefill(_config.InkLockSeconds);
                if (!Tank.TryConsume(_config.InkCostPerShot))
                {
                    return;
                }
            }
            if (!TryComputeAim(out Vector3 origin, out Vector3 direction))
            {
                return;
            }
            // 散布(S3 模型):只偏水平,首發近乎必中、連射漸散、跳射固定高偏向且偏角較大
            bool airborne = Controller != null && !Controller.isGrounded;
            if (airborne && !_wasAirborne)
            {
                _takeoffTime = Time.time;
            }
            _wasAirborne = airborne;
            float yawOffset = _spreadModel.NextYawOffset(Time.time, _config.Spread, airborne,
                Time.time - _takeoffTime, Random.value, Random.value) * SpreadMultiplier;
            LastYawOffsetDeg = yawOffset;
            direction = Quaternion.AngleAxis(yawOffset, Vector3.up) * direction;
            if (_config.UsesInkBullet)
            {
                FireBullet(origin, direction);
            }
            else
            {
                var projectile = _pool.Get();
                projectile.Launch(origin, direction * _config.MuzzleSpeed, _config, _pool, _dripPool, _shotIndex, Owner);
            }
            PaintMuzzleSplash(origin, direction, _shotIndex);
            GameAudio.PlayFrom(Owner != null && Owner.IsLocalPlayer, _fireSound, origin, 1f, 0.05f);
            LastShotTime = Time.time;
            _shotIndex++;
            NotifyAttack();
        }

        // S3 墨彈:沿路墨滴的起點逐發換 SplitNum 種相位(順序打散,S3ShooterParams.DropletPhase),連續幾發把路錯開填滿
        private void FireBullet(Vector3 origin, Vector3 direction)
        {
            InkBulletSpec spec = _config.Bullet;
            spec.AttackClass = _config.AttackClass;
            if (spec.DripSpacing > 0f)
            {
                spec.DripStartDistance = S3ShooterParams.DropletPhase(_shotIndex, _config.DropletSplitNum, spec.DripSpacing,
                    _config.EffectiveRange - spec.RadiusForPlayer);
            }
            SplatoonC.Core.Team team = Owner != null ? Owner.Team : SplatoonC.Core.Team.Alpha;
            InkBullet.Fire(spec, origin, direction, team, Owner, 0, InheritVelocity(direction));
        }

        // 槍口噴濺:每次射擊必定在腳前濺一小片墨——這是地面路徑痕跡的第二個來源。
        // 落點距離逐發在近↔遠之間輪替,否則每發都疊在同一點,近端只等於一個墨點。
        private void PaintMuzzleSplash(Vector3 origin, Vector3 direction, int shotIndex)
        {
            if (_config.MuzzleSplashRadius <= 0f)
            {
                return;
            }
            Vector3 flat = new Vector3(direction.x, 0f, direction.z);
            if (flat.sqrMagnitude < 0.0001f)
            {
                return;
            }
            int cycle = Mathf.Max(1, _config.DripPhaseCycle);
            float t = cycle > 1 ? (shotIndex % cycle) / (float)(cycle - 1) : 0f;
            float distance = Mathf.Lerp(_config.MuzzleSplashDistance,
                _config.MuzzleSplashDistanceFar, t);
            Vector3 probe = origin + flat.normalized * distance;
            // 從槍口高度往下掉(InkBulletDrip 落下動畫,著地才塗;落點與半徑同改版前)
            SplatoonC.Core.Team team = Owner != null ? Owner.Team : SplatoonC.Core.Team.Alpha;
            InkBulletDrip.Drop(probe + Vector3.up * 0.2f, _config.MuzzleSplashRadius, _config.SplatHardness, InkSourceOf(Owner),
                team, 4.2f);
        }
    }
}

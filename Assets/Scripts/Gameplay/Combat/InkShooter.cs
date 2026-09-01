using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;
using UnityEngine.Pool;

namespace SplatoonC.Gameplay.Combat
{
    // 射墨迴路:AttackHeld 意圖 → FireClock 節奏 → 池化墨彈。
    // 瞄準:相機中心射線的落點當目標,槍口朝目標發射(TPS 標準做法)。
    public sealed class InkShooter : MonoBehaviour
    {
        [SerializeField, Tooltip("武器設定資產(Assets/Data/WeaponConfig)")]
        private WeaponConfig _config;

        [SerializeField, Tooltip("輸入來源;留空自動抓同物件上的 PlayerInputRouter")]
        private PlayerInputRouter _input;

        [SerializeField, Tooltip("槍口位置(用 CameraPivot 即可)")]
        private Transform _muzzle;

        [SerializeField, Tooltip("墨彈 prefab(Assets/Prefabs/InkProjectile)")]
        private InkProjectile _projectilePrefab;

        [SerializeField, Tooltip("墨滴 prefab(Assets/Prefabs/InkDrip);沿彈道滴落的小墨點")]
        private InkDrip _dripPrefab;

        [SerializeField, Tooltip("瞄準射線圖層(排除 Player)")]
        private LayerMask _aimMask = ~0;

        [SerializeField, Tooltip("瞄準射線最大距離;沒打中東西就朝這個距離的遠點射")]
        private float _aimMaxDistance = 40f;

        [SerializeField, Tooltip("物件池預熱數量")]
        private int _poolPrewarm = 32;

        [SerializeField, Tooltip("墨滴池預熱數量(每發最多滴 3 滴,需求量是墨彈的數倍)")]
        private int _dripPoolPrewarm = 96;

        [SerializeField, Tooltip("發射點沿瞄準方向前移(公尺);_muzzle 已在槍口時設 0")]
        private float _muzzleForwardOffset;

        [SerializeField, Tooltip("發射點下移(公尺);_muzzle 已在槍口時設 0")]
        private float _muzzleDropOffset;

        private Camera _camera;
        private FireClock _fireClock;
        private ObjectPool<InkProjectile> _pool;
        private ObjectPool<InkDrip> _dripPool;
        private Transform _poolRoot;
        private Transform _dripPoolRoot;
        private SquidController _squidController;
        private PlayerInkTank _inkTank;

        private void Awake()
        {
            if (_config == null)
            {
                Debug.LogError("InkShooter:缺少武器設定資產,無法射擊", this);
            }
            if (_input == null)
            {
                _input = GetComponent<PlayerInputRouter>();
            }
            if (_projectilePrefab == null)
            {
                Debug.LogError("InkShooter:缺少墨彈 prefab", this);
            }
            _camera = Camera.main;
            _fireClock = FireClock.CreateReady();
            _squidController = GetComponent<SquidController>();
            _inkTank = GetComponent<PlayerInkTank>();

            _poolRoot = new GameObject("InkProjectilePool").transform;
            _pool = new ObjectPool<InkProjectile>(
                CreateProjectile,
                p => p.gameObject.SetActive(true),
                p => p.gameObject.SetActive(false),
                p => Destroy(p.gameObject),
                collectionCheck: true,
                defaultCapacity: _poolPrewarm);

            // 預熱:先生成再全部回收,避免第一輪連射時 Instantiate 尖峰。
            if (_projectilePrefab != null)
            {
                var warm = new InkProjectile[_poolPrewarm];
                for (int i = 0; i < _poolPrewarm; i++)
                {
                    warm[i] = _pool.Get();
                }
                for (int i = 0; i < _poolPrewarm; i++)
                {
                    _pool.Release(warm[i]);
                }
            }

            if (_dripPrefab == null)
            {
                Debug.LogWarning("InkShooter:缺少墨滴 prefab,沿路滴墨會停用", this);
            }
            else
            {
                _dripPoolRoot = new GameObject("InkDripPool").transform;
                _dripPool = new ObjectPool<InkDrip>(
                    CreateDrip,
                    d => d.gameObject.SetActive(true),
                    d => d.gameObject.SetActive(false),
                    d => Destroy(d.gameObject),
                    collectionCheck: true,
                    defaultCapacity: _dripPoolPrewarm);

                var warmDrips = new InkDrip[_dripPoolPrewarm];
                for (int i = 0; i < _dripPoolPrewarm; i++)
                {
                    warmDrips[i] = _dripPool.Get();
                }
                for (int i = 0; i < _dripPoolPrewarm; i++)
                {
                    _dripPool.Release(warmDrips[i]);
                }
            }
        }

        private InkProjectile CreateProjectile()
        {
            var projectile = Instantiate(_projectilePrefab, _poolRoot);
            return projectile;
        }

        private InkDrip CreateDrip()
        {
            return Instantiate(_dripPrefab, _dripPoolRoot);
        }

        private void Update()
        {
            if (_config == null || _input == null || _projectilePrefab == null || _muzzle == null)
            {
                return;
            }

            // 烏賊態不可射擊(視為未按住,冷卻照走)。
            bool attackHeld = _input.AttackHeld
                && (_squidController == null || !_squidController.IsSquid);
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
            if (_camera == null)
            {
                _camera = Camera.main;
                if (_camera == null)
                {
                    return false;
                }
            }

            origin = _muzzle.position - Vector3.up * _muzzleDropOffset;

            // 準心射線的取樣上限 = 相機到槍口的距離 + 武器射程,如此射線上的目標點
            // 正好對應「彈從槍口飛完射程」的位置。
            Vector3 camPos = _camera.transform.position;
            Vector3 camForward = _camera.transform.forward;
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

        public WeaponConfig Config => _config;

        private void Fire()
        {
            // 空墨不發射(整發判定,FireClock 的節奏槽照走 = 乾扣扳機)。
            if (_inkTank != null && !_inkTank.TryConsume(_config.InkCostPerShot))
            {
                return;
            }
            if (!TryComputeAim(out Vector3 origin, out Vector3 direction))
            {
                return;
            }
            if (_config.SpreadAngleDeg > 0f)
            {
                Vector2 offset = Random.insideUnitCircle * _config.SpreadAngleDeg;
                direction = Quaternion.AngleAxis(offset.x, Vector3.up)
                    * Quaternion.AngleAxis(offset.y, Vector3.Cross(direction, Vector3.up).normalized)
                    * direction;
            }
            var projectile = _pool.Get();
            projectile.Launch(origin, direction * _config.MuzzleSpeed, _config, _pool, _dripPool);
            PaintMuzzleSplash(origin, direction);
        }

        // 槍口噴濺:每次射擊必定在腳前濺一小片墨——這是地面路徑痕跡的第二個來源。
        private void PaintMuzzleSplash(Vector3 origin, Vector3 direction)
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
            Vector3 probe = origin + flat.normalized * _config.MuzzleSplashDistance;
            if (Physics.Raycast(probe + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit,
                    4f, _config.HitMask, QueryTriggerInteraction.Ignore))
            {
                var surface = hit.collider.GetComponent<PaintableSurface>();
                if (surface != null)
                {
                    surface.Paint(hit.point, _config.MuzzleSplashRadius,
                        _config.InkColor, _config.SplatHardness);
                }
            }
        }
    }
}

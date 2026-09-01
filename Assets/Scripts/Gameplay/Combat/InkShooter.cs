using SplatoonC.Core.Combat;
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

        [SerializeField, Tooltip("瞄準射線圖層(排除 Player)")]
        private LayerMask _aimMask = ~0;

        [SerializeField, Tooltip("瞄準射線最大距離;沒打中東西就朝這個距離的遠點射")]
        private float _aimMaxDistance = 40f;

        [SerializeField, Tooltip("物件池預熱數量")]
        private int _poolPrewarm = 32;

        [SerializeField, Tooltip("發射點沿瞄準方向前移(公尺),讓彈從身前射出")]
        private float _muzzleForwardOffset = 0.4f;

        [SerializeField, Tooltip("發射點下移(公尺),從頭頂壓到胸口高度")]
        private float _muzzleDropOffset = 0.3f;

        private Camera _camera;
        private FireClock _fireClock;
        private ObjectPool<InkProjectile> _pool;
        private Transform _poolRoot;
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
        }

        private InkProjectile CreateProjectile()
        {
            var projectile = Instantiate(_projectilePrefab, _poolRoot);
            return projectile;
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

        private void Fire()
        {
            // 空墨不發射(整發判定,FireClock 的節奏槽照走 = 乾扣扳機)。
            if (_inkTank != null && !_inkTank.TryConsume(_config.InkCostPerShot))
            {
                return;
            }
            if (_camera == null)
            {
                _camera = Camera.main;
                if (_camera == null)
                {
                    return;
                }
            }

            var aimRay = new Ray(_camera.transform.position, _camera.transform.forward);
            Vector3 target;
            if (Physics.Raycast(aimRay, out RaycastHit hit, _aimMaxDistance, _aimMask, QueryTriggerInteraction.Ignore))
            {
                target = hit.point;
            }
            else
            {
                target = aimRay.GetPoint(_aimMaxDistance);
            }

            Vector3 origin = _muzzle.position - Vector3.up * _muzzleDropOffset;
            Vector3 direction = (target - origin).normalized;
            origin += direction * _muzzleForwardOffset;
            if (_config.SpreadAngleDeg > 0f)
            {
                Vector2 offset = Random.insideUnitCircle * _config.SpreadAngleDeg;
                direction = Quaternion.AngleAxis(offset.x, Vector3.up)
                    * Quaternion.AngleAxis(offset.y, Vector3.Cross(direction, Vector3.up).normalized)
                    * direction;
            }
            var projectile = _pool.Get();
            projectile.Launch(origin, direction * _config.MuzzleSpeed, _config, _pool);
        }
    }
}

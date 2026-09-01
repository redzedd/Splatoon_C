using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Painting;
using UnityEngine;
using UnityEngine.Pool;

namespace SplatoonC.Gameplay.Combat
{
    // 池化墨彈:兩段式彈道(Splatoon 式)——射程內近乎直線維持準心高度,
    // 到達射程極限後重力驟增高速墜地。飛行途中沿路滴下 1~3 滴墨(InkDrip),
    // 連射時那些墨滴在地面鋪出一條墨路。每幀「前一位置→新位置」線段 raycast,不靠 collider。
    public sealed class InkProjectile : MonoBehaviour
    {
        // 滴墨排程緩衝:預先配置,避免每發射擊產生 GC
        private const int MaxDrips = 8;

        private WeaponConfig _config;
        private IObjectPool<InkProjectile> _pool;
        private IObjectPool<InkDrip> _dripPool;
        private Vector3 _velocity;
        private float _remainingLifetime;
        private bool _released;
        private TrailRenderer _trail;
        // 沿彈道的總飛行距離(非水平距離):如此仰射時水平射程自然縮短,平射才是最遠的
        private float _travelledDistance;
        private readonly float[] _dripDistances = new float[MaxDrips];
        private readonly float[] _dripSamples = new float[MaxDrips];
        private int _dripCount;
        private int _dripIndex;

        private void Awake()
        {
            _trail = GetComponent<TrailRenderer>();
        }

        public void Launch(Vector3 position, Vector3 velocity, WeaponConfig config,
            IObjectPool<InkProjectile> pool, IObjectPool<InkDrip> dripPool)
        {
            transform.position = position;
            if (velocity.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(velocity);
            }
            _velocity = velocity;
            _config = config;
            _pool = pool;
            _dripPool = dripPool;
            _remainingLifetime = config.ProjectileLifetime;
            _released = false;
            _travelledDistance = 0f;
            PlanDrips(config);
            // 池化重用鐵律:清掉上一輪殘留拖尾,否則會從回收點畫一條線到槍口
            if (_trail != null)
            {
                _trail.Clear();
            }
        }

        private void PlanDrips(WeaponConfig config)
        {
            _dripIndex = 0;
            _dripCount = 0;
            if (_dripPool == null || config.DripCountMax <= 0)
            {
                return;
            }
            // 不是每發都滴:約每 4 發滴一次(否則連射會糊成一整片)
            if (Random.value >= config.DripChancePerShot)
            {
                return;
            }
            int min = Mathf.Max(0, config.DripCountMin);
            int max = Mathf.Max(min, config.DripCountMax);
            int count = Random.Range(min, max + 1);
            for (int i = 0; i < count && i < MaxDrips; i++)
            {
                _dripSamples[i] = Random.value;
            }
            _dripCount = DripPlanner.Plan(_dripDistances, count,
                config.DripStartDistance, config.StraightRange, _dripSamples,
                config.DripDistanceBias);
        }

        private void Update()
        {
            if (_config == null)
            {
                return;
            }

            float dt = Time.deltaTime;
            Vector3 previous = transform.position;

            // 兩段式:射程內幾乎不掉(維持準心高度),超過射程才急墜。
            // 以「沿彈道飛行距離」判定 → 仰射把射程花在高度上,水平距離變短(平射最遠)
            bool beyondRange = _travelledDistance >= _config.StraightRange;
            _velocity.y += (beyondRange ? _config.DropGravity : _config.StraightGravity) * dt;
            if (beyondRange && _config.DropHorizontalDrag > 0f)
            {
                // 墜落時煞停水平速度 → 近乎垂直落下,仰射不會因滯空而飛得比平射遠
                float damp = Mathf.Exp(-_config.DropHorizontalDrag * dt);
                _velocity.x *= damp;
                _velocity.z *= damp;
            }

            Vector3 next = previous + _velocity * dt;
            Vector3 delta = next - previous;
            float distance = delta.magnitude;

            if (distance > 0.0001f && Physics.Raycast(
                    previous, delta / distance, out RaycastHit hit, distance,
                    _config.HitMask, QueryTriggerInteraction.Ignore))
            {
                OnHit(hit);
                return;
            }

            transform.position = next;
            // 墨彈是橢球(prefab 的 z 軸被拉長),必須讓長軸對齊飛行方向
            if (_velocity.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(_velocity);
            }
            _travelledDistance += distance;
            ReleaseDueDrips(next);
            _remainingLifetime -= dt;
            if (_remainingLifetime <= 0f)
            {
                Release();
            }
        }

        // 沿路滴墨:飛行距離越過排定點就滴一滴。排程是遞增的,單一游標即可。
        private void ReleaseDueDrips(Vector3 position)
        {
            while (_dripIndex < _dripCount && _travelledDistance >= _dripDistances[_dripIndex])
            {
                SpawnDrip(position);
                _dripIndex++;
            }
        }

        private void SpawnDrip(Vector3 position)
        {
            if (_dripPool == null)
            {
                return;
            }
            Vector2 side = Random.insideUnitCircle * _config.DripSideSpeed;
            Vector3 right = Vector3.Cross(Vector3.up, _velocity);
            if (right.sqrMagnitude < 0.0001f)
            {
                right = Vector3.right;
            }
            right.Normalize();
            // 墨滴只繼承一小部分彈速,再加一點橫向擾動 → 幾乎原地垂直落下,墨路才不會變成一條直線
            Vector3 velocity = _velocity * _config.DripInheritSpeed
                + right * side.x + Vector3.up * side.y;
            InkDrip drip = _dripPool.Get();
            drip.Launch(position, velocity, _config, _dripPool);
        }


        private void OnHit(RaycastHit hit)
        {
            if (InkSplashFxPool.Instance != null)
            {
                InkSplashFxPool.Instance.Spawn(hit.point, hit.normal);
            }
            var surface = hit.collider.GetComponent<PaintableSurface>();
            if (surface != null)
            {
                surface.Paint(hit.point, _config.SplatRadius, _config.InkColor, _config.SplatHardness);

                // 噴濺小點:沿命中面的切平面隨機外擴,做出有機噴濺形狀。
                for (int i = 0; i < _config.SplashCount; i++)
                {
                    Vector2 circle = Random.insideUnitCircle;
                    Vector3 tangent = Vector3.Cross(hit.normal, Vector3.up);
                    if (tangent.sqrMagnitude < 0.001f)
                    {
                        tangent = Vector3.Cross(hit.normal, Vector3.forward);
                    }
                    tangent.Normalize();
                    Vector3 bitangent = Vector3.Cross(hit.normal, tangent);
                    Vector3 offset = (tangent * circle.x + bitangent * circle.y) * _config.SplashSpread;
                    surface.Paint(hit.point + offset, _config.SplashRadius, _config.InkColor, _config.SplatHardness);
                }
            }
            Release();
        }

        private void Release()
        {
            if (_released)
            {
                return;
            }
            _released = true;
            _config = null;
            if (_pool != null)
            {
                _pool.Release(this);
            }
            else
            {
                Destroy(gameObject);
            }
        }
    }
}

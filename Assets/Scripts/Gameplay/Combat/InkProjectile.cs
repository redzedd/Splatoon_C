using SplatoonC.Gameplay.Painting;
using UnityEngine;
using UnityEngine.Pool;

namespace SplatoonC.Gameplay.Combat
{
    // 池化墨彈:兩段式彈道(Splatoon 式)——射程內近乎直線維持準心高度,
    // 到達射程極限後重力驟增高速墜地。部分彈的射程被隨機縮短(提前墜落),
    // 連射時就在路徑上鋪出零星墨點。每幀「前一位置→新位置」線段 raycast,不靠 collider。
    public sealed class InkProjectile : MonoBehaviour
    {
        private WeaponConfig _config;
        private IObjectPool<InkProjectile> _pool;
        private Vector3 _velocity;
        private float _remainingLifetime;
        private bool _released;
        private TrailRenderer _trail;
        // 沿彈道的總飛行距離(非水平距離):如此仰射時水平射程自然縮短,平射才是最遠的
        private float _travelledDistance;
        private float _effectiveRange;

        private void Awake()
        {
            _trail = GetComponent<TrailRenderer>();
        }

        public void Launch(Vector3 position, Vector3 velocity, WeaponConfig config, IObjectPool<InkProjectile> pool)
        {
            transform.position = position;
            _velocity = velocity;
            _config = config;
            _pool = pool;
            _remainingLifetime = config.ProjectileLifetime;
            _released = false;
            _travelledDistance = 0f;
            // 少數彈提前墜落:這是地面路徑痕跡的來源之一(另一個是槍口噴濺)
            _effectiveRange = Random.value < config.EarlyDropChance
                ? Random.Range(config.EarlyDropRangeMin, config.EarlyDropRangeMax)
                : config.StraightRange;
            // 池化重用鐵律:清掉上一輪殘留拖尾,否則會從回收點畫一條線到槍口
            if (_trail != null)
            {
                _trail.Clear();
            }
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
            bool beyondRange = _travelledDistance >= _effectiveRange;
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
            _travelledDistance += distance;
            _remainingLifetime -= dt;
            if (_remainingLifetime <= 0f)
            {
                Release();
            }
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

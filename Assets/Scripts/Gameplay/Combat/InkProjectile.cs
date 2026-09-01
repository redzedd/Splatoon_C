using SplatoonC.Gameplay.Painting;
using UnityEngine;
using UnityEngine.Pool;

namespace SplatoonC.Gameplay.Combat
{
    // 池化墨彈:手動積分拋物線 + 每幀「前一位置→新位置」線段 raycast(高速彈不靠 collider)。
    // 命中 PaintableSurface 就塗主點 + 噴濺小點;命中非可塗表面直接回收。
    public sealed class InkProjectile : MonoBehaviour
    {
        private WeaponConfig _config;
        private IObjectPool<InkProjectile> _pool;
        private Vector3 _velocity;
        private float _remainingLifetime;
        private bool _released;
        private TrailRenderer _trail;

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
            _velocity.y += _config.ProjectileGravity * dt;
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

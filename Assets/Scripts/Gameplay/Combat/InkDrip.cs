using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;
using UnityEngine.Pool;

namespace SplatoonC.Gameplay.Combat
{
    // 沿彈道滴落的墨滴:墨彈飛行途中每發滴 1~3 滴,滴下後像墜落的子彈一樣落地塗色。
    // 連射時這些墨滴在路徑上鋪出一條墨路。無傷害、無噴濺小點,只有一個小 splat。
    public sealed class InkDrip : MonoBehaviour
    {
        private WeaponConfig _config;
        private IObjectPool<InkDrip> _pool;
        private Vector3 _velocity;
        private float _remainingLifetime;
        private bool _released;
        private Inkling _owner;
        private Renderer[] _renderers;
        private Color _appliedColor = Color.clear;

        private void Awake()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
        }

        public void Launch(Vector3 position, Vector3 velocity, WeaponConfig config, IObjectPool<InkDrip> pool,
            Inkling owner)
        {
            _owner = owner;
            TeamTint.ApplyCached(_renderers, ref _appliedColor,
                TeamPalette.ColorOf(owner != null ? owner.Team : SplatoonC.Core.Team.Alpha));
            transform.position = position;
            _velocity = velocity;
            _config = config;
            _pool = pool;
            _remainingLifetime = config.DripLifetime;
            _released = false;
        }

        private void Update()
        {
            if (_config == null)
            {
                return;
            }

            float dt = Time.deltaTime;
            Vector3 previous = transform.position;
            _velocity.y += _config.DripGravity * dt;

            Vector3 next = previous + _velocity * dt;
            Vector3 delta = next - previous;
            float distance = delta.magnitude;

            // 墨滴只落地塗色,不被任何角色擋下
            if (distance > 0.0001f && Physics.Raycast(
                    previous, delta / distance, out RaycastHit hit, distance,
                    _config.HitMask & ~GameLayers.InklingMask, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.GetComponent<PaintableSurface>() != null)
                {
                    InkPainter.PaintSphere(hit.point, _config.DripRadius, _config.SplatHardness,
                        InkShooter.InkSourceOf(_owner));
                }
                Release();
                return;
            }

            transform.position = next;
            _remainingLifetime -= dt;
            if (_remainingLifetime <= 0f)
            {
                Release();
            }
        }

        private void Release()
        {
            if (_released)
            {
                return;
            }
            _released = true;
            _config = null;
            _owner = null;
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

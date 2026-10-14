using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;
using UnityEngine.Pool;

namespace SplatoonC.Gameplay.Combat
{
    // 池化三重龍捲風裝置:單一重力彈道,命中牆/地就貼上(沿法線外推),ArmDelay 後在附著點長出龍捲風並回收自己。
    // 命中假人不黏:小幅彈開往下掉,之後射線忽略目標圖層,落到地/牆再貼。
    public sealed class InkstrikeDevice : MonoBehaviour
    {
        private TripleInkstrikeConfig _config;
        private IObjectPool<InkstrikeDevice> _pool;
        private IObjectPool<Tornado> _tornadoPool;
        private Vector3 _velocity;
        private float _life;
        private int _hitMask;
        private bool _attached;
        private float _attachedAt;
        private Vector3 _attachPoint;
        private bool _released;
        private Inkling _owner;

        // 測試用
        public static int AttachedCount { get; private set; }
        public static InkstrikeDevice LastLaunched { get; private set; }

        public bool IsAttached => _attached;
        public Collider AttachedCollider { get; private set; }
        public Vector3 Velocity => _velocity;

        public void Launch(Vector3 position, Vector3 velocity, TripleInkstrikeConfig config,
            IObjectPool<InkstrikeDevice> pool, IObjectPool<Tornado> tornadoPool, Inkling owner)
        {
            _owner = owner;
            TeamTint.ApplyAll(this, TeamPalette.ColorOf(owner != null ? owner.Team : Team.Alpha));
            transform.position = position;
            transform.localScale = Vector3.one * config.VisualSize;
            if (velocity.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(velocity);
            }
            _velocity = velocity;
            _config = config;
            _pool = pool;
            _tornadoPool = tornadoPool;
            _life = config.Lifetime;
            _hitMask = config.HitMask.value;
            _attached = false;
            _released = false;
            AttachedCollider = null;
            LastLaunched = this;
        }

        private void Update()
        {
            if (_config == null)
            {
                return;
            }
            if (_attached)
            {
                if (Time.time >= _attachedAt + _config.ArmDelay)
                {
                    SpawnTornado();
                    Release();
                }
                return;
            }

            float dt = Time.deltaTime;
            _life -= dt;
            if (_life <= 0f)
            {
                Release();
                return;
            }

            Vector3 previous = transform.position;
            Vector3 next = previous;
            Vector3 velocity = _velocity;
            SimpleBallistics.Step(ref next, ref velocity, _config.Gravity, dt);
            Vector3 delta = next - previous;
            float distance = delta.magnitude;
            if (distance > 0.0001f && Physics.Raycast(previous, delta / distance, out RaycastHit hit,
                    distance, _hitMask, QueryTriggerInteraction.Ignore))
            {
                var target = hit.collider.GetComponentInParent<IDamageable>();
                if (target != null)
                {
                    // 不黏人:彈開往下掉,之後忽略目標圖層
                    transform.position = hit.point + hit.normal * 0.05f;
                    _velocity = SimpleBallistics.Reflect(velocity, hit.normal, 0.2f, 0.3f);
                    _hitMask &= ~_config.TargetMask.value;
                    return;
                }
                Attach(hit);
                return;
            }
            transform.position = next;
            _velocity = velocity;
            if (velocity.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(velocity);
            }
        }

        private void Attach(RaycastHit hit)
        {
            _attached = true;
            _attachedAt = Time.time;
            _attachPoint = hit.point;
            AttachedCollider = hit.collider;
            transform.SetPositionAndRotation(hit.point + hit.normal * _config.StickOffset,
                Quaternion.FromToRotation(Vector3.up, hit.normal));
            AttachedCount++;
        }

        private void SpawnTornado()
        {
            if (_tornadoPool == null)
            {
                return;
            }
            Tornado tornado = _tornadoPool.Get();
            tornado.Begin(_attachPoint, _config, _tornadoPool, _owner);
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

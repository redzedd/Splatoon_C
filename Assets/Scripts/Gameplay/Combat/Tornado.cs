using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;
using UnityEngine.Pool;

namespace SplatoonC.Gameplay.Combat
{
    // 池化龍捲風光柱:附著點為底、一律世界垂直向上;半徑由細變粗 → 停留 → 淡出 → 回收。
    // 傷害與塗墨都用 tick(不每幀):傷害走 DamageableRegistry(圓柱範圍判定),
    // 塗墨在當前半徑圓周打向下射線(相位每次轉黃金角,掃滿整個圓盤)+ 徑向射線(貼牆時牆面才有墨)。
    // 視覺用 MaterialPropertyBlock 改 alpha,不複製材質。
    public sealed class Tornado : MonoBehaviour
    {
        [SerializeField, Tooltip("光柱視覺(子物件 Pillar 的 Renderer;Cylinder 原始 mesh 高 2 單位)")]
        private Renderer _pillar;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private const float GoldenAngle = 2.39996f;

        private TripleInkstrikeConfig _config;
        private IObjectPool<Tornado> _pool;
        private Vector3 _base;
        private float _startTime;
        private float _nextDamageTime;
        private float _nextPaintTime;
        private AudioLoop _loop;
        private float _paintPhase;
        private bool _released;
        private MaterialPropertyBlock _block;
        private Color _materialColor = new Color(1f, 0.5f, 0f, 0.55f);
        private Inkling _owner;
        private Team _team;

        // 測試用
        public static int ActiveCount { get; private set; }
        public static int SpawnCount { get; private set; }
        public static Tornado LastSpawned { get; private set; }

        public float CurrentRadius { get; private set; }
        public TornadoGrowth.Phase Phase { get; private set; }
        public Vector3 Base => _base;

        private void Awake()
        {
            _block = new MaterialPropertyBlock();
            if (_pillar == null)
            {
                _pillar = GetComponentInChildren<Renderer>(true);
            }
            if (_pillar != null && _pillar.sharedMaterial != null && _pillar.sharedMaterial.HasProperty(BaseColorId))
            {
                _materialColor = _pillar.sharedMaterial.GetColor(BaseColorId);
            }
        }

        public void Begin(Vector3 basePosition, TripleInkstrikeConfig config, IObjectPool<Tornado> pool, Inkling owner)
        {
            _owner = owner;
            _team = owner != null ? owner.Team : Team.Alpha;
            _base = basePosition;
            _config = config;
            _pool = pool;
            _startTime = Time.time;
            _nextDamageTime = _startTime;
            _nextPaintTime = _startTime;
            _released = false;
            transform.position = basePosition;
            CurrentRadius = config.TornadoStartRadius;
            Phase = TornadoGrowth.Phase.Growing;
            ActiveCount++;
            SpawnCount++;
            LastSpawned = this;
            ApplyVisual(CurrentRadius, 1f);
            _loop = GameAudio.StartLoop("tornado_loop", transform);
        }

        private void Update()
        {
            if (_config == null)
            {
                return;
            }
            float elapsed = Time.time - _startTime;
            float grow = _config.TornadoGrowDuration;
            float hold = _config.TornadoHoldDuration;
            float fade = _config.TornadoFadeDuration;
            Phase = TornadoGrowth.GetPhase(elapsed, grow, hold, fade);
            if (Phase == TornadoGrowth.Phase.Done)
            {
                Release();
                return;
            }
            CurrentRadius = TornadoGrowth.Radius(elapsed, _config.TornadoStartRadius, _config.TornadoMaxRadius, grow);
            ApplyVisual(CurrentRadius, TornadoGrowth.Alpha(elapsed, grow, hold, fade));
            if (Phase == TornadoGrowth.Phase.Fading)
            {
                return;
            }
            if (Time.time >= _nextDamageTime)
            {
                _nextDamageTime += Mathf.Max(0.02f, _config.TornadoDamageTickInterval);
                DealDamage(Mathf.Max(0.02f, _config.TornadoDamageTickInterval));
            }
            if (Time.time >= _nextPaintTime)
            {
                _nextPaintTime += Mathf.Max(0.02f, _config.TornadoPaintInterval);
                Paint();
            }
        }

        private void ApplyVisual(float radius, float alpha)
        {
            if (_pillar == null)
            {
                return;
            }
            float height = _config != null ? _config.TornadoHeight : 6f;
            Transform pillar = _pillar.transform;
            pillar.position = _base + Vector3.up * (height * 0.5f);
            pillar.rotation = Quaternion.identity;
            pillar.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
            // 光柱染成施放者的隊伍色(保留材質原本的透明度)
            Color color = TeamPalette.ColorOf(_team);
            color.a = _materialColor.a * alpha;
            _pillar.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, color);
            _pillar.SetPropertyBlock(_block);
        }

        // 圓柱範圍:目標最近點的水平距離 ≤ 當前半徑,且高度落在光柱內。
        private void DealDamage(float seconds)
        {
            float damage = _config.TornadoDamagePerSecond * seconds;
            float height = _config.TornadoHeight;
            Vector3 axisMid = _base + Vector3.up * (height * 0.5f);
            var targets = DamageableRegistry.Active;
            for (int i = 0; i < targets.Count; i++)
            {
                IDamageable target = targets[i];
                if (!target.IsAlive || !DamageRules.CanDamage(_team, target))
                {
                    continue;
                }
                Vector3 closest = target.ClosestPoint(axisMid);
                float dx = closest.x - _base.x;
                float dz = closest.z - _base.z;
                if (dx * dx + dz * dz > CurrentRadius * CurrentRadius)
                {
                    continue;
                }
                if (closest.y < _base.y - 0.5f || closest.y > _base.y + height)
                {
                    continue;
                }
                target.ApplyDamage(new DamageInfo(damage, closest, _team, _owner, DamageKind.Special,
                    SplatoonC.Core.Combat.AttackClass.Inkstrike));
            }
        }

        private void Paint()
        {
            float reach = _config.TornadoHeight + 1f;
            PaintDown(_base + Vector3.up * 0.5f, _config.TornadoPaintRadius, reach, false);
            int ringCount = Mathf.Max(1, _config.TornadoRingRayCount);
            for (int k = 0; k < ringCount; k++)
            {
                float angle = _paintPhase + k * Mathf.PI * 2f / ringCount;
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * CurrentRadius;
                PaintDown(_base + offset + Vector3.up * 0.5f, _config.TornadoPaintRadius, reach, k == 0);
            }
            int wallCount = Mathf.Max(0, _config.TornadoWallRayCount);
            for (int k = 0; k < wallCount; k++)
            {
                float angle = _paintPhase + k * Mathf.PI * 2f / wallCount;
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                PaintRay(_base + Vector3.up * 0.5f, direction, CurrentRadius + 0.5f, _config.TornadoPaintRadius);
                PaintRay(_base + Vector3.up * 2f, direction, CurrentRadius + 0.5f, _config.TornadoPaintRadius);
            }
            _paintPhase += GoldenAngle;
        }

        private void PaintDown(Vector3 origin, float radius, float reach, bool fx)
        {
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, reach, _config.HitMask,
                    QueryTriggerInteraction.Ignore))
            {
                return;
            }
            if (hit.collider.GetComponent<PaintableSurface>() != null)
            {
                // 大招自己塗的墨不充下一次大招
                InkPainter.PaintSphere(hit.point, radius, _config.PaintHardness, InkShooter.InkSourceOf(_owner, false));
            }
            if (fx && InkSplashFxPool.Instance != null)
            {
                InkSplashFxPool.Instance.Spawn(hit.point, hit.normal, TeamPalette.ColorOf(_team));
            }
        }

        private void PaintRay(Vector3 origin, Vector3 direction, float reach, float radius)
        {
            if (!Physics.Raycast(origin, direction, out RaycastHit hit, reach, _config.HitMask,
                    QueryTriggerInteraction.Ignore))
            {
                return;
            }
            if (hit.collider.GetComponent<PaintableSurface>() != null)
            {
                InkPainter.PaintSphere(hit.point, radius, _config.PaintHardness, InkShooter.InkSourceOf(_owner, false));
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
            if (_loop != null)
            {
                _loop.Stop(0.4f);
                _loop = null;
            }
            ActiveCount = Mathf.Max(0, ActiveCount - 1);
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

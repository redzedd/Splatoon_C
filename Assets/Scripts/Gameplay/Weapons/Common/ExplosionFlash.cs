using SplatoonC.Gameplay.Painting;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 爆炸閃光:團隊色半透明球,0.1 秒內從 0.35 倍撐到爆炸半徑、0.3 秒淡出後回池。
    // 讓玩家看得出「炸到多大」(S3 的爆炸墨花);純視覺,無碰撞。由 Explosion.Detonate 產生。
    public sealed class ExplosionFlash : MonoBehaviour
    {
        private const float GrowSeconds = 0.1f;
        private const float LifeSeconds = 0.3f;
        private const float StartAlpha = 0.55f;

        [SerializeField, Tooltip("閃光球的 Renderer(子物件 Sphere);留空自動找")]
        private Renderer _renderer;

        private float _age;
        private float _radius;
        private Color _color;
        private bool _playing;

        public static int SpawnCount { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetOnLoad()
        {
            SpawnCount = 0;
        }

        public static void Spawn(Vector3 center, float radius, Color color)
        {
            CombatAssets assets = CombatAssets.Instance;
            if (assets == null || assets.ExplosionFlashPrefab == null || radius <= 0f)
            {
                return;
            }
            ExplosionFlash flash = SharedPool.Spawn(assets.ExplosionFlashPrefab);
            if (flash != null)
            {
                flash.Play(center, radius, color);
                SpawnCount++;
            }
        }

        private void Awake()
        {
            if (_renderer == null)
            {
                _renderer = GetComponentInChildren<Renderer>(true);
            }
        }

        private void Play(Vector3 center, float radius, Color color)
        {
            transform.position = center;
            _radius = radius;
            _color = color;
            _age = 0f;
            _playing = true;
            Apply();
        }

        private void Update()
        {
            if (!_playing)
            {
                return;
            }
            _age += Time.deltaTime;
            if (_age >= LifeSeconds)
            {
                _playing = false;
                SharedPool.Despawn(this);
                return;
            }
            Apply();
        }

        private void Apply()
        {
            float grow = Mathf.Clamp01(_age / GrowSeconds);
            grow = 1f - (1f - grow) * (1f - grow);
            transform.localScale = Vector3.one * (Mathf.Lerp(0.35f, 1f, grow) * _radius * 2f);
            if (_renderer != null)
            {
                TeamTint.Apply(_renderer, _color, StartAlpha * (1f - _age / LifeSeconds));
            }
        }
    }
}

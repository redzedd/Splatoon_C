using SplatoonC.Core;
using SplatoonC.Gameplay.Painting;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 墨滴的落下動畫。滴墨的「邏輯」不變:落點 = 滴墨處正下方的地形、半徑與硬度由發射端決定
    //(InkBullet 沿路每 DripSpacing 一滴、射擊槍腳前墨滴);差別只在看得到一滴墨從彈道上掉下來,碰到地面那一刻才塗。
    // 直直往下掉,落點在生成時就用射線決定好,所以塗的位置和改版前的即時塗墨完全相同。
    // 池化(SharedPool);沒有 prefab(未跑 M7 Setup)時 Drop 直接在落點塗(與改版前相同)。
    public sealed class InkBulletDrip : MonoBehaviour
    {
        // 下墜加速度與初速(公尺/秒²、公尺/秒;與射擊槍舊墨滴 InkDrip 的手感同級 [EST])
        private const float Gravity = 25f;
        private const float StartSpeed = 1.5f;

        private Renderer[] _renderers;
        private Color _appliedColor = Color.clear;
        private Vector3 _landing;
        private float _radius;
        private float _hardness;
        private InkSource _source;
        private float _speed;

        // 場上落下中的墨滴數(測試用)
        public static int ActiveCount { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetOnLoad()
        {
            ActiveCount = 0;
        }

        // 從 position 往正下方 reach 公尺內找地形;找到就在落點塗 radius——有 prefab 時先播落下動畫、著地才塗。
        // 回傳是否有落點
        public static bool Drop(Vector3 position, float radius, float hardness, in InkSource source, Team team, float reach)
        {
            if (radius <= 0f || !Physics.Raycast(position, Vector3.down, out RaycastHit hit, reach, CombatMasks.Terrain,
                    QueryTriggerInteraction.Ignore))
            {
                return false;
            }
            CombatAssets assets = CombatAssets.Instance;
            InkBulletDrip prefab = assets != null ? assets.InkBulletDripPrefab : null;
            if (prefab == null || hit.distance < 0.05f)
            {
                InkPainter.PaintSphere(hit.point, radius, hardness, source);
                return true;
            }
            InkBulletDrip drip = SharedPool.Spawn(prefab);
            drip.Launch(position, hit.point, radius, hardness, source, team);
            return true;
        }

        private void Awake()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
        }

        private void OnEnable()
        {
            ActiveCount++;
        }

        private void OnDisable()
        {
            ActiveCount = Mathf.Max(0, ActiveCount - 1);
        }

        private void Launch(Vector3 position, Vector3 landing, float radius, float hardness, in InkSource source, Team team)
        {
            transform.position = position;
            _landing = landing;
            _radius = radius;
            _hardness = hardness;
            _source = source;
            _speed = StartSpeed;
            TeamTint.ApplyCached(_renderers, ref _appliedColor, TeamPalette.ColorOf(team));
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            _speed += Gravity * dt;
            Vector3 position = transform.position;
            position.y -= _speed * dt;
            if (position.y <= _landing.y)
            {
                InkPainter.PaintSphere(_landing, _radius, _hardness, _source);
                _source = default;
                SharedPool.Despawn(this);
                return;
            }
            transform.position = position;
        }
    }
}

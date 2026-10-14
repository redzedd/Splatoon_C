using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 戰鬥基礎設施共用的 prefab 參照(Resources/Weapons/CombatAssets.asset,由 CombatInfraSetup 建立與接線)。
    // 讓 InkBullet.Fire / Explosion 不需要每個武器設定都各拉一份 prefab;武器要換外觀時可在 Fire 傳自己的 prefab。
    [CreateAssetMenu(fileName = "CombatAssets", menuName = "SplatoonC/戰鬥共用資產")]
    public sealed class CombatAssets : ScriptableObject
    {
        public const string ResourcePath = "Weapons/CombatAssets";

        [SerializeField, Tooltip("通用墨彈 prefab(Assets/Prefabs/Weapons/InkBullet)")]
        private InkBullet _inkBulletPrefab;

        [SerializeField, Tooltip("爆炸閃光 prefab(Assets/Prefabs/Weapons/ExplosionFlash)")]
        private ExplosionFlash _explosionFlashPrefab;

        [SerializeField, Tooltip("墨滴落下動畫 prefab(Assets/Prefabs/Weapons/InkBulletDrip);留空 = 墨滴直接在落點塗")]
        private InkBulletDrip _inkBulletDripPrefab;

        private static CombatAssets _instance;
        private static bool _loaded;

        public InkBullet InkBulletPrefab => _inkBulletPrefab;
        public ExplosionFlash ExplosionFlashPrefab => _explosionFlashPrefab;
        public InkBulletDrip InkBulletDripPrefab => _inkBulletDripPrefab;

        public static CombatAssets Instance
        {
            get
            {
                if (!_loaded || _instance == null)
                {
                    _instance = Resources.Load<CombatAssets>(ResourcePath);
                    _loaded = true;
                }
                return _instance;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetOnLoad()
        {
            _instance = null;
            _loaded = false;
        }
    }
}

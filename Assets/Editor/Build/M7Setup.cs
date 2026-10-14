using System.Collections.Generic;
using SplatoonC.Gameplay.Combat;
using UnityEditor;
using UnityEngine;

namespace SplatoonC.EditorBuild
{
    // M7(武器組合框架)的接線腳本:把 prefab 參照寫進設定資產、建立武器組合與 Resources/WeaponCatalog、
    // 在 Player prefab 掛上 WeaponLoadout。冪等、可重跑。組合清單來自 Core/Combat/S3KitTable(新主武器的組合加在那裡)。
    // 選單 Tools/SplatoonC/M7 Setup;batchmode -executeMethod SplatoonC.EditorBuild.M7Setup.Apply
    public static class M7Setup
    {
        private const string KitFolder = "Assets/Data/Kits";
        private const string CatalogPath = "Assets/Resources/WeaponCatalog.asset";
        private const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";

        [MenuItem("Tools/SplatoonC/M7 Setup")]
        public static void Apply()
        {
            if (Application.isPlaying)
            {
                Debug.LogError("[M7SETUP] ERROR Play mode 中不可接線");
                return;
            }
            WireConfigs();
            // 各武器模組建立自己的設定資產與 prefab(模組的 [InitializeOnLoad] 編輯器腳本註冊)
            foreach (var setup in ExtraSetups)
            {
                setup();
            }
            AssetDatabase.SaveAssets();
            List<WeaponKit> kits = Kits();
            if (kits.Count == 0)
            {
                Debug.LogError("[M7SETUP] ERROR 沒有任何有效的武器組合");
                return;
            }
            WriteCatalog(kits);
            WirePlayerPrefab(S3KitCatalogBuilder.FindPlayerDefault(kits));
            AssetDatabase.SaveAssets();
            Debug.Log($"[M7SETUP] 完成:武器組合 {kits.Count} 組");
        }

        // 設定資產 ← prefab 參照(執行期換裝不靠 Player prefab 上的欄位)
        private static void WireConfigs()
        {
            var weapon = Load<WeaponConfig>("Assets/Data/WeaponConfig.asset");
            SetRef(weapon, "_projectilePrefab", LoadComponent<InkProjectile>("Assets/Prefabs/InkProjectile.prefab"));
            SetRef(weapon, "_dripPrefab", LoadComponent<InkDrip>("Assets/Prefabs/InkDrip.prefab"));

            var bomb = Load<SplatBombConfig>("Assets/Data/SplatBombConfig.asset");
            SetRef(bomb, "_prefab", LoadComponent<SplatBomb>("Assets/Prefabs/SplatBomb.prefab"));
            SetString(bomb, "_displayName", "斯普拉炸彈");

            var strike = Load<TripleInkstrikeConfig>("Assets/Data/TripleInkstrikeConfig.asset");
            SetRef(strike, "_devicePrefab", LoadComponent<InkstrikeDevice>("Assets/Prefabs/InkstrikeDevice.prefab"));
            SetRef(strike, "_tornadoPrefab", LoadComponent<Tornado>("Assets/Prefabs/Tornado.prefab"));
            SetString(strike, "_specialName", "三重龍捲風");
        }

        // 武器組合清單(第一組 = 選單預設):S3 正式組合依 S3KitTable 建立(缺零件的跳過,做好後重跑自動加入)
        private static List<WeaponKit> Kits()
        {
            var kits = new List<WeaponKit>();
            S3KitCatalogBuilder.AddAll(kits);
            foreach (var extra in ExtraKits)
            {
                extra(kits);
            }
            return kits;
        }

        // 其他模組(新武器種類)在這裡註冊自己的組合
        public static readonly List<System.Action<List<WeaponKit>>> ExtraKits = new List<System.Action<List<WeaponKit>>>();

        // 其他模組的資產建立步驟(設定資產、prefab),在組合之前執行;必須冪等
        public static readonly List<System.Action> ExtraSetups = new List<System.Action>();

        public static void AddKit(List<WeaponKit> kits, string id, string name, string english, string description,
            MainWeaponConfig main, SubWeaponConfig sub, SpecialWeaponConfig special, float points, WeightClass weight,
            string s1, int v1, string s2, int v2, string s3, int v3)
        {
            if (main == null || sub == null || special == null)
            {
                Debug.LogError($"[M7SETUP] ERROR 組合 {id} 缺主/副/大招設定");
                return;
            }
            EnsureFolder("Assets/Data", "Kits");
            string path = $"{KitFolder}/Kit_{id}.asset";
            var kit = AssetDatabase.LoadAssetAtPath<WeaponKit>(path);
            if (kit == null)
            {
                kit = ScriptableObject.CreateInstance<WeaponKit>();
                AssetDatabase.CreateAsset(kit, path);
            }
            kit.Setup(id, name, english, description, main, sub, special, points, weight, s1, v1, s2, v2, s3, v3);
            EditorUtility.SetDirty(kit);
            kits.Add(kit);
        }

        private static void WriteCatalog(List<WeaponKit> kits)
        {
            EnsureFolder("Assets", "Resources");
            var catalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<WeaponCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            catalog.SetKits(kits);
            EditorUtility.SetDirty(catalog);
        }

        private static void WirePlayerPrefab(WeaponKit defaultKit)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                var loadout = root.GetComponent<WeaponLoadout>();
                if (loadout == null)
                {
                    loadout = root.AddComponent<WeaponLoadout>();
                }
                var so = new SerializedObject(loadout);
                so.FindProperty("_defaultKit").objectReferenceValue = defaultKit;
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        public static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                Debug.LogError($"[M7SETUP] ERROR 找不到 {path}");
            }
            return asset;
        }

        public static T LoadComponent<T>(string prefabPath) where T : Component
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            return go != null ? go.GetComponent<T>() : null;
        }

        public static void SetRef(Object target, string field, Object value)
        {
            if (target == null)
            {
                return;
            }
            var so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogError($"[M7SETUP] ERROR {target.name} 沒有欄位 {field}");
                return;
            }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        public static void SetString(Object target, string field, string value)
        {
            if (target == null)
            {
                return;
            }
            var so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(field);
            if (prop != null)
            {
                prop.stringValue = value;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(target);
            }
        }

        public static void EnsureFolder(string parent, string child)
        {
            if (!AssetDatabase.IsValidFolder($"{parent}/{child}"))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }
    }
}

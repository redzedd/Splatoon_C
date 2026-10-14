using SplatoonC.Gameplay;
using SplatoonC.Gameplay.Combat;
using UnityEditor;
using UnityEngine;

namespace SplatoonC.EditorBuild
{
    // 副武器 A4「Squid Beakon」的資產(冪等):Assets/Resources/Weapons/Sub_squid_beakon.asset(ResetToS3 寫入 S3 0 AP 數值)、
    // 場上裝置 prefab(三腳架 + 桿 + 旋轉的隊伍色雷達碟 + 兩顆剩餘次數燈;Target 圖層膠囊碰撞體)與手持模型 prefab
    // (Assets/Prefabs/Weapons/)。材質沿用大招單元的共用色組(SpecialPrefabParts)。
    // 由 M7Setup.Apply 經 ExtraSetups 呼叫;選單 Tools/SplatoonC/Weapons/Squid Beakon Setup。
    [InitializeOnLoad]
    public static class SquidBeakonSetup
    {
        private const string ConfigPath = SpecialPrefabParts.ConfigFolder + "/Sub_squid_beakon.asset";
        private const string DevicePath = SpecialPrefabParts.PrefabFolder + "/SquidBeakon.prefab";
        private const string HeldPath = SpecialPrefabParts.PrefabFolder + "/SquidBeakonHeld.prefab";
        private const string TankInkPath = "Assets/Materials/Characters/TankInk.mat";

        static SquidBeakonSetup()
        {
            System.Action create = Create;
            if (!M7Setup.ExtraSetups.Contains(create))
            {
                M7Setup.ExtraSetups.Add(create);
            }
        }

        [MenuItem("Tools/SplatoonC/Weapons/Squid Beakon Setup")]
        public static void Create()
        {
            if (Application.isPlaying)
            {
                Debug.LogError("[BEAKONSETUP] ERROR Play mode 中不可建立資產");
                return;
            }
            SpecialPrefabParts.EnsureFolders();
            SpecialPrefabParts.Palette palette = SpecialPrefabParts.CreatePalette();
            SquidBeakon device = BuildDevice(palette);
            GameObject held = BuildHeld(palette);

            var config = AssetDatabase.LoadAssetAtPath<SquidBeakonConfig>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<SquidBeakonConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
                Debug.Log($"[BEAKONSETUP] 建立 {ConfigPath}");
            }
            config.ResetToS3();
            EditorUtility.SetDirty(config);
            M7Setup.SetRef(config, "_prefab", device);
            M7Setup.SetRef(config, "_heldModel", held);
            AssetDatabase.SaveAssets();
            Debug.Log("[BEAKONSETUP] 完成:Sub_squid_beakon、SquidBeakon、SquidBeakonHeld");
        }

        // 場上裝置(高約 1.1 m):三腳架(暗色)+ 桿 + 雷達碟(隊伍色,Dish 節點持續旋轉)+ 兩顆次數燈 + 腳下墨圈
        private static SquidBeakon BuildDevice(SpecialPrefabParts.Palette p)
        {
            var root = new GameObject("SquidBeakon");
            root.layer = GameLayers.Target;
            var collider = root.AddComponent<CapsuleCollider>();
            collider.center = new Vector3(0f, 0.55f, 0f);
            collider.radius = 0.3f;
            collider.height = 1.1f;

            SpecialPrefabParts.Pillar(root.transform, "InkBase", p.Ink, Vector3.zero, 0.34f, 0.03f);
            for (int i = 0; i < 3; i++)
            {
                float yaw = i * 120f + 30f;
                Vector3 foot = Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 0f, 0.3f);
                Vector3 top = new Vector3(0f, 0.5f, 0f);
                Vector3 mid = (foot + top) * 0.5f;
                Transform leg = SpecialPrefabParts.Primitive(root.transform, "Leg" + i, PrimitiveType.Cylinder, p.Dark);
                leg.localPosition = mid;
                leg.localRotation = Quaternion.FromToRotation(Vector3.up, (top - foot).normalized);
                leg.localScale = new Vector3(0.045f, (top - foot).magnitude * 0.5f, 0.045f);
            }
            SpecialPrefabParts.Pillar(root.transform, "Hub", p.Metal, new Vector3(0f, 0.46f, 0f), 0.07f, 0.1f);
            SpecialPrefabParts.Pillar(root.transform, "Pole", p.Dark, new Vector3(0f, 0.5f, 0f), 0.035f, 0.38f);

            Transform dish = SpecialPrefabParts.Empty(root.transform, "Dish", new Vector3(0f, 0.9f, 0f));
            dish.gameObject.layer = GameLayers.Target;
            SpecialPrefabParts.Pillar(dish, "Mount", p.Metal, Vector3.zero, 0.06f, 0.08f);
            Transform bowl = SpecialPrefabParts.Empty(dish, "Bowl", new Vector3(0f, 0.1f, 0.02f));
            bowl.localRotation = Quaternion.Euler(-40f, 0f, 0f);
            SpecialPrefabParts.Ball(bowl, "InkDish", p.Ink, Vector3.zero, new Vector3(0.56f, 0.14f, 0.56f));
            SpecialPrefabParts.Pillar(bowl, "DishBack", p.Ivory, new Vector3(0f, -0.07f, 0f), 0.12f, 0.05f);
            Transform feed = SpecialPrefabParts.Primitive(bowl, "Feed", PrimitiveType.Cylinder, p.Dark);
            feed.localPosition = new Vector3(0f, 0.14f, 0f);
            feed.localScale = new Vector3(0.025f, 0.12f, 0.025f);
            SpecialPrefabParts.Ball(bowl, "InkHorn", p.Ink, new Vector3(0f, 0.27f, 0f), Vector3.one * 0.09f);

            // 次數燈(S3 信標上的兩顆燈:用掉一次熄一顆)
            var lights = new Renderer[2];
            for (int i = 0; i < 2; i++)
            {
                Transform light = SpecialPrefabParts.Ball(dish, "Light" + i, p.Glow,
                    new Vector3(i == 0 ? -0.09f : 0.09f, 0.06f, -0.16f), Vector3.one * 0.085f);
                lights[i] = light.GetComponent<Renderer>();
            }
            SpecialPrefabParts.SetLayerRecursively(root.transform, GameLayers.Target);

            var beakon = root.AddComponent<SquidBeakon>();
            SpecialPrefabParts.SetObject(beakon, "_dish", dish);
            var so = new SerializedObject(beakon);
            SerializedProperty prop = so.FindProperty("_useLights");
            prop.arraySize = lights.Length;
            for (int i = 0; i < lights.Length; i++)
            {
                prop.GetArrayElementAtIndex(i).objectReferenceValue = lights[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            GameObject saved = SpecialPrefabParts.SavePrefab(root, DevicePath);
            return saved != null ? saved.GetComponent<SquidBeakon>() : null;
        }

        // 手持模型(投擲姿勢時左手拿著;以公尺建模,CharacterVisual 抵銷手骨縮放):縮小的桿 + 碟,墨水部位用 TankInk
        private static GameObject BuildHeld(SpecialPrefabParts.Palette p)
        {
            Material heldInk = AssetDatabase.LoadAssetAtPath<Material>(TankInkPath);
            if (heldInk == null)
            {
                heldInk = p.Ink;
            }
            var root = new GameObject("SquidBeakonHeld");
            SpecialPrefabParts.Pillar(root.transform, "Pole", p.Dark, new Vector3(0f, -0.08f, 0f), 0.02f, 0.16f);
            Transform bowl = SpecialPrefabParts.Empty(root.transform, "Bowl", new Vector3(0f, 0.1f, 0f));
            bowl.localRotation = Quaternion.Euler(-40f, 0f, 0f);
            SpecialPrefabParts.Ball(bowl, "InkDish", heldInk, Vector3.zero, new Vector3(0.24f, 0.06f, 0.24f));
            SpecialPrefabParts.Ball(bowl, "InkHorn", heldInk, new Vector3(0f, 0.11f, 0f), Vector3.one * 0.045f);
            SpecialPrefabParts.Ball(root.transform, "Light0", p.Glow, new Vector3(0f, 0.05f, -0.06f), Vector3.one * 0.04f);
            return SpecialPrefabParts.SavePrefab(root, HeldPath);
        }
    }
}

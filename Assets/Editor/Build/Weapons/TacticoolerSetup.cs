using SplatoonC.Gameplay;
using SplatoonC.Gameplay.Combat;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace SplatoonC.EditorBuild
{
    // Tacticooler(D2)的資產:飲料機 prefab(罐形機身 + 頂端燈泡 + 四支拿飲料的手臂;Default 圖層實心碰撞體 = 雙方都擋、
    // NavMeshObstacle carve)、手上舉著的罐子、飲用者發光 prefab,與 Resources/Weapons/Special_tacticooler(含投擲參數)。
    // 冪等,M7Setup.Apply 會跑。
    [InitializeOnLoad]
    public static class TacticoolerSetup
    {
        private const string DevicePath = SpecialPrefabParts.PrefabFolder + "/TacticoolerDevice.prefab";
        private const string HeldPath = SpecialPrefabParts.PrefabFolder + "/TacticoolerHeld.prefab";
        private const string AuraPath = SpecialPrefabParts.PrefabFolder + "/TacticoolerAura.prefab";

        static TacticoolerSetup()
        {
            System.Action create = Create;
            if (!M7Setup.ExtraSetups.Contains(create))
            {
                M7Setup.ExtraSetups.Add(create);
            }
        }

        [MenuItem("Tools/SplatoonC/Weapons/Tacticooler Setup")]
        public static void Create()
        {
            if (Application.isPlaying)
            {
                Debug.LogError("[SPECIALSETUP] ERROR Play mode 中不可建立資產");
                return;
            }
            SpecialPrefabParts.EnsureFolders();
            SpecialPrefabParts.Palette palette = SpecialPrefabParts.CreatePalette();
            Material chrome = SpecialPrefabParts.Lit("SpecialChrome", new Color(0.82f, 0.84f, 0.88f), 0.7f, 0.8f);
            TacticoolerDevice device = BuildDevice(palette, chrome);
            GameObject held = BuildHeld(palette, chrome);
            TacticoolerAura aura = BuildAura(palette);
            var config = SpecialPrefabParts.ResetConfig<TacticoolerConfig>("tacticooler");
            M7Setup.SetRef(config, "_devicePrefab", device);
            M7Setup.SetRef(config, "_heldPrefab", held);
            M7Setup.SetRef(config, "_auraPrefab", aura);
            M7Setup.SetString(config, "_specialName", "能量站");
            SpecialPrefabParts.SetFloat(config, "_fallbackCostPoints", 200f);
            // 投擲(MoveParam):水平 0.4 u/F(24 m/s,v3.1.0 起)、上 0.07 u/F(4.2 m/s)→ 初速 24.37 m/s、上抬 9.9°;
            // 重力 FlyGravity 0.009 u/F² = 32.4 m/s²(空氣阻力 0.001/F 可忽略);不繼承慣性;不耗墨
            SpecialPrefabParts.SetFloat(config, "_throwSpeed", Mathf.Sqrt(24f * 24f + 4.2f * 4.2f));
            SpecialPrefabParts.SetFloat(config, "_liftAngleDeg", Mathf.Atan2(4.2f, 24f) * Mathf.Rad2Deg);
            SpecialPrefabParts.SetFloat(config, "_gravity", -0.009f * 3600f);
            SpecialPrefabParts.SetFloat(config, "_aimReach", 12f);
            SpecialPrefabParts.SetFloat(config, "_previewStepTime", 0.02f);
            SpecialPrefabParts.SetInt(config, "_previewMaxSteps", 120);
            SpecialPrefabParts.SetFloat(config, "_inkLockSeconds", 0f);
            AssetDatabase.SaveAssets();
            Debug.Log("[SPECIALSETUP] Tacticooler:Special_tacticooler、TacticoolerDevice、TacticoolerHeld、TacticoolerAura");
        }

        // 飲料機:罐形機身(白 + 隊伍色腰帶 + 透明窗)、頂端燈泡、四支手臂各拿一罐;碰撞體 1.1 × 2.1 × 1.1(Default 圖層)
        private static TacticoolerDevice BuildDevice(SpecialPrefabParts.Palette p, Material chrome)
        {
            var root = new GameObject("TacticoolerDevice");
            root.layer = GameLayers.Default;
            var solidGo = new GameObject("Solid");
            solidGo.layer = GameLayers.Default;
            solidGo.transform.SetParent(root.transform, false);
            var solid = solidGo.AddComponent<BoxCollider>();
            solid.center = new Vector3(0f, 1.05f, 0f);
            solid.size = new Vector3(1.1f, 2.1f, 1.1f);
            var obstacle = root.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = new Vector3(0f, 1.05f, 0f);
            obstacle.size = new Vector3(1.3f, 2.1f, 1.3f);
            obstacle.carving = true;
            obstacle.carveOnlyStationary = true;

            Transform body = SpecialPrefabParts.Empty(root.transform, "Body", Vector3.zero);
            SpecialPrefabParts.Pillar(body, "Foot", p.Dark, Vector3.zero, 0.56f, 0.12f);
            SpecialPrefabParts.Pillar(body, "Shell", p.Ivory, new Vector3(0f, 0.12f, 0f), 0.5f, 1.45f);
            SpecialPrefabParts.Pillar(body, "InkBand", p.Ink, new Vector3(0f, 0.55f, 0f), 0.52f, 0.32f);
            SpecialPrefabParts.Box(body, "Window", p.Glass, new Vector3(0f, 1.05f, 0.47f), new Vector3(0.5f, 0.55f, 0.06f));
            SpecialPrefabParts.Pillar(body, "Lid", chrome, new Vector3(0f, 1.57f, 0f), 0.46f, 0.12f);
            SpecialPrefabParts.Pillar(body, "Stem", p.Dark, new Vector3(0f, 1.69f, 0f), 0.08f, 0.14f);
            Transform bulb = SpecialPrefabParts.Ball(body, "InkBulb", p.Glow, new Vector3(0f, 1.98f, 0f), Vector3.one * 0.46f);
            var arms = new Transform[4];
            var cans = new GameObject[4];
            for (int i = 0; i < 4; i++)
            {
                float yaw = 45f + i * 90f;
                Transform arm = SpecialPrefabParts.Empty(body, $"Arm{i}", new Vector3(0f, 1.18f, 0f));
                arm.localRotation = Quaternion.Euler(0f, yaw, 0f);
                SpecialPrefabParts.Box(arm, "Rod", chrome, new Vector3(0f, 0f, 0.62f), new Vector3(0.06f, 0.06f, 0.36f));
                SpecialPrefabParts.Box(arm, "Cup", p.Dark, new Vector3(0f, -0.06f, 0.82f), new Vector3(0.2f, 0.04f, 0.2f));
                Transform can = SpecialPrefabParts.Empty(arm, "Can", new Vector3(0f, 0.1f, 0.82f));
                SpecialPrefabParts.Pillar(can, "InkCan", p.Ink, new Vector3(0f, -0.13f, 0f), 0.09f, 0.26f);
                SpecialPrefabParts.Pillar(can, "CanTop", chrome, new Vector3(0f, 0.13f, 0f), 0.085f, 0.03f);
                SpecialPrefabParts.Box(can, "Bolt", p.Glow, new Vector3(0f, 0f, 0.092f), new Vector3(0.05f, 0.14f, 0.01f),
                    new Vector3(0f, 0f, 18f));
                arms[i] = arm;
                cans[i] = can.gameObject;
            }
            var device = root.AddComponent<TacticoolerDevice>();
            SpecialPrefabParts.SetObject(device, "_solid", solid);
            SpecialPrefabParts.SetObject(device, "_obstacle", obstacle);
            SpecialPrefabParts.SetObject(device, "_body", body);
            SpecialPrefabParts.SetObject(device, "_bulb", bulb.GetComponent<Renderer>());
            SetArray(device, "_arms", arms);
            SetArray(device, "_cans", cans);
            GameObject saved = SpecialPrefabParts.SavePrefab(root, DevicePath);
            return saved != null ? saved.GetComponent<TacticoolerDevice>() : null;
        }

        // 手上舉著的壓扁罐子(插槽座標,+Y 朝上):S3 發動時舉著「潰した缶のような装置」
        private static GameObject BuildHeld(SpecialPrefabParts.Palette p, Material chrome)
        {
            var root = new GameObject("TacticoolerHeld");
            Transform t = root.transform;
            SpecialPrefabParts.Pillar(t, "Shell", p.Ivory, new Vector3(0f, -0.02f, 0.05f), 0.15f, 0.3f);
            SpecialPrefabParts.Pillar(t, "InkBand", p.Ink, new Vector3(0f, 0.06f, 0.05f), 0.155f, 0.08f);
            SpecialPrefabParts.Pillar(t, "Lid", chrome, new Vector3(0f, 0.28f, 0.05f), 0.14f, 0.04f);
            SpecialPrefabParts.Ball(t, "InkBulb", p.Glow, new Vector3(0f, 0.37f, 0.05f), Vector3.one * 0.14f);
            return SpecialPrefabParts.SavePrefab(root, HeldPath);
        }

        // 飲用者發光:腳下光環(LineRenderer 圓)+ 三顆光點
        private static TacticoolerAura BuildAura(SpecialPrefabParts.Palette p)
        {
            var root = new GameObject("TacticoolerAura");
            Transform ringRoot = SpecialPrefabParts.Empty(root.transform, "Ring", new Vector3(0f, 0.06f, 0f));
            LineRenderer ring = SpecialPrefabParts.Line(ringRoot, "RingLine", p.Line, 0.08f, false, true,
                SpecialPrefabParts.Circle(32, 0.62f, 0));
            ring.alignment = LineAlignment.View;
            var sparks = new Transform[3];
            for (int i = 0; i < 3; i++)
            {
                Transform spark = SpecialPrefabParts.Ball(root.transform, $"Spark{i}", p.Glow,
                    new Vector3(0.55f, 0.6f, 0f), Vector3.one * 0.13f);
                spark.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                sparks[i] = spark;
            }
            var aura = root.AddComponent<TacticoolerAura>();
            SpecialPrefabParts.SetObject(aura, "_ring", ringRoot);
            SetArray(aura, "_sparks", sparks);
            GameObject saved = SpecialPrefabParts.SavePrefab(root, AuraPath);
            return saved != null ? saved.GetComponent<TacticoolerAura>() : null;
        }

        private static void SetArray(Object target, string field, Object[] values)
        {
            var so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogError($"[SPECIALSETUP] ERROR {target.GetType().Name} 沒有欄位 {field}");
                return;
            }
            prop.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}

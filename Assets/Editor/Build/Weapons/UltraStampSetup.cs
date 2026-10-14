using SplatoonC.Gameplay;
using SplatoonC.Gameplay.Combat;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SplatoonC.EditorBuild
{
    // Ultra Stamp(D2)的資產:手上的大槌(黃握把 + 隊伍色槌頭,槌面朝插槽 +Y、名為 Head 的空物件 = 槌頭中心、
    // 丟出預覽線 InkGuide)、槌頭擋彈判定(UltraStampGuard,Target 圖層方塊)、丟出的槌(InkBullet),
    // 與 Resources/Weapons/Special_ultra_stamp。冪等,M7Setup.Apply 會跑。
    [InitializeOnLoad]
    public static class UltraStampSetup
    {
        private const string HammerPath = SpecialPrefabParts.PrefabFolder + "/UltraStampHammer.prefab";
        private const string GuardPath = SpecialPrefabParts.PrefabFolder + "/UltraStampGuard.prefab";
        private const string ThrownPath = SpecialPrefabParts.PrefabFolder + "/UltraStampThrown.prefab";

        // 握把長與槌頭(公尺):握把 2.95、槌頭中心在握把 2.9 m 處、槌頭圓柱半徑 0.65 × 長 1.3(沿插槽 Y)
        private const float HeadAlong = 2.9f;
        private const float HeadRadius = 0.65f;
        private const float HeadLength = 1.3f;

        static UltraStampSetup()
        {
            System.Action create = Create;
            if (!M7Setup.ExtraSetups.Contains(create))
            {
                M7Setup.ExtraSetups.Add(create);
            }
        }

        [MenuItem("Tools/SplatoonC/Weapons/Ultra Stamp Setup")]
        public static void Create()
        {
            if (Application.isPlaying)
            {
                Debug.LogError("[SPECIALSETUP] ERROR Play mode 中不可建立資產");
                return;
            }
            SpecialPrefabParts.EnsureFolders();
            SpecialPrefabParts.Palette palette = SpecialPrefabParts.CreatePalette();
            Material handle = SpecialPrefabParts.Lit("SpecialStampHandle", new Color(0.86f, 0.95f, 0.18f), 0.05f, 0.55f);
            Material rim = SpecialPrefabParts.Lit("SpecialStampRim", new Color(0.18f, 0.38f, 0.92f), 0.1f, 0.6f);
            GameObject hammer = BuildHammer(palette, handle, rim);
            UltraStampGuard guard = BuildGuard();
            InkBullet thrown = BuildThrown(palette, handle, rim);
            var config = SpecialPrefabParts.ResetConfig<UltraStampConfig>("ultra_stamp");
            M7Setup.SetRef(config, "_hammerPrefab", hammer);
            M7Setup.SetRef(config, "_guardPrefab", guard);
            M7Setup.SetRef(config, "_throwPrefab", thrown);
            M7Setup.SetString(config, "_specialName", "巨大印章槌");
            SpecialPrefabParts.SetFloat(config, "_fallbackCostPoints", 190f);
            AssetDatabase.SaveAssets();
            Debug.Log("[SPECIALSETUP] Ultra Stamp:Special_ultra_stamp、UltraStampHammer、UltraStampGuard、UltraStampThrown");
        }

        // 大槌(插槽座標:+Z = 握把往槌頭、原點 = 右手握把、+Y = 槌面那一側)
        private static GameObject BuildHammer(SpecialPrefabParts.Palette p, Material handle, Material rim)
        {
            var root = new GameObject("UltraStampHammer");
            Transform t = root.transform;
            AddHammerParts(t, p, handle, rim, 0f);
            LineRenderer guide = SpecialPrefabParts.Line(t, "InkGuide", p.Line, 0.14f, true, false, null, 2);
            guide.enabled = false;
            return SpecialPrefabParts.SavePrefab(root, HammerPath);
        }

        // along0 = 握把起點的 Z(手上的:0;丟出的:讓槌頭在前、整支往後排)
        private static void AddHammerParts(Transform t, SpecialPrefabParts.Palette p, Material handle, Material rim,
            float along0)
        {
            SpecialPrefabParts.Rod(t, "Handle", handle, new Vector3(0f, 0f, along0 + 1.12f), 0.07f, 2.95f);
            SpecialPrefabParts.Rod(t, "Band", p.Ivory, new Vector3(0f, 0f, along0 + 0.25f), 0.085f, 0.18f);
            SpecialPrefabParts.Rod(t, "Pommel", rim, new Vector3(0f, 0f, along0 - 0.38f), 0.1f, 0.1f);
            Transform head = SpecialPrefabParts.Empty(t, "Head", new Vector3(0f, 0f, along0 + HeadAlong));
            SpecialPrefabParts.Pillar(head, "InkHead", p.Ink, new Vector3(0f, -HeadLength * 0.5f, 0f), HeadRadius,
                HeadLength);
            SpecialPrefabParts.Pillar(head, "Rim", rim, new Vector3(0f, HeadLength * 0.5f - 0.12f, 0f), HeadRadius + 0.04f, 0.12f);
            SpecialPrefabParts.Pillar(head, "StampFace", p.Dark, new Vector3(0f, HeadLength * 0.5f, 0f), HeadRadius - 0.05f,
                0.05f);
            SpecialPrefabParts.Ball(head, "InkTank", p.Ink, new Vector3(0f, -HeadLength * 0.5f - 0.25f, 0f),
                new Vector3(0.7f, 0.5f, 0.7f));
            SpecialPrefabParts.Pillar(head, "TankCap", p.Ivory, new Vector3(0f, -HeadLength * 0.5f - 0.62f, 0f), 0.14f, 0.14f);
        }

        // 擋彈判定:1.6 m 的方塊(比槌頭略大),Target 圖層;平常關閉
        private static UltraStampGuard BuildGuard()
        {
            var root = new GameObject("UltraStampGuard");
            root.layer = GameLayers.Target;
            var shieldGo = new GameObject("Shield");
            shieldGo.layer = GameLayers.Target;
            shieldGo.transform.SetParent(root.transform, false);
            var box = shieldGo.AddComponent<BoxCollider>();
            box.size = new Vector3(1.6f, 1.6f, 1.6f);
            box.enabled = false;
            var guard = root.AddComponent<UltraStampGuard>();
            SpecialPrefabParts.SetObject(guard, "_shield", box);
            GameObject saved = SpecialPrefabParts.SavePrefab(root, GuardPath);
            return saved != null ? saved.GetComponent<UltraStampGuard>() : null;
        }

        // 丟出的槌:InkBullet 根(沿速度方向)+ 槌頭朝前的整支槌
        private static InkBullet BuildThrown(SpecialPrefabParts.Palette p, Material handle, Material rim)
        {
            var root = new GameObject("UltraStampThrown");
            Transform body = SpecialPrefabParts.Empty(root.transform, "Body", Vector3.zero);
            AddHammerParts(body, p, handle, rim, -HeadAlong + 0.2f);
            var trail = root.AddComponent<TrailRenderer>();
            trail.sharedMaterial = p.Line;
            trail.time = 0.12f;
            trail.minVertexDistance = 0.25f;
            trail.widthMultiplier = 1.2f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            var bullet = root.AddComponent<InkBullet>();
            SpecialPrefabParts.SetObject(bullet, "_visual", body);
            GameObject saved = SpecialPrefabParts.SavePrefab(root, ThrownPath);
            return saved != null ? saved.GetComponent<InkBullet>() : null;
        }
    }
}

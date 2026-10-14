using SplatoonC.Gameplay.Combat;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SplatoonC.EditorBuild
{
    // Trizooka(D1)的資產:墨團 prefab(InkBullet + 拖尾)、手上的三管發射器模型、Resources/Weapons/Special_trizooka。
    // 冪等:每次重建 prefab 與重設設定資產。由 M7Setup.Apply 經 ExtraSetups 呼叫。
    [InitializeOnLoad]
    public static class TrizookaSetup
    {
        private const string GlobPath = SpecialPrefabParts.PrefabFolder + "/TrizookaGlob.prefab";
        private const string LauncherPath = SpecialPrefabParts.PrefabFolder + "/TrizookaLauncher.prefab";

        static TrizookaSetup()
        {
            System.Action create = Create;
            if (!M7Setup.ExtraSetups.Contains(create))
            {
                M7Setup.ExtraSetups.Add(create);
            }
        }

        [MenuItem("Tools/SplatoonC/Weapons/Trizooka Setup")]
        public static void Create()
        {
            if (Application.isPlaying)
            {
                Debug.LogError("[SPECIALSETUP] ERROR Play mode 中不可建立資產");
                return;
            }
            SpecialPrefabParts.EnsureFolders();
            SpecialPrefabParts.Palette palette = SpecialPrefabParts.CreatePalette();
            InkBullet glob = BuildGlob(palette);
            GameObject launcher = BuildLauncher(palette);
            var config = SpecialPrefabParts.ResetConfig<TrizookaConfig>("trizooka");
            M7Setup.SetRef(config, "_globPrefab", glob);
            M7Setup.SetRef(config, "_launcherPrefab", launcher);
            M7Setup.SetString(config, "_specialName", "終極發射");
            SpecialPrefabParts.SetFloat(config, "_fallbackCostPoints", 200f);
            AssetDatabase.SaveAssets();
            Debug.Log("[SPECIALSETUP] Trizooka:Special_trizooka、TrizookaGlob、TrizookaLauncher");
        }

        // 墨團:空根(InkBullet + 粗拖尾)+ Body(拉長球;規格 VisualScale 2.6 放大)。隊伍色由 InkBullet 執行期套上
        private static InkBullet BuildGlob(SpecialPrefabParts.Palette p)
        {
            var root = new GameObject("TrizookaGlob");
            Transform body = SpecialPrefabParts.Ball(root.transform, "Body", p.Glow, Vector3.zero,
                new Vector3(0.34f, 0.34f, 0.5f));
            body.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            var trail = root.AddComponent<TrailRenderer>();
            trail.sharedMaterial = p.Line;
            trail.time = 0.12f;
            trail.minVertexDistance = 0.2f;
            trail.widthMultiplier = 1f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 0.55f), new Keyframe(1f, 0f));
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.numCapVertices = 3;
            var bullet = root.AddComponent<InkBullet>();
            SpecialPrefabParts.SetObject(bullet, "_visual", body);
            GameObject saved = SpecialPrefabParts.SavePrefab(root, GlobPath);
            return saved != null ? saved.GetComponent<InkBullet>() : null;
        }

        // 發射器(插槽座標:+Z 管口方向、原點 = 右手握把):扛在肩上的粗管、前端三支小炮管呈三角形、
        // 後段墨水罐與管口墨環(Ink* = 隊伍色)
        private static GameObject BuildLauncher(SpecialPrefabParts.Palette p)
        {
            var root = new GameObject("TrizookaLauncher");
            Transform t = root.transform;
            SpecialPrefabParts.Box(t, "Grip", p.Dark, new Vector3(0f, -0.02f, 0f), new Vector3(0.06f, 0.16f, 0.08f),
                new Vector3(-12f, 0f, 0f));
            SpecialPrefabParts.Rod(t, "Tube", p.Metal, new Vector3(0f, 0.19f, 0.12f), 0.15f, 1.25f);
            SpecialPrefabParts.Rod(t, "InkTank", p.Ink, new Vector3(0f, 0.2f, -0.42f), 0.13f, 0.42f);
            SpecialPrefabParts.Rod(t, "TankCap", p.Dark, new Vector3(0f, 0.2f, -0.65f), 0.14f, 0.05f);
            SpecialPrefabParts.Rod(t, "Collar", p.Dark, new Vector3(0f, 0.19f, 0.5f), 0.17f, 0.08f);
            for (int i = 0; i < 3; i++)
            {
                float a = (90f + i * 120f) * Mathf.Deg2Rad;
                var offset = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.085f;
                SpecialPrefabParts.Rod(t, $"Barrel{i}", p.Dark, new Vector3(0f, 0.19f, 0.72f) + offset, 0.05f, 0.34f);
            }
            SpecialPrefabParts.Rod(t, "InkMuzzle", p.Ink, new Vector3(0f, 0.19f, 0.86f), 0.16f, 0.04f);
            SpecialPrefabParts.Box(t, "Sight", p.Dark, new Vector3(0f, 0.38f, 0.15f), new Vector3(0.05f, 0.08f, 0.14f));
            SpecialPrefabParts.Box(t, "ForeGrip", p.Dark, new Vector3(0f, 0.04f, 0.32f), new Vector3(0.05f, 0.14f, 0.06f));
            return SpecialPrefabParts.SavePrefab(root, LauncherPath);
        }
    }
}

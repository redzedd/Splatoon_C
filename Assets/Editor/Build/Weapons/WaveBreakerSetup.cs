using SplatoonC.Gameplay;
using SplatoonC.Gameplay.Combat;
using UnityEditor;
using UnityEngine;

namespace SplatoonC.EditorBuild
{
    // Wave Breaker(D1)的資產:場上裝置 prefab(劍玉狀:錐形底座 + 彈跳球,Target 圖層膠囊碰撞體)、衝擊波環 prefab、
    // 手上舉著的小模型,與 Resources/Weapons/Special_wave_breaker(含投擲參數)。冪等,M7Setup.Apply 會跑。
    [InitializeOnLoad]
    public static class WaveBreakerSetup
    {
        private const string DevicePath = SpecialPrefabParts.PrefabFolder + "/WaveBreakerDevice.prefab";
        private const string WavePath = SpecialPrefabParts.PrefabFolder + "/WaveBreakerWave.prefab";
        private const string HeldPath = SpecialPrefabParts.PrefabFolder + "/WaveBreakerHeld.prefab";

        static WaveBreakerSetup()
        {
            System.Action create = Create;
            if (!M7Setup.ExtraSetups.Contains(create))
            {
                M7Setup.ExtraSetups.Add(create);
            }
        }

        [MenuItem("Tools/SplatoonC/Weapons/Wave Breaker Setup")]
        public static void Create()
        {
            if (Application.isPlaying)
            {
                Debug.LogError("[SPECIALSETUP] ERROR Play mode 中不可建立資產");
                return;
            }
            SpecialPrefabParts.EnsureFolders();
            SpecialPrefabParts.Palette palette = SpecialPrefabParts.CreatePalette();
            WaveBreakerDevice device = BuildDevice(palette);
            WaveBreakerWave wave = BuildWave(palette);
            GameObject held = BuildHeld(palette);
            var config = SpecialPrefabParts.ResetConfig<WaveBreakerConfig>("wave_breaker");
            M7Setup.SetRef(config, "_devicePrefab", device);
            M7Setup.SetRef(config, "_wavePrefab", wave);
            M7Setup.SetRef(config, "_heldPrefab", held);
            M7Setup.SetString(config, "_specialName", "彈跳聲納");
            SpecialPrefabParts.SetFloat(config, "_fallbackCostPoints", 210f);
            // 投擲(ShotParam / MoveParam):水平 0.3 u/F(18 m/s)、上 0.07 u/F(4.2 m/s)→ 初速 18.48 m/s、上抬 13.1°;
            // 重力 FlyGravity 0.009 u/F² = 32.4 m/s²(空氣阻力 0.001/F 可忽略);不繼承慣性;不耗墨
            SpecialPrefabParts.SetFloat(config, "_throwSpeed", Mathf.Sqrt(18f * 18f + 4.2f * 4.2f));
            SpecialPrefabParts.SetFloat(config, "_liftAngleDeg", Mathf.Atan2(4.2f, 18f) * Mathf.Rad2Deg);
            SpecialPrefabParts.SetFloat(config, "_gravity", -0.009f * 3600f);
            SpecialPrefabParts.SetFloat(config, "_aimReach", 12f);
            SpecialPrefabParts.SetFloat(config, "_previewStepTime", 0.02f);
            SpecialPrefabParts.SetInt(config, "_previewMaxSteps", 120);
            SpecialPrefabParts.SetFloat(config, "_inkLockSeconds", 0f);
            AssetDatabase.SaveAssets();
            Debug.Log("[SPECIALSETUP] Wave Breaker:Special_wave_breaker、WaveBreakerDevice、WaveBreakerWave、WaveBreakerHeld");
        }

        // 場上裝置:錐形底座(隊伍色杯)+ 細繩 + 彈跳球(隊伍色);膠囊碰撞體涵蓋底座到球的最高點(敵人打得到)
        private static WaveBreakerDevice BuildDevice(SpecialPrefabParts.Palette p)
        {
            var root = new GameObject("WaveBreakerDevice");
            root.layer = GameLayers.Target;
            Transform body = SpecialPrefabParts.Empty(root.transform, "Body", Vector3.zero);
            body.gameObject.layer = GameLayers.Target;
            var collider = body.gameObject.AddComponent<CapsuleCollider>();
            collider.center = new Vector3(0f, 1.1f, 0f);
            collider.radius = 0.4f;
            collider.height = 2.2f;
            SpecialPrefabParts.Pillar(body, "Base", p.Dark, Vector3.zero, 0.45f, 0.18f);
            SpecialPrefabParts.Pillar(body, "InkCone", p.Ink, new Vector3(0f, 0.18f, 0f), 0.34f, 0.55f);
            SpecialPrefabParts.Pillar(body, "Neck", p.Dark, new Vector3(0f, 0.73f, 0f), 0.12f, 0.12f);
            SpecialPrefabParts.Pillar(body, "Cup", p.Ivory, new Vector3(0f, 0.85f, 0f), 0.3f, 0.07f);
            SpecialPrefabParts.Pillar(body, "Cord", p.Dark, new Vector3(0.2f, 0.9f, 0f), 0.012f, 1.2f);
            Transform ball = SpecialPrefabParts.Empty(root.transform, "Ball", new Vector3(0f, 1.05f, 0f));
            ball.gameObject.layer = GameLayers.Target;
            SpecialPrefabParts.Ball(ball, "InkBall", p.Ink, Vector3.zero, Vector3.one * 0.55f);
            SpecialPrefabParts.Ball(ball, "BallShine", p.Glow, new Vector3(0.1f, 0.12f, -0.1f), Vector3.one * 0.12f);
            var device = root.AddComponent<WaveBreakerDevice>();
            SpecialPrefabParts.SetObject(device, "_ball", ball);
            GameObject saved = SpecialPrefabParts.SavePrefab(root, DevicePath);
            return saved != null ? saved.GetComponent<WaveBreakerDevice>() : null;
        }

        // 衝擊波:沿地形的環(世界座標 LineRenderer,loop;點由執行期每幀貼地)+ 環上往上淡出的半透明波牆(網格執行期生成)
        private static WaveBreakerWave BuildWave(SpecialPrefabParts.Palette p)
        {
            var root = new GameObject("WaveBreakerWave");
            Material bandMaterial = SpecialPrefabParts.UnlitTextured("SpecialWaveBand",
                SpecialPrefabParts.VerticalFade("SpecialVerticalFade"));
            var band = new GameObject("Band");
            band.transform.SetParent(root.transform, false);
            var bandFilter = band.AddComponent<MeshFilter>();
            var bandRenderer = band.AddComponent<MeshRenderer>();
            bandRenderer.sharedMaterial = bandMaterial;
            bandRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            bandRenderer.receiveShadows = false;
            bandRenderer.enabled = false;
            LineRenderer ring = root.AddComponent<LineRenderer>();
            ring.sharedMaterial = p.Line;
            ring.useWorldSpace = true;
            ring.loop = true;
            ring.alignment = LineAlignment.TransformZ;
            ring.widthMultiplier = 0.5f;
            ring.positionCount = 72;
            ring.numCornerVertices = 2;
            ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring.receiveShadows = false;
            ring.enabled = false;
            var wave = root.AddComponent<WaveBreakerWave>();
            SpecialPrefabParts.SetObject(wave, "_ring", ring);
            SpecialPrefabParts.SetObject(wave, "_band", bandFilter);
            GameObject saved = SpecialPrefabParts.SavePrefab(root, WavePath);
            return saved != null ? saved.GetComponent<WaveBreakerWave>() : null;
        }

        // 手上舉著的縮小版(插槽座標,+Y 朝上)
        private static GameObject BuildHeld(SpecialPrefabParts.Palette p)
        {
            var root = new GameObject("WaveBreakerHeld");
            Transform t = root.transform;
            SpecialPrefabParts.Pillar(t, "Base", p.Dark, new Vector3(0f, -0.05f, 0f), 0.16f, 0.06f);
            SpecialPrefabParts.Pillar(t, "InkCone", p.Ink, new Vector3(0f, 0.01f, 0f), 0.12f, 0.2f);
            SpecialPrefabParts.Pillar(t, "Cup", p.Ivory, new Vector3(0f, 0.21f, 0f), 0.11f, 0.03f);
            SpecialPrefabParts.Ball(t, "InkBall", p.Ink, new Vector3(0f, 0.33f, 0f), Vector3.one * 0.2f);
            return SpecialPrefabParts.SavePrefab(root, HeldPath);
        }
    }
}

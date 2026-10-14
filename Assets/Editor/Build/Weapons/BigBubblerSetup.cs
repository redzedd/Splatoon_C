using SplatoonC.Gameplay;
using SplatoonC.Gameplay.Combat;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SplatoonC.EditorBuild
{
    // Big Bubbler(D1)的資產:裝置 prefab(地面發射座 + 頂端無人機 = 弱點碰撞體、Target 圖層;
    // 半透明雙面屏障球 + 三圈球面線,沒有碰撞體)與 Resources/Weapons/Special_big_bubbler。冪等,M7Setup.Apply 會跑。
    [InitializeOnLoad]
    public static class BigBubblerSetup
    {
        private const string DevicePath = SpecialPrefabParts.PrefabFolder + "/BigBubblerDevice.prefab";

        static BigBubblerSetup()
        {
            System.Action create = Create;
            if (!M7Setup.ExtraSetups.Contains(create))
            {
                M7Setup.ExtraSetups.Add(create);
            }
        }

        [MenuItem("Tools/SplatoonC/Weapons/Big Bubbler Setup")]
        public static void Create()
        {
            if (Application.isPlaying)
            {
                Debug.LogError("[SPECIALSETUP] ERROR Play mode 中不可建立資產");
                return;
            }
            SpecialPrefabParts.EnsureFolders();
            SpecialPrefabParts.Palette palette = SpecialPrefabParts.CreatePalette();
            BigBubblerDevice device = BuildDevice(palette);
            var config = SpecialPrefabParts.ResetConfig<BigBubblerConfig>("big_bubbler");
            M7Setup.SetRef(config, "_devicePrefab", device);
            M7Setup.SetString(config, "_specialName", "巨大防護罩");
            SpecialPrefabParts.SetFloat(config, "_fallbackCostPoints", 180f);
            AssetDatabase.SaveAssets();
            Debug.Log("[SPECIALSETUP] Big Bubbler:Special_big_bubbler、BigBubblerDevice");
        }

        private static BigBubblerDevice BuildDevice(SpecialPrefabParts.Palette p)
        {
            var root = new GameObject("BigBubblerDevice");
            root.layer = GameLayers.Target;

            // 地面發射座(陀螺狀,頂端一個 Squid Beakon):弱點碰撞體
            Transform launcher = SpecialPrefabParts.Empty(root.transform, "Launcher", Vector3.zero);
            launcher.gameObject.layer = GameLayers.Target;
            var launcherCollider = launcher.gameObject.AddComponent<CapsuleCollider>();
            launcherCollider.center = new Vector3(0f, 0.6f, 0f);
            launcherCollider.radius = 0.45f;
            launcherCollider.height = 1.2f;
            SpecialPrefabParts.Pillar(launcher, "LauncherBase", p.Dark, Vector3.zero, 0.5f, 0.22f);
            SpecialPrefabParts.Pillar(launcher, "InkBody", p.Ink, new Vector3(0f, 0.22f, 0f), 0.38f, 0.55f);
            SpecialPrefabParts.Pillar(launcher, "Collar", p.Ivory, new Vector3(0f, 0.77f, 0f), 0.3f, 0.08f);
            SpecialPrefabParts.Pillar(launcher, "BeakonPole", p.Dark, new Vector3(0f, 0.85f, 0f), 0.05f, 0.25f);
            SpecialPrefabParts.Ball(launcher, "InkBeakon", p.Ink, new Vector3(0f, 1.08f, 0f), Vector3.one * 0.3f);
            SpecialPrefabParts.Ball(launcher, "BeakonLight", p.Glow, new Vector3(0f, 1.22f, 0f), Vector3.one * 0.12f);
            SpecialPrefabParts.Box(launcher, "Ripcord", p.Dark, new Vector3(0.4f, 0.45f, 0f), new Vector3(0.05f, 0.05f, 0.35f),
                new Vector3(0f, 30f, 0f));

            // 無人機(半球 + 白色螺旋槳):隨屏障頂端移動,弱點碰撞體
            Transform drone = SpecialPrefabParts.Empty(root.transform, "Drone", new Vector3(0f, 1.2f, 0f));
            drone.gameObject.layer = GameLayers.Target;
            var droneCollider = drone.gameObject.AddComponent<SphereCollider>();
            droneCollider.center = new Vector3(0f, 0.15f, 0f);
            droneCollider.radius = 0.55f;
            SpecialPrefabParts.Ball(drone, "InkDome", p.Ink, new Vector3(0f, 0.05f, 0f), new Vector3(1.1f, 0.6f, 1.1f));
            SpecialPrefabParts.Pillar(drone, "DroneRim", p.Dark, new Vector3(0f, -0.05f, 0f), 0.58f, 0.1f);
            SpecialPrefabParts.Pillar(drone, "Mast", p.Dark, new Vector3(0f, 0.3f, 0f), 0.05f, 0.25f);
            Transform propeller = SpecialPrefabParts.Empty(drone, "Propeller", new Vector3(0f, 0.55f, 0f));
            SpecialPrefabParts.Box(propeller, "BladeA", p.Ivory, Vector3.zero, new Vector3(1.3f, 0.03f, 0.14f),
                new Vector3(8f, 0f, 0f));
            SpecialPrefabParts.Box(propeller, "BladeB", p.Ivory, Vector3.zero, new Vector3(0.14f, 0.03f, 1.3f),
                new Vector3(0f, 0f, 8f));

            // 屏障(直徑 1 的球;裝置依半徑縮放):雙面半透明球 + 三圈球面線,沒有碰撞體(敵人走得進來)
            Transform barrier = SpecialPrefabParts.Empty(root.transform, "Barrier", new Vector3(0f, 1f, 0f));
            barrier.gameObject.layer = 0;
            Transform glass = SpecialPrefabParts.Ball(barrier, "GlassSphere", p.Glass, Vector3.zero, Vector3.one);
            var glassRenderer = glass.GetComponent<Renderer>();
            glassRenderer.shadowCastingMode = ShadowCastingMode.Off;
            glassRenderer.receiveShadows = false;
            SpecialPrefabParts.Line(barrier, "GlassRingEquator", p.Line, 0.05f, false, true,
                SpecialPrefabParts.Circle(64, 0.5f, 0));
            SpecialPrefabParts.Line(barrier, "GlassRingMeridianA", p.Line, 0.05f, false, true,
                SpecialPrefabParts.Circle(64, 0.5f, 1));
            SpecialPrefabParts.Line(barrier, "GlassRingMeridianB", p.Line, 0.05f, false, true,
                SpecialPrefabParts.Circle(64, 0.5f, 2));
            Transform upper = SpecialPrefabParts.Line(barrier, "GlassRingUpper", p.Line, 0.04f, false, true,
                SpecialPrefabParts.Circle(64, 0.35f, 0)).transform;
            upper.localPosition = new Vector3(0f, 0.357f, 0f);

            var device = root.AddComponent<BigBubblerDevice>();
            SpecialPrefabParts.SetObject(device, "_launcherCollider", launcherCollider);
            SpecialPrefabParts.SetObject(device, "_drone", drone);
            SpecialPrefabParts.SetObject(device, "_droneCollider", droneCollider);
            SpecialPrefabParts.SetObject(device, "_propeller", propeller);
            SpecialPrefabParts.SetObject(device, "_barrier", barrier);
            GameObject saved = SpecialPrefabParts.SavePrefab(root, DevicePath);
            return saved != null ? saved.GetComponent<BigBubblerDevice>() : null;
        }
    }
}

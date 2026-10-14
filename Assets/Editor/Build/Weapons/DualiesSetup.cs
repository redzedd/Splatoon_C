using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Combat;
using UnityEditor;
using UnityEngine;

namespace SplatoonC.EditorBuild
{
    // 雙槍(Splat Dualies):S3 原始參數(S3DualiesParams,Leanny v11.3.0 Maneuver_Normal)→ InkBulletSpec 與 DualiesConfig,
    // 寫到 Assets/Resources/Weapons/Main_splat_dualies.asset(執行期 Resources.Load("Weapons/Main_splat_dualies"));
    // 另產生程序化槍模 prefab(左右手共用一把的外型)與材質。冪等,Tools/SplatoonC/M7 Setup 會跑。
    [InitializeOnLoad]
    public static class DualiesSetup
    {
        public const string ConfigPath = "Assets/Resources/Weapons/Main_splat_dualies.asset";
        private const string PrefabFolder = "Assets/Prefabs/Weapons";
        private const string GunPrefabPath = PrefabFolder + "/SplatDualiesGun.prefab";
        private const string MaterialFolder = "Assets/Materials/Weapons";

        // 新音效 id(音效單元之後合成;未合成前 DualiesWeapon 退回 shoot_shooter / squid_dive)
        public const string FireSound = "shoot_dualies";
        public const string RollSound = "dualies_roll";

        static DualiesSetup()
        {
            M7Setup.ExtraSetups.Add(Create);
        }

        [MenuItem("Tools/SplatoonC/Weapons/Dualies Setup")]
        public static void Create()
        {
            if (Application.isPlaying)
            {
                Debug.LogError("[DUALIESSETUP] ERROR Play mode 中不可建立資產");
                return;
            }
            M7Setup.EnsureFolder("Assets", "Resources");
            M7Setup.EnsureFolder("Assets/Resources", "Weapons");
            M7Setup.EnsureFolder("Assets", "Prefabs");
            M7Setup.EnsureFolder("Assets/Prefabs", "Weapons");
            M7Setup.EnsureFolder("Assets", "Materials");
            M7Setup.EnsureFolder("Assets/Materials", "Weapons");

            var palette = new Palette
            {
                Body = EnsureLitMaterial("DualiesBody", new Color(0.16f, 0.17f, 0.2f), 0.15f, 0.55f),
                Trim = EnsureLitMaterial("DualiesTrim", new Color(0.78f, 0.79f, 0.8f), 0.55f, 0.7f),
                Grip = EnsureLitMaterial("DualiesGrip", new Color(0.07f, 0.07f, 0.08f), 0f, 0.3f),
                Ink = EnsureLitMaterial("DualiesInk", Color.white, 0f, 0.92f),
            };
            GameObject gun = CreateGunPrefab(palette);
            if (gun == null)
            {
                Debug.LogError("[DUALIESSETUP] ERROR 槍模 prefab 建立失敗");
                return;
            }
            WriteConfig(gun);
            AssetDatabase.SaveAssets();
            Debug.Log($"[DUALIESSETUP] 雙槍:設定 {ConfigPath}、槍模 {GunPrefabPath}");
        }

        // ── 設定資產 ──

        private static void WriteConfig(GameObject gun)
        {
            var config = AssetDatabase.LoadAssetAtPath<DualiesConfig>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<DualiesConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }
            S3DualiesParams s3 = S3DualiesParams.SplatDualies;
            InkBulletSpec bullet = BuildBullet(s3, s3.RadiusForPlayer);
            InkBulletSpec turret = BuildBullet(s3, s3.TurretRadiusForPlayer);
            config.Setup(gun, null, s3, bullet, turret, FireSound, RollSound);
            config.name = "Main_splat_dualies";
            EditorUtility.SetDirty(config);
        }

        // S3 MoveParam / DamageParam / CollisionParam / PaintParam 原樣進 InkBulletSpec
        public static InkBulletSpec BuildBullet(in S3DualiesParams s3, float radiusForPlayer)
        {
            InkBulletSpec spec = InkBulletSpec.ShooterFamily(s3.SpawnSpeed, s3.StraightFrames, s3.StraightEndMaxSpeed,
                s3.DamageMax, s3.DamageMin, s3.ReduceStartFrame, s3.ReduceEndFrame, radiusForPlayer, AttackClass.Dualies);
            spec.RadiusForField = s3.RadiusForField;
            spec.PaintWidthHalfNear = s3.WidthHalfNear;
            spec.PaintWidthHalfFar = s3.WidthHalfFar;
            spec.PaintDepthScaleMax = s3.DepthScaleMax;
            spec.PaintDepthScaleMin = s3.DepthScaleMin;
            spec.PaintDepthScaleBreakFree = s3.DepthScaleMinBreakFree;
            spec.PaintRadiusShock = s3.PaintRadiusShock;
            // 墨滴:每發 1 滴(SpawnNum 1.0)、間距 14 u;落點相位由 DualiesWeapon 逐發輪替 7 種樣式(S3DualiesParams.DropletPhase)。
            // 墨滴塗墨半徑 = S3 SplashPaintParam.WidthHalf 1.558(與落點橢圓一樣原樣用 S3 單位)× √1.1:
            // S3 墨滴落差 < 3 u 拉長 ×1.2、> 10 u ×1.0,取平均 ×1.1 的等面積圓 [EST] ≈ 1.63
            spec.DripSpacing = s3.SpawnBetweenLength;
            spec.DripRadius = s3.DropletWidthHalf * Mathf.Sqrt(1.1f);
            spec.DripMaxCount = Mathf.CeilToInt(s3.SpawnNum);
            spec.VisualScale = 0.85f;
            return spec;
        }

        // ── 槍模 prefab(WeaponSocket 座標:+Z 槍管、+Y 上方/拇指側、原點 = 手掌握把點;單位公尺,全長約 0.27 m)──
        // Splat Dualies:深灰小手槍、槍身上方一支橫放的透明墨槽(隊伍色)、淺色槍管護套、墨色槍口

        private struct Palette
        {
            public Material Body;
            public Material Trim;
            public Material Grip;
            public Material Ink;
        }

        private static GameObject CreateGunPrefab(Palette p)
        {
            var root = new GameObject("SplatDualiesGun");
            try
            {
                Transform t = root.transform;
                Box(t, "Grip", p.Grip, new Vector3(0f, -0.008f, -0.008f), new Vector3(0.03f, 0.082f, 0.036f), 14f);
                Box(t, "Body", p.Body, new Vector3(0f, 0.058f, 0.04f), new Vector3(0.052f, 0.062f, 0.15f));
                Ball(t, "RearCap", p.Body, new Vector3(0f, 0.058f, -0.035f), new Vector3(0.052f, 0.062f, 0.05f));
                Box(t, "TriggerGuard", p.Body, new Vector3(0f, 0.018f, 0.035f), new Vector3(0.012f, 0.03f, 0.036f));
                Box(t, "SideStripe", p.Trim, new Vector3(0f, 0.042f, 0.05f), new Vector3(0.055f, 0.011f, 0.11f));
                Rod(t, "InkTank", p.Ink, new Vector3(0f, 0.106f, 0.035f), 0.021f, 0.1f);
                Rod(t, "TankCapFront", p.Trim, new Vector3(0f, 0.106f, 0.087f), 0.024f, 0.012f);
                Rod(t, "TankCapRear", p.Trim, new Vector3(0f, 0.106f, -0.017f), 0.024f, 0.012f);
                Rod(t, "Shroud", p.Trim, new Vector3(0f, 0.06f, 0.135f), 0.02f, 0.06f);
                Rod(t, "Barrel", p.Body, new Vector3(0f, 0.06f, 0.18f), 0.012f, 0.04f);
                Rod(t, "InkNozzle", p.Ink, new Vector3(0f, 0.06f, 0.2f), 0.016f, 0.016f);
                var muzzle = new GameObject("Muzzle").transform;
                muzzle.SetParent(t, false);
                muzzle.localPosition = new Vector3(0f, 0.06f, 0.212f);
                return PrefabUtility.SaveAsPrefabAsset(root, GunPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void Box(Transform parent, string name, Material material, Vector3 position, Vector3 size,
            float pitchDeg = 0f)
        {
            Transform t = Primitive(parent, name, PrimitiveType.Cube, material);
            t.localPosition = position;
            t.localRotation = Quaternion.Euler(pitchDeg, 0f, 0f);
            t.localScale = size;
        }

        // 圓柱沿 +Z(Unity 圓柱沿 Y、高 2、半徑 0.5)
        private static void Rod(Transform parent, string name, Material material, Vector3 position, float radius,
            float length)
        {
            Transform t = Primitive(parent, name, PrimitiveType.Cylinder, material);
            t.localPosition = position;
            t.localRotation = Quaternion.Euler(90f, 0f, 0f);
            t.localScale = new Vector3(radius * 2f, length * 0.5f, radius * 2f);
        }

        private static void Ball(Transform parent, string name, Material material, Vector3 position, Vector3 size)
        {
            Transform t = Primitive(parent, name, PrimitiveType.Sphere, material);
            t.localPosition = position;
            t.localScale = size;
        }

        // 基本形去掉 collider:槍模純視覺,不能擋墨彈或瞄準射線
        private static Transform Primitive(Transform parent, string name, PrimitiveType type, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent, false);
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return go.transform;
        }

        // URP Lit;墨水部位是白底,執行期以 MaterialPropertyBlock 套隊伍色
        private static Material EnsureLitMaterial(string name, Color color, float metallic, float smoothness)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                {
                    Debug.LogError("[DUALIESSETUP] ERROR 找不到 URP Lit shader");
                    return null;
                }
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}

using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Combat;
using UnityEditor;
using UnityEngine;

namespace SplatoonC.EditorBuild
{
    // 射擊槍變體(Splattershot Jr. / .52 Gal / N-ZAP '85 / Splattershot Pro):
    // S3 原始參數(S3ShooterParams,Leanny v11.3.0)原樣寫成 S3 墨彈規格(InkBulletSpec,實際使用的彈道),
    // 舊的兩段式 InkProjectile 欄位也照 ShooterCalibration 換算保留(沒有墨彈規格時的後備)。寫進 WeaponConfig,
    // 寫到 Assets/Resources/Weapons/Main_<id>.asset(執行期 Resources.Load("Weapons/Main_<id>"));
    // 另產生各自的墨彈 prefab(彈徑)、程序化槍模 prefab 與材質。冪等,Tools/SplatoonC/M7 Setup 會跑。
    [InitializeOnLoad]
    public static class ShooterVariantsSetup
    {
        private const string BaseConfigPath = "Assets/Data/WeaponConfig.asset";
        private const string BaseBulletPath = "Assets/Prefabs/InkProjectile.prefab";
        private const string ConfigFolder = "Assets/Resources/Weapons";
        private const string PrefabFolder = "Assets/Prefabs/Weapons";
        private const string MaterialFolder = "Assets/Materials/Weapons";
        private const string GunBodyMaterialPath = "Assets/Materials/GunBody.mat";

        private enum GunStyle
        {
            Junior,
            Gal,
            Zap,
            Pro,
        }

        private struct Variant
        {
            public string Id;
            public string Pascal;
            public S3ShooterParams S3;
            public string FireSound;
            // 墨彈外觀:寬(xy)與長(z)相對 Splattershot 墨彈(0.62 × 1.24)的倍率
            public float BulletWidth;
            public float BulletLength;
            public GunStyle Style;
        }

        private static Variant[] Variants => new[]
        {
            // Jr.:對人判定 0.3(Splattershot 0.285)→ 彈略粗
            new Variant
            {
                Id = "splattershot_jr", Pascal = "SplattershotJr", S3 = S3ShooterParams.SplattershotJr,
                FireSound = "shoot_shooter_jr", BulletWidth = 1.05f, BulletLength = 1f, Style = GunStyle.Junior,
            },
            // .52 Gal:大口徑,彈明顯粗
            new Variant
            {
                Id = "52_gal", Pascal = "Gal52", S3 = S3ShooterParams.Gal52,
                FireSound = "shoot_shooter_52gal", BulletWidth = 1.3f, BulletLength = 1.2f, Style = GunStyle.Gal,
            },
            new Variant
            {
                Id = "n_zap_85", Pascal = "NZap85", S3 = S3ShooterParams.NZap85,
                FireSound = "shoot_shooter_nzap", BulletWidth = 0.95f, BulletLength = 0.95f, Style = GunStyle.Zap,
            },
            // Pro:對人判定 0.235、初速 230 m/s → 細長彈
            new Variant
            {
                Id = "splattershot_pro", Pascal = "SplattershotPro", S3 = S3ShooterParams.SplattershotPro,
                FireSound = "shoot_shooter_pro", BulletWidth = 0.82f, BulletLength = 1.3f, Style = GunStyle.Pro,
            },
        };

        static ShooterVariantsSetup()
        {
            M7Setup.ExtraSetups.Add(Create);
        }

        [MenuItem("Tools/SplatoonC/Weapons/Shooter Variants Setup")]
        public static void Create()
        {
            if (Application.isPlaying)
            {
                Debug.LogError("[SHOOTERSETUP] ERROR Play mode 中不可建立資產");
                return;
            }
            var baseConfig = M7Setup.Load<WeaponConfig>(BaseConfigPath);
            if (baseConfig == null)
            {
                return;
            }
            M7Setup.EnsureFolder("Assets", "Resources");
            M7Setup.EnsureFolder("Assets/Resources", "Weapons");
            M7Setup.EnsureFolder("Assets", "Prefabs");
            M7Setup.EnsureFolder("Assets/Prefabs", "Weapons");
            M7Setup.EnsureFolder("Assets", "Materials");
            M7Setup.EnsureFolder("Assets/Materials", "Weapons");

            // Splattershot 本身:全傷距離改用 S3 有效射程(11.56 − 0.285),與變體同一把尺
            var baseSo = new SerializedObject(baseConfig);
            SetFloat(baseSo, "_damageFalloffStart", ShooterCalibration.DamageFalloffStart(S3ShooterParams.Splattershot));
            baseSo.ApplyModifiedPropertiesWithoutUndo();
            baseConfig.SetBullet(BuildBullet(S3ShooterParams.Splattershot, 1f), S3ShooterParams.Splattershot.SplitNum);
            EditorUtility.SetDirty(baseConfig);

            var palette = new Palette
            {
                Dark = AssetDatabase.LoadAssetAtPath<Material>(GunBodyMaterialPath),
                Ink = EnsureLitMaterial("ShooterInk", Color.white, 0f, 0.92f),
                Ivory = EnsureLitMaterial("ShooterIvory", new Color(0.93f, 0.9f, 0.82f), 0f, 0.55f),
                Gunmetal = EnsureLitMaterial("ShooterGunmetal", new Color(0.5f, 0.53f, 0.57f), 0.35f, 0.62f),
                Charcoal = EnsureLitMaterial("ShooterCharcoal", new Color(0.2f, 0.21f, 0.23f), 0.1f, 0.45f),
                RetroRed = EnsureLitMaterial("ShooterRetroRed", new Color(0.62f, 0.16f, 0.14f), 0f, 0.5f),
                Gold = EnsureLitMaterial("ShooterGold", new Color(0.95f, 0.74f, 0.3f), 0.35f, 0.75f),
            };
            if (palette.Dark == null)
            {
                palette.Dark = EnsureLitMaterial("ShooterDark", new Color(0.12f, 0.13f, 0.16f), 0f, 0.6f);
            }

            int count = 0;
            foreach (Variant variant in Variants)
            {
                InkProjectile bullet = CreateBulletPrefab(variant);
                GameObject gun = CreateGunPrefab(variant, palette);
                if (bullet == null || gun == null)
                {
                    continue;
                }
                WriteConfig(variant, baseConfig, bullet, gun);
                count++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[SHOOTERSETUP] 射擊槍變體 {count} 把:設定 {ConfigFolder}/Main_*.asset、墨彈與槍模 {PrefabFolder}");
        }

        // ── 設定資產 ──

        private static void WriteConfig(Variant v, WeaponConfig baseConfig, InkProjectile bullet, GameObject gun)
        {
            string path = $"{ConfigFolder}/Main_{v.Id}.asset";
            var config = AssetDatabase.LoadAssetAtPath<WeaponConfig>(path);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<WeaponConfig>();
                AssetDatabase.CreateAsset(config, path);
            }

            // 先整份照抄 Splattershot(墨滴 prefab、命中圖層、滴墨物理、硬度等沒有 S3 對應的手感值),再覆寫 S3 換算值
            var src = new SerializedObject(baseConfig);
            var so = new SerializedObject(config);
            SerializedProperty it = src.GetIterator();
            bool enterChildren = true;
            while (it.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (it.propertyPath == "m_Script")
                {
                    continue;
                }
                so.CopyFromSerializedProperty(it);
            }

            S3ShooterParams s3 = v.S3;
            SetFloat(so, "_moveSpeedWhileFiring", s3.MoveSpeedWhileFiring);
            SetString(so, "_fireSound", v.FireSound);
            SetObject(so, "_projectilePrefab", bullet);
            SetObject(so, "_gunModelPrefab", gun);

            SetFloat(so, "_fireInterval", s3.FireIntervalSeconds);
            SetFloat(so, "_inkCostPerShot", s3.InkConsume);
            SetFloat(so, "_inkLockSeconds", s3.InkLockSeconds);
            SetFloat(so, "_inkTankScale", s3.InkTankScale);
            WriteSpread(so.FindProperty("_spread"), s3.ToSpreadSettings());

            SetFloat(so, "_muzzleSpeed", ShooterCalibration.MuzzleSpeed(s3));
            SetFloat(so, "_straightRange", ShooterCalibration.StraightRange(s3));
            SetFloat(so, "_straightGravity", ShooterCalibration.StraightGravity(s3));
            SetFloat(so, "_dropGravity", ShooterCalibration.DropGravity(s3));
            SetFloat(so, "_dropHorizontalDrag", ShooterCalibration.DropHorizontalDrag(s3));

            SetInt(so, "_dripsPerShot", ShooterCalibration.DripsPerShot(s3));
            SetFloat(so, "_dripRangeMultiplier", ShooterCalibration.DripRangeMultiplier(s3));
            SetFloat(so, "_dripRadius", ShooterCalibration.DripRadius(s3));
            SetFloat(so, "_muzzleSplashRadius", ShooterCalibration.MuzzleSplashRadius(s3));
            SetFloat(so, "_splatRadius", ShooterCalibration.SplatRadius(s3));
            SetFloat(so, "_splashRadius", ShooterCalibration.SplashRadius(s3));

            SetFloat(so, "_damage", s3.DamageMax);
            SetFloat(so, "_minDamage", s3.DamageMin);
            SetFloat(so, "_damageDecayDistance", ShooterCalibration.DamageDecayDistance(s3));
            SetFloat(so, "_damageFalloffStart", ShooterCalibration.DamageFalloffStart(s3));
            so.ApplyModifiedPropertiesWithoutUndo();
            config.SetBullet(BuildBullet(s3, v.BulletWidth), s3.SplitNum);
            config.name = $"Main_{v.Id}";
            EditorUtility.SetDirty(config);
        }

        // S3 MoveParam / DamageParam / CollisionParam / PaintParam / SplashSpawnParam 原樣進 InkBulletSpec(同 DualiesSetup.BuildBullet)。
        // 攻擊類別在發射時照 WeaponConfig.AttackClass 覆寫(.52 Gal = HeavyShooter 等)。
        public static InkBulletSpec BuildBullet(in S3ShooterParams s3, float visualScale)
        {
            InkBulletSpec spec = InkBulletSpec.ShooterFamily(s3.SpawnSpeed, s3.StraightFrames, s3.StraightEndMaxSpeed,
                s3.DamageMax, s3.DamageMin, s3.ReduceStartFrame, s3.ReduceEndFrame, s3.EndRadiusForPlayer);
            spec.RadiusForField = s3.RadiusForField;
            spec.PaintWidthHalfNear = s3.WidthHalfNear;
            spec.PaintWidthHalfFar = s3.WidthHalfFar;
            spec.PaintDepthScaleMax = s3.DepthScaleMax;
            spec.PaintDepthScaleMin = s3.DepthScaleMin;
            spec.PaintDepthScaleBreakFree = s3.DepthScaleMinBreakFree;
            spec.PaintRadiusShock = s3.PaintRadiusShock;
            // 沿路墨滴:每 SpawnBetweenLength 一滴、每發最多 ceil(SpawnNum) 滴,起點相位由 InkShooter 逐發輪替 SplitNum 種;
            // 墨滴塗墨半徑 = S3 SplashPaintParam.WidthHalf × √1.1(落差 < 3 u 拉長 ×1.2、> 10 u ×1.0 的平均,與雙槍同一套)
            spec.DripSpacing = s3.SpawnBetweenLength;
            spec.DripRadius = s3.DropletWidthHalf * Mathf.Sqrt(1.1f);
            spec.DripMaxCount = Mathf.CeilToInt(s3.SpawnNum);
            spec.VisualScale = visualScale;
            return spec;
        }

        private static void WriteSpread(SerializedProperty spread, SpreadSettings s)
        {
            if (spread == null)
            {
                Debug.LogError("[SHOOTERSETUP] ERROR WeaponConfig 沒有 _spread 欄位");
                return;
            }
            spread.FindPropertyRelative("StandSwerveDeg").floatValue = s.StandSwerveDeg;
            spread.FindPropertyRelative("JumpSwerveDeg").floatValue = s.JumpSwerveDeg;
            spread.FindPropertyRelative("BiasMin").floatValue = s.BiasMin;
            spread.FindPropertyRelative("BiasKf").floatValue = s.BiasKf;
            spread.FindPropertyRelative("BiasMax").floatValue = s.BiasMax;
            spread.FindPropertyRelative("BiasDecreasePerSecond").floatValue = s.BiasDecreasePerSecond;
            spread.FindPropertyRelative("RepeatSeconds").floatValue = s.RepeatSeconds;
            spread.FindPropertyRelative("JumpBiasMax").floatValue = s.JumpBiasMax;
            spread.FindPropertyRelative("JumpShrinkStartSeconds").floatValue = s.JumpShrinkStartSeconds;
            spread.FindPropertyRelative("JumpShrinkEndSeconds").floatValue = s.JumpShrinkEndSeconds;
        }

        // ── 墨彈 prefab:Splattershot 墨彈換彈徑與拖尾寬 ──

        private static InkProjectile CreateBulletPrefab(Variant v)
        {
            string path = $"{PrefabFolder}/{v.Pascal}Bullet.prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(BaseBulletPath);
            try
            {
                Vector3 scale = root.transform.localScale;
                root.transform.localScale = new Vector3(scale.x * v.BulletWidth, scale.y * v.BulletWidth,
                    scale.z * v.BulletLength);
                var trail = root.GetComponent<TrailRenderer>();
                if (trail != null)
                {
                    trail.widthMultiplier *= v.BulletWidth;
                }
                root.name = $"{v.Pascal}Bullet";
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
                return saved != null ? saved.GetComponent<InkProjectile>() : null;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ── 槍模 prefab(程序化基本形;座標系 = 角色內建 GunBarrel 的位置,槍口在 +Z 0.6)──

        private struct Palette
        {
            public Material Dark;
            public Material Ink;
            public Material Ivory;
            public Material Gunmetal;
            public Material Charcoal;
            public Material RetroRed;
            public Material Gold;
        }

        private static GameObject CreateGunPrefab(Variant v, Palette p)
        {
            string path = $"{PrefabFolder}/{v.Pascal}Gun.prefab";
            var root = new GameObject($"{v.Pascal}Gun");
            try
            {
                root.AddComponent<ShooterGunModel>();
                switch (v.Style)
                {
                    case GunStyle.Junior:
                        BuildJunior(root.transform, p);
                        break;
                    case GunStyle.Gal:
                        BuildGal(root.transform, p);
                        break;
                    case GunStyle.Zap:
                        BuildZap(root.transform, p);
                        break;
                    default:
                        BuildPro(root.transform, p);
                        break;
                }
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // Splattershot Jr.:玩具感的圓胖槍身,頂上一顆奶瓶墨槽
        private static void BuildJunior(Transform root, Palette p)
        {
            Box(root, "Body", p.Ivory, new Vector3(0f, 0f, 0.02f), new Vector3(0.2f, 0.22f, 0.6f));
            Box(root, "Grip", p.Dark, new Vector3(0f, -0.18f, -0.16f), new Vector3(0.1f, 0.24f, 0.12f), -15f);
            Box(root, "Trigger", p.Dark, new Vector3(0f, -0.13f, -0.03f), new Vector3(0.03f, 0.08f, 0.03f));
            Rod(root, "Barrel", p.Dark, new Vector3(0f, 0.01f, 0.43f), 0.06f, 0.26f);
            Rod(root, "InkNozzle", p.Ink, new Vector3(0f, 0.01f, 0.56f), 0.075f, 0.06f);
            Rod(root, "InkBottle", p.Ink, new Vector3(0f, 0.17f, -0.06f), 0.085f, 0.26f);
            Ball(root, "BottleCap", p.Ivory, new Vector3(0f, 0.17f, 0.1f), 0.1f);
            Box(root, "Sight", p.Dark, new Vector3(0f, 0.14f, 0.24f), new Vector3(0.04f, 0.06f, 0.04f));
        }

        // .52 Gal:短胖大口徑,外側(+X,背對角色身體)掛墨罐
        private static void BuildGal(Transform root, Palette p)
        {
            Box(root, "Body", p.Gunmetal, new Vector3(0f, 0f, -0.02f), new Vector3(0.22f, 0.25f, 0.58f));
            Box(root, "TopBlock", p.Dark, new Vector3(0f, 0.155f, -0.06f), new Vector3(0.15f, 0.06f, 0.34f));
            Box(root, "Grip", p.Dark, new Vector3(0f, -0.2f, -0.17f), new Vector3(0.11f, 0.26f, 0.13f), -12f);
            Rod(root, "Barrel", p.Gunmetal, new Vector3(0f, 0.02f, 0.4f), 0.095f, 0.3f);
            Rod(root, "InkMuzzle", p.Ink, new Vector3(0f, 0.02f, 0.56f), 0.108f, 0.06f);
            Rod(root, "InkCanister", p.Ink, new Vector3(0.15f, 0.03f, -0.02f), 0.07f, 0.36f);
            Rod(root, "CanisterBand", p.Dark, new Vector3(0.15f, 0.03f, -0.02f), 0.075f, 0.05f);
        }

        // N-ZAP '85:細長方正的復古槍身,紅色飾條、側面墨窗
        private static void BuildZap(Transform root, Palette p)
        {
            Box(root, "Body", p.Charcoal, new Vector3(0f, 0f, -0.04f), new Vector3(0.14f, 0.2f, 0.74f));
            Box(root, "Stripe", p.RetroRed, new Vector3(0f, 0.05f, 0f), new Vector3(0.146f, 0.04f, 0.52f));
            Box(root, "TopRail", p.Dark, new Vector3(0f, 0.125f, -0.06f), new Vector3(0.06f, 0.05f, 0.5f));
            Box(root, "InkWindow", p.Ink, new Vector3(0f, -0.045f, -0.18f), new Vector3(0.15f, 0.06f, 0.24f));
            Box(root, "Grip", p.Charcoal, new Vector3(0f, -0.19f, -0.22f), new Vector3(0.1f, 0.24f, 0.12f), -10f);
            Rod(root, "Barrel", p.Dark, new Vector3(0f, 0.01f, 0.45f), 0.045f, 0.24f);
            Rod(root, "InkTip", p.Ink, new Vector3(0f, 0.01f, 0.565f), 0.052f, 0.04f);
        }

        // Splattershot Pro:長槍管 + 瞄準鏡 + 槍托,金色槍管、墨管沿槍身
        private static void BuildPro(Transform root, Palette p)
        {
            Box(root, "Body", p.Dark, new Vector3(0f, 0f, -0.22f), new Vector3(0.17f, 0.2f, 0.62f));
            Box(root, "Stock", p.Dark, new Vector3(0f, -0.03f, -0.62f), new Vector3(0.12f, 0.19f, 0.22f));
            Box(root, "Grip", p.Dark, new Vector3(0f, -0.19f, -0.32f), new Vector3(0.1f, 0.24f, 0.12f), -12f);
            Rod(root, "Shroud", p.Dark, new Vector3(0f, 0.01f, 0.16f), 0.07f, 0.2f);
            Rod(root, "Barrel", p.Gold, new Vector3(0f, 0.01f, 0.36f), 0.048f, 0.42f);
            Rod(root, "InkMuzzle", p.Ink, new Vector3(0f, 0.01f, 0.57f), 0.058f, 0.05f);
            Rod(root, "Scope", p.Dark, new Vector3(0f, 0.165f, -0.12f), 0.045f, 0.3f);
            Rod(root, "ScopeRing", p.Gold, new Vector3(0f, 0.165f, 0.02f), 0.052f, 0.03f);
            Rod(root, "InkTube", p.Ink, new Vector3(0.1f, -0.02f, -0.18f), 0.035f, 0.5f);
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

        private static void Ball(Transform parent, string name, Material material, Vector3 position, float radius)
        {
            Transform t = Primitive(parent, name, PrimitiveType.Sphere, material);
            t.localPosition = position;
            t.localScale = Vector3.one * radius * 2f;
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

        // ── 材質(URP Lit;墨水部位是白底,執行期以 MaterialPropertyBlock 套隊伍色)──

        private static Material EnsureLitMaterial(string name, Color color, float metallic, float smoothness)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                {
                    Debug.LogError("[SHOOTERSETUP] ERROR 找不到 URP Lit shader");
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

        // ── SerializedObject 小工具 ──

        private static void SetFloat(SerializedObject so, string field, float value)
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogError($"[SHOOTERSETUP] ERROR {so.targetObject.name} 沒有欄位 {field}");
                return;
            }
            prop.floatValue = value;
        }

        private static void SetInt(SerializedObject so, string field, int value)
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogError($"[SHOOTERSETUP] ERROR {so.targetObject.name} 沒有欄位 {field}");
                return;
            }
            prop.intValue = value;
        }

        private static void SetString(SerializedObject so, string field, string value)
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogError($"[SHOOTERSETUP] ERROR {so.targetObject.name} 沒有欄位 {field}");
                return;
            }
            prop.stringValue = value;
        }

        private static void SetObject(SerializedObject so, string field, Object value)
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogError($"[SHOOTERSETUP] ERROR {so.targetObject.name} 沒有欄位 {field}");
                return;
            }
            prop.objectReferenceValue = value;
        }
    }
}

using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Combat;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SplatoonC.EditorBuild
{
    // 爆破槍(Blaster / Luna Blaster):S3 原始參數(S3BlasterParams,Leanny v11.3.0 Blaster_Middle / Blaster_Short)
    // → InkBulletSpec(BlasterFamily + ExplosionSpec)與 BlasterConfig,寫到 Assets/Resources/Weapons/Main_<id>.asset
    //(執行期 Resources.Load("Weapons/Main_blaster"));另產生程序化槍模 prefab、爆破槍墨彈 prefab(大墨團 + 拖尾)與材質。
    // 冪等,Tools/SplatoonC/M7 Setup 會跑(本類別的 [InitializeOnLoad] 靜態建構子註冊到 M7Setup.ExtraSetups)。
    // 不動武器組合清單與 Player prefab(組合由 K 單元統一建立;測試在執行期建 WeaponKit)。
    [InitializeOnLoad]
    public static class BlasterSetup
    {
        public const string BlasterConfigPath = "Assets/Resources/Weapons/Main_blaster.asset";
        public const string LunaConfigPath = "Assets/Resources/Weapons/Main_luna_blaster.asset";
        private const string PrefabFolder = "Assets/Prefabs/Weapons";
        private const string MaterialFolder = "Assets/Materials/Weapons";
        private const string BlasterGunPath = PrefabFolder + "/BlasterGun.prefab";
        private const string LunaGunPath = PrefabFolder + "/LunaBlasterGun.prefab";
        private const string ShellPrefabPath = PrefabFolder + "/BlasterShell.prefab";
        private const string BulletMaterialPath = MaterialFolder + "/InkBullet.mat";

        // 新音效 id(音效單元之後合成;未合成前 BlasterWeapon 退回 shoot_shooter / bomb_explode,直擊音不發聲)
        public const string BlasterFireSound = "shoot_blaster";
        public const string LunaFireSound = "shoot_luna_blaster";
        public const string BurstSound = "blaster_burst";
        public const string DirectHitSound = "blaster_direct_hit";

        private struct Palette
        {
            public Material Ivory;
            public Material Dark;
            public Material Steel;
            public Material Gold;
            public Material Ink;
        }

        static BlasterSetup()
        {
            System.Action create = Create;
            if (!M7Setup.ExtraSetups.Contains(create))
            {
                M7Setup.ExtraSetups.Add(create);
            }
        }

        [MenuItem("Tools/SplatoonC/Weapons/Blaster Setup")]
        public static void Create()
        {
            if (Application.isPlaying)
            {
                Debug.LogError("[BLASTERSETUP] ERROR Play mode 中不可建立資產");
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
                Ivory = EnsureLitMaterial("BlasterIvory", new Color(0.93f, 0.92f, 0.87f), 0.1f, 0.6f),
                Dark = EnsureLitMaterial("BlasterDark", new Color(0.1f, 0.1f, 0.12f), 0.05f, 0.35f),
                Steel = EnsureLitMaterial("BlasterSteel", new Color(0.62f, 0.64f, 0.68f), 0.7f, 0.6f),
                Gold = EnsureLitMaterial("BlasterGold", new Color(0.96f, 0.76f, 0.2f), 0.45f, 0.65f),
                Ink = EnsureLitMaterial("BlasterInk", Color.white, 0f, 0.92f),
            };
            if (palette.Ivory == null || palette.Ink == null)
            {
                Debug.LogError("[BLASTERSETUP] ERROR 材質建立失敗(找不到 URP Lit shader?)");
                return;
            }
            GameObject blasterGun = CreateGunPrefab(BlasterGunPath, "BlasterGun", palette, false);
            GameObject lunaGun = CreateGunPrefab(LunaGunPath, "LunaBlasterGun", palette, true);
            InkBullet shell = CreateShellPrefab();
            if (blasterGun == null || lunaGun == null || shell == null)
            {
                Debug.LogError("[BLASTERSETUP] ERROR prefab 建立失敗");
                return;
            }
            WriteConfig(BlasterConfigPath, "Main_blaster", blasterGun, shell, S3BlasterParams.Blaster, AttackClass.Blaster,
                BlasterFireSound, 1f);
            WriteConfig(LunaConfigPath, "Main_luna_blaster", lunaGun, shell, S3BlasterParams.Luna, AttackClass.LunaBlaster,
                LunaFireSound, 1.1f);
            AssetDatabase.SaveAssets();
            Debug.Log($"[BLASTERSETUP] 爆破槍:設定 {BlasterConfigPath}、{LunaConfigPath};槍模 {BlasterGunPath}、{LunaGunPath}");
        }

        // ── 設定資產 ──

        private static void WriteConfig(string path, string assetName, GameObject gun, InkBullet shell, in S3BlasterParams s3,
            AttackClass attackClass, string fireSound, float visualScale)
        {
            var config = AssetDatabase.LoadAssetAtPath<BlasterConfig>(path);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<BlasterConfig>();
                AssetDatabase.CreateAsset(config, path);
            }
            InkBulletSpec bullet = BuildBullet(s3, attackClass, visualScale);
            config.Setup(gun, shell, s3, bullet, attackClass, fireSound, BurstSound, DirectHitSound);
            config.name = assetName;
            EditorUtility.SetDirty(config);
        }

        // S3 MoveParam / DamageParam / CollisionParam / BlastParam 原樣進 InkBulletSpec(BlasterFamily):
        //   直擊 125 不衰減、第 BurstFrame 幀空爆、撞地形 / 目標提早爆、撞地形爆風 × ShotCollisionHitRadiusRate、直擊目標不吃爆風;
        //   爆炸:70 → 50 線性、擊退 700;爆炸塗墨 / 閃光交給 BlasterWeapon(空爆與撞地形兩組半徑,ExplosionSpec 無法區分)。
        //   落點不塗橢圓(S3 PaintParam 全 0);沿路墨滴每 1.5 m 一滴、最多 SpawnNum 滴,半徑 = 半寬 × √1.2(落差 < 3 u 滴長 ×1.2 [EST])
        public static InkBulletSpec BuildBullet(in S3BlasterParams s3, AttackClass attackClass, float visualScale)
        {
            var blast = new ExplosionSpec
            {
                NearDamage = s3.NearDamage,
                NearRadius = s3.NearRadius,
                FarDamage = s3.FarDamage,
                FarRadius = s3.FarRadius,
                DamageLinear = true,
                AttackClass = attackClass,
                Kind = DamageKind.Main,
                PaintRadius = 0f,
                PaintRayCount = 0,
                DropCount = 0,
                KnockbackAccel = s3.KnockbackAccel,
                KnockbackRange = s3.KnockbackDistance,
                FxBursts = 0,
                FlashRadius = -1f,
                Sound = BurstSound,
                SoundVolume = 0.9f,
            };
            InkBulletSpec spec = InkBulletSpec.BlasterFamily(s3.SpawnSpeed, s3.BurstFrame, s3.DirectDamage, blast,
                s3.TerrainRadiusRate, s3.RadiusForPlayer, attackClass);
            // BlasterFamily 的直線段 = BurstFrame − 4,但減速入口速度要用 S3 的 GoStraightStateEndMaxSpeed(0.9131 / 0.8484)
            spec.Motion = s3.Motion;
            spec.RadiusForField = s3.RadiusForField;
            spec.PaintWidthHalfNear = 0f;
            spec.PaintWidthHalfFar = 0f;
            spec.PaintRadiusShock = 0f;
            spec.DripSpacing = s3.SpawnBetweenLength;
            spec.DripStartDistance = s3.SpawnBetweenLength;
            spec.DripRadius = s3.DropletPaintRadius;
            spec.DripMaxCount = Mathf.FloorToInt(s3.SpawnNum + 1e-4f);
            // 撞地形的聲音由爆炸音效負責
            spec.Silent = true;
            spec.VisualScale = visualScale;
            // 拖尾 0.06 s:直線段 57 m/s 約 3.4 m 的尾巴(S3 爆破槍彈是大墨團 + 短尾)
            spec.TrailTime = 0.06f;
            return spec;
        }

        // ── 墨彈 prefab:空根(InkBullet + TrailRenderer)+ 子物件 Body(圓球,無碰撞);隊伍色由執行期 MPB 套上 ──

        private static InkBullet CreateShellPrefab()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(BulletMaterialPath);
            if (material == null)
            {
                // ExtraSetups 的執行順序不固定:通用墨彈材質還沒建就先跑戰鬥基礎設施
                CombatInfraSetup.Create();
                material = AssetDatabase.LoadAssetAtPath<Material>(BulletMaterialPath);
            }
            if (material == null)
            {
                Debug.LogError($"[BLASTERSETUP] ERROR 找不到 {BulletMaterialPath}(CombatInfraSetup 先跑)");
                return null;
            }
            var root = new GameObject("BlasterShell");
            try
            {
                GameObject body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                body.name = "Body";
                Object.DestroyImmediate(body.GetComponent<Collider>());
                body.transform.SetParent(root.transform, false);
                // S3 爆破槍墨彈:對人判定直徑 0.57 的大墨團,外觀略小於判定(0.5 m 圓球,沿飛行方向微拉長)
                body.transform.localScale = new Vector3(0.48f, 0.48f, 0.56f);
                var renderer = body.GetComponent<Renderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                var trail = root.AddComponent<TrailRenderer>();
                trail.sharedMaterial = material;
                trail.time = 0.06f;
                trail.minVertexDistance = 0.12f;
                trail.widthMultiplier = 1f;
                trail.widthCurve = new AnimationCurve(new Keyframe(0f, 0.36f), new Keyframe(1f, 0f));
                trail.shadowCastingMode = ShadowCastingMode.Off;
                trail.receiveShadows = false;
                trail.numCapVertices = 3;

                var bullet = root.AddComponent<InkBullet>();
                var so = new SerializedObject(bullet);
                so.FindProperty("_visual").objectReferenceValue = body.transform;
                so.ApplyModifiedPropertiesWithoutUndo();
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, ShellPrefabPath);
                return saved != null ? saved.GetComponent<InkBullet>() : null;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // ── 槍模 prefab(WeaponSocket 座標:+Z 槍管、+Y 上方 / 拇指側、原點 = 手掌握把點;單位公尺)──
        // Blaster:米白粗壯槍身、上方一支橫放的大墨槽(隊伍色)、短粗槍管接喇叭口,全長約 0.42 m(比 Splattershot 的 0.31 m 大一號)。
        // Luna Blaster:短胖槍身、槍口是一顆大圓球(「月亮」)、頂上一顆圓墨槽、金色飾環,全長約 0.38 m。
        // 名稱 Ink 開頭的零件套隊伍色;Muzzle 子物件 = 出膛特效位置。

        private static GameObject CreateGunPrefab(string path, string name, Palette p, bool luna)
        {
            var root = new GameObject(name);
            try
            {
                Transform t = root.transform;
                // 共通:握把(下端往後斜)、板機護弓
                Box(t, "Grip", p.Dark, new Vector3(0f, -0.006f, -0.01f), new Vector3(0.034f, 0.09f, 0.046f), 14f);
                Box(t, "TriggerGuard", p.Dark, new Vector3(0f, 0.022f, 0.038f), new Vector3(0.014f, 0.034f, 0.04f));
                if (luna)
                {
                    BuildLuna(t, p);
                }
                else
                {
                    BuildBlaster(t, p);
                }
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void BuildBlaster(Transform t, Palette p)
        {
            Box(t, "Body", p.Ivory, new Vector3(0f, 0.076f, 0.07f), new Vector3(0.082f, 0.092f, 0.25f));
            Ball(t, "RearCap", p.Ivory, new Vector3(0f, 0.076f, -0.055f), new Vector3(0.082f, 0.092f, 0.07f));
            Box(t, "SideStripe", p.Dark, new Vector3(0f, 0.052f, 0.08f), new Vector3(0.086f, 0.014f, 0.2f));
            Box(t, "ForeGrip", p.Dark, new Vector3(0f, 0.02f, 0.15f), new Vector3(0.04f, 0.046f, 0.06f));
            // 大墨槽:透明罐裡的墨(隊伍色),兩端金屬蓋
            Rod(t, "InkTank", p.Ink, new Vector3(0f, 0.163f, 0.03f), 0.046f, 0.17f);
            Rod(t, "TankCapRear", p.Steel, new Vector3(0f, 0.163f, -0.058f), 0.05f, 0.016f);
            Rod(t, "TankCapFront", p.Steel, new Vector3(0f, 0.163f, 0.118f), 0.05f, 0.016f);
            Box(t, "TankMount", p.Dark, new Vector3(0f, 0.122f, 0.03f), new Vector3(0.03f, 0.02f, 0.12f));
            // 短粗槍管 → 喇叭口 → 墨色槍口
            Rod(t, "Barrel", p.Dark, new Vector3(0f, 0.076f, 0.232f), 0.034f, 0.08f);
            Rod(t, "Bell", p.Ivory, new Vector3(0f, 0.076f, 0.29f), 0.058f, 0.05f);
            Rod(t, "BellRim", p.Dark, new Vector3(0f, 0.076f, 0.312f), 0.06f, 0.008f);
            Rod(t, "InkNozzle", p.Ink, new Vector3(0f, 0.076f, 0.316f), 0.045f, 0.01f);
            Muzzle(t, new Vector3(0f, 0.076f, 0.325f));
        }

        private static void BuildLuna(Transform t, Palette p)
        {
            Box(t, "Body", p.Ivory, new Vector3(0f, 0.072f, 0.045f), new Vector3(0.078f, 0.088f, 0.18f));
            Ball(t, "RearCap", p.Ivory, new Vector3(0f, 0.072f, -0.045f), new Vector3(0.078f, 0.088f, 0.07f));
            Box(t, "SideStripe", p.Gold, new Vector3(0f, 0.05f, 0.05f), new Vector3(0.082f, 0.012f, 0.14f));
            Box(t, "ForeGrip", p.Dark, new Vector3(0f, 0.018f, 0.13f), new Vector3(0.04f, 0.044f, 0.05f));
            // 頂上一顆圓墨槽(隊伍色)+ 金色底座
            Ball(t, "InkTank", p.Ink, new Vector3(0f, 0.16f, 0.01f), new Vector3(0.09f, 0.09f, 0.09f));
            Rod(t, "TankCollar", p.Gold, new Vector3(0f, 0.122f, 0.01f), 0.032f, 0.02f, false);
            // 槍口的大圓球(月亮)+ 金環 + 墨色槍口
            Rod(t, "Neck", p.Dark, new Vector3(0f, 0.072f, 0.155f), 0.03f, 0.05f);
            Ball(t, "Moon", p.Ivory, new Vector3(0f, 0.072f, 0.225f), new Vector3(0.15f, 0.15f, 0.14f));
            Rod(t, "MoonRing", p.Gold, new Vector3(0f, 0.072f, 0.225f), 0.077f, 0.018f);
            Rod(t, "InkNozzle", p.Ink, new Vector3(0f, 0.072f, 0.292f), 0.042f, 0.012f);
            Muzzle(t, new Vector3(0f, 0.072f, 0.3f));
        }

        private static void Muzzle(Transform parent, Vector3 position)
        {
            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(parent, false);
            muzzle.localPosition = position;
        }

        private static void Box(Transform parent, string name, Material material, Vector3 position, Vector3 size,
            float pitchDeg = 0f)
        {
            Transform t = Primitive(parent, name, PrimitiveType.Cube, material);
            t.localPosition = position;
            t.localRotation = Quaternion.Euler(pitchDeg, 0f, 0f);
            t.localScale = size;
        }

        // 圓柱:alongZ = 沿 +Z(槍管方向),否則直立(Unity 圓柱沿 Y、高 2、半徑 0.5)
        private static void Rod(Transform parent, string name, Material material, Vector3 position, float radius,
            float length, bool alongZ = true)
        {
            Transform t = Primitive(parent, name, PrimitiveType.Cylinder, material);
            t.localPosition = position;
            t.localRotation = alongZ ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.identity;
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
            renderer.shadowCastingMode = ShadowCastingMode.On;
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

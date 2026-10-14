using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Combat;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SplatoonC.EditorBuild
{
    // 狙擊槍(Splat Charger / E-liter 4K):
    // S3 原始參數(S3ChargerParams,Leanny v11.3.0)寫到 Assets/Resources/Weapons/Main_<id>.asset(ChargerConfig),
    // 另產生程序化狙擊槍模型 prefab(Assets/Prefabs/Weapons/<Name>.prefab)、子彈 prefab(ChargerShot)與材質
    //(雷射線 / 蓄力計用 URP Unlit,隊伍色由執行期 MaterialPropertyBlock 套上)。
    // 冪等,Tools/SplatoonC/M7 Setup 會跑(本類別的 [InitializeOnLoad] 靜態建構子註冊到 M7Setup.ExtraSetups)。
    // 不動武器組合清單與 Player prefab(組合由 K 單元統一建立;測試在執行期建 WeaponKit)。
    [InitializeOnLoad]
    public static class ChargerSetup
    {
        private const string ConfigFolder = "Assets/Resources/Weapons";
        private const string PrefabFolder = "Assets/Prefabs/Weapons";
        private const string MaterialFolder = "Assets/Materials/Weapons";
        private const string TeamInkMaterialPath = "Assets/Materials/Characters/TankInk.mat";
        private const string ShotPrefabPath = PrefabFolder + "/ChargerShot.prefab";

        private enum Style
        {
            Splat,
            ELiter,
        }

        private struct Variant
        {
            public string Id;
            public string Pascal;
            public Style Style;
            public S3ChargerParams S3;
            public bool Heavy;
            public AttackClass Partial;
            public AttackClass Full;
            public string FireSound;
            // 模型:握把到槍口的距離(公尺)
            public float MuzzleDistance;
            public Vector3 Grip;
            public Vector3 Support;
            public float ZoomFov;
            public float BotClose;
        }

        private static Variant[] Variants => new[]
        {
            new Variant
            {
                Id = "splat_charger", Pascal = "SplatCharger", Style = Style.Splat, S3 = S3ChargerParams.SplatCharger,
                Heavy = false, Partial = AttackClass.Charger, Full = AttackClass.ChargerFull, FireSound = "charger_fire",
                MuzzleDistance = 1.03f, Grip = new Vector3(0.035f, -0.075f, 0.2f), Support = new Vector3(-0.015f, -0.05f, 0.33f),
                ZoomFov = 50f, BotClose = 7f,
            },
            new Variant
            {
                Id = "e_liter_4k", Pascal = "ELiter4K", Style = Style.ELiter, S3 = S3ChargerParams.ELiter4K,
                Heavy = true, Partial = AttackClass.ELiter, Full = AttackClass.ELiterFull, FireSound = "charger_fire_eliter",
                MuzzleDistance = 1.3f, Grip = new Vector3(0.035f, -0.08f, 0.2f), Support = new Vector3(-0.015f, -0.055f, 0.38f),
                ZoomFov = 47f, BotClose = 8f,
            },
        };

        private struct Palette
        {
            public Material Ink;
            public Material Gauge;
            public Material Dark;
            public Material Steel;
            public Material SplatBody;
            public Material SplatAccent;
            public Material ELiterBody;
            public Material ELiterAccent;
        }

        static ChargerSetup()
        {
            System.Action create = Create;
            if (!M7Setup.ExtraSetups.Contains(create))
            {
                M7Setup.ExtraSetups.Add(create);
            }
        }

        [MenuItem("Tools/SplatoonC/Weapons/Charger Setup")]
        public static void Create()
        {
            if (Application.isPlaying)
            {
                Debug.LogError("[CHARGERSETUP] ERROR Play mode 中不可建立資產");
                return;
            }
            M7Setup.EnsureFolder("Assets", "Resources");
            M7Setup.EnsureFolder("Assets/Resources", "Weapons");
            M7Setup.EnsureFolder("Assets", "Prefabs");
            M7Setup.EnsureFolder("Assets/Prefabs", "Weapons");
            M7Setup.EnsureFolder("Assets", "Materials");
            M7Setup.EnsureFolder("Assets/Materials", "Weapons");

            // 墨槽用角色的隊伍色墨水材質(CharacterSetup 建的 TankInk):CharacterVisual 的 MPB 染色 + 微光
            Material teamInk = AssetDatabase.LoadAssetAtPath<Material>(TeamInkMaterialPath);
            var palette = new Palette
            {
                Ink = teamInk != null ? teamInk : EnsureLitMaterial("ChargerInk", Color.white, 0f, 0.9f),
                Gauge = EnsureUnlitMaterial("ChargerGauge", Color.white, false),
                Dark = EnsureLitMaterial("ChargerDark", new Color(0.1f, 0.1f, 0.12f), 0.1f, 0.35f),
                Steel = EnsureLitMaterial("ChargerSteel", new Color(0.62f, 0.64f, 0.68f), 0.75f, 0.66f),
                SplatBody = EnsureLitMaterial("ChargerSplatBody", new Color(0.9f, 0.9f, 0.87f), 0.05f, 0.55f),
                SplatAccent = EnsureLitMaterial("ChargerSplatAccent", new Color(0.95f, 0.55f, 0.12f), 0.1f, 0.6f),
                ELiterBody = EnsureLitMaterial("ChargerELiterBody", new Color(0.24f, 0.25f, 0.28f), 0.45f, 0.55f),
                ELiterAccent = EnsureLitMaterial("ChargerELiterAccent", new Color(0.78f, 0.8f, 0.84f), 0.6f, 0.7f),
            };
            if (teamInk == null)
            {
                Debug.LogWarning("[CHARGERSETUP] 找不到 " + TeamInkMaterialPath + "(先跑 Character Setup);墨槽改用 ChargerInk");
            }
            Material laser = EnsureUnlitMaterial("ChargerLaser", Color.white, true);

            InkBullet shot = CreateShotPrefab();
            int count = 0;
            foreach (Variant variant in Variants)
            {
                ChargerModel model = CreateModelPrefab(variant, palette);
                if (model == null || shot == null || laser == null)
                {
                    Debug.LogError($"[CHARGERSETUP] ERROR {variant.Id} 的模型、子彈 prefab 或雷射材質建立失敗");
                    continue;
                }
                WriteConfig(variant, model, shot, laser);
                count++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[CHARGERSETUP] 狙擊槍 {count} 把:設定 {ConfigFolder}/Main_*.asset、模型與子彈 {PrefabFolder}");
        }

        // ── 設定資產 ──

        private static void WriteConfig(Variant v, ChargerModel model, InkBullet shot, Material laser)
        {
            string path = $"{ConfigFolder}/Main_{v.Id}.asset";
            var config = AssetDatabase.LoadAssetAtPath<ChargerConfig>(path);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<ChargerConfig>();
                AssetDatabase.CreateAsset(config, path);
            }
            else
            {
                // 整份重設成程式碼預設值(S3 數值以程式碼為準,不手調資產)
                ChargerConfig fresh = ScriptableObject.CreateInstance<ChargerConfig>();
                EditorUtility.CopySerialized(fresh, config);
                Object.DestroyImmediate(fresh);
            }
            config.Setup(v.S3, v.Heavy, v.Partial, v.Full, model, shot, laser, v.FireSound, v.Grip, v.Support, v.ZoomFov,
                v.BotClose);
            config.name = $"Main_{v.Id}";
            EditorUtility.SetDirty(config);
        }

        // ── 子彈:細長發光墨條 + 長拖尾(S3 狙擊彈是一道細長墨線;隊伍色由 InkBullet 的 MPB 套上)──

        private static InkBullet CreateShotPrefab()
        {
            Material material = EnsureUnlitMaterial("ChargerShot", Color.white, false);
            Material trailMaterial = EnsureUnlitMaterial("ChargerShotTrail", new Color(1f, 1f, 1f, 0.85f), true);
            var root = new GameObject("ChargerShot");
            try
            {
                GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                body.name = "Body";
                Object.DestroyImmediate(body.GetComponent<Collider>());
                body.transform.SetParent(root.transform, false);
                // 膠囊沿 Y → 轉到沿 +Z(飛行方向):粗 0.1 m、長 0.9 m
                body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                body.transform.localScale = new Vector3(0.1f, 0.45f, 0.1f);
                var renderer = body.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                var trail = root.AddComponent<TrailRenderer>();
                trail.sharedMaterial = trailMaterial;
                trail.time = 0.045f;
                trail.minVertexDistance = 0.3f;
                trail.widthMultiplier = 1f;
                trail.widthCurve = new AnimationCurve(new Keyframe(0f, 0.12f), new Keyframe(1f, 0.01f));
                trail.shadowCastingMode = ShadowCastingMode.Off;
                trail.receiveShadows = false;
                trail.numCapVertices = 2;

                var bullet = root.AddComponent<InkBullet>();
                var so = new SerializedObject(bullet);
                so.FindProperty("_visual").objectReferenceValue = body.transform;
                so.ApplyModifiedPropertiesWithoutUndo();
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, ShotPrefabPath);
                return saved != null ? saved.GetComponent<InkBullet>() : null;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // ── 狙擊槍模型(座標系 = 右手 WeaponSocket:原點握把、+Z 槍管、+Y 上;槍托往 −Z)──

        private static ChargerModel CreateModelPrefab(Variant v, Palette p)
        {
            string path = $"{PrefabFolder}/{v.Pascal}.prefab";
            var root = new GameObject(v.Pascal);
            try
            {
                var model = root.AddComponent<ChargerModel>();
                Transform gauge = v.Style == Style.ELiter ? BuildELiter(root.transform, v, p) : BuildSplat(root.transform, v, p);
                var tip = new GameObject("MuzzleTip").transform;
                tip.SetParent(root.transform, false);
                tip.localPosition = new Vector3(0f, 0.02f, v.MuzzleDistance);
                var so = new SerializedObject(model);
                so.FindProperty("_muzzleTip").objectReferenceValue = tip;
                so.FindProperty("_chargeGauge").objectReferenceValue = gauge;
                so.FindProperty("_muzzleDistance").floatValue = v.MuzzleDistance;
                so.ApplyModifiedPropertiesWithoutUndo();
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
                return saved != null ? saved.GetComponent<ChargerModel>() : null;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // Splat Charger:米白機匣與槍托、黑色握把與護木、細長鋼色槍管、橘色飾環;機匣上方一支隊伍色墨槽,
        // 機匣兩側一對蓄力計(從後往前亮)
        private static Transform BuildSplat(Transform root, Variant v, Palette p)
        {
            float l = v.MuzzleDistance;
            Box(root, "Stock", p.SplatBody, new Vector3(0f, -0.015f, -0.2f), new Vector3(0.065f, 0.11f, 0.24f));
            Box(root, "StockPad", p.Dark, new Vector3(0f, -0.015f, -0.33f), new Vector3(0.07f, 0.12f, 0.025f));
            Transform grip = Box(root, "Grip", p.Dark, new Vector3(0f, -0.075f, -0.015f), new Vector3(0.042f, 0.12f, 0.055f));
            grip.localRotation = Quaternion.Euler(-14f, 0f, 0f);
            Box(root, "Receiver", p.SplatBody, new Vector3(0f, 0.02f, 0.13f), new Vector3(0.085f, 0.1f, 0.36f));
            Box(root, "TriggerGuard", p.Dark, new Vector3(0f, -0.045f, 0.055f), new Vector3(0.02f, 0.03f, 0.09f));
            Rod(root, "InkTank", p.Ink, new Vector3(0f, 0.115f, 0.02f), 0.05f, 0.26f);
            Rod(root, "TankCapBack", p.Dark, new Vector3(0f, 0.115f, -0.12f), 0.054f, 0.03f);
            Rod(root, "TankCapFront", p.Dark, new Vector3(0f, 0.115f, 0.16f), 0.054f, 0.03f);
            Box(root, "Rail", p.Dark, new Vector3(0f, 0.08f, 0.27f), new Vector3(0.03f, 0.02f, 0.2f));
            Box(root, "Handguard", p.Dark, new Vector3(0f, 0.0f, 0.41f), new Vector3(0.068f, 0.075f, 0.2f));
            float barrelStart = 0.3f;
            float barrelEnd = l - 0.05f;
            Rod(root, "Barrel", p.Steel, new Vector3(0f, 0.02f, (barrelStart + barrelEnd) * 0.5f), 0.018f, barrelEnd - barrelStart);
            Rod(root, "Band", p.SplatAccent, new Vector3(0f, 0.02f, 0.66f), 0.026f, 0.05f);
            Rod(root, "MuzzleBrake", p.Dark, new Vector3(0f, 0.02f, l - 0.035f), 0.028f, 0.07f);
            return Gauge(root, p.Gauge, 0.047f, 0.025f, -0.02f, 0.3f);
        }

        // E-liter 4K:鐵灰機匣、銀色飾件、更長更細的槍管、大墨槽;槍托較厚(重量級)
        private static Transform BuildELiter(Transform root, Variant v, Palette p)
        {
            float l = v.MuzzleDistance;
            Box(root, "Stock", p.ELiterBody, new Vector3(0f, -0.02f, -0.21f), new Vector3(0.075f, 0.13f, 0.27f));
            Box(root, "StockPad", p.Dark, new Vector3(0f, -0.02f, -0.355f), new Vector3(0.08f, 0.14f, 0.025f));
            Box(root, "CheekRest", p.ELiterAccent, new Vector3(0f, 0.06f, -0.18f), new Vector3(0.06f, 0.025f, 0.16f));
            Transform grip = Box(root, "Grip", p.Dark, new Vector3(0f, -0.08f, -0.015f), new Vector3(0.045f, 0.13f, 0.06f));
            grip.localRotation = Quaternion.Euler(-14f, 0f, 0f);
            Box(root, "Receiver", p.ELiterBody, new Vector3(0f, 0.02f, 0.16f), new Vector3(0.095f, 0.115f, 0.42f));
            Box(root, "TriggerGuard", p.Dark, new Vector3(0f, -0.05f, 0.055f), new Vector3(0.02f, 0.03f, 0.09f));
            Rod(root, "InkTank", p.Ink, new Vector3(0f, 0.14f, 0.05f), 0.065f, 0.34f);
            Rod(root, "TankCapBack", p.ELiterAccent, new Vector3(0f, 0.14f, -0.135f), 0.069f, 0.035f);
            Rod(root, "TankCapFront", p.ELiterAccent, new Vector3(0f, 0.14f, 0.235f), 0.069f, 0.035f);
            Box(root, "Rail", p.Dark, new Vector3(0f, 0.09f, 0.33f), new Vector3(0.032f, 0.022f, 0.24f));
            Box(root, "Handguard", p.Dark, new Vector3(0f, 0.0f, 0.47f), new Vector3(0.072f, 0.08f, 0.24f));
            float barrelStart = 0.35f;
            float barrelEnd = l - 0.06f;
            Rod(root, "Barrel", p.Steel, new Vector3(0f, 0.02f, (barrelStart + barrelEnd) * 0.5f), 0.016f, barrelEnd - barrelStart);
            Rod(root, "BandA", p.ELiterAccent, new Vector3(0f, 0.02f, 0.72f), 0.024f, 0.04f);
            Rod(root, "BandB", p.ELiterAccent, new Vector3(0f, 0.02f, 0.98f), 0.022f, 0.04f);
            Rod(root, "MuzzleBrake", p.Dark, new Vector3(0f, 0.02f, l - 0.04f), 0.03f, 0.08f);
            return Gauge(root, p.Gauge, 0.052f, 0.03f, -0.02f, 0.36f);
        }

        // 蓄力計:空節點在後端(z = start),底下左右兩條發光條往 +Z 延伸 length;執行期沿 Z 縮放 = 蓄力比例
        private static Transform Gauge(Transform root, Material material, float x, float y, float start, float length)
        {
            var gauge = new GameObject("ChargeGauge").transform;
            gauge.SetParent(root, false);
            gauge.localPosition = new Vector3(0f, y, start);
            Box(gauge, "GaugeL", material, new Vector3(-x, 0f, length * 0.5f), new Vector3(0.012f, 0.022f, length));
            Box(gauge, "GaugeR", material, new Vector3(x, 0f, length * 0.5f), new Vector3(0.012f, 0.022f, length));
            return gauge;
        }

        private static Transform Box(Transform parent, string name, Material material, Vector3 position, Vector3 size)
        {
            Transform t = Primitive(parent, name, PrimitiveType.Cube, material);
            t.localPosition = position;
            t.localScale = size;
            return t;
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

        // 基本形去掉 collider:狙擊槍模型純視覺,不能擋子彈、瞄準射線或雷射
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

        // ── 材質(URP Lit;雷射 / 蓄力計 / 子彈 URP Unlit)──

        private static Material EnsureUnlitMaterial(string name, Color color, bool transparent)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null)
                {
                    Debug.LogError("[CHARGERSETUP] ERROR 找不到 URP Unlit shader");
                    return null;
                }
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            if (transparent)
            {
                // URP 從程式建立的材質預設 Opaque;半透明要補齊這些欄位(CombatInfraSetup 同款)
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", 0f);
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.SetOverrideTag("RenderType", "Transparent");
                material.renderQueue = (int)RenderQueue.Transparent;
                material.SetFloat("_Cull", (float)CullMode.Off);
            }
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material EnsureLitMaterial(string name, Color color, float metallic, float smoothness)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                {
                    Debug.LogError("[CHARGERSETUP] ERROR 找不到 URP Lit shader");
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

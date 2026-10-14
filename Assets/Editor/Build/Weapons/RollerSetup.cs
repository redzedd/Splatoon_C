using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Combat;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SplatoonC.EditorBuild
{
    // 滾筒(Splat Roller / Carbon Roller / Dynamo Roller):
    // S3 原始參數(S3RollerParams,Leanny v11.3.0)寫到 Assets/Resources/Weapons/Main_<id>.asset(RollerConfig),
    // 另產生程序化滾筒模型 prefab(Assets/Prefabs/Weapons/<Name>.prefab)、甩墨墨團 prefab(RollerGlob)與材質。
    // 冪等,Tools/SplatoonC/M7 Setup 會跑(本類別的 [InitializeOnLoad] 靜態建構子註冊到 M7Setup.ExtraSetups)。
    // 不動武器組合清單與 Player prefab(組合由 K 單元統一建立;測試在執行期建 WeaponKit)。
    [InitializeOnLoad]
    public static class RollerSetup
    {
        private const string ConfigFolder = "Assets/Resources/Weapons";
        private const string PrefabFolder = "Assets/Prefabs/Weapons";
        private const string MaterialFolder = "Assets/Materials/Weapons";
        private const string TeamInkMaterialPath = "Assets/Materials/Characters/TankInk.mat";
        private const string GlobPrefabPath = PrefabFolder + "/RollerGlob.prefab";

        private enum Style
        {
            Splat,
            Carbon,
            Dynamo,
        }

        private struct Variant
        {
            public string Id;
            public string Pascal;
            public Style Style;
            public S3RollerParams S3;
            public string FlickSound;
            public string WindupSound;
            // 模型:滾筒半徑 / 寬 / 握把長(公尺;與 S3RollerParams.RollForward 一起決定推滾姿勢)
            public float DrumRadius;
            public float DrumWidth;
            public float HandleLength;
            public float BotRollCommit;
        }

        private static Variant[] Variants => new[]
        {
            new Variant
            {
                Id = "splat_roller", Pascal = "SplatRoller", Style = Style.Splat, S3 = S3RollerParams.SplatRoller,
                FlickSound = "roller_flick", WindupSound = "roller_windup",
                DrumRadius = 0.2f, DrumWidth = 1.4f, HandleLength = 1.05f, BotRollCommit = 2.4f,
            },
            new Variant
            {
                Id = "carbon_roller", Pascal = "CarbonRoller", Style = Style.Carbon, S3 = S3RollerParams.CarbonRoller,
                FlickSound = "roller_flick_carbon", WindupSound = "roller_windup",
                DrumRadius = 0.16f, DrumWidth = 1.05f, HandleLength = 0.98f, BotRollCommit = 2.0f,
            },
            new Variant
            {
                Id = "dynamo_roller", Pascal = "DynamoRoller", Style = Style.Dynamo, S3 = S3RollerParams.DynamoRoller,
                FlickSound = "roller_flick_dynamo", WindupSound = "roller_windup_dynamo",
                DrumRadius = 0.27f, DrumWidth = 1.8f, HandleLength = 1.15f, BotRollCommit = 2.6f,
            },
        };

        private struct Palette
        {
            public Material Ink;
            public Material Grip;
            public Material Steel;
            public Material Groove;
            public Material SplatFrame;
            public Material CarbonFiber;
            public Material CarbonAccent;
            public Material DynamoFrame;
            public Material Brass;
        }

        static RollerSetup()
        {
            System.Action create = Create;
            if (!M7Setup.ExtraSetups.Contains(create))
            {
                M7Setup.ExtraSetups.Add(create);
            }
        }

        [MenuItem("Tools/SplatoonC/Weapons/Roller Setup")]
        public static void Create()
        {
            if (Application.isPlaying)
            {
                Debug.LogError("[ROLLERSETUP] ERROR Play mode 中不可建立資產");
                return;
            }
            M7Setup.EnsureFolder("Assets", "Resources");
            M7Setup.EnsureFolder("Assets/Resources", "Weapons");
            M7Setup.EnsureFolder("Assets", "Prefabs");
            M7Setup.EnsureFolder("Assets/Prefabs", "Weapons");
            M7Setup.EnsureFolder("Assets", "Materials");
            M7Setup.EnsureFolder("Assets/Materials", "Weapons");

            // 滾筒的墨水部位用角色的隊伍色墨水材質(CharacterSetup 建的 TankInk):CharacterVisual 的 MPB 染色 + 微光
            Material teamInk = AssetDatabase.LoadAssetAtPath<Material>(TeamInkMaterialPath);
            var palette = new Palette
            {
                Ink = teamInk != null ? teamInk : EnsureLitMaterial("RollerInk", Color.white, 0f, 0.9f),
                Grip = EnsureLitMaterial("RollerGrip", new Color(0.09f, 0.09f, 0.1f), 0f, 0.3f),
                Steel = EnsureLitMaterial("RollerSteel", new Color(0.72f, 0.74f, 0.78f), 0.7f, 0.62f),
                Groove = EnsureLitMaterial("RollerGroove", new Color(0.18f, 0.18f, 0.2f), 0f, 0.4f),
                SplatFrame = EnsureLitMaterial("RollerSplatFrame", new Color(0.93f, 0.92f, 0.88f), 0.1f, 0.55f),
                CarbonFiber = EnsureLitMaterial("RollerCarbonFiber", new Color(0.13f, 0.14f, 0.16f), 0.3f, 0.78f),
                CarbonAccent = EnsureLitMaterial("RollerCarbonAccent", new Color(0.86f, 0.87f, 0.9f), 0.2f, 0.6f),
                DynamoFrame = EnsureLitMaterial("RollerDynamoFrame", new Color(0.36f, 0.38f, 0.42f), 0.55f, 0.55f),
                Brass = EnsureLitMaterial("RollerBrass", new Color(0.86f, 0.66f, 0.26f), 0.6f, 0.7f),
            };
            if (teamInk == null)
            {
                Debug.LogWarning("[ROLLERSETUP] 找不到 " + TeamInkMaterialPath + "(先跑 Character Setup);滾筒墨水改用 RollerInk");
            }

            InkBullet glob = CreateGlobPrefab();
            int count = 0;
            foreach (Variant variant in Variants)
            {
                RollerModel model = CreateModelPrefab(variant, palette);
                if (model == null || glob == null)
                {
                    Debug.LogError($"[ROLLERSETUP] ERROR {variant.Id} 的模型或墨團 prefab 建立失敗");
                    continue;
                }
                WriteConfig(variant, model, glob);
                count++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[ROLLERSETUP] 滾筒 {count} 把:設定 {ConfigFolder}/Main_*.asset、模型與墨團 {PrefabFolder}");
        }

        // ── 設定資產 ──

        private static void WriteConfig(Variant v, RollerModel model, InkBullet glob)
        {
            string path = $"{ConfigFolder}/Main_{v.Id}.asset";
            var config = AssetDatabase.LoadAssetAtPath<RollerConfig>(path);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<RollerConfig>();
                AssetDatabase.CreateAsset(config, path);
            }
            config.Setup(v.S3, model, glob, v.FlickSound, v.WindupSound, "roller_roll_loop", "roller_hit", v.BotRollCommit);
            config.name = $"Main_{v.Id}";
            EditorUtility.SetDirty(config);
        }

        // ── 甩墨墨團:圓胖的墨團 + 短拖尾(隊伍色由 InkBullet 的 MPB 套上)──

        private static InkBullet CreateGlobPrefab()
        {
            Material material = EnsureLitMaterial("RollerGlob", Color.white, 0f, 0.92f);
            // 拖尾用 Unlit(Lit 的拖尾沒有法線,打光後是暗色雜訊條)
            Material trailMaterial = EnsureUnlitMaterial("RollerGlobTrail", Color.white);
            var root = new GameObject("RollerGlob");
            try
            {
                GameObject body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                body.name = "Body";
                Object.DestroyImmediate(body.GetComponent<Collider>());
                body.transform.SetParent(root.transform, false);
                // 直徑 0.6 m(S3 DrawSizeParam 半徑 0.3),沿飛行方向略拉長
                body.transform.localScale = new Vector3(0.6f, 0.52f, 0.78f);
                var renderer = body.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;

                var trail = root.AddComponent<TrailRenderer>();
                trail.sharedMaterial = trailMaterial;
                trail.time = 0.03f;
                trail.minVertexDistance = 0.12f;
                trail.widthMultiplier = 1f;
                trail.widthCurve = new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(1f, 0f));
                trail.shadowCastingMode = ShadowCastingMode.Off;
                trail.receiveShadows = false;
                trail.numCapVertices = 2;

                var bullet = root.AddComponent<InkBullet>();
                var so = new SerializedObject(bullet);
                so.FindProperty("_visual").objectReferenceValue = body.transform;
                so.ApplyModifiedPropertiesWithoutUndo();
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, GlobPrefabPath);
                return saved != null ? saved.GetComponent<InkBullet>() : null;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // ── 滾筒模型(座標系 = 右手 WeaponSocket:原點握把、+Z 往滾筒、滾筒軸 = X)──

        private static RollerModel CreateModelPrefab(Variant v, Palette p)
        {
            string path = $"{PrefabFolder}/{v.Pascal}.prefab";
            var root = new GameObject(v.Pascal);
            try
            {
                var model = root.AddComponent<RollerModel>();
                Transform drum;
                switch (v.Style)
                {
                    case Style.Carbon:
                        drum = BuildCarbon(root.transform, v, p);
                        break;
                    case Style.Dynamo:
                        drum = BuildDynamo(root.transform, v, p);
                        break;
                    default:
                        drum = BuildSplat(root.transform, v, p);
                        break;
                }
                var so = new SerializedObject(model);
                so.FindProperty("_drum").objectReferenceValue = drum;
                so.FindProperty("_drumRadius").floatValue = v.DrumRadius;
                so.FindProperty("_drumWidth").floatValue = v.DrumWidth;
                so.FindProperty("_handleLength").floatValue = v.HandleLength;
                so.FindProperty("_supportDistance").floatValue = v.Style == Style.Dynamo ? 0.3f : 0.26f;
                so.ApplyModifiedPropertiesWithoutUndo();
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
                return saved != null ? saved.GetComponent<RollerModel>() : null;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // Splat Roller:黑色橡膠握把、銀色桿、奶白 U 形框、小墨罐;滾筒整支沾滿隊伍色墨水
        private static Transform BuildSplat(Transform root, Variant v, Palette p)
        {
            float l = v.HandleLength;
            float half = v.DrumWidth * 0.5f;
            Rod(root, "Grip", p.Grip, new Vector3(0f, 0f, 0.03f), 0.036f, 0.3f);
            Ball(root, "Knob", p.Grip, new Vector3(0f, 0f, -0.13f), 0.05f);
            Rod(root, "Shaft", p.Steel, new Vector3(0f, 0f, (0.18f + l - 0.32f) * 0.5f), 0.024f, l - 0.5f);
            Rod(root, "InkBottle", p.Ink, new Vector3(0f, 0.06f, 0.46f), 0.05f, 0.2f);
            Rod(root, "BottleCap", p.SplatFrame, new Vector3(0f, 0.06f, 0.58f), 0.054f, 0.04f);
            Box(root, "Yoke", p.SplatFrame, new Vector3(0f, 0f, l - 0.3f), new Vector3(v.DrumWidth + 0.1f, 0.06f, 0.07f));
            Box(root, "ArmL", p.SplatFrame, new Vector3(-(half + 0.035f), 0f, l - 0.15f), new Vector3(0.05f, 0.06f, 0.34f));
            Box(root, "ArmR", p.SplatFrame, new Vector3(half + 0.035f, 0f, l - 0.15f), new Vector3(0.05f, 0.06f, 0.34f));
            Transform drum = Drum(root, v, p.Ink, p.Groove, 4);
            Disc(drum, "CapL", p.Steel, new Vector3(-(half + 0.005f), 0f, 0f), v.DrumRadius * 0.55f, 0.02f);
            Disc(drum, "CapR", p.Steel, new Vector3(half + 0.005f, 0f, 0f), v.DrumRadius * 0.55f, 0.02f);
            return drum;
        }

        // Carbon Roller:細長碳纖維桿、白色飾條、單薄的黑框、較細的滾筒
        private static Transform BuildCarbon(Transform root, Variant v, Palette p)
        {
            float l = v.HandleLength;
            float half = v.DrumWidth * 0.5f;
            Rod(root, "Grip", p.Grip, new Vector3(0f, 0f, 0.02f), 0.032f, 0.28f);
            Ball(root, "Knob", p.CarbonAccent, new Vector3(0f, 0f, -0.12f), 0.04f);
            Rod(root, "Shaft", p.CarbonFiber, new Vector3(0f, 0f, (0.16f + l - 0.26f) * 0.5f), 0.02f, l - 0.42f);
            Rod(root, "InkStripe", p.Ink, new Vector3(0f, 0f, 0.42f), 0.023f, 0.16f);
            Rod(root, "Band", p.CarbonAccent, new Vector3(0f, 0f, 0.28f), 0.025f, 0.03f);
            Box(root, "Yoke", p.CarbonFiber, new Vector3(0f, 0f, l - 0.24f), new Vector3(v.DrumWidth + 0.06f, 0.04f, 0.05f));
            Box(root, "ArmL", p.CarbonFiber, new Vector3(-(half + 0.025f), 0f, l - 0.12f), new Vector3(0.035f, 0.045f, 0.27f));
            Box(root, "ArmR", p.CarbonFiber, new Vector3(half + 0.025f, 0f, l - 0.12f), new Vector3(0.035f, 0.045f, 0.27f));
            Transform drum = Drum(root, v, p.Ink, p.Groove, 3);
            Disc(drum, "CapL", p.CarbonAccent, new Vector3(-(half + 0.004f), 0f, 0f), v.DrumRadius * 0.6f, 0.016f);
            Disc(drum, "CapR", p.CarbonAccent, new Vector3(half + 0.004f, 0f, 0f), v.DrumRadius * 0.6f, 0.016f);
            return drum;
        }

        // Dynamo Roller:粗重的槍鐵色框、黃銅飾件、大墨罐、特寬特粗的滾筒
        private static Transform BuildDynamo(Transform root, Variant v, Palette p)
        {
            float l = v.HandleLength;
            float half = v.DrumWidth * 0.5f;
            Rod(root, "Grip", p.Grip, new Vector3(0f, 0f, 0.05f), 0.042f, 0.36f);
            Ball(root, "Knob", p.Brass, new Vector3(0f, 0f, -0.15f), 0.06f);
            Rod(root, "Shaft", p.DynamoFrame, new Vector3(0f, 0f, (0.24f + l - 0.4f) * 0.5f), 0.034f, l - 0.64f);
            Rod(root, "InkTank", p.Ink, new Vector3(0f, 0.09f, 0.5f), 0.085f, 0.3f);
            Rod(root, "TankBandA", p.Brass, new Vector3(0f, 0.09f, 0.38f), 0.09f, 0.03f);
            Rod(root, "TankBandB", p.Brass, new Vector3(0f, 0.09f, 0.62f), 0.09f, 0.03f);
            Box(root, "Yoke", p.DynamoFrame, new Vector3(0f, 0f, l - 0.4f), new Vector3(v.DrumWidth + 0.16f, 0.1f, 0.1f));
            Box(root, "ArmL", p.DynamoFrame, new Vector3(-(half + 0.05f), 0f, l - 0.2f), new Vector3(0.07f, 0.1f, 0.46f));
            Box(root, "ArmR", p.DynamoFrame, new Vector3(half + 0.05f, 0f, l - 0.2f), new Vector3(0.07f, 0.1f, 0.46f));
            Box(root, "Brace", p.Brass, new Vector3(0f, 0.05f, l - 0.4f), new Vector3(0.3f, 0.04f, 0.14f));
            Transform drum = Drum(root, v, p.Ink, p.Groove, 5);
            Disc(drum, "CapL", p.Brass, new Vector3(-(half + 0.006f), 0f, 0f), v.DrumRadius * 0.5f, 0.024f);
            Disc(drum, "CapR", p.Brass, new Vector3(half + 0.006f, 0f, 0f), v.DrumRadius * 0.5f, 0.024f);
            return drum;
        }

        // 滾筒:空節點在軸心(+Z × 握把長),底下是沿 X 的墨水圓柱 + 幾道深色溝槽(轉動時看得出在滾)
        private static Transform Drum(Transform root, Variant v, Material ink, Material groove, int grooves)
        {
            var drum = new GameObject("Drum").transform;
            drum.SetParent(root, false);
            drum.localPosition = new Vector3(0f, 0f, v.HandleLength);
            Transform body = Primitive(drum, "InkDrum", PrimitiveType.Cylinder, ink);
            body.localRotation = Quaternion.Euler(0f, 0f, 90f);
            body.localScale = new Vector3(v.DrumRadius * 2f, v.DrumWidth * 0.5f, v.DrumRadius * 2f);
            for (int i = 0; i < grooves; i++)
            {
                float angle = i * 360f / grooves;
                Transform line = Primitive(drum, $"Groove{i}", PrimitiveType.Cube, groove);
                Quaternion rotation = Quaternion.AngleAxis(angle, Vector3.right);
                line.localPosition = rotation * new Vector3(0f, v.DrumRadius * 0.985f, 0f);
                line.localRotation = rotation;
                line.localScale = new Vector3(v.DrumWidth * 0.96f, 0.012f, 0.03f);
            }
            return drum;
        }

        private static void Disc(Transform parent, string name, Material material, Vector3 position, float radius,
            float thickness)
        {
            Transform t = Primitive(parent, name, PrimitiveType.Cylinder, material);
            t.localPosition = position;
            t.localRotation = Quaternion.Euler(0f, 0f, 90f);
            t.localScale = new Vector3(radius * 2f, thickness * 0.5f, radius * 2f);
        }

        private static void Box(Transform parent, string name, Material material, Vector3 position, Vector3 size)
        {
            Transform t = Primitive(parent, name, PrimitiveType.Cube, material);
            t.localPosition = position;
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

        // 基本形去掉 collider:滾筒模型純視覺,不能擋墨彈、瞄準射線或推滾的地面偵測
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

        // ── 材質(URP Lit;墨團拖尾 URP Unlit)──

        private static Material EnsureUnlitMaterial(string name, Color color)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null)
                {
                    Debug.LogError("[ROLLERSETUP] ERROR 找不到 URP Unlit shader");
                    return null;
                }
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
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
                    Debug.LogError("[ROLLERSETUP] ERROR 找不到 URP Lit shader");
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

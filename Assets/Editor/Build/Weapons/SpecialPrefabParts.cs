using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SplatoonC.EditorBuild
{
    // 大招單元(D1:Trizooka / Big Bubbler / Killer Wail 5.1 / Wave Breaker)建立程序化 prefab 的共用小工具:
    // 資料夾、材質(URP Lit / Unlit,墨水部位白底、執行期以 MaterialPropertyBlock 套隊伍色)、基本形(拆掉 collider)、
    // LineRenderer、設定資產(每次重跑都重設成程式碼預設值 → 資產與程式碼的 S3 數值一致)。
    public static class SpecialPrefabParts
    {
        public const string ConfigFolder = "Assets/Resources/Weapons";
        public const string PrefabFolder = "Assets/Prefabs/Weapons";
        public const string MaterialFolder = "Assets/Materials/Weapons";

        public static void EnsureFolders()
        {
            M7Setup.EnsureFolder("Assets", "Resources");
            M7Setup.EnsureFolder("Assets/Resources", "Weapons");
            M7Setup.EnsureFolder("Assets", "Prefabs");
            M7Setup.EnsureFolder("Assets/Prefabs", "Weapons");
            M7Setup.EnsureFolder("Assets", "Materials");
            M7Setup.EnsureFolder("Assets/Materials", "Weapons");
        }

        // ── 材質 ──

        public static Material Lit(string name, Color color, float metallic, float smoothness)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                {
                    Debug.LogError("[SPECIALSETUP] ERROR 找不到 URP Lit shader");
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

        // URP Unlit;transparent = 半透明(不寫深度),doubleSided = 兩面都畫(罩內也看得到球面)
        public static Material Unlit(string name, Color color, bool transparent, bool doubleSided)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null)
                {
                    Debug.LogError("[SPECIALSETUP] ERROR 找不到 URP Unlit shader");
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
            }
            material.SetFloat("_Cull", doubleSided ? (float)CullMode.Off : (float)CullMode.Back);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        // 由下往上淡出的白色貼圖(alpha:底邊 6% 全實 → 頂端 0,線性衰減):貼地往上散的半透明面(Wave Breaker 波牆)用。
        // 已存在就沿用(內容固定)
        public static Texture2D VerticalFade(string name)
        {
            string path = $"{MaterialFolder}/{name}.asset";
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture != null)
            {
                return texture;
            }
            const int height = 64;
            texture = new Texture2D(4, height, TextureFormat.RGBA32, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            for (int y = 0; y < height; y++)
            {
                float v = y / (height - 1f);
                float alpha = v < 0.06f ? 1f : 1f - v;
                for (int x = 0; x < 4; x++)
                {
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            texture.Apply(false, false);
            AssetDatabase.CreateAsset(texture, path);
            return texture;
        }

        // 半透明雙面 Unlit + 貼圖(顏色由執行期 MaterialPropertyBlock 的 _BaseColor 乘上去)
        public static Material UnlitTextured(string name, Texture2D texture)
        {
            Material material = Unlit(name, Color.white, true, true);
            if (material != null && texture != null)
            {
                material.SetTexture("_BaseMap", texture);
                material.SetTexture("_MainTex", texture);
                EditorUtility.SetDirty(material);
            }
            return material;
        }

        // ── 基本形(純視覺:拆掉 collider)──

        public static Transform Primitive(Transform parent, string name, PrimitiveType type, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = name;
            go.layer = parent != null ? parent.gameObject.layer : 0;
            go.transform.SetParent(parent, false);
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            return go.transform;
        }

        public static Transform Box(Transform parent, string name, Material material, Vector3 position, Vector3 size,
            Vector3 euler = default)
        {
            Transform t = Primitive(parent, name, PrimitiveType.Cube, material);
            t.localPosition = position;
            t.localRotation = Quaternion.Euler(euler);
            t.localScale = size;
            return t;
        }

        // 圓柱沿 +Z(Unity 圓柱沿 Y、高 2、半徑 0.5)
        public static Transform Rod(Transform parent, string name, Material material, Vector3 position, float radius,
            float length)
        {
            Transform t = Primitive(parent, name, PrimitiveType.Cylinder, material);
            t.localPosition = position;
            t.localRotation = Quaternion.Euler(90f, 0f, 0f);
            t.localScale = new Vector3(radius * 2f, length * 0.5f, radius * 2f);
            return t;
        }

        // 直立圓柱(沿 +Y),position = 底面中心
        public static Transform Pillar(Transform parent, string name, Material material, Vector3 basePosition,
            float radius, float height)
        {
            Transform t = Primitive(parent, name, PrimitiveType.Cylinder, material);
            t.localPosition = basePosition + Vector3.up * (height * 0.5f);
            t.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
            return t;
        }

        public static Transform Ball(Transform parent, string name, Material material, Vector3 position, Vector3 diameter)
        {
            Transform t = Primitive(parent, name, PrimitiveType.Sphere, material);
            t.localPosition = position;
            t.localScale = diameter;
            return t;
        }

        public static Transform Empty(Transform parent, string name, Vector3 position)
        {
            var go = new GameObject(name);
            go.layer = parent != null ? parent.gameObject.layer : 0;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            return go.transform;
        }

        // LineRenderer(不投影);points = null 時只設定數量
        public static LineRenderer Line(Transform parent, string name, Material material, float width, bool worldSpace,
            bool loop, Vector3[] points, int count = 2)
        {
            var go = new GameObject(name);
            go.layer = parent != null ? parent.gameObject.layer : 0;
            go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = worldSpace;
            line.loop = loop;
            line.widthMultiplier = width;
            line.numCapVertices = 2;
            line.numCornerVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.textureMode = LineTextureMode.Stretch;
            if (points != null)
            {
                line.positionCount = points.Length;
                line.SetPositions(points);
            }
            else
            {
                line.positionCount = count;
            }
            return line;
        }

        // 半徑 radius 的圓(本地座標),normal 軸:0 = 水平(XZ)、1 = 垂直 XY、2 = 垂直 YZ
        public static Vector3[] Circle(int segments, float radius, int plane)
        {
            var points = new Vector3[segments];
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                float c = Mathf.Cos(a) * radius;
                float s = Mathf.Sin(a) * radius;
                points[i] = plane == 0 ? new Vector3(c, 0f, s) : plane == 1 ? new Vector3(c, s, 0f) : new Vector3(0f, c, s);
            }
            return points;
        }

        public static void SetLayerRecursively(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++)
            {
                SetLayerRecursively(root.GetChild(i), layer);
            }
        }

        // ── prefab / 設定資產 ──

        public static GameObject SavePrefab(GameObject root, string path)
        {
            try
            {
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // 設定資產:沒有就建立;有就整份重設成程式碼預設值(S3 數值以程式碼為準,不手調資產)
        public static T ResetConfig<T>(string id) where T : ScriptableObject
        {
            string path = $"{ConfigFolder}/Special_{id}.asset";
            var config = AssetDatabase.LoadAssetAtPath<T>(path);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(config, path);
            }
            else
            {
                T fresh = ScriptableObject.CreateInstance<T>();
                EditorUtility.CopySerialized(fresh, config);
                Object.DestroyImmediate(fresh);
            }
            config.name = $"Special_{id}";
            EditorUtility.SetDirty(config);
            return config;
        }

        public static void SetFloat(Object target, string field, float value)
        {
            var so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogError($"[SPECIALSETUP] ERROR {target.name} 沒有欄位 {field}");
                return;
            }
            prop.floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        public static void SetInt(Object target, string field, int value)
        {
            var so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogError($"[SPECIALSETUP] ERROR {target.name} 沒有欄位 {field}");
                return;
            }
            prop.intValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        // 元件上的物件參照欄位
        public static void SetObject(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogError($"[SPECIALSETUP] ERROR {target.GetType().Name} 沒有欄位 {field}");
                return;
            }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // 常用材質組
        public struct Palette
        {
            public Material Dark;
            public Material Metal;
            public Material Ivory;
            public Material Ink;
            public Material Glow;
            public Material Line;
            public Material Glass;
        }

        public static Palette CreatePalette()
        {
            return new Palette
            {
                Dark = Lit("SpecialDark", new Color(0.13f, 0.14f, 0.17f), 0.1f, 0.55f),
                Metal = Lit("SpecialMetal", new Color(0.55f, 0.58f, 0.63f), 0.5f, 0.62f),
                Ivory = Lit("SpecialIvory", new Color(0.93f, 0.9f, 0.82f), 0f, 0.5f),
                Ink = Lit("SpecialInk", Color.white, 0f, 0.88f),
                Glow = Unlit("SpecialGlow", Color.white, false, false),
                Line = Unlit("SpecialLine", new Color(1f, 1f, 1f, 0.8f), true, true),
                Glass = Unlit("SpecialBubbleGlass", new Color(1f, 1f, 1f, 0.2f), true, true),
            };
        }
    }
}

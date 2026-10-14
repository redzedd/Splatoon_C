using SplatoonC.Gameplay.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SplatoonC.EditorBuild
{
    // 移動單元 A4(Squid Roll / Squid Surge / Super Jump)的資產(冪等,每次重建):
    //   Assets/Resources/Movement/MovementConfig.asset(ResetToS3 寫回 S3 數值 + 接上 prefab / 材質)、
    //   超級跳落點標記 prefab(Assets/Prefabs/Movement/SuperJumpMarker.prefab:地面圈 + 半透明圓盤 + 往下箭頭(墨水縮放樞紐)
    //   + 名字錨點)、網格(Assets/Prefabs/Movement/Meshes/)、URP Unlit 材質(Assets/Materials/Movement/:
    //   隊伍色部位白底由 MaterialPropertyBlock 上色;護甲光暈半透明)。
    // 由 M7Setup.Apply 經 ExtraSetups 呼叫;選單 Tools/SplatoonC/Weapons/Movement Setup。
    [InitializeOnLoad]
    public static class MovementSetup
    {
        private const string ConfigFolder = "Assets/Resources/Movement";
        private const string ConfigPath = ConfigFolder + "/MovementConfig.asset";
        private const string PrefabFolder = "Assets/Prefabs/Movement";
        private const string MeshFolder = PrefabFolder + "/Meshes";
        private const string MaterialFolder = "Assets/Materials/Movement";
        private const string MarkerPath = PrefabFolder + "/SuperJumpMarker.prefab";

        static MovementSetup()
        {
            System.Action create = Create;
            if (!M7Setup.ExtraSetups.Contains(create))
            {
                M7Setup.ExtraSetups.Add(create);
            }
        }

        [MenuItem("Tools/SplatoonC/Weapons/Movement Setup")]
        public static void Create()
        {
            if (Application.isPlaying)
            {
                Debug.LogError("[MOVESETUP] ERROR Play mode 中不可建立資產");
                return;
            }
            M7Setup.EnsureFolder("Assets", "Resources");
            M7Setup.EnsureFolder("Assets/Resources", "Movement");
            M7Setup.EnsureFolder("Assets", "Prefabs");
            M7Setup.EnsureFolder("Assets/Prefabs", "Movement");
            M7Setup.EnsureFolder(PrefabFolder, "Meshes");
            M7Setup.EnsureFolder("Assets", "Materials");
            M7Setup.EnsureFolder("Assets/Materials", "Movement");

            Material ink = Unlit("MoveInk", Color.white, false, CullMode.Back);
            Material inkTransparent = Unlit("MoveInkGlass", new Color(1f, 1f, 1f, 0.35f), true, CullMode.Off);
            Material shell = Unlit("MoveArmorShell", new Color(1f, 1f, 1f, 0.25f), true, CullMode.Back);
            // 箭頭外框 = 反向外殼(只畫背面):墨水(不透明、寫深度)擋住的部分不畫,只剩一圈白邊;
            // 墨水流乾的上半段看得到外殼背面 → 箭頭形狀仍在(正面畫的話會整片蓋在墨水上、把隊伍色洗白)
            Material outline = Unlit("MoveMarkerOutline", new Color(1f, 1f, 1f, 0.55f), true, CullMode.Front);
            if (ink == null || inkTransparent == null || shell == null || outline == null)
            {
                return;
            }
            Mesh ring = Ring("SuperJumpRing", 1.0f, 1.25f, 48);
            Mesh cone = Cone("SuperJumpArrowHead", 0.5f, 0.75f, 24);

            SuperJumpMarker marker = BuildMarker(ink, inkTransparent, outline, ring, cone);

            var config = AssetDatabase.LoadAssetAtPath<MovementConfig>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<MovementConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
                Debug.Log($"[MOVESETUP] 建立 {ConfigPath}");
            }
            config.ResetToS3();
            EditorUtility.SetDirty(config);
            M7Setup.SetRef(config, "_markerPrefab", marker);
            M7Setup.SetRef(config, "_armorShellMaterial", shell);
            M7Setup.SetRef(config, "_armorShellMesh", Resources.GetBuiltinResource<Mesh>("Sphere.fbx"));
            AssetDatabase.SaveAssets();
            Debug.Log("[MOVESETUP] 完成:MovementConfig、SuperJumpMarker");
        }

        // 落點標記:地面圈(InkRing,慢轉脈動)+ 半透明圓盤(InkDisc)+ 懸空往下箭頭(外框 + 墨水,墨水以尖端為樞紐縮放)+ 名字錨點
        private static SuperJumpMarker BuildMarker(Material ink, Material inkGlass, Material outline, Mesh ring, Mesh cone)
        {
            var root = new GameObject("SuperJumpMarker");
            Transform ringRoot = Empty(root.transform, "Ring", new Vector3(0f, 0.04f, 0f));
            MeshPart(ringRoot, "InkRing", ring, ink, Vector3.zero, Vector3.one);
            Transform disc = Primitive(root.transform, "InkDisc", PrimitiveType.Cylinder, inkGlass);
            disc.localPosition = new Vector3(0f, 0.03f, 0f);
            disc.localScale = new Vector3(2.1f, 0.005f, 2.1f);

            Transform arrow = Empty(root.transform, "Arrow", new Vector3(0f, 3.2f, 0f));
            // 外框(半透明白,比墨水略大):墨水流乾後仍看得到箭頭形狀
            MeshPart(arrow, "ArrowHeadOutline", cone, outline, new Vector3(0f, -0.04f, 0f), new Vector3(1.18f, 1.12f, 1.18f));
            Transform shaftOutline = Primitive(arrow, "ArrowShaftOutline", PrimitiveType.Cylinder, outline);
            shaftOutline.localPosition = new Vector3(0f, 1.15f, 0f);
            shaftOutline.localScale = new Vector3(0.42f, 0.42f, 0.42f);
            // 墨水:樞紐在箭頭尖端(y 0),y 縮放 = 剩餘墨水 → 由上往下流乾
            Transform fill = Empty(arrow, "FillPivot", Vector3.zero);
            MeshPart(fill, "InkArrowHead", cone, ink, Vector3.zero, Vector3.one);
            Transform shaft = Primitive(fill, "InkArrowShaft", PrimitiveType.Cylinder, ink);
            shaft.localPosition = new Vector3(0f, 1.15f, 0f);
            shaft.localScale = new Vector3(0.34f, 0.4f, 0.34f);

            Transform label = Empty(root.transform, "LabelAnchor", new Vector3(0f, 5.1f, 0f));

            var marker = root.AddComponent<SuperJumpMarker>();
            SetObject(marker, "_ring", ringRoot);
            SetObject(marker, "_arrow", arrow);
            SetObject(marker, "_fillPivot", fill);
            SetObject(marker, "_labelAnchor", label);
            GameObject saved;
            try
            {
                saved = PrefabUtility.SaveAsPrefabAsset(root, MarkerPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
            return saved != null ? saved.GetComponent<SuperJumpMarker>() : null;
        }

        // ── 小工具 ──

        private static Transform Empty(Transform parent, string name, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            return go.transform;
        }

        private static Transform Primitive(Transform parent, string name, PrimitiveType type, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent, false);
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go.transform;
        }

        private static Transform MeshPart(Transform parent, string name, Mesh mesh, Material material, Vector3 position,
            Vector3 scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go.transform;
        }

        private static void SetObject(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogError($"[MOVESETUP] ERROR {target.GetType().Name} 沒有欄位 {field}");
                return;
            }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // URP Unlit;transparent = 半透明(不寫深度),cull = 剔除哪一面(Off = 兩面都畫、Front = 只畫背面)
        private static Material Unlit(string name, Color color, bool transparent, CullMode cull)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null)
                {
                    Debug.LogError("[MOVESETUP] ERROR 找不到 URP Unlit shader");
                    return null;
                }
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            if (transparent)
            {
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", 0f);
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.SetOverrideTag("RenderType", "Transparent");
                material.renderQueue = (int)RenderQueue.Transparent;
            }
            material.SetFloat("_Cull", (float)cull);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        // 平面環(朝上)
        private static Mesh Ring(string name, float inner, float outer, int segments)
        {
            var vertices = new Vector3[(segments + 1) * 2];
            var normals = new Vector3[vertices.Length];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[segments * 6];
            for (int s = 0; s <= segments; s++)
            {
                float angle = s * Mathf.PI * 2f / segments;
                var dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                vertices[s * 2] = dir * inner;
                vertices[s * 2 + 1] = dir * outer;
                normals[s * 2] = Vector3.up;
                normals[s * 2 + 1] = Vector3.up;
                uvs[s * 2] = new Vector2((float)s / segments, 0f);
                uvs[s * 2 + 1] = new Vector2((float)s / segments, 1f);
            }
            for (int s = 0; s < segments; s++)
            {
                int t = s * 6;
                int a = s * 2;
                triangles[t] = a;
                triangles[t + 1] = a + 2;
                triangles[t + 2] = a + 1;
                triangles[t + 3] = a + 1;
                triangles[t + 4] = a + 2;
                triangles[t + 5] = a + 3;
            }
            var mesh = new Mesh { name = name, vertices = vertices, normals = normals, uv = uvs, triangles = triangles };
            mesh.RecalculateBounds();
            return SaveMesh(mesh);
        }

        // 往下指的圓錐:尖端在原點,底面(半徑 radius)在 y = height,底面封口
        private static Mesh Cone(string name, float radius, float height, int segments)
        {
            int ringCount = segments + 1;
            var vertices = new Vector3[ringCount * 2 + ringCount + 1];
            var normals = new Vector3[vertices.Length];
            var uvs = new Vector2[vertices.Length];
            var triangles = new System.Collections.Generic.List<int>(segments * 9);
            float slope = Mathf.Atan2(radius, height);
            for (int s = 0; s <= segments; s++)
            {
                float angle = s * Mathf.PI * 2f / segments;
                var dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 side = (dir * Mathf.Cos(slope) - Vector3.up * Mathf.Sin(slope)).normalized;
                vertices[s] = Vector3.zero;
                normals[s] = side;
                vertices[ringCount + s] = dir * radius + Vector3.up * height;
                normals[ringCount + s] = side;
                vertices[ringCount * 2 + s] = dir * radius + Vector3.up * height;
                normals[ringCount * 2 + s] = Vector3.up;
                uvs[s] = new Vector2((float)s / segments, 0f);
                uvs[ringCount + s] = new Vector2((float)s / segments, 1f);
                uvs[ringCount * 2 + s] = new Vector2(0.5f + dir.x * 0.5f, 0.5f + dir.z * 0.5f);
            }
            int center = ringCount * 3;
            vertices[center] = Vector3.up * height;
            normals[center] = Vector3.up;
            uvs[center] = new Vector2(0.5f, 0.5f);
            for (int s = 0; s < segments; s++)
            {
                // 側面(外側朝外;Unity 正面 = 從外側看順時針)
                triangles.Add(s);
                triangles.Add(ringCount + s);
                triangles.Add(ringCount + s + 1);
                // 底面封口(朝上)
                triangles.Add(center);
                triangles.Add(ringCount * 2 + s + 1);
                triangles.Add(ringCount * 2 + s);
            }
            var mesh = new Mesh { name = name, vertices = vertices, normals = normals, uv = uvs };
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return SaveMesh(mesh);
        }

        // 網格資產:已存在就原地更新(GUID 不變,prefab 參照不斷)
        private static Mesh SaveMesh(Mesh mesh)
        {
            string path = $"{MeshFolder}/{mesh.name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            existing.Clear();
            existing.vertices = mesh.vertices;
            existing.normals = mesh.normals;
            existing.uv = mesh.uv;
            existing.triangles = mesh.triangles;
            existing.RecalculateBounds();
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(mesh);
            return existing;
        }
    }
}

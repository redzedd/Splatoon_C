using System.Collections.Generic;
using SplatoonC.Gameplay.Combat;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SplatoonC.EditorBuild
{
    // 副武器 A2「投擲物 II」:Torpedo / Fizzy Bomb / Point Sensor / Angle Shooter 的資產建立(冪等,每次重建):
    //   設定 Assets/Resources/Weapons/Sub_<id>.asset(ResetToS3 寫入 S3 0 AP 數值)、
    //   投擲物 prefab 與手持模型 prefab(Assets/Prefabs/Weapons/)、旋轉體網格(Assets/Prefabs/Weapons/Meshes/)、
    //   URP 材質(Assets/Materials/Weapons/Throwable*.mat)。
    // 外觀約定(SimulatedBomb):名稱以 Ink 開頭 = 隊伍色墨水部位;Light 開頭 = 指示燈;手持模型的墨水部位用角色的 TankInk。
    // Torpedo 的受擊碰撞體(Hull,Target 圖層)平時關閉,變身時由執行期打開。
    // 由 M7Setup.Apply 經 ExtraSetups 呼叫;選單 Tools/SplatoonC/Weapons/Throwables II Setup。
    [InitializeOnLoad]
    public static class ThrowablesSetup
    {
        private const string ConfigFolder = "Assets/Resources/Weapons";
        private const string PrefabFolder = "Assets/Prefabs/Weapons";
        private const string MeshFolder = PrefabFolder + "/Meshes";
        private const string MaterialFolder = "Assets/Materials/Weapons";
        private const string TankInkPath = "Assets/Materials/Characters/TankInk.mat";
        private const int Segments = 28;
        private const int TargetLayer = 6;

        private sealed class Palette
        {
            public Material Ink;
            public Material HeldInk;
            public Material Shell;
            public Material Metal;
            public Material Dark;
            public Material Light;
            public Material Field;
            public Material Line;
        }

        private sealed class Meshes
        {
            public Mesh TorpedoBody;
            public Mesh TorpedoNose;
            public Mesh TorpedoTail;
            public Mesh FizzyCan;
            public Mesh FizzyLid;
            public Mesh SensorShell;
            public Mesh SensorCore;
            public Mesh DartTip;
            public Mesh DashedRing;
            public Mesh FieldRing;
            public Mesh Sphere;
            public Mesh Cylinder;
            public Mesh Cube;
        }

        static ThrowablesSetup()
        {
            System.Action create = Create;
            if (!M7Setup.ExtraSetups.Contains(create))
            {
                M7Setup.ExtraSetups.Add(create);
            }
        }

        [MenuItem("Tools/SplatoonC/Weapons/Throwables II Setup")]
        public static void Create()
        {
            if (Application.isPlaying)
            {
                Debug.LogError("[THROWSETUP] ERROR Play mode 中不可建立資產");
                return;
            }
            M7Setup.EnsureFolder("Assets", "Resources");
            M7Setup.EnsureFolder("Assets/Resources", "Weapons");
            M7Setup.EnsureFolder("Assets", "Prefabs");
            M7Setup.EnsureFolder("Assets/Prefabs", "Weapons");
            M7Setup.EnsureFolder(PrefabFolder, "Meshes");
            M7Setup.EnsureFolder("Assets", "Materials");
            M7Setup.EnsureFolder("Assets/Materials", "Weapons");

            Palette palette = BuildPalette();
            if (palette == null)
            {
                return;
            }
            Meshes meshes = BuildMeshes();

            var torpedo = EnsureConfig<TorpedoConfig>("torpedo");
            torpedo.ResetToS3();
            var fizzy = EnsureConfig<FizzyBombConfig>("fizzy_bomb");
            fizzy.ResetToS3();
            var sensor = EnsureConfig<PointSensorConfig>("point_sensor");
            sensor.ResetToS3();
            var angle = EnsureConfig<AngleShooterConfig>("angle_shooter");
            angle.ResetToS3();

            Wire(torpedo, BuildTorpedo(palette, meshes, false), BuildTorpedo(palette, meshes, true));
            Wire(fizzy, BuildFizzy(palette, meshes, false), BuildFizzy(palette, meshes, true));
            Wire(sensor, BuildSensor(palette, meshes, false), BuildSensor(palette, meshes, true));
            Wire(angle, BuildAngleShooter(palette, meshes, false), BuildAngleShooter(palette, meshes, true));
            AssetDatabase.SaveAssets();
            Debug.Log("[THROWSETUP] 完成:Sub_torpedo / Sub_fizzy_bomb / Sub_point_sensor / Sub_angle_shooter + prefab");
        }

        // ── 設定資產 ──

        private static T EnsureConfig<T>(string id) where T : SubWeaponConfig
        {
            string path = $"{ConfigFolder}/Sub_{id}.asset";
            var config = AssetDatabase.LoadAssetAtPath<T>(path);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(config, path);
                Debug.Log($"[THROWSETUP] 建立 {path}");
            }
            return config;
        }

        private static void Wire(SubWeaponConfig config, GameObject prefab, GameObject held)
        {
            EditorUtility.SetDirty(config);
            M7Setup.SetRef(config, "_prefab", prefab != null ? prefab.GetComponent<SubProjectile>() : null);
            M7Setup.SetRef(config, "_heldModel", held);
        }

        // ── 材質 ──

        private static Palette BuildPalette()
        {
            var palette = new Palette
            {
                // 墨水部位白底(執行期 MaterialPropertyBlock 套隊伍色)
                Ink = EnsureLit("ThrowableInk", Color.white, 0f, 0.9f),
                HeldInk = AssetDatabase.LoadAssetAtPath<Material>(TankInkPath),
                Shell = EnsureLit("ThrowableShell", new Color(0.93f, 0.93f, 0.95f), 0f, 0.65f),
                Metal = EnsureLit("ThrowableMetal", new Color(0.58f, 0.6f, 0.64f), 0.6f, 0.6f),
                Dark = EnsureLit("ThrowableDark", new Color(0.16f, 0.17f, 0.2f), 0f, 0.4f),
                Light = EnsureUnlit("ThrowableLight", Color.white, false, false),
                Field = EnsureUnlit("ThrowableField", new Color(1f, 1f, 1f, 0.3f), true, true),
                Line = EnsureUnlit("ThrowableLine", new Color(1f, 1f, 1f, 0.9f), true, true),
            };
            if (palette.Ink == null || palette.Light == null || palette.Field == null || palette.Line == null)
            {
                Debug.LogError("[THROWSETUP] ERROR 材質建立失敗(找不到 URP shader?)");
                return null;
            }
            if (palette.HeldInk == null)
            {
                Debug.LogWarning($"[THROWSETUP] 找不到 {TankInkPath}(先跑 Character Setup);手持模型改用 ThrowableInk");
                palette.HeldInk = palette.Ink;
            }
            return palette;
        }

        private static Material EnsureLit(string name, Color color, float metallic, float smoothness)
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

        private static Material EnsureUnlit(string name, Color color, bool transparent, bool doubleSided)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null)
                {
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

        // ── 網格(旋轉體:輪廓 (半徑, 高度) 由下往上;連續重複的點 = 折角)──

        private static Meshes BuildMeshes()
        {
            return new Meshes
            {
                // 魚雷:沿 +Y 建模(prefab 裡轉 90° 對準 +Z),尾 −0.3 → 鼻 0.335
                TorpedoBody = Lathe("TorpedoBody", new[]
                {
                    P(0f, -0.3f), P(0.07f, -0.3f), P(0.07f, -0.3f), P(0.09f, -0.25f), P(0.1f, -0.16f),
                    P(0.1f, 0.12f), P(0.1f, 0.12f), P(0f, 0.12f),
                }),
                TorpedoNose = Lathe("TorpedoNose", new[]
                {
                    P(0f, 0.12f), P(0.1f, 0.12f), P(0.1f, 0.12f), P(0.094f, 0.19f), P(0.074f, 0.26f),
                    P(0.04f, 0.315f), P(0f, 0.335f),
                }),
                TorpedoTail = Lathe("TorpedoTail", new[]
                {
                    P(0f, -0.37f), P(0.045f, -0.37f), P(0.045f, -0.37f), P(0.058f, -0.33f), P(0.072f, -0.3f),
                    P(0.072f, -0.3f), P(0f, -0.3f),
                }),
                // 碳酸罐:直立建模,中心在原點(高 0.29、半徑 0.115)
                FizzyCan = Lathe("FizzyCan", new[]
                {
                    P(0f, -0.145f), P(0.092f, -0.145f), P(0.092f, -0.145f), P(0.112f, -0.125f), P(0.115f, -0.1f),
                    P(0.115f, 0.1f), P(0.105f, 0.122f), P(0.092f, 0.13f), P(0.092f, 0.13f), P(0f, 0.13f),
                }),
                FizzyLid = Lathe("FizzyLid", new[]
                {
                    P(0f, 0.128f), P(0.095f, 0.128f), P(0.095f, 0.128f), P(0.095f, 0.142f), P(0.082f, 0.146f),
                    P(0.082f, 0.146f), P(0f, 0.14f),
                }),
                // 偵測器:扁球外殼 + 腰部墨水環
                SensorShell = Lathe("SensorShell", new[]
                {
                    P(0f, -0.1f), P(0.07f, -0.094f), P(0.11f, -0.07f), P(0.13f, -0.03f), P(0.13f, 0.03f),
                    P(0.11f, 0.07f), P(0.07f, 0.094f), P(0f, 0.1f),
                }),
                SensorCore = Lathe("SensorCore", new[]
                {
                    P(0f, -0.028f), P(0.138f, -0.028f), P(0.138f, -0.028f), P(0.142f, 0f), P(0.138f, 0.028f),
                    P(0.138f, 0.028f), P(0f, 0.028f),
                }),
                DartTip = Lathe("AngleShooterTip", new[]
                {
                    P(0f, 0f), P(0.042f, 0f), P(0.042f, 0f), P(0.03f, 0.05f), P(0f, 0.13f),
                }),
                DashedRing = DashedRing("TorpedoReticle", 0.86f, 1f, 18, 0.62f),
                FieldRing = Ring("PointSensorRing", 0.9f, 1f, 64),
                Sphere = Lathe("ThrowableUnitSphere", SphereProfile(14)),
                Cylinder = Lathe("ThrowableUnitCylinder", new[]
                {
                    P(0f, -0.5f), P(0.5f, -0.5f), P(0.5f, -0.5f), P(0.5f, 0.5f), P(0.5f, 0.5f), P(0f, 0.5f),
                }),
                Cube = Box("ThrowableUnitCube"),
            };
        }

        private static Vector2 P(float radius, float height)
        {
            return new Vector2(radius, height);
        }

        // 半徑 0.5 的半圓輪廓(由南極到北極)
        private static Vector2[] SphereProfile(int rows)
        {
            var profile = new Vector2[rows + 1];
            for (int i = 0; i <= rows; i++)
            {
                float angle = -Mathf.PI * 0.5f + Mathf.PI * i / rows;
                profile[i] = new Vector2(Mathf.Max(0f, Mathf.Cos(angle) * 0.5f), Mathf.Sin(angle) * 0.5f);
            }
            profile[0].x = 0f;
            profile[rows].x = 0f;
            return profile;
        }

        private static Mesh Lathe(string name, Vector2[] profile)
        {
            int rows = profile.Length;
            int columns = Segments + 1;
            var vertices = new Vector3[rows * columns];
            var normals = new Vector3[rows * columns];
            var uvs = new Vector2[rows * columns];
            for (int i = 0; i < rows; i++)
            {
                Vector2 normal2 = ProfileNormal(profile, i);
                for (int s = 0; s < columns; s++)
                {
                    float angle = s * Mathf.PI * 2f / Segments;
                    float cos = Mathf.Cos(angle);
                    float sin = Mathf.Sin(angle);
                    int index = i * columns + s;
                    vertices[index] = new Vector3(profile[i].x * cos, profile[i].y, profile[i].x * sin);
                    normals[index] = new Vector3(normal2.x * cos, normal2.y, normal2.x * sin).normalized;
                    uvs[index] = new Vector2((float)s / Segments, (float)i / (rows - 1));
                }
            }
            var triangles = new List<int>(rows * Segments * 6);
            for (int i = 0; i < rows - 1; i++)
            {
                if ((profile[i] - profile[i + 1]).sqrMagnitude < 1e-10f)
                {
                    continue;
                }
                for (int s = 0; s < Segments; s++)
                {
                    int a = i * columns + s;
                    int b = a + 1;
                    int c = a + columns;
                    int d = c + 1;
                    triangles.Add(a);
                    triangles.Add(c);
                    triangles.Add(b);
                    triangles.Add(b);
                    triangles.Add(c);
                    triangles.Add(d);
                }
            }
            var mesh = new Mesh { name = name };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return SaveMesh(mesh);
        }

        // 輪廓點法線:兩側切線平均;與鄰點重合(折角)時只用另一側
        private static Vector2 ProfileNormal(Vector2[] profile, int i)
        {
            int last = profile.Length - 1;
            bool samePrev = i > 0 && (profile[i] - profile[i - 1]).sqrMagnitude < 1e-10f;
            bool sameNext = i < last && (profile[i] - profile[i + 1]).sqrMagnitude < 1e-10f;
            Vector2 tangent;
            if (sameNext || i == 0)
            {
                int next = Mathf.Min(sameNext ? i + 2 : i + 1, last);
                tangent = profile[next] - profile[i];
            }
            else if (samePrev || i == last)
            {
                int prev = Mathf.Max(samePrev ? i - 2 : i - 1, 0);
                tangent = profile[i] - profile[prev];
            }
            else
            {
                tangent = (profile[i + 1] - profile[i]).normalized + (profile[i] - profile[i - 1]).normalized;
            }
            if (tangent.sqrMagnitude < 1e-12f)
            {
                return Vector2.up;
            }
            tangent.Normalize();
            return new Vector2(tangent.y, -tangent.x);
        }

        // 邊長 1 的方塊(每面 4 頂點,平面法線)
        private static Mesh Box(string name)
        {
            var vertices = new List<Vector3>(24);
            var normals = new List<Vector3>(24);
            var uvs = new List<Vector2>(24);
            var triangles = new List<int>(36);
            Vector3[] axes = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (Vector3 n in axes)
            {
                Vector3 u = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 v = Vector3.Cross(n, u);
                int start = vertices.Count;
                Vector3 c = n * 0.5f;
                vertices.Add(c - u * 0.5f - v * 0.5f);
                vertices.Add(c + u * 0.5f - v * 0.5f);
                vertices.Add(c + u * 0.5f + v * 0.5f);
                vertices.Add(c - u * 0.5f + v * 0.5f);
                for (int k = 0; k < 4; k++)
                {
                    normals.Add(n);
                }
                uvs.Add(new Vector2(0f, 0f));
                uvs.Add(new Vector2(1f, 0f));
                uvs.Add(new Vector2(1f, 1f));
                uvs.Add(new Vector2(0f, 1f));
                Vector3 cross = Vector3.Cross(vertices[start + 1] - vertices[start], vertices[start + 2] - vertices[start]);
                if (Vector3.Dot(cross, n) > 0f)
                {
                    triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
                }
                else
                {
                    triangles.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
                }
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return SaveMesh(mesh);
        }

        // 平面環(朝上,外半徑 outer)
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

        // 虛線環(朝上,dashes 段,每段佔 fill 比例):Torpedo 的瞄準圈(S3 マルチミサイル式の照準)
        private static Mesh DashedRing(string name, float inner, float outer, int dashes, float fill)
        {
            const int perDash = 4;
            var vertices = new List<Vector3>(dashes * (perDash + 1) * 2);
            var normals = new List<Vector3>(vertices.Capacity);
            var uvs = new List<Vector2>(vertices.Capacity);
            var triangles = new List<int>(dashes * perDash * 6);
            float step = Mathf.PI * 2f / dashes;
            for (int d = 0; d < dashes; d++)
            {
                int start = vertices.Count;
                for (int k = 0; k <= perDash; k++)
                {
                    float angle = d * step + step * fill * k / perDash;
                    var dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    vertices.Add(dir * inner);
                    vertices.Add(dir * outer);
                    normals.Add(Vector3.up);
                    normals.Add(Vector3.up);
                    uvs.Add(new Vector2((float)k / perDash, 0f));
                    uvs.Add(new Vector2((float)k / perDash, 1f));
                }
                for (int k = 0; k < perDash; k++)
                {
                    int a = start + k * 2;
                    triangles.Add(a);
                    triangles.Add(a + 2);
                    triangles.Add(a + 1);
                    triangles.Add(a + 1);
                    triangles.Add(a + 2);
                    triangles.Add(a + 3);
                }
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
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
            if (mesh.tangents != null && mesh.tangents.Length == mesh.vertexCount)
            {
                existing.tangents = mesh.tangents;
            }
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(mesh);
            return existing;
        }

        // ── prefab ──

        // Torpedo:白鼻 + 隊伍色彈身 + 深色腰帶 + 金屬尾椎 + 四片尾翼(變身時展開)+ 尾燈;中心 = 碰撞球心。
        // 投擲物另有受擊碰撞體(Hull)、瞄準圈(Reticle)、8 顆散彈外觀(Drop0~7)
        private static GameObject BuildTorpedo(Palette p, Meshes m, bool held)
        {
            var root = new GameObject(held ? "TorpedoHeld" : "Torpedo");
            try
            {
                Transform model = Group(root.transform, "Model", Vector3.zero, held ? 0.7f : 1f);
                // 旋轉體沿 +Y 建模 → 轉 90° 讓鼻子朝 +Z(飛行方向)
                Transform body = Group(model, "Body", Vector3.zero, 1f);
                body.localRotation = Quaternion.Euler(90f, 0f, 0f);
                Part(body, "InkHull", m.TorpedoBody, held ? p.HeldInk : p.Ink, Vector3.zero, Vector3.zero, Vector3.one);
                Part(body, "Nose", m.TorpedoNose, p.Shell, Vector3.zero, Vector3.zero, Vector3.one);
                Part(body, "Tail", m.TorpedoTail, p.Metal, Vector3.zero, Vector3.zero, Vector3.one);
                Part(body, "Band", m.Cylinder, p.Dark, new Vector3(0f, 0.09f, 0f), Vector3.zero,
                    new Vector3(0.206f, 0.028f, 0.206f));
                Transform fins = Group(model, "Fins", new Vector3(0f, 0f, -0.24f), 1f);
                fins.localScale = held ? Vector3.one : new Vector3(0.35f, 0.35f, 1f);
                Part(fins, "FinUp", m.Cube, p.Shell, new Vector3(0f, 0.14f, 0f), Vector3.zero, new Vector3(0.016f, 0.1f, 0.12f));
                Part(fins, "FinDown", m.Cube, p.Shell, new Vector3(0f, -0.14f, 0f), Vector3.zero, new Vector3(0.016f, 0.1f, 0.12f));
                Part(fins, "FinLeft", m.Cube, p.Shell, new Vector3(-0.14f, 0f, 0f), Vector3.zero, new Vector3(0.1f, 0.016f, 0.12f));
                Part(fins, "FinRight", m.Cube, p.Shell, new Vector3(0.14f, 0f, 0f), Vector3.zero, new Vector3(0.1f, 0.016f, 0.12f));
                Part(model, "LightTail", m.Sphere, p.Light, new Vector3(0f, 0f, -0.375f), Vector3.zero, Vector3.one * 0.05f);
                if (held)
                {
                    return Save(root, "TorpedoHeld");
                }
                // 受擊碰撞體(Target 圖層,平時關閉;半徑執行期由設定寫入)
                var hullGo = new GameObject("Hull");
                hullGo.layer = TargetLayer;
                hullGo.transform.SetParent(root.transform, false);
                var hull = hullGo.AddComponent<SphereCollider>();
                hull.radius = 0.35f;
                hull.enabled = false;
                Transform reticle = Part(root.transform, "Reticle", m.DashedRing, p.Field, Vector3.zero, Vector3.zero,
                    Vector3.one);
                var reticleRenderer = Quiet(reticle);
                reticleRenderer.enabled = false;
                var drops = new Renderer[8];
                for (int i = 0; i < drops.Length; i++)
                {
                    Transform drop = Part(root.transform, "Drop" + i, m.Sphere, p.Ink, Vector3.zero, Vector3.zero,
                        Vector3.one * 0.2f);
                    drops[i] = Quiet(drop);
                    drops[i].enabled = false;
                }
                var torpedo = root.AddComponent<Torpedo>();
                var so = new SerializedObject(torpedo);
                so.FindProperty("_model").objectReferenceValue = model;
                so.FindProperty("_hull").objectReferenceValue = hull;
                so.FindProperty("_fins").objectReferenceValue = fins;
                so.FindProperty("_reticle").objectReferenceValue = reticleRenderer;
                SerializedProperty dropsProp = so.FindProperty("_droplets");
                dropsProp.arraySize = drops.Length;
                for (int i = 0; i < drops.Length; i++)
                {
                    dropsProp.GetArrayElementAtIndex(i).objectReferenceValue = drops[i];
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                return Save(root, "Torpedo");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // Fizzy Bomb:隊伍色罐身 + 白色標籤帶 + 金屬上蓋 + 拉環燈;直立建模、中心 = 碰撞球心(飛行時側躺翻滾)。
        // 手持模型掛 FizzyChargeIndicator(段數燈色、升段「喀」聲、連打時搖晃)
        private static GameObject BuildFizzy(Palette p, Meshes m, bool held)
        {
            var root = new GameObject(held ? "FizzyBombHeld" : "FizzyBomb");
            try
            {
                Transform model = Group(root.transform, "Model", Vector3.zero, held ? 0.8f : 1f);
                Part(model, "InkCan", m.FizzyCan, held ? p.HeldInk : p.Ink, Vector3.zero, Vector3.zero, Vector3.one);
                Part(model, "Label", m.Cylinder, p.Shell, new Vector3(0f, -0.01f, 0f), Vector3.zero,
                    new Vector3(0.236f, 0.11f, 0.236f));
                Part(model, "Lid", m.FizzyLid, p.Metal, Vector3.zero, Vector3.zero, Vector3.one);
                Transform light = Part(model, "LightTab", m.Sphere, p.Light, new Vector3(0.035f, 0.152f, 0f),
                    Vector3.zero, new Vector3(0.05f, 0.02f, 0.035f));
                if (held)
                {
                    var indicator = root.AddComponent<FizzyChargeIndicator>();
                    var so = new SerializedObject(indicator);
                    so.FindProperty("_light").objectReferenceValue = light.GetComponent<MeshRenderer>();
                    so.FindProperty("_body").objectReferenceValue = model;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    return Save(root, "FizzyBombHeld");
                }
                var bomb = root.AddComponent<FizzyBomb>();
                var bombSo = new SerializedObject(bomb);
                bombSo.FindProperty("_model").objectReferenceValue = model;
                bombSo.ApplyModifiedPropertiesWithoutUndo();
                return Save(root, "FizzyBomb");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // Point Sensor:白色扁球外殼 + 隊伍色腰環 + 天線 + 綠色感應燈;投擲物另有感應球(Dome)與地面雷達圈(FieldRing)
        private static GameObject BuildSensor(Palette p, Meshes m, bool held)
        {
            var root = new GameObject(held ? "PointSensorHeld" : "PointSensor");
            try
            {
                Transform model = Group(root.transform, "Model", Vector3.zero, held ? 0.8f : 1f);
                Part(model, "Shell", m.SensorShell, p.Shell, Vector3.zero, Vector3.zero, Vector3.one);
                Part(model, "InkCore", m.SensorCore, held ? p.HeldInk : p.Ink, Vector3.zero, Vector3.zero, Vector3.one);
                Part(model, "Antenna", m.Cylinder, p.Metal, new Vector3(0f, 0.14f, 0f), Vector3.zero,
                    new Vector3(0.016f, 0.09f, 0.016f));
                Part(model, "LightEye", m.Sphere, p.Light, new Vector3(0f, 0.195f, 0f), Vector3.zero, Vector3.one * 0.045f);
                Part(model, "Lens", m.Sphere, p.Dark, new Vector3(0f, 0f, 0.118f), Vector3.zero,
                    new Vector3(0.07f, 0.07f, 0.03f));
                if (held)
                {
                    return Save(root, "PointSensorHeld");
                }
                Transform dome = Part(root.transform, "Dome", m.Sphere, p.Field, Vector3.zero, Vector3.zero, Vector3.one);
                var domeRenderer = Quiet(dome);
                domeRenderer.enabled = false;
                Transform ring = Part(root.transform, "FieldRing", m.FieldRing, p.Field, Vector3.zero, Vector3.zero,
                    Vector3.one);
                var ringRenderer = Quiet(ring);
                ringRenderer.enabled = false;
                var sensor = root.AddComponent<PointSensor>();
                var so = new SerializedObject(sensor);
                so.FindProperty("_model").objectReferenceValue = model;
                so.FindProperty("_dome").objectReferenceValue = domeRenderer;
                so.FindProperty("_ring").objectReferenceValue = ringRenderer;
                so.ApplyModifiedPropertiesWithoutUndo();
                return Save(root, "PointSensor");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // Angle Shooter:飛鏢(隊伍色鏢身 + 金屬鏢頭 + 白色十字尾翼);投擲物另有世界座標線段(Line)
        private static GameObject BuildAngleShooter(Palette p, Meshes m, bool held)
        {
            var root = new GameObject(held ? "AngleShooterHeld" : "AngleShooter");
            try
            {
                Transform model = Group(root.transform, "Model", Vector3.zero, held ? 0.9f : 1f);
                Part(model, "InkShaft", m.Cylinder, held ? p.HeldInk : p.Ink, Vector3.zero, new Vector3(90f, 0f, 0f),
                    new Vector3(0.05f, 0.32f, 0.05f));
                Part(model, "Tip", m.DartTip, p.Metal, new Vector3(0f, 0f, 0.16f), new Vector3(90f, 0f, 0f), Vector3.one);
                Part(model, "FinV", m.Cube, p.Shell, new Vector3(0f, 0f, -0.14f), Vector3.zero,
                    new Vector3(0.012f, 0.12f, 0.09f));
                Part(model, "FinH", m.Cube, p.Shell, new Vector3(0f, 0f, -0.14f), Vector3.zero,
                    new Vector3(0.12f, 0.012f, 0.09f));
                if (held)
                {
                    return Save(root, "AngleShooterHeld");
                }
                var lineGo = new GameObject("Line");
                lineGo.transform.SetParent(root.transform, false);
                var line = lineGo.AddComponent<LineRenderer>();
                line.sharedMaterial = p.Line;
                line.useWorldSpace = true;
                line.positionCount = 0;
                line.widthMultiplier = 0.12f;
                line.numCapVertices = 2;
                line.numCornerVertices = 2;
                line.alignment = LineAlignment.View;
                line.shadowCastingMode = ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.enabled = false;
                var shooter = root.AddComponent<AngleShooter>();
                var so = new SerializedObject(shooter);
                so.FindProperty("_model").objectReferenceValue = model;
                so.FindProperty("_line").objectReferenceValue = line;
                so.ApplyModifiedPropertiesWithoutUndo();
                return Save(root, "AngleShooter");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static GameObject Save(GameObject root, string pascal)
        {
            string path = $"{PrefabFolder}/{pascal}.prefab";
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            if (saved == null)
            {
                Debug.LogError($"[THROWSETUP] ERROR 無法儲存 {path}");
            }
            return saved;
        }

        private static Transform Group(Transform parent, string name, Vector3 position, float scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = Vector3.one * scale;
            return go.transform;
        }

        // 純視覺網格(沒有碰撞體:投擲物本體不擋子彈與射線)
        private static Transform Part(Transform parent, string name, Mesh mesh, Material material, Vector3 position,
            Vector3 euler, Vector3 scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            return go.transform;
        }

        // 特效用網格:不投影、不收影
        private static MeshRenderer Quiet(Transform part)
        {
            var renderer = part.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }
    }
}

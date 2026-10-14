using System.Collections.Generic;
using SplatoonC.Gameplay.Combat;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SplatoonC.EditorBuild
{
    // 副武器 A1「炸彈 I」:Suction Bomb / Burst Bomb / Curling Bomb / Autobomb 的資產建立(冪等,每次重建):
    //   設定 Assets/Resources/Weapons/Sub_<id>.asset(ResetToS3 寫入 S3 0 AP 數值)、
    //   投擲物 prefab 與手持模型 prefab(Assets/Prefabs/Weapons/)、旋轉體網格(Assets/Prefabs/Weapons/Meshes/)、
    //   URP 材質(Assets/Materials/Weapons/)。
    // 外觀約定(SimulatedBomb):名稱以 Ink 開頭 = 隊伍色墨水部位;Light 開頭 = 指示燈。手持模型的墨水部位用
    // 角色的 TankInk 材質(CharacterVisual 換上後由 CharacterTint 染色)。
    // 由 M7Setup.Apply 經 ExtraSetups 呼叫;選單 Tools/SplatoonC/Weapons/Bombs I Setup。
    [InitializeOnLoad]
    public static class BombsSetup
    {
        private const string ConfigFolder = "Assets/Resources/Weapons";
        private const string PrefabFolder = "Assets/Prefabs/Weapons";
        private const string MeshFolder = PrefabFolder + "/Meshes";
        private const string MaterialFolder = "Assets/Materials/Weapons";
        private const string TankInkPath = "Assets/Materials/Characters/TankInk.mat";
        private const int Segments = 32;

        private sealed class Palette
        {
            public Material Ink;
            public Material HeldInk;
            public Material Shell;
            public Material Metal;
            public Material Dark;
            public Material EyeWhite;
            public Material Pupil;
            public Material Light;
            public Material Ring;
        }

        private sealed class Meshes
        {
            public Mesh SuctionCup;
            public Mesh SuctionBulb;
            public Mesh SuctionCap;
            public Mesh BurstDrop;
            public Mesh BurstKnot;
            public Mesh CurlingBase;
            public Mesh CurlingBand;
            public Mesh CurlingTop;
            public Mesh AutobombBody;
            public Mesh AutobombBeak;
            public Mesh SearchRing;
            public Mesh Sphere;
            public Mesh Cube;
            public Mesh Cylinder;
        }

        static BombsSetup()
        {
            System.Action create = Create;
            if (!M7Setup.ExtraSetups.Contains(create))
            {
                M7Setup.ExtraSetups.Add(create);
            }
        }

        [MenuItem("Tools/SplatoonC/Weapons/Bombs I Setup")]
        public static void Create()
        {
            if (Application.isPlaying)
            {
                Debug.LogError("[BOMBSETUP] ERROR Play mode 中不可建立資產");
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

            var suction = EnsureConfig<SuctionBombConfig>("suction_bomb");
            suction.ResetToS3();
            var burst = EnsureConfig<BurstBombConfig>("burst_bomb");
            burst.ResetToS3();
            var curling = EnsureConfig<CurlingBombConfig>("curling_bomb");
            curling.ResetToS3();
            var autobomb = EnsureConfig<AutobombConfig>("autobomb");
            autobomb.ResetToS3();

            Wire(suction, BuildSuction(palette, meshes, false), BuildSuction(palette, meshes, true));
            Wire(burst, BuildBurst(palette, meshes, false), BuildBurst(palette, meshes, true));
            Wire(curling, BuildCurling(palette, meshes, false), BuildCurling(palette, meshes, true));
            Wire(autobomb, BuildAutobomb(palette, meshes, false), BuildAutobomb(palette, meshes, true));
            AssetDatabase.SaveAssets();
            Debug.Log("[BOMBSETUP] 完成:Sub_suction_bomb / Sub_burst_bomb / Sub_curling_bomb / Sub_autobomb + prefab");
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
                Debug.Log($"[BOMBSETUP] 建立 {path}");
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
                // 墨水部位白底(執行期 MaterialPropertyBlock 套隊伍色),高光滑
                Ink = EnsureLit("BombInk", Color.white, 0f, 0.9f),
                HeldInk = AssetDatabase.LoadAssetAtPath<Material>(TankInkPath),
                Shell = EnsureLit("BombShell", new Color(0.93f, 0.93f, 0.95f), 0f, 0.6f),
                Metal = EnsureLit("BombMetal", new Color(0.58f, 0.6f, 0.64f), 0.6f, 0.6f),
                Dark = EnsureLit("BombDark", new Color(0.16f, 0.17f, 0.2f), 0f, 0.4f),
                EyeWhite = EnsureLit("BombEyeWhite", Color.white, 0f, 0.7f),
                Pupil = EnsureLit("BombPupil", new Color(0.04f, 0.04f, 0.06f), 0f, 0.8f),
                Light = EnsureUnlit("BombLight", Color.white, false),
                Ring = EnsureUnlit("BombSearchRing", new Color(1f, 1f, 1f, 0.5f), true),
            };
            if (palette.Ink == null || palette.Light == null || palette.Ring == null)
            {
                Debug.LogError("[BOMBSETUP] ERROR 材質建立失敗(找不到 URP shader?)");
                return null;
            }
            if (palette.HeldInk == null)
            {
                Debug.LogWarning($"[BOMBSETUP] 找不到 {TankInkPath}(先跑 Character Setup);手持模型改用 BombInk");
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

        private static Material EnsureUnlit(string name, Color color, bool transparent)
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
                // URP 材質從程式建立預設 Opaque;半透明要補齊這些欄位(CombatInfraSetup 同款);雙面(搜尋圈)
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", 0f);
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.SetFloat("_Cull", (float)CullMode.Off);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.SetOverrideTag("RenderType", "Transparent");
                material.renderQueue = (int)RenderQueue.Transparent;
            }
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        // ── 網格(旋轉體:輪廓 (半徑, 高度) 由下往上;連續重複的點 = 折角)──

        private static Meshes BuildMeshes()
        {
            var m = new Meshes
            {
                SuctionCup = Lathe("SuctionCup", new[]
                {
                    P(0f, 0f), P(0.13f, 0f), P(0.13f, 0f), P(0.13f, 0.012f), P(0.1f, 0.03f), P(0.06f, 0.058f),
                    P(0.045f, 0.075f), P(0.045f, 0.075f), P(0f, 0.075f),
                }),
                SuctionBulb = Lathe("SuctionBulb", new[]
                {
                    P(0f, 0.066f), P(0.055f, 0.066f), P(0.055f, 0.066f), P(0.075f, 0.08f), P(0.115f, 0.105f),
                    P(0.148f, 0.145f), P(0.16f, 0.19f), P(0.148f, 0.238f), P(0.112f, 0.28f), P(0.07f, 0.305f),
                    P(0.048f, 0.322f), P(0.048f, 0.322f), P(0f, 0.322f),
                }),
                SuctionCap = Lathe("SuctionCap", new[]
                {
                    P(0f, 0.315f), P(0.056f, 0.315f), P(0.056f, 0.315f), P(0.056f, 0.36f), P(0.056f, 0.36f),
                    P(0.035f, 0.372f), P(0f, 0.376f),
                }),
                BurstDrop = Lathe("BurstDrop", new[]
                {
                    P(0f, -0.15f), P(0.07f, -0.137f), P(0.118f, -0.095f), P(0.143f, -0.035f), P(0.14f, 0.025f),
                    P(0.114f, 0.078f), P(0.072f, 0.12f), P(0.04f, 0.148f), P(0.028f, 0.16f), P(0f, 0.165f),
                }),
                BurstKnot = Lathe("BurstKnot", new[]
                {
                    P(0f, 0.145f), P(0.034f, 0.145f), P(0.034f, 0.145f), P(0.036f, 0.175f), P(0.05f, 0.19f),
                    P(0.038f, 0.205f), P(0f, 0.21f),
                }),
                CurlingBase = Lathe("CurlingBase", new[]
                {
                    P(0f, 0f), P(0.22f, 0f), P(0.22f, 0f), P(0.262f, 0.012f), P(0.284f, 0.035f), P(0.286f, 0.05f),
                    P(0.286f, 0.05f), P(0f, 0.05f),
                }),
                CurlingBand = Lathe("CurlingBand", new[]
                {
                    P(0f, 0.05f), P(0.287f, 0.05f), P(0.287f, 0.05f), P(0.292f, 0.1f), P(0.287f, 0.15f),
                    P(0.287f, 0.15f), P(0f, 0.15f),
                }),
                CurlingTop = Lathe("CurlingTop", new[]
                {
                    P(0f, 0.15f), P(0.286f, 0.15f), P(0.286f, 0.15f), P(0.272f, 0.172f), P(0.21f, 0.195f),
                    P(0.11f, 0.208f), P(0f, 0.211f),
                }),
                AutobombBody = Lathe("AutobombBody", new[]
                {
                    P(0f, 0.05f), P(0.1f, 0.068f), P(0.168f, 0.128f), P(0.2f, 0.215f), P(0.192f, 0.305f),
                    P(0.152f, 0.378f), P(0.085f, 0.428f), P(0f, 0.445f),
                }),
                AutobombBeak = Lathe("AutobombBeak", new[]
                {
                    P(0f, 0f), P(0.072f, 0f), P(0.072f, 0f), P(0.05f, 0.06f), P(0f, 0.125f),
                }),
                SearchRing = Ring("AutobombSearchRing", 0.94f, 1f, 64),
                // 單位基本形(直徑 1;不用內建 primitive 網格,尺寸由這裡保證)
                Sphere = Lathe("BombUnitSphere", SphereProfile(12)),
                Cylinder = Lathe("BombUnitCylinder", new[]
                {
                    P(0f, -0.5f), P(0.5f, -0.5f), P(0.5f, -0.5f), P(0.5f, 0.5f), P(0.5f, 0.5f), P(0f, 0.5f),
                }),
                Cube = Box("BombUnitCube"),
            };
            return m;
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
                // 正面(Unity 順時針):法線 = (v1 − v0) × (v2 − v0) 要朝外
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

        private static Vector2 P(float radius, float height)
        {
            return new Vector2(radius, height);
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
                    // 外側面朝外(輪廓由下往上、繞 +Y 逆時針看是順時針 → Unity 正面)
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
                int next = sameNext ? i + 2 : i + 1;
                next = Mathf.Min(next, last);
                tangent = profile[next] - profile[i];
            }
            else if (samePrev || i == last)
            {
                int prev = samePrev ? i - 2 : i - 1;
                prev = Mathf.Max(prev, 0);
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
            // 輪廓由下往上:外法線 = (dy, −dr)
            return new Vector2(tangent.y, -tangent.x);
        }

        // 平面環(朝上,外半徑 outer):搜尋圈
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

        // Suction Bomb:吸盤(深灰)+ 燈泡形墨囊(隊伍色)+ 白蓋 + 紅色指示燈;吸盤底 = 模型原點,碰撞球心在上方 0.2
        private static GameObject BuildSuction(Palette p, Meshes m, bool held)
        {
            var root = new GameObject(held ? "SuctionBombHeld" : "SuctionBomb");
            try
            {
                Transform model = held ? Group(root.transform, "Model", new Vector3(0f, -0.19f, 0f), 0.75f)
                    : Group(root.transform, "Model", new Vector3(0f, -0.2f, 0f), 1f);
                Part(model, "Cup", m.SuctionCup, p.Dark, Vector3.zero, Vector3.zero, Vector3.one);
                Part(model, "InkBulb", m.SuctionBulb, held ? p.HeldInk : p.Ink, Vector3.zero, Vector3.zero, Vector3.one);
                Part(model, "Cap", m.SuctionCap, p.Shell, Vector3.zero, Vector3.zero, Vector3.one);
                Part(model, "LightTop", m.Sphere, p.Light, new Vector3(0f, 0.39f, 0f), Vector3.zero, Vector3.one * 0.056f);
                // 墨囊腰部的白色細環(辨識度)
                Part(model, "Band", m.Cylinder, p.Shell, new Vector3(0f, 0.19f, 0f), Vector3.zero,
                    new Vector3(0.326f, 0.012f, 0.326f));
                return Save(root, held, "SuctionBomb", held ? null : root.AddComponent<SuctionBomb>(), model);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // Burst Bomb:小水滴形墨球(隊伍色)+ 白色束口;中心 = 模型原點
        private static GameObject BuildBurst(Palette p, Meshes m, bool held)
        {
            var root = new GameObject(held ? "BurstBombHeld" : "BurstBomb");
            try
            {
                Transform model = Group(root.transform, "Model", Vector3.zero, held ? 0.9f : 1f);
                Part(model, "InkDrop", m.BurstDrop, held ? p.HeldInk : p.Ink, Vector3.zero, Vector3.zero, Vector3.one);
                Part(model, "Knot", m.BurstKnot, p.Shell, Vector3.zero, Vector3.zero, Vector3.one);
                return Save(root, held, "BurstBomb", held ? null : root.AddComponent<BurstBomb>(), model);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // Curling Bomb:冰壺(深色底座 + 隊伍色墨身 + 白色細環 + 金屬提把 + 指示燈);底 = 模型原點,碰撞球心在 0.22
        private static GameObject BuildCurling(Palette p, Meshes m, bool held)
        {
            var root = new GameObject(held ? "CurlingBombHeld" : "CurlingBomb");
            try
            {
                Transform model = held ? Group(root.transform, "Model", new Vector3(0f, -0.12f, 0f), 0.6f)
                    : Group(root.transform, "Model", new Vector3(0f, -0.22f, 0f), 1f);
                Part(model, "Base", m.CurlingBase, p.Dark, Vector3.zero, Vector3.zero, Vector3.one);
                Part(model, "InkBand", m.CurlingBand, held ? p.HeldInk : p.Ink, Vector3.zero, Vector3.zero, Vector3.one);
                Part(model, "InkTop", m.CurlingTop, held ? p.HeldInk : p.Ink, Vector3.zero, Vector3.zero, Vector3.one);
                // 墨帶與上蓋之間的白色細環(辨識度)
                Part(model, "Rim", m.Cylinder, p.Shell, new Vector3(0f, 0.15f, 0f), Vector3.zero,
                    new Vector3(0.585f, 0.012f, 0.585f));
                Part(model, "HandlePostA", m.Cube, p.Metal, new Vector3(0f, 0.235f, -0.09f), Vector3.zero,
                    new Vector3(0.036f, 0.06f, 0.036f));
                Part(model, "HandlePostB", m.Cube, p.Metal, new Vector3(0f, 0.235f, 0.09f), Vector3.zero,
                    new Vector3(0.036f, 0.06f, 0.036f));
                Part(model, "HandleBar", m.Cube, p.Metal, new Vector3(0f, 0.272f, 0.02f), Vector3.zero,
                    new Vector3(0.05f, 0.036f, 0.27f));
                Transform light = Part(model, "LightIndicator", m.Sphere, p.Light, new Vector3(0f, 0.296f, -0.07f),
                    Vector3.zero, Vector3.one * 0.05f);
                if (held)
                {
                    // 手持時的蓄力指示燈(讀持有者 ThrowController.SubCharge01)
                    var indicator = root.AddComponent<SubChargeIndicator>();
                    var so = new SerializedObject(indicator);
                    so.FindProperty("_light").objectReferenceValue = light.GetComponent<MeshRenderer>();
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                return Save(root, held, "CurlingBomb", held ? null : root.AddComponent<CurlingBomb>(), model);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // Autobomb:小雞形機器人(隊伍色身體 + 金屬鳥喙與雙腳 + 眼睛 + 天線燈);地面 = 模型原點,身體球心在 0.22
        private static GameObject BuildAutobomb(Palette p, Meshes m, bool held)
        {
            var root = new GameObject(held ? "AutobombHeld" : "Autobomb");
            try
            {
                Transform model = held ? Group(root.transform, "Model", new Vector3(0f, -0.16f, 0f), 0.65f)
                    : Group(root.transform, "Model", new Vector3(0f, -0.22f, 0f), 1f);
                Part(model, "InkBody", m.AutobombBody, held ? p.HeldInk : p.Ink, Vector3.zero, Vector3.zero, Vector3.one);
                Part(model, "Beak", m.AutobombBeak, p.Metal, new Vector3(0f, 0.25f, 0.175f), new Vector3(90f, 0f, 0f),
                    Vector3.one);
                for (int side = -1; side <= 1; side += 2)
                {
                    string suffix = side < 0 ? "L" : "R";
                    Part(model, "Eye" + suffix, m.Sphere, p.EyeWhite, new Vector3(0.07f * side, 0.33f, 0.155f),
                        Vector3.zero, Vector3.one * 0.085f);
                    Part(model, "Pupil" + suffix, m.Sphere, p.Pupil, new Vector3(0.073f * side, 0.334f, 0.19f),
                        Vector3.zero, Vector3.one * 0.04f);
                    // 腳:樞紐在髖部(走路時繞 X 擺動),腳掌是扁盒。
                    // 子物件名稱全 prefab 唯一(SoleL/SoleR…):覆寫既有 prefab 時 Unity 依名稱配對物件,
                    // 同名會重新配發 fileID,重跑設定就不是逐位元組相同
                    Transform hip = Group(model, "Foot" + suffix, new Vector3(0.085f * side, 0.075f, 0f), 1f);
                    Part(hip, "Sole" + suffix, m.Cube, p.Dark, new Vector3(0f, -0.055f, 0.035f), Vector3.zero,
                        new Vector3(0.085f, 0.035f, 0.15f));
                    Part(hip, "Leg" + suffix, m.Cylinder, p.Metal, new Vector3(0f, -0.02f, 0f), Vector3.zero,
                        new Vector3(0.03f, 0.06f, 0.03f));
                }
                Part(model, "Antenna", m.Cylinder, p.Metal, new Vector3(0f, 0.475f, -0.03f), Vector3.zero,
                    new Vector3(0.018f, 0.08f, 0.018f));
                Part(model, "LightAntenna", m.Sphere, p.Light, new Vector3(0f, 0.53f, -0.03f), Vector3.zero,
                    Vector3.one * 0.055f);
                Autobomb bomb = null;
                if (!held)
                {
                    // 搜尋圈:放在根物件下(不隨膨脹縮放),預設關閉
                    Transform ring = Part(root.transform, "SearchRing", m.SearchRing, p.Ring, new Vector3(0f, -0.2f, 0f),
                        Vector3.zero, Vector3.one);
                    var ringRenderer = ring.GetComponent<MeshRenderer>();
                    ringRenderer.shadowCastingMode = ShadowCastingMode.Off;
                    ringRenderer.receiveShadows = false;
                    ringRenderer.enabled = false;
                    bomb = root.AddComponent<Autobomb>();
                    var so = new SerializedObject(bomb);
                    so.FindProperty("_footLeft").objectReferenceValue = model.Find("FootL");
                    so.FindProperty("_footRight").objectReferenceValue = model.Find("FootR");
                    so.FindProperty("_searchRing").objectReferenceValue = ringRenderer;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                return Save(root, held, "Autobomb", bomb, model);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static GameObject Save(GameObject root, bool held, string pascal, SimulatedBomb bomb, Transform model)
        {
            if (bomb != null)
            {
                var so = new SerializedObject(bomb);
                so.FindProperty("_model").objectReferenceValue = model;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            string path = $"{PrefabFolder}/{pascal}{(held ? "Held" : "")}.prefab";
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            if (saved == null)
            {
                Debug.LogError($"[BOMBSETUP] ERROR 無法儲存 {path}");
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

        // 純視覺網格(沒有碰撞體:炸彈打不掉、不擋子彈與射線)
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
    }
}

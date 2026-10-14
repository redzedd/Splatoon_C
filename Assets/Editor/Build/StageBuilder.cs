using System.Collections.Generic;
using SplatoonC.Core;
using SplatoonC.Core.Painting;
using SplatoonC.Gameplay;
using SplatoonC.Gameplay.AI;
using SplatoonC.Gameplay.CameraRig;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Match;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using SplatoonC.Gameplay.UI;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace SplatoonC.EditorBuild
{
    // 塗地對戰場地產生器:以程式碼描述「半張地圖」,自動 180° 點對稱複製成另一隊的半場(Splatoon 所有塗地場都是點對稱)。
    // 冪等:每次重建整個場景檔(場景只存參數化方塊,網格執行期由 PaintableBlock 生成)。
    // 選單 Tools/SplatoonC/Build Stage/…;batchmode -executeMethod SplatoonC.EditorBuild.StageBuilder.BuildScorchGorge
    public static class StageBuilder
    {
        private const string MaterialFolder = "Assets/Materials/Stage";
        private const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";
        private const string SplashFxPrefabPath = "Assets/Prefabs/InkSplashFX.prefab";

        // 可塗方塊的 ink map 解析度:每像素 0.1m(50m 見方的地面約 500px)
        private const float TexelSize = 0.1f;

        public enum Mat
        {
            // 可塗:沙岩地磚頂 + 紅岩側面
            Block,
            // 可塗:木棧板
            Wood,
            // 可塗:淺色石材(中場/塔)
            Stone,
            // 不可塗:金屬/貨櫃
            Metal,
            // 不可塗:場地外圍岩壁
            Cliff,
            // 不可塗,無碰撞:深淵底的遠景岩地
            Abyss,
        }

        private sealed class Block
        {
            public string Name;
            public bool Wedge;
            // 方塊:中心;斜坡:底面中心
            public Vector3 Position;
            public Vector3 Size;
            public float Yaw;
            public Mat Material;
            public bool Mirror;
            public PaintableMeshBuilder.BoxFaces Faces = AllButBottom;
        }

        private const PaintableMeshBuilder.BoxFaces AllButBottom =
            PaintableMeshBuilder.BoxFaces.All & ~PaintableMeshBuilder.BoxFaces.Bottom;

        private sealed class Layout
        {
            public string SceneName;
            public string DisplayName;
            public readonly List<Block> Blocks = new List<Block>();
            public Vector3 BaseCenter;
            public float BaseRadius;
            public Vector3[] Spawns;
            public float KillPlaneY = -10f;
            public Color SkyTint;
            public Color GroundTint;
            public Color FogColor;
            public Color SunColor;
            public Vector3 SunEuler;

            // 軸對齊方塊(min/max)
            public void Box(string name, float x0, float y0, float z0, float x1, float y1, float z1, Mat mat,
                bool mirror = true)
            {
                Blocks.Add(new Block
                {
                    Name = name,
                    Position = new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f),
                    Size = new Vector3(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0), Mathf.Abs(z1 - z0)),
                    Material = mat,
                    Mirror = mirror,
                });
            }

            // 斜坡:底面中心、寬×高×長;yaw 0 = 往 +Z 升高
            public void Ramp(string name, float x, float y, float z, float width, float height, float length,
                float yaw, Mat mat, bool mirror = true)
            {
                Blocks.Add(new Block
                {
                    Name = name,
                    Wedge = true,
                    Position = new Vector3(x, y, z),
                    Size = new Vector3(width, height, length),
                    Yaw = yaw,
                    Material = mat,
                    Mirror = mirror,
                });
            }
        }

        [MenuItem("Tools/SplatoonC/Build Stage/Scorch Gorge (焦岩峽谷)")]
        public static void BuildScorchGorge()
        {
            Build(ScorchGorge());
        }

        // ───────────────────────── 場地:焦岩峽谷(參考 S3 Scorch Gorge 8.0 版的配置)─────────────────────────
        // 座標:長軸 Z,Alpha 基地在 -Z,Bravo 在 +Z;全長 ~170m,基地寬 25、自家區 45、中場 60(含兩側深淵缺口)。
        // 高度:盆地 0、中場台地 1.5、基地 3、中央塔頂 6;所有落差都是 1.5 的倍數(1.5 可跳上、3 要游牆)。
        private static Layout ScorchGorge()
        {
            var L = new Layout
            {
                SceneName = "TurfWar_ScorchGorge",
                DisplayName = "焦岩峽谷",
                BaseCenter = new Vector3(0f, 3f, -79f),
                BaseRadius = 10f,
                Spawns = new[]
                {
                    new Vector3(-4.5f, 3f, -80f), new Vector3(-1.5f, 3f, -81f),
                    new Vector3(1.5f, 3f, -81f), new Vector3(4.5f, 3f, -80f),
                },
                SkyTint = new Color(0.45f, 0.62f, 0.9f),
                GroundTint = new Color(0.42f, 0.3f, 0.24f),
                FogColor = new Color(0.86f, 0.74f, 0.62f),
                SunColor = new Color(1f, 0.93f, 0.82f),
                SunEuler = new Vector3(48f, -35f, 0f),
            };

            // ── 基地(出生台)z -85..-71
            L.Box("Base_Platform", -12.5f, 0f, -85f, 12.5f, 3f, -71f, Mat.Block);
            L.Box("Base_WingLeft", -22.5f, 0f, -80f, -12.5f, 1.5f, -62f, Mat.Wood);
            L.Box("Base_WingRight", 12.5f, 0f, -78f, 22.5f, 1.5f, -66f, Mat.Block);
            L.Ramp("Base_RampLeft", -6.5f, 0f, -67.5f, 5f, 3f, 7f, 180f, Mat.Wood);
            L.Ramp("Base_RampRight", 6.5f, 0f, -67.5f, 5f, 3f, 7f, 180f, Mat.Wood);
            L.Ramp("Wing_RampLeft", -18.5f, 0f, -60f, 5f, 1.5f, 4f, 180f, Mat.Wood);
            L.Ramp("Wing_RampRight", 17.5f, 0f, -64f, 5f, 1.5f, 4f, 180f, Mat.Block);
            L.Box("Base_CrateL", -12f, 3f, -85f, -10f, 4.5f, -83f, Mat.Metal);
            L.Box("Base_CrateR", 10f, 3f, -85f, 12f, 4.5f, -83f, Mat.Metal);

            // ── 自家盆地 z -71..-12(地面 y 0)
            L.Box("Basin_Floor", -22.5f, -1f, -71f, 22.5f, 0f, -12f, Mat.Block);
            L.Box("Basin_LaneWallL", -9f, 0f, -58f, -8f, 3f, -52f, Mat.Block);
            L.Box("Basin_LaneWallR", 8f, 0f, -40f, 9f, 3f, -34f, Mat.Block);
            L.Box("Basin_Pillar", -1.5f, 0f, -49f, 1.5f, 2.5f, -46f, Mat.Stone);
            L.Box("Basin_SideBlockL", -21f, 0f, -48f, -14f, 1.5f, -38f, Mat.Wood);
            L.Box("Basin_SideCrateL", -19f, 1.5f, -46f, -17f, 2.7f, -44f, Mat.Metal);
            L.Box("Basin_SideBlockR", 14f, 0f, -50f, 21f, 3f, -40f, Mat.Block);
            L.Ramp("Basin_SideRampR", 17.5f, 0f, -36.5f, 4f, 3f, 7f, 180f, Mat.Wood);
            L.Box("PreMid_Perch", -9f, 0f, -22f, -5f, 3f, -18f, Mat.Stone);
            L.Box("PreMid_Ledge", 8f, 0f, -22f, 16f, 1.5f, -16f, Mat.Wood);
            L.Box("Basin_LowCover", -3f, 0f, -30f, 3f, 1.1f, -29f, Mat.Metal);

            // ── 中場 z -12..12(台地 y 1.5);左前、右後各一個深淵缺口(點對稱)
            L.Box("Mid_Plateau", -21f, -1f, -12f, 21f, 1.5f, 12f, Mat.Stone, mirror: false);
            L.Box("Mid_WingBehindNotch", -30f, -1f, 6f, -21f, 1.5f, 12f, Mat.Stone);
            L.Ramp("Mid_RampLeft", -12f, 0f, -14.25f, 6f, 1.5f, 4.5f, 0f, Mat.Stone);
            L.Ramp("Mid_RampRight", 12f, 0f, -14.25f, 6f, 1.5f, 4.5f, 0f, Mat.Stone);
            L.Box("Mid_Tower", -4f, 1.5f, -4f, 4f, 6f, 4f, Mat.Block, mirror: false);
            L.Ramp("Mid_TowerRamp", 0f, 1.5f, -8f, 3f, 4.5f, 8f, 0f, Mat.Wood);
            L.Box("Mid_CoverWall", -15f, 1.5f, -6f, -10f, 3f, -5f, Mat.Block);
            L.Box("Mid_Crate", 9f, 1.5f, -7f, 11f, 3f, -5f, Mat.Metal);

            // ── 外圍岩壁(不可塗)與深淵
            L.Box("Cliff_Back", -24.5f, 0f, -87f, 24.5f, 9f, -85f, Mat.Cliff);
            L.Box("Cliff_BackCornerL", -24.5f, 0f, -85f, -12.5f, 9f, -80f, Mat.Cliff);
            L.Box("Cliff_BackCornerR", 12.5f, 0f, -85f, 24.5f, 9f, -78f, Mat.Cliff);
            L.Box("Cliff_SideL", -24.5f, -1f, -80f, -22.5f, 7f, -14f, Mat.Cliff);
            L.Box("Cliff_SideR", 22.5f, -1f, -78f, 24.5f, 7f, -14f, Mat.Cliff);
            L.Box("Cliff_MidEdgeL", -32f, -16f, -14f, -22.5f, 7f, -12f, Mat.Cliff);
            L.Box("Cliff_MidEdgeR", 22.5f, -16f, -14f, 32f, 7f, -12f, Mat.Cliff);
            L.Box("Cliff_MidSideL", -32f, -16f, -12f, -30f, 7f, 12f, Mat.Cliff, mirror: false);
            L.Box("Cliff_MidSideR", 30f, -16f, -12f, 32f, 7f, 12f, Mat.Cliff, mirror: false);
            L.Box("Abyss_Floor", -34f, -20f, -16f, 34f, -18f, 16f, Mat.Abyss, mirror: false);
            return L;
        }

        // ───────────────────────── 建置 ─────────────────────────

        private static void Build(Layout layout)
        {
            if (Application.isPlaying)
            {
                Debug.LogError("[STAGE] Play mode 中不可建置場地");
                return;
            }
            var materials = EnsureMaterials();
            if (materials == null)
            {
                return;
            }
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            var splashPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SplashFxPrefabPath);
            if (playerPrefab == null || playerPrefab.GetComponent<Inkling>() == null)
            {
                Debug.LogError("[STAGE] ERROR 找不到 Player.prefab 或其上沒有 Inkling");
                return;
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var stageRoot = new GameObject("Stage");
            var geometry = new GameObject("Geometry").transform;
            geometry.SetParent(stageRoot.transform, false);

            int paintable = 0;
            foreach (Block block in layout.Blocks)
            {
                CreateBlock(block, geometry, materials, false);
                if (block.Mirror)
                {
                    CreateBlock(block, geometry, materials, true);
                }
                if (IsPaintable(block.Material))
                {
                    paintable += block.Mirror ? 2 : 1;
                }
            }

            CreateBases(layout, stageRoot.transform);
            CreateLighting(layout);
            CreatePostProcessing();
            CreateCamera();
            CreateSystems(playerPrefab, splashPrefab);

            // NavMesh:收集 Stage 底下的實體碰撞(方塊 BoxCollider、斜坡 MeshCollider)
            var surface = stageRoot.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = ~GameLayers.InklingMask;
            surface.BuildNavMesh();

            string scenePath = $"Assets/Scenes/{layout.SceneName}.unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            string navPath = $"Assets/Scenes/{layout.SceneName}_NavMesh.asset";
            if (surface.navMeshData != null)
            {
                AssetDatabase.DeleteAsset(navPath);
                AssetDatabase.CreateAsset(surface.navMeshData, navPath);
                EditorUtility.SetDirty(surface);
                EditorSceneManager.SaveScene(scene, scenePath);
            }
            AddToBuildSettings(scenePath);
            AssetDatabase.SaveAssets();

            float floorArea = 0f;
            foreach (var s in Object.FindObjectsByType<PaintableSurface>(FindObjectsSortMode.None))
            {
                float a = 0f, b = 0f, t = 0f;
                s.AccumulateTurf(ref a, ref b, ref t);
                floorArea += t;
            }
            Debug.Log($"[STAGE] 完成 {layout.DisplayName} → {scenePath} 方塊={geometry.childCount} 可塗={paintable} " +
                      $"地面投影面積={floorArea:0}m²(≈{floorArea / 3.3f:0}p) NavMesh={(surface.navMeshData != null ? "OK" : "FAIL")}");
        }

        private static bool IsPaintable(Mat mat)
        {
            return mat == Mat.Block || mat == Mat.Wood || mat == Mat.Stone;
        }

        private static void CreateBlock(Block block, Transform parent, Dictionary<Mat, Material> materials, bool mirrored)
        {
            Vector3 position = block.Position;
            float yaw = block.Yaw;
            if (mirrored)
            {
                // 點對稱:繞原點垂直軸轉 180°
                position = new Vector3(-position.x, position.y, -position.z);
                yaw += 180f;
            }
            var go = new GameObject(mirrored ? block.Name + "_B" : block.Name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            go.layer = GameLayers.Default;
            go.isStatic = false;

            Material material = materials[block.Material];
            if (IsPaintable(block.Material))
            {
                go.AddComponent<MeshFilter>();
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                var paintableBlock = go.AddComponent<PaintableBlock>();
                paintableBlock.Configure(block.Wedge ? PaintableBlock.BlockShape.Wedge : PaintableBlock.BlockShape.Box,
                    block.Size, TexelSize, block.Faces);
                var surface = go.AddComponent<PaintableSurface>();
                var so = new SerializedObject(surface);
                so.FindProperty("_splatShader").objectReferenceValue = Shader.Find("SplatoonC/InkSplat");
                so.ApplyModifiedPropertiesWithoutUndo();
                return;
            }

            // 不可塗:一般方塊(斜坡形狀的不可塗物目前用不到)
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Mesh";
            cube.transform.SetParent(go.transform, false);
            cube.transform.localScale = block.Size;
            cube.GetComponent<MeshRenderer>().sharedMaterial = material;
            if (block.Material == Mat.Abyss)
            {
                Object.DestroyImmediate(cube.GetComponent<Collider>());
                cube.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        private static void CreateBases(Layout layout, Transform parent)
        {
            CreateBase(Team.Alpha, layout.BaseCenter, layout.Spawns, layout.BaseRadius, false, parent);
            CreateBase(Team.Bravo, layout.BaseCenter, layout.Spawns, layout.BaseRadius, true, parent);
        }

        private static void CreateBase(Team team, Vector3 center, Vector3[] spawns, float radius, bool mirrored,
            Transform parent)
        {
            Vector3 Map(Vector3 p) => mirrored ? new Vector3(-p.x, p.y, -p.z) : p;
            var go = new GameObject("TeamBase_" + team);
            go.transform.SetParent(parent, false);
            go.transform.position = Map(center);
            var points = new Transform[spawns.Length];
            Quaternion facing = Quaternion.Euler(0f, mirrored ? 180f : 0f, 0f);
            for (int i = 0; i < spawns.Length; i++)
            {
                var spawn = new GameObject("SpawnPoint_" + i).transform;
                spawn.SetParent(go.transform, false);
                spawn.SetPositionAndRotation(Map(spawns[i]) + Vector3.up * 0.05f, facing);
                points[i] = spawn;
            }
            var teamBase = go.AddComponent<TeamBase>();
            teamBase.Configure(team, points, radius);
        }

        private static void CreateLighting(Layout layout)
        {
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = layout.SunColor;
            sun.intensity = 1.45f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            sunGo.transform.rotation = Quaternion.Euler(layout.SunEuler);
            sunGo.AddComponent<UniversalAdditionalLightData>();
            RenderSettings.sun = sun;

            var sky = new Material(Shader.Find("Skybox/Procedural"));
            sky.SetFloat("_SunSize", 0.035f);
            sky.SetFloat("_SunSizeConvergence", 6f);
            sky.SetFloat("_AtmosphereThickness", 0.75f);
            sky.SetColor("_SkyTint", layout.SkyTint);
            sky.SetColor("_GroundColor", layout.GroundTint);
            sky.SetFloat("_Exposure", 1.25f);
            string skyPath = $"{MaterialFolder}/Sky_{layout.SceneName}.mat";
            AssetDatabase.DeleteAsset(skyPath);
            AssetDatabase.CreateAsset(sky, skyPath);
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1.1f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = layout.FogColor;
            RenderSettings.fogStartDistance = 70f;
            RenderSettings.fogEndDistance = 320f;
            DynamicGI.UpdateEnvironment();
        }

        private static void CreatePostProcessing()
        {
            const string profilePath = "Assets/Settings/Stage_PostFX.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, profilePath);
            }
            // Splatoon 的畫面:高彩度、輕微 bloom(墨面高光會亮)、中性色調映射、淡暗角
            var bloom = GetOrAdd<Bloom>(profile);
            bloom.threshold.Override(1.0f);
            bloom.intensity.Override(0.35f);
            bloom.scatter.Override(0.6f);
            var tone = GetOrAdd<Tonemapping>(profile);
            tone.mode.Override(TonemappingMode.Neutral);
            var color = GetOrAdd<ColorAdjustments>(profile);
            color.saturation.Override(18f);
            color.contrast.Override(8f);
            color.postExposure.Override(0.15f);
            var vignette = GetOrAdd<Vignette>(profile);
            vignette.intensity.Override(0.18f);
            vignette.smoothness.Override(0.45f);
            EditorUtility.SetDirty(profile);

            var go = new GameObject("Global Volume");
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;
        }

        private static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (!profile.TryGet(out T component))
            {
                component = profile.Add<T>(true);
                AssetDatabase.AddObjectToAsset(component, profile);
            }
            component.active = true;
            return component;
        }

        private static void CreateCamera()
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 600f;
            go.AddComponent<AudioListener>();
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.renderShadows = true;
            var rig = go.AddComponent<ThirdPersonCameraRig>();
            var so = new SerializedObject(rig);
            so.FindProperty("_occlusionMask").intValue = ~GameLayers.InklingMask;
            so.ApplyModifiedPropertiesWithoutUndo();
            go.transform.SetPositionAndRotation(new Vector3(0f, 8f, -92f), Quaternion.Euler(15f, 0f, 0f));
        }

        private static void CreateSystems(GameObject playerPrefab, GameObject splashPrefab)
        {
            var go = new GameObject("GameSystems");
            var match = go.AddComponent<MatchController>();
            var so = new SerializedObject(match);
            so.FindProperty("_inklingPrefab").objectReferenceValue = playerPrefab.GetComponent<Inkling>();
            so.ApplyModifiedPropertiesWithoutUndo();

            var fx = go.AddComponent<InkSplashFxPool>();
            var fxSo = new SerializedObject(fx);
            SerializedProperty prefabProp = fxSo.FindProperty("_prefab");
            if (prefabProp != null && splashPrefab != null)
            {
                prefabProp.objectReferenceValue = splashPrefab.GetComponent<ParticleSystem>() != null
                    ? (Object)splashPrefab.GetComponent<ParticleSystem>()
                    : splashPrefab;
                fxSo.ApplyModifiedPropertiesWithoutUndo();
            }
            go.AddComponent<BotDirector>();
            go.AddComponent<MatchHud>();
            go.AddComponent<MatchPresentation>();
            // 其他系統(音效)由各自的 Setup 方法附加
            foreach (var hook in SystemHooks)
            {
                hook(go);
            }
        }

        // 其他模組(HUD、bot、音效)在這裡註冊「建場景時要掛上的元件」
        public static readonly List<System.Action<GameObject>> SystemHooks = new List<System.Action<GameObject>>();

        private static void AddToBuildSettings(string scenePath)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Exists(s => s.path == scenePath))
            {
                return;
            }
            scenes.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        // ───────────────────────── 材質 ─────────────────────────

        private static Dictionary<Mat, Material> EnsureMaterials()
        {
            if (!AssetDatabase.IsValidFolder(MaterialFolder))
            {
                AssetDatabase.CreateFolder("Assets/Materials", "Stage");
            }
            Shader paintable = Shader.Find("SplatoonC/PaintableSurface");
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (paintable == null || lit == null)
            {
                Debug.LogError("[STAGE] ERROR 找不到 SplatoonC/PaintableSurface 或 URP Lit shader");
                return null;
            }
            var result = new Dictionary<Mat, Material>
            {
                [Mat.Block] = PaintableMaterial("Stage_Block", paintable,
                    top: Hex("#d6c19a"), topPattern: Hex("#b49d76"), topType: 1, topScale: 3f,
                    side: Hex("#b8663f"), sidePattern: Hex("#8f4a2c"), sideType: 3, sideScale: 2.2f),
                [Mat.Wood] = PaintableMaterial("Stage_Wood", paintable,
                    top: Hex("#a8784a"), topPattern: Hex("#7d5532"), topType: 2, topScale: 0.45f,
                    side: Hex("#8a5c36"), sidePattern: Hex("#6a4426"), sideType: 2, sideScale: 0.45f),
                [Mat.Stone] = PaintableMaterial("Stage_Stone", paintable,
                    top: Hex("#cfc6b6"), topPattern: Hex("#a89e8e"), topType: 1, topScale: 2f,
                    side: Hex("#a99a86"), sidePattern: Hex("#857563"), sideType: 3, sideScale: 1.8f),
                [Mat.Metal] = LitMaterial("Stage_Metal", lit, Hex("#6f8494"), 0.55f, 0.55f),
                [Mat.Cliff] = LitMaterial("Stage_Cliff", lit, Hex("#9a5334"), 0f, 0.15f),
                [Mat.Abyss] = LitMaterial("Stage_Abyss", lit, Hex("#4a2a1e"), 0f, 0.05f),
            };
            AssetDatabase.SaveAssets();
            return result;
        }

        private static Material PaintableMaterial(string name, Shader shader, Color top, Color topPattern, int topType,
            float topScale, Color side, Color sidePattern, int sideType, float sideScale)
        {
            Material m = LoadOrCreate(name, shader);
            m.SetColor("_BaseColor", top);
            m.SetColor("_PatternColor", topPattern);
            m.SetFloat("_PatternType", topType);
            m.SetFloat("_PatternScale", topScale);
            m.SetFloat("_PatternStrength", 0.55f);
            m.SetFloat("_TileVariation", 0.07f);
            m.SetFloat("_UseSideLook", 1f);
            m.SetColor("_SideColor", side);
            m.SetColor("_SidePatternColor", sidePattern);
            m.SetFloat("_SidePatternType", sideType);
            m.SetFloat("_SidePatternScale", sideScale);
            m.enableInstancing = false;
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material LitMaterial(string name, Shader shader, Color color, float metallic, float smoothness)
        {
            Material m = LoadOrCreate(name, shader);
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material LoadOrCreate(string name, Shader shader)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader != shader)
            {
                m.shader = shader;
            }
            return m;
        }

        private static Color Hex(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out Color c) ? c : Color.magenta;
        }
    }
}

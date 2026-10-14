using System.Collections.Generic;
using System.IO;
using SplatoonC.Gameplay.Match;
using SplatoonC.Gameplay.UI.Menu;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace SplatoonC.EditorBuild
{
    // 主選單場景與場地清單的產生器(冪等、便宜,掛在 M7Setup.ExtraSetups 每次接線都會跑):
    //   1. Resources/StageCatalog:補齊已知場地 + Assets/Scenes/TurfWar_*.unity(已存在的條目保留)
    //   2. Assets/Scenes/MainMenu.unity:不存在或版本過舊才重建(場景只有相機、EventSystem、MainMenuController,UI 執行期建)
    //   3. Build Settings:MainMenu 第一、訓練場(SampleScene)、再來所有場地,其餘原有場景保留在後面
    // 選單 Tools/SplatoonC/Build Main Menu(強制重建);batchmode -executeMethod SplatoonC.EditorBuild.MenuBuilder.Build
    [InitializeOnLoad]
    public static class MenuBuilder
    {
        public const string MenuScenePath = "Assets/Scenes/MainMenu.unity";
        public const string TrainingScenePath = "Assets/Scenes/SampleScene.unity";
        public const string StageCatalogPath = "Assets/Resources/StageCatalog.asset";
        private const string VersionField = "_sceneVersion: ";

        static MenuBuilder()
        {
            M7Setup.ExtraSetups.Add(Ensure);
        }

        // 已知場地的選單資料(產生器建好的場景);預覽色取自 StageBuilder 的材質色
        private static List<StageCatalog.Stage> KnownStages()
        {
            return new List<StageCatalog.Stage>
            {
                new StageCatalog.Stage
                {
                    SceneName = "TurfWar_ScorchGorge",
                    DisplayName = "焦岩峽谷",
                    EnglishName = "Scorch Gorge",
                    PreviewColor = new Color(0.84f, 0.62f, 0.42f),
                    PreviewAccent = new Color(0.72f, 0.4f, 0.25f),
                    Description = "紅岩峽谷裡的點對稱塗地場(S3的8.0版配置):中央高塔、兩側深淵缺口,基地前的盆地是主要的塗地區。",
                },
            };
        }

        [MenuItem("Tools/SplatoonC/Build Main Menu")]
        public static void Build()
        {
            if (Application.isPlaying)
            {
                Debug.LogError("[MENU] Play mode 中不可建置主選單");
                return;
            }
            EnsureCatalog();
            BuildScene();
            EnsureBuildSettings();
            AssetDatabase.SaveAssets();
        }

        // 冪等:只補缺的、只在版本不同時重建場景
        public static void Ensure()
        {
            if (Application.isPlaying)
            {
                return;
            }
            EnsureCatalog();
            if (!SceneUpToDate())
            {
                BuildScene();
            }
            EnsureBuildSettings();
        }

        private static bool SceneUpToDate()
        {
            if (!File.Exists(MenuScenePath))
            {
                return false;
            }
            string text = File.ReadAllText(MenuScenePath);
            return text.Contains(VersionField + MainMenuController.CurrentSceneVersion + "\n")
                   || text.Contains(VersionField + MainMenuController.CurrentSceneVersion + "\r");
        }

        private static void EnsureCatalog()
        {
            M7Setup.EnsureFolder("Assets", "Resources");
            var catalog = AssetDatabase.LoadAssetAtPath<StageCatalog>(StageCatalogPath);
            bool created = false;
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<StageCatalog>();
                AssetDatabase.CreateAsset(catalog, StageCatalogPath);
                created = true;
            }
            var stages = new List<StageCatalog.Stage>(catalog.Stages);
            bool changed = created;
            foreach (StageCatalog.Stage known in KnownStages())
            {
                if (!File.Exists($"Assets/Scenes/{known.SceneName}.unity"))
                {
                    continue;
                }
                int index = stages.FindIndex(s => s.SceneName == known.SceneName);
                if (index < 0)
                {
                    stages.Add(known);
                    changed = true;
                }
                else if (!Same(stages[index], known))
                {
                    // 已知場地的選單資料以程式碼為準(改了名稱/說明/配色就更新)
                    stages[index] = known;
                    changed = true;
                }
            }
            // 其他產生器建好的塗地場景(還沒有選單資料):用場景名當名稱先列出來
            foreach (string path in Directory.GetFiles("Assets/Scenes", "TurfWar_*.unity"))
            {
                string scene = Path.GetFileNameWithoutExtension(path);
                if (stages.FindIndex(s => s.SceneName == scene) >= 0)
                {
                    continue;
                }
                stages.Add(new StageCatalog.Stage
                {
                    SceneName = scene,
                    DisplayName = scene.Substring("TurfWar_".Length),
                    EnglishName = scene.Substring("TurfWar_".Length),
                    PreviewColor = new Color(0.6f, 0.6f, 0.65f),
                    PreviewAccent = new Color(0.45f, 0.45f, 0.5f),
                    Description = "",
                });
                changed = true;
            }
            // 場景檔已刪除的條目移除
            int removed = stages.RemoveAll(s => !File.Exists($"Assets/Scenes/{s.SceneName}.unity"));
            if (changed || removed > 0)
            {
                catalog.SetStages(stages);
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssets();
                Debug.Log($"[MENU] 場地清單更新:{stages.Count} 個場地");
            }
        }

        private static bool Same(StageCatalog.Stage a, StageCatalog.Stage b)
        {
            return a.SceneName == b.SceneName && a.DisplayName == b.DisplayName && a.EnglishName == b.EnglishName
                   && a.Description == b.Description && a.PreviewColor == b.PreviewColor && a.PreviewAccent == b.PreviewAccent;
        }

        private static void BuildScene()
        {
            // 以 additive 新場景建置,不動使用者目前開著的場景;有未命名場景時只能用 Single(批次模式沒有使用者的場景)
            bool additive = true;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path))
                {
                    additive = false;
                }
            }
            if (!additive && !Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.LogWarning("[MENU] 取消:目前場景未存檔");
                return;
            }
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                additive ? NewSceneMode.Additive : NewSceneMode.Single);
            SceneManager.SetActiveScene(scene);

            var cameraGo = new GameObject("Main Camera");
            cameraGo.tag = "MainCamera";
            var cam = cameraGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = MenuTheme.Background;
            cam.orthographic = true;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 50f;
            cameraGo.AddComponent<AudioListener>();
            var cameraData = cameraGo.AddComponent<UniversalAdditionalCameraData>();
            cameraData.renderPostProcessing = false;
            cameraData.renderShadows = false;

            var eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<InputSystemUIInputModule>();

            var menu = new GameObject("MainMenu");
            var controller = menu.AddComponent<MainMenuController>();
            var so = new SerializedObject(controller);
            so.FindProperty("_sceneVersion").intValue = MainMenuController.CurrentSceneVersion;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, MenuScenePath);
            if (additive)
            {
                if (previous.IsValid() && previous.isLoaded)
                {
                    SceneManager.SetActiveScene(previous);
                }
                EditorSceneManager.CloseScene(scene, true);
            }
            Debug.Log($"[MENU] 主選單場景建置完成 → {MenuScenePath}(版本 {MainMenuController.CurrentSceneVersion})");
        }

        // Build Settings:主選單第一、訓練場、所有場地,其餘原有的保留在後面(沒變就不寫,避免無謂的檔案變動)
        private static void EnsureBuildSettings()
        {
            var wanted = new List<string> { MenuScenePath };
            if (File.Exists(TrainingScenePath))
            {
                wanted.Add(TrainingScenePath);
            }
            var catalog = AssetDatabase.LoadAssetAtPath<StageCatalog>(StageCatalogPath);
            if (catalog != null)
            {
                foreach (StageCatalog.Stage stage in catalog.Stages)
                {
                    string path = $"Assets/Scenes/{stage.SceneName}.unity";
                    if (File.Exists(path) && !wanted.Contains(path))
                    {
                        wanted.Add(path);
                    }
                }
            }
            var result = new List<EditorBuildSettingsScene>();
            foreach (string path in wanted)
            {
                result.Add(new EditorBuildSettingsScene(path, true));
            }
            foreach (EditorBuildSettingsScene existing in EditorBuildSettings.scenes)
            {
                if (!wanted.Contains(existing.path) && File.Exists(existing.path))
                {
                    result.Add(existing);
                }
            }
            EditorBuildSettingsScene[] current = EditorBuildSettings.scenes;
            bool same = current.Length == result.Count;
            for (int i = 0; same && i < current.Length; i++)
            {
                same = current[i].path == result[i].path && current[i].enabled == result[i].enabled;
            }
            if (!same)
            {
                EditorBuildSettings.scenes = result.ToArray();
                Debug.Log($"[MENU] Build Settings:{string.Join(", ", wanted)}");
            }
        }

        // 打包用:Build Settings 內啟用的場景(先確保主選單在第一個)
        public static string[] BuildScenes()
        {
            Ensure();
            var scenes = new List<string>();
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled && File.Exists(scene.path))
                {
                    scenes.Add(scene.path);
                }
            }
            return scenes.ToArray();
        }
    }
}

using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Debugging;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SplatoonC.EditorBuild
{
    // M2 步驟 1 的一次性接線(batchmode -executeMethod 呼叫):
    // 武器調參落資產 + GameSystems 掛 AutoPerfRun。冪等,可重跑。
    public static class M2Setup
    {
        public static void Apply()
        {
            var config = AssetDatabase.LoadAssetAtPath<WeaponConfig>("Assets/Data/WeaponConfig.asset");
            if (config == null)
            {
                Debug.LogError("[M2SETUP] 找不到 WeaponConfig.asset");
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                }
                return;
            }
            var soConfig = new SerializedObject(config);
            soConfig.FindProperty("_muzzleSpeed").floatValue = 32f;
            soConfig.FindProperty("_projectileGravity").floatValue = -10f;
            soConfig.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);

            var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");

            // InkSplat 只被執行期 Shader.Find 使用,無資產引用會被 build 剔除
            // (2026-09-01 standalone 實測:塗色整個壞死)。填進場景欄位建立引用。
            var splatShader = Shader.Find("SplatoonC/InkSplat");
            var ground = GameObject.Find("Ground");
            if (splatShader == null || ground == null)
            {
                Debug.LogError("[M2SETUP] 找不到 InkSplat shader 或 Ground");
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                }
                return;
            }
            var surface = ground.GetComponent<SplatoonC.Gameplay.Painting.PaintableSurface>();
            var soSurface = new SerializedObject(surface);
            soSurface.FindProperty("_splatShader").objectReferenceValue = splatShader;
            soSurface.ApplyModifiedPropertiesWithoutUndo();

            var systems = GameObject.Find("GameSystems");
            if (systems == null)
            {
                Debug.LogError("[M2SETUP] 找不到 GameSystems");
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                }
                return;
            }
            if (systems.GetComponent<AutoPerfRun>() == null)
            {
                systems.AddComponent<AutoPerfRun>();
            }
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[M2SETUP] 完成:WeaponConfig 32/-10、GameSystems 掛 AutoPerfRun");
        }

        public static void ApplyAndBuild()
        {
            Apply();
            ProjectBuilder.BuildWindows();
        }
    }
}

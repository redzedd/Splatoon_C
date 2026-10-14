using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SplatoonC.EditorBuild
{
    // M2 步驟 1:一鍵 Windows standalone build(選單或自動化呼叫)。
    // 打包 Build Settings 內的場景:主選單(第一個,啟動畫面)+ 訓練場 + 所有場地(MenuBuilder 維護順序)。
    public static class ProjectBuilder
    {
        private const string OutputPath = "Builds/Windows/Splatoon_C.exe";

        [MenuItem("Tools/SplatoonC/Build Windows")]
        public static void BuildWindows()
        {
            var options = new BuildPlayerOptions
            {
                scenes = MenuBuilder.BuildScenes(),
                locationPathName = OutputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            Debug.Log($"[BUILD] result={summary.result} sizeMB={summary.totalSize / (1024f * 1024f):F0} " +
                $"time={summary.totalTime.TotalSeconds:F0}s errors={summary.totalErrors} 輸出={OutputPath}");
            if (Application.isBatchMode && summary.result != BuildResult.Succeeded)
            {
                EditorApplication.Exit(1);
            }
        }
    }
}

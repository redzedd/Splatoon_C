using System;

namespace SplatoonC.Gameplay.Debugging
{
    // 標記 play mode AutoTest(類別需有 public static void Run(),結束印 [AUTOTEST] DONE passed=N failed=M)。
    // AutoTestSuite 以 TypeCache 自動收進套件,新模組的測試不必改套件清單。
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class AutoTestAttribute : Attribute
    {
        public const string TrainingScene = "Assets/Scenes/SampleScene.unity";
        public const string MatchScene = "Assets/Scenes/TurfWar_ScorchGorge.unity";

        // 執行場景(預設試射場)
        public string Scene { get; set; } = TrainingScene;

        // 逾時秒數(沒有 DONE 標記就算失敗)
        public float Timeout { get; set; } = 90f;

        // 套件內排序(小的先跑;同值依類別名)
        public int Order { get; set; } = 100;
    }
}

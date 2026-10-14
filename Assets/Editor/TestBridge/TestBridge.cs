using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace SplatoonC.EditorTools
{
    // verify 管道的測試入口:結果以 [TESTRUN] 標記寫進 console,供 MCP 輪詢掃描。
    public static class TestBridge
    {
        [MenuItem("Tools/SplatoonC/Run EditMode Tests")]
        public static void RunEditModeTests()
        {
            RunEditModeTests(null);
        }

        // onFinished(passed, failed):批次驗證用
        public static void RunEditModeTests(System.Action<int, int> onFinished)
        {
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new ResultLogger { OnFinished = onFinished });
            api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode }));
        }

        // Test Framework 的工作(含 RunFinished 之後的場景還原)是否仍在進行(內部 API,以反射讀取)
        public static bool IsRunActive()
        {
            var method = typeof(TestRunnerApi).GetMethod("IsRunActive",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            return method != null && method.Invoke(null, null) is bool active && active;
        }

        private class ResultLogger : ICallbacks
        {
            public System.Action<int, int> OnFinished;

            public void RunStarted(ITestAdaptor testsToRun)
            {
                Debug.Log("[TESTRUN] START");
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                Debug.Log($"[TESTRUN] DONE passed={result.PassCount} failed={result.FailCount} skipped={result.SkipCount}");
                OnFinished?.Invoke(result.PassCount, result.FailCount);
            }

            public void TestStarted(ITestAdaptor test)
            {
            }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (result.TestStatus == TestStatus.Failed && !result.Test.IsSuite)
                {
                    Debug.LogError($"[TESTRUN] FAIL {result.FullName}: {result.Message}");
                }
            }
        }
    }
}

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
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new ResultLogger());
            api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode }));
        }

        private class ResultLogger : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun)
            {
                Debug.Log("[TESTRUN] START");
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                Debug.Log($"[TESTRUN] DONE passed={result.PassCount} failed={result.FailCount} skipped={result.SkipCount}");
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

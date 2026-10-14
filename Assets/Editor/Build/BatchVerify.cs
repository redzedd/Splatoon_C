using System;
using System.IO;
using System.Reflection;
using System.Text;
using SplatoonC.EditorTools;
using UnityEditor;
using UnityEngine;

namespace SplatoonC.EditorBuild
{
    // 無介面批次驗證(CI / 平行工作樹用):接線 → play mode AutoTest 套件 → EditMode 測試 → 寫結果檔 → 以結束碼離開。
    // EditMode 放最後:Test Framework 在 RunFinished 之後還會還原場景,且這個工作會跨 domain reload 續跑,
    // 若之後才進 play mode,AutoTest 會載到被還原成空白的場景。
    //
    //   Unity.exe -batchmode -projectPath <專案> -logFile <log> -executeMethod SplatoonC.EditorBuild.BatchVerify.Run
    //       [-verifySetups "SplatoonC.EditorBuild.M7Setup.Apply,SplatoonC.EditorBuild.AudioSetup.Apply"]
    //       [-verifyEditMode]
    //       [-verifyAutoTests all | "類別A,類別B"]
    //       [-verifyResult <結果檔路徑>]
    //
    // 結束碼:0 全過、1 有測試失敗、2 接線/執行錯誤。不要加 -quit(play mode 測試要跑完才離開)。
    // 腳本編譯失敗時 Unity 找不到本方法會直接結束——先看 log 的 error CS。
    // 進 play mode 會 domain reload,進度存在 SessionState。
    [InitializeOnLoad]
    public static class BatchVerify
    {
        private const string KeyActive = "SplatoonC.Verify.Active";
        private const string KeyResultPath = "SplatoonC.Verify.ResultPath";
        private const string KeyReport = "SplatoonC.Verify.Report";
        private const string KeyFailed = "SplatoonC.Verify.Failed";
        private const string KeyAutoTests = "SplatoonC.Verify.AutoTests";
        private const string KeyStartedAt = "SplatoonC.Verify.StartedAt";
        private const string KeyEditMode = "SplatoonC.Verify.EditMode";

        static BatchVerify()
        {
            // domain reload 後仍在驗證中:加上全域逾時保險(GlobalTimeoutSeconds)
            if (SessionState.GetBool(KeyActive, false))
            {
                EditorApplication.update += Watchdog;
            }
        }

        // -executeMethod 在專案啟動完成前就被呼叫,之後啟動流程還會載入預設場景;等編輯器閒置 2 秒再開始
        public static void Run()
        {
            double startAt = EditorApplication.timeSinceStartup + 2.0;
            void WaitStartup()
            {
                if (EditorApplication.timeSinceStartup < startAt || EditorApplication.isCompiling || EditorApplication.isUpdating)
                {
                    return;
                }
                EditorApplication.update -= WaitStartup;
                RunNow();
            }
            EditorApplication.update += WaitStartup;
        }

        private static void RunNow()
        {
            string[] args = Environment.GetCommandLineArgs();
            string setups = Arg(args, "-verifySetups");
            bool editMode = Has(args, "-verifyEditMode");
            string autoTests = Arg(args, "-verifyAutoTests");
            string resultPath = Arg(args, "-verifyResult") ?? Path.GetFullPath("Temp/verify_result.txt");

            SessionState.SetBool(KeyActive, true);
            SessionState.SetString(KeyResultPath, resultPath);
            SessionState.SetString(KeyReport, "");
            SessionState.SetInt(KeyFailed, 0);
            SessionState.SetString(KeyAutoTests, autoTests ?? "");
            SessionState.SetBool(KeyEditMode, editMode);
            SessionState.SetFloat(KeyStartedAt, (float)EditorApplication.timeSinceStartup);
            EditorApplication.update -= Watchdog;
            EditorApplication.update += Watchdog;
            Report("[VERIFY] START " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            if (!string.IsNullOrEmpty(setups))
            {
                foreach (string method in setups.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!InvokeStatic(method.Trim()))
                    {
                        Complete(2);
                        return;
                    }
                }
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }

            StartAutoTests();
        }

        // 最後一步:EditMode 測試(要求時),等 Test Framework 完全收尾後才結束
        private static void FinishWithEditMode()
        {
            if (!SessionState.GetBool(KeyEditMode, false))
            {
                Complete(SessionState.GetInt(KeyFailed, 0) > 0 ? 1 : 0);
                return;
            }
            TestBridge.RunEditModeTests((passed, failed) =>
            {
                Report($"[VERIFY] EDITMODE passed={passed} failed={failed}");
                if (failed > 0)
                {
                    MarkFailed();
                }
                double readyAt = EditorApplication.timeSinceStartup + 2.0;
                void WaitIdle()
                {
                    if (TestRunActive() && EditorApplication.timeSinceStartup < readyAt + 30.0
                        || EditorApplication.timeSinceStartup < readyAt)
                    {
                        return;
                    }
                    EditorApplication.update -= WaitIdle;
                    Complete(SessionState.GetInt(KeyFailed, 0) > 0 ? 1 : 0);
                }
                EditorApplication.update += WaitIdle;
            });
        }

        private static bool TestRunActive()
        {
            return TestBridge.IsRunActive();
        }

        private static void StartAutoTests()
        {
            string autoTests = SessionState.GetString(KeyAutoTests, "");
            if (string.IsNullOrEmpty(autoTests))
            {
                FinishWithEditMode();
                return;
            }
            string[] queue;
            if (autoTests == "all")
            {
                queue = AutoTestSuite.FullQueue();
            }
            else
            {
                var entries = new System.Collections.Generic.List<string>();
                string[] full = AutoTestSuite.FullQueue();
                foreach (string name in autoTests.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string match = Array.Find(full, e => e.StartsWith(name.Trim() + "|", StringComparison.Ordinal));
                    if (match == null)
                    {
                        Report($"[VERIFY] ERROR 找不到 AutoTest {name}");
                        MarkFailed();
                        continue;
                    }
                    entries.Add(match);
                }
                queue = entries.ToArray();
            }
            if (queue.Length == 0)
            {
                FinishWithEditMode();
                return;
            }
            AutoTestSuite.RunList(string.Join(";", queue));
        }

        // AutoTestSuite 跑完時呼叫
        public static void OnSuiteFinished(string summary, int passed, int failed, int broken)
        {
            if (!SessionState.GetBool(KeyActive, false))
            {
                return;
            }
            Report(summary.TrimEnd());
            Report($"[VERIFY] AUTOTEST cases_passed={passed} cases_failed={failed} tests_broken={broken}");
            if (failed > 0 || broken > 0)
            {
                MarkFailed();
            }
            FinishWithEditMode();
        }

        // 全量(80 組 AutoTest + EditMode)批次約 45 分鐘,留餘裕到 70 分鐘
        private const float GlobalTimeoutSeconds = 4200f;

        private static void Watchdog()
        {
            if (!SessionState.GetBool(KeyActive, false))
            {
                EditorApplication.update -= Watchdog;
                return;
            }
            float elapsed = (float)EditorApplication.timeSinceStartup - SessionState.GetFloat(KeyStartedAt, 0f);
            if (elapsed > GlobalTimeoutSeconds)
            {
                Report($"[VERIFY] ERROR 全域逾時 {GlobalTimeoutSeconds / 60f:0} 分鐘");
                Complete(2);
            }
        }

        private static bool InvokeStatic(string fullName)
        {
            int dot = fullName.LastIndexOf('.');
            if (dot <= 0)
            {
                Report($"[VERIFY] ERROR 接線方法名稱不完整:{fullName}");
                return false;
            }
            string typeName = fullName.Substring(0, dot);
            string methodName = fullName.Substring(dot + 1);
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(typeName);
                MethodInfo method = type?.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                    null, Type.EmptyTypes, null);
                if (method == null)
                {
                    continue;
                }
                try
                {
                    method.Invoke(null, null);
                    Report($"[VERIFY] SETUP {fullName} OK");
                    return true;
                }
                catch (Exception e)
                {
                    Report($"[VERIFY] ERROR 接線 {fullName} 例外:{(e.InnerException ?? e).Message}");
                    Debug.LogException(e.InnerException ?? e);
                    return false;
                }
            }
            Report($"[VERIFY] ERROR 找不到接線方法 {fullName}");
            return false;
        }

        private static void MarkFailed()
        {
            SessionState.SetInt(KeyFailed, SessionState.GetInt(KeyFailed, 0) + 1);
        }

        private static void Report(string line)
        {
            Debug.Log(line);
            SessionState.SetString(KeyReport, SessionState.GetString(KeyReport, "") + line + "\n");
        }

        private static void Complete(int exitCode)
        {
            Report($"[VERIFY] DONE exit={exitCode}");
            string path = SessionState.GetString(KeyResultPath, Path.GetFullPath("Temp/verify_result.txt"));
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                File.WriteAllText(path, SessionState.GetString(KeyReport, ""), new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Debug.LogError("[VERIFY] 無法寫結果檔:" + e.Message);
            }
            SessionState.SetBool(KeyActive, false);
            EditorApplication.update -= Watchdog;
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
            }
        }

        private static string Arg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }
            return null;
        }

        private static bool Has(string[] args, string name)
        {
            return Array.Exists(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        }
    }
}

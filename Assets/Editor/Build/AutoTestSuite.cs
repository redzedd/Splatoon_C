using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SplatoonC.Gameplay.Debugging;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SplatoonC.EditorBuild
{
    // Play mode AutoTest 套件執行器:每個測試開新的 play session(多數 M4 測試要求乾淨狀態),
    // 呼叫 <類別>.Run(),等 [AUTOTEST] DONE / [PROBE] DONE 標記,退出後跑下一個,最後印 [SUITE] DONE 摘要。
    // 進 Play 會 domain reload,進度存在 SessionState,[InitializeOnLoad] 重新掛回 update 迴圈。
    // 選單 Tools/SplatoonC/Run AutoTest Suite;單一測試可用 RunOne("類別名")。
    // 清單 = 下面的固定清單 + 所有標了 [AutoTest] 的類別(新模組的測試自動收進來)。
    [InitializeOnLoad]
    public static class AutoTestSuite
    {
        private const string KeyQueue = "SplatoonC.Suite.Queue";
        private const string KeyIndex = "SplatoonC.Suite.Index";
        private const string KeyStage = "SplatoonC.Suite.Stage";
        private const string KeyStartedAt = "SplatoonC.Suite.StartedAt";
        private const string KeyResults = "SplatoonC.Suite.Results";
        private const string KeyDone = "SplatoonC.Suite.CurrentDone";
        private const string KeyLine = "SplatoonC.Suite.CurrentLine";

        private const string Training = "Assets/Scenes/SampleScene.unity";

        // 類別名|場景|逾時秒數
        private static readonly string[] DefaultQueue =
        {
            "PaintAutoTest|" + Training + "|40",
            "ShootingAutoTest|" + Training + "|40",
            "WallPaintAutoTest|" + Training + "|40",
            "ClimbAutoTest|" + Training + "|60",
            "InkTankAutoTest|" + Training + "|60",
            "DiveAutoTest|" + Training + "|40",
            "SwimFeelAutoTest|" + Training + "|60",
            "FeelAutoTest|" + Training + "|60",
            "LocomotionAutoTest|" + Training + "|60",
            "RoadAutoTest|" + Training + "|60",
            "AimAutoTest|" + Training + "|60",
            "CombatAutoTest|" + Training + "|90",
            "BombAutoTest|" + Training + "|90",
            "SpecialAutoTest|" + Training + "|90",
            "InkstrikeAutoTest|" + Training + "|120",
            "S3AlignmentAutoTest|" + Training + "|90",
        };

        private enum Stage
        {
            Idle = 0,
            EnteringPlay = 1,
            WaitingWarmup = 2,
            Running = 3,
            Exiting = 4,
            OpeningScene = 5,
        }

        static AutoTestSuite()
        {
            EditorApplication.update += Tick;
            Application.logMessageReceived += OnLog;
        }

        [MenuItem("Tools/SplatoonC/Run AutoTest Suite")]
        public static void RunAll()
        {
            Start(FullQueue());
        }

        // 固定清單 + [AutoTest] 標記的類別(依 Order、類別名排序,不重複)
        public static string[] FullQueue()
        {
            var queue = new List<string>(DefaultQueue);
            var known = new HashSet<string>(DefaultQueue.Select(e => e.Split('|')[0]));
            var discovered = TypeCache.GetTypesWithAttribute<AutoTestAttribute>()
                .Where(t => !t.IsAbstract && !known.Contains(t.Name))
                .Select(t => (type: t, attr: (AutoTestAttribute)Attribute.GetCustomAttribute(t, typeof(AutoTestAttribute))))
                .OrderBy(x => x.attr.Order).ThenBy(x => x.type.Name, StringComparer.Ordinal);
            foreach (var (type, attr) in discovered)
            {
                queue.Add($"{type.Name}|{attr.Scene}|{attr.Timeout:0}");
            }
            return queue.ToArray();
        }

        public static void RunOne(string className)
        {
            foreach (string entry in FullQueue())
            {
                if (entry.StartsWith(className + "|", StringComparison.Ordinal))
                {
                    Start(new[] { entry });
                    return;
                }
            }
            Debug.LogError($"[SUITE] 找不到測試 {className}");
        }

        // 自訂清單:"類別名|場景路徑|逾時" 以 ; 分隔
        public static void RunList(string entries)
        {
            Start(entries.Split(';'));
        }

        private static void Start(string[] queue)
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("[SUITE] 請先退出 Play mode");
                return;
            }
            SessionState.SetString(KeyQueue, string.Join(";", queue));
            SessionState.SetInt(KeyIndex, 0);
            SessionState.SetString(KeyResults, "");
            SetStage(Stage.EnteringPlay);
            BeginCurrent();
        }

        private static string[] Queue => SessionState.GetString(KeyQueue, "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        private static int Index => SessionState.GetInt(KeyIndex, 0);
        private static Stage CurrentStage => (Stage)SessionState.GetInt(KeyStage, 0);

        private static void SetStage(Stage stage)
        {
            SessionState.SetInt(KeyStage, (int)stage);
            SessionState.SetFloat(KeyStartedAt, (float)EditorApplication.timeSinceStartup);
        }

        private static float Elapsed => (float)EditorApplication.timeSinceStartup - SessionState.GetFloat(KeyStartedAt, 0f);

        private static void Parse(string entry, out string className, out string scene, out float timeout)
        {
            string[] parts = entry.Split('|');
            className = parts[0];
            scene = parts.Length > 1 ? parts[1] : Training;
            timeout = parts.Length > 2 && float.TryParse(parts[2], out float t) ? t : 60f;
        }

        private static void BeginCurrent()
        {
            string[] queue = Queue;
            if (Index >= queue.Length)
            {
                Finish();
                return;
            }
            Parse(queue[Index], out string className, out string scene, out _);
            SessionState.SetBool(KeyDone, false);
            SessionState.SetString(KeyLine, "");
            Debug.Log($"[SUITE] ({Index + 1}/{queue.Length}) {className} @ {scene}");
            EditorSceneManager.OpenScene(scene);
            // 進 Play 改在 Tick 確認作用中場景正確後才進(批次模式下啟動/退出 Play 的場景還原可能晚一拍蓋掉剛開的場景)
            SetStage(Stage.OpeningScene);
        }

        private static void Tick()
        {
            Stage stage = CurrentStage;
            if (stage == Stage.Idle)
            {
                return;
            }
            string[] queue = Queue;
            if (Index >= queue.Length)
            {
                return;
            }
            Parse(queue[Index], out string className, out string scenePath, out float timeout);
            switch (stage)
            {
                case Stage.OpeningScene:
                    if (Elapsed < 0.5f)
                    {
                        break;
                    }
                    if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != scenePath)
                    {
                        if (Elapsed > 20f)
                        {
                            Record(className, "ERROR 無法開啟場景 " + scenePath);
                            Advance();
                            break;
                        }
                        EditorSceneManager.OpenScene(scenePath);
                        break;
                    }
                    if (Application.isBatchMode)
                    {
                        PinScriptsForBatch();
                    }
                    SetStage(Stage.EnteringPlay);
                    EditorApplication.EnterPlaymode();
                    break;
                case Stage.EnteringPlay:
                    if (EditorApplication.isPlaying)
                    {
                        if (Application.isBatchMode)
                        {
                            // 批次模式沒有 vsync,Play 迴圈不限速(實測每幀 ≈ 2ms);既有測試以互動編輯器 ~60fps 撰寫
                            //(「按下後第 2 幀已下沉」「等 30 幀鏡頭平滑」),不限速會假紅。鎖 60fps 對齊實際遊玩條件。
                            QualitySettings.vSyncCount = 0;
                            Application.targetFrameRate = 60;
                        }
                        SetStage(Stage.WaitingWarmup);
                    }
                    else if (Elapsed > 60f)
                    {
                        Record(className, "ERROR 無法進入 Play mode");
                        Advance();
                    }
                    break;
                case Stage.WaitingWarmup:
                    if (Elapsed > 1.5f)
                    {
                        Invoke(className);
                        SetStage(Stage.Running);
                    }
                    break;
                case Stage.Running:
                    if (SessionState.GetBool(KeyDone, false))
                    {
                        Record(className, SessionState.GetString(KeyLine, ""));
                        SetStage(Stage.Exiting);
                        EditorApplication.ExitPlaymode();
                    }
                    else if (Elapsed > timeout)
                    {
                        Record(className, $"TIMEOUT {timeout}s(沒有 DONE 標記)");
                        SetStage(Stage.Exiting);
                        EditorApplication.ExitPlaymode();
                    }
                    break;
                case Stage.Exiting:
                    if (!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode && Elapsed > 1f)
                    {
                        Advance();
                    }
                    break;
            }
        }

        // 批次模式:進 Play 的 domain reload 還原場景時,若 MonoScript 資產已被卸載,場景上的元件會變成 missing script。
        // 進 Play 前把所有執行期腳本標成不可卸載,並記錄作用中場景的 missing 數(診斷用)。
        private static void PinScriptsForBatch()
        {
            int pinned = 0;
            foreach (MonoScript script in MonoImporter.GetAllRuntimeMonoScripts())
            {
                if (script != null && script.GetClass() != null)
                {
                    script.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                    pinned++;
                }
            }
            int missing = 0;
            foreach (GameObject root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    missing += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                }
            }
            Debug.Log($"[SUITE] 批次模式:固定 {pinned} 個腳本資產;進 Play 前場景 missing script = {missing}");
        }

        private static void Invoke(string className)
        {
            Type type = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType("SplatoonC.Gameplay.Debugging." + className);
                if (type != null)
                {
                    break;
                }
            }
            // [AutoTest] 類別可以在任何命名空間
            if (type == null)
            {
                type = TypeCache.GetTypesWithAttribute<AutoTestAttribute>().FirstOrDefault(t => t.Name == className);
            }
            var run = type?.GetMethod("Run", Type.EmptyTypes);
            if (run == null)
            {
                Debug.LogError($"[SUITE] 找不到 {className}.Run()");
                SessionState.SetString(KeyLine, "ERROR 找不到 Run()");
                SessionState.SetBool(KeyDone, true);
                return;
            }
            // SquidCoverage 的 60 秒效能段在套件中略過
            var skipField = type.GetField("SkipPerfPhase");
            skipField?.SetValue(null, true);
            run.Invoke(null, null);
        }

        private static void OnLog(string message, string stackTrace, LogType type)
        {
            if (CurrentStage != Stage.Running)
            {
                return;
            }
            if (message.StartsWith("[AUTOTEST] DONE", StringComparison.Ordinal)
                || message.StartsWith("[PROBE] DONE", StringComparison.Ordinal))
            {
                SessionState.SetString(KeyLine, message);
                SessionState.SetBool(KeyDone, true);
            }
        }

        private static void Record(string className, string line)
        {
            string results = SessionState.GetString(KeyResults, "");
            results += className + " => " + line + "\n";
            SessionState.SetString(KeyResults, results);
        }

        private static void Advance()
        {
            SessionState.SetInt(KeyIndex, Index + 1);
            BeginCurrent();
        }

        private static void Finish()
        {
            SetStage(Stage.Idle);
            string results = SessionState.GetString(KeyResults, "");
            int passed = 0;
            int failed = 0;
            int broken = 0;
            var summary = new StringBuilder();
            foreach (string line in results.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                summary.AppendLine("[SUITE] " + line);
                int p = ReadInt(line, "passed=");
                int f = ReadInt(line, "failed=");
                if (p < 0 || f < 0)
                {
                    broken++;
                    continue;
                }
                passed += p;
                failed += f;
            }
            Debug.Log(summary.ToString());
            Debug.Log($"[SUITE] DONE cases_passed={passed} cases_failed={failed} tests_broken={broken}");
            BatchVerify.OnSuiteFinished(summary.ToString(), passed, failed, broken);
        }

        private static int ReadInt(string line, string key)
        {
            int i = line.IndexOf(key, StringComparison.Ordinal);
            if (i < 0)
            {
                return -1;
            }
            i += key.Length;
            int j = i;
            while (j < line.Length && char.IsDigit(line[j]))
            {
                j++;
            }
            return int.TryParse(line.Substring(i, j - i), out int v) ? v : -1;
        }
    }
}

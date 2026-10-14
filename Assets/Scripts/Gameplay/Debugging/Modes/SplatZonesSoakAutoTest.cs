using System.Collections;
using SplatoonC.Core;
using SplatoonC.Core.Match;
using SplatoonC.Core.Match.Ranked;
using SplatoonC.Gameplay.AI;
using SplatoonC.Gameplay.Match;
using SplatoonC.Gameplay.UI.Menu;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SplatoonC.Gameplay.Debugging
{
    // 真格區域 8 bot 觀戰 soak(從主選單走真實 UI 事件路徑選「真格區域」開賽):
    //   標題 → 對戰 → 模式頁右移到「真格區域」(已開放)→ 場地 → 武器 → 開始 → 對戰場景以真格區域開賽
    //   → 本機玩家交給 bot(8 bot)→ DebugSetRemaining 快轉、3 倍速跑完一場
    // 斷言:全程無例外、區域真的被佔領過、計數真的有動、bot 的塗地目標確實指向區域、判定走計數且與 Verdict 一致。
    [AutoTest(Scene = MenuFlowAutoTest.MenuScene, Timeout = 300f, Order = 401)]
    public sealed class SplatZonesSoakAutoTest : AutoTestBase
    {
        private const float BattleSeconds = 150f;
        private const float TimeScale = 3f;

        private int _exceptions;
        private string _firstException;
        private int _captures;
        private int _controlChanges;
        private int _penalties;

        public static void Run()
        {
            Launch<SplatZonesSoakAutoTest>();
        }

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            Application.logMessageReceived += OnLog;
        }

        private void OnDisable()
        {
            Application.logMessageReceived -= OnLog;
            Time.timeScale = 1f;
        }

        private void OnDestroy()
        {
            MatchSettings.Mode = MatchMode.TurfWar;
        }

        private void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Exception)
            {
                _exceptions++;
                if (_firstException == null)
                {
                    _firstException = message + " @ " + (stack.Length > 300 ? stack.Substring(0, 300) : stack);
                }
            }
        }

        private static GameObject Selected => EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;

        private static void Submit()
        {
            GameObject go = Selected;
            if (go != null)
            {
                ExecuteEvents.Execute(go, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
            }
        }

        private static void Move(MoveDirection direction)
        {
            GameObject go = Selected;
            if (go != null)
            {
                ExecuteEvents.Execute(go, new AxisEventData(EventSystem.current) { moveDir = direction }, ExecuteEvents.moveHandler);
            }
        }

        private static IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return null;
            }
        }

        private static IEnumerator WaitReal(System.Func<bool> condition, float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < end)
            {
                yield return null;
            }
        }

        private IEnumerator Start()
        {
            // ── 主選單:選真格區域 ──
            yield return WaitReal(() => MainMenuController.Instance != null && MainMenuController.Instance.Navigator != null
                                        && MainMenuController.Instance.Navigator.Current != null, 8f);
            MainMenuController menu = MainMenuController.Instance;
            if (menu == null)
            {
                FailSetup("沒有 MainMenuController");
                yield break;
            }
            yield return new WaitForSecondsRealtime(0.4f);
            Submit();
            yield return Frames(3);
            Submit();
            yield return Frames(3);
            bool onModes = menu.Navigator.Current == menu.Modes;
            Move(MoveDirection.Right);
            yield return Frames(3);
            bool onZones = Selected == menu.Modes.Cards[1].gameObject && !menu.Modes.Cards[1].Locked;
            Submit();
            yield return Frames(3);
            Check("模式頁:真格區域已開放,選了之後進場地頁",
                onModes && onZones && menu.Navigator.Current == menu.Stages && menu.ModeId == MatchModes.SplatZonesId,
                $"onModes={onModes} onZones={onZones} page={menu.Navigator.Current?.GetType().Name} modeId={menu.ModeId}");
            Submit();
            yield return Frames(3);
            Submit();
            yield return Frames(3);
            bool onSetup = menu.Navigator.Current == menu.Setup && Selected == menu.Setup.StartButton.gameObject;
            Submit();
            yield return Frames(1);
            Check("開始 → MatchSettings.Mode = 真格區域", onSetup && MatchSettings.Mode == MatchMode.SplatZones,
                $"onSetup={onSetup} mode={MatchSettings.Mode}");

            // ── 對戰場景 ──
            yield return WaitReal(() => !SceneTransition.Busy && MatchController.Instance != null
                                        && MatchController.Instance.LocalPlayer != null, 40f);
            MatchController match = MatchController.Instance;
            ObjectiveModeController objective = ObjectiveModeController.Instance;
            SplatZonesDriver driver = objective != null ? objective.SplatZones : null;
            if (match == null || driver == null)
            {
                FailSetup($"真格區域對戰沒有開起來(match={(match != null)} driver={(driver != null)})");
                yield break;
            }
            Check("選單開的對戰是真格區域(5:00)", match.Mode == MatchMode.SplatZones
                                               && Mathf.Approximately(match.Timeline.Settings.BattleSeconds, 300f),
                $"mode={match.Mode} battle={match.Timeline.Settings.BattleSeconds}");
            SplatZonesRules rules = driver.ZoneRules;
            rules.ZoneCaptured += (z, t) => _captures++;
            rules.ControlChanged += (n, p) => _controlChanges++;
            rules.PenaltyAdded += (t, n) => _penalties++;
            if (match.LocalPlayer.GetComponent<BotBrain>() == null)
            {
                match.LocalPlayer.gameObject.AddComponent<BotBrain>().Configure(BotBrain.Difficulty.Normal);
            }
            yield return WaitReal(() => match.Phase >= MatchPhase.Battle, 20f);
            match.DebugSetRemaining(BattleSeconds);
            Time.timeScale = TimeScale;
            float realStart = Time.realtimeSinceStartup;
            bool shot = false;
            while (match.Phase == MatchPhase.Battle && Time.realtimeSinceStartup - realStart < 170f)
            {
                if (!shot && match.Timeline.BattleRemaining < BattleSeconds * 0.35f)
                {
                    shot = true;
                    Time.timeScale = 1f;
                    yield return ModeTestKit.ShotZone(driver.Zones[0], "zones_soak_mid", true, null);
                    Time.timeScale = TimeScale;
                }
                yield return null;
            }
            Time.timeScale = 1f;
            bool ended = match.Phase >= MatchPhase.Finish;
            yield return ModeTestKit.WaitPhase(match, MatchPhase.Judging, 10f);
            AnarchyResult result = match.RankedResult;
            string how = result.Knockout ? "KNOCKOUT" : match.Timeline.WentToOvertime ? "延長戰後" : "時間到";
            Debug.Log($"[SOAK] 真格區域 {how}:Alpha {rules.Alpha.Count}(+{rules.Alpha.Penalty}) vs Bravo {rules.Bravo.Count}" +
                      $"(+{rules.Bravo.Penalty}) 勝方={result.Winner} 佔領 {_captures} 次、控制權變化 {_controlChanges} 次、罰分 {_penalties} 次、" +
                      $"bot 目標 區域內 {driver.BotPicksInZone} / 外圍 {driver.BotPicksAround} / 交回 {driver.BotPicksDeferred}、" +
                      $"規則幀 {objective.StepsRun}");

            Check("soak 全程無例外", _exceptions == 0, $"例外={_exceptions}{(_firstException != null ? " 第一個:" + _firstException : "")}");
            Check("一場打完(時間到 / 延長 / KO)並進入判定", ended && match.HasVerdict && match.Phase >= MatchPhase.Judging,
                $"ended={ended} phase={match.Phase} {how}");
            Check("bot 真的在打區域:區域被佔領過、計數有動",
                _captures >= 1 && (rules.Alpha.Count < 100 || rules.Bravo.Count < 100),
                $"佔領 {_captures} 次 計數 Alpha {rules.Alpha.Count} Bravo {rules.Bravo.Count}");
            Check("bot 的塗地目標指向區域(目標建議被採用)", driver.BotPicksInZone >= 8,
                $"區域內 {driver.BotPicksInZone} 外圍 {driver.BotPicksAround} 交回 {driver.BotPicksDeferred}");
            Check("判定走計數:勝方 = 計數較少(或 KO)、Verdict 一致",
                match.Verdict.Winner == result.Winner && result.Winner != Team.None
                && (result.Knockout || result.NoProgress || result.CountOf(result.Winner) < result.CountOf(TeamUtil.Opponent(result.Winner))),
                $"winner={result.Winner} {result.AlphaCount} vs {result.BravoCount} verdict={match.Verdict.Winner}");
            Finish();
        }
    }
}

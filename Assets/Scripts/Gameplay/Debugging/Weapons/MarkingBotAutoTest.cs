using System.Collections;
using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.AI;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Match;
using SplatoonC.Gameplay.Player;
using SplatoonC.Gameplay.UI;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 對戰場景:標記讓 bot 穿牆知道目標(§2.11「被標記 → 穿牆可見」)。
    // 在 Bravo 基地台上放一面牆,本機玩家(Alpha)躲在牆後:沒標記時 Bravo bot 看不到;被 Bravo 標記後
    // bot 立刻取得目標(TargetVisible = false,位置隨標記更新)並進入交戰;標記結束後位置不再更新。
    // 另驗:本機被標記時對戰 HUD 顯示「被標記!」、本機隊伍標記敵人時畫面顯示穿牆輪廓;全程無例外。
    [AutoTest(Scene = AutoTestAttribute.MatchScene, Timeout = 120f, Order = 211)]
    public sealed class MarkingBotAutoTest : AutoTestBase
    {
        private int _errors;
        private string _firstError;

        public static void Run()
        {
            Launch<MarkingBotAutoTest>();
        }

        private void OnEnable()
        {
            Application.logMessageReceived += OnLog;
        }

        private void OnDisable()
        {
            Application.logMessageReceived -= OnLog;
        }

        private void OnLog(string message, string stackTrace, LogType type)
        {
            if ((type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
                && !message.StartsWith("[AUTOTEST]", System.StringComparison.Ordinal))
            {
                _errors++;
                if (_firstError == null)
                {
                    _firstError = message;
                }
            }
        }

        private IEnumerator Start()
        {
            MatchController match = MatchController.Instance;
            if (match == null)
            {
                FailSetup("沒有 MatchController(需在對戰場景)");
                yield break;
            }
            yield return WaitUntil(() => match.LocalPlayer != null && match.Roster.Count >= 2, 10f);
            match.DebugSetRemaining(150f);
            yield return null;

            Inkling player = match.LocalPlayer;
            BotBrain watcher = null;
            for (int i = 0; i < match.Roster.Count; i++)
            {
                Inkling inkling = match.Roster[i];
                if (inkling != null && inkling.Team != player.Team && inkling.GetComponent<BotBrain>() != null)
                {
                    watcher = inkling.GetComponent<BotBrain>();
                    break;
                }
            }
            if (player == null || watcher == null)
            {
                FailSetup("找不到本機玩家或敵隊 bot");
                yield break;
            }
            Inkling watcherInkling = watcher.GetComponent<Inkling>();

            // 所有角色鎖輸入(bot 感知照跑,只是不動),其餘 bot 留在各自基地;玩家無敵
            for (int i = 0; i < match.Roster.Count; i++)
            {
                var router = match.Roster[i] != null ? match.Roster[i].GetComponent<PlayerInputRouter>() : null;
                if (router != null)
                {
                    router.InputLocked = true;
                }
            }
            player.GetComponent<InklingHealth>().Invulnerable = true;

            // Bravo 基地台(y 3,z 71..85):觀察者 z 80 面向 −Z,玩家 z 72.5,中間一面 6×5m 的牆
            var watcherPos = new Vector3(8f, 3.05f, 80f);
            var hidePos = new Vector3(8f, 3.05f, 72.5f);
            watcherInkling.Teleport(watcherPos, 180f);
            player.Teleport(hidePos, 0f);
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "MarkTestWall";
            wall.transform.SetPositionAndRotation(new Vector3(8f, 5.5f, 76.25f), Quaternion.identity);
            wall.transform.localScale = new Vector3(6f, 5f, 0.6f);
            Physics.SyncTransforms();
            yield return null;

            var view = watcher.GetComponent<BotAimView>();
            Vector3 eye = view != null ? view.AimOrigin : watcherInkling.transform.position + Vector3.up * 1.5f;
            bool blocked = Physics.Linecast(eye, player.transform.position + Vector3.up, ~GameLayers.InklingMask,
                QueryTriggerInteraction.Ignore);
            yield return new WaitForSeconds(0.8f);
            Check("前提:牆擋住視線、未標記時 bot 看不到玩家", blocked && watcher.Target != player,
                $"blocked={blocked} target={(watcher.Target != null ? watcher.Target.DisplayName : "無")}");

            // 敵隊(Bravo)標記玩家 → bot 穿牆取得目標
            float markedAt = Time.time;
            Marking.Apply(player, Team.Bravo, Marking.PointSensorSeconds);
            yield return WaitUntil(() => watcher.Target == player, 1f);
            float acquire = Time.time - markedAt;
            Check("被標記後 bot 穿牆取得目標(感知週期 ≤ 0.24s)", watcher.Target == player && !watcher.TargetVisible,
                $"{acquire:0.00}s target={(watcher.Target != null ? watcher.Target.DisplayName : "無")} visible={watcher.TargetVisible}");

            var hud = FindAnyObjectByType<MatchHud>();
            yield return null;
            Check("對戰 HUD 顯示「被標記!」", hud != null && (hud.ShownStatus & StatusFlags.Marked) != 0,
                hud != null ? hud.ShownStatus.ToString() : "沒有 MatchHud");
            var selfIndicator = player.GetComponent<MarkIndicator>();
            Check("被標記者自己的畫面不畫自己的標記", selfIndicator == null || !selfIndicator.Showing,
                $"showing={(selfIndicator != null && selfIndicator.Showing)}");

            // 標記中移動:bot 知道的位置跟著更新(看不到也知道)
            var moved = new Vector3(9.5f, 3.05f, 72.5f);
            player.Teleport(moved, 0f);
            yield return new WaitForSeconds(0.5f);
            float trackError = Flat(watcher.TargetPosition - moved);
            Check("標記中目標位置持續更新(穿牆)", trackError < 0.3f && !watcher.TargetVisible, $"誤差 {trackError:0.00}m");

            // 解鎖觀察者:對被標記的目標進入交戰(去最後已知位置)
            watcher.GetComponent<PlayerInputRouter>().InputLocked = false;
            yield return new WaitForSeconds(0.6f);
            Check("bot 對被標記的敵人進入交戰", watcher.CurrentMode == BotBrain.Mode.Engage, $"mode={watcher.CurrentMode}");
            watcher.GetComponent<PlayerInputRouter>().InputLocked = true;
            watcherInkling.Teleport(watcherPos, 180f);

            // 本機隊伍(Alpha)標記敵人:本機畫面顯示穿牆輪廓與指向線
            Marking.Apply(watcherInkling, Team.Alpha, 2f);
            yield return null;
            yield return null;
            var enemyIndicator = watcherInkling.GetComponent<MarkIndicator>();
            Check("本機隊伍標記的敵人:穿牆輪廓 + 指向線", enemyIndicator != null && enemyIndicator.Showing && enemyIndicator.LineShowing,
                enemyIndicator == null ? "沒有 MarkIndicator" : $"showing={enemyIndicator.Showing} line={enemyIndicator.LineShowing}");

            // 標記結束:位置不再更新
            Marking.Clear(player);
            yield return new WaitForSeconds(0.4f);
            Vector3 lastKnown = watcher.TargetPosition;
            player.Teleport(new Vector3(6.5f, 3.05f, 72.5f), 0f);
            yield return new WaitForSeconds(0.6f);
            float stale = Flat(watcher.TargetPosition - lastKnown);
            Check("標記結束後 bot 不再得知新位置", stale < 0.05f && !watcher.TargetVisible, $"已知位置變動 {stale:0.00}m");
            yield return null;
            Check("標記結束 HUD 收起", hud != null && (hud.ShownStatus & StatusFlags.Marked) == 0,
                hud != null ? hud.ShownStatus.ToString() : "-");

            Check("全程無例外/錯誤 log", _errors == 0, _errors == 0 ? "0" : $"{_errors} 筆,第一筆:{_firstError}");

            Marking.ClearAll();
            Destroy(wall);
            player.GetComponent<InklingHealth>().Invulnerable = false;
            for (int i = 0; i < match.Roster.Count; i++)
            {
                var router = match.Roster[i] != null ? match.Roster[i].GetComponent<PlayerInputRouter>() : null;
                if (router != null)
                {
                    router.InputLocked = false;
                }
            }
            Finish();
        }

        private static float Flat(Vector3 v)
        {
            v.y = 0f;
            return v.magnitude;
        }
    }
}

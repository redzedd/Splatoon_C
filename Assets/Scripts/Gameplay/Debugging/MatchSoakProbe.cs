using System.Collections;
using System.Text;
using SplatoonC.Core.Match;
using SplatoonC.Gameplay.AI;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Match;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 對戰健康探針(不斷言,印統計):以加速時間跑一段對戰,期間攔截例外,結束印每個角色的
    // 武器、塗地點數、擊倒/助攻/陣亡、大招次數與兩隊佔地。本機玩家交給 bot 腦(等於 8 bot 觀戰)。
    // 用法:對戰場景剛進 Play 時 MatchSoakProbe.Run(battleSeconds, timeScale)。
    public sealed class MatchSoakProbe : MonoBehaviour
    {
        private float _battleSeconds = 60f;
        private float _timeScale = 3f;
        private int _exceptions;
        private string _firstException;

        public static void Run()
        {
            Run(60f, 3f);
        }

        public static void Run(float battleSeconds, float timeScale)
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("[PROBE] 需在 Play mode 中執行");
                return;
            }
            var probe = new GameObject("MatchSoakProbe").AddComponent<MatchSoakProbe>();
            probe._battleSeconds = battleSeconds;
            probe._timeScale = Mathf.Clamp(timeScale, 0.5f, 5f);
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

        private IEnumerator Start()
        {
            MatchController match = MatchController.Instance;
            if (match == null)
            {
                Debug.LogError("[PROBE] 前置失敗:沒有 MatchController(需在對戰場景)");
                Destroy(gameObject);
                yield break;
            }
            // 本機玩家也交給 bot 腦:觀戰模式
            while (match.LocalPlayer == null && match.Roster.Count == 0)
            {
                yield return null;
            }
            if (match.LocalPlayer != null && match.LocalPlayer.GetComponent<BotBrain>() == null)
            {
                match.LocalPlayer.gameObject.AddComponent<BotBrain>().Configure(BotBrain.Difficulty.Normal);
            }
            while (match.Phase < MatchPhase.Battle)
            {
                yield return null;
            }
            float remaining = match.Timeline.BattleRemaining;
            if (_battleSeconds < remaining)
            {
                match.DebugSetRemaining(_battleSeconds);
            }
            Time.timeScale = _timeScale;
            while (match.Phase == MatchPhase.Battle)
            {
                yield return null;
            }
            Time.timeScale = 1f;
            while (!match.HasVerdict)
            {
                yield return null;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"[PROBE] SOAK 判定 Alpha {match.Verdict.AlphaPercent:0.0}% vs Bravo {match.Verdict.BravoPercent:0.0}% 勝方={match.Verdict.Winner}");
            foreach (Inkling inkling in match.Roster)
            {
                if (inkling == null)
                {
                    continue;
                }
                var loadout = inkling.GetComponent<WeaponLoadout>();
                string kit = loadout != null && loadout.Kit != null ? loadout.Kit.DisplayName : "?";
                sb.AppendLine($"[PROBE] SOAK {inkling.Team} {inkling.DisplayName,-8} {kit,-10} {TurfPoints.Rounded(inkling.TurfInkedArea),5}p " +
                              $"K{inkling.Splats} A{inkling.Assists} D{inkling.Deaths} SP{inkling.SpecialsUsed}");
            }
            sb.Append($"[PROBE] DONE soak 例外={_exceptions}{(_firstException != null ? " 第一個:" + _firstException : "")}");
            Debug.Log(sb.ToString());
            Destroy(gameObject);
        }
    }
}

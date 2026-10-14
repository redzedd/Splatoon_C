using System.Collections;
using System.IO;
using SplatoonC.Core.Match;
using SplatoonC.Gameplay.Match;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 對戰演出截圖探針(不斷言):開場、Ready/GO、戰鬥 HUD、最後一分鐘、最後倒數、GAME!、判定、結算。
    // 本機玩家由 ScriptedIntent 驅動往前邊走邊射;對戰時間用 DebugSetRemaining 快轉。需在對戰場景剛進 Play 時執行。
    public sealed class MatchShowcaseProbe : MonoBehaviour
    {
        private string _folder = "Builds/Screenshots";

        public static void Run()
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("[PROBE] 需在 Play mode 中執行");
                return;
            }
            new GameObject("MatchShowcaseProbe").AddComponent<MatchShowcaseProbe>();
        }

        private IEnumerator Start()
        {
            MatchController match = MatchController.Instance;
            if (match == null || match.LocalPlayer == null)
            {
                Debug.LogError("[PROBE] 前置失敗:沒有 MatchController 或本機玩家");
                Destroy(gameObject);
                yield break;
            }
            Directory.CreateDirectory(_folder);
            var intent = ScriptedIntent.Drive(match.LocalPlayer);

            yield return WaitPhase(match, MatchPhase.Intro, 0.3f);
            Shot("match_intro");
            yield return WaitPhase(match, MatchPhase.Countdown, 0.5f);
            Shot("match_ready");
            yield return WaitPhase(match, MatchPhase.Battle, 0.25f);
            Shot("match_go");
            intent.Move = new Vector2(0.2f, 1f);
            intent.Attack = true;
            yield return new WaitForSeconds(7f);
            intent.Move = Vector2.zero;
            intent.Look = new Vector2(0.6f, 0f);
            yield return new WaitForSeconds(1.5f);
            intent.Look = Vector2.zero;
            Shot("match_battle");
            match.DebugSetRemaining(61f);
            yield return new WaitForSeconds(1.2f);
            Shot("match_lastminute");
            match.DebugSetRemaining(8.5f);
            yield return new WaitForSeconds(1.3f);
            Shot("match_final10");
            yield return WaitPhase(match, MatchPhase.Finish, 0.5f);
            Shot("match_game");
            yield return WaitPhase(match, MatchPhase.Judging, 2.5f);
            Shot("match_judge_mid");
            yield return new WaitForSeconds(3.5f);
            Shot("match_judge_end");
            yield return WaitPhase(match, MatchPhase.Results, 0.8f);
            Shot("match_results");
            yield return null;
            Debug.Log("[PROBE] DONE 對戰演出截圖完成");
            Destroy(gameObject);
        }

        private static IEnumerator WaitPhase(MatchController match, MatchPhase phase, float extra)
        {
            float timeout = Time.time + 400f;
            while (match.Phase < phase && Time.time < timeout)
            {
                yield return null;
            }
            // 已經過了這個階段(探針啟動太晚)就不再等
            if (match.Phase == phase)
            {
                yield return new WaitForSeconds(extra);
            }
        }

        private void Shot(string name)
        {
            string path = $"{_folder}/{name}.png";
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log($"[PROBE] 截圖 {path}");
        }
    }
}

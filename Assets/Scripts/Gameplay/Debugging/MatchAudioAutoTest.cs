using System.Collections;
using System.Collections.Generic;
using SplatoonC.Core.Match;
using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.Match;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 對戰流程聲音驗收(對戰場景):Ready/GO、對戰曲 → 最後一分鐘曲、倒數滴答、結束哨音、判定三段、結算曲。
    // 以 DebugSetRemaining 快轉對戰時間。
    [AutoTest(Scene = AutoTestAttribute.MatchScene, Timeout = 120f, Order = 51)]
    public sealed class MatchAudioAutoTest : AutoTestBase
    {
        private readonly List<string> _heard = new List<string>();

        public static void Run()
        {
            Launch<MatchAudioAutoTest>();
        }

        private void OnEnable()
        {
            GameAudio.Played += OnPlayed;
        }

        private void OnDisable()
        {
            GameAudio.Played -= OnPlayed;
        }

        private void OnPlayed(string id)
        {
            _heard.Add(id);
        }

        private int Count(string id)
        {
            int n = 0;
            foreach (string heard in _heard)
            {
                if (heard == id)
                {
                    n++;
                }
            }
            return n;
        }

        private IEnumerator Start()
        {
            MatchController match = MatchController.Instance;
            if (match == null || match.GetComponent<MatchAudio>() == null)
            {
                FailSetup("沒有 MatchController / MatchAudio(需在對戰場景)");
                yield break;
            }

            yield return WaitUntil(() => match.Phase >= MatchPhase.Battle, 15f);
            yield return null;
            Check("倒數 Ready? 與 GO!", Count("ready") == 1 && Count("go") == 1, $"ready={Count("ready")} go={Count("go")}");
            Check("開戰播對戰曲", GameAudio.CurrentMusicId == "battle_theme", GameAudio.CurrentMusicId ?? "(無)");

            match.DebugSetRemaining(61f);
            yield return WaitUntil(() => GameAudio.CurrentMusicId == "last_minute", 4f);
            Check("剩 1 分鐘:提示音 + 換最後一分鐘曲", Count("last_minute_alert") == 1 && GameAudio.CurrentMusicId == "last_minute",
                $"alert={Count("last_minute_alert")} music={GameAudio.CurrentMusicId ?? "(無)"}");

            match.DebugSetRemaining(4.5f);
            yield return WaitUntil(() => match.Phase >= MatchPhase.Finish, 8f);
            yield return null;
            int ticks = Count("countdown_tick");
            Check("最後倒數每秒滴答", ticks >= 4 && ticks <= 6, $"滴答 {ticks} 次");
            Check("結束:哨音 + GAME! 且音樂停止", Count("whistle_end") == 1 && Count("game_stinger") == 1,
                $"whistle={Count("whistle_end")} game={Count("game_stinger")}");

            yield return WaitUntil(() => match.Phase >= MatchPhase.Judging, 6f);
            yield return new WaitForSeconds(5.2f);
            bool jingle = Count("victory_jingle") + Count("defeat_jingle") == 1;
            Check("判定:鼓聲 → 揭曉 → 勝/敗小曲", Count("judge_drumroll") == 1 && Count("judge_reveal") == 1 && jingle,
                $"drum={Count("judge_drumroll")} reveal={Count("judge_reveal")} jingle={jingle}");

            yield return WaitUntil(() => match.Phase >= MatchPhase.Results, 6f);
            yield return new WaitForSeconds(0.2f);
            Check("結算曲", GameAudio.CurrentMusicId == "results_theme", GameAudio.CurrentMusicId ?? "(無)");
            Finish();
        }
    }
}

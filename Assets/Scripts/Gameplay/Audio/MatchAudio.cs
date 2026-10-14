using SplatoonC.Core;
using SplatoonC.Core.Match;
using SplatoonC.Gameplay.Match;
using UnityEngine;

namespace SplatoonC.Gameplay.Audio
{
    // 對戰流程的聲音與音樂(掛在 MatchController 同物件,MatchController 自動補上):
    //   倒數 Ready? → GO! 開始對戰曲 → 剩 1 分鐘提示 + 換成剛好 60 秒的最後一分鐘曲(時間被調過就從對應位置起播)
    //   → 最後 10 秒每秒滴答 → 結束哨音 + GAME! → 判定鼓聲、揭曉、勝/敗小曲 → 結算曲。
    // 判定的時間點與 MatchHud 的判定動畫一致(0.6s 起長條、3.4s 分出高下、4.6s 宣告)。
    [DisallowMultipleComponent]
    public sealed class MatchAudio : MonoBehaviour
    {
        private const float DrumrollAt = 0.6f;
        private const float RevealAt = 3.4f;
        private const float VerdictAt = 4.6f;

        private MatchController _match;
        private bool _lastMinuteStarted;
        private int _lastTick = -1;
        private float _judgeStart = -1f;
        private int _judgeStep;

        private void Awake()
        {
            _match = GetComponent<MatchController>();
        }

        private void OnEnable()
        {
            if (_match != null)
            {
                _match.PhaseChanged += OnPhaseChanged;
            }
        }

        private void OnDisable()
        {
            if (_match != null)
            {
                _match.PhaseChanged -= OnPhaseChanged;
            }
        }

        private Team AllyTeam => _match != null && _match.LocalPlayer != null ? _match.LocalPlayer.Team : Team.Alpha;

        private void OnPhaseChanged(MatchPhase phase)
        {
            switch (phase)
            {
                case MatchPhase.Countdown:
                    GameAudio.Play2D("ready");
                    break;
                case MatchPhase.Battle:
                    GameAudio.Play2D("go");
                    // 從最後一分鐘直接開(除錯調時間)時不先播一般對戰曲
                    if (!_lastMinuteStarted)
                    {
                        GameAudio.PlayMusic("battle_theme", 0.2f);
                    }
                    break;
                case MatchPhase.Finish:
                    GameAudio.StopMusic(0.08f);
                    GameAudio.Play2D("whistle_end");
                    // 蠻頹對戰計數歸 0:KNOCKOUT! 專用音效
                    GameAudio.Play2D(_match.EndedByKnockout ? "knockout" : "game_stinger");
                    break;
                case MatchPhase.Judging:
                    _judgeStart = Time.time;
                    _judgeStep = 0;
                    break;
                case MatchPhase.Results:
                    GameAudio.PlayMusic("results_theme", 0.8f);
                    break;
            }
        }

        private void Update()
        {
            if (_match == null || _match.Timeline == null)
            {
                return;
            }
            MatchTimeline timeline = _match.Timeline;
            if (timeline.Phase == MatchPhase.Battle)
            {
                float remaining = timeline.BattleRemaining;
                // 最後一分鐘換曲只有塗地(S3:Now or Never!);蠻頹對戰的 LastMinuteSeconds = 0
                float lastMinute = timeline.Settings.LastMinuteSeconds;
                if (!_lastMinuteStarted && lastMinute > 0f && remaining <= lastMinute && remaining > 0f)
                {
                    _lastMinuteStarted = true;
                    if (remaining > timeline.Settings.FinalCountdownSeconds)
                    {
                        GameAudio.Play2D("last_minute_alert");
                    }
                    // 60 秒整的曲子:剩多少就從 60 - 剩餘 起播,最後一拍對上時間到
                    GameAudio.PlayMusic("last_minute", 0.25f, lastMinute - remaining);
                }
                if (timeline.IsFinalCountdown && remaining > 0f)
                {
                    int second = Mathf.CeilToInt(remaining);
                    if (second != _lastTick)
                    {
                        _lastTick = second;
                        GameAudio.Play2D("countdown_tick", second <= 3 ? 1f : 0.75f);
                    }
                }
                return;
            }

            if (timeline.Phase == MatchPhase.Judging && _judgeStart >= 0f && _match.HasVerdict)
            {
                float t = Time.time - _judgeStart;
                if (_judgeStep == 0 && t >= DrumrollAt)
                {
                    _judgeStep = 1;
                    GameAudio.Play2D("judge_drumroll");
                }
                else if (_judgeStep == 1 && t >= RevealAt)
                {
                    _judgeStep = 2;
                    GameAudio.Play2D("judge_reveal");
                }
                else if (_judgeStep == 2 && t >= VerdictAt)
                {
                    _judgeStep = 3;
                    bool win = _match.Verdict.Winner == AllyTeam;
                    GameAudio.Play2D(win ? "victory_jingle" : "defeat_jingle");
                }
            }
        }
    }
}

using System;

namespace SplatoonC.Core.Match
{
    public enum MatchPhase
    {
        // 場地巡禮 + 模式/場地名
        Intro = 0,
        // 「Ready?」→「GO!」
        Countdown = 1,
        // 對戰計時中(塗地 3:00)
        Battle = 2,
        // 時間到:「GAME!」全員凍結
        Finish = 3,
        // 判定動畫(兩貓揭曉百分比)
        Judging = 4,
        // 結算表(等待玩家確認才離開)
        Results = 5,
        Done = 6,
    }

    [Serializable]
    public struct MatchTimelineSettings
    {
        public float IntroSeconds;
        public float CountdownSeconds;
        public float BattleSeconds;
        public float FinishSeconds;
        public float JudgingSeconds;
        // 最後一分鐘換曲(Now or Never!)的門檻
        public float LastMinuteSeconds;
        // 最後 N 秒放大計時並每秒提示
        public float FinalCountdownSeconds;

        public static MatchTimelineSettings TurfWar => new MatchTimelineSettings
        {
            IntroSeconds = 4f,
            CountdownSeconds = 2.2f,
            BattleSeconds = 180f,
            FinishSeconds = 2.5f,
            JudgingSeconds = 7f,
            LastMinuteSeconds = 60f,
            FinalCountdownSeconds = 10f,
        };

        // 蠻頹對戰(Anarchy):對戰 5:00 + 加時;沒有最後一分鐘換曲(S3 只有塗地換 Now or Never!)。
        // 開場 / 倒數 / GAME! / 判定沿用 baseSettings(場景的設定)。
        public static MatchTimelineSettings Anarchy(MatchTimelineSettings baseSettings)
        {
            MatchTimelineSettings s = baseSettings;
            s.BattleSeconds = 300f;
            s.LastMinuteSeconds = 0f;
            s.FinalCountdownSeconds = 10f;
            return s;
        }
    }

    // 時間到時的加時閘門(蠻頹對戰的模式規則實作):
    // TryStartOvertime 在計時歸零那一刻問一次(true = 對戰繼續、計時停在 0),加時中每次 Tick 問 IsOvertimeOver。
    public interface IOvertimeGate
    {
        bool TryStartOvertime();

        bool IsOvertimeOver();
    }

    // 比賽流程時間軸(純邏輯):Tick 推進、跨階段回報。Results 不會自己結束,由 UI 呼叫 Finish()。
    public sealed class MatchTimeline
    {
        private readonly MatchTimelineSettings _settings;

        public MatchPhase Phase { get; private set; } = MatchPhase.Intro;

        // 目前階段已經過秒數
        public float PhaseElapsed { get; private set; }

        public float BattleRemaining { get; private set; }

        public MatchTimelineSettings Settings => _settings;

        // LastMinuteSeconds ≤ 0 = 沒有最後一分鐘(蠻頹對戰)
        public bool IsLastMinute => Phase == MatchPhase.Battle && _settings.LastMinuteSeconds > 0f
                                                               && BattleRemaining <= _settings.LastMinuteSeconds;

        public bool IsFinalCountdown => Phase == MatchPhase.Battle && BattleRemaining <= _settings.FinalCountdownSeconds;

        // 角色可操作:只有對戰中(Countdown 階段的「GO!」之前全員凍結)
        public bool PlayersCanAct => Phase == MatchPhase.Battle;

        // 加時閘門(null = 塗地:時間到直接結束)
        public IOvertimeGate OvertimeGate { get; set; }

        // 加時中(Phase 仍是 Battle,BattleRemaining 停在 0)
        public bool InOvertime { get; private set; }

        public float OvertimeElapsed { get; private set; }

        // 這場有沒有進過加時(結算顯示用)
        public bool WentToOvertime { get; private set; }

        // 對戰被提前結束(KNOCKOUT)
        public bool EndedEarly { get; private set; }

        public MatchTimeline(MatchTimelineSettings settings)
        {
            _settings = settings;
            BattleRemaining = settings.BattleSeconds;
        }

        // 推進 dt 秒;回傳這次是否進入了新階段(一次 Tick 最多跨一個階段,餘時帶入下一階段)。
        public bool Tick(float dt)
        {
            if (dt <= 0f || Phase == MatchPhase.Results || Phase == MatchPhase.Done)
            {
                return false;
            }
            PhaseElapsed += dt;
            if (Phase == MatchPhase.Battle)
            {
                if (InOvertime)
                {
                    OvertimeElapsed += dt;
                    if (OvertimeGate == null || OvertimeGate.IsOvertimeOver())
                    {
                        InOvertime = false;
                        Enter(MatchPhase.Finish, 0f);
                        return true;
                    }
                    return false;
                }
                BattleRemaining = Math.Max(0f, BattleRemaining - dt);
                if (BattleRemaining <= 0f)
                {
                    if (OvertimeGate != null && OvertimeGate.TryStartOvertime())
                    {
                        InOvertime = true;
                        WentToOvertime = true;
                        OvertimeElapsed = 0f;
                        return false;
                    }
                    Enter(MatchPhase.Finish, 0f);
                    return true;
                }
                return false;
            }
            float duration = DurationOf(Phase);
            if (PhaseElapsed < duration)
            {
                return false;
            }
            float overflow = PhaseElapsed - duration;
            Enter(Phase + 1, overflow);
            if (Phase == MatchPhase.Battle)
            {
                BattleRemaining = Math.Max(0f, _settings.BattleSeconds - overflow);
            }
            return true;
        }

        // 結算畫面確認 → 結束
        public void Finish()
        {
            if (Phase == MatchPhase.Results)
            {
                Enter(MatchPhase.Done, 0f);
            }
        }

        // 對戰中提前結束(蠻頹對戰 KNOCKOUT):立刻進入 GAME!(Finish)
        public bool EndBattle()
        {
            if (Phase != MatchPhase.Battle)
            {
                return false;
            }
            EndedEarly = true;
            InOvertime = false;
            Enter(MatchPhase.Finish, 0f);
            return true;
        }

        // 除錯/測試:直接跳到指定階段(加時狀態一併清掉)
        public void SkipTo(MatchPhase phase, float battleRemaining = -1f)
        {
            InOvertime = false;
            Enter(phase, 0f);
            if (battleRemaining >= 0f)
            {
                BattleRemaining = battleRemaining;
            }
        }

        private void Enter(MatchPhase phase, float elapsed)
        {
            Phase = phase;
            PhaseElapsed = elapsed;
        }

        private float DurationOf(MatchPhase phase)
        {
            switch (phase)
            {
                case MatchPhase.Intro: return _settings.IntroSeconds;
                case MatchPhase.Countdown: return _settings.CountdownSeconds;
                case MatchPhase.Finish: return _settings.FinishSeconds;
                case MatchPhase.Judging: return _settings.JudgingSeconds;
                default: return float.MaxValue;
            }
        }

        // 計時器顯示字串用:m:ss(向上取整秒,與 Splatoon 一樣 0:01 才是最後一秒)
        public static void SplitClock(float seconds, out int minutes, out int secs)
        {
            int total = (int)Math.Ceiling(Math.Max(0f, seconds) - 1e-4f);
            minutes = total / 60;
            secs = total % 60;
        }
    }
}

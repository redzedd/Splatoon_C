using NUnit.Framework;
using SplatoonC.Core;
using SplatoonC.Core.Match;

namespace SplatoonC.Tests
{
    public class TurfVerdictTests
    {
        [Test]
        public void 面積大者勝_顯示小數一位()
        {
            TurfVerdict v = TurfVerdict.Judge(412.34f, 380f, 1000f);
            Assert.AreEqual(Team.Alpha, v.Winner);
            Assert.AreEqual(41.2f, v.AlphaPercent, 1e-4f);
            Assert.AreEqual(38.0f, v.BravoPercent, 1e-4f);
        }

        [Test]
        public void 完全平手_Alpha勝並加零點一()
        {
            TurfVerdict v = TurfVerdict.Judge(300f, 300f, 1000f);
            Assert.AreEqual(Team.Alpha, v.Winner);
            Assert.AreEqual(30.1f, v.AlphaPercent, 1e-4f);
            Assert.AreEqual(30.0f, v.BravoPercent, 1e-4f);
        }

        [Test]
        public void 顯示相同但面積不同_勝方加零點一()
        {
            TurfVerdict v = TurfVerdict.Judge(450.1f, 450.3f, 1000f);
            Assert.AreEqual(Team.Bravo, v.Winner);
            Assert.Greater(v.BravoPercent, v.AlphaPercent);
        }

        [Test]
        public void 無可塗面積不丟例外()
        {
            TurfVerdict v = TurfVerdict.Judge(0f, 0f, 0f);
            Assert.AreEqual(Team.Alpha, v.Winner);
            Assert.AreEqual(0.1f, v.AlphaPercent, 1e-4f);
        }
    }

    public class MatchTimelineTests
    {
        private static MatchTimelineSettings Short => new MatchTimelineSettings
        {
            IntroSeconds = 1f,
            CountdownSeconds = 1f,
            BattleSeconds = 10f,
            FinishSeconds = 1f,
            JudgingSeconds = 2f,
            LastMinuteSeconds = 5f,
            FinalCountdownSeconds = 3f,
        };

        [Test]
        public void 依序走過各階段並停在結算()
        {
            var t = new MatchTimeline(Short);
            Assert.AreEqual(MatchPhase.Intro, t.Phase);
            Assert.IsTrue(t.Tick(1.01f));
            Assert.AreEqual(MatchPhase.Countdown, t.Phase);
            Assert.IsFalse(t.PlayersCanAct);
            Assert.IsTrue(t.Tick(1f));
            Assert.AreEqual(MatchPhase.Battle, t.Phase);
            Assert.IsTrue(t.PlayersCanAct);
            for (int i = 0; i < 100 && t.Phase == MatchPhase.Battle; i++)
            {
                t.Tick(0.2f);
            }
            Assert.AreEqual(MatchPhase.Finish, t.Phase);
            Assert.AreEqual(0f, t.BattleRemaining);
            t.Tick(1.01f);
            Assert.AreEqual(MatchPhase.Judging, t.Phase);
            t.Tick(2.01f);
            Assert.AreEqual(MatchPhase.Results, t.Phase);
            Assert.IsFalse(t.Tick(100f), "結算不自動結束");
            t.Finish();
            Assert.AreEqual(MatchPhase.Done, t.Phase);
        }

        [Test]
        public void 餘時帶入對戰計時()
        {
            var t = new MatchTimeline(Short);
            t.Tick(1f);
            t.Tick(1.5f);
            Assert.AreEqual(MatchPhase.Battle, t.Phase);
            Assert.AreEqual(9.5f, t.BattleRemaining, 1e-4f);
        }

        [Test]
        public void 最後一分鐘與最後倒數旗標()
        {
            var t = new MatchTimeline(Short);
            t.SkipTo(MatchPhase.Battle, 6f);
            Assert.IsFalse(t.IsLastMinute);
            t.Tick(1.5f);
            Assert.IsTrue(t.IsLastMinute);
            Assert.IsFalse(t.IsFinalCountdown);
            t.Tick(2f);
            Assert.IsTrue(t.IsFinalCountdown);
        }

        [Test]
        public void 時鐘向上取整()
        {
            MatchTimeline.SplitClock(179.2f, out int m, out int s);
            Assert.AreEqual(3, m);
            Assert.AreEqual(0, s);
            MatchTimeline.SplitClock(0.3f, out m, out s);
            Assert.AreEqual(0, m);
            Assert.AreEqual(1, s);
            MatchTimeline.SplitClock(0f, out m, out s);
            Assert.AreEqual(0, s);
        }
    }
}

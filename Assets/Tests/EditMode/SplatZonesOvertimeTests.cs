using NUnit.Framework;
using SplatoonC.Core;
using SplatoonC.Core.Match.Ranked;

namespace SplatoonC.Tests
{
    // 真格區域加時(Inkipedia Overtime / jpwiki ガチエリア 延長戦)情境表:
    //   觸發(時間到那一刻,落後方):持有全部區域;或失去控制未滿 10 秒且領先方尚未奪回
    //   結束:落後方反超 / 領先方控制全部區域 / 落後方連續 10 秒沒有控制(9 秒奪回可續)/ 加時滿 5:00
    public class SplatZonesOvertimeTests
    {
        public enum Situation
        {
            // 落後方(Bravo)正持有區域
            LoserHolds,
            // 領先方(Alpha)正持有區域
            LeaderHolds,
            // 落後方 5 秒前失去控制(中立),領先方沒奪回
            LoserLost5sAgo,
            // 落後方 12 秒前失去控制
            LoserLost12sAgo,
            // 落後方 3 秒前被領先方奪走
            LeaderRetook3sAgo,
            // 雙方都沒進度
            NoProgress,
            // 區域中立,落後方從未控制過
            NeutralNeverHeld,
        }

        private static void Steps(SplatZonesRules r, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                r.Step();
            }
        }

        private static void Ink(SplatZonesRules r, float alpha, float bravo)
        {
            r.SetZoneInk(0, alpha, bravo);
        }

        // Alpha 30 領先、Bravo 60 落後,依情境擺好區域狀態
        private static SplatZonesRules Arrange(Situation situation)
        {
            var r = new SplatZonesRules(1);
            if (situation == Situation.NoProgress)
            {
                return r;
            }
            r.DebugSetScore(Team.Alpha, 30, 0);
            r.DebugSetScore(Team.Bravo, 60, 0);
            switch (situation)
            {
                case Situation.LoserHolds:
                    Ink(r, 0f, 1f);
                    Steps(r, 10);
                    break;
                case Situation.LeaderHolds:
                    Ink(r, 1f, 0f);
                    Steps(r, 10);
                    break;
                case Situation.LoserLost5sAgo:
                    Ink(r, 0f, 1f);
                    Steps(r, 10);
                    Ink(r, 0.5f, 0.5f);
                    Steps(r, 300);
                    break;
                case Situation.LoserLost12sAgo:
                    Ink(r, 0f, 1f);
                    Steps(r, 10);
                    Ink(r, 0.5f, 0.5f);
                    Steps(r, 720);
                    break;
                case Situation.LeaderRetook3sAgo:
                    Ink(r, 0f, 1f);
                    Steps(r, 10);
                    Ink(r, 1f, 0f);
                    Steps(r, 180);
                    break;
                case Situation.NeutralNeverHeld:
                    Ink(r, 0.4f, 0.4f);
                    Steps(r, 10);
                    break;
            }
            return r;
        }

        [TestCase(Situation.LoserHolds, true)]
        [TestCase(Situation.LeaderHolds, false)]
        [TestCase(Situation.LoserLost5sAgo, true)]
        [TestCase(Situation.LoserLost12sAgo, false)]
        [TestCase(Situation.LeaderRetook3sAgo, false)]
        [TestCase(Situation.NoProgress, false)]
        [TestCase(Situation.NeutralNeverHeld, false)]
        public void 加時觸發情境表(Situation situation, bool expected)
        {
            SplatZonesRules r = Arrange(situation);
            Assert.AreEqual(expected, r.IsOvertimeTriggered(), situation.ToString());
        }

        [Test]
        public void 無進度_Alpha以99比100獲勝且不加時()
        {
            var r = new SplatZonesRules(1);
            Steps(r, 600);
            Assert.IsFalse(r.IsOvertimeTriggered());
            AnarchyResult result = r.Resolve();
            Assert.AreEqual(Team.Alpha, result.Winner);
            Assert.AreEqual(99, result.AlphaCount);
            Assert.AreEqual(100, result.BravoCount);
            Assert.IsTrue(result.NoProgress);
        }

        [Test]
        public void 失去控制5秒前觸發_寬限從失去那刻起算只剩5秒()
        {
            SplatZonesRules r = Arrange(Situation.LoserLost5sAgo);
            Assert.IsTrue(r.IsOvertimeTriggered());
            r.BeginOvertime();
            Assert.AreEqual(Team.Bravo, r.OvertimeLoser);
            Assert.AreEqual(0.5f, r.OvertimeGrace01, 0.01f);
            Steps(r, 299);
            Assert.IsFalse(r.IsOvertimeOver());
            r.Step();
            Assert.IsTrue(r.IsOvertimeOver(), "落後方連續 10 秒沒有控制");
            Assert.AreEqual(Team.Alpha, r.Resolve().Winner);
            Assert.IsTrue(r.Resolve().WentToOvertime);
        }

        [Test]
        public void 加時結束_落後方反超()
        {
            var r = new SplatZonesRules(1);
            r.DebugSetScore(Team.Alpha, 30, 0);
            r.DebugSetScore(Team.Bravo, 32, 0);
            Ink(r, 0f, 1f);
            Steps(r, 10);
            Assert.IsTrue(r.IsOvertimeTriggered());
            r.BeginOvertime();
            // Bravo 已控制 10 幀:再 26 幀 → 31、再 36 → 30(同分,後到仍落後)、再 36 → 29 反超
            Steps(r, 26);
            Assert.AreEqual(31, r.Bravo.Count);
            Assert.IsFalse(r.IsOvertimeOver());
            Steps(r, 36);
            Assert.AreEqual(30, r.Bravo.Count);
            Assert.IsFalse(r.IsOvertimeOver(), "同分但後到 = 仍落後");
            Steps(r, 36);
            Assert.AreEqual(29, r.Bravo.Count);
            Assert.IsTrue(r.IsOvertimeOver());
            Assert.AreEqual(Team.Bravo, r.Resolve().Winner);
        }

        [Test]
        public void 加時結束_領先方奪回立即結束()
        {
            SplatZonesRules r = Arrange(Situation.LoserHolds);
            r.BeginOvertime();
            Steps(r, 60);
            Assert.IsFalse(r.IsOvertimeOver());
            Ink(r, 1f, 0f);
            r.Step();
            Assert.AreEqual(Team.Alpha, r.Controller);
            Assert.IsTrue(r.IsOvertimeOver());
        }

        [Test]
        public void 加時_中立9秒後落後方奪回可以繼續()
        {
            SplatZonesRules r = Arrange(Situation.LoserHolds);
            r.BeginOvertime();
            Assert.AreEqual(1f, r.OvertimeGrace01, 1e-6f, "落後方持有 = 加時框滿格");
            Ink(r, 0.5f, 0.5f);
            Steps(r, 540);
            Assert.IsFalse(r.IsOvertimeOver(), "中立 9 秒");
            Assert.AreEqual(0.1f, r.OvertimeGrace01, 0.01f);
            Ink(r, 0f, 1f);
            r.Step();
            Assert.AreEqual(Team.Bravo, r.Controller);
            Assert.IsFalse(r.IsOvertimeOver());
            Assert.AreEqual(1f, r.OvertimeGrace01, 1e-6f, "奪回 → 寬限重置");
            Ink(r, 0.5f, 0.5f);
            Steps(r, 599);
            Assert.IsFalse(r.IsOvertimeOver());
            r.Step();
            Assert.IsTrue(r.IsOvertimeOver(), "再次連續 10 秒沒有控制");
        }

        [Test]
        public void 加時上限五分鐘()
        {
            // 落後方一直持有但計數被大量罰分卡住,5:00 內反超不了
            var r = new SplatZonesRules(1);
            r.DebugSetScore(Team.Alpha, 30, 0);
            r.DebugSetScore(Team.Bravo, 60, 1000);
            Ink(r, 0f, 1f);
            Steps(r, 10);
            Assert.IsTrue(r.IsOvertimeTriggered());
            r.BeginOvertime();
            Steps(r, SplatZonesRules.OvertimeCapFrames - 1);
            Assert.IsFalse(r.IsOvertimeOver());
            r.Step();
            Assert.IsTrue(r.IsOvertimeOver());
            Assert.AreEqual(Team.Alpha, r.Resolve().Winner);
        }

        [Test]
        public void 兩區域加時_領先方拿一個要等10秒_拿兩個立即結束()
        {
            var r = new SplatZonesRules(2);
            r.DebugSetScore(Team.Alpha, 30, 0);
            r.DebugSetScore(Team.Bravo, 60, 0);
            r.SetZoneInk(0, 0f, 1f);
            r.SetZoneInk(1, 0f, 1f);
            Steps(r, 10);
            Assert.AreEqual(Team.Bravo, r.Controller);
            Assert.IsTrue(r.IsOvertimeTriggered());
            r.BeginOvertime();
            r.SetZoneInk(0, 1f, 0f);
            r.Step();
            Assert.AreEqual(Team.None, r.Controller);
            Assert.IsFalse(r.IsOvertimeOver(), "只拿一個:不會立刻結束");
            Steps(r, 598);
            Assert.IsFalse(r.IsOvertimeOver());
            r.Step();
            Assert.IsTrue(r.IsOvertimeOver(), "落後方 10 秒沒有完全控制");

            var r2 = new SplatZonesRules(2);
            r2.DebugSetScore(Team.Alpha, 30, 0);
            r2.DebugSetScore(Team.Bravo, 60, 0);
            r2.SetZoneInk(0, 0f, 1f);
            r2.SetZoneInk(1, 0f, 1f);
            Steps(r2, 10);
            r2.BeginOvertime();
            r2.SetZoneInk(0, 1f, 0f);
            r2.SetZoneInk(1, 1f, 0f);
            r2.Step();
            Assert.AreEqual(Team.Alpha, r2.Controller);
            Assert.IsTrue(r2.IsOvertimeOver(), "領先方拿下兩個:立即結束");
        }

        [Test]
        public void 加時中KO立即結束()
        {
            var r = new SplatZonesRules(1);
            r.DebugSetScore(Team.Alpha, 30, 0);
            r.DebugSetScore(Team.Bravo, 31, 0);
            Ink(r, 0f, 1f);
            Steps(r, 10);
            r.BeginOvertime();
            r.DebugSetScore(Team.Bravo, 1, 0);
            Steps(r, 26);
            Assert.AreEqual(Team.Bravo, r.KnockoutTeam);
            Assert.IsTrue(r.IsOvertimeOver());
            AnarchyResult result = r.Resolve();
            Assert.IsTrue(result.Knockout);
            Assert.AreEqual(Team.Bravo, result.Winner);
        }
    }
}

using System.Collections.Generic;
using NUnit.Framework;
using SplatoonC.Core;
using SplatoonC.Core.Match.Ranked;

namespace SplatoonC.Tests
{
    // 真格區域規則(DESIGN_REFERENCE §6.5;research/05 §3.1;jpwiki ガチエリア)
    public class SplatZonesRulesTests
    {
        private static void Steps(SplatZonesRules r, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                r.Step();
            }
        }

        // 某隊把區域塗到 ratio(另一隊 0)並推進 1 步
        private static void PaintAndStep(SplatZonesRules r, int zone, Team team, float ratio)
        {
            r.SetZoneInk(zone, team == Team.Alpha ? ratio : 0f, team == Team.Bravo ? ratio : 0f);
            r.Step();
        }

        // Alpha 佔領並連續控制 counts 個計數(佔領那一步算控制第 1 幀)
        private static void CaptureAndHold(SplatZonesRules r, Team team, int counts)
        {
            PaintAndStep(r, 0, team, 1f);
            Steps(r, counts * SplatZonesRules.FramesPerCount - 1);
        }

        // ───────── 罰分公式 ─────────

        [TestCase(100, 70, 24)]
        [TestCase(100, 50, 39)]
        [TestCase(94, 81, 10)]
        [TestCase(100, 1, 75)]
        [TestCase(100, 100, 1)]
        [TestCase(80, 80, 0)]
        [TestCase(60, 40, 15)]
        public void 罰分公式_文件範例(int start, int end, int expected)
        {
            Assert.AreEqual(expected, SplatZonesRules.PenaltyFor(start, end));
        }

        // ───────── 佔領 / 中立門檻 ─────────

        [Test]
        public void 佔領門檻七成_未滿不佔領()
        {
            var r = new SplatZonesRules(1);
            PaintAndStep(r, 0, Team.Alpha, 0.69f);
            Assert.AreEqual(Team.None, r.OwnerOf(0));
            Assert.AreEqual(Team.None, r.Controller);
        }

        [Test]
        public void 佔領門檻七成_達到即佔領並瞬間重塗()
        {
            var r = new SplatZonesRules(1);
            var captured = new List<Team>();
            r.ZoneCaptured += (z, t) => captured.Add(t);
            PaintAndStep(r, 0, Team.Bravo, 0.70f);
            Assert.AreEqual(Team.Bravo, r.OwnerOf(0));
            Assert.AreEqual(Team.Bravo, r.Controller);
            Assert.AreEqual(1f, r.InkOf(0, Team.Bravo), 1e-6f, "佔領瞬間整個區域變成佔領方的墨");
            Assert.AreEqual(0f, r.InkOf(0, Team.Alpha), 1e-6f);
            CollectionAssert.AreEqual(new[] { Team.Bravo }, captured);
        }

        [Test]
        public void 中立門檻五成_未滿維持佔領()
        {
            var r = new SplatZonesRules(1);
            PaintAndStep(r, 0, Team.Alpha, 1f);
            r.SetZoneInk(0, 0.51f, 0.49f);
            r.Step();
            Assert.AreEqual(Team.Alpha, r.OwnerOf(0));
        }

        [Test]
        public void 中立門檻五成_敵墨達五成變中立且計數停止()
        {
            var r = new SplatZonesRules(1);
            var neutralized = new List<Team>();
            r.ZoneNeutralized += (z, former) => neutralized.Add(former);
            CaptureAndHold(r, Team.Alpha, 3);
            Assert.AreEqual(97, r.Alpha.Count);
            r.SetZoneInk(0, 0.5f, 0.5f);
            r.Step();
            Assert.AreEqual(Team.None, r.OwnerOf(0));
            Assert.AreEqual(Team.None, r.Controller);
            CollectionAssert.AreEqual(new[] { Team.Alpha }, neutralized);
            Steps(r, 36 * 5);
            Assert.AreEqual(97, r.Alpha.Count, "中立時計數停止");
            Assert.AreEqual(0, r.Alpha.Penalty, "變中立不給罰分");
        }

        [Test]
        public void 中立後需重新塗到七成才佔領()
        {
            var r = new SplatZonesRules(1);
            PaintAndStep(r, 0, Team.Alpha, 1f);
            r.SetZoneInk(0, 0.45f, 0.55f);
            r.Step();
            Assert.AreEqual(Team.None, r.OwnerOf(0));
            r.SetZoneInk(0, 0.35f, 0.65f);
            r.Step();
            Assert.AreEqual(Team.None, r.OwnerOf(0), "65% 不夠");
            r.SetZoneInk(0, 0.3f, 0.7f);
            r.Step();
            Assert.AreEqual(Team.Bravo, r.OwnerOf(0));
        }

        // ───────── 計數 ─────────

        [Test]
        public void 每36幀計數減一()
        {
            var r = new SplatZonesRules(1);
            PaintAndStep(r, 0, Team.Alpha, 1f);
            Steps(r, 34);
            Assert.AreEqual(100, r.Alpha.Count, "控制 35 幀還沒減");
            r.Step();
            Assert.AreEqual(99, r.Alpha.Count, "第 36 幀減 1");
            Steps(r, 35);
            Assert.AreEqual(99, r.Alpha.Count);
            r.Step();
            Assert.AreEqual(98, r.Alpha.Count);
            Assert.AreEqual(100, r.Bravo.Count);
        }

        [Test]
        public void 連續控制60秒_KNOCKOUT()
        {
            var r = new SplatZonesRules(1);
            Team ko = Team.None;
            r.KnockedOut += t => ko = t;
            PaintAndStep(r, 0, Team.Bravo, 1f);
            Steps(r, 3600 - 2);
            Assert.AreEqual(1, r.Bravo.Count);
            Assert.AreEqual(Team.None, r.KnockoutTeam);
            r.Step();
            Assert.AreEqual(0, r.Bravo.Count, "100 計數 × 36F = 3600F = 60 秒");
            Assert.AreEqual(Team.Bravo, r.KnockoutTeam);
            Assert.AreEqual(Team.Bravo, ko);
            int frame = r.Frame;
            Steps(r, 10);
            Assert.AreEqual(frame, r.Frame, "KO 後規則停止");
            AnarchyResult result = r.Resolve();
            Assert.IsTrue(result.Knockout);
            Assert.AreEqual(Team.Bravo, result.Winner);
        }

        [Test]
        public void 計數進度跨控制權保留()
        {
            var r = new SplatZonesRules(1);
            PaintAndStep(r, 0, Team.Alpha, 1f);
            Steps(r, 19);
            Assert.AreEqual(20, r.TickProgress(Team.Alpha));
            r.SetZoneInk(0, 0.5f, 0.5f);
            r.Step();
            Steps(r, 100);
            r.SetZoneInk(0, 1f, 0f);
            r.Step();
            Assert.AreEqual(21, r.TickProgress(Team.Alpha), "中立期間進度凍結,重新控制從原進度續算");
            Steps(r, 14);
            Assert.AreEqual(100, r.Alpha.Count);
            r.Step();
            Assert.AreEqual(99, r.Alpha.Count, "累計控制滿 36 幀才減");
        }

        // ───────── 罰分 ─────────

        [Test]
        public void 被敵方奪走才給罰分_100到70加24()
        {
            var r = new SplatZonesRules(1);
            var penalties = new List<int>();
            r.PenaltyAdded += (team, amount) =>
            {
                Assert.AreEqual(Team.Alpha, team);
                penalties.Add(amount);
            };
            CaptureAndHold(r, Team.Alpha, 30);
            Assert.AreEqual(70, r.Alpha.Count);
            r.SetZoneInk(0, 0.5f, 0.5f);
            r.Step();
            Assert.AreEqual(0, r.Alpha.Penalty, "變中立不罰");
            r.SetZoneInk(0, 0.3f, 0.7f);
            r.Step();
            Assert.AreEqual(Team.Bravo, r.Controller);
            Assert.AreEqual(24, r.Alpha.Penalty);
            CollectionAssert.AreEqual(new[] { 24 }, penalties);
        }

        [Test]
        public void 罰分_100到50加39()
        {
            var r = new SplatZonesRules(1);
            CaptureAndHold(r, Team.Alpha, 50);
            PaintAndStep(r, 0, Team.Bravo, 1f);
            Assert.AreEqual(50, r.Alpha.Count);
            Assert.AreEqual(39, r.Alpha.Penalty);
        }

        [Test]
        public void 罰分_94到81加10()
        {
            var r = new SplatZonesRules(1);
            r.DebugSetScore(Team.Alpha, 94, 0);
            CaptureAndHold(r, Team.Alpha, 13);
            Assert.AreEqual(81, r.Alpha.Count);
            PaintAndStep(r, 0, Team.Bravo, 1f);
            Assert.AreEqual(10, r.Alpha.Penalty);
        }

        [Test]
        public void 自己奪回不給罰分()
        {
            var r = new SplatZonesRules(1);
            CaptureAndHold(r, Team.Alpha, 10);
            r.SetZoneInk(0, 0.4f, 0.6f);
            r.Step();
            PaintAndStep(r, 0, Team.Alpha, 1f);
            Assert.AreEqual(Team.Alpha, r.Controller);
            Assert.AreEqual(0, r.Alpha.Penalty);
        }

        [Test]
        public void 重新控制先扣罰分_最後一點與本計數同時減()
        {
            // jpwiki:40+2 → 40+1 → 39 → 38
            var r = new SplatZonesRules(1);
            r.DebugSetScore(Team.Alpha, 40, 2);
            PaintAndStep(r, 0, Team.Alpha, 1f);
            Steps(r, 35);
            Assert.AreEqual(40, r.Alpha.Count);
            Assert.AreEqual(1, r.Alpha.Penalty);
            Steps(r, 36);
            Assert.AreEqual(39, r.Alpha.Count);
            Assert.AreEqual(0, r.Alpha.Penalty);
            Steps(r, 36);
            Assert.AreEqual(38, r.Alpha.Count);
        }

        [Test]
        public void 罰分計算用含罰分的計數()
        {
            // 帶著罰分重新控制:start = 40 + 20 = 60;控制 20 個計數週期(前 19 次只扣罰分,第 20 次罰分歸 0 同時本計數 −1)
            // 後被奪 → end = 39 + 0
            var r = new SplatZonesRules(1);
            r.DebugSetScore(Team.Alpha, 40, 20);
            CaptureAndHold(r, Team.Alpha, 20);
            Assert.AreEqual(39, r.Alpha.Count);
            Assert.AreEqual(0, r.Alpha.Penalty);
            PaintAndStep(r, 0, Team.Bravo, 1f);
            Assert.AreEqual(SplatZonesRules.PenaltyFor(60, 39), r.Alpha.Penalty);
        }

        [Test]
        public void 罰分不參與勝負比較()
        {
            var r = new SplatZonesRules(1);
            r.DebugSetScore(Team.Alpha, 30, 50);
            r.DebugSetScore(Team.Bravo, 40, 0);
            Assert.AreEqual(Team.Alpha, AnarchyRules.Leader(r.Alpha, r.Bravo));
        }

        // ───────── 平手迴避 ─────────

        [Test]
        public void 平手迴避_後追平者失去控制時計數加一()
        {
            var r = new SplatZonesRules(1);
            r.DebugSetScore(Team.Alpha, 50, 0);
            r.DebugSetScore(Team.Bravo, 51, 0);
            CaptureAndHold(r, Team.Bravo, 1);
            Assert.AreEqual(50, r.Bravo.Count);
            Assert.AreEqual(Team.Alpha, AnarchyRules.Leader(r.Alpha, r.Bravo), "同分時先到者領先");
            r.SetZoneInk(0, 0.5f, 0.5f);
            r.Step();
            Assert.AreEqual(51, r.Bravo.Count, "後追平的一方失去控制 → +1(加在計數,不加罰分)");
            Assert.AreEqual(0, r.Bravo.Penalty);
            Assert.AreEqual(Team.Alpha, AnarchyRules.Leader(r.Alpha, r.Bravo));
        }

        [Test]
        public void 平手迴避_先到者失去控制不加()
        {
            var r = new SplatZonesRules(1);
            r.DebugSetScore(Team.Bravo, 50, 0);
            Steps(r, 5);
            r.DebugSetScore(Team.Alpha, 50, 0);
            // Bravo 先到 50;Bravo 控制後失去 → 不加
            PaintAndStep(r, 0, Team.Bravo, 1f);
            r.SetZoneInk(0, 0.5f, 0.5f);
            r.Step();
            Assert.AreEqual(50, r.Bravo.Count);
        }

        // ───────── 被動大招充能(§6.4)─────────

        [Test]
        public void 被動充能_開場平手無人充能()
        {
            var r = new SplatZonesRules(1);
            r.Step();
            Assert.AreEqual(0f, r.PassiveChargeRate(Team.Alpha));
            Assert.AreEqual(0f, r.PassiveChargeRate(Team.Bravo));
        }

        [Test]
        public void 被動充能_敵方控制全部區域四點五()
        {
            var r = new SplatZonesRules(1);
            r.DebugSetScore(Team.Bravo, 20, 0);
            PaintAndStep(r, 0, Team.Alpha, 1f);
            Assert.AreEqual(4.5f, r.PassiveChargeRate(Team.Bravo), 1e-6f, "不論領先落後");
            Assert.AreEqual(0f, r.PassiveChargeRate(Team.Alpha));
        }

        [Test]
        public void 被動充能_無人控制且落後一點五()
        {
            var r = new SplatZonesRules(1);
            r.DebugSetScore(Team.Alpha, 90, 0);
            r.DebugSetScore(Team.Bravo, 80, 0);
            r.Step();
            Assert.AreEqual(1.5f, r.PassiveChargeRate(Team.Alpha), 1e-6f);
            Assert.AreEqual(0f, r.PassiveChargeRate(Team.Bravo));
        }

        [Test]
        public void 被動充能_我方控制時不充()
        {
            var r = new SplatZonesRules(1);
            r.DebugSetScore(Team.Alpha, 90, 0);
            r.DebugSetScore(Team.Bravo, 80, 0);
            PaintAndStep(r, 0, Team.Alpha, 1f);
            Assert.AreEqual(0f, r.PassiveChargeRate(Team.Alpha), "落後但自己控制中");
            Assert.AreEqual(4.5f, r.PassiveChargeRate(Team.Bravo), 1e-6f);
        }

        // ───────── 兩個區域 ─────────

        [Test]
        public void 兩區域_要兩個都佔才計數_任一中立就停()
        {
            var r = new SplatZonesRules(2);
            r.SetZoneInk(0, 1f, 0f);
            r.SetZoneInk(1, 0.2f, 0f);
            Steps(r, 72);
            Assert.AreEqual(Team.Alpha, r.OwnerOf(0));
            Assert.AreEqual(Team.None, r.Controller);
            Assert.AreEqual(100, r.Alpha.Count, "只佔一個不計數");
            r.SetZoneInk(1, 0.8f, 0f);
            Steps(r, 36);
            Assert.AreEqual(Team.Alpha, r.Controller);
            Assert.AreEqual(99, r.Alpha.Count);
            r.SetZoneInk(1, 0.5f, 0.5f);
            Steps(r, 72);
            Assert.AreEqual(Team.None, r.Controller);
            Assert.AreEqual(99, r.Alpha.Count, "任一個被中立就停止");
        }

        [Test]
        public void 兩區域_敵方拿下兩個才給罰分()
        {
            var r = new SplatZonesRules(2);
            r.SetZoneInk(0, 1f, 0f);
            r.SetZoneInk(1, 1f, 0f);
            Steps(r, 36 * 10);
            Assert.AreEqual(90, r.Alpha.Count);
            r.SetZoneInk(0, 0f, 1f);
            r.Step();
            Assert.AreEqual(Team.Bravo, r.OwnerOf(0));
            Assert.AreEqual(0, r.Alpha.Penalty, "Bravo 只拿一個:Alpha 失去控制但還沒被奪走全部");
            r.SetZoneInk(1, 0f, 1f);
            r.Step();
            Assert.AreEqual(Team.Bravo, r.Controller);
            Assert.AreEqual(SplatZonesRules.PenaltyFor(100, 90), r.Alpha.Penalty);
        }
    }
}

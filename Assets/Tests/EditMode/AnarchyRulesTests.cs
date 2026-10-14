using NUnit.Framework;
using SplatoonC.Core;
using SplatoonC.Core.Match;
using SplatoonC.Core.Match.Ranked;
using UnityEngine;

namespace SplatoonC.Tests
{
    // 蠻頹對戰共通規則(DESIGN_REFERENCE §6.4 / §6.9)
    public class AnarchyRulesTests
    {
        private static TeamScore Score(Team team, int count, int reachedFrame, int penalty = 0)
        {
            var s = new TeamScore(team);
            s.Set(count, penalty, reachedFrame);
            return s;
        }

        [Test]
        public void 計數低者領先()
        {
            Assert.AreEqual(Team.Bravo, AnarchyRules.Leader(Score(Team.Alpha, 40, 0), Score(Team.Bravo, 39, 50)));
            Assert.AreEqual(Team.Alpha, AnarchyRules.Leader(Score(Team.Alpha, 10, 90), Score(Team.Bravo, 11, 5)));
        }

        [Test]
        public void 同分時先達到者領先()
        {
            Assert.AreEqual(Team.Bravo, AnarchyRules.Leader(Score(Team.Alpha, 25, 300), Score(Team.Bravo, 25, 120)));
            Assert.IsTrue(AnarchyRules.IsBehind(Team.Alpha, Score(Team.Alpha, 25, 300), Score(Team.Bravo, 25, 120)));
        }

        [Test]
        public void 開場完全相同_沒有人落後()
        {
            var a = new TeamScore(Team.Alpha);
            var b = new TeamScore(Team.Bravo);
            Assert.AreEqual(Team.None, AnarchyRules.Leader(a, b));
            Assert.IsFalse(AnarchyRules.IsBehind(Team.Alpha, a, b));
            Assert.IsFalse(AnarchyRules.IsBehind(Team.Bravo, a, b));
        }

        [Test]
        public void 結果_KO優先()
        {
            AnarchyResult r = AnarchyRules.Resolve(Score(Team.Alpha, 50, 10), Score(Team.Bravo, 0, 900), Team.Bravo, false);
            Assert.IsTrue(r.Knockout);
            Assert.AreEqual(Team.Bravo, r.Winner);
            Assert.AreEqual(100, r.ProgressOf(Team.Bravo));
            Assert.AreEqual(50, r.ProgressOf(Team.Alpha));
        }

        [Test]
        public void 結果_無進度Alpha一比零()
        {
            AnarchyResult r = AnarchyRules.Resolve(new TeamScore(Team.Alpha), new TeamScore(Team.Bravo), Team.None, false);
            Assert.AreEqual(Team.Alpha, r.Winner);
            Assert.AreEqual(99, r.AlphaCount);
            Assert.AreEqual(100, r.BravoCount);
            Assert.IsTrue(r.NoProgress);
        }

        [Test]
        public void 結果_同分顯示後到者加一()
        {
            AnarchyResult r = AnarchyRules.Resolve(Score(Team.Alpha, 20, 500), Score(Team.Bravo, 20, 100), Team.None, true);
            Assert.AreEqual(Team.Bravo, r.Winner);
            Assert.AreEqual(21, r.AlphaCount);
            Assert.AreEqual(20, r.BravoCount);
            Assert.IsTrue(r.WentToOvertime);
        }

        [Test]
        public void 罰分不影響比較()
        {
            Assert.AreEqual(Team.Alpha, AnarchyRules.Leader(Score(Team.Alpha, 30, 0, 40), Score(Team.Bravo, 31, 0)));
        }

        [Test]
        public void 常數_5分鐘()
        {
            Assert.AreEqual(300f, AnarchyRules.RegulationSeconds);
            Assert.AreEqual(18000, AnarchyRules.RegulationFrames);
            Assert.AreEqual(100, TeamScore.StartCount);
        }
    }

    public class MatchModeTests
    {
        [Test]
        public void 模式id對照往返()
        {
            foreach (MatchMode mode in System.Enum.GetValues(typeof(MatchMode)))
            {
                Assert.AreEqual(mode, MatchModes.FromId(MatchModes.IdOf(mode)));
            }
            Assert.AreEqual("splat_zones", MatchModes.IdOf(MatchMode.SplatZones));
            Assert.AreEqual(MatchMode.TurfWar, MatchModes.FromId("???"));
            Assert.AreEqual(MatchMode.TurfWar, MatchModes.FromId(null));
        }

        [Test]
        public void 只有塗地不是蠻頹()
        {
            Assert.IsFalse(MatchModes.IsRanked(MatchMode.TurfWar));
            Assert.IsTrue(MatchModes.IsRanked(MatchMode.SplatZones));
            Assert.IsTrue(MatchModes.IsRanked(MatchMode.ClamBlitz));
        }
    }

    public class ZonePolygonTests
    {
        [Test]
        public void 切角矩形_面積與包含()
        {
            Vector2[] poly = ZonePolygon.ChamferedBox(Vector2.zero, new Vector2(19f, 14f), 2.5f);
            Assert.AreEqual(8, poly.Length);
            Assert.AreEqual(19f * 14f - 4f * 2.5f * 2.5f * 0.5f, ZonePolygon.Area(poly), 1e-3f);
            Assert.IsTrue(ZonePolygon.Contains(poly, 0f, 0f));
            Assert.IsTrue(ZonePolygon.Contains(poly, 9.3f, 0f));
            Assert.IsTrue(ZonePolygon.Contains(poly, 0f, -6.9f));
            Assert.IsFalse(ZonePolygon.Contains(poly, 9.3f, 6.8f), "切掉的角");
            Assert.IsFalse(ZonePolygon.Contains(poly, 10f, 0f));
            Vector2 c = ZonePolygon.Centroid(poly);
            Assert.AreEqual(0f, c.x, 1e-4f);
            Assert.AreEqual(0f, c.y, 1e-4f);
        }

        [Test]
        public void 凹多邊形包含()
        {
            // L 形
            Vector2[] poly =
            {
                new Vector2(0f, 0f), new Vector2(4f, 0f), new Vector2(4f, 1f), new Vector2(1f, 1f), new Vector2(1f, 4f),
                new Vector2(0f, 4f),
            };
            Assert.IsTrue(ZonePolygon.Contains(poly, 0.5f, 3f));
            Assert.IsTrue(ZonePolygon.Contains(poly, 3f, 0.5f));
            Assert.IsFalse(ZonePolygon.Contains(poly, 3f, 3f));
            Assert.AreEqual(7f, ZonePolygon.Area(poly), 1e-4f);
        }

        [Test]
        public void 旋轉矩形與點對稱()
        {
            Vector2[] box = ZonePolygon.Box(new Vector2(10f, 5f), new Vector2(4f, 2f), 90f);
            Assert.AreEqual(8f, ZonePolygon.Area(box), 1e-4f);
            // 轉 90°:長邊改沿 z
            Assert.IsTrue(ZonePolygon.Contains(box, 10f, 6.8f));
            Assert.IsFalse(ZonePolygon.Contains(box, 11.5f, 5f));
            Vector2[] mirrored = ZonePolygon.PointMirror(box);
            Assert.IsTrue(ZonePolygon.Contains(mirrored, -10f, -6.8f));
        }

        [Test]
        public void 到邊界距離()
        {
            Vector2[] box = ZonePolygon.Box(Vector2.zero, new Vector2(10f, 10f), 0f);
            Assert.AreEqual(5f, ZonePolygon.DistanceToEdge(box, 0f, 0f), 1e-4f);
            Assert.AreEqual(3f, ZonePolygon.DistanceToEdge(box, 8f, 0f), 1e-4f);
            Assert.AreEqual(40f, ZonePolygon.Perimeter(box), 1e-4f);
        }
    }

    public class MatchTimelineOvertimeTests
    {
        private sealed class Gate : IOvertimeGate
        {
            public bool Start;
            public bool Over;
            public int Asked;

            public bool TryStartOvertime()
            {
                Asked++;
                return Start;
            }

            public bool IsOvertimeOver()
            {
                return Over;
            }
        }

        private static MatchTimeline AtBattle(MatchTimelineSettings settings, IOvertimeGate gate)
        {
            var t = new MatchTimeline(settings) { OvertimeGate = gate };
            t.SkipTo(MatchPhase.Battle, 1f);
            return t;
        }

        private static MatchTimelineSettings Anarchy => MatchTimelineSettings.Anarchy(MatchTimelineSettings.TurfWar);

        [Test]
        public void 蠻頹時間軸_五分鐘且沒有最後一分鐘()
        {
            MatchTimelineSettings s = Anarchy;
            Assert.AreEqual(300f, s.BattleSeconds);
            Assert.AreEqual(4f, s.IntroSeconds, "開場沿用場景設定");
            var t = new MatchTimeline(s);
            t.SkipTo(MatchPhase.Battle, 30f);
            Assert.IsFalse(t.IsLastMinute, "蠻頹對戰不換最後一分鐘曲");
            Assert.IsFalse(t.IsFinalCountdown);
            t.SkipTo(MatchPhase.Battle, 9f);
            Assert.IsTrue(t.IsFinalCountdown);
        }

        [Test]
        public void 塗地時間軸最後一分鐘不變()
        {
            var t = new MatchTimeline(MatchTimelineSettings.TurfWar);
            t.SkipTo(MatchPhase.Battle, 59f);
            Assert.IsTrue(t.IsLastMinute);
        }

        [Test]
        public void 閘門不加時_時間到直接結束()
        {
            var gate = new Gate();
            MatchTimeline t = AtBattle(Anarchy, gate);
            Assert.IsTrue(t.Tick(1.1f));
            Assert.AreEqual(MatchPhase.Finish, t.Phase);
            Assert.AreEqual(1, gate.Asked);
            Assert.IsFalse(t.WentToOvertime);
        }

        [Test]
        public void 加時_對戰持續到閘門說結束()
        {
            var gate = new Gate { Start = true };
            MatchTimeline t = AtBattle(Anarchy, gate);
            Assert.IsFalse(t.Tick(1.1f));
            Assert.AreEqual(MatchPhase.Battle, t.Phase);
            Assert.IsTrue(t.InOvertime);
            Assert.IsTrue(t.PlayersCanAct);
            Assert.AreEqual(0f, t.BattleRemaining);
            for (int i = 0; i < 100; i++)
            {
                t.Tick(0.1f);
            }
            Assert.AreEqual(MatchPhase.Battle, t.Phase);
            Assert.AreEqual(10f, t.OvertimeElapsed, 1e-3f);
            Assert.AreEqual(1, gate.Asked, "只在時間到那一刻問一次");
            gate.Over = true;
            Assert.IsTrue(t.Tick(0.1f));
            Assert.AreEqual(MatchPhase.Finish, t.Phase);
            Assert.IsFalse(t.InOvertime);
            Assert.IsTrue(t.WentToOvertime);
        }

        [Test]
        public void KO提前結束()
        {
            MatchTimeline t = AtBattle(Anarchy, new Gate());
            t.SkipTo(MatchPhase.Battle, 120f);
            Assert.IsTrue(t.EndBattle());
            Assert.AreEqual(MatchPhase.Finish, t.Phase);
            Assert.IsTrue(t.EndedEarly);
            Assert.IsFalse(t.EndBattle(), "不在對戰中不能再結束");
        }

        [Test]
        public void 沒有閘門的塗地流程不變()
        {
            var t = new MatchTimeline(MatchTimelineSettings.TurfWar);
            t.SkipTo(MatchPhase.Battle, 0.5f);
            Assert.IsTrue(t.Tick(0.6f));
            Assert.AreEqual(MatchPhase.Finish, t.Phase);
            Assert.IsFalse(t.InOvertime);
        }
    }
}

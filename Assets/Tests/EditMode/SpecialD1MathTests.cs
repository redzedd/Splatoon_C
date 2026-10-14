using NUnit.Framework;
using SplatoonC.Core.Combat;
using UnityEngine;

namespace SplatoonC.Tests
{
    // D1 大招(Trizooka / Big Bubbler / Killer Wail 5.1 / Wave Breaker)的純邏輯:S3 曲線、屏障幾何、時間表、彈道
    public class HermiteCurveTests
    {
        [Test]
        public void 通過每個鍵值且兩端夾住()
        {
            HermiteCurve curve = HermiteCurve.BubblerRadiusRatio;
            Assert.AreEqual(0f, curve.Evaluate(0f), 1e-5f, "低於第一鍵 = 第一鍵的值(最小半徑)");
            Assert.AreEqual(0f, curve.Evaluate(0.1940299f), 1e-5f);
            Assert.AreEqual(0.3726415f, curve.Evaluate(0.6522388f), 1e-5f);
            Assert.AreEqual(0.75f, curve.Evaluate(0.8723881f), 1e-5f);
            Assert.AreEqual(1f, curve.Evaluate(1f), 1e-5f, "滿血 = 最大半徑");
            Assert.AreEqual(1f, curve.Evaluate(2f), 1e-5f);
        }

        [Test]
        public void 兩鍵之間是三次Hermite()
        {
            var curve = new HermiteCurve(0f, 0f, 0f, 1f, 1f, 0f);
            // 斜率 0 的兩端:smoothstep
            Assert.AreEqual(0.5f, curve.Evaluate(0.5f), 1e-5f);
            Assert.AreEqual(0.15625f, curve.Evaluate(0.25f), 1e-5f);
            var linear = new HermiteCurve(0f, 0f, 1f, 2f, 2f, 1f);
            Assert.AreEqual(1.5f, linear.Evaluate(1.5f), 1e-5f, "斜率 1 的直線");
        }

        [Test]
        public void 無人機上升曲線單調遞增()
        {
            float previous = -1f;
            for (int i = 0; i <= 30; i++)
            {
                float y = HermiteCurve.BubblerAscend.Evaluate(i / 30f);
                Assert.GreaterOrEqual(y, previous - 1e-4f);
                previous = y;
            }
            Assert.AreEqual(1f, previous, 1e-5f);
        }
    }

    public class SphereBarrierMathTests
    {
        private static readonly Vector3 Center = new Vector3(0f, 1f, 0f);
        private const float Radius = 7.5f;

        [Test]
        public void 從外面打進來停在球面()
        {
            Vector3 from = new Vector3(0f, 1f, 20f);
            Assert.IsTrue(SphereBarrierMath.Entry(Center, Radius, from, Vector3.back, 30f, 0f,
                out float distance, out Vector3 point, out Vector3 normal));
            Assert.AreEqual(12.5f, distance, 1e-4f);
            Assert.AreEqual(new Vector3(0f, 1f, 7.5f).z, point.z, 1e-4f);
            Assert.AreEqual(1f, normal.z, 1e-4f, "朝外法線");
        }

        [Test]
        public void 投射半徑讓接觸提早()
        {
            Vector3 from = new Vector3(0f, 1f, 20f);
            SphereBarrierMath.Entry(Center, Radius, from, Vector3.back, 30f, 0.3f, out float distance, out _, out _);
            Assert.AreEqual(12.2f, distance, 1e-4f);
        }

        [Test]
        public void 罩內往外打不擋()
        {
            Assert.IsFalse(SphereBarrierMath.Entry(Center, Radius, new Vector3(0f, 1f, 3f), Vector3.forward, 30f, 0f,
                out _, out _, out _));
            Assert.IsFalse(SphereBarrierMath.Entry(Center, Radius, new Vector3(0f, 1f, 3f), Vector3.back, 30f, 0f,
                out _, out _, out _), "罩內往任何方向都不擋");
        }

        [Test]
        public void 射程不到或偏開不擋()
        {
            Vector3 from = new Vector3(0f, 1f, 20f);
            Assert.IsFalse(SphereBarrierMath.Entry(Center, Radius, from, Vector3.back, 10f, 0f, out _, out _, out _),
                "12.5 m 才碰到,這一步只走 10 m");
            Assert.IsFalse(SphereBarrierMath.Entry(Center, Radius, new Vector3(9f, 1f, 20f), Vector3.back, 30f, 0f,
                out _, out _, out _), "側偏 9 m 擦過");
            Assert.IsFalse(SphereBarrierMath.Entry(Center, Radius, from, Vector3.forward, 30f, 0f, out _, out _, out _),
                "背向屏障");
        }

        [Test]
        public void 貼著外殼往外走不擋()
        {
            Vector3 from = new Vector3(0f, 1f, 7.6f);
            Assert.IsFalse(SphereBarrierMath.Entry(Center, Radius, from, Vector3.forward, 5f, 0.3f, out _, out _, out _));
            Assert.IsTrue(SphereBarrierMath.Entry(Center, Radius, from, Vector3.back, 5f, 0.3f, out float d, out _, out _));
            Assert.AreEqual(0f, d, 1e-5f);
        }

        [Test]
        public void 爆炸視線罩外擋罩內不擋()
        {
            Vector3 outside = new Vector3(0f, 1f, 9f);
            Assert.IsTrue(SphereBarrierMath.BlocksSegment(Center, Radius, outside, new Vector3(0f, 1f, 6f)),
                "罩外 → 罩內:擋");
            Assert.IsTrue(SphereBarrierMath.BlocksSegment(Center, Radius, outside, new Vector3(0f, 1f, -9f)),
                "穿過球到另一側:擋");
            Assert.IsFalse(SphereBarrierMath.BlocksSegment(Center, Radius, new Vector3(0f, 1f, 5f), new Vector3(0f, 1f, 12f)),
                "爆心在罩內:不擋");
            Assert.IsFalse(SphereBarrierMath.BlocksSegment(Center, Radius, outside, new Vector3(5f, 1f, 12f)),
                "罩外到罩外沒經過球:不擋");
        }

        [Test]
        public void 球面最近點()
        {
            Assert.IsTrue(SphereBarrierMath.ClosestOnShell(Center, Radius, new Vector3(0f, 1f, 10f), out Vector3 p));
            Assert.AreEqual(7.5f, p.z, 1e-4f);
            Assert.IsFalse(SphereBarrierMath.ClosestOnShell(Center, Radius, new Vector3(0f, 1f, 2f), out _),
                "罩內的點碰不到屏障本身");
        }
    }

    public class WaveBreakerModelTests
    {
        [Test]
        public void 三波時間與擴張速度()
        {
            CollectionAssert.AreEqual(new[] { 90, 240, 390 }, WaveBreakerModel.WaveEmitFrames);
            Assert.AreEqual(7.5f, WaveBreakerModel.WaveSpeed(), 1e-4f, "20 u / 160F = 0.125 u/F = 7.5 m/s");
            Assert.AreEqual(10f, WaveBreakerModel.WaveRadius(80f), 1e-4f);
            Assert.AreEqual(20f, WaveBreakerModel.WaveRadius(160f), 1e-4f);
            Assert.AreEqual(20f, WaveBreakerModel.WaveRadius(500f), 1e-4f);
            Assert.AreEqual(0f, WaveBreakerModel.WaveRadius(0f), 1e-4f);
        }

        [Test]
        public void 設置感應半徑依幀擴張到12點3()
        {
            Assert.AreEqual(0f, WaveBreakerModel.SensorRadius(3f));
            Assert.AreEqual(3.8f, WaveBreakerModel.SensorRadius(4f), 1e-4f);
            Assert.AreEqual(8f, WaveBreakerModel.SensorRadius(10f), 1e-4f);
            Assert.AreEqual(12.3f, WaveBreakerModel.SensorRadius(18f), 1e-4f);
            Assert.AreEqual(12.3f, WaveBreakerModel.SensorRadius(57f), 1e-4f);
            Assert.AreEqual(0f, WaveBreakerModel.SensorRadius(58f), "57F 之後不再判定");
        }

        [Test]
        public void 垂直範圍上2點8下5點2()
        {
            Assert.IsTrue(WaveBreakerModel.InVerticalRange(2.8f));
            Assert.IsFalse(WaveBreakerModel.InVerticalRange(2.81f));
            Assert.IsTrue(WaveBreakerModel.InVerticalRange(-5.2f));
            Assert.IsFalse(WaveBreakerModel.InVerticalRange(-5.21f));
        }

        [Test]
        public void 波前掃過判定與到達時間()
        {
            Assert.IsTrue(WaveBreakerModel.FrontSweeps(9.2f, 9.325f, 10f, 0.4f, 0.35f), "波前 9.325 + 帶 0.35 碰到 10 − 0.4");
            Assert.IsFalse(WaveBreakerModel.FrontSweeps(8f, 8.125f, 10f, 0.4f, 0.35f), "還沒到");
            Assert.IsFalse(WaveBreakerModel.FrontSweeps(11.5f, 11.625f, 10f, 0.4f, 0.35f), "已經過去");
            Assert.AreEqual(0.2f, WaveBreakerModel.SecondsUntilFront(8.5f, 10f, 7.5f), 1e-4f);
            Assert.AreEqual(-1f, WaveBreakerModel.SecondsUntilFront(10.5f, 10f, 7.5f), "已經過去");
            Assert.AreEqual(-1f, WaveBreakerModel.SecondsUntilFront(5f, 21f, 7.5f), "超出 20 u 到不了");
        }
    }

    public class KillerWailModelTests
    {
        [Test]
        public void 時間表每組36F且共288F()
        {
            Assert.AreEqual(36, KillerWailModel.FireFrame(0));
            Assert.AreEqual(72, KillerWailModel.FireFrame(1));
            Assert.AreEqual(108, KillerWailModel.FireFrame(2));
            Assert.AreEqual(252, KillerWailModel.EndFrame(2) - KillerWailModel.FireFrame(0), "JP wiki 総効果時間 252F");
            Assert.AreEqual(288, KillerWailModel.TotalFrames());
            Assert.AreEqual(36, KillerWailModel.TicksPerBeam());
            Assert.AreEqual(756f, KillerWailModel.TicksPerBeam() * KillerWailModel.DamagePerTick * 6f, 1e-3f,
                "6 支全中 = 756(JP wiki 大假人總傷)");
        }

        [Test]
        public void 鎖定範圍正負80度且離射線10u()
        {
            Vector3 origin = Vector3.zero;
            Assert.IsTrue(KillerWailModel.InLockon(origin, Vector3.forward, new Vector3(3f, 0f, 20f), out float lateral));
            Assert.AreEqual(3f, lateral, 1e-4f);
            Assert.IsFalse(KillerWailModel.InLockon(origin, Vector3.forward, new Vector3(11f, 0f, 30f), out _),
                "離射線 11 u");
            Assert.IsFalse(KillerWailModel.InLockon(origin, Vector3.forward, new Vector3(5f, 0f, -1f), out _),
                "背後(超過 80°)");
            Assert.IsTrue(KillerWailModel.InLockon(origin, Vector3.forward, new Vector3(5f, 0f, 1f), out _),
                "79° 內、離射線 5 u");
        }

        [Test]
        public void 六角隊形由上而下成對()
        {
            Assert.Greater(KillerWailModel.HexSlot(0).y, 1f);
            Assert.Greater(KillerWailModel.HexSlot(1).y, 1f);
            Assert.AreEqual(0f, KillerWailModel.HexSlot(2).y, 1e-4f);
            Assert.AreEqual(0f, KillerWailModel.HexSlot(3).y, 1e-4f);
            Assert.Less(KillerWailModel.HexSlot(4).y, -1f);
            Assert.AreEqual(1.3f, KillerWailModel.HexSlot(5).magnitude, 1e-4f);
        }
    }

    public class TrizookaModelTests
    {
        [Test]
        public void 節奏38F首發55F連射()
        {
            Assert.AreEqual(38, TrizookaModel.StartDelayFrames + TrizookaModel.ShotDelayFrames);
            Assert.AreEqual(55, TrizookaModel.RepeatFrames);
        }

        [Test]
        public void 螺旋同時刻差120度且同距離排成一直線()
        {
            // 同一時刻:第 k 顆比第 0 顆晚 4k F 射出 → 自己的飛行幀少 4k
            int frame = 20;
            float a0 = TrizookaModel.OrbitAngleDeg(0, frame);
            float a1 = TrizookaModel.OrbitAngleDeg(1, frame - 4);
            float a2 = TrizookaModel.OrbitAngleDeg(2, frame - 8);
            Assert.AreEqual(120f, Mathf.Repeat(a1 - a0, 360f), 1e-3f);
            Assert.AreEqual(240f, Mathf.Repeat(a2 - a0, 360f), 1e-3f);
            // 同一距離(同一飛行幀):0° / 180° / 0° → 「I」
            Assert.AreEqual(180f, Mathf.Repeat(TrizookaModel.OrbitAngleDeg(1, frame) - a0, 360f), 1e-3f);
            Assert.AreEqual(0f, Mathf.Repeat(TrizookaModel.OrbitAngleDeg(2, frame) - a0, 360f), 1e-3f);
            Assert.AreEqual(0.5f, TrizookaModel.OrbitRadiusAt(5), 1e-5f);
            Assert.AreEqual(1f, TrizookaModel.OrbitRadiusAt(30), 1e-5f);
        }

        [Test]
        public void 平射直線16F後下墜直擊射程約29u()
        {
            BulletMotion motion = TrizookaModel.Motion;
            BulletState state = BulletBallistics.Launch(Vector3.zero, Vector3.forward, motion);
            for (int i = 0; i < 16; i++)
            {
                BulletBallistics.Step(ref state, motion);
            }
            Assert.AreEqual(18f, state.Position.z, 1e-3f, "直線 16F × 1.125");
            Assert.AreEqual(0f, state.Position.y, 1e-4f);
            // 從肩高 1.5 m 平射:落地前飛到 25–30 u(JP wiki 直擊射程 5.8 條 = 29 u,含對人判定 0.75)
            Assert.IsTrue(BulletBallistics.PredictFlatLanding(motion, 1.5f, 0.3f, out int frames, out float distance));
            Assert.That(distance + 0.75f, Is.InRange(25f, 31f), $"{distance:0.00} u,{frames}F");
        }

        [Test]
        public void 歸零仰角讓彈道通過目標點()
        {
            BulletMotion motion = TrizookaModel.Motion;
            float elevation = TrizookaModel.SolveElevation(motion, 29f, -0.5f);
            Assert.AreEqual(-0.5f, TrizookaModel.HeightAt(motion, elevation, 29f), 0.02f);
            Assert.Greater(elevation, 0f, "29 u 外已在下墜:要往上抬");
            float midHeight = TrizookaModel.HeightAt(motion, elevation, 16f);
            Assert.Greater(midHeight, -0.5f * 16f / 29f + 0.2f, "中距離彈道高於準心連線(JP wiki)");
        }
    }
}

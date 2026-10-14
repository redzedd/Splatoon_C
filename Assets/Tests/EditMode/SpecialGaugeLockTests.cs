using NUnit.Framework;
using SplatoonC.Core.Combat;

namespace SplatoonC.Tests
{
    // 大招能量的鎖定型與 S3 死亡規則(research/04 §1.4–1.5)
    public class SpecialGaugeLockTests
    {
        private static SpecialGauge Activated()
        {
            var gauge = new SpecialGauge();
            gauge.Charge(100f);
            Assert.IsTrue(gauge.TryActivate());
            return gauge;
        }

        [Test]
        public void 鎖定後離開啟動狀態並自己倒數到零()
        {
            SpecialGauge gauge = Activated();
            gauge.BeginLock();
            Assert.IsFalse(gauge.IsActive);
            Assert.IsTrue(gauge.IsLocked);
            // Wave Breaker:390F 倒數
            float drain = 100f / (390f / 60f);
            bool ended = false;
            for (int i = 0; i < 389 && !ended; i++)
            {
                ended = gauge.TickLock(1f / 60f, drain);
            }
            Assert.IsFalse(ended, "389F 時還在倒數");
            Assert.IsTrue(gauge.TickLock(1f / 60f, drain) || gauge.TickLock(1f / 60f, drain), "390F 左右歸零");
            Assert.IsFalse(gauge.IsLocked);
            Assert.AreEqual(0f, gauge.Points);
        }

        [Test]
        public void 鎖定中不充能也不能再發動()
        {
            SpecialGauge gauge = Activated();
            gauge.BeginLock();
            gauge.TickLock(1f, 10f);
            float points = gauge.Points;
            gauge.Charge(50f);
            Assert.AreEqual(points, gauge.Points, "鎖定倒數中塗地不回補");
            gauge.SetPoints(100f);
            Assert.IsFalse(gauge.TryActivate(), "鎖定中不能再發動");
        }

        [Test]
        public void 裝置被破壞立刻歸零並恢復充能()
        {
            SpecialGauge gauge = Activated();
            gauge.BeginLock();
            gauge.EndLock();
            Assert.IsFalse(gauge.IsLocked);
            Assert.AreEqual(0f, gauge.Points);
            gauge.Charge(30f);
            Assert.AreEqual(30f, gauge.Points, 0.0001f);
        }

        [Test]
        public void 鎖定型死亡不中斷不懲罰()
        {
            SpecialGauge gauge = Activated();
            gauge.BeginLock();
            gauge.TickLock(1f, 20f);
            gauge.ApplyDeathPenalty(0.5f);
            Assert.IsTrue(gauge.IsLocked);
            Assert.AreEqual(80f, gauge.Points, 0.0001f);
        }

        [Test]
        public void 持續型死亡停止衰減並保留剩餘的一半()
        {
            SpecialGauge gauge = Activated();
            gauge.Tick(1f, 100f / 5.5f);
            float remaining = gauge.Points;
            gauge.ApplyDeathPenalty(0.5f);
            Assert.IsFalse(gauge.IsActive);
            Assert.AreEqual(remaining * 0.5f, gauge.Points, 0.0001f);
            Assert.IsFalse(gauge.Tick(1f, 100f), "死亡後不再衰減");
            Assert.AreEqual(remaining * 0.5f, gauge.Points, 0.0001f);
        }

        [Test]
        public void 未啟動死亡保留比例()
        {
            var gauge = new SpecialGauge();
            gauge.Charge(80f);
            gauge.ApplyDeathPenalty(0.5f);
            Assert.AreEqual(40f, gauge.Points, 0.0001f);
        }

        [Test]
        public void 能量為零時不鎖定()
        {
            SpecialGauge gauge = Activated();
            gauge.ForceEnd();
            gauge.BeginLock();
            Assert.IsFalse(gauge.IsLocked);
        }
    }
}

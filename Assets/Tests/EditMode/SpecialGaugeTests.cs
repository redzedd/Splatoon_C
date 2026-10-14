using NUnit.Framework;
using SplatoonC.Core.Combat;

namespace SplatoonC.Tests
{
    public class SpecialGaugeTests
    {
        [Test]
        public void 充能夾在一百()
        {
            var gauge = new SpecialGauge();
            gauge.Charge(60f);
            gauge.Charge(60f);
            Assert.AreEqual(100f, gauge.Points);
            Assert.IsTrue(gauge.IsFull);
            Assert.AreEqual(1f, gauge.Normalized);
        }

        [Test]
        public void 未滿不能啟動()
        {
            var gauge = new SpecialGauge();
            gauge.Charge(99f);
            Assert.IsFalse(gauge.TryActivate());
            Assert.IsFalse(gauge.IsActive);
        }

        [Test]
        public void 滿了啟動且啟動中不充能()
        {
            var gauge = new SpecialGauge();
            gauge.Charge(100f);
            Assert.IsTrue(gauge.TryActivate());
            Assert.IsTrue(gauge.IsActive);
            gauge.Tick(1f, 100f / 6f);
            float after = gauge.Points;
            gauge.Charge(50f);
            Assert.AreEqual(after, gauge.Points, "啟動中塗地不該回補能量");
        }

        [Test]
        public void 衰減到零即結束()
        {
            var gauge = new SpecialGauge();
            gauge.Charge(100f);
            gauge.TryActivate();
            bool ended = false;
            for (int i = 0; i < 100 && !ended; i++)
            {
                ended = gauge.Tick(0.1f, 100f / 6f);
            }
            Assert.IsTrue(ended, "六秒內應衰減到零");
            Assert.IsFalse(gauge.IsActive);
            Assert.AreEqual(0f, gauge.Points);
        }

        [Test]
        public void 丟滿三個裝置直接歸零結束()
        {
            var gauge = new SpecialGauge();
            gauge.Charge(100f);
            gauge.TryActivate();
            Assert.IsTrue(gauge.CanThrow(3));
            Assert.IsFalse(gauge.RegisterDeviceThrown(3));
            Assert.IsFalse(gauge.RegisterDeviceThrown(3));
            Assert.IsTrue(gauge.CanThrow(3));
            Assert.IsTrue(gauge.RegisterDeviceThrown(3), "第三個丟出即結束");
            Assert.IsFalse(gauge.IsActive);
            Assert.AreEqual(0f, gauge.Points);
            Assert.IsFalse(gauge.CanThrow(3));
        }

        [Test]
        public void 未啟動不能丟裝置()
        {
            var gauge = new SpecialGauge();
            gauge.Charge(100f);
            Assert.IsFalse(gauge.CanThrow(3));
            Assert.IsFalse(gauge.RegisterDeviceThrown(3));
            Assert.AreEqual(0, gauge.DevicesThrown);
        }

        [Test]
        public void 再次啟動裝置計數歸零()
        {
            var gauge = new SpecialGauge();
            gauge.Charge(100f);
            gauge.TryActivate();
            gauge.RegisterDeviceThrown(3);
            while (!gauge.Tick(0.5f, 100f / 6f))
            {
            }
            gauge.Charge(100f);
            Assert.IsTrue(gauge.TryActivate());
            Assert.AreEqual(0, gauge.DevicesThrown);
        }
    }
}

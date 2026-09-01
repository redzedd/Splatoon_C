using NUnit.Framework;
using SplatoonC.Core.Combat;

namespace SplatoonC.Tests
{
    public class InkTankTests
    {
        [Test]
        public void 滿罐可連續消耗到耗盡()
        {
            var tank = InkTank.CreateFull();
            int shots = 0;
            while (tank.TryConsume(0.05f) && shots < 100)
            {
                shots++;
            }
            Assert.AreEqual(20, shots);
        }

        [Test]
        public void 墨不足_不部分扣()
        {
            var tank = InkTank.CreateFull();
            for (int i = 0; i < 19; i++)
            {
                tank.TryConsume(0.05f);
            }
            float before = tank.Normalized;
            Assert.IsFalse(tank.TryConsume(0.1f), "剩 0.05 不夠射 0.1 的一發");
            Assert.AreEqual(before, tank.Normalized, 1e-5f, "失敗的消耗不得扣墨");
        }

        [Test]
        public void 回墨封頂在滿罐()
        {
            var tank = InkTank.CreateFull();
            tank.TryConsume(0.3f);
            tank.Refill(1f, 10f);
            Assert.AreEqual(1f, tank.Normalized, 1e-5f);
        }

        [Test]
        public void 空墨回墨後恢復可射()
        {
            var tank = InkTank.CreateFull();
            while (tank.TryConsume(0.05f))
            {
            }
            Assert.IsFalse(tank.TryConsume(0.05f));
            tank.Refill(0.5f, 0.2f);
            Assert.IsTrue(tank.TryConsume(0.05f), "回 0.1 後應可再射一發");
        }

        [Test]
        public void 回墨速率隨時間線性()
        {
            var tank = InkTank.CreateFull();
            tank.TryConsume(0.5f);
            tank.Refill(0.5f, 0.6f);
            Assert.AreEqual(0.8f, tank.Normalized, 1e-5f);
        }
    }
}

using NUnit.Framework;
using SplatoonC.Core.Combat;

namespace SplatoonC.Tests
{
    public class DamageAccumulatorTests
    {
        [Test]
        public void 累加全額含溢傷()
        {
            var acc = new DamageAccumulator();
            acc.Add(90f, 1f);
            acc.Add(30f, 1.1f);
            Assert.AreEqual(120f, acc.Total, "殺死 100 血的最後一發要全額計入,顯示 120 不是 100");
            Assert.IsTrue(acc.HasHits);
        }

        [Test]
        public void 閒置逾時判定()
        {
            var acc = new DamageAccumulator();
            Assert.IsFalse(acc.HasExpired(100f, 3f), "沒被打過不算逾時");
            acc.Add(30f, 10f);
            Assert.IsFalse(acc.HasExpired(12.9f, 3f));
            Assert.IsTrue(acc.HasExpired(13f, 3f));
        }

        [Test]
        public void 重置清空()
        {
            var acc = new DamageAccumulator();
            acc.Add(30f, 1f);
            acc.Reset();
            Assert.AreEqual(0f, acc.Total);
            Assert.IsFalse(acc.HasHits);
        }
    }
}

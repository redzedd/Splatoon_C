using NUnit.Framework;
using SplatoonC.Core.Combat;

namespace SplatoonC.Tests
{
    public class ExplosionDamageModelTests
    {
        [Test]
        public void 近距離全額()
        {
            Assert.AreEqual(180f, ExplosionDamageModel.Evaluate(0f, 2f, 4f, 180f, 30f));
            Assert.AreEqual(180f, ExplosionDamageModel.Evaluate(2f, 2f, 4f, 180f, 30f), "邊界含入近距離");
        }

        [Test]
        public void 超過近距離但在遠距離內為低額()
        {
            Assert.AreEqual(30f, ExplosionDamageModel.Evaluate(2.01f, 2f, 4f, 180f, 30f));
            Assert.AreEqual(30f, ExplosionDamageModel.Evaluate(4f, 2f, 4f, 180f, 30f));
        }

        [Test]
        public void 超過遠距離無傷()
        {
            Assert.AreEqual(0f, ExplosionDamageModel.Evaluate(4.01f, 2f, 4f, 180f, 30f));
        }
    }
}

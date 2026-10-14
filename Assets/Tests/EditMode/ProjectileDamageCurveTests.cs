using NUnit.Framework;
using SplatoonC.Core.Combat;

namespace SplatoonC.Tests
{
    public class ProjectileDamageCurveTests
    {
        [Test]
        public void 射程內全額()
        {
            Assert.AreEqual(30f, ProjectileDamageCurve.Evaluate(0f, 10f, 30f, 0.1f, 3f));
            Assert.AreEqual(30f, ProjectileDamageCurve.Evaluate(10f, 10f, 30f, 0.1f, 3f), "剛好到射程仍是全額");
        }

        [Test]
        public void 急墜段線性衰減()
        {
            float half = ProjectileDamageCurve.Evaluate(11.5f, 10f, 30f, 0.1f, 3f);
            Assert.AreEqual(15.05f, half, 0.001f, "衰減距離走一半 = 全額與下限的中點");
        }

        [Test]
        public void 超過衰減距離夾在下限()
        {
            Assert.AreEqual(0.1f, ProjectileDamageCurve.Evaluate(13f, 10f, 30f, 0.1f, 3f), 0.0001f);
            Assert.AreEqual(0.1f, ProjectileDamageCurve.Evaluate(50f, 10f, 30f, 0.1f, 3f), 0.0001f);
        }

        [Test]
        public void 衰減距離為零時一出射程即下限()
        {
            Assert.AreEqual(0.1f, ProjectileDamageCurve.Evaluate(10.001f, 10f, 30f, 0.1f, 0f), 0.0001f);
        }
    }
}

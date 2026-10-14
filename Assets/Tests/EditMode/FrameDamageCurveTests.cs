using NUnit.Framework;
using SplatoonC.Core.Combat;

namespace SplatoonC.Tests
{
    // 以飛行幀數衰減(§3.1):Splattershot 36/18、8→40F,取整到 0.1;以及爆破槍的線性爆炸
    public class FrameDamageCurveTests
    {
        private static float Splattershot(int frame)
        {
            return ProjectileDamageCurve.EvaluateByFrame(frame, 36f, 18f, 8, 40);
        }

        [Test]
        public void 衰減開始前全額()
        {
            Assert.AreEqual(36f, Splattershot(1));
            Assert.AreEqual(36f, Splattershot(8), "第 8F 仍全額");
        }

        [Test]
        public void 第12幀是三發擊倒的最後一幀()
        {
            Assert.AreEqual(33.7f, Splattershot(12), 1e-4f, "36 − 0.5625×4 = 33.75 → 33.7");
            Assert.Less(Splattershot(13), 33.4f, "第 13F 起三發打不死");
        }

        [Test]
        public void 衰減結束後維持最小值()
        {
            Assert.AreEqual(18f, Splattershot(40));
            Assert.AreEqual(18f, Splattershot(200));
        }

        [Test]
        public void 負的開始幀代表不衰減()
        {
            Assert.AreEqual(125f, ProjectileDamageCurve.EvaluateByFrame(30, 125f, 125f, -1, 0));
        }

        [Test]
        public void 線性爆炸內圈全額外圈線性()
        {
            // Blaster:70(≤ 1.025)→ 50(3.385)
            Assert.AreEqual(70f, ExplosionDamageModel.EvaluateLinear(0.5f, 1.025f, 3.385f, 70f, 50f));
            Assert.AreEqual(60f, ExplosionDamageModel.EvaluateLinear(2.205f, 1.025f, 3.385f, 70f, 50f), 0.05f);
            Assert.AreEqual(50f, ExplosionDamageModel.EvaluateLinear(3.385f, 1.025f, 3.385f, 70f, 50f), 1e-4f);
            Assert.AreEqual(0f, ExplosionDamageModel.EvaluateLinear(3.4f, 1.025f, 3.385f, 70f, 50f));
        }
    }
}

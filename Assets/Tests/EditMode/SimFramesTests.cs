using NUnit.Framework;
using SplatoonC.Core.Combat;

namespace SplatoonC.Tests
{
    // 60 Hz 幀換算與固定步長累加器:任何幀率下步數 = 經過時間 × 60
    public class SimFramesTests
    {
        [Test]
        public void 幀秒換算()
        {
            Assert.AreEqual(1f, SimFrames.ToSeconds(60), 1e-6f);
            Assert.AreEqual(30, SimFrames.ToFrames(0.5f));
            Assert.AreEqual(20, SimFrames.ToFrames(20f / 60f));
            Assert.AreEqual(136f, SimFrames.PerFrameToPerSecond(2.266f), 0.05f, "Splattershot 2.266 u/F ≈ 136 m/s");
        }

        [TestCase(1f / 30f)]
        [TestCase(1f / 60f)]
        [TestCase(1f / 144f)]
        [TestCase(0.0166f)]
        [TestCase(0.013f)]
        public void 任何幀率十秒都是600步(float dt)
        {
            var clock = new FixedStepClock();
            int steps = 0;
            int frames = (int)(10f / dt + 0.5f);
            for (int i = 0; i < frames; i++)
            {
                steps += clock.Advance(dt);
            }
            Assert.AreEqual(600, steps, 2, $"dt={dt}");
        }

        [Test]
        public void 長掉幀時單幀步數有上限()
        {
            var clock = new FixedStepClock();
            Assert.AreEqual(8, clock.Advance(1f, 8));
            Assert.AreEqual(0, clock.Advance(0f, 8), "超出上限的欠帳丟棄");
        }

        [Test]
        public void 插值比例在0到1之間()
        {
            var clock = new FixedStepClock();
            clock.Advance(SimFrames.Dt * 0.5f);
            Assert.AreEqual(0.5f, clock.Alpha, 1e-3f);
        }
    }
}

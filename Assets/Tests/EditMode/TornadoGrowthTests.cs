using NUnit.Framework;
using SplatoonC.Core.Combat;

namespace SplatoonC.Tests
{
    public class TornadoGrowthTests
    {
        [Test]
        public void 半徑由細變粗到最大()
        {
            Assert.AreEqual(0.2f, TornadoGrowth.Radius(0f, 0.2f, 3f, 1f), 0.0001f);
            float mid = TornadoGrowth.Radius(0.5f, 0.2f, 3f, 1f);
            Assert.AreEqual(1.6f, mid, 0.0001f, "smoothstep 在中點剛好一半");
            Assert.AreEqual(3f, TornadoGrowth.Radius(1f, 0.2f, 3f, 1f), 0.0001f);
            Assert.AreEqual(3f, TornadoGrowth.Radius(5f, 0.2f, 3f, 1f), 0.0001f, "到頂後維持最大");
        }

        [Test]
        public void 階段依序為成長停留淡出結束()
        {
            Assert.AreEqual(TornadoGrowth.Phase.Growing, TornadoGrowth.GetPhase(0.5f, 1f, 0.2f, 0.25f));
            Assert.AreEqual(TornadoGrowth.Phase.Holding, TornadoGrowth.GetPhase(1.1f, 1f, 0.2f, 0.25f));
            Assert.AreEqual(TornadoGrowth.Phase.Fading, TornadoGrowth.GetPhase(1.3f, 1f, 0.2f, 0.25f));
            Assert.AreEqual(TornadoGrowth.Phase.Done, TornadoGrowth.GetPhase(1.45f, 1f, 0.2f, 0.25f));
        }

        [Test]
        public void 停留為零時到頂即淡出()
        {
            Assert.AreEqual(TornadoGrowth.Phase.Fading, TornadoGrowth.GetPhase(1f, 1f, 0f, 0.25f));
        }

        [Test]
        public void 淡出期間透明度線性歸零()
        {
            Assert.AreEqual(1f, TornadoGrowth.Alpha(1.2f, 1f, 0.2f, 0.25f), 0.0001f);
            Assert.AreEqual(0.5f, TornadoGrowth.Alpha(1.325f, 1f, 0.2f, 0.25f), 0.0001f);
            Assert.AreEqual(0f, TornadoGrowth.Alpha(1.45f, 1f, 0.2f, 0.25f), 0.0001f);
        }
    }
}

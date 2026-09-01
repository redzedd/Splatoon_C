using NUnit.Framework;
using SplatoonC.Core.Combat;

namespace SplatoonC.Tests.EditMode
{
    public sealed class DiveTransitionTests
    {
        [Test]
        public void 初始為完全露出()
        {
            var dive = new DiveTransition();

            Assert.AreEqual(0f, dive.Progress, 0.0001f);
        }

        [Test]
        public void 下潛在時長內走完且不超過一()
        {
            var dive = new DiveTransition();

            dive.Advance(true, 0.1f, 0.2f, 0.1f);
            Assert.AreEqual(0.5f, dive.Progress, 0.0001f);

            dive.Advance(true, 0.1f, 0.2f, 0.1f);
            Assert.AreEqual(1f, dive.Progress, 0.0001f);

            dive.Advance(true, 0.1f, 0.2f, 0.1f);
            Assert.AreEqual(1f, dive.Progress, 0.0001f, "不應超過 1");
        }

        [Test]
        public void 鑽出走另一個時長且不低於零()
        {
            var dive = new DiveTransition();
            dive.Advance(true, 1f, 0.2f, 0.1f);

            dive.Advance(false, 0.05f, 0.2f, 0.1f);
            Assert.AreEqual(0.5f, dive.Progress, 0.0001f);

            dive.Advance(false, 0.2f, 0.2f, 0.1f);
            Assert.AreEqual(0f, dive.Progress, 0.0001f, "不應低於 0");
        }

        [Test]
        public void 中途反轉從當前進度繼續()
        {
            var dive = new DiveTransition();
            dive.Advance(true, 0.05f, 0.2f, 0.1f);
            Assert.AreEqual(0.25f, dive.Progress, 0.0001f);

            dive.Advance(false, 0.01f, 0.2f, 0.1f);

            Assert.AreEqual(0.15f, dive.Progress, 0.0001f, "反轉應從 0.25 往回走,而非跳回 0");
        }

        [Test]
        public void 時長為零時瞬間到位()
        {
            var dive = new DiveTransition();

            dive.Advance(true, 0.016f, 0f, 0f);

            Assert.AreEqual(1f, dive.Progress, 0.0001f);
        }

        [Test]
        public void 平滑曲線端點對齊且中點為半()
        {
            Assert.AreEqual(0f, DiveTransition.Ease(0f), 0.0001f);
            Assert.AreEqual(1f, DiveTransition.Ease(1f), 0.0001f);
            Assert.AreEqual(0.5f, DiveTransition.Ease(0.5f), 0.0001f);
            Assert.AreEqual(0f, DiveTransition.Ease(-2f), 0.0001f);
            Assert.AreEqual(1f, DiveTransition.Ease(3f), 0.0001f);
        }

        [Test]
        public void 平滑曲線在頭段比線性慢()
        {
            Assert.Less(DiveTransition.Ease(0.2f), 0.2f);
            Assert.Greater(DiveTransition.Ease(0.8f), 0.8f);
        }
    }
}

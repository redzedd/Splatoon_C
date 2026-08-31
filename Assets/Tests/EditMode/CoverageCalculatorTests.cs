using NUnit.Framework;
using SplatoonC.Core;

namespace SplatoonC.Tests
{
    // 測試島樣板:純邏輯 + NUnit,之後所有 Core 新邏輯照此模式加測試。
    public class CoverageCalculatorTests
    {
        [Test]
        public void Compute_各半塗色_比例正確()
        {
            var r = CoverageCalculator.Compute(50, 50, 200);
            Assert.AreEqual(0.25f, r.TeamRatio, 1e-5f);
            Assert.AreEqual(0.25f, r.EnemyRatio, 1e-5f);
            Assert.AreEqual(0.5f, r.PaintedRatio, 1e-5f);
        }

        [Test]
        public void Compute_空地圖_全零()
        {
            var r = CoverageCalculator.Compute(0, 0, 200);
            Assert.AreEqual(0f, r.TeamRatio);
            Assert.AreEqual(0f, r.EnemyRatio);
            Assert.AreEqual(0f, r.PaintedRatio);
        }

        [Test]
        public void Compute_總數為零_不丟例外且全零()
        {
            var r = CoverageCalculator.Compute(10, 10, 0);
            Assert.AreEqual(0f, r.PaintedRatio);
        }

        [Test]
        public void Compute_負值輸入_夾成零()
        {
            var r = CoverageCalculator.Compute(-5, 100, 200);
            Assert.AreEqual(0f, r.TeamRatio);
            Assert.AreEqual(0.5f, r.EnemyRatio);
        }

        [Test]
        public void Compute_統計溢出總數_塗色比例夾在一()
        {
            var r = CoverageCalculator.Compute(150, 150, 200);
            Assert.AreEqual(1f, r.PaintedRatio, 1e-5f);
        }
    }
}

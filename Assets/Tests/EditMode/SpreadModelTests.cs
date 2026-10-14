using NUnit.Framework;
using SplatoonC.Core.Combat;

namespace SplatoonC.Tests
{
    public class SpreadModelTests
    {
        private static readonly SpreadSettings S = SpreadSettings.Splattershot;

        [Test]
        public void 中位數落在_b乘最大偏角()
        {
            Assert.AreEqual(0.25f * 4.86f, SpreadModel.AngleFor(4.86f, 0.25f, 0.5f), 1e-3f);
            // JP 範例:Splattershot Jr. s=12°, b=0.04, x=0.5 → 0.48°
            Assert.AreEqual(0.48f, SpreadModel.AngleFor(12f, 0.04f, 0.5f), 1e-3f);
        }

        [Test]
        public void 首發幾乎必中準心()
        {
            var m = new SpreadModel();
            float median = m.NextYawOffset(0f, S, false, 0f, 0.5f, 0.9f);
            Assert.AreEqual(0.01f * 4.86f, median, 1e-4f, "首發 b = 0.01:中位數偏角 0.05°");
            Assert.AreEqual(0.02f, m.CurrentBias, 1e-4f);
            // 分佈:P(偏角 < a) = (a/s)^(1/k);首發約 71% 落在 0.5° 內
            float k = UnityEngine.Mathf.Log(0.01f) / UnityEngine.Mathf.Log(0.5f);
            float within = UnityEngine.Mathf.Pow(0.5f / 4.86f, 1f / k);
            Assert.Greater(within, 0.7f);
        }

        [Test]
        public void 連射b上升到上限_停火後回復()
        {
            var m = new SpreadModel();
            float t = 0f;
            for (int i = 0; i < 40; i++)
            {
                m.NextYawOffset(t, S, false, 0f, 0.5f, 0.9f);
                t += 0.1f;
            }
            Assert.AreEqual(0.25f, m.CurrentBias, 1e-4f);
            // 停 1 秒:扣掉 RepeatSeconds 後以 0.9/s 回到下限
            m.NextYawOffset(t + 1f, S, false, 0f, 0.5f, 0.9f);
            Assert.AreEqual(0.02f, m.CurrentBias, 1e-3f, "回到下限後這一發又加一次 Kf");
        }

        [Test]
        public void 跳射固定高偏向且偏角較大()
        {
            var m = new SpreadModel();
            float air = m.NextYawOffset(0f, S, true, 0.1f, 0.5f, 0.9f);
            Assert.AreEqual(0.4f * 11.66f, air, 1e-3f);
            float shrunk = SpreadModel.AngleFor(4.86f, 0.4f, 0.5f);
            float late = m.NextYawOffset(2f, S, true, 2f, 0.5f, 0.9f);
            Assert.AreEqual(shrunk, late, 1e-3f, "起跳 70F 後偏角回到地面值");
        }

        [Test]
        public void 左右隨機()
        {
            var m = new SpreadModel();
            Assert.Less(m.NextYawOffset(0f, S, true, 0f, 0.5f, 0.1f), 0f);
            Assert.Greater(m.NextYawOffset(0.1f, S, true, 0f, 0.5f, 0.9f), 0f);
        }
    }
}

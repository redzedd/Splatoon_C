using NUnit.Framework;
using SplatoonC.Core.Combat;

namespace SplatoonC.Tests.EditMode
{
    public sealed class DripPlannerTests
    {
        [Test]
        public void 距離依序遞增()
        {
            var distances = new float[3];
            var samples = new[] { 0.9f, 0.1f, 0.5f };

            int count = DripPlanner.Plan(distances, 3, 1f, 11f, samples);

            Assert.AreEqual(3, count);
            Assert.LessOrEqual(distances[0], distances[1]);
            Assert.LessOrEqual(distances[1], distances[2]);
        }

        [Test]
        public void 取樣映射到指定區間()
        {
            var distances = new float[3];
            var samples = new[] { 0f, 0.5f, 1f };

            DripPlanner.Plan(distances, 3, 2f, 10f, samples);

            Assert.AreEqual(2f, distances[0], 0.0001f);
            Assert.AreEqual(6f, distances[1], 0.0001f);
            Assert.AreEqual(10f, distances[2], 0.0001f);
        }

        [Test]
        public void 取樣超出範圍會被夾住()
        {
            var distances = new float[2];
            var samples = new[] { -3f, 7f };

            DripPlanner.Plan(distances, 2, 2f, 10f, samples);

            Assert.AreEqual(2f, distances[0], 0.0001f);
            Assert.AreEqual(10f, distances[1], 0.0001f);
        }

        [Test]
        public void 數量被緩衝區長度夾住()
        {
            var distances = new float[2];
            var samples = new float[4];

            int count = DripPlanner.Plan(distances, 4, 1f, 5f, samples);

            Assert.AreEqual(2, count);
        }

        [Test]
        public void 數量為零或負回傳零()
        {
            var distances = new float[3];
            var samples = new float[3];

            Assert.AreEqual(0, DripPlanner.Plan(distances, 0, 1f, 5f, samples));
            Assert.AreEqual(0, DripPlanner.Plan(distances, -1, 1f, 5f, samples));
        }

        [Test]
        public void 偏早權重把滴落點壓向近端()
        {
            var uniform = new float[1];
            var biased = new float[1];
            var samples = new[] { 0.5f };

            DripPlanner.Plan(uniform, 1, 0f, 10f, samples);
            DripPlanner.Plan(biased, 1, 0f, 10f, samples, 2.5f);

            Assert.AreEqual(5f, uniform[0], 0.0001f);
            Assert.Less(biased[0], uniform[0]);
        }

        [Test]
        public void 偏早權重不會超出區間()
        {
            var distances = new float[2];
            var samples = new[] { 0f, 1f };

            DripPlanner.Plan(distances, 2, 2f, 10f, samples, 3f);

            Assert.AreEqual(2f, distances[0], 0.0001f);
            Assert.AreEqual(10f, distances[1], 0.0001f);
        }

        [Test]
        public void 區間倒置時全部落在起點()
        {
            var distances = new float[2];
            var samples = new[] { 0.2f, 0.8f };

            DripPlanner.Plan(distances, 2, 8f, 3f, samples);

            Assert.AreEqual(8f, distances[0], 0.0001f);
            Assert.AreEqual(8f, distances[1], 0.0001f);
        }
    }
}

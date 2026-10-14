using NUnit.Framework;
using SplatoonC.Core.Combat;

namespace SplatoonC.Tests.EditMode
{
    public sealed class DripPlannerTests
    {
        [Test]
        public void 無抖動時每滴落在自己的等分區間()
        {
            var distances = new float[2];

            int count = DripPlanner.Plan(distances, 2, 0f, 10f, 0f, null, 0f);

            Assert.AreEqual(2, count);
            Assert.AreEqual(0f, distances[0], 0.0001f);
            Assert.AreEqual(5f, distances[1], 0.0001f);
        }

        [Test]
        public void 相位把整組滴落點往後推()
        {
            var noPhase = new float[2];
            var halfPhase = new float[2];

            DripPlanner.Plan(noPhase, 2, 0f, 10f, 0f, null, 0f);
            DripPlanner.Plan(halfPhase, 2, 0f, 10f, 0.5f, null, 0f);

            Assert.AreEqual(2.5f, halfPhase[0], 0.0001f);
            Assert.AreEqual(7.5f, halfPhase[1], 0.0001f);
            Assert.Greater(halfPhase[0], noPhase[0]);
        }

        [Test]
        public void 逐發相位錯開後整體間距均勻()
        {
            // 四發一循環、每發兩滴 → 八個位置應等距 10/8 = 1.25
            var all = new System.Collections.Generic.List<float>();
            var buffer = new float[2];
            for (int shot = 0; shot < 4; shot++)
            {
                DripPlanner.Plan(buffer, 2, 0f, 10f, shot / 4f, null, 0f);
                all.Add(buffer[0]);
                all.Add(buffer[1]);
            }
            all.Sort();

            for (int i = 1; i < all.Count; i++)
            {
                Assert.AreEqual(1.25f, all[i] - all[i - 1], 0.0001f,
                    $"第 {i} 個間距不均勻");
            }
        }

        [Test]
        public void 相位為一時不會超出上限()
        {
            var distances = new float[2];

            DripPlanner.Plan(distances, 2, 0f, 10f, 0.999f, null, 0f);

            Assert.LessOrEqual(distances[1], 10f);
        }

        [Test]
        public void 抖動後仍維持遞增且不越界()
        {
            var distances = new float[3];
            // 極端抖動:第一滴往後推到底、第二滴往前拉到底,順序會互換
            var jitter = new[] { 1f, 0f, 1f };

            int count = DripPlanner.Plan(distances, 3, 1f, 10f, 0f, jitter, 5f);

            Assert.AreEqual(3, count);
            Assert.LessOrEqual(distances[0], distances[1]);
            Assert.LessOrEqual(distances[1], distances[2]);
            Assert.GreaterOrEqual(distances[0], 1f);
            Assert.LessOrEqual(distances[2], 10f);
        }

        [Test]
        public void 數量被緩衝區長度夾住()
        {
            var distances = new float[2];

            int count = DripPlanner.Plan(distances, 4, 1f, 5f, 0f, null, 0f);

            Assert.AreEqual(2, count);
        }

        [Test]
        public void 數量為零或負回傳零()
        {
            var distances = new float[3];

            Assert.AreEqual(0, DripPlanner.Plan(distances, 0, 1f, 5f, 0f, null, 0f));
            Assert.AreEqual(0, DripPlanner.Plan(distances, -1, 1f, 5f, 0f, null, 0f));
        }

        [Test]
        public void 區間倒置時全部落在起點()
        {
            var distances = new float[2];

            DripPlanner.Plan(distances, 2, 8f, 3f, 0.5f, null, 0f);

            Assert.AreEqual(8f, distances[0], 0.0001f);
            Assert.AreEqual(8f, distances[1], 0.0001f);
        }
    }
}

using NUnit.Framework;
using SplatoonC.Core.Painting;

namespace SplatoonC.Tests
{
    public class InkOwnershipGridTests
    {
        private static InkOwnershipGrid CreateGrid()
        {
            // 50×50 場地,中心原點,0.5m cell(與場景 Ground 一致)。
            return new InkOwnershipGrid(-25f, -25f, 50f, 50f, 0.5f);
        }

        [Test]
        public void 標記圓形_圓心與圓內有墨()
        {
            var grid = CreateGrid();
            grid.MarkCircle(3f, 3f, 1f, 1);
            Assert.AreEqual(1, grid.Sample(3f, 3f));
            Assert.AreEqual(1, grid.Sample(3.5f, 3f));
        }

        [Test]
        public void 圓外無墨()
        {
            var grid = CreateGrid();
            grid.MarkCircle(3f, 3f, 1f, 1);
            Assert.AreEqual(0, grid.Sample(5f, 3f));
            Assert.AreEqual(0, grid.Sample(3f, -3f));
        }

        [Test]
        public void 未標記處無墨()
        {
            var grid = CreateGrid();
            Assert.AreEqual(0, grid.Sample(0f, 0f));
        }

        [Test]
        public void 界外查詢回零不丟例外()
        {
            var grid = CreateGrid();
            Assert.AreEqual(0, grid.Sample(999f, 999f));
            Assert.AreEqual(0, grid.Sample(-999f, 0f));
        }

        [Test]
        public void 跨界標記_只寫界內不丟例外()
        {
            var grid = CreateGrid();
            grid.MarkCircle(25f, 25f, 3f, 1);
            Assert.AreEqual(1, grid.Sample(24f, 24f));
        }

        [Test]
        public void 後標記覆蓋先標記()
        {
            var grid = CreateGrid();
            grid.MarkCircle(0f, 0f, 1f, 1);
            grid.MarkCircle(0f, 0f, 1f, 2);
            Assert.AreEqual(2, grid.Sample(0f, 0f));
        }
    }
}

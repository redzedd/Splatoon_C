using NUnit.Framework;
using SplatoonC.Core.Painting;

namespace SplatoonC.Tests
{
    // 各隊格數增量維護:佔地判定只加總計數,必須與逐格掃描一致。
    public class InkOwnershipGridTeamCountTests
    {
        private static InkOwnershipGrid CreateGrid()
        {
            return new InkOwnershipGrid(0f, 0f, 10f, 10f, 0.25f);
        }

        private static int Scan(InkOwnershipGrid grid, byte team)
        {
            int count = 0;
            for (int z = 0; z < 40; z++)
            {
                for (int x = 0; x < 40; x++)
                {
                    if (grid.Sample(x * 0.25f + 0.125f, z * 0.25f + 0.125f) == team)
                    {
                        count++;
                    }
                }
            }
            return count;
        }

        [Test]
        public void 初始全是無墨格()
        {
            var grid = CreateGrid();
            Assert.AreEqual(1600, grid.TotalCells);
            Assert.AreEqual(1600, grid.CountOf(0));
            Assert.AreEqual(0, grid.CountOf(1));
        }

        [Test]
        public void 敵墨覆蓋後兩隊計數與掃描一致()
        {
            var grid = CreateGrid();
            grid.MarkCircle(4f, 4f, 2f, 1);
            int overwritten = grid.MarkCircle(5f, 5f, 2f, 2);
            Assert.Greater(overwritten, 0);
            Assert.AreEqual(Scan(grid, 1), grid.CountOf(1));
            Assert.AreEqual(Scan(grid, 2), grid.CountOf(2));
            Assert.AreEqual(grid.TotalCells, grid.CountOf(0) + grid.CountOf(1) + grid.CountOf(2));
        }

        [Test]
        public void 覆蓋敵墨算新增_重塗自家不算()
        {
            var grid = CreateGrid();
            int first = grid.MarkCircle(5f, 5f, 1f, 2);
            Assert.AreEqual(first, grid.MarkCircle(5f, 5f, 1f, 1), "整片敵墨翻成自家墨,新增格數 = 原面積");
            Assert.AreEqual(0, grid.MarkCircle(5f, 5f, 1f, 1));
        }

        [Test]
        public void 封鎖格不計入總數_塗不到_查詢為零()
        {
            var grid = CreateGrid();
            grid.BlockCell(0, 0);
            grid.BlockCell(1, 0);
            Assert.AreEqual(1598, grid.TotalCells);
            Assert.AreEqual(1598, grid.CountOf(0));
            grid.MarkCircle(0.25f, 0.125f, 1f, 1);
            Assert.AreEqual(0, grid.Sample(0.125f, 0.125f), "封鎖格查詢回 0,不會被當成敵墨");
            Assert.AreEqual(grid.TotalCells, grid.CountOf(0) + grid.CountOf(1));
        }

        [Test]
        public void 清空保留封鎖格()
        {
            var grid = CreateGrid();
            grid.BlockCell(3, 3);
            grid.MarkCircle(5f, 5f, 2f, 2);
            grid.Clear();
            Assert.AreEqual(1599, grid.TotalCells);
            Assert.AreEqual(1599, grid.CountOf(0));
            Assert.AreEqual(0, grid.CountOf(2));
        }

        [Test]
        public void 清空後計數歸零()
        {
            var grid = CreateGrid();
            grid.MarkCircle(5f, 5f, 3f, 1);
            grid.Clear();
            Assert.AreEqual(0, grid.CountOf(1));
            Assert.AreEqual(grid.TotalCells, grid.CountOf(0));
            Assert.AreEqual(0, grid.Sample(5f, 5f));
        }
    }
}

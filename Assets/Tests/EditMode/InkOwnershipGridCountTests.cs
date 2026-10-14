using NUnit.Framework;
using SplatoonC.Core.Painting;
using UnityEngine;

namespace SplatoonC.Tests
{
    // MarkCircle 回傳「新增歸屬格數」——大招能量只算新塗到的面積,重塗自家墨不充能。
    public class InkOwnershipGridCountTests
    {
        private static InkOwnershipGrid CreateGrid()
        {
            return new InkOwnershipGrid(-25f, -25f, 50f, 50f, 0.25f);
        }

        [Test]
        public void 首次標記回傳圓面積的格數()
        {
            var grid = CreateGrid();
            int cells = grid.MarkCircle(0f, 0f, 2f, 1);
            float expected = Mathf.PI * 2f * 2f / (0.25f * 0.25f);
            Assert.Greater(cells, expected * 0.9f);
            Assert.Less(cells, expected * 1.1f);
        }

        [Test]
        public void 重塗同一處回傳零()
        {
            var grid = CreateGrid();
            grid.MarkCircle(0f, 0f, 2f, 1);
            Assert.AreEqual(0, grid.MarkCircle(0f, 0f, 2f, 1));
        }

        [Test]
        public void 部分重疊只算新增的格()
        {
            var grid = CreateGrid();
            int first = grid.MarkCircle(0f, 0f, 2f, 1);
            int second = grid.MarkCircle(1f, 0f, 2f, 1);
            Assert.Greater(second, 0);
            Assert.Less(second, first, "半徑 2 平移 1m,新增面積必小於整圓");
        }

        [Test]
        public void 小於格子的圓至少算圓心那格()
        {
            var grid = CreateGrid();
            Assert.AreEqual(1, grid.MarkCircle(0.1f, 0.1f, 0.05f, 1));
            Assert.AreEqual(0, grid.MarkCircle(0.1f, 0.1f, 0.05f, 1), "圓心格已是自家墨,不重複計");
        }
    }
}

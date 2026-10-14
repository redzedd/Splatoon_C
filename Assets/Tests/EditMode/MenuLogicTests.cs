using NUnit.Framework;
using SplatoonC.Core.Menu;
using UnityEngine;

namespace SplatoonC.Tests
{
    public class LookSensitivityTests
    {
        [Test]
        public void 零級為一倍_正負五為四倍與四分之一()
        {
            Assert.AreEqual(1f, LookSensitivity.Multiplier(0f), 1e-5f);
            Assert.AreEqual(4f, LookSensitivity.Multiplier(5f), 1e-4f);
            Assert.AreEqual(0.25f, LookSensitivity.Multiplier(-5f), 1e-5f);
        }

        [Test]
        public void 每二點五級倍率翻倍_指數對應()
        {
            Assert.AreEqual(2f, LookSensitivity.Multiplier(2.5f), 1e-4f);
            Assert.AreEqual(0.5f, LookSensitivity.Multiplier(-2.5f), 1e-5f);
            // 相鄰兩格的比例處處相同
            float r1 = LookSensitivity.Multiplier(0.5f) / LookSensitivity.Multiplier(0f);
            float r2 = LookSensitivity.Multiplier(5f) / LookSensitivity.Multiplier(4.5f);
            Assert.AreEqual(r1, r2, 1e-4f);
        }

        [Test]
        public void 超出範圍與非半格值會對齊()
        {
            Assert.AreEqual(5f, LookSensitivity.Clamp(9f), 1e-5f);
            Assert.AreEqual(-5f, LookSensitivity.Clamp(-7.2f), 1e-5f);
            Assert.AreEqual(1.5f, LookSensitivity.Clamp(1.4f), 1e-5f);
            Assert.AreEqual(4f, LookSensitivity.Multiplier(12f), 1e-4f);
        }

        [Test]
        public void 格位與級數互轉_共二十一格()
        {
            Assert.AreEqual(21, LookSensitivity.StepCount);
            Assert.AreEqual(0, LookSensitivity.IndexOfLevel(-5f));
            Assert.AreEqual(10, LookSensitivity.IndexOfLevel(0f));
            Assert.AreEqual(20, LookSensitivity.IndexOfLevel(5f));
            for (int i = 0; i < LookSensitivity.StepCount; i++)
            {
                Assert.AreEqual(i, LookSensitivity.IndexOfLevel(LookSensitivity.LevelOfIndex(i)));
            }
        }

        [Test]
        public void 顯示格式_正號與零()
        {
            Assert.AreEqual("0", LookSensitivity.Format(0f));
            Assert.AreEqual("+2.5", LookSensitivity.Format(2.5f));
            Assert.AreEqual("-1.0", LookSensitivity.Format(-1f));
        }

        [Test]
        public void 套用倍率與反轉_各軸獨立()
        {
            Vector2 d = LookSensitivity.Apply(new Vector2(2f, 3f), 2.5f, 0f, false, true);
            Assert.AreEqual(4f, d.x, 1e-4f);
            Assert.AreEqual(-3f, d.y, 1e-4f);
            Vector2 e = LookSensitivity.Apply(new Vector2(2f, 3f), 0f, -2.5f, true, false);
            Assert.AreEqual(-2f, e.x, 1e-4f);
            Assert.AreEqual(1.5f, e.y, 1e-4f);
        }
    }

    public class MenuGridLayoutTests
    {
        private const float W = 1000f;
        private const float H = 700f;
        private const float Gap = 16f;
        private const float Aspect = 0.8f;

        [Test]
        public void 一張卡_用最大卡寬並置中()
        {
            GridLayoutResult r = MenuGridLayout.Compute(1, W, H, Gap, Aspect, 180f, 380f);
            Assert.AreEqual(1, r.Columns);
            Assert.AreEqual(380f, r.CellWidth, 1e-3f);
            Assert.IsFalse(r.Scrolls);
            Assert.AreEqual((W - 380f) * 0.5f, r.OffsetX, 1e-3f);
            Assert.AreEqual((H - 380f * Aspect) * 0.5f, r.OffsetY, 1e-3f);
        }

        [Test]
        public void 兩張卡_並排而非疊成一列()
        {
            GridLayoutResult r = MenuGridLayout.Compute(2, W, H, Gap, Aspect, 180f, 380f);
            Assert.AreEqual(2, r.Columns);
            Assert.AreEqual(1, r.Rows);
            Assert.AreEqual(380f, r.CellWidth, 1e-3f);
        }

        [Test]
        public void 一到三十張_卡片不超出區域寬度且只有塞不下才捲動()
        {
            for (int n = 1; n <= 30; n++)
            {
                GridLayoutResult r = MenuGridLayout.Compute(n, W, H, Gap, Aspect, 180f, 380f);
                float blockWidth = r.Columns * r.CellWidth + (r.Columns - 1) * Gap;
                Assert.LessOrEqual(blockWidth, W + 0.01f, $"n={n}");
                Assert.GreaterOrEqual(r.CellWidth, 180f - 0.01f, $"n={n}");
                Assert.AreEqual((n + r.Columns - 1) / r.Columns, r.Rows, $"n={n}");
                if (!r.Scrolls)
                {
                    Assert.LessOrEqual(r.ContentHeight, H + 0.01f, $"n={n}");
                }
                else
                {
                    Assert.Greater(r.ContentHeight, H, $"n={n}");
                }
            }
        }

        [Test]
        public void 卡片越多卡寬不會變大()
        {
            float previous = float.MaxValue;
            for (int n = 1; n <= 30; n++)
            {
                GridLayoutResult r = MenuGridLayout.Compute(n, W, H, Gap, Aspect, 180f, 380f);
                Assert.LessOrEqual(r.CellWidth, previous + 0.01f, $"n={n}");
                previous = r.CellWidth;
            }
        }

        [Test]
        public void 三十張塞不下_改捲動且欄數用下限卡寬()
        {
            GridLayoutResult r = MenuGridLayout.Compute(30, W, H, Gap, Aspect, 180f, 380f);
            Assert.IsTrue(r.Scrolls);
            Assert.AreEqual(5, r.Columns);
            Assert.AreEqual(6, r.Rows);
            Assert.AreEqual(0f, r.OffsetY, 1e-3f);
        }

        [Test]
        public void 卡片位置依欄列排列()
        {
            GridLayoutResult r = MenuGridLayout.Compute(7, W, H, Gap, Aspect, 180f, 380f);
            Vector2 first = MenuGridLayout.CellTopLeft(r, 0);
            Vector2 second = MenuGridLayout.CellTopLeft(r, 1);
            Vector2 nextRow = MenuGridLayout.CellTopLeft(r, r.Columns);
            Assert.AreEqual(r.CellWidth + Gap, second.x - first.x, 1e-3f);
            Assert.AreEqual(r.CellHeight + Gap, nextRow.y - first.y, 1e-3f);
            Assert.AreEqual(first.x, nextRow.x, 1e-3f);
        }

        [Test]
        public void 格子導覽_邊界回傳負一且往下落在最後一張()
        {
            // 7 張、3 欄:0 1 2 / 3 4 5 / 6
            Assert.AreEqual(1, MenuGridLayout.Neighbor(0, 7, 3, 1, 0));
            Assert.AreEqual(-1, MenuGridLayout.Neighbor(2, 7, 3, 1, 0));
            Assert.AreEqual(-1, MenuGridLayout.Neighbor(3, 7, 3, -1, 0));
            Assert.AreEqual(-1, MenuGridLayout.Neighbor(6, 7, 3, 1, 0));
            Assert.AreEqual(-1, MenuGridLayout.Neighbor(1, 7, 3, 0, -1));
            Assert.AreEqual(4, MenuGridLayout.Neighbor(1, 7, 3, 0, 1));
            Assert.AreEqual(6, MenuGridLayout.Neighbor(5, 7, 3, 0, 1));
            Assert.AreEqual(6, MenuGridLayout.Neighbor(4, 7, 3, 0, 1));
            Assert.AreEqual(-1, MenuGridLayout.Neighbor(6, 7, 3, 0, 1));
            Assert.AreEqual(3, MenuGridLayout.Neighbor(6, 7, 3, 0, -1));
            Assert.AreEqual(-1, MenuGridLayout.Neighbor(0, 1, 1, 0, 1));
        }

        [Test]
        public void 捲動讓選到的卡完整可見()
        {
            // 可視 700、內容 1400:卡在 900~1050 → 捲到 1050+20-700
            Assert.AreEqual(370f, MenuGridLayout.ScrollToReveal(0f, 700f, 1400f, 900f, 150f, 20f), 1e-3f);
            // 往上:卡在 100 → 捲到 80
            Assert.AreEqual(80f, MenuGridLayout.ScrollToReveal(500f, 700f, 1400f, 100f, 150f, 20f), 1e-3f);
            // 已可見不動
            Assert.AreEqual(200f, MenuGridLayout.ScrollToReveal(200f, 700f, 1400f, 400f, 150f, 20f), 1e-3f);
            // 內容比可視短:永遠 0
            Assert.AreEqual(0f, MenuGridLayout.ScrollToReveal(0f, 700f, 500f, 400f, 150f, 20f), 1e-3f);
        }
    }

    public class MenuMathTests
    {
        [Test]
        public void 循環切換_wrap與夾住()
        {
            Assert.AreEqual(0, MenuMath.Cycle(2, 1, 3, true));
            Assert.AreEqual(2, MenuMath.Cycle(0, -1, 3, true));
            Assert.AreEqual(2, MenuMath.Cycle(2, 1, 3, false));
            Assert.AreEqual(0, MenuMath.Cycle(0, -1, 3, false));
            Assert.AreEqual(0, MenuMath.Cycle(5, 1, 0, true));
        }

        [Test]
        public void 音量格位互轉()
        {
            Assert.AreEqual(8, MenuMath.VolumeToStep(0.8f, 10));
            Assert.AreEqual(10, MenuMath.VolumeToStep(1.3f, 10));
            Assert.AreEqual(0, MenuMath.VolumeToStep(-1f, 10));
            Assert.AreEqual(0.3f, MenuMath.StepToVolume(3, 10), 1e-5f);
            Assert.AreEqual(0f, MenuMath.StepToVolume(3, 0), 1e-5f);
        }

        [Test]
        public void 條狀選項點擊比例換成格位()
        {
            Assert.AreEqual(0, MenuMath.StepFromFraction(0f, 21));
            Assert.AreEqual(10, MenuMath.StepFromFraction(0.5f, 21));
            Assert.AreEqual(20, MenuMath.StepFromFraction(1.2f, 21));
            Assert.AreEqual(0, MenuMath.StepFromFraction(0.7f, 1));
        }
    }
}

using NUnit.Framework;
using SplatoonC.Core.Combat;
using UnityEngine;

namespace SplatoonC.Tests
{
    // 平面圓盤塗墨的筆刷排列(Suction Bomb 黏住的面 r 5.0 + 交叉塗墨 2.5)
    public class DiscStampLayoutTests
    {
        private static int Build(float radius, float stamp, out Vector2[] offsets)
        {
            offsets = new Vector2[64];
            return DiscStampLayout.Build(radius, stamp, offsets);
        }

        private static bool Covered(Vector2 p, Vector2[] offsets, int count, float stamp)
        {
            for (int i = 0; i < count; i++)
            {
                if ((p - offsets[i]).sqrMagnitude <= stamp * stamp)
                {
                    return true;
                }
            }
            return false;
        }

        [Test]
        public void 半徑5交叉25_圓盤內全覆蓋且外緣接近5()
        {
            int count = Build(5f, 2.5f, out Vector2[] offsets);
            Assert.That(count, Is.InRange(8, 16), $"筆刷 {count} 顆");
            float minReach = float.MaxValue;
            float maxReach = 0f;
            for (int a = 0; a < 360; a += 3)
            {
                Vector2 dir = new Vector2(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad));
                // 由內往外找第一個沒覆蓋的半徑
                float reach = 0f;
                for (float r = 0f; r <= 7f; r += 0.02f)
                {
                    if (!Covered(dir * r, offsets, count, 2.5f))
                    {
                        break;
                    }
                    reach = r;
                }
                minReach = Mathf.Min(minReach, reach);
                maxReach = Mathf.Max(maxReach, reach);
            }
            Assert.That(minReach, Is.GreaterThanOrEqualTo(4.75f), $"最短 {minReach:0.00}");
            Assert.That(maxReach, Is.LessThanOrEqualTo(5.6f), $"最長 {maxReach:0.00}");
        }

        [Test]
        public void 半徑5筆刷125_Suction實際排列全覆蓋()
        {
            int count = Build(5f, 1.25f, out Vector2[] offsets);
            Assert.That(count, Is.InRange(20, 48), $"筆刷 {count} 顆");
            for (int a = 0; a < 360; a += 2)
            {
                Vector2 dir = new Vector2(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad));
                for (float r = 0f; r <= 4.85f; r += 0.05f)
                {
                    Assert.IsTrue(Covered(dir * r, offsets, count, 1.25f), $"角度 {a}° 半徑 {r:0.00} 沒塗到");
                }
                Assert.IsFalse(Covered(dir * 5.4f, offsets, count, 1.25f), $"角度 {a}° 半徑 5.4 不該塗到");
            }
        }

        [Test]
        public void 小圓盤只要一顆()
        {
            int count = Build(2f, 2.5f, out Vector2[] offsets);
            Assert.AreEqual(1, count);
            Assert.AreEqual(Vector2.zero, offsets[0]);
            Assert.AreEqual(2f, DiscStampLayout.StampRadius(2f, 2.5f));
            Assert.AreEqual(2.5f, DiscStampLayout.StampRadius(5f, 2.5f));
        }

        [Test]
        public void 大圓盤多環仍全覆蓋()
        {
            int count = Build(9f, 2f, out Vector2[] offsets);
            for (int a = 0; a < 360; a += 5)
            {
                Vector2 dir = new Vector2(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad));
                for (float r = 0f; r <= 8.6f; r += 0.1f)
                {
                    Assert.IsTrue(Covered(dir * r, offsets, count, 2f), $"角度 {a}° 半徑 {r:0.0} 沒塗到(共 {count} 顆)");
                }
            }
        }
    }
}

using NUnit.Framework;
using SplatoonC.Core.Combat;
using UnityEngine;

namespace SplatoonC.Tests
{
    public class CapsuleMathTests
    {
        // 假人膠囊:中心 y=1、高 2、半徑 0.4 → 軸線 y 0.4~1.6
        private static readonly Vector3 A = new Vector3(0f, 0.4f, 0f);
        private static readonly Vector3 B = new Vector3(0f, 1.6f, 0f);

        [Test]
        public void 側邊最近點在圓柱面上()
        {
            Vector3 p = CapsuleMath.ClosestPoint(A, B, 0.4f, new Vector3(3f, 1f, 0f));
            Assert.AreEqual(new Vector3(0.4f, 1f, 0f), p);
        }

        [Test]
        public void 上方最近點在半球頂()
        {
            Vector3 p = CapsuleMath.ClosestPoint(A, B, 0.4f, new Vector3(0f, 5f, 0f));
            Assert.AreEqual(2f, p.y, 0.0001f);
        }

        [Test]
        public void 膠囊內的點回傳自己()
        {
            Vector3 inside = new Vector3(0.1f, 1f, 0.1f);
            Assert.AreEqual(inside, CapsuleMath.ClosestPoint(A, B, 0.4f, inside));
        }

        [Test]
        public void 斜向距離正確()
        {
            Vector3 from = new Vector3(3f, 1f, 4f);
            Vector3 p = CapsuleMath.ClosestPoint(A, B, 0.4f, from);
            Assert.AreEqual(5f - 0.4f, Vector3.Distance(from, p), 0.0001f, "到軸線 5m,扣半徑 0.4");
        }
    }
}

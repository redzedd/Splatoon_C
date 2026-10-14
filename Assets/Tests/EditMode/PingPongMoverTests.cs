using NUnit.Framework;
using SplatoonC.Core.Combat;
using UnityEngine;

namespace SplatoonC.Tests
{
    public class PingPongMoverTests
    {
        private static readonly Vector3 A = new Vector3(18f, 0f, 6f);
        private static readonly Vector3 B = new Vector3(23f, 0f, 6f);

        [Test]
        public void 起點與去程()
        {
            Vector3 p0 = PingPongMover.Position(A, B, 2f, 0f, out int d0);
            Assert.AreEqual(A, p0);
            Assert.AreEqual(1, d0);
            Vector3 p1 = PingPongMover.Position(A, B, 2f, 1f, out int d1);
            Assert.AreEqual(20f, p1.x, 0.0001f, "2 m/s 走 1 秒 = 2m");
            Assert.AreEqual(1, d1);
        }

        [Test]
        public void 到終點折返()
        {
            Vector3 p = PingPongMover.Position(A, B, 2f, 3f, out int d);
            Assert.AreEqual(22f, p.x, 0.0001f, "2.5 秒到 B 後折返 0.5 秒 = 1m");
            Assert.AreEqual(-1, d);
        }

        [Test]
        public void 整個週期回到起點()
        {
            Vector3 p = PingPongMover.Position(A, B, 2f, 5f, out _);
            Assert.AreEqual(A.x, p.x, 0.0001f);
        }

        [Test]
        public void 零速或零距離停在起點()
        {
            Assert.AreEqual(A, PingPongMover.Position(A, B, 0f, 7f, out _));
            Assert.AreEqual(A, PingPongMover.Position(A, A, 2f, 7f, out _));
        }
    }
}

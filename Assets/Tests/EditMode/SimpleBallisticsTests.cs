using NUnit.Framework;
using SplatoonC.Core.Combat;
using UnityEngine;

namespace SplatoonC.Tests
{
    public class SimpleBallisticsTests
    {
        [Test]
        public void 上抬後初速大小不變且有向上分量()
        {
            Vector3 v = SimpleBallistics.LaunchVelocity(Vector3.forward, 14f, 20f);
            Assert.AreEqual(14f, v.magnitude, 0.001f);
            Assert.AreEqual(14f * Mathf.Sin(20f * Mathf.Deg2Rad), v.y, 0.001f, "上抬 20° 的垂直分量");
            Assert.AreEqual(14f * Mathf.Cos(20f * Mathf.Deg2Rad), v.z, 0.001f);
            Assert.AreEqual(0f, v.x, 0.001f);
        }

        [Test]
        public void 朝下瞄準上抬後仍朝前()
        {
            Vector3 aim = new Vector3(0f, -1f, 1f).normalized;
            Vector3 v = SimpleBallistics.LaunchVelocity(aim, 10f, 20f);
            Assert.Greater(v.z, 0f);
            Assert.Greater(v.y, aim.y * 10f, "上抬後比原方向高");
        }

        [Test]
        public void 純垂直方向退回原方向()
        {
            Vector3 v = SimpleBallistics.LaunchVelocity(Vector3.up, 10f, 20f);
            Assert.AreEqual(10f, v.y, 0.001f);
        }

        [Test]
        public void 步進為半隱式歐拉()
        {
            Vector3 p = new Vector3(0f, 10f, 0f);
            Vector3 v = new Vector3(5f, 0f, 0f);
            SimpleBallistics.Step(ref p, ref v, -20f, 0.5f);
            Assert.AreEqual(-10f, v.y, 0.001f);
            Assert.AreEqual(new Vector3(2.5f, 5f, 0f), p);
        }

        [Test]
        public void 反彈保留切向並衰減法向()
        {
            Vector3 r = SimpleBallistics.Reflect(new Vector3(0f, -10f, 3f), Vector3.up, 0.35f, 0.6f);
            Assert.AreEqual(3.5f, r.y, 0.001f, "法向 10 × 0.35 反向");
            Assert.AreEqual(1.8f, r.z, 0.001f, "切向 3 × 0.6");
        }
    }
}

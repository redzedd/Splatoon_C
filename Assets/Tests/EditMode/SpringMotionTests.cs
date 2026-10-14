using NUnit.Framework;
using SplatoonC.Core.Animation;
using UnityEngine;

namespace SplatoonC.Tests.EditMode
{
    public sealed class SpringMotionTests
    {
        [Test]
        public void 半衰期平滑_經過一個半衰期差距減半()
        {
            float v = SpringMotion.Damp(0f, 1f, 0.1f, 0.1f);

            Assert.AreEqual(0.5f, v, 1e-4f);
        }

        [Test]
        public void 平滑與幀率無關()
        {
            float a = 0f;
            for (int i = 0; i < 60; i++)
            {
                a = SpringMotion.Damp(a, 1f, 0.2f, 1f / 60f);
            }
            float b = SpringMotion.Damp(0f, 1f, 0.2f, 1f);

            Assert.AreEqual(b, a, 1e-4f);
        }

        [Test]
        public void 彈簧收斂到目標()
        {
            float x = 0f;
            float v = 0f;
            for (int i = 0; i < 240; i++)
            {
                SpringMotion.Step(ref x, ref v, 1f, 200f, 11f, 1f / 60f);
            }

            Assert.AreEqual(1f, x, 1e-3f);
            Assert.AreEqual(0f, v, 1e-2f);
        }

        [Test]
        public void 大步長不發散()
        {
            Vector3 x = Vector3.zero;
            Vector3 v = new Vector3(0f, 30f, 0f);
            for (int i = 0; i < 20; i++)
            {
                SpringMotion.Step(ref x, ref v, Vector3.zero, 400f, 30f, 0.1f);
            }

            Assert.Less(x.magnitude, 0.01f);
        }

        [Test]
        public void 升降不同速()
        {
            float up = SpringMotion.DampAsymmetric(0f, 1f, 0.01f, 1f, 0.05f);
            float down = SpringMotion.DampAsymmetric(1f, 0f, 0.01f, 1f, 0.05f);

            Assert.Greater(up, 0.95f);
            Assert.Greater(down, 0.9f);
        }
    }
}

using NUnit.Framework;
using SplatoonC.Core.Locomotion;
using UnityEngine;

namespace SplatoonC.Tests
{
    public class WallClimbSolverTests
    {
        [Test]
        public void 垂直牆_上爬加貼牆()
        {
            Vector3 step = WallClimbSolver.Step(1f, new Vector3(-1f, 0f, 0f), 3.5f, 1f, 1f);
            Assert.AreEqual(1f, step.x, 1e-4f);
            Assert.AreEqual(3.5f, step.y, 1e-4f);
            Assert.AreEqual(0f, step.z, 1e-4f);
        }

        [Test]
        public void 垂直牆_下滑()
        {
            Vector3 step = WallClimbSolver.Step(-1f, new Vector3(-1f, 0f, 0f), 3.5f, 0f, 1f);
            Assert.AreEqual(-3.5f, step.y, 1e-4f);
        }

        [Test]
        public void 無輸入_只有貼牆分量()
        {
            Vector3 step = WallClimbSolver.Step(0f, new Vector3(0f, 0f, -1f), 3.5f, 2f, 0.5f);
            Assert.AreEqual(new Vector3(0f, 0f, 1f), step);
        }

        [Test]
        public void 斜面_沿坡面爬升()
        {
            var normal = new Vector3(-0.5f, 0.8660254f, 0f);
            Vector3 up = WallClimbSolver.UpAlongWall(normal);
            Assert.AreEqual(0.8660254f, up.x, 1e-4f);
            Assert.AreEqual(0.5f, up.y, 1e-4f);
        }

        [Test]
        public void 天花板退化_無爬升方向不丟例外()
        {
            Vector3 step = WallClimbSolver.Step(1f, Vector3.down, 3.5f, 1f, 1f);
            Assert.AreEqual(new Vector3(0f, 1f, 0f), step);
        }
    }
}

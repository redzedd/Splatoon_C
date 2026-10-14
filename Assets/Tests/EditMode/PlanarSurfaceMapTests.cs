using NUnit.Framework;
using SplatoonC.Core.Painting;
using UnityEngine;

namespace SplatoonC.Tests
{
    public class PlanarSurfaceMapTests
    {
        [Test]
        public void 水平地面_法線Y_攤成XZ平面()
        {
            // Unity Plane mesh:局部 10×10,Y 厚度 0
            var map = PlanarSurfaceMap.FromBounds(new Vector3(-5f, 0f, -5f), new Vector3(10f, 0f, 10f));
            Assert.AreEqual(1, map.NormalAxis);
            Assert.AreEqual(new Vector2(-5f, -5f), map.PlaneMin);
            Assert.AreEqual(new Vector2(10f, 10f), map.PlaneSize);
            Assert.AreEqual(new Vector2(3f, -2f), map.ToPlane(new Vector3(3f, 0.5f, -2f)));
        }

        [Test]
        public void 垂直牆_法線Z_攤成XY平面()
        {
            // Unity Quad mesh:局部 1×1,Z 厚度 0
            var map = PlanarSurfaceMap.FromBounds(new Vector3(-0.5f, -0.5f, 0f), new Vector3(1f, 1f, 0f));
            Assert.AreEqual(2, map.NormalAxis);
            Assert.AreEqual(new Vector2(-0.5f, -0.5f), map.PlaneMin);
            Assert.AreEqual(new Vector2(0.3f, -0.1f), map.ToPlane(new Vector3(0.3f, -0.1f, 0f)));
        }

        [Test]
        public void 側牆_法線X_攤成ZY平面()
        {
            var map = PlanarSurfaceMap.FromBounds(new Vector3(0f, -2f, -3f), new Vector3(0f, 4f, 6f));
            Assert.AreEqual(0, map.NormalAxis);
            Assert.AreEqual(new Vector2(-3f, -2f), map.PlaneMin);
            Assert.AreEqual(new Vector2(1.5f, 1f), map.ToPlane(new Vector3(0f, 1f, 1.5f)));
        }

        [Test]
        public void 等厚退化_取先到的X軸_決定性()
        {
            var map = PlanarSurfaceMap.FromBounds(Vector3.zero, new Vector3(2f, 2f, 2f));
            Assert.AreEqual(0, map.NormalAxis);
        }
    }
}

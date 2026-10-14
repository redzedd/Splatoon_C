using NUnit.Framework;
using SplatoonC.Core.Painting;
using UnityEngine;

namespace SplatoonC.Tests
{
    public class SurfaceFaceTests
    {
        private const float Eps = 1e-4f;

        [Test]
        public void 平面座標往返一致()
        {
            var face = SurfaceFace.Create(new Vector3(1f, 2f, 3f), Vector3.right, Vector3.back, 4f, 6f);
            Vector3 p = face.FromPlane(new Vector2(1.5f, 2.5f));
            Vector2 q = face.ToPlane(p);
            Assert.AreEqual(1.5f, q.x, Eps);
            Assert.AreEqual(2.5f, q.y, Eps);
        }

        [Test]
        public void 法線等於U叉V_正面距離為正()
        {
            var face = SurfaceFace.Create(Vector3.zero, Vector3.right, Vector3.back, 1f, 1f);
            Assert.AreEqual(1f, Vector3.Dot(face.Normal, Vector3.up), Eps, "right × back = up");
            Assert.Greater(face.SignedDistance(new Vector3(0.5f, 0.3f, -0.5f)), 0f);
            Assert.Less(face.SignedDistance(new Vector3(0.5f, -0.3f, -0.5f)), 0f);
        }

        [Test]
        public void 矩形包含判定含邊距()
        {
            var face = SurfaceFace.Create(Vector3.zero, Vector3.right, Vector3.up, 2f, 1f);
            Assert.IsTrue(face.Contains(new Vector2(1f, 0.5f), 0f));
            Assert.IsFalse(face.Contains(new Vector2(2.2f, 0.5f), 0f));
            Assert.IsTrue(face.Contains(new Vector2(2.2f, 0.5f), 0.25f));
        }

        [Test]
        public void 三角形斜邊外不包含()
        {
            var face = SurfaceFace.Create(Vector3.zero, Vector3.right, Vector3.up, 2f, 2f, SurfaceFaceShape.Triangle);
            Assert.IsTrue(face.Contains(new Vector2(0.5f, 0.5f), 0f));
            Assert.IsFalse(face.Contains(new Vector2(1.5f, 1.5f), 0f));
            Assert.AreEqual(2f, face.Area, Eps);
        }

        [Test]
        public void 轉換到世界空間_寬高隨縮放伸縮()
        {
            var face = SurfaceFace.Create(Vector3.zero, Vector3.right, Vector3.back, 1f, 1f);
            Matrix4x4 m = Matrix4x4.TRS(new Vector3(10f, 0f, 0f), Quaternion.identity, new Vector3(5f, 1f, 3f));
            SurfaceFace world = face.Transform(m);
            Assert.AreEqual(5f, world.Width, Eps);
            Assert.AreEqual(3f, world.Height, Eps);
            Assert.AreEqual(10f, world.Origin.x, Eps);
            Assert.AreEqual(1f, Vector3.Dot(world.Normal, Vector3.up), Eps);
        }

        [Test]
        public void 從邊界推面_法線跟隨mesh法線()
        {
            // Unity Quad:局部 1×1、法線 -Z
            SurfaceFace quad = SurfaceFace.FromBounds(new Vector3(-0.5f, -0.5f, 0f), new Vector3(1f, 1f, 0f), Vector3.back);
            Assert.AreEqual(1f, Vector3.Dot(quad.Normal, Vector3.back), Eps);
            Assert.AreEqual(1f, quad.Width, Eps);
            Vector2 center = quad.ToPlane(Vector3.zero);
            Assert.AreEqual(0.5f, center.x, Eps);
            Assert.AreEqual(0.5f, center.y, Eps);

            // Unity Plane:局部 10×10、法線 +Y
            SurfaceFace plane = SurfaceFace.FromBounds(new Vector3(-5f, 0f, -5f), new Vector3(10f, 0f, 10f), Vector3.up);
            Assert.AreEqual(1f, Vector3.Dot(plane.Normal, Vector3.up), Eps);
            Assert.IsTrue(plane.Contains(plane.ToPlane(new Vector3(4.9f, 0f, -4.9f)), 0f));
        }
    }
}

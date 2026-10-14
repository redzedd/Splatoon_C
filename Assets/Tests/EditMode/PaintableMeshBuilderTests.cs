using NUnit.Framework;
using SplatoonC.Core.Painting;
using UnityEngine;

namespace SplatoonC.Tests
{
    public class PaintableMeshBuilderTests
    {
        private const float Eps = 1e-3f;

        [Test]
        public void 方塊六面法線朝外()
        {
            PaintableMeshData data = PaintableMeshBuilder.BuildBox(new Vector3(4f, 2f, 6f), 0.1f);
            Assert.AreEqual(6, data.Faces.Length);
            foreach (SurfaceFace face in data.Faces)
            {
                Vector3 center = face.FromPlane(new Vector2(face.Width * 0.5f, face.Height * 0.5f));
                Assert.Greater(Vector3.Dot(face.Normal, center), 0f, "中心樞紐方塊:面中心沿法線必然遠離原點");
                Assert.AreEqual(1f, Vector3.Dot(Vector3.Cross(face.AxisU, face.AxisV), face.Normal), Eps);
            }
        }

        [Test]
        public void 三角形繞序與面法線一致()
        {
            PaintableMeshData data = PaintableMeshBuilder.BuildWedge(new Vector3(3f, 1.5f, 4f), 0.1f);
            for (int t = 0; t < data.Triangles.Length; t += 3)
            {
                Vector3 a = data.Vertices[data.Triangles[t]];
                Vector3 b = data.Vertices[data.Triangles[t + 1]];
                Vector3 c = data.Vertices[data.Triangles[t + 2]];
                Vector3 n = Vector3.Cross(b - a, c - a).normalized;
                Assert.Greater(Vector3.Dot(n, data.Normals[data.Triangles[t]]), 0.99f);
            }
        }

        [Test]
        public void 斜坡坡面朝上且長度正確()
        {
            PaintableMeshData data = PaintableMeshBuilder.BuildWedge(new Vector3(3f, 3f, 4f), 0.1f);
            SurfaceFace slope = data.Faces[0];
            Assert.Greater(slope.Normal.y, 0.7f);
            Assert.Less(slope.Normal.z, 0f);
            Assert.AreEqual(5f, slope.Height, Eps);
            // 坡頂在後緣 (z = +2, y = 3)
            Vector3 top = slope.FromPlane(new Vector2(0f, slope.Height));
            Assert.AreEqual(3f, top.y, Eps);
            Assert.AreEqual(2f, top.z, Eps);
        }

        [Test]
        public void 圖集UV互不重疊且在零到一之間()
        {
            PaintableMeshData data = PaintableMeshBuilder.BuildBox(new Vector3(5f, 1f, 3f), 0.05f);
            var rects = new Rect[data.Faces.Length];
            for (int f = 0; f < data.Faces.Length; f++)
            {
                Vector2 min = Vector2.one;
                Vector2 max = Vector2.zero;
                for (int k = 0; k < 4; k++)
                {
                    Vector2 uv = data.Uvs[f * 4 + k];
                    Assert.GreaterOrEqual(uv.x, 0f);
                    Assert.GreaterOrEqual(uv.y, 0f);
                    Assert.LessOrEqual(uv.x, 1f);
                    Assert.LessOrEqual(uv.y, 1f);
                    min = Vector2.Min(min, uv);
                    max = Vector2.Max(max, uv);
                }
                rects[f] = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            }
            for (int i = 0; i < rects.Length; i++)
            {
                for (int j = i + 1; j < rects.Length; j++)
                {
                    Assert.IsFalse(rects[i].Overlaps(rects[j]), $"面 {i} 與面 {j} 的 UV 重疊");
                }
            }
        }

        [Test]
        public void 塗色網格外擴後仍不侵入鄰面槽位()
        {
            PaintableMeshData data = PaintableMeshBuilder.BuildBox(new Vector3(2f, 2f, 2f), 0.1f);
            var paintRects = new Rect[data.Faces.Length];
            for (int f = 0; f < data.Faces.Length; f++)
            {
                Vector2 min = Vector2.one * 10f;
                Vector2 max = -Vector2.one * 10f;
                for (int k = 0; k < 4; k++)
                {
                    Vector2 uv = data.PaintUvs[f * 4 + k];
                    min = Vector2.Min(min, uv);
                    max = Vector2.Max(max, uv);
                }
                paintRects[f] = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            }
            for (int i = 0; i < paintRects.Length; i++)
            {
                for (int j = i + 1; j < paintRects.Length; j++)
                {
                    Assert.IsFalse(paintRects[i].Overlaps(paintRects[j]), $"擴邊後面 {i} 與面 {j} 重疊");
                }
            }
        }

        [Test]
        public void 兩向texel密度一致()
        {
            PaintableMeshData data = PaintableMeshBuilder.BuildBox(new Vector3(8f, 1f, 2f), 0.1f);
            float metresPerUvX = data.AtlasWidthPx * data.TexelSize;
            float metresPerUvY = data.AtlasHeightPx * data.TexelSize;
            // 頂面:沿 U 走完 8m 的 UV 差 × 圖集寬(公尺)必須 = 8
            SurfaceFace top = data.Faces[0];
            Vector2 uv0 = data.Uvs[0];
            Vector2 uv1 = data.Uvs[1];
            Vector2 uv3 = data.Uvs[3];
            Assert.AreEqual(top.Width, Mathf.Abs(uv1.x - uv0.x) * metresPerUvX, 0.01f);
            Assert.AreEqual(top.Height, Mathf.Abs(uv3.y - uv0.y) * metresPerUvY, 0.01f);
        }

        [Test]
        public void 超大表面自動放大texel不超過上限()
        {
            PaintableMeshData data = PaintableMeshBuilder.BuildBox(new Vector3(300f, 1f, 300f), 0.05f,
                PaintableMeshBuilder.BoxFaces.Top);
            Assert.LessOrEqual(data.AtlasWidthPx, PaintableMeshBuilder.MaxResolution);
            Assert.LessOrEqual(data.AtlasHeightPx, PaintableMeshBuilder.MaxResolution);
            Assert.Greater(data.TexelSize, 0.05f);
        }
    }
}

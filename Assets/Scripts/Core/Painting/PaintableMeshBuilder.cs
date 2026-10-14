using System;
using System.Collections.Generic;
using UnityEngine;

namespace SplatoonC.Core.Painting
{
    // 產生器輸出:渲染網格(每面獨立、UV 不重疊的圖集)+ 塗色網格(每面往外擴 bleed 的版本)+ 面清單。
    // 塗色網格只給 InkSplat pass 畫進 ink map:擴邊讓圖集縫隙內也有墨,雙線性取樣到邊緣才不會露出底色細線。
    public sealed class PaintableMeshData
    {
        public Vector3[] Vertices;
        public Vector3[] Normals;
        public Vector2[] Uvs;
        public int[] Triangles;

        public Vector3[] PaintVertices;
        public Vector3[] PaintNormals;
        public Vector2[] PaintUvs;
        public int[] PaintTriangles;

        // 局部空間的面(順序與建構時的面規格相同)
        public SurfaceFace[] Faces;

        // ink map 解析度(像素);圖集寬高 = 像素 × TexelSize,u、v 兩向的公尺/像素完全一致
        public int AtlasWidthPx;
        public int AtlasHeightPx;
        public float TexelSize;
    }

    // 灰盒關卡用的可塗網格產生器(純邏輯,可測):方塊與斜坡。
    // 每面在圖集裡佔獨立槽位並留 padding,整張圖同一個 texel 密度(公尺/像素)。
    public static class PaintableMeshBuilder
    {
        // 面與面之間的留白(像素):必須 ≥ 2 × BleedTexels,擴邊才不會畫進鄰面
        public const int PaddingTexels = 4;
        public const int BleedTexels = 2;
        public const int MaxResolution = 2048;

        [Flags]
        public enum BoxFaces
        {
            None = 0,
            Top = 1,
            Bottom = 2,
            PosX = 4,
            NegX = 8,
            PosZ = 16,
            NegZ = 32,
            All = Top | Bottom | PosX | NegX | PosZ | NegZ,
        }

        // 中心樞紐的方塊。size = 全尺寸(公尺)。
        public static PaintableMeshData BuildBox(Vector3 size, float texelSize, BoxFaces faces = BoxFaces.All)
        {
            float hx = size.x * 0.5f;
            float hy = size.y * 0.5f;
            float hz = size.z * 0.5f;
            var specs = new List<SurfaceFace>(6);
            // 每面以「中心 + 法線 + U 方向」定義,V = cross(N, U)——保證 cross(U, V) = N(朝外)
            if ((faces & BoxFaces.Top) != 0) specs.Add(Face(new Vector3(0f, hy, 0f), Vector3.up, Vector3.right, size.x, size.z));
            if ((faces & BoxFaces.PosX) != 0) specs.Add(Face(new Vector3(hx, 0f, 0f), Vector3.right, Vector3.back, size.z, size.y));
            if ((faces & BoxFaces.NegX) != 0) specs.Add(Face(new Vector3(-hx, 0f, 0f), Vector3.left, Vector3.forward, size.z, size.y));
            if ((faces & BoxFaces.PosZ) != 0) specs.Add(Face(new Vector3(0f, 0f, hz), Vector3.forward, Vector3.right, size.x, size.y));
            if ((faces & BoxFaces.NegZ) != 0) specs.Add(Face(new Vector3(0f, 0f, -hz), Vector3.back, Vector3.left, size.x, size.y));
            if ((faces & BoxFaces.Bottom) != 0) specs.Add(Face(new Vector3(0f, -hy, 0f), Vector3.down, Vector3.right, size.x, size.z));
            return Build(specs, texelSize);
        }

        // 底面中心樞紐的斜坡:x 寬、y 高、z 長;坡面從 (z = -len/2, y = 0) 升到 (z = +len/2, y = h)。
        public static PaintableMeshData BuildWedge(Vector3 size, float texelSize)
        {
            float hx = size.x * 0.5f;
            float h = size.y;
            float hz = size.z * 0.5f;
            var specs = new List<SurfaceFace>(5);

            // 坡面:U = -X,V 沿坡往上
            Vector3 up = new Vector3(0f, h, size.z);
            float slopeLength = up.magnitude;
            Vector3 slopeOrigin = new Vector3(hx, 0f, -hz);
            specs.Add(SurfaceFace.Create(slopeOrigin, Vector3.left, up, size.x, slopeLength));
            // 背面(+Z 直立)
            specs.Add(Face(new Vector3(0f, h * 0.5f, hz), Vector3.forward, Vector3.right, size.x, h));
            // 兩側直角三角形:直角在底部後緣
            specs.Add(SurfaceFace.Create(new Vector3(hx, 0f, hz), Vector3.back, Vector3.up, size.z, h, SurfaceFaceShape.Triangle));
            specs.Add(SurfaceFace.Create(new Vector3(-hx, 0f, hz), Vector3.up, Vector3.back, h, size.z, SurfaceFaceShape.Triangle));
            // 底面
            specs.Add(Face(new Vector3(0f, 0f, 0f), Vector3.down, Vector3.right, size.x, size.z));
            return Build(specs, texelSize);
        }

        private static SurfaceFace Face(Vector3 center, Vector3 normal, Vector3 uDir, float uLength, float vLength)
        {
            Vector3 vDir = Vector3.Cross(normal, uDir).normalized;
            Vector3 origin = center - uDir * (uLength * 0.5f) - vDir * (vLength * 0.5f);
            return SurfaceFace.Create(origin, uDir, vDir, uLength, vLength);
        }

        private struct Slot
        {
            public float X;
            public float Y;
        }

        public static PaintableMeshData Build(IList<SurfaceFace> faces, float texelSize)
        {
            if (faces == null || faces.Count == 0)
            {
                throw new ArgumentException("至少需要一個面", nameof(faces));
            }
            texelSize = Mathf.Max(texelSize, 0.005f);

            // 圖集尺寸隨 texel 放大收斂:超過 MaxResolution 就把 texel 放大重排
            Slot[] slots;
            int widthPx;
            int heightPx;
            while (true)
            {
                slots = Pack(faces, texelSize, out float atlasWidth, out float atlasHeight);
                widthPx = Mathf.Max(1, Mathf.CeilToInt(atlasWidth / texelSize - 1e-3f));
                heightPx = Mathf.Max(1, Mathf.CeilToInt(atlasHeight / texelSize - 1e-3f));
                if (widthPx <= MaxResolution && heightPx <= MaxResolution)
                {
                    break;
                }
                texelSize *= Mathf.Max(widthPx, heightPx) / (float)MaxResolution * 1.02f;
            }

            float atlasW = widthPx * texelSize;
            float atlasH = heightPx * texelSize;
            float pad = PaddingTexels * texelSize;
            float bleed = BleedTexels * texelSize;

            var data = new PaintableMeshData
            {
                Faces = new SurfaceFace[faces.Count],
                AtlasWidthPx = widthPx,
                AtlasHeightPx = heightPx,
                TexelSize = texelSize,
            };

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            var paintVertices = new List<Vector3>();
            var paintNormals = new List<Vector3>();
            var paintUvs = new List<Vector2>();
            var paintTriangles = new List<int>();

            for (int i = 0; i < faces.Count; i++)
            {
                SurfaceFace face = faces[i];
                data.Faces[i] = face;
                // 面在圖集中的左下角(公尺)
                float baseU = slots[i].X + pad;
                float baseV = slots[i].Y + pad;

                int start = vertices.Count;
                if (face.Shape == SurfaceFaceShape.Triangle)
                {
                    AddVertex(face, 0f, 0f, baseU, baseV, atlasW, atlasH, vertices, normals, uvs);
                    AddVertex(face, face.Width, 0f, baseU, baseV, atlasW, atlasH, vertices, normals, uvs);
                    AddVertex(face, 0f, face.Height, baseU, baseV, atlasW, atlasH, vertices, normals, uvs);
                    triangles.Add(start);
                    triangles.Add(start + 1);
                    triangles.Add(start + 2);
                }
                else
                {
                    AddQuad(face, 0f, 0f, face.Width, face.Height, baseU, baseV, atlasW, atlasH,
                        vertices, normals, uvs, triangles);
                }

                // 塗色網格:外擴 bleed(三角形用外接矩形——槽位內三角形外的 texel 不會被渲染網格取樣,多塗無害)
                AddQuad(face, -bleed, -bleed, face.Width + bleed, face.Height + bleed, baseU, baseV, atlasW, atlasH,
                    paintVertices, paintNormals, paintUvs, paintTriangles);
            }

            data.Vertices = vertices.ToArray();
            data.Normals = normals.ToArray();
            data.Uvs = uvs.ToArray();
            data.Triangles = triangles.ToArray();
            data.PaintVertices = paintVertices.ToArray();
            data.PaintNormals = paintNormals.ToArray();
            data.PaintUvs = paintUvs.ToArray();
            data.PaintTriangles = paintTriangles.ToArray();
            return data;
        }

        private static void AddVertex(SurfaceFace face, float x, float y, float baseU, float baseV,
            float atlasW, float atlasH, List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs)
        {
            vertices.Add(face.FromPlane(new Vector2(x, y)));
            normals.Add(face.Normal);
            uvs.Add(new Vector2((baseU + x) / atlasW, (baseV + y) / atlasH));
        }

        // 平面座標 [x0,x1]×[y0,y1] 的四邊形;三角形繞序讓 cross(b-a, c-a) = 面法線(Unity 正面)
        private static void AddQuad(SurfaceFace face, float x0, float y0, float x1, float y1,
            float baseU, float baseV, float atlasW, float atlasH,
            List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles)
        {
            int start = vertices.Count;
            AddVertex(face, x0, y0, baseU, baseV, atlasW, atlasH, vertices, normals, uvs);
            AddVertex(face, x1, y0, baseU, baseV, atlasW, atlasH, vertices, normals, uvs);
            AddVertex(face, x1, y1, baseU, baseV, atlasW, atlasH, vertices, normals, uvs);
            AddVertex(face, x0, y1, baseU, baseV, atlasW, atlasH, vertices, normals, uvs);
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
        }

        // 貨架式裝箱:依高度遞減排列,一列放滿換列。目標寬度 ≈ 總面積平方根(近正方形圖集)。
        private static Slot[] Pack(IList<SurfaceFace> faces, float texelSize, out float atlasWidth, out float atlasHeight)
        {
            float pad = PaddingTexels * texelSize;
            int count = faces.Count;
            var order = new int[count];
            float totalArea = 0f;
            float maxSlotWidth = 0f;
            for (int i = 0; i < count; i++)
            {
                order[i] = i;
                float w = faces[i].Width + pad * 2f;
                float h = faces[i].Height + pad * 2f;
                totalArea += w * h;
                maxSlotWidth = Mathf.Max(maxSlotWidth, w);
            }
            Array.Sort(order, (a, b) =>
            {
                int byHeight = faces[b].Height.CompareTo(faces[a].Height);
                return byHeight != 0 ? byHeight : a.CompareTo(b);
            });

            float targetWidth = Mathf.Max(maxSlotWidth, Mathf.Sqrt(totalArea));
            var slots = new Slot[count];
            float cursorX = 0f;
            float cursorY = 0f;
            float rowHeight = 0f;
            atlasWidth = 0f;
            foreach (int index in order)
            {
                float w = faces[index].Width + pad * 2f;
                float h = faces[index].Height + pad * 2f;
                if (cursorX > 0f && cursorX + w > targetWidth + 1e-4f)
                {
                    cursorY += rowHeight;
                    cursorX = 0f;
                    rowHeight = 0f;
                }
                slots[index] = new Slot { X = cursorX, Y = cursorY };
                cursorX += w;
                rowHeight = Mathf.Max(rowHeight, h);
                atlasWidth = Mathf.Max(atlasWidth, cursorX);
            }
            atlasHeight = cursorY + rowHeight;
            return slots;
        }

        // 把產生器資料寫進 Unity Mesh(渲染用與塗色用各一個)。
        public static Mesh ToRenderMesh(PaintableMeshData data, string name)
        {
            var mesh = new Mesh { name = name };
            if (data.Vertices.Length > 65000)
            {
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }
            mesh.vertices = data.Vertices;
            mesh.normals = data.Normals;
            mesh.uv = data.Uvs;
            mesh.triangles = data.Triangles;
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        public static Mesh ToPaintMesh(PaintableMeshData data, string name)
        {
            var mesh = new Mesh { name = name };
            mesh.vertices = data.PaintVertices;
            mesh.normals = data.PaintNormals;
            mesh.uv = data.PaintUvs;
            mesh.triangles = data.PaintTriangles;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}

using System;
using UnityEngine;

namespace SplatoonC.Core.Painting
{
    public enum SurfaceFaceShape : byte
    {
        Rect = 0,
        // 直角在 Origin,兩股沿 AxisU、AxisV
        Triangle = 1,
    }

    // 可塗表面上的一個平面區塊:以 Origin + AxisU/AxisV(單位向量)張成,法線 = cross(U, V)。
    // 同一個 struct 兼用局部空間(網格產生器輸出)與世界空間(PaintableSurface 初始化時轉換)。
    // 平面座標 (x, y) = 沿 U、V 的公尺距離,範圍 [0, Width] × [0, Height]——歸屬網格直接吃這個座標。
    [Serializable]
    public struct SurfaceFace
    {
        public Vector3 Origin;
        public Vector3 AxisU;
        public Vector3 AxisV;
        public Vector3 Normal;
        public float Width;
        public float Height;
        public SurfaceFaceShape Shape;

        public float Area => Shape == SurfaceFaceShape.Triangle ? Width * Height * 0.5f : Width * Height;

        public static SurfaceFace Create(Vector3 origin, Vector3 axisU, Vector3 axisV,
            float width, float height, SurfaceFaceShape shape = SurfaceFaceShape.Rect)
        {
            Vector3 u = axisU.normalized;
            Vector3 v = axisV.normalized;
            return new SurfaceFace
            {
                Origin = origin,
                AxisU = u,
                AxisV = v,
                Normal = Vector3.Cross(u, v).normalized,
                Width = width,
                Height = height,
                Shape = shape,
            };
        }

        public Vector2 ToPlane(Vector3 point)
        {
            Vector3 d = point - Origin;
            return new Vector2(Vector3.Dot(d, AxisU), Vector3.Dot(d, AxisV));
        }

        public Vector3 FromPlane(Vector2 planePoint)
        {
            return Origin + AxisU * planePoint.x + AxisV * planePoint.y;
        }

        // 正值 = 點在法線那一側(面的正面)。
        public float SignedDistance(Vector3 point)
        {
            return Vector3.Dot(point - Origin, Normal);
        }

        public bool Contains(Vector2 planePoint, float margin)
        {
            if (planePoint.x < -margin || planePoint.y < -margin
                || planePoint.x > Width + margin || planePoint.y > Height + margin)
            {
                return false;
            }
            if (Shape == SurfaceFaceShape.Triangle && Width > 0f && Height > 0f)
            {
                // 斜邊 x/W + y/H = 1;邊距換算成沿斜邊法線的距離
                float hyp = Mathf.Sqrt(Width * Width + Height * Height);
                float excess = (planePoint.x / Width + planePoint.y / Height - 1f) * (Width * Height / hyp);
                return excess <= margin;
            }
            return true;
        }

        // 轉到另一個空間(通常 localToWorld)。非等比縮放下軸長不同,寬高各自乘上對應軸的伸縮量。
        public SurfaceFace Transform(Matrix4x4 matrix)
        {
            Vector3 u = matrix.MultiplyVector(AxisU);
            Vector3 v = matrix.MultiplyVector(AxisV);
            float uScale = u.magnitude;
            float vScale = v.magnitude;
            SurfaceFace result = Create(matrix.MultiplyPoint3x4(Origin), u, v, Width * uScale, Height * vScale, Shape);
            // 鏡像縮放(負 scale)會讓 cross(U,V) 反向:以法線的逆轉置結果校正方向
            Vector3 expected = matrix.inverse.transpose.MultiplyVector(Normal);
            if (Vector3.Dot(result.Normal, expected) < 0f)
            {
                result.Normal = -result.Normal;
            }
            return result;
        }

        // 單一平面 mesh(Unity Plane/Quad、薄方塊)的退路:沿用 PlanarSurfaceMap 的最薄軸規則,
        // 法線方向取 mesh 自己的法線(normalHint)——Plane 朝 +Y、Quad 朝 -Z。
        public static SurfaceFace FromBounds(Vector3 boundsMin, Vector3 boundsSize, Vector3 normalHint)
        {
            PlanarSurfaceMap map = PlanarSurfaceMap.FromBounds(boundsMin, boundsSize);
            Vector3 u;
            Vector3 v;
            Vector3 thin;
            switch (map.NormalAxis)
            {
                case 0: u = Vector3.forward; v = Vector3.up; thin = Vector3.right; break;
                case 1: u = Vector3.right; v = Vector3.forward; thin = Vector3.up; break;
                default: u = Vector3.right; v = Vector3.up; thin = Vector3.forward; break;
            }
            // 平面位在最薄軸的中點(厚度 0 的 mesh 就是那個座標)
            Vector3 center = boundsMin + boundsSize * 0.5f;
            float thinCoord = Vector3.Dot(center, thin);
            var origin = new Vector3(
                thin.x != 0f ? thinCoord : boundsMin.x,
                thin.y != 0f ? thinCoord : boundsMin.y,
                thin.z != 0f ? thinCoord : boundsMin.z);
            SurfaceFace face = Create(origin, u, v, map.PlaneSize.x, map.PlaneSize.y);
            if (normalHint.sqrMagnitude > 0f && Vector3.Dot(face.Normal, normalHint) < 0f)
            {
                // 法線要朝 mesh 正面:交換 U/V 讓 cross 反向,平面座標系仍為右手連續
                face = Create(origin, v, u, map.PlaneSize.y, map.PlaneSize.x);
            }
            return face;
        }
    }
}

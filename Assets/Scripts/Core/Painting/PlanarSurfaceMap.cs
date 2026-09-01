using UnityEngine;

namespace SplatoonC.Core.Painting
{
    // 平面表面的局部 3D → 2D 映射:從 mesh bounds 挑「最薄軸」當法線,其餘兩軸攤成平面座標。
    // 平面性假設:可塗面(地面/牆/斜坡)都是單一平面 mesh;非平面 mesh 需另行設計。
    public readonly struct PlanarSurfaceMap
    {
        // 法線軸:0=X、1=Y、2=Z。
        public readonly int NormalAxis;
        public readonly Vector2 PlaneMin;
        public readonly Vector2 PlaneSize;

        private PlanarSurfaceMap(int normalAxis, Vector2 planeMin, Vector2 planeSize)
        {
            NormalAxis = normalAxis;
            PlaneMin = planeMin;
            PlaneSize = planeSize;
        }

        public static PlanarSurfaceMap FromBounds(Vector3 boundsMin, Vector3 boundsSize)
        {
            // 並列最小時取先到者(X→Y→Z),決定性。
            int normalAxis = 0;
            float minExtent = boundsSize.x;
            if (boundsSize.y < minExtent)
            {
                minExtent = boundsSize.y;
                normalAxis = 1;
            }
            if (boundsSize.z < minExtent)
            {
                normalAxis = 2;
            }

            Vector2 planeMin;
            Vector2 planeSize;
            switch (normalAxis)
            {
                case 0:
                    planeMin = new Vector2(boundsMin.z, boundsMin.y);
                    planeSize = new Vector2(boundsSize.z, boundsSize.y);
                    break;
                case 1:
                    planeMin = new Vector2(boundsMin.x, boundsMin.z);
                    planeSize = new Vector2(boundsSize.x, boundsSize.z);
                    break;
                default:
                    planeMin = new Vector2(boundsMin.x, boundsMin.y);
                    planeSize = new Vector2(boundsSize.x, boundsSize.y);
                    break;
            }
            return new PlanarSurfaceMap(normalAxis, planeMin, planeSize);
        }

        public Vector2 ToPlane(Vector3 localPoint)
        {
            switch (NormalAxis)
            {
                case 0: return new Vector2(localPoint.z, localPoint.y);
                case 1: return new Vector2(localPoint.x, localPoint.z);
                default: return new Vector2(localPoint.x, localPoint.y);
            }
        }
    }
}

using UnityEngine;

namespace SplatoonC.Core.Match.Ranked
{
    // 目標區域的俯視多邊形(XZ 平面,頂點順/逆時針皆可;允許凹多邊形,不可自交)。
    // 真格區域的範圍判定、面積、中心、邊界取樣都用它;純數學,EditMode 測試。
    public static class ZonePolygon
    {
        // 點在多邊形內(射線交點奇偶;邊上的點視為內部與否不保證,區域以格中心判定不受影響)
        public static bool Contains(Vector2[] polygon, float x, float z)
        {
            if (polygon == null || polygon.Length < 3)
            {
                return false;
            }
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[j];
                if ((a.y > z) != (b.y > z))
                {
                    float crossX = a.x + (z - a.y) * (b.x - a.x) / (b.y - a.y);
                    if (x < crossX)
                    {
                        inside = !inside;
                    }
                }
            }
            return inside;
        }

        // 有號面積(鞋帶公式);取絕對值 = 面積(m²)
        public static float SignedArea(Vector2[] polygon)
        {
            if (polygon == null || polygon.Length < 3)
            {
                return 0f;
            }
            float sum = 0f;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                sum += polygon[j].x * polygon[i].y - polygon[i].x * polygon[j].y;
            }
            return sum * 0.5f;
        }

        public static float Area(Vector2[] polygon)
        {
            return Mathf.Abs(SignedArea(polygon));
        }

        // 面積重心(退化多邊形退回頂點平均)
        public static Vector2 Centroid(Vector2[] polygon)
        {
            if (polygon == null || polygon.Length == 0)
            {
                return Vector2.zero;
            }
            float area = SignedArea(polygon);
            if (Mathf.Abs(area) < 1e-6f)
            {
                Vector2 mean = Vector2.zero;
                for (int i = 0; i < polygon.Length; i++)
                {
                    mean += polygon[i];
                }
                return mean / polygon.Length;
            }
            float cx = 0f;
            float cz = 0f;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                float cross = polygon[j].x * polygon[i].y - polygon[i].x * polygon[j].y;
                cx += (polygon[j].x + polygon[i].x) * cross;
                cz += (polygon[j].y + polygon[i].y) * cross;
            }
            float k = 1f / (6f * area);
            return new Vector2(cx * k, cz * k);
        }

        // 周長(m)
        public static float Perimeter(Vector2[] polygon)
        {
            if (polygon == null || polygon.Length < 2)
            {
                return 0f;
            }
            float sum = 0f;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                sum += Vector2.Distance(polygon[i], polygon[j]);
            }
            return sum;
        }

        // 點到多邊形邊界的最短距離(m)
        public static float DistanceToEdge(Vector2[] polygon, float x, float z)
        {
            if (polygon == null || polygon.Length < 2)
            {
                return float.MaxValue;
            }
            var p = new Vector2(x, z);
            float best = float.MaxValue;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                Vector2 a = polygon[j];
                Vector2 ab = polygon[i] - a;
                float t = ab.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
                best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
            }
            return best;
        }

        // 軸對齊矩形(繞 Y 旋轉 yawDeg)轉成 4 頂點多邊形
        public static Vector2[] Box(Vector2 center, Vector2 size, float yawDeg)
        {
            float rad = yawDeg * Mathf.Deg2Rad;
            float c = Mathf.Cos(rad);
            float s = Mathf.Sin(rad);
            float hx = size.x * 0.5f;
            float hz = size.y * 0.5f;
            var corners = new[] { new Vector2(-hx, -hz), new Vector2(hx, -hz), new Vector2(hx, hz), new Vector2(-hx, hz) };
            for (int i = 0; i < corners.Length; i++)
            {
                Vector2 v = corners[i];
                // Unity 的 yaw 正值 = 從上往下看順時針:x' = x·cos + z·sin、z' = −x·sin + z·cos
                corners[i] = center + new Vector2(v.x * c + v.y * s, -v.x * s + v.y * c);
            }
            return corners;
        }

        // 切角矩形(八角形):S3 區域常見形狀
        public static Vector2[] ChamferedBox(Vector2 center, Vector2 size, float chamfer)
        {
            float hx = size.x * 0.5f;
            float hz = size.y * 0.5f;
            float k = Mathf.Clamp(chamfer, 0f, Mathf.Min(hx, hz));
            return new[]
            {
                center + new Vector2(-hx + k, -hz), center + new Vector2(hx - k, -hz),
                center + new Vector2(hx, -hz + k), center + new Vector2(hx, hz - k),
                center + new Vector2(hx - k, hz), center + new Vector2(-hx + k, hz),
                center + new Vector2(-hx, hz - k), center + new Vector2(-hx, -hz + k),
            };
        }

        // 點對稱(繞原點 180°):S3 場地都是點對稱,第二個區域由第一個鏡像
        public static Vector2[] PointMirror(Vector2[] polygon)
        {
            var result = new Vector2[polygon.Length];
            for (int i = 0; i < polygon.Length; i++)
            {
                result[i] = -polygon[i];
            }
            return result;
        }
    }
}

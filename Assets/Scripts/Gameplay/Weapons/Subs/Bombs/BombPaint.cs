using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Painting;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 炸彈的特殊塗墨。
    // PaintDisc:「只塗貼住的那一面」的平面圓盤 + 交叉塗墨(Suction Bomb:PaintRadius 5.0 + CrossPaintRadius 2.5)。
    // 專案塗墨是球形(InkPainter.PaintSphere:球與各面的交圓),一顆 r 5.0 的球會把牆腳地面也塗 5.0。
    // 改成:以小筆刷(stampRadius)排滿貼住的面(DiscStampLayout)→ 該面得到半徑 ≈ radius 的圓盤,與它相交的其他面
    // 只在筆刷半徑內被帶到;再以貼住點為球心塗一顆 crossRadius(S3 交叉塗墨:貼住點附近相交的面)。
    // 筆刷在面外的部分(越過牆邊、低於地面)不會塗到背對它的面(PaintableSurface 背面測試)。
    public static class BombPaint
    {
        private static readonly Vector2[] Offsets = new Vector2[64];

        // 回傳新增地盤面積(m²)
        public static float PaintDisc(Vector3 center, Vector3 normal, float radius, float stampRadius,
            float crossRadius, float hardness, in InkSource source)
        {
            if (radius <= 0f)
            {
                return 0f;
            }
            Vector3 n = normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.up;
            Vector3 tangent = Vector3.Cross(n, Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.forward).normalized;
            Vector3 bitangent = Vector3.Cross(n, tangent);
            float stamp = DiscStampLayout.StampRadius(radius, stampRadius);
            int count = DiscStampLayout.Build(radius, stampRadius, Offsets);
            float area = 0f;
            for (int i = 0; i < count; i++)
            {
                Vector3 position = center + tangent * Offsets[i].x + bitangent * Offsets[i].y;
                area += InkPainter.PaintSphere(position, stamp, hardness, source);
            }
            if (crossRadius > 0f)
            {
                area += InkPainter.PaintSphere(center, crossRadius, hardness, source);
            }
            return area;
        }
    }
}

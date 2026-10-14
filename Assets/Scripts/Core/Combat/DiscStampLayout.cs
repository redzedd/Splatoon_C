using UnityEngine;

namespace SplatoonC.Core.Combat
{
    // 「平面圓盤塗墨」的球形筆刷排列(Suction Bomb:只塗黏住的那一面 r 5.0 + 交叉塗墨 2.5,research 03 §2.2)。
    // 專案的塗墨是球形(InkPainter.PaintSphere:球與各面的交圓),一顆 r 5.0 的球會把牆腳地面也塗 5.0。
    // 改成以小球(筆刷半徑 s,Suction 1.25)排滿黏住的面:該面上是半徑 ≈ R 的圓盤,與它相交的其他面(牆腳地面、
    // 側面)只在筆刷半徑內被帶到;交叉塗墨(CrossPaintRadius 2.5)由呼叫端在黏住點另塗一顆(BombPaint.PaintDisc)。
    // 排列:中心一顆 + 同心環(環距 ≤ 1.6 s,最外環在 R − 0.84 s,環上間距 ≤ 0.9 s),外緣在 R 附近起伏(≈ ±0.3 m)。
    public static class DiscStampLayout
    {
        // 寫入 offsets(平面座標,單位同半徑),回傳數量;buffer 不夠就截斷。
        // radius ≤ stampRadius × 1.05:單顆半徑 radius 即可(回傳 1 顆、offset 0,呼叫端用 radius 當筆刷)。
        public static int Build(float radius, float stampRadius, Vector2[] offsets)
        {
            if (offsets == null || offsets.Length == 0 || radius <= 0f)
            {
                return 0;
            }
            offsets[0] = Vector2.zero;
            int count = 1;
            if (stampRadius <= 0f || radius <= stampRadius * 1.05f)
            {
                return count;
            }
            float outer = radius - 0.84f * stampRadius;
            int rings = Mathf.Max(1, Mathf.CeilToInt(outer / (1.6f * stampRadius) - 1e-4f));
            for (int ring = 1; ring <= rings && count < offsets.Length; ring++)
            {
                float distance = outer * ring / rings;
                int n = Mathf.Max(6, Mathf.CeilToInt(2f * Mathf.PI * distance / (0.9f * stampRadius)));
                // 相鄰環錯開半格,縫隙不對齊
                float phase = ring % 2 == 0 ? Mathf.PI / n : 0f;
                for (int i = 0; i < n && count < offsets.Length; i++)
                {
                    float angle = phase + i * 2f * Mathf.PI / n;
                    offsets[count++] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
                }
            }
            return count;
        }

        // 單顆筆刷的半徑(radius 夠小時用 radius 本身;與 Build 的單顆判定一致)
        public static float StampRadius(float radius, float stampRadius)
        {
            return stampRadius <= 0f || radius <= stampRadius * 1.05f ? radius : stampRadius;
        }
    }
}

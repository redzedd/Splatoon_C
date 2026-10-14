using UnityEngine;

namespace SplatoonC.Core.Combat
{
    // 單向球形屏障(S3 Big Bubbler)的幾何純邏輯:從外面打進來的攻擊停在球面上;
    // 起點已在球內的攻擊(敵人走進罩內開槍)不擋、往外打也不擋(research/04 §2.2、JP wiki グレートバリア)。
    public static class SphereBarrierMath
    {
        // point 是否在球內(含球面)
        public static bool Contains(Vector3 center, float radius, Vector3 point)
        {
            return (point - center).sqrMagnitude <= radius * radius;
        }

        // 半徑 castRadius 的球沿 from + dir·s(s ∈ [0, maxDistance],dir 已正規化)移動時,第一次碰到屏障外殼的 s。
        // 起點中心已在屏障內 → 不算進入(false)。碰到時 point = 球面上的接觸點、normal = 朝外法線。
        public static bool Entry(Vector3 center, float radius, Vector3 from, Vector3 dir, float maxDistance,
            float castRadius, out float distance, out Vector3 point, out Vector3 normal)
        {
            distance = 0f;
            point = from;
            normal = -dir;
            if (radius <= 0f || Contains(center, radius, from))
            {
                return false;
            }
            float r = radius + Mathf.Max(0f, castRadius);
            Vector3 m = from - center;
            float b = Vector3.Dot(m, dir);
            float c = m.sqrMagnitude - r * r;
            if (c <= 0f)
            {
                // 投射球已經貼著外殼(中心在殼外、外擴後重疊):往內走才算立刻接觸,往外走不擋
                if (b >= 0f)
                {
                    return false;
                }
                distance = 0f;
            }
            else
            {
                if (b > 0f)
                {
                    return false;
                }
                float disc = b * b - c;
                if (disc < 0f)
                {
                    return false;
                }
                distance = -b - Mathf.Sqrt(disc);
                if (distance > maxDistance)
                {
                    return false;
                }
            }
            Vector3 hitCenter = from + dir * distance;
            Vector3 outward = hitCenter - center;
            normal = outward.sqrMagnitude > 1e-8f ? outward.normalized : -dir;
            point = center + normal * radius;
            return true;
        }

        // 爆炸/範圍攻擊的視線:從 from(爆心)到 to(目標最近點)的線段是否被屏障擋住。
        // 爆心在罩內 → 不擋;爆心在罩外而線段碰到球 → 擋(不論目標在罩內或在另一側)。
        public static bool BlocksSegment(Vector3 center, float radius, Vector3 from, Vector3 to)
        {
            if (radius <= 0f || Contains(center, radius, from))
            {
                return false;
            }
            return SegmentDistanceSq(center, from, to) <= radius * radius;
        }

        // 點到線段的距離平方
        public static float SegmentDistanceSq(Vector3 point, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float lengthSq = ab.sqrMagnitude;
            float t = lengthSq > 1e-10f ? Mathf.Clamp01(Vector3.Dot(point - a, ab) / lengthSq) : 0f;
            return (a + ab * t - point).sqrMagnitude;
        }

        // 球面上離 from 最近的點(from 在球外時);from 在球內回傳 false(罩內的攻擊打不到屏障本身)
        public static bool ClosestOnShell(Vector3 center, float radius, Vector3 from, out Vector3 point)
        {
            Vector3 offset = from - center;
            if (radius <= 0f || offset.sqrMagnitude <= radius * radius)
            {
                point = from;
                return false;
            }
            point = center + offset.normalized * radius;
            return true;
        }
    }
}

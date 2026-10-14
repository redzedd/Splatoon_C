using UnityEngine;

namespace SplatoonC.Core.Combat
{
    // 直立膠囊的最近點(解析解):假人死亡關 collider 後仍要算得出來,爆炸/龍捲風的距離判定才不會跳。
    public static class CapsuleMath
    {
        // segmentA/B = 膠囊軸線兩端(半球圓心),radius = 半徑;from 在膠囊內時最近點就是它自己。
        public static Vector3 ClosestPoint(Vector3 segmentA, Vector3 segmentB, float radius, Vector3 from)
        {
            Vector3 axis = segmentB - segmentA;
            float lengthSq = axis.sqrMagnitude;
            float t = lengthSq > 0.00000001f
                ? Mathf.Clamp01(Vector3.Dot(from - segmentA, axis) / lengthSq)
                : 0f;
            Vector3 onAxis = segmentA + axis * t;
            Vector3 offset = from - onAxis;
            float distance = offset.magnitude;
            if (distance <= radius || distance < 0.00001f)
            {
                return from;
            }
            return onAxis + offset / distance * radius;
        }
    }
}

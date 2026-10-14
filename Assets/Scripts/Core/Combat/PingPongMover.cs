using UnityEngine;

namespace SplatoonC.Core.Combat
{
    // 動態假人:在 a、b 之間以定速來回。direction = +1 表示正朝 b 走。
    public static class PingPongMover
    {
        public static Vector3 Position(Vector3 a, Vector3 b, float speed, float time, out int direction)
        {
            direction = 1;
            float length = Vector3.Distance(a, b);
            if (length <= 0.0001f || speed <= 0f)
            {
                return a;
            }
            float cycle = Mathf.Repeat(speed * time, 2f * length);
            float t;
            if (cycle <= length)
            {
                t = cycle / length;
            }
            else
            {
                t = 2f - cycle / length;
                direction = -1;
            }
            return Vector3.Lerp(a, b, t);
        }
    }
}

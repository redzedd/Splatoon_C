using UnityEngine;

namespace SplatoonC.Core.Animation
{
    // 程序動畫用的小工具:指數平滑(與幀率無關)與阻尼彈簧(觸手甩動、落地蹲、後座力)。
    // 彈簧用半隱式 Euler + 固定子步長,低幀率(大 dt)也不會發散。
    public static class SpringMotion
    {
        // 子步長上限(秒):剛性 ≤ 400、阻尼 ≤ 40 時 1/120 秒內穩定
        public const float MaxSubStep = 1f / 120f;

        // 以「半衰期」表示的指數平滑:每經過 halfLife 秒,與目標的差距減半
        public static float Damp(float current, float target, float halfLife, float deltaTime)
        {
            if (halfLife <= 0f)
            {
                return target;
            }
            return target + (current - target) * Mathf.Pow(0.5f, deltaTime / halfLife);
        }

        public static Vector3 Damp(Vector3 current, Vector3 target, float halfLife, float deltaTime)
        {
            if (halfLife <= 0f)
            {
                return target;
            }
            float k = Mathf.Pow(0.5f, deltaTime / halfLife);
            return target + (current - target) * k;
        }

        // 升降不同速度的平滑(例:瞄準姿勢 3F 內舉起、放下慢慢收)
        public static float DampAsymmetric(float current, float target, float riseHalfLife, float fallHalfLife,
            float deltaTime)
        {
            return Damp(current, target, target > current ? riseHalfLife : fallHalfLife, deltaTime);
        }

        // 阻尼彈簧(一維):value 往 target 拉,stiffness = k(1/s²)、damping = c(1/s)
        public static void Step(ref float value, ref float velocity, float target, float stiffness, float damping,
            float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }
            int steps = Mathf.Clamp(Mathf.CeilToInt(deltaTime / MaxSubStep), 1, 16);
            float h = deltaTime / steps;
            for (int i = 0; i < steps; i++)
            {
                velocity += ((target - value) * stiffness - velocity * damping) * h;
                value += velocity * h;
            }
        }

        public static void Step(ref Vector3 value, ref Vector3 velocity, Vector3 target, float stiffness,
            float damping, float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }
            int steps = Mathf.Clamp(Mathf.CeilToInt(deltaTime / MaxSubStep), 1, 16);
            float h = deltaTime / steps;
            for (int i = 0; i < steps; i++)
            {
                velocity += ((target - value) * stiffness - velocity * damping) * h;
                value += velocity * h;
            }
        }
    }
}

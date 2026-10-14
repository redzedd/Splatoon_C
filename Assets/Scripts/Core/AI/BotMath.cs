using UnityEngine;

namespace SplatoonC.Core.AI
{
    // bot 的純數學:移動輸入換算、瞄準角度、提前量、轉向限速。全部可測,不碰場景。
    public static class BotMath
    {
        // 世界方向 → 相對視線 yaw 的移動輸入(x = 右、y = 前),長度 = magnitude(0~1)
        public static Vector2 MoveInputFor(Vector3 worldDirection, float viewYawDeg, float magnitude = 1f)
        {
            worldDirection.y = 0f;
            if (worldDirection.sqrMagnitude < 1e-6f || magnitude <= 0f)
            {
                return Vector2.zero;
            }
            Vector3 local = Quaternion.Euler(0f, -viewYawDeg, 0f) * worldDirection.normalized;
            return new Vector2(local.x, local.z) * Mathf.Clamp01(magnitude);
        }

        // 從 from 看向 to 的 yaw/pitch(度;pitch 正值 = 往下看,與相機慣例一致)
        public static void AnglesTo(Vector3 from, Vector3 to, out float yawDeg, out float pitchDeg)
        {
            Vector3 d = to - from;
            float flat = new Vector2(d.x, d.z).magnitude;
            yawDeg = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            pitchDeg = -Mathf.Atan2(d.y, Mathf.Max(flat, 1e-4f)) * Mathf.Rad2Deg;
        }

        // 直線彈的提前量:迭代兩次讓飛行時間收斂
        public static Vector3 LeadTarget(Vector3 shooter, Vector3 target, Vector3 targetVelocity, float projectileSpeed)
        {
            if (projectileSpeed <= 0.01f)
            {
                return target;
            }
            Vector3 predicted = target;
            for (int i = 0; i < 2; i++)
            {
                float t = Vector3.Distance(shooter, predicted) / projectileSpeed;
                predicted = target + targetVelocity * t;
            }
            return predicted;
        }

        // 限速轉向(度/秒),處理 ±180 接縫
        public static float TurnTowards(float current, float target, float maxDegPerSecond, float dt)
        {
            return Mathf.MoveTowardsAngle(current, target, maxDegPerSecond * dt);
        }

        // 兩個方向(yaw/pitch)之間的角距(度),開火判定用
        public static float AngularError(float yawA, float pitchA, float yawB, float pitchB)
        {
            float dy = Mathf.DeltaAngle(yawA, yawB);
            float dp = pitchB - pitchA;
            return Mathf.Sqrt(dy * dy + dp * dp);
        }

        // 塗地視線:要讓墨彈落在前方 distance 公尺的地面,眼高 eyeHeight 時的俯角(度)
        public static float PitchToGround(float eyeHeight, float distance)
        {
            return Mathf.Atan2(eyeHeight, Mathf.Max(distance, 0.1f)) * Mathf.Rad2Deg;
        }
    }
}

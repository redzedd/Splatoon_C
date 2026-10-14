using UnityEngine;

namespace SplatoonC.Core.Combat
{
    // 爆炸擊退的純邏輯(S3 KnockBackParam {Accel, Bias, Distance};research 07 §2.7、DESIGN_REFERENCE §4.3)。
    // S3 只公開了參數值(必殺炸彈 700 / 0.8 / 12 u;Burst / Auto / Fizzy 470 / 0.8 / 8–9 u;擊倒爆墨 0),沒有公式。
    // [EST] 模型:水平衝量 ∝ Accel × (1 − d / Distance),方向 = 爆心 → 目標(水平),外加一點向上;
    // 之後水平外力在地面以 S3 基礎加速度(0.01 u/F² = 36 m/s²)減速、空中減半,重力照常。
    // 調校目標:700 的炸彈在 3 m 處把人推開 ≈ 1.75 m(驗收 1.5–2 m)。Bias 的意義 JP wiki 也不明,未使用。
    public static class KnockbackModel
    {
        // 每 1 單位 Accel × (1 − d/R) 給多少水平速度(m/s)[EST]:700 × 0.75 × 0.0171 ≈ 9.0 m/s → 推 ≈ 1.75 m
        public const float SpeedPerAccel = 0.0171f;

        // 向上分量 = 水平速度 × 此比例 [EST](小跳 ≈ 0.17 s,讓人「被炸飛一下」但不會飛上台階)
        public const float UpwardRatio = 0.1f;

        // 地面減速度(m/s²):S3 人形基礎加速度 0.01 u/F² × 3600
        public const float GroundDeceleration = 36f;

        // 空中減速度(m/s²)[EST]:空中摩擦較小
        public const float AirDeceleration = 18f;

        // 爆心 center 對目標最近點 targetPoint 的擊退速度變化(m/s)。超出 range 或 accel ≤ 0 → 零。
        public static Vector3 Impulse(Vector3 center, Vector3 targetPoint, float accel, float range)
        {
            if (accel <= 0f || range <= 0f)
            {
                return Vector3.zero;
            }
            Vector3 offset = targetPoint - center;
            float distance = offset.magnitude;
            if (distance >= range)
            {
                return Vector3.zero;
            }
            float speed = accel * (1f - distance / range) * SpeedPerAccel;
            Vector3 horizontal = new Vector3(offset.x, 0f, offset.z);
            if (horizontal.sqrMagnitude < 1e-6f)
            {
                // 爆心正上/正下:只給向上(不挑方向)
                return Vector3.up * (speed * UpwardRatio);
            }
            horizontal.Normalize();
            return horizontal * speed + Vector3.up * (speed * UpwardRatio);
        }

        // 水平外力速度一幀的衰減(等減速,不會反向)
        public static Vector3 DecayHorizontal(Vector3 velocity, bool grounded, float deltaTime)
        {
            velocity.y = 0f;
            float speed = velocity.magnitude;
            if (speed <= 1e-5f)
            {
                return Vector3.zero;
            }
            float decel = grounded ? GroundDeceleration : AirDeceleration;
            float next = speed - decel * deltaTime;
            return next <= 0f ? Vector3.zero : velocity * (next / speed);
        }

        // 平地上被推的總水平距離(公尺;60 Hz 模擬,含小跳的空中段)。測試與 bot 預估用。
        public static float PredictFlatDistance(Vector3 impulse, float gravity, float deltaTime = 1f / 60f)
        {
            float speed = new Vector2(impulse.x, impulse.z).magnitude;
            float vy = impulse.y;
            float y = 0f;
            bool grounded = vy <= 0f;
            float travelled = 0f;
            for (int i = 0; i < 1200 && speed > 1e-4f; i++)
            {
                travelled += speed * deltaTime;
                if (!grounded)
                {
                    vy += gravity * deltaTime;
                    y += vy * deltaTime;
                    if (y <= 0f)
                    {
                        grounded = true;
                    }
                }
                float decel = grounded ? GroundDeceleration : AirDeceleration;
                speed = Mathf.Max(0f, speed - decel * deltaTime);
            }
            return travelled;
        }
    }
}

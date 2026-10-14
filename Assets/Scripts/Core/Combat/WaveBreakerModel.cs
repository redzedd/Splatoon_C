using UnityEngine;

namespace SplatoonC.Core.Combat
{
    // Wave Breaker(S3 SpShockSonar,research/04 §2.7、JP wiki ホップソナー)的時間與範圍純邏輯。
    // 單位:幀(60 Hz)與公尺(1 u = 1 m)。
    //   設置後第 90 / 240 / 390F 各放一波(WaveEmitFrameArray);每波 160F 內半徑 0 → 20 等速擴張(0.125 u/F);
    //   垂直有效範圍:裝置高度 +2.8 / −5.2(DamageYMax / DamageYMin);傷害 45、標記 480F。
    //   設置當下的感應標記:圓柱範圍,半徑 = 波最大半徑 × 比例(第 4F 0.19 → 10F 0.40 → 18F 起 0.615,判定持續到 57F)。
    public static class WaveBreakerModel
    {
        public static readonly int[] WaveEmitFrames = { 90, 240, 390 };
        public const int WaveCount = 3;
        public const float WaveMaxRadius = 20f;
        public const int WaveExpandFrames = 160;
        public const float DamageYMax = 2.8f;
        public const float DamageYMin = -5.2f;
        public const float WaveDamage = 45f;
        public const float WaveMarkSeconds = 480f / 60f;

        // 設置感應(MarkingGroundFrameRadiusRatio):(幀, 比例);第 57F 之後不再判定
        private static readonly int[] SensorFrames = { 4, 10, 18, 57 };
        private static readonly float[] SensorRatios = { 0.19f, 0.4f, 0.615f, 0.615f };
        public const int SensorEndFrame = 57;

        // 波前擴張速度(公尺/秒):20 u / 160F × 60 = 7.5 m/s
        public static float WaveSpeed(float maxRadius = WaveMaxRadius, int expandFrames = WaveExpandFrames)
        {
            return expandFrames > 0 ? maxRadius / expandFrames * 60f : 0f;
        }

        // 放波後第 frames 幀(可為小數)的波前半徑;超過擴張時間回傳最大半徑
        public static float WaveRadius(float frames, float maxRadius = WaveMaxRadius, int expandFrames = WaveExpandFrames)
        {
            if (frames <= 0f)
            {
                return 0f;
            }
            if (expandFrames <= 0 || frames >= expandFrames)
            {
                return maxRadius;
            }
            return maxRadius * frames / expandFrames;
        }

        // 設置後第 frames 幀的感應半徑(4F 之前 0;57F 之後 0 = 不再判定)
        public static float SensorRadius(float frames, float maxRadius = WaveMaxRadius)
        {
            if (frames < SensorFrames[0] || frames > SensorEndFrame)
            {
                return 0f;
            }
            for (int i = 1; i < SensorFrames.Length; i++)
            {
                if (frames <= SensorFrames[i])
                {
                    float t = (frames - SensorFrames[i - 1]) / (SensorFrames[i] - SensorFrames[i - 1]);
                    return maxRadius * Mathf.Lerp(SensorRatios[i - 1], SensorRatios[i], t);
                }
            }
            return maxRadius * SensorRatios[SensorRatios.Length - 1];
        }

        // 相對裝置的高度差是否在波的垂直範圍內(+2.8 / −5.2)
        public static bool InVerticalRange(float dy)
        {
            return dy >= DamageYMin && dy <= DamageYMax;
        }

        // 這一幀波前從 previousRadius 掃到 radius:水平距離 distance、半徑 bodyRadius 的目標有沒有被波前帶(半寬 halfBand)掃到
        public static bool FrontSweeps(float previousRadius, float radius, float distance, float bodyRadius, float halfBand)
        {
            return distance + bodyRadius >= previousRadius - halfBand && distance - bodyRadius <= radius + halfBand;
        }

        // 波前還要幾秒到達水平距離 distance(已經過去或到不了回傳 −1)
        public static float SecondsUntilFront(float radius, float distance, float speed, float maxRadius = WaveMaxRadius)
        {
            if (speed <= 0f || distance < radius || distance > maxRadius)
            {
                return -1f;
            }
            return (distance - radius) / speed;
        }
    }
}

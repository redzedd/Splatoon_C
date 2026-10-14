namespace SplatoonC.Core.Combat
{
    // S3 的時間單位是 60 Hz 的「幀」(F)。投射物、引信、墨量鎖、判定窗口都用原始幀數,換算集中在這裡。
    // DESIGN_REFERENCE §1.1/§1.2:秒 = F / 60;m/s = u/F × 60;m/s² = u/F² × 3600(1 u = 1 m)。
    public static class SimFrames
    {
        public const int Rate = 60;
        public const float Dt = 1f / Rate;

        public static float ToSeconds(float frames)
        {
            return frames * Dt;
        }

        // 秒 → 幀(四捨五入;1e-4 容差避免 0.5 秒 = 29.999… 幀)
        public static int ToFrames(float seconds)
        {
            return (int)(seconds * Rate + 0.5f + 1e-4f);
        }

        // 每幀速度(u/F)→ 公尺/秒
        public static float PerFrameToPerSecond(float unitsPerFrame)
        {
            return unitsPerFrame * Rate;
        }

        public static float PerSecondToPerFrame(float metresPerSecond)
        {
            return metresPerSecond * Dt;
        }
    }

    // 固定步長累加器:把不定長的 Time.deltaTime 換成整數個 60 Hz 步,任何幀率下模擬結果都相同。
    // 單幀步數有上限(長掉幀時丟棄剩餘欠帳,避免「死亡螺旋」);Alpha 給渲染插值用。
    public struct FixedStepClock
    {
        private float _accumulator;

        // 已累積但還不滿一步的比例(0~1):渲染在「上一步 → 這一步」之間插值
        public float Alpha => _accumulator * SimFrames.Rate;

        public void Reset()
        {
            _accumulator = 0f;
        }

        // 加入這一幀的 dt,回傳這一幀要跑幾步(≤ maxSteps)
        public int Advance(float deltaTime, int maxSteps = 8)
        {
            if (deltaTime > 0f)
            {
                _accumulator += deltaTime;
            }
            int steps = 0;
            // 1e-6 容差:60 fps 下 dt 常是 0.016666 而非 1/60,不加容差會每隔幾幀少跑一步
            while (_accumulator >= SimFrames.Dt - 1e-6f && steps < maxSteps)
            {
                _accumulator -= SimFrames.Dt;
                steps++;
            }
            if (_accumulator < 0f)
            {
                _accumulator = 0f;
            }
            if (steps >= maxSteps && _accumulator >= SimFrames.Dt)
            {
                _accumulator = 0f;
            }
            return steps;
        }
    }
}

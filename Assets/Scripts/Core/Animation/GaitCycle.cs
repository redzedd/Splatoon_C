using UnityEngine;

namespace SplatoonC.Core.Animation
{
    // 跑步步態:相位以「實際移動距離」推進(不是時間)——腳步頻率自然跟著速度走,停下就停,
    // 被牆擋住原地踏步也不會空轉。一個完整週期 = 左右各踩一步 = 2 × 步幅(弧度 2π)。
    // 步幅在全速時與 InklingAudio 的腳步聲間距一致(1.35 m),腳著地與腳步聲同拍。
    //
    // 相位慣例(左腿;右腿 = 相位 + π):
    //   腳的前後位置 = sin(p):p = π/2 腳在最前(著地瞬間)、p = 3π/2 腳在最後(蹬離)。
    //   cos(p) > 0 = 擺盪期(腳往前、離地),cos(p) < 0 = 支撐期(腳貼地往後滑 = 身體往前)。
    public struct GaitCycle
    {
        private const float TwoPi = Mathf.PI * 2f;

        private float _phase;

        public float Phase => _phase;

        // 推進 distance 公尺,步幅 strideLength(一步的距離);回傳新相位
        public float Advance(float distance, float strideLength)
        {
            if (distance <= 0f || strideLength <= 0.0001f)
            {
                return _phase;
            }
            _phase += distance / (2f * strideLength) * TwoPi;
            _phase = Mathf.Repeat(_phase, TwoPi);
            return _phase;
        }

        // 停下時把相位慢慢帶回「雙腳並攏」的位置(sin = 0),不會停在跨步姿勢
        public void SettleTowardsRest(float rate, float deltaTime)
        {
            // 最近的 0 / π / 2π(兩腳都在身體正下方的相位)
            float target = Mathf.Round(_phase / Mathf.PI) * Mathf.PI;
            _phase = Mathf.MoveTowards(_phase, target, rate * deltaTime);
            _phase = Mathf.Repeat(_phase, TwoPi);
        }

        public void Reset()
        {
            _phase = 0f;
        }

        // 依速度決定步幅:慢走步子小、全速跑到基準步幅(S3 人形全速 5.76 m/s)
        public static float StrideForSpeed(float planarSpeed, float runSpeed, float fullStride, float minStride)
        {
            float t = runSpeed > 0.0001f ? Mathf.Clamp01(planarSpeed / runSpeed) : 0f;
            return Mathf.Lerp(minStride, fullStride, t);
        }

        // 支撐期腳相對身體往後滑的半幅度:sin 軌跡在支撐中點的速度 = 身體速度 → 半幅 = 步幅 / π(不滑步)
        public static float FootSweepHalf(float strideLength)
        {
            return strideLength / Mathf.PI;
        }

        // 腳的前後位置(-1…1,乘上半幅度 = 公尺)
        public static float FootForward(float phase)
        {
            return Mathf.Sin(phase);
        }

        // 腳的抬起量(0…1):只在擺盪期抬起;峰值略早於擺盪中段(腳還在身後時腳跟往上踢),支撐期為 0(貼地)
        public static float FootLift(float phase)
        {
            if (Mathf.Cos(phase) <= 0f)
            {
                return 0f;
            }
            // 擺盪期進度 u:0 = 蹬離(p = -π/2)、1 = 著地(p = π/2);兩端為 0 保證連續,u^0.8 讓峰值提前到 u ≈ 0.42
            float u = Mathf.Clamp01(Mathf.Repeat(phase + Mathf.PI * 0.5f, TwoPi) / Mathf.PI);
            return Mathf.Sin(Mathf.PI * Mathf.Pow(u, 0.8f));
        }

        // 腳掌俯仰(度,正 = 腳尖朝下):蹬離時腳尖朝下、著地前腳尖抬起
        public static float FootPitch(float phase)
        {
            return -14f * Mathf.Sin(phase) + 18f * Mathf.Max(0f, Mathf.Cos(phase + 0.6f));
        }

        // 骨盆上下(-1…1):兩腳各踩一次 = 兩倍頻;支撐中段(一腳在正下方)最低
        public static float HipBob(float phase)
        {
            return -Mathf.Cos(2f * phase);
        }

        // 手臂前後擺(-1…1):與同側腿相反
        public static float ArmSwing(float phase)
        {
            return -Mathf.Sin(phase);
        }
    }
}

using UnityEngine;

namespace SplatoonC.Core.Combat
{
    // S3 參數檔的曲線型別「Hermit2DSmooth」:Data = [x0, y0, 斜率0, x1, y1, 斜率1, …](斜率 = dy/dx),
    // 相鄰兩鍵之間以三次 Hermite 內插;x 在第一鍵之前取第一鍵的 y、最後一鍵之後取最後一鍵的 y。
    // 不可變、建構一次後零配置求值。S3 大招用到的曲線放在下面的預設(數值原樣取自 Leanny v11.3.0 參數檔)。
    public sealed class HermiteCurve
    {
        private readonly float[] _x;
        private readonly float[] _y;
        private readonly float[] _slope;

        public int KeyCount => _x.Length;

        // data:每 3 個數一組 (x, y, 斜率),x 遞增
        public HermiteCurve(params float[] data)
        {
            int count = data != null ? data.Length / 3 : 0;
            _x = new float[count];
            _y = new float[count];
            _slope = new float[count];
            for (int i = 0; i < count; i++)
            {
                _x[i] = data[i * 3];
                _y[i] = data[i * 3 + 1];
                _slope[i] = data[i * 3 + 2];
            }
        }

        public float Evaluate(float x)
        {
            int count = _x.Length;
            if (count == 0)
            {
                return 0f;
            }
            if (x <= _x[0])
            {
                return _y[0];
            }
            if (x >= _x[count - 1])
            {
                return _y[count - 1];
            }
            int i = 0;
            while (i < count - 2 && x > _x[i + 1])
            {
                i++;
            }
            float x0 = _x[i];
            float x1 = _x[i + 1];
            float h = x1 - x0;
            if (h <= 1e-6f)
            {
                return _y[i + 1];
            }
            float t = (x - x0) / h;
            float t2 = t * t;
            float t3 = t2 * t;
            float h00 = 2f * t3 - 3f * t2 + 1f;
            float h10 = t3 - 2f * t2 + t;
            float h01 = -2f * t3 + 3f * t2;
            float h11 = t3 - t2;
            return h00 * _y[i] + h10 * h * _slope[i] + h01 * _y[i + 1] + h11 * h * _slope[i + 1];
        }

        // ── S3 預設 ──

        // Big Bubbler 屏障半徑比例(BarrierParam.RadiusRatioCurve):x = 耐久比例,y = (半徑 − 2.255) / (7.5 − 2.255)。
        // 耐久 ≤ 19.4% 時縮到最小半徑;滿血時最大(JP wiki:耐久減少時屏障逐漸變小、頂端無人機跟著下降)。
        public static readonly HermiteCurve BubblerRadiusRatio = new HermiteCurve(
            0.1940299f, 0f, 0f,
            0.6522388f, 0.3726415f, -0.02766653f,
            0.8723881f, 0.75f, 1.1808f,
            1f, 1f, 4.740566f);

        // Big Bubbler 無人機上升(DroneParam.AscendCurve):x = 上升進度(AscendFrame 30F 內 0→1),y = 高度比例
        public static readonly HermiteCurve BubblerAscend = new HermiteCurve(
            0f, 0f, 0f,
            0.25f, 0.1122642f, 0.8181818f,
            0.5f, 0.5f, 2.926098f,
            0.6830189f, 0.8556603f, 1.395833f,
            1f, 1f, 0f);

        // Wave Breaker 彈跳球(GeneratorParam.SphereAnimCurve):x = 兩波之間的進度,y = 球抬起比例(慢慢升起、放波前落下)
        public static readonly HermiteCurve WaveBreakerBall = new HermiteCurve(
            0f, 0f, 0f,
            0.1454545f, 0f, 0f,
            0.901515f, 1f, -0.1383645f,
            1f, 0f, 0f);
    }
}

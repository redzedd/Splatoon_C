using System;
using UnityEngine;

namespace SplatoonC.Core.Combat
{
    [Serializable]
    public struct SpreadSettings
    {
        // 地面/空中最大偏角(度)
        public float StandSwerveDeg;
        public float JumpSwerveDeg;
        // 偏向外側的機率參數 b:首發 BiasMin,每發 +BiasKf,上限 BiasMax;停火 RepeatSeconds 後每秒減 BiasDecreasePerSecond
        public float BiasMin;
        public float BiasKf;
        public float BiasMax;
        public float BiasDecreasePerSecond;
        public float RepeatSeconds;
        // 空中:b 固定為 JumpBiasMax;偏角在起跳後 JumpShrinkStart~JumpShrinkEnd 秒間由空中值縮回地面值
        public float JumpBiasMax;
        public float JumpShrinkStartSeconds;
        public float JumpShrinkEndSeconds;

        // S3 Splattershot(Leanny v11.3.0)
        public static SpreadSettings Splattershot => new SpreadSettings
        {
            StandSwerveDeg = 4.86f,
            JumpSwerveDeg = 11.66f,
            BiasMin = 0.01f,
            BiasKf = 0.01f,
            BiasMax = 0.25f,
            BiasDecreasePerSecond = 0.015f * 60f,
            RepeatSeconds = 6f / 60f,
            JumpBiasMax = 0.4f,
            JumpShrinkStartSeconds = 25f / 60f,
            JumpShrinkEndSeconds = 70f / 60f,
        };
    }

    // Splatoon 3 的射擊散布模型(JP wiki「ブレの計算式」):
    // 偏角 = swerve × x^(log0.5 b),x ~ U[0,1],只偏水平(yaw),左右隨機。中位數剛好落在 b × swerve。
    // 首發 b 很小 → 幾乎必中準心;連射 b 上升 → 散得越來越開;跳射 b 固定 0.4 且偏角更大。
    public struct SpreadModel
    {
        // 連射時實際出膛時刻落在幀邊界,比 FireClock 的排程晚 0~1 幀:這一幀以內的延遲不算停火。
        // 否則每發都被扣一點回復量,b 爬不到 S3 的上限(N-ZAP 每發 +0.005,每秒回復 0.3,晚一幀就抵掉 +0.005)。
        public const float FrameGraceSeconds = 1f / 60f;

        private float _bias;
        private float _lastShotTime;
        private bool _hasShot;

        public float CurrentBias => _bias;

        // randomX:U[0,1];randomSign:U[0,1](< 0.5 往左)。回傳 yaw 偏移(度,帶正負號)。
        public float NextYawOffset(float time, in SpreadSettings s, bool airborne, float secondsSinceTakeoff,
            float randomX, float randomSign)
        {
            if (!_hasShot)
            {
                _bias = s.BiasMin;
                _hasShot = true;
            }
            else
            {
                // 停火回復:最後一發後過了 RepeatSeconds(+ 一幀出膛誤差)才開始遞減
                float idle = time - _lastShotTime - s.RepeatSeconds - FrameGraceSeconds;
                if (idle > 0f)
                {
                    _bias = Mathf.Max(s.BiasMin, _bias - s.BiasDecreasePerSecond * idle);
                }
            }
            _lastShotTime = time;

            float bias;
            float swerve;
            if (airborne)
            {
                bias = s.JumpBiasMax;
                float span = Mathf.Max(1e-4f, s.JumpShrinkEndSeconds - s.JumpShrinkStartSeconds);
                float t = Mathf.Clamp01((secondsSinceTakeoff - s.JumpShrinkStartSeconds) / span);
                swerve = Mathf.Lerp(s.JumpSwerveDeg, s.StandSwerveDeg, t);
            }
            else
            {
                bias = _bias;
                swerve = s.StandSwerveDeg;
                _bias = Mathf.Min(s.BiasMax, _bias + s.BiasKf);
            }
            float angle = AngleFor(swerve, bias, randomX);
            return randomSign < 0.5f ? -angle : angle;
        }

        public static float AngleFor(float swerveDeg, float bias, float randomX)
        {
            if (swerveDeg <= 0f)
            {
                return 0f;
            }
            float b = Mathf.Clamp(bias, 1e-4f, 0.9999f);
            float k = Mathf.Log(b) / Mathf.Log(0.5f);
            return swerveDeg * Mathf.Pow(Mathf.Clamp01(randomX), k);
        }
    }
}

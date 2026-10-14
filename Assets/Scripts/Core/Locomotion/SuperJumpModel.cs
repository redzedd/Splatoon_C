using System;
using UnityEngine;

namespace SplatoonC.Core.Locomotion
{
    // S3 超級跳時間軸與弧線參數(DESIGN_REFERENCE §2.9、research/01 §15、research/07 §6.1)。時間單位 F(60 Hz)。
    [Serializable]
    public struct SuperJumpParams
    {
        [Tooltip("蓄力幀數(S3 SuperJump_ChargeFrm 0 AP 80F;Quick Super Jump 由 InklingModifiers 提供):不能動、可被打")]
        public int ChargeFrames;

        [Tooltip("從人形開始時多出的蓄力幀數(S3 +18F = 人形 → 烏賊蹲下;research/07 §6.1)")]
        public int HumanoidExtraFrames;

        [Tooltip("飛行幀數(S3 SuperJump_MoveFrm 138F,與距離無關):完全無敵、穿牆")]
        public int FlightFrames;

        [Tooltip("落地前可以開槍的幀數 [EST](S3「落地前一點點就能開槍」;這段變回人形的著地姿勢)")]
        public int FireWindowFrames;

        [Tooltip("弧線頂點高於起訖較高者的基本高度(公尺)[EST]")]
        public float ApexBase;

        [Tooltip("弧線頂點隨水平距離增加的比例 [EST]")]
        public float ApexPerMeter;

        [Tooltip("弧線頂點高度上限(公尺)[EST]")]
        public float ApexMax;

        public static SuperJumpParams Splatoon3 => new SuperJumpParams
        {
            ChargeFrames = 80,
            HumanoidExtraFrames = 18,
            FlightFrames = 138,
            FireWindowFrames = 12,
            ApexBase = 10f,
            ApexPerMeter = 0.3f,
            ApexMax = 28f,
        };
    }

    // 超級跳的純數學:蓄力秒數、飛行弧線(三次 Bézier:起點正上方 → 終點正上方,參數以 smoothstep 緩起緩落)、
    // 落點標記的「墨水流乾」比例。
    public static class SuperJumpModel
    {
        // 蓄力秒數:baseChargeSeconds 已含 QSJ(InklingModifiers.SuperJumpChargeSeconds);人形開始再加 18F
        public static float ChargeSeconds(in SuperJumpParams p, float baseChargeSeconds, bool fromHumanoid)
        {
            return Mathf.Max(0f, baseChargeSeconds) + (fromHumanoid ? p.HumanoidExtraFrames / 60f : 0f);
        }

        // 0 AP 從烏賊態:80F;人形:98F
        public static int ChargeFramesAt0Ap(in SuperJumpParams p, bool fromHumanoid)
        {
            return p.ChargeFrames + (fromHumanoid ? p.HumanoidExtraFrames : 0);
        }

        // 弧線頂點高度(高於起訖較高者)
        public static float ApexHeight(in SuperJumpParams p, Vector3 start, Vector3 end)
        {
            float flat = new Vector2(end.x - start.x, end.z - start.z).magnitude;
            return Mathf.Min(p.ApexMax, p.ApexBase + p.ApexPerMeter * flat);
        }

        // 飛行位置:t = 0~1(飛行經過時間 / 飛行總時間)
        public static Vector3 FlightPosition(Vector3 start, Vector3 end, float apexHeight, float t)
        {
            float s = Smooth(Mathf.Clamp01(t));
            float top = Mathf.Max(start.y, end.y) + apexHeight;
            // 控制點高度:使 Bézier 中點剛好到 top(中點 = (P0 + 3P1 + 3P2 + P3) / 8)
            float controlY = (8f * top - start.y - end.y) / 6f;
            Vector3 p1 = new Vector3(start.x, controlY, start.z);
            Vector3 p2 = new Vector3(end.x, controlY, end.z);
            float u = 1f - s;
            return u * u * u * start + 3f * u * u * s * p1 + 3f * u * s * s * p2 + s * s * s * end;
        }

        // 落點標記的墨水剩餘比例(1 → 0;蓄力中維持 1,飛行中隨時間流乾)
        public static float MarkerFill(float flightElapsed, float flightSeconds)
        {
            if (flightSeconds <= 0f)
            {
                return 0f;
            }
            return 1f - Mathf.Clamp01(flightElapsed / flightSeconds);
        }

        // 是否已進入「落地前可開槍」的窗口
        public static bool InFireWindow(in SuperJumpParams p, float flightElapsed, float flightSeconds)
        {
            return flightSeconds - flightElapsed <= p.FireWindowFrames / 60f + 1e-4f;
        }

        private static float Smooth(float x)
        {
            return x * x * (3f - 2f * x);
        }
    }
}

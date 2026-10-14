using System;
using UnityEngine;

namespace SplatoonC.Core.Locomotion
{
    // S3 烏賊態的新動作(Squid Roll / Wall Roll / Squid Surge)參數。時間單位 F(60 Hz)、距離 u(= 公尺)。
    // 來源:DESIGN_REFERENCE §2.7、research/01 §9–§11、research/07 §3.3/§3.4、Leanny SplPlayer(WallJumpChargeFrm)。
    [Serializable]
    public struct SquidMoveParams
    {
        [Tooltip("翻滾門檻速度(公尺/秒;S3 0.145 u/F = 8.7,與武器/裝備無關,也是大水花門檻)")]
        public float RollMinSpeed;

        [Tooltip("搖桿與實際速度方向的最小夾角(度;S3 60)")]
        public float RollMinAngleDeg;

        [Tooltip("速度掉到門檻以下後仍可翻滾的輸入緩衝(F;S3 10)")]
        public int RollBufferFrames;

        [Tooltip("連續翻滾判定窗(F;S3 90):窗內的第二次起每次速度 × RollChainFactor")]
        public int RollChainWindowFrames;

        [Tooltip("連續翻滾的速度倍率(0 AP 0.85;Intensify Action 57 AP → 1.0,由 InklingModifiers 提供)")]
        public float RollChainFactor;

        [Tooltip("翻滾滯空幀數 [EST](S3 滯空時間固定、與速度無關;未公開 → 36F,重力 10.8 → 起跳 3.24 m/s、頂點 0.49 m)")]
        public int RollAirFrames;

        [Tooltip("翻滾滯空中往搖桿方向的空中加速度(公尺/秒²)[EST]:S3 基礎加速度 0.01 u/F² = 36")]
        public float RollAirAccel;

        [Tooltip("翻滾/牆滾/衝牆的護甲耐久(HP;S3 30)")]
        public float MoveArmorHp;

        [Tooltip("翻滾護甲幀數(S3 18F;變回人形立刻結束)")]
        public int RollArmorFrames;

        [Tooltip("衝牆滿蓄幀數(S3 WallJumpChargeFrm 45;Intensify Action 57 AP → 5,由 InklingModifiers 提供)")]
        public int SurgeChargeFrames;

        [Tooltip("衝牆/牆滾護甲幀數(S3 45F)")]
        public int SurgeArmorFrames;

        [Tooltip("滿蓄衝牆沿牆上衝的速度(公尺/秒)[EST] research/07 §3.3:0.35–0.4 u/F → 0.375 = 22.5")]
        public float SurgeMaxSpeed;

        [Tooltip("零蓄衝牆越過牆頂的彈起高度(公尺)[EST]:部分蓄力 = 小跳")]
        public float SurgeMinHop;

        [Tooltip("滿蓄衝牆越過牆頂的彈起高度(公尺)[EST] research/07 §3.3:牆頂上方 3–4 u")]
        public float SurgeMaxHop;

        [Tooltip("衝牆越頂時往牆內(平台上方)推的水平速度(公尺/秒)[EST]:只要越過牆緣、落在牆頂平台上(滿蓄滯空約 1.7 s → 約 2.6 m)")]
        public float SurgeLipPush;

        [Tooltip("牆滾:搖桿與牆面法線(離牆方向)的最大夾角(度;S3 45)")]
        public float WallRollMaxAngleDeg;

        [Tooltip("牆滾離牆的水平速度 = 游速 × 此倍率 × 連滾倍率 [EST]")]
        public float WallRollSpeedScale;

        [Tooltip("牆滾起跳的垂直速度(公尺/秒)[EST]:頂點約 1.35 m")]
        public float WallRollUpSpeed;

        [Tooltip("觸發翻滾/牆滾的最小搖桿推量(S3 搖桿深度無關,這裡只排除死區)")]
        public float MinStick;

        public static SquidMoveParams Splatoon3 => new SquidMoveParams
        {
            RollMinSpeed = 8.7f,
            RollMinAngleDeg = 60f,
            RollBufferFrames = 10,
            RollChainWindowFrames = 90,
            RollChainFactor = 0.85f,
            RollAirFrames = 36,
            RollAirAccel = 36f,
            MoveArmorHp = 30f,
            RollArmorFrames = 18,
            SurgeChargeFrames = 45,
            SurgeArmorFrames = 45,
            SurgeMaxSpeed = 22.5f,
            SurgeMinHop = 0.6f,
            SurgeMaxHop = 3.5f,
            SurgeLipPush = 1.5f,
            WallRollMaxAngleDeg = 45f,
            WallRollSpeedScale = 0.65f,
            WallRollUpSpeed = 5.4f,
            MinStick = 0.2f,
        };
    }

    // 翻滾 / 牆滾 / 衝牆的判定與數值(純邏輯;PlayerLocomotion 經 SquidMoves 呼叫)。
    public static class SquidMoveModel
    {
        // 兩個水平向量的夾角(度,0~180);任一為零向量回 0
        public static float FlatAngle(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            if (a.sqrMagnitude < 1e-6f || b.sqrMagnitude < 1e-6f)
            {
                return 0f;
            }
            return Vector3.Angle(a, b);
        }

        // 翻滾條件(S3 §2.7):① 目前游速 ≥ 門檻,或掉到門檻以下還不到 RollBufferFrames;
        // ② 搖桿方向與實際速度方向夾角 ≥ RollMinAngleDeg(搖桿深度無關,只排除死區)。
        // secondsSinceFast:最後一次「游速 ≥ 門檻」到現在的秒數;velocityDir:實際速度方向(速度太小時用最後的高速方向)。
        public static bool CanRoll(in SquidMoveParams p, float currentSpeed, float secondsSinceFast, Vector3 velocityDir,
            Vector3 stickDir, float stickMagnitude)
        {
            if (stickMagnitude < p.MinStick)
            {
                return false;
            }
            bool fastEnough = currentSpeed >= p.RollMinSpeed
                              || secondsSinceFast <= p.RollBufferFrames / 60f + 1e-4f;
            if (!fastEnough)
            {
                return false;
            }
            return FlatAngle(velocityDir, stickDir) >= p.RollMinAngleDeg - 1e-3f;
        }

        // 連續翻滾序號:上一次翻滾在窗內 → +1,否則從 0 開始(第一滾不打折)
        public static int NextChainIndex(int previousIndex, float secondsSinceLastRoll, in SquidMoveParams p)
        {
            return secondsSinceLastRoll <= p.RollChainWindowFrames / 60f + 1e-4f ? previousIndex + 1 : 0;
        }

        // 第 chainIndex 次連滾的速度倍率(0.85^n)
        public static float ChainSpeedFactor(int chainIndex, float factor)
        {
            if (chainIndex <= 0)
            {
                return 1f;
            }
            return Mathf.Pow(Mathf.Clamp(factor, 0f, 1f), chainIndex);
        }

        // 固定滯空 airFrames(平地起落)所需的起跳垂直速度:v = g·T/2(gravity 為負值)
        public static float LaunchSpeedForAirtime(int airFrames, float gravity)
        {
            return Mathf.Abs(gravity) * (airFrames / 60f) * 0.5f;
        }

        // 升到 height 公尺所需的起跳垂直速度:v = √(2gh)
        public static float LaunchSpeedForHeight(float height, float gravity)
        {
            return Mathf.Sqrt(2f * Mathf.Abs(gravity) * Mathf.Max(0f, height));
        }

        // 衝牆蓄力比例(0~1)
        public static float SurgeCharge01(float heldSeconds, float chargeSeconds)
        {
            return chargeSeconds <= 0f ? 1f : Mathf.Clamp01(heldSeconds / chargeSeconds);
        }

        // 衝牆沿牆上衝速度:零蓄 = 平常爬牆速度,滿蓄 = SurgeMaxSpeed(線性)
        public static float SurgeClimbSpeed(in SquidMoveParams p, float charge01, float normalClimbSpeed)
        {
            return Mathf.Lerp(normalClimbSpeed, Mathf.Max(normalClimbSpeed, p.SurgeMaxSpeed), Mathf.Clamp01(charge01));
        }

        // 衝牆越頂彈起的高度(牆頂上方,公尺)
        public static float SurgeHopHeight(in SquidMoveParams p, float charge01)
        {
            return Mathf.Lerp(p.SurgeMinHop, p.SurgeMaxHop, Mathf.Clamp01(charge01));
        }

        // 牆滾輸入:搖桿(世界方向)與牆面法線(離牆方向)夾角 ≤ WallRollMaxAngleDeg
        public static bool IsWallRollInput(in SquidMoveParams p, Vector3 stickWorld, float stickMagnitude, Vector3 wallNormal)
        {
            if (stickMagnitude < p.MinStick)
            {
                return false;
            }
            return FlatAngle(stickWorld, wallNormal) <= p.WallRollMaxAngleDeg + 1e-3f;
        }

        // 蓄力/上衝中搖桿往牆外推 → 取消衝牆(與牆滾同一個方向判定,但不需要按跳)
        public static bool IsPushingAway(in SquidMoveParams p, Vector3 stickWorld, float stickMagnitude, Vector3 wallNormal)
        {
            return stickMagnitude >= 0.5f && IsWallRollInput(p, stickWorld, stickMagnitude, wallNormal);
        }
    }
}

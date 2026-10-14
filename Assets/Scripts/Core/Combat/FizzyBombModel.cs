using System;
using UnityEngine;

namespace SplatoonC.Core.Combat
{
    // Fizzy Bomb 的 S3 參數(Leanny v11.3.0 WeaponBombFizzy + JP wiki タンサンボム;u = m、F = 1/60 s)。
    // 逐爆炸(第 1/2/3 爆)的欄位分開寫(不用陣列:struct 預設值與零配置)。
    [Serializable]
    public struct FizzyBombParams
    {
        [Header("投擲")]
        [Tooltip("通用投擲彈道(SpawnSpeedZ 1.36 u/F;ZRate 2.0)")]
        public S3ThrowParams Throw;

        [Header("蓄力(按住副武器)")]
        [Tooltip("按下後炸彈出現在手上、開始蓄力的幀數(人形 5F)")]
        public int WindupFrames;

        [Tooltip("第 2 段需要的蓄力幀(S3 ChargeFrameArray[0] 40)")]
        public int Level2Frames;

        [Tooltip("第 3 段再多需要的蓄力幀(S3 ChargeFrameArray[1] 80 → 共 120)")]
        public int Level3ExtraFrames;

        [Tooltip("按一次跳躍折抵的蓄力幀(JP:B 鍵 −8F)")]
        public float MashJumpFrames;

        [Tooltip("搖桿從中間甩出一次折抵的蓄力幀(JP:−11F)")]
        public float MashStickFrames;

        [Tooltip("折抵量每幀最多兌現幾 F(JP:2.5F → 最快每幀 3.5F)")]
        public float MashMaxPerFrame;

        [Header("爆炸時機(F)")]
        [Tooltip("第 1 爆:著地後等待(S3 BurstWaitFrameArray[0] 15)")]
        public int FirstBurstWaitFrames;

        [Tooltip("第 1 爆:警告(S3 BurstWarnFrameArray[0] 15 → 著地 30F 後爆)")]
        public int FirstBurstWarnFrames;

        [Tooltip("第 2、3 爆:再次著地後等待(S3 1)")]
        public int NextBurstWaitFrames;

        [Tooltip("第 2、3 爆:警告(S3 7 → 再次著地 8F 後爆)")]
        public int NextBurstWarnFrames;

        [Header("爆炸後的彈跳(S3 Hop*)")]
        [Tooltip("彈跳初速下限(u/F;S3 HopVelL 0.24)")]
        public float HopSpeedMin;

        [Tooltip("彈跳初速上限(u/F;S3 HopVelH 0.32)")]
        public float HopSpeedMax;

        [Tooltip("彈跳仰角下限(度;S3 HopPitchL 48)")]
        public float HopPitchMin;

        [Tooltip("彈跳仰角上限(度;S3 HopPitchH 58)")]
        public float HopPitchMax;

        [Tooltip("彈跳方向的左右亂數(度;[EST] JP「跳ね方にランダム性」)")]
        public float HopYawJitter;

        [Header("地面")]
        [Tooltip("地面滾動阻力(每幀 ×(1 − 此值);S3 GroundPositionHorizonAirResist 0.19)")]
        public float GroundResist;

        [Tooltip("撞牆反彈係數([EST] JP「入射角 = 反射角」,法線與切線同比例 0.7)")]
        public float WallRebound;

        [Tooltip("著地瞬間保留的水平速度比例([EST])")]
        public float LandingKeep;

        [Header("飛行軌跡塗墨(依蓄力段數)")]
        [Tooltip("軌跡落墨間距(u;S3 FlySplashDistanceArray 2.2)")]
        public float TrailSpacing;

        [Tooltip("軌跡落墨往下找地面的最大高度(u;S3 DepthMinDropHeight 10)")]
        public float TrailDropHeight;

        [Tooltip("每筆沿飛行方向拉長的倍率(S3 DepthScaleMin/Max 1.6:橢圓半長軸 = 半寬 × 1.6,前後筆相連成帶)")]
        public float TrailDepthScale;

        [Tooltip("軌跡半寬,第 1 段(u;S3 WidthHalf 1.2;JP 塗り幅 0.48 線)")]
        public float TrailHalfWidth1;

        [Tooltip("軌跡半寬,第 2 段(u;S3 1.26)")]
        public float TrailHalfWidth2;

        [Tooltip("軌跡半寬,第 3 段(u;S3 1.32)")]
        public float TrailHalfWidth3;

        [Header("爆炸(每一爆 50 / 35,半徑依第幾爆)")]
        [Tooltip("近距傷害(S3 500 = 50.0)")]
        public float NearDamage;

        [Tooltip("遠距傷害(S3 350 = 35.0)")]
        public float FarDamage;

        [Tooltip("第 1 爆近距半徑(u;S3 1.6)")]
        public float NearRadius1;

        [Tooltip("第 2 爆近距半徑(u;S3 1.95)")]
        public float NearRadius2;

        [Tooltip("第 3 爆近距半徑(u;S3 2.6)")]
        public float NearRadius3;

        [Tooltip("第 1 爆遠距半徑(u;S3 3.8)")]
        public float FarRadius1;

        [Tooltip("第 2 爆遠距半徑(u;S3 4.5)")]
        public float FarRadius2;

        [Tooltip("第 3 爆遠距半徑(u;S3 5.45)")]
        public float FarRadius3;

        [Tooltip("第 1 爆塗墨半徑(u;S3 3.0)")]
        public float PaintRadius1;

        [Tooltip("第 2 爆塗墨半徑(u;S3 3.4)")]
        public float PaintRadius2;

        [Tooltip("第 3 爆塗墨半徑(u;S3 3.8)")]
        public float PaintRadius3;

        // S3 v11.3.0、0 AP
        public static FizzyBombParams S3 => new FizzyBombParams
        {
            Throw = S3ThrowParams.Bomb(1.36f),
            WindupFrames = 5,
            Level2Frames = 40,
            Level3ExtraFrames = 80,
            MashJumpFrames = 8f,
            MashStickFrames = 11f,
            MashMaxPerFrame = 2.5f,
            FirstBurstWaitFrames = 15,
            FirstBurstWarnFrames = 15,
            NextBurstWaitFrames = 1,
            NextBurstWarnFrames = 7,
            HopSpeedMin = 0.24f,
            HopSpeedMax = 0.32f,
            HopPitchMin = 48f,
            HopPitchMax = 58f,
            HopYawJitter = 20f,
            GroundResist = 0.19f,
            WallRebound = 0.7f,
            LandingKeep = 0.5f,
            TrailSpacing = 2.2f,
            TrailDropHeight = 10f,
            TrailDepthScale = 1.6f,
            TrailHalfWidth1 = 1.2f,
            TrailHalfWidth2 = 1.26f,
            TrailHalfWidth3 = 1.32f,
            NearDamage = 50f,
            FarDamage = 35f,
            NearRadius1 = 1.6f,
            NearRadius2 = 1.95f,
            NearRadius3 = 2.6f,
            FarRadius1 = 3.8f,
            FarRadius2 = 4.5f,
            FarRadius3 = 5.45f,
            PaintRadius1 = 3f,
            PaintRadius2 = 3.4f,
            PaintRadius3 = 3.8f,
        };
    }

    // Fizzy Bomb 純邏輯(research 03 §2.6、DESIGN_REFERENCE §4.3):
    //   蓄力:炸彈 5F 後出現在手上開始蓄;40F → 第 2 段,再 80F(共 120F)→ 第 3 段;按跳躍 −8F、甩搖桿 −11F,
    //         折抵量每幀最多兌現 2.5F(最快每幀 3.5F,理論 35F 蓄滿)。段數 = 爆炸次數。
    //   爆炸:第 1 爆 = 第一次著地後 15 + 15 = 30F(與飛多久無關);之後每爆完彈跳一次(0.24~0.32 u/F、仰角 48~58°),
    //         再次著地 1 + 7 = 8F 後下一爆。每爆 50 / 35,半徑 1.6/3.8 → 1.95/4.5 → 2.6/5.45,塗墨 3.0 → 3.4 → 3.8。
    //   飛行軌跡:每 2.2 u 往下落一筆墨(半寬 1.2 / 1.26 / 1.32 依段數),唯一會塗飛行路徑的副武器。
    public static class FizzyBombModel
    {
        public static int MaxChargeFrames(in FizzyBombParams p)
        {
            return p.Level2Frames + p.Level3ExtraFrames;
        }

        // 蓄力幀 → 段數(1~3)= 爆炸次數
        public static int LevelFor(in FizzyBombParams p, float chargeFrames)
        {
            if (chargeFrames >= MaxChargeFrames(p) - 1e-3f)
            {
                return 3;
            }
            return chargeFrames >= p.Level2Frames - 1e-3f ? 2 : 1;
        }

        // 蓄力比例 0~1 → 段數
        public static int LevelFor01(in FizzyBombParams p, float charge01)
        {
            return LevelFor(p, Mathf.Clamp01(charge01) * MaxChargeFrames(p));
        }

        // 第 burstIndex 爆(0 起算)在著地後幾 F 爆
        public static int BurstDelayFrames(in FizzyBombParams p, int burstIndex)
        {
            return burstIndex <= 0
                ? p.FirstBurstWaitFrames + p.FirstBurstWarnFrames
                : p.NextBurstWaitFrames + p.NextBurstWarnFrames;
        }

        public static float NearRadius(in FizzyBombParams p, int burstIndex)
        {
            return burstIndex <= 0 ? p.NearRadius1 : burstIndex == 1 ? p.NearRadius2 : p.NearRadius3;
        }

        public static float FarRadius(in FizzyBombParams p, int burstIndex)
        {
            return burstIndex <= 0 ? p.FarRadius1 : burstIndex == 1 ? p.FarRadius2 : p.FarRadius3;
        }

        public static float PaintRadius(in FizzyBombParams p, int burstIndex)
        {
            return burstIndex <= 0 ? p.PaintRadius1 : burstIndex == 1 ? p.PaintRadius2 : p.PaintRadius3;
        }

        public static float TrailHalfWidth(in FizzyBombParams p, int level)
        {
            return level <= 1 ? p.TrailHalfWidth1 : level == 2 ? p.TrailHalfWidth2 : p.TrailHalfWidth3;
        }

        // 爆完的彈跳初速(u/F):heading = 前進方向(水平),speed01 / pitch01 / yaw01 = 0~1 亂數
        public static Vector3 HopVelocity(in FizzyBombParams p, Vector3 heading, float speed01, float pitch01, float yaw01)
        {
            Vector3 flat = new Vector3(heading.x, 0f, heading.z);
            if (flat.sqrMagnitude < 1e-8f)
            {
                flat = Vector3.forward;
            }
            flat.Normalize();
            float yaw = (Mathf.Clamp01(yaw01) * 2f - 1f) * p.HopYawJitter;
            flat = Quaternion.AngleAxis(yaw, Vector3.up) * flat;
            float speed = Mathf.Lerp(p.HopSpeedMin, p.HopSpeedMax, Mathf.Clamp01(speed01));
            float pitch = Mathf.Lerp(p.HopPitchMin, p.HopPitchMax, Mathf.Clamp01(pitch01)) * Mathf.Deg2Rad;
            return flat * (Mathf.Cos(pitch) * speed) + Vector3.up * (Mathf.Sin(pitch) * speed);
        }

        // 折抵:bank = 尚未兌現的折抵幀;回傳這一幀兌現多少(≤ MashMaxPerFrame × frames)並從 bank 扣掉
        public static float TakeMash(in FizzyBombParams p, ref float bank, float frames)
        {
            if (bank <= 0f || frames <= 0f)
            {
                return 0f;
            }
            float take = Mathf.Min(bank, p.MashMaxPerFrame * frames);
            bank -= take;
            return take;
        }

        // 只按住(不連打)要幾 F 才到 level 段(含 5F 出手準備)
        public static int HoldFramesForLevel(in FizzyBombParams p, int level)
        {
            if (level <= 1)
            {
                return p.WindupFrames;
            }
            return p.WindupFrames + (level == 2 ? p.Level2Frames : MaxChargeFrames(p));
        }
    }
}

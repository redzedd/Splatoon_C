using System;
using UnityEngine;

namespace SplatoonC.Core.Combat
{
    // 雙槍閃避翻滾的參數(S3 SideStepParam;Leanny v11.3.0 Maneuver_Normal)。時間單位 F(60 Hz)、距離 u(= 公尺)。
    [Serializable]
    public struct DodgeRollParams
    {
        [Tooltip("起手幀(按下跳到開始位移;research 02 §10.2:4F)")]
        public int StartupFrames;

        [Tooltip("翻滾位移幀(SideStepParam.MoveFrame 12);結束後即可接下一滾")]
        public int MoveFrames;

        [Tooltip("翻滾位移距離(u;S3 4.0)")]
        public float Distance;

        [Tooltip("翻滾後的滑行距離(u;S3 1.0)")]
        public float SlideDistance;

        [Tooltip("滑行幀數 [EST]:以翻滾末速度線性減到 0 剛好滑完 SlideDistance(4.0/12 u/F → 2×1.0/0.333 = 6F)")]
        public int SlideFrames;

        [Tooltip("翻滾結束後還不能開火的幀數(S3:4F)")]
        public int NoFireFrames;

        [Tooltip("翻滾結束後的硬直幀數(UnrelaxFrameMove/NoSquid 32 = 4F 不能開火 + 28F 定身):不能走、不能變烏賊")]
        public int UnrelaxFrames;

        [Tooltip("可連續翻滾次數(Splat Dualies 2);硬直結束後重置")]
        public int MaxRolls;

        [Tooltip("每滾耗墨(標準墨槽的分數;SideStepParam.InkConsume 0.07)")]
        public float InkConsume;

        [Tooltip("翻滾後的墨量鎖(F;S3 70F)")]
        public int InkRecoverStopFrames;

        [Tooltip("落點塗墨半寬(u;SplashSlideParam.PaintWidthHalf 1.8)")]
        public float PaintWidthHalf;

        [Tooltip("空中翻滾的水平距離倍率 [EST]:S3 空中翻滾是往下急墜、過不了溝")]
        public float AirDistanceScale;

        [Tooltip("空中翻滾的急墜速度(u/F)[EST]:0.5 u/F = 30 m/s")]
        public float AirSlamSpeed;

        [Tooltip("觸發翻滾的最小搖桿推量(0~1;沒推方向 = 一般跳)")]
        public float MinStick;

        // S3 Splat Dualies(Maneuver_Normal):2 滾、每滾 7%、4.0 + 1.0 u、4F 起手 → 12F 翻滾 → 4F 不能開火 → 再定身 28F(共 48F)、
        // 落點塗半寬 1.8、墨量鎖 70F
        public static DodgeRollParams SplatDualies => new DodgeRollParams
        {
            StartupFrames = 4,
            MoveFrames = 12,
            Distance = 4f,
            SlideDistance = 1f,
            SlideFrames = 6,
            NoFireFrames = 4,
            UnrelaxFrames = 32,
            MaxRolls = 2,
            InkConsume = 0.07f,
            InkRecoverStopFrames = 70,
            PaintWidthHalf = 1.8f,
            AirDistanceScale = 0.35f,
            AirSlamSpeed = 0.5f,
            MinStick = 0.35f,
        };
    }

    public enum DodgeRollPhase : byte
    {
        // 沒在翻滾(或時間軸已走完)
        None = 0,
        // 起手:蹲下蓄勢,還沒位移
        Startup = 1,
        // 翻滾位移中
        Move = 2,
        // 翻滾結束的硬直(滑行、砲台架好、定身):可接下一滾,前 NoFireFrames 不能開火
        Unrelax = 3,
    }

    // 一次閃避翻滾的時間軸(純邏輯;以翻滾開始後經過的幀數 f 查詢,可為小數):
    //   0 ─ Startup ─ S ─ Move(等速 Distance)─ S+M ─ 滑行(二次緩停 SlideDistance)… ─ S+M+Unrelax = 結束
    // 位移曲線連續:滑行起速 = 2·SlideDistance/SlideFrames,與翻滾速度 Distance/MoveFrames 相同時(S3 數值剛好如此)速度也連續。
    // 呼叫端以「本幀 − 上一幀」的位移差換算速度,總位移與幀率無關。
    public static class DodgeRollModel
    {
        public static int TotalFrames(in DodgeRollParams p)
        {
            return p.StartupFrames + p.MoveFrames + p.UnrelaxFrames;
        }

        // 翻滾位移結束(可接下一滾、砲台架起)的幀
        public static int MoveEndFrame(in DodgeRollParams p)
        {
            return p.StartupFrames + p.MoveFrames;
        }

        // 可以再開火的幀(S3 4 + 12 + 4 = 20F)
        public static int FireResumeFrame(in DodgeRollParams p)
        {
            return p.StartupFrames + p.MoveFrames + p.NoFireFrames;
        }

        public static DodgeRollPhase PhaseAt(in DodgeRollParams p, float frames)
        {
            if (frames < 0f || frames >= TotalFrames(p))
            {
                return DodgeRollPhase.None;
            }
            if (frames < p.StartupFrames)
            {
                return DodgeRollPhase.Startup;
            }
            return frames < MoveEndFrame(p) ? DodgeRollPhase.Move : DodgeRollPhase.Unrelax;
        }

        // 沿翻滾方向的累計位移(u)
        public static float DistanceAt(in DodgeRollParams p, float frames)
        {
            if (frames <= p.StartupFrames)
            {
                return 0f;
            }
            float moving = frames - p.StartupFrames;
            if (moving < p.MoveFrames)
            {
                return p.MoveFrames > 0 ? p.Distance * moving / p.MoveFrames : p.Distance;
            }
            if (p.SlideFrames <= 0)
            {
                return p.Distance + p.SlideDistance;
            }
            float u = Mathf.Clamp01((moving - p.MoveFrames) / p.SlideFrames);
            float eased = 1f - (1f - u) * (1f - u);
            return p.Distance + p.SlideDistance * eased;
        }

        public static float TotalDistance(in DodgeRollParams p)
        {
            return p.Distance + p.SlideDistance;
        }

        public static bool CanFireAt(in DodgeRollParams p, float frames)
        {
            return frames >= FireResumeFrame(p);
        }

        public static bool CanChainAt(in DodgeRollParams p, float frames)
        {
            return frames >= MoveEndFrame(p);
        }

        // 整段時間軸都鎖住自由移動(翻滾中被帶著走、硬直中定身)
        public static bool IsLockedAt(in DodgeRollParams p, float frames)
        {
            return frames >= 0f && frames < TotalFrames(p);
        }

        // 翻滾位移速度(u/F;MoveFrames 期間)
        public static float RollSpeed(in DodgeRollParams p)
        {
            return p.MoveFrames > 0 ? p.Distance / p.MoveFrames : 0f;
        }
    }
}

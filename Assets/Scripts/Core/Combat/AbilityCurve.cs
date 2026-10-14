using UnityEngine;

namespace SplatoonC.Core.Combat
{
    // S3 裝備能力(只列暫時增益會給到的那些)。本作裝備一律 0 AP,AP 只來自暫時效果
    //(能量站 Tacticooler;之後若做 Comeback / Drop Roller / Flow Aura 也走同一條路)。
    public enum Ability : byte
    {
        RunSpeedUp = 0,
        SwimSpeedUp = 1,
        InkRecoveryUp = 2,
        InkResistanceUp = 3,
        SpecialChargeUp = 4,
        SpecialSaver = 5,
        QuickRespawn = 6,
        QuickSuperJump = 7,
        IntensifyAction = 8,
    }

    // 一組能力參數 [High(57 AP), Mid, Low(0 AP)](Leanny v11.3 參數表,research/01 §2.1)
    public readonly struct AbilityParam
    {
        public readonly float High;
        public readonly float Mid;
        public readonly float Low;

        public AbilityParam(float high, float mid, float low)
        {
            High = high;
            Mid = mid;
            Low = low;
        }

        public float Evaluate(float ap)
        {
            return AbilityCurve.Evaluate(this, ap);
        }
    }

    // S3 能力效果公式(DESIGN_REFERENCE §2.13、research/01 §2):
    //   ratio(ap) = min(0.033·ap − 0.00027·ap², 1)
    //   slope     = (Mid − Low) / (High − Low)
    //   t         = slope == 0.5 ? ratio : ratio^(ln slope / ln 0.5)
    //   value     = Low + (High − Low) · t
    // 暫時 AP 與裝備相加但總和上限 57;能量站與裝備「取高者」(本作裝備 0 AP,等價)。
    public static class AbilityCurve
    {
        public const int Count = 9;
        public const float MaxAp = 57f;

        // ── 參數表(u/F、F 或比率;與 research/01 §2.1 表逐欄對照)──
        // 跑速 MoveVel_Human(一般 / 輕 / 重)
        public static readonly AbilityParam RunMid = new AbilityParam(0.144f, 0.12f, 0.096f);
        public static readonly AbilityParam RunLight = new AbilityParam(0.144f, 0.124f, 0.104f);
        public static readonly AbilityParam RunHeavy = new AbilityParam(0.144f, 0.116f, 0.088f);
        // 開火走速倍率 MoveVelRt_Shot(RSU)
        public static readonly AbilityParam ShotMove = new AbilityParam(1.25f, 1.125f, 1f);
        // 游速 MoveVel_Stealth(一般 / 輕 / 重)
        public static readonly AbilityParam SwimMid = new AbilityParam(0.24f, 0.216f, 0.192f);
        public static readonly AbilityParam SwimLight = new AbilityParam(0.24f, 0.2208f, 0.2016f);
        public static readonly AbilityParam SwimHeavy = new AbilityParam(0.24f, 0.216f, 0.1728f);
        // 回墨滿罐幀數 InkRecoverFrm_Stealth / _Std(IRU)
        public static readonly AbilityParam InkRecoverSubmergedFrames = new AbilityParam(117f, 148.5f, 180f);
        public static readonly AbilityParam InkRecoverStandingFrames = new AbilityParam(220f, 410f, 600f);
        // 敵墨(Ink Resistance):走速、開火走速、起跳初速、持續傷害上限、每幀持續傷害、寬限幀
        public static readonly AbilityParam EnemyInkWalk = new AbilityParam(0.0768f, 0.0557f, 0.024f);
        public static readonly AbilityParam EnemyInkShotWalk = new AbilityParam(0.042f, 0.033f, 0.012f);
        public static readonly AbilityParam EnemyInkJumpVelocity = new AbilityParam(0.11f, 0.098f, 0.08f);
        public static readonly AbilityParam EnemyInkDamageCap = new AbilityParam(20f, 30f, 40f);
        public static readonly AbilityParam EnemyInkDamagePerFrame = new AbilityParam(0.15f, 0.225f, 0.3f);
        public static readonly AbilityParam EnemyInkGraceFrames = new AbilityParam(39f, 26f, 0f);
        // 大招:充能倍率 IncreaseRt_Special(SCU)、死亡保留比例 SpecialGaugeRt_Restart(Special Saver)
        public static readonly AbilityParam SpecialChargeRate = new AbilityParam(1.3f, 1.15f, 1f);
        public static readonly AbilityParam SpecialKeep = new AbilityParam(1f, 0.8f, 0.5f);
        // Quick Respawn:自身鏡頭 Dying_AroundFrm、擊殺鏡頭 Dying_ChaseFrm
        public static readonly AbilityParam OwnCameraFrames = new AbilityParam(30f, 60f, 90f);
        public static readonly AbilityParam KillCameraFrames = new AbilityParam(90f, 180f, 270f);
        // Quick Super Jump:蓄力 SuperJump_ChargeFrm、飛行 SuperJump_MoveFrm
        public static readonly AbilityParam SuperJumpChargeFrames = new AbilityParam(20f, 35f, 80f);
        public static readonly AbilityParam SuperJumpFlightFrames = new AbilityParam(96.6f, 132.3f, 138f);
        // Intensify Action:衝牆蓄力 WallJumpChargeFrm、連續翻滾速度係數、跳射散布移除比例
        public static readonly AbilityParam SquidSurgeChargeFrames = new AbilityParam(5f, 18f, 45f);
        public static readonly AbilityParam RollChainFactor = new AbilityParam(1f, 0.925f, 0.85f);
        public static readonly AbilityParam JumpSpreadReduction = new AbilityParam(1f, 0.75f, 0f);

        // 人形起跳初速(一般地面,u/F):敵墨起跳高度倍率 = (敵墨初速 / 此值)²
        public const float JumpVelocity = 0.11f;

        public static float Ratio(float ap)
        {
            float a = Mathf.Clamp(ap, 0f, MaxAp);
            return Mathf.Clamp01(0.033f * a - 0.00027f * a * a);
        }

        public static float Evaluate(in AbilityParam p, float ap)
        {
            float span = p.High - p.Low;
            if (Mathf.Abs(span) < 1e-9f)
            {
                return p.Low;
            }
            float ratio = Ratio(ap);
            if (ratio <= 0f)
            {
                return p.Low;
            }
            float slope = (p.Mid - p.Low) / span;
            float t;
            if (Mathf.Abs(slope - 0.5f) < 1e-6f)
            {
                t = ratio;
            }
            else if (slope <= 0f)
            {
                // Mid == Low:57 AP 前都停在 Low
                t = ratio >= 1f ? 1f : 0f;
            }
            else if (slope >= 1f)
            {
                t = 1f;
            }
            else
            {
                t = Mathf.Pow(ratio, Mathf.Log(slope) / Mathf.Log(0.5f));
            }
            return p.Low + span * t;
        }

        // ── 以「相對 0 AP 的倍率」表示的效果(0 AP → 1)──

        // 重量級別索引:0 = 輕、1 = 中、2 = 重(與 Gameplay 的 WeightClass 列舉值一致)
        public static float RunSpeedRatio(float ap, int weight)
        {
            AbilityParam p = weight == 0 ? RunLight : weight == 2 ? RunHeavy : RunMid;
            return p.Evaluate(ap) / p.Low;
        }

        public static float SwimSpeedRatio(float ap, int weight)
        {
            AbilityParam p = weight == 0 ? SwimLight : weight == 2 ? SwimHeavy : SwimMid;
            return p.Evaluate(ap) / p.Low;
        }

        public static float ShotMoveRatio(float ap)
        {
            return ShotMove.Evaluate(ap);
        }

        // 回墨速率倍率 = 0 AP 滿罐幀數 / 目前滿罐幀數
        public static float InkRecoveryRatio(float ap, bool submerged)
        {
            AbilityParam p = submerged ? InkRecoverSubmergedFrames : InkRecoverStandingFrames;
            return p.Low / Mathf.Max(1f, p.Evaluate(ap));
        }

        public static float EnemyInkWalkRatio(float ap)
        {
            return EnemyInkWalk.Evaluate(ap) / EnemyInkWalk.Low;
        }

        public static float EnemyInkShotWalkRatio(float ap)
        {
            return EnemyInkShotWalk.Evaluate(ap) / EnemyInkShotWalk.Low;
        }

        // 敵墨中起跳高度 / 一般起跳高度(高度 ∝ 初速²;0 AP = (0.08/0.11)² ≈ 0.53)
        public static float EnemyInkJumpHeightRatio(float ap)
        {
            float v = EnemyInkJumpVelocity.Evaluate(ap) / JumpVelocity;
            return v * v;
        }

        // 敵墨持續傷害(HP/秒):每幀值先取整到 0.1(S3:0 AP 0.3/F、3–25 AP 0.2、26–57 AP 0.1)
        public static float EnemyInkDamagePerSecond(float ap)
        {
            float perFrame = Mathf.Floor(EnemyInkDamagePerFrame.Evaluate(ap) * 10f + 1e-3f) / 10f;
            return perFrame * 60f;
        }

        // Quick Respawn 縮短的秒數(自身鏡頭 + 擊殺鏡頭;57 AP:(90−30)+(270−90) = 240F = 4 s)
        public static float QuickRespawnSavedSeconds(float ap)
        {
            if (ap <= 0f)
            {
                return 0f;
            }
            float own = OwnCameraFrames.Low - OwnCameraFrames.Evaluate(ap);
            float kill = KillCameraFrames.Low - KillCameraFrames.Evaluate(ap);
            return (own + kill) / 60f;
        }
    }
}

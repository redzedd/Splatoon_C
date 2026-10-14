using System;
using UnityEngine;

namespace SplatoonC.Core.Combat
{
    // 滾筒甩墨的距離分段傷害(S3 WideSwing / VerticalSwing UnitGroupParam.DamageParam.Inside / Outside;
    // datamine 傷害 ×10 存放,這裡已 ÷10 成 HP;距離 u = 公尺):
    //   ≤ MaxDistance            → MaxValue
    //   MaxDistance → HighDistance 線性到 HighValue
    //   HighDistance → LowDistance 線性到 LowValue
    //   LowDistance → MinDistance  線性到 MinValue,之後維持 MinValue(FinalDamageMinValue)
    // 例 Splat Roller 橫甩中央:150(≤ 4.2)→ 100(7.2)→ 35(10.7),一擊必殺距離 = 7.2。
    [Serializable]
    public struct RollerDamageCurve
    {
        [Tooltip("最大傷害(HP;DamageMaxValue ÷ 10)")]
        public float MaxValue;

        [Tooltip("維持最大傷害的最遠距離(u;DamageMaxDistance)")]
        public float MaxDistance;

        [Tooltip("第二段終點傷害(DamageHighValue ÷ 10)")]
        public float HighValue;

        [Tooltip("第二段終點距離(DamageHighDistance)")]
        public float HighDistance;

        [Tooltip("第三段終點傷害(DamageLowValue ÷ 10)")]
        public float LowValue;

        [Tooltip("第三段終點距離(DamageLowDistance)")]
        public float LowDistance;

        [Tooltip("最小傷害(DamageMinValue ÷ 10;之後維持)")]
        public float MinValue;

        [Tooltip("衰減到最小傷害的距離(DamageMinDistance)")]
        public float MinDistance;

        [Tooltip("用這條曲線的墨團偏角上限(度;Inside.Degree:|出手偏角| ≤ 此值 = 中央墨團)")]
        public float Degree;

        public RollerDamageCurve(float maxValue, float maxDistance, float highValue, float highDistance,
            float lowValue, float lowDistance, float minValue, float minDistance, float degree)
        {
            MaxValue = maxValue;
            MaxDistance = maxDistance;
            HighValue = highValue;
            HighDistance = highDistance;
            LowValue = lowValue;
            LowDistance = lowDistance;
            MinValue = minValue;
            MinDistance = minDistance;
            Degree = degree;
        }

        // 飛行距離 distance(u)處的傷害(未乘時間衰減、未取整)
        public float Evaluate(float distance)
        {
            if (distance <= MaxDistance)
            {
                return MaxValue;
            }
            if (distance < HighDistance)
            {
                return Segment(distance, MaxDistance, MaxValue, HighDistance, HighValue);
            }
            if (distance < LowDistance)
            {
                return Segment(distance, HighDistance, HighValue, LowDistance, LowValue);
            }
            if (distance < MinDistance)
            {
                return Segment(distance, LowDistance, LowValue, MinDistance, MinValue);
            }
            return MinValue;
        }

        // 傷害 ≥ hp 的最遠飛行距離(u;一擊必殺距離)。曲線單調遞減,逐段反解;最大傷害不到 hp 回傳 0
        public float LethalDistance(float hp = 100f)
        {
            if (MaxValue < hp)
            {
                return 0f;
            }
            float best = MaxDistance;
            best = Mathf.Max(best, SegmentReach(MaxDistance, MaxValue, HighDistance, HighValue, hp));
            best = Mathf.Max(best, SegmentReach(HighDistance, HighValue, LowDistance, LowValue, hp));
            best = Mathf.Max(best, SegmentReach(LowDistance, LowValue, MinDistance, MinValue, hp));
            return best;
        }

        private static float Segment(float x, float x0, float y0, float x1, float y1)
        {
            if (x1 <= x0)
            {
                return y1;
            }
            return Mathf.Lerp(y0, y1, (x - x0) / (x1 - x0));
        }

        // 一段 (x0, y0) → (x1, y1) 上傷害仍 ≥ hp 的最遠 x(整段都 ≥ hp 回傳 x1;起點就 < hp 回傳 0)
        private static float SegmentReach(float x0, float y0, float x1, float y1, float hp)
        {
            if (y0 < hp || x1 <= x0)
            {
                return 0f;
            }
            if (y1 >= hp)
            {
                return x1;
            }
            return x0 + (x1 - x0) * (y0 - hp) / (y0 - y1);
        }
    }

    // 一種甩法(橫甩 / 直甩)的完整傷害規則:中央/側邊兩條距離曲線 + 飛行幀數的時間衰減 + 墨不足弱甩倍率。
    // S3:DamageRejectStartFrame 之後線性降到 DamageRejectRate(50%),DamageRejectEndFrame 之後維持;
    // 最後向下取整到 0.1(VitalsModel.Floor01)。同一甩對同一目標只算最高的一顆(去重在武器端)。
    [Serializable]
    public struct RollerFlingDamage
    {
        [Tooltip("中央墨團的距離曲線(Inside)")]
        public RollerDamageCurve Inside;

        [Tooltip("側邊墨團(偏角 > Inside.Degree)的距離曲線(Outside)")]
        public RollerDamageCurve Outside;

        [Tooltip("時間衰減開始幀(DamageRejectStartFrame)")]
        public int RejectStartFrame;

        [Tooltip("時間衰減結束幀(DamageRejectEndFrame)")]
        public int RejectEndFrame;

        [Tooltip("時間衰減終值倍率(DamageRejectRate;S3 全部 0.5)")]
        public float RejectRate;

        [Tooltip("墨不足弱甩的傷害倍率(DepletionDamageRate;橫甩 0.25)")]
        public float DepletionDamageRate;

        // 飛行 frame 幀、距離 distance 命中時的傷害(HP,已取整到 0.1)。
        // unitRate = 該墨團所屬單位的傷害倍率(Dynamo 橫甩中央小墨團 UnitDamageRate 0 = 只塗不傷)
        public float Evaluate(float distance, int frame, bool inside, bool depleted, float unitRate = 1f)
        {
            float damage = (inside ? Inside : Outside).Evaluate(distance);
            damage *= TimeRate(frame);
            if (depleted)
            {
                damage *= DepletionDamageRate;
            }
            damage *= unitRate;
            return VitalsModel.Floor01(Mathf.Max(0f, damage));
        }

        // 第 frame 幀的時間衰減倍率(1 → RejectRate)
        public float TimeRate(int frame)
        {
            if (frame <= RejectStartFrame)
            {
                return 1f;
            }
            if (frame >= RejectEndFrame || RejectEndFrame <= RejectStartFrame)
            {
                return RejectRate;
            }
            float t = (frame - RejectStartFrame) / (float)(RejectEndFrame - RejectStartFrame);
            return Mathf.Lerp(1f, RejectRate, t);
        }

        // 墨團出手偏角(度,水平)是否算中央
        public bool IsInside(float yawDeg)
        {
            return Mathf.Abs(yawDeg) <= Inside.Degree + 1e-3f;
        }
    }
}

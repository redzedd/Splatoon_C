using UnityEngine;

namespace SplatoonC.Core.Combat
{
    // 一顆甩墨墨團的出生參數(相對甩墨基準點與瞄準方向)
    public struct RollerGlobSpawn
    {
        // 水平偏角(度,+ = 往右)
        public float YawDeg;
        // 仰角(度,+ = 往上;疊在瞄準仰角上)
        public float PitchDeg;
        // 初速(u/F)
        public float Speed;
        // 出生點橫向位移(u,+ = 往右)
        public float Lateral;
        // 出生點高度位移(u)
        public float Height;
    }

    // 滾筒的純邏輯:墨團排列、推滾衝刺計時。WeaponRoller 與 EditMode 測試共用。
    public static class RollerMotion
    {
        // 衝刺中每幀轉向超過此角度(度/F)= 急轉,速度掉回 SpeedDashTurnBreak、衝刺重算 [EST](S3 只公開急轉速度)
        public const float TurnBreakDegPerFrame = 2f;

        // 單位 unit 的第 index 顆(本次共 count 顆)的出生參數。speedRandom01 / jitter01 ∈ [0,1)(呼叫端給亂數,測試給固定值)。
        // 橫甩:偏角在 ±WideDegree 內等分(加一點不超過四分之一間距的抖動),出生點沿 PositionWidth 橫線等分;
        // 只有一顆時偏角在 ±WideDegree/2 內隨機。直甩:逐顆 AfterOffset 變慢、仰角與高度遞減。
        public static RollerGlobSpawn Layout(in RollerFlingUnit unit, int index, int count, bool depleted,
            float speedRandom01, float jitter01)
        {
            float yaw;
            float lateral;
            if (count > 1)
            {
                float t = index / (float)(count - 1);
                float step = 2f * unit.WideDegree / (count - 1);
                yaw = Mathf.Lerp(-unit.WideDegree, unit.WideDegree, t) + (jitter01 - 0.5f) * 0.5f * step;
                yaw = Mathf.Clamp(yaw, -unit.WideDegree, unit.WideDegree);
                lateral = Mathf.Lerp(-0.5f, 0.5f, t) * unit.PositionWidth;
            }
            else
            {
                yaw = (jitter01 - 0.5f) * unit.WideDegree;
                lateral = 0f;
            }
            float speed = unit.SpawnSpeed + unit.AfterOffsetSpeed * index
                          + (speedRandom01 * 2f - 1f) * unit.SpawnSpeedRandom;
            if (depleted)
            {
                speed *= unit.DepletionSpeedRate > 0f ? unit.DepletionSpeedRate : 1f;
            }
            return new RollerGlobSpawn
            {
                YawDeg = yaw,
                PitchDeg = unit.PitchDeg + unit.AfterOffsetPitchDeg * index,
                Speed = Mathf.Max(0.05f, speed),
                Lateral = lateral,
                Height = unit.HeightOffset + unit.HeightStep * index,
            };
        }
    }

    // 推滾的衝刺計時(S3 WeaponRollParam):連續推滾(有在移動)DashFrame 幀後速度升到 SpeedDash;
    // 停下(< SpeedInkConsumeMin)或衝刺中急轉 → 從頭算。以幀(可小數)累加,任何幀率結果相同。
    public struct RollerDashTracker
    {
        public float RollFrames;

        public bool IsDashing(in RollerRollParams roll)
        {
            return RollFrames >= roll.DashFrame;
        }

        public void Reset()
        {
            RollFrames = 0f;
        }

        // 前進 frames 幀;speed = 實際推滾速度(u/F);turnDegPerFrame = 面向轉速(度/F,取絕對值)。
        // 回傳這一幀要用的推滾速度上限(u/F),turning = 這一幀是衝刺急轉
        public float Advance(in RollerRollParams roll, float frames, float speed, float turnDegPerFrame,
            out bool turning)
        {
            turning = false;
            bool moving = speed >= roll.SpeedInkConsumeMin;
            if (!moving)
            {
                RollFrames = 0f;
                return roll.SpeedNormal;
            }
            bool dashing = IsDashing(roll);
            if (dashing && Mathf.Abs(turnDegPerFrame) > RollerMotion.TurnBreakDegPerFrame)
            {
                turning = true;
                RollFrames = 0f;
                return roll.SpeedFor(true, true);
            }
            RollFrames += frames;
            return roll.SpeedFor(IsDashing(roll), false);
        }
    }
}

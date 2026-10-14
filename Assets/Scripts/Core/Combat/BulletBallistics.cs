using System;
using UnityEngine;

namespace SplatoonC.Core.Combat
{
    // S3 投射物的三個飛行狀態(DESIGN_REFERENCE §3.1;research 07 §2.2 JP-SYS「弾道、弾速、射程について」)
    public enum BulletPhase : byte
    {
        // 直線:SpawnSpeed 等速、無重力無阻力
        Straight = 0,
        // 減速:每幀先阻力後重力,XZ/Y 有下限
        Brake = 1,
        // 自由落體:阻力 + 重力,終端落速 −FreeGravity / FreeAirResist
        Free = 2,
    }

    // 彈道參數(S3 MoveParam,單位 u/F、u/F²,1 u = 1 m)。所有數值原樣取自 datamine,不做秒換算。
    [Serializable]
    public struct BulletMotion
    {
        [Tooltip("初速(u/F;×60 = m/s)。S3 Splattershot 2.266")]
        public float SpawnSpeed;

        [Tooltip("直線狀態幀數 GoStraightToBrakeStateFrame(Splattershot 4;爆破槍 = BurstFrame − 4)")]
        public int StraightFrames;

        [Tooltip("進入減速時的速度上限 GoStraightStateEndMaxSpeed(u/F;Splattershot 1.493;滾筒/潑桶/弓給大值 = 不夾)")]
        public float StraightEndMaxSpeed;

        [Tooltip("減速阻力 BrakeAirResist(每幀 v *= 1 − 值;射擊槍 0.36)")]
        public float BrakeAirResist;

        [Tooltip("減速重力 BrakeGravity(u/F²;射擊槍 0.07)")]
        public float BrakeGravity;

        [Tooltip("轉自由落體的水平速度下限 BrakeToFreeVelocityXZ(射擊槍 0.2355;弓/潑桶 20 = 跳過減速;0 = 無下限)")]
        public float BrakeToFreeVelocityXZ;

        [Tooltip("轉自由落體的垂直速度下限 BrakeToFreeVelocityY(射擊槍 −0.15;弓/潑桶 10 = 跳過減速;≤ −1000 = 無下限)")]
        public float BrakeToFreeVelocityY;

        [Tooltip("自由落體阻力 FreeAirResist(射擊槍 0.02)")]
        public float FreeAirResist;

        [Tooltip("自由落體重力 FreeGravity(u/F²;射擊槍 0.016 → 終端 −0.8 u/F)")]
        public float FreeGravity;

        // 「無下限」的 Y 值(滾筒、刮水刀:永遠留在減速狀態)
        public const float NoYLimit = -1000f;

        // 射擊槍、加特林、雙槍、傘、爆破槍共用的減速/自由落體常數(§3.1 表第一列)
        public static BulletMotion ShooterFamily(float spawnSpeed, int straightFrames, float straightEndMaxSpeed)
        {
            return new BulletMotion
            {
                SpawnSpeed = spawnSpeed,
                StraightFrames = straightFrames,
                StraightEndMaxSpeed = straightEndMaxSpeed,
                BrakeAirResist = 0.36f,
                BrakeGravity = 0.07f,
                BrakeToFreeVelocityXZ = 0.2355f,
                BrakeToFreeVelocityY = -0.15f,
                FreeAirResist = 0.02f,
                FreeGravity = 0.016f,
            };
        }

        // 滾筒、筆刷的甩墨:減速 0.1 / 0.04,沒有下限也沒有自由落體段(= 一直用減速參數)
        public static BulletMotion RollerFamily(float spawnSpeed, int straightFrames)
        {
            return new BulletMotion
            {
                SpawnSpeed = spawnSpeed,
                StraightFrames = straightFrames,
                StraightEndMaxSpeed = 1000f,
                BrakeAirResist = 0.1f,
                BrakeGravity = 0.04f,
                BrakeToFreeVelocityXZ = 0f,
                BrakeToFreeVelocityY = NoYLimit,
                FreeAirResist = 0.1f,
                FreeGravity = 0.04f,
            };
        }

        // 弓:下限 20 / 10 → 直線結束直接進自由落體(0.24 / 0.15)
        public static BulletMotion StringerFamily(float spawnSpeed, int straightFrames)
        {
            return new BulletMotion
            {
                SpawnSpeed = spawnSpeed,
                StraightFrames = straightFrames,
                StraightEndMaxSpeed = 1000f,
                BrakeAirResist = 0.1f,
                BrakeGravity = 0.04f,
                BrakeToFreeVelocityXZ = 20f,
                BrakeToFreeVelocityY = 10f,
                FreeAirResist = 0.24f,
                FreeGravity = 0.15f,
            };
        }

        // 潑桶:下限 20 / 10 → 直線結束直接進自由落體(0.12 / 0.05);發射時另加 10–20% 向上速度(由武器處理)
        public static BulletMotion SlosherFamily(float spawnSpeed, int straightFrames)
        {
            return new BulletMotion
            {
                SpawnSpeed = spawnSpeed,
                StraightFrames = straightFrames,
                StraightEndMaxSpeed = 1000f,
                BrakeAirResist = 0.1f,
                BrakeGravity = 0.04f,
                BrakeToFreeVelocityXZ = 20f,
                BrakeToFreeVelocityY = 10f,
                FreeAirResist = 0.12f,
                FreeGravity = 0.05f,
            };
        }

        // 刮水刀墨波:無阻力;橫砍重力 0、蓄力砍 0.0027(Wiper)/ 0.02(Stamper);無下限
        public static BulletMotion SplatanaFamily(float spawnSpeed, int straightFrames, float gravity)
        {
            return new BulletMotion
            {
                SpawnSpeed = spawnSpeed,
                StraightFrames = straightFrames,
                StraightEndMaxSpeed = 1000f,
                BrakeAirResist = 0f,
                BrakeGravity = gravity,
                BrakeToFreeVelocityXZ = 0f,
                BrakeToFreeVelocityY = NoYLimit,
                FreeAirResist = 0f,
                FreeGravity = gravity,
            };
        }
    }

    // 飛行中的狀態(呼叫端持有;位置公尺、速度 u/F = 公尺/幀)
    public struct BulletState
    {
        public Vector3 Position;
        public Vector3 Velocity;
        public BulletPhase Phase;
        // 已飛行幀數(Step 一次 +1;第一次 Step 完 = 1)。傷害衰減以此計(§3.1「以飛行幀數計」)
        public int Frame;
        // 沿彈道累計飛行距離(公尺)
        public float Travelled;
        // 最近一步的位移(公尺)
        public Vector3 LastDisplacement;
    }

    // S3 投射物三狀態彈道的逐幀純邏輯(60 Hz 一步)。InkBullet、預覽、測試共用同一個步進。
    //
    // STRAIGHT:pos += dir·SpawnSpeed(可疊加玩家速度),持續 StraightFrames 幀。
    // BRAKE:進入時 |v| = min(|v|, StraightEndMaxSpeed);每幀(第一幀就開始)v *= 1 − BrakeAirResist,vY −= BrakeGravity;
    //        |vXZ| 低於下限 → 該幀夾到下限;vY 低於下限 → 該幀夾到下限;兩者同時低於 → 轉 FREE。
    // FREE:v *= 1 − FreeAirResist,vY −= FreeGravity。
    //
    // 下限語意的兩個細節(JP-SYS 原文「その値に固定」,本專案解讀):
    // 1. 進入減速的那一刻若速度已經兩者都低於下限(弓/潑桶把下限設成 20/10)→ 直接轉 FREE,不夾——
    //    否則箭會被「夾」到 20 u/F。這就是「下限 20/10 = 跳過減速」。
    // 2. 減速途中夾住的值會存回速度(下一幀從夾住的值繼續衰減再夾),因此兩者都觸底時必然轉 FREE。
    public static class BulletBallistics
    {
        public static BulletState Launch(Vector3 position, Vector3 direction, in BulletMotion motion,
            Vector3 inheritPerFrame = default)
        {
            Vector3 dir = direction.sqrMagnitude > 1e-8f ? direction.normalized : Vector3.forward;
            var state = new BulletState
            {
                Position = position,
                Velocity = dir * motion.SpawnSpeed + inheritPerFrame,
                Phase = BulletPhase.Straight,
                Frame = 0,
                Travelled = 0f,
            };
            if (motion.StraightFrames <= 0)
            {
                EnterBrake(ref state, motion);
            }
            return state;
        }

        // 前進一幀(1/60 秒);回傳本幀位移(公尺)
        public static Vector3 Step(ref BulletState state, in BulletMotion motion)
        {
            if (state.Phase == BulletPhase.Straight && state.Frame >= motion.StraightFrames)
            {
                EnterBrake(ref state, motion);
            }

            Vector3 v = state.Velocity;
            switch (state.Phase)
            {
                case BulletPhase.Brake:
                    v *= 1f - motion.BrakeAirResist;
                    v.y -= motion.BrakeGravity;
                    float xzSq = v.x * v.x + v.z * v.z;
                    float xzLimit = motion.BrakeToFreeVelocityXZ;
                    bool xzBelow = xzLimit > 0f && xzSq < xzLimit * xzLimit;
                    bool yBelow = v.y < motion.BrakeToFreeVelocityY;
                    if (xzBelow)
                    {
                        float xz = Mathf.Sqrt(xzSq);
                        if (xz > 1e-6f)
                        {
                            float scale = xzLimit / xz;
                            v.x *= scale;
                            v.z *= scale;
                        }
                    }
                    if (yBelow)
                    {
                        v.y = motion.BrakeToFreeVelocityY;
                    }
                    if (xzBelow && yBelow)
                    {
                        state.Phase = BulletPhase.Free;
                    }
                    break;
                case BulletPhase.Free:
                    v *= 1f - motion.FreeAirResist;
                    v.y -= motion.FreeGravity;
                    break;
            }

            state.Velocity = v;
            state.Position += v;
            state.Frame++;
            state.Travelled += v.magnitude;
            state.LastDisplacement = v;
            return v;
        }

        private static void EnterBrake(ref BulletState state, in BulletMotion motion)
        {
            state.Phase = BulletPhase.Brake;
            float speed = state.Velocity.magnitude;
            if (speed > motion.StraightEndMaxSpeed && speed > 1e-6f)
            {
                state.Velocity *= motion.StraightEndMaxSpeed / speed;
            }
            // 進入時兩者都已低於下限(弓/潑桶的 20/10)→ 直接自由落體
            Vector3 v = state.Velocity;
            float xzLimit = motion.BrakeToFreeVelocityXZ;
            bool xzBelow = xzLimit > 0f && v.x * v.x + v.z * v.z < xzLimit * xzLimit;
            if (xzBelow && v.y < motion.BrakeToFreeVelocityY)
            {
                state.Phase = BulletPhase.Free;
            }
        }

        // 終端落速(u/F,負值):自由落體 vY 收斂到 −FreeGravity / FreeAirResist(射擊槍 −0.8)
        public static float TerminalFallSpeed(in BulletMotion motion)
        {
            return motion.FreeAirResist > 0f ? -motion.FreeGravity / motion.FreeAirResist : float.NegativeInfinity;
        }

        // 有效射程(準星鎖定距離,公尺;§3.1):R = A·N + E·(1−r)·(1−(1−r)^(G−N))/r + EndRadiusForPlayer
        // A = SpawnSpeed、N = 直線幀、E = min(A, StraightEndMaxSpeed)、r = BrakeAirResist、G = ShotGuideFrame
        public static float EffectiveRange(in BulletMotion motion, int guideFrames, float endRadiusForPlayer)
        {
            int n = Mathf.Max(0, motion.StraightFrames);
            float straight = motion.SpawnSpeed * Mathf.Min(n, guideFrames);
            int brakeFrames = guideFrames - n;
            if (brakeFrames <= 0)
            {
                return straight + endRadiusForPlayer;
            }
            float e = Mathf.Min(motion.SpawnSpeed, motion.StraightEndMaxSpeed);
            float r = motion.BrakeAirResist;
            float brake = r > 1e-6f
                ? e * (1f - r) * (1f - Mathf.Pow(1f - r, brakeFrames)) / r
                : e * brakeFrames;
            return straight + brake + endRadiusForPlayer;
        }

        // 平地水平射擊的落點預測:從高度 height(公尺,球心離地)水平發射,直到球心低於 fieldRadius
        //(sphere cast 碰到地面)為止。回傳落地幀數與水平距離(幀內線性插值)。maxFrames 內沒落地回傳 false。
        public static bool PredictFlatLanding(in BulletMotion motion, float height, float fieldRadius,
            out int frames, out float distance, int maxFrames = 600)
        {
            BulletState state = Launch(new Vector3(0f, height, 0f), Vector3.forward, motion);
            for (int i = 0; i < maxFrames; i++)
            {
                Vector3 before = state.Position;
                Step(ref state, motion);
                if (state.Position.y <= fieldRadius)
                {
                    float dy = before.y - state.Position.y;
                    float t = dy > 1e-6f ? (before.y - fieldRadius) / dy : 1f;
                    frames = state.Frame;
                    distance = Mathf.Lerp(before.z, state.Position.z, Mathf.Clamp01(t));
                    return true;
                }
            }
            frames = maxFrames;
            distance = state.Position.z;
            return false;
        }
    }
}

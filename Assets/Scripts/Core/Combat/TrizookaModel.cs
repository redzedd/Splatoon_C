using UnityEngine;

namespace SplatoonC.Core.Combat
{
    // Trizooka(S3 SpUltraShot,research/04 §2.1、JP wiki ウルトラショット)的純邏輯:彈道、螺旋、節奏、瞄準歸零。
    //
    // 每發 = 3 顆墨團,每 4F 射出一顆,繞同一條彈道軸螺旋(OrbitalRadiusEnd 1.0,10F 內長滿)。
    // 螺旋角速度 15°/F 與各顆相位差 180° 是從兩條觀察推導的 [推導]:
    //   (1) 同一時刻三顆相隔 120°(正面看是正三角形):相位差 − 4F × ω = 120° → 180° − 60° = 120° ✓;
    //   (2) 到達同一距離時三顆排成一直線(「△ 不是 △,是 I」):相位差 180° → 0°/180°/0° ✓。
    public static class TrizookaModel
    {
        // 彈道(MoveParam):初速 1.125 u/F、直線 16F、進減速上限 1.0;減速 0.09/0.09、下限 1.0/−0.1;自由 0.01985/0.0190565
        public static BulletMotion Motion => new BulletMotion
        {
            SpawnSpeed = 1.125f,
            StraightFrames = 16,
            StraightEndMaxSpeed = 1f,
            BrakeAirResist = 0.09f,
            BrakeGravity = 0.09f,
            BrakeToFreeVelocityXZ = 1f,
            BrakeToFreeVelocityY = -0.1f,
            FreeAirResist = 0.01985f,
            FreeGravity = 0.0190565f,
        };

        public const int GlobsPerShot = 3;
        public const int GlobIntervalFrames = 4;
        public const float OrbitRadius = 1f;
        public const int OrbitGrowFrames = 10;
        public const float OrbitDegPerFrame = 15f;
        public const float GlobPhaseStepDeg = 180f;

        // 節奏(WeaponSpUltraShotParam + JP wiki):發動後 23F 才能開始射、按下後 15F 出膛、出膛後 40F 硬直 → 連射 55F
        public const int StartDelayFrames = 23;
        public const int ShotDelayFrames = 15;
        public const int PostShotFrames = 40;
        public const int RepeatFrames = ShotDelayFrames + PostShotFrames;
        public const int Shots = 3;
        public const int DurationFrames = 330;

        // 走速(u/F):平常 0.07、射擊前後 0.04;烏賊速約 1.82 DU/F(一般中量級 1.92)[JP wiki 約]
        public const float MoveSpeed = 0.07f;
        public const float MoveSpeedInShot = 0.04f;
        public const float SwimSpeedRatio = 1.82f / 1.92f;

        // 瞄準歸零距離:直擊射程 5.8 條試射線 = 29 u;中距離彈道高於準心(JP wiki:3.1–3.5 條處正面會打不中)
        public const float ZeroDistance = 29f;

        // 第 k 顆(0..2)在自己飛行第 frame 幀的螺旋角(度);phase0 = 第一顆的起始角
        public static float OrbitAngleDeg(int globIndex, int frame, float phase0Deg = 90f)
        {
            return phase0Deg + globIndex * GlobPhaseStepDeg + OrbitDegPerFrame * frame;
        }

        // 第 frame 幀的螺旋半徑(10F 內 0 → 1.0)
        public static float OrbitRadiusAt(int frame)
        {
            return OrbitRadius * Mathf.Clamp01(frame / (float)OrbitGrowFrames);
        }

        // 螺旋偏移:以發射方向為軸的右/上基底
        public static Vector3 OrbitOffset(Vector3 right, Vector3 up, float angleDeg, float radius)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            return (right * Mathf.Cos(rad) + up * Mathf.Sin(rad)) * radius;
        }

        // 發射方向的正交基底(right 水平;幾乎垂直發射時退回世界 X)
        public static void OrbitBasis(Vector3 direction, out Vector3 right, out Vector3 up)
        {
            Vector3 dir = direction.sqrMagnitude > 1e-8f ? direction.normalized : Vector3.forward;
            right = Vector3.Cross(Vector3.up, dir);
            if (right.sqrMagnitude < 1e-6f)
            {
                right = Vector3.right;
            }
            right.Normalize();
            up = Vector3.Cross(dir, right);
        }

        // 歸零:求發射仰角(弧度),讓彈道軸在水平距離 horizontal 處的高度差 = vertical(相對發射點)。
        // 以真實三狀態彈道模擬 + 割線法迭代(高度對仰角幾乎線性,3–4 次收斂到 1 cm);到不了時回傳最後的估計。
        public static float SolveElevation(in BulletMotion motion, float horizontal, float vertical, int iterations = 6)
        {
            float a0 = Mathf.Atan2(vertical, Mathf.Max(0.01f, horizontal));
            if (horizontal <= 0.01f)
            {
                return a0;
            }
            float h0 = HeightAt(motion, a0, horizontal);
            if (Mathf.Abs(vertical - h0) < 0.005f)
            {
                return a0;
            }
            float a1 = Mathf.Clamp(a0 + 0.03f, -1.45f, 1.45f);
            float h1 = HeightAt(motion, a1, horizontal);
            for (int i = 0; i < iterations; i++)
            {
                if (Mathf.Abs(vertical - h1) < 0.005f)
                {
                    break;
                }
                float slope = (h1 - h0) / (a1 - a0);
                if (Mathf.Abs(slope) < 1e-4f)
                {
                    break;
                }
                float a2 = Mathf.Clamp(a1 + (vertical - h1) / slope, -1.45f, 1.45f);
                a0 = a1;
                h0 = h1;
                a1 = a2;
                h1 = HeightAt(motion, a1, horizontal);
            }
            return a1;
        }

        // 以仰角 elevation(弧度)發射,彈道軸到水平距離 horizontal 時的高度(相對發射點);到不了回傳最後高度
        public static float HeightAt(in BulletMotion motion, float elevation, float horizontal, int maxFrames = 240)
        {
            Vector3 dir = new Vector3(0f, Mathf.Sin(elevation), Mathf.Cos(elevation));
            BulletState state = BulletBallistics.Launch(Vector3.zero, dir, motion);
            for (int i = 0; i < maxFrames; i++)
            {
                Vector3 before = state.Position;
                BulletBallistics.Step(ref state, motion);
                if (state.Position.z >= horizontal)
                {
                    float dz = state.Position.z - before.z;
                    float t = dz > 1e-6f ? (horizontal - before.z) / dz : 1f;
                    return Mathf.Lerp(before.y, state.Position.y, t);
                }
                if (state.Position.y < -60f)
                {
                    break;
                }
            }
            return state.Position.y;
        }
    }
}

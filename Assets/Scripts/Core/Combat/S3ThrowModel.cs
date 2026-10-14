using System;
using UnityEngine;

namespace SplatoonC.Core.Combat
{
    // S3 通用投擲物彈道參數(research 03 §1「Generic thrown-projectile physics」、DESIGN_REFERENCE §4.2;u = m、F = 1/60 s)。
    // Torpedo / Fizzy Bomb / Point Sensor 的設定資產各內嵌一份,只差 SpawnSpeedZ(1.40 / 1.36 / 1.38 u/F)。
    [Serializable]
    public struct S3ThrowParams
    {
        [Tooltip("視線方向出手速度(u/F;S3 SpawnSpeedZSpecUp.Low,0 AP)")]
        public float SpawnSpeedZ;

        [Tooltip("額外往上的出手速度(u/F;S3 SpawnSpeedY 0.24)")]
        public float SpawnSpeedY;

        [Tooltip("出手垂直速度下限(u/F;S3 SpawnSpeedYWorldMin −0.4:往正下方丟不會把炸彈砸進地裡)")]
        public float SpawnSpeedYWorldMin;

        [Tooltip("空中重力(u/F²;S3 FlyGravity 0.016)")]
        public float FlyGravity;

        [Tooltip("空中阻力(每幀速度 ×(1 − 此值);S2 mFly_VelKd 0.94134 → 0.05866)")]
        public float FlyAirResist;

        [Tooltip("繼承投擲者橫向速度倍率(S3 XRate 1.6)")]
        public float InheritLateral;

        [Tooltip("繼承投擲者前向速度倍率(S3 ZRate 2.0;檔案沒列的沿用 Fizzy 的 2.0——與 JP「帶動量 5.0 線」吻合)")]
        public float InheritForward;

        [Tooltip("上升中繼承垂直速度倍率(S3 YPlusRate 4.0)")]
        public float InheritUpRate;

        [Tooltip("繼承垂直速度上限(u/F;S3 YMax 0.32)")]
        public float InheritUpMax;

        // S3 通用投擲參數(SpawnSpeedY 0.24、下限 −0.4、g 0.016、阻力 0.05866、動量 1.6 / 2.0 / 4.0 ≤ 0.32)
        public static S3ThrowParams Bomb(float spawnSpeedZ)
        {
            return new S3ThrowParams
            {
                SpawnSpeedZ = spawnSpeedZ,
                SpawnSpeedY = 0.24f,
                SpawnSpeedYWorldMin = -0.4f,
                FlyGravity = 0.016f,
                FlyAirResist = 0.05866f,
                InheritLateral = 1.6f,
                InheritForward = 2f,
                InheritUpRate = 4f,
                InheritUpMax = 0.32f,
            };
        }
    }

    // S3 通用投擲彈道(逐幀 60 Hz):
    //   出手 v = 視線 × SpawnSpeedZ + 上 × SpawnSpeedY;v.y ≥ SpawnSpeedYWorldMin;
    //         再加投擲者動量(橫向 × XRate、前向 × ZRate;上升中 + min(vY × YPlusRate, YMax))。
    //   每幀 pos += v;v ×= (1 − 0.05866);v.y −= 0.016。
    //   驗算(出手高 1 u、平視):1.12 u/F 約 31F 落地 16.2 u(JP 3.0~3.5 線);1.40 → 20.2 u(Torpedo,JP 4.4 線);
    //   1.36 → 19.6 u(Fizzy,JP 4.2 線);1.38 → 19.9 u(Point Sensor,JP 4.0 線);帶衝刺 + 跳躍動量約 29 u(JP 6.0 線)。
    // 投擲物實飛與 ThrowController 預覽都走這一套步進(預覽 = 實際)。
    public static class S3ThrowModel
    {
        // 出手初速(u/F):aimDirection = 視線(單位向量);ownerVelocity = 投擲者速度(u/F)
        public static Vector3 LaunchVelocity(in S3ThrowParams p, Vector3 aimDirection, Vector3 ownerVelocity)
        {
            Vector3 aim = aimDirection.sqrMagnitude > 1e-8f ? aimDirection.normalized : Vector3.forward;
            Vector3 v = aim * p.SpawnSpeedZ + Vector3.up * p.SpawnSpeedY;
            v.y = Mathf.Max(v.y, p.SpawnSpeedYWorldMin);

            Vector3 forward = new Vector3(aim.x, 0f, aim.z);
            if (forward.sqrMagnitude < 1e-8f)
            {
                forward = Vector3.forward;
            }
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 flatOwner = new Vector3(ownerVelocity.x, 0f, ownerVelocity.z);
            v += forward * (Vector3.Dot(flatOwner, forward) * p.InheritForward)
                 + right * (Vector3.Dot(flatOwner, right) * p.InheritLateral);
            if (ownerVelocity.y > 0f)
            {
                v.y += Mathf.Min(ownerVelocity.y * p.InheritUpRate, p.InheritUpMax);
            }
            return v;
        }

        // 空中一幀(u/F):先移動,再套阻力與重力
        public static void StepAir(in S3ThrowParams p, ref Vector3 position, ref Vector3 velocity)
        {
            position += velocity;
            velocity *= 1f - p.FlyAirResist;
            velocity.y -= p.FlyGravity;
        }

        // 無限平地上的落地水平距離(u):出手高 launchHeight、視線俯仰 pitchDeg(負 = 往下看),不帶動量
        public static float PredictFlatRange(in S3ThrowParams p, float pitchDeg, float launchHeight, out int frames)
        {
            float rad = pitchDeg * Mathf.Deg2Rad;
            Vector3 aim = new Vector3(0f, Mathf.Sin(rad), Mathf.Cos(rad));
            Vector3 v = LaunchVelocity(p, aim, Vector3.zero);
            Vector3 position = new Vector3(0f, launchHeight, 0f);
            frames = 0;
            for (int i = 0; i < 600; i++)
            {
                Vector3 previous = position;
                StepAir(p, ref position, ref v);
                frames++;
                if (position.y <= 0f)
                {
                    // 這一步內線性內插到 y = 0
                    float t = previous.y / Mathf.Max(1e-6f, previous.y - position.y);
                    return Mathf.Lerp(previous.z, position.z, t);
                }
            }
            return position.z;
        }

        // 貼地滾動一幀的水平速度(u/F;S3 GroundPositionHorizonAirResist:每幀 ×(1 − resist))
        public static float StepGroundSpeed(float speed, float resist)
        {
            return speed * (1f - Mathf.Clamp01(resist));
        }
    }
}

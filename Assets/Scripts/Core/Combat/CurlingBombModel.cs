using System;
using UnityEngine;

namespace SplatoonC.Core.Combat
{
    // Curling Bomb 的 S3 參數(Leanny v11.3.0 WeaponBombCurling;u = m、F = 1/60 s、速度 u/F)。
    // 設定資產內嵌一份(可序列化),純邏輯 CurlingBombModel 與執行期 CurlingBomb、拋物線預覽、bot 都讀同一份。
    [Serializable]
    public struct CurlingBombParams
    {
        [Header("蓄力與引信")]
        [Tooltip("蓄力上限(F;S3 MaxChargeFrame 60)")]
        public int MaxChargeFrames;

        [Tooltip("按下後到開始蓄力的出手準備(F;[EST] 同 Fizzy:炸彈 5F 後才出現在手上)")]
        public int ChargeWindupFrames;

        [Tooltip("未蓄力引信(F;S3 BurstFrame 210,放開起算)")]
        public int BurstFrame;

        [Tooltip("每蓄 1F 扣幾 F 引信(S3 ChargeFrameBlastRate 2.0 → 滿蓄 90F)")]
        public float ChargeFrameBlastRate;

        [Header("出手(S3 低拋,u/F)")]
        [Tooltip("未蓄力出手速度(S3 SpawnSpeedZ 0.40)")]
        public float SpawnSpeedZ;

        [Tooltip("滿蓄出手速度(S3 SpawnSpeedZMaxCharge 0.20)")]
        public float SpawnSpeedZMaxCharge;

        [Tooltip("上拋速度(S3 SpawnSpeedY 0.12)")]
        public float SpawnSpeedY;

        [Tooltip("出手垂直速度下限(S3 SpawnSpeedYWorldMin −0.5:往正下方丟不會砸穿地)")]
        public float SpawnSpeedYWorldMin;

        [Tooltip("繼承玩家橫向速度倍率(S3 XRate 0.8)")]
        public float InheritLateral;

        [Tooltip("繼承玩家前向速度倍率(S3 ZRate 1.2:往前走丟得快、往後退丟得慢)")]
        public float InheritForward;

        [Tooltip("上升中繼承垂直速度倍率(S3 YPlusRate 2.0)")]
        public float InheritUpRate;

        [Tooltip("繼承垂直速度上限(S3 YMax 0.16)")]
        public float InheritUpMax;

        [Header("空中(u/F、u/F²)")]
        [Tooltip("空中重力(S3 FlyGravity 0.016)")]
        public float FlyGravity;

        [Tooltip("空中阻力(每幀速度 ×(1 − 此值);S3 FlyPositionAirResist 0.05866)")]
        public float FlyAirResist;

        [Header("滑行(u/F)")]
        [Tooltip("未蓄力巡航速度(S3 BaseSpeedMinCharge 0.22)")]
        public float BaseSpeedMinCharge;

        [Tooltip("滿蓄巡航速度(JP 實測 1.0 DU/F = 0.10 u/F)")]
        public float BaseSpeedMaxCharge;

        [Tooltip("比巡航快時,超出量每幀保留比例(S3 BaseSpeedComeOverRate 0.92)")]
        public float ComeOverRate;

        [Tooltip("比巡航慢時,不足量每幀保留比例(S3 BaseSpeedComeUnderRate 0.96)")]
        public float ComeUnderRate;

        [Tooltip("剩幾 F 開始減速(S3 BurstTimingSpeedStartRestFrame 90)")]
        public int SlowdownRestFrames;

        [Tooltip("減速曲線偏移(S3 BurstTimingSpeedStopBias 0.41;[EST] 巡航 ×(剩餘比例)^(1/0.41))")]
        public float StopBias;

        [Header("墨量鎖(F)")]
        [Tooltip("未蓄力墨量鎖(S3 InkRecoverStop 70)")]
        public int InkLockFrames;

        [Tooltip("滿蓄墨量鎖(S3 InkRecoverStopMaxCharge 30)")]
        public int InkLockFramesMaxCharge;

        [Header("爆炸(依蓄力內插)")]
        [Tooltip("未蓄力近距半徑(180 傷;S3 BlastParamMinCharge 1.6)")]
        public float NearRadiusMinCharge;

        [Tooltip("滿蓄近距半徑(S3 BlastParamMaxCharge 4.6)")]
        public float NearRadiusMaxCharge;

        [Tooltip("未蓄力遠距半徑(30 傷;S3 5.0)")]
        public float FarRadiusMinCharge;

        [Tooltip("滿蓄遠距半徑(S3 8.0)")]
        public float FarRadiusMaxCharge;

        [Tooltip("未蓄力爆炸塗墨半徑(S3 PaintRadius 2.133)")]
        public float PaintRadiusMinCharge;

        [Tooltip("滿蓄爆炸塗墨半徑(S3 5.0)")]
        public float PaintRadiusMaxCharge;

        [Tooltip("未蓄力滑行軌跡塗墨半徑(S3 PaintRadiusMinCharge 1.075)")]
        public float TrailRadiusMinCharge;

        [Tooltip("滿蓄滑行軌跡塗墨半徑(S3 PaintRadiusMaxCharge 1.29)")]
        public float TrailRadiusMaxCharge;

        [Header("接觸")]
        [Tooltip("滑過敵人的接觸傷害(S3 DamageDirectHit 200 = 20.0)")]
        public float ContactDamage;

        [Tooltip("同一目標接觸傷害間隔(F;S3 DamageDirectSpanSecond 0.6667 = 40F)")]
        public int ContactIntervalFrames;

        // S3 v11.3.0、0 AP(Sub Power Up 0)
        public static CurlingBombParams S3 => new CurlingBombParams
        {
            MaxChargeFrames = 60,
            ChargeWindupFrames = 5,
            BurstFrame = 210,
            ChargeFrameBlastRate = 2f,
            SpawnSpeedZ = 0.4f,
            SpawnSpeedZMaxCharge = 0.2f,
            SpawnSpeedY = 0.12f,
            SpawnSpeedYWorldMin = -0.5f,
            InheritLateral = 0.8f,
            InheritForward = 1.2f,
            InheritUpRate = 2f,
            InheritUpMax = 0.16f,
            FlyGravity = 0.016f,
            FlyAirResist = 0.05866f,
            BaseSpeedMinCharge = 0.22f,
            BaseSpeedMaxCharge = 0.1f,
            ComeOverRate = 0.92f,
            ComeUnderRate = 0.96f,
            SlowdownRestFrames = 90,
            StopBias = 0.41f,
            InkLockFrames = 70,
            InkLockFramesMaxCharge = 30,
            NearRadiusMinCharge = 1.6f,
            NearRadiusMaxCharge = 4.6f,
            FarRadiusMinCharge = 5f,
            FarRadiusMaxCharge = 8f,
            PaintRadiusMinCharge = 2.133f,
            PaintRadiusMaxCharge = 5f,
            TrailRadiusMinCharge = 1.075f,
            TrailRadiusMaxCharge = 1.29f,
            ContactDamage = 20f,
            ContactIntervalFrames = 40,
        };
    }

    // Curling Bomb 的純邏輯(逐幀 60 Hz;research 03 §2.7、DESIGN_REFERENCE §4.3):
    //   蓄力:按住 R 出手準備 5F 後開始蓄,上限 60F;引信 = 210 − 2 × 蓄力幀(放開起算,空中也照算)。
    //   出手:v = 視線 × lerp(0.40, 0.20) + 上 × 0.12,垂直下限 −0.5,再加玩家動量(橫 ×0.8、前 ×1.2、上升 ×2.0 ≤ 0.16)。
    //   空中:pos += v;v ×= (1 − 0.05866);v.y −= 0.016。
    //   滑行:巡航 = lerp(0.22, 0.10);比巡航快 → 超出量每幀 ×0.92,慢 → 不足量每幀 ×0.96(落地後慢慢加速到巡航);
    //         剩 90F 起巡航 ×(剩餘/90)^(1/0.41)[EST],最後停住。
    //   驗算(0 蓄、平丟、出手高 0.55):總距離 ≈ 33 u(JP 實測 6.9 線 = 34.5 u);滿蓄 ≈ 4.2 u(JP 1.0 線 = 5 u)。
    public static class CurlingBombModel
    {
        public static float Charge01(in CurlingBombParams p, float chargeFrames)
        {
            return p.MaxChargeFrames > 0 ? Mathf.Clamp01(chargeFrames / p.MaxChargeFrames) : 0f;
        }

        // 按住 heldFrames 幀後的蓄力幀(扣出手準備,上限 MaxChargeFrames)
        public static float ChargeFramesAfterHold(in CurlingBombParams p, float heldFrames)
        {
            return Mathf.Clamp(heldFrames - p.ChargeWindupFrames, 0f, p.MaxChargeFrames);
        }

        // 引信(F,放開起算):210 − 2 × 蓄力幀
        public static int FuseFrames(in CurlingBombParams p, float charge01)
        {
            float chargeFrames = Mathf.Clamp01(charge01) * p.MaxChargeFrames;
            return Mathf.Max(1, Mathf.RoundToInt(p.BurstFrame - p.ChargeFrameBlastRate * chargeFrames));
        }

        // 墨量鎖(F):70 → 30 依蓄力線性
        public static int InkLockFrames(in CurlingBombParams p, float charge01)
        {
            return Mathf.RoundToInt(Mathf.Lerp(p.InkLockFrames, p.InkLockFramesMaxCharge, Mathf.Clamp01(charge01)));
        }

        public static float BaseSpeed(in CurlingBombParams p, float charge01)
        {
            return Mathf.Lerp(p.BaseSpeedMinCharge, p.BaseSpeedMaxCharge, Mathf.Clamp01(charge01));
        }

        public static float NearRadius(in CurlingBombParams p, float charge01)
        {
            return Mathf.Lerp(p.NearRadiusMinCharge, p.NearRadiusMaxCharge, Mathf.Clamp01(charge01));
        }

        public static float FarRadius(in CurlingBombParams p, float charge01)
        {
            return Mathf.Lerp(p.FarRadiusMinCharge, p.FarRadiusMaxCharge, Mathf.Clamp01(charge01));
        }

        public static float PaintRadius(in CurlingBombParams p, float charge01)
        {
            return Mathf.Lerp(p.PaintRadiusMinCharge, p.PaintRadiusMaxCharge, Mathf.Clamp01(charge01));
        }

        public static float TrailRadius(in CurlingBombParams p, float charge01)
        {
            return Mathf.Lerp(p.TrailRadiusMinCharge, p.TrailRadiusMaxCharge, Mathf.Clamp01(charge01));
        }

        // 引信剩 restFrames 時的巡航倍率:剩 ≥ 90F 為 1,之後 (剩餘/90)^(1/StopBias) 降到 0 [EST]
        public static float SlowdownFactor(in CurlingBombParams p, int restFrames)
        {
            if (p.SlowdownRestFrames <= 0 || restFrames >= p.SlowdownRestFrames)
            {
                return 1f;
            }
            if (restFrames <= 0)
            {
                return 0f;
            }
            float t = (float)restFrames / p.SlowdownRestFrames;
            float exponent = p.StopBias > 0.01f ? 1f / p.StopBias : 1f;
            return Mathf.Pow(t, exponent);
        }

        // 這一幀的巡航速度(u/F)
        public static float TargetSpeed(in CurlingBombParams p, float charge01, int restFrames)
        {
            return BaseSpeed(p, charge01) * SlowdownFactor(p, restFrames);
        }

        // 滑行速度往巡航收斂一幀(S3 BaseSpeedComeOver/UnderRate)
        public static float StepGroundSpeed(in CurlingBombParams p, float speed, float target)
        {
            float rate = speed > target ? p.ComeOverRate : p.ComeUnderRate;
            return target + (speed - target) * rate;
        }

        // 空中一幀(u/F;S3 FlyGravity + FlyPositionAirResist)
        public static void StepAir(in CurlingBombParams p, ref Vector3 position, ref Vector3 velocity)
        {
            position += velocity;
            velocity *= 1f - p.FlyAirResist;
            velocity.y -= p.FlyGravity;
        }

        // 出手初速(u/F):aimDirection = 視線方向;ownerVelocity = 投擲者速度(u/F)
        public static Vector3 LaunchVelocity(in CurlingBombParams p, Vector3 aimDirection, float charge01,
            Vector3 ownerVelocity)
        {
            Vector3 aim = aimDirection.sqrMagnitude > 1e-8f ? aimDirection.normalized : Vector3.forward;
            float speed = Mathf.Lerp(p.SpawnSpeedZ, p.SpawnSpeedZMaxCharge, Mathf.Clamp01(charge01));
            Vector3 v = aim * speed + Vector3.up * p.SpawnSpeedY;
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

        // 無限平地上的總移動距離(u,不含牆):出手高 launchHeight、視線俯仰 pitchDeg(負 = 往下看)。
        // 測試與 bot 蓄力選擇用;執行期碰撞版在 CurlingBomb。
        public static float PredictFlatTravel(in CurlingBombParams p, float charge01, float pitchDeg,
            float launchHeight, out int airFrames)
        {
            float rad = pitchDeg * Mathf.Deg2Rad;
            Vector3 aim = new Vector3(0f, Mathf.Sin(rad), Mathf.Cos(rad));
            Vector3 v = LaunchVelocity(p, aim, charge01, Vector3.zero);
            Vector3 position = new Vector3(0f, launchHeight, 0f);
            int fuse = FuseFrames(p, charge01);
            bool air = true;
            float speed = 0f;
            float travelled = 0f;
            airFrames = 0;
            for (int frame = 1; frame <= fuse; frame++)
            {
                if (air)
                {
                    StepAir(p, ref position, ref v);
                    airFrames++;
                    if (position.y <= 0f)
                    {
                        air = false;
                        position.y = 0f;
                        speed = new Vector2(v.x, v.z).magnitude;
                    }
                    travelled = new Vector2(position.x, position.z).magnitude;
                    continue;
                }
                speed = StepGroundSpeed(p, speed, TargetSpeed(p, charge01, fuse - frame));
                travelled += speed;
            }
            return travelled;
        }

        // bot:要在 distance 處爆炸該蓄多少(0~1)。滑行距離隨蓄力單調遞減 → 二分法;太遠 0、太近 1。
        public static float ChargeForDistance(in CurlingBombParams p, float distance, float pitchDeg,
            float launchHeight)
        {
            float far = PredictFlatTravel(p, 0f, pitchDeg, launchHeight, out _);
            if (distance >= far)
            {
                return 0f;
            }
            float near = PredictFlatTravel(p, 1f, pitchDeg, launchHeight, out _);
            if (distance <= near)
            {
                return 1f;
            }
            float lo = 0f;
            float hi = 1f;
            for (int i = 0; i < 14; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (PredictFlatTravel(p, mid, pitchDeg, launchHeight, out _) > distance)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }
            return (lo + hi) * 0.5f;
        }
    }
}

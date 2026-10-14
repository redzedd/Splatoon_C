using UnityEngine;

namespace SplatoonC.Core.Combat
{
    // Crab Tank(S3 SpChariot,research/04 §2.12、JP wiki カニタンク;v11.3.0 0 AP)的純邏輯。
    //   時間:480F(SPU 660F);裝甲在發動後 45F 才出現(使用者畫面);結束/被破壞後 25F 不能攻擊(NoShotBombReqFrm)。
    //   裝甲耐久 500(ArmorHP 5000),受擊倍率走 DamageRates(TargetClass.CrabTank);破壞 → 駕駛彈出、不算被擊倒。
    //   步行:橫移 0.075 u/F、前後 0.015 u/F(MoveSpeedX/Z);車身轉向很慢 [EST 70°/s]。
    //   ZR 連射:間隔 12F → 4F(ShooterRepeatFrame_First 12、Term 90:連射 90F 後到最快 [EST 線性])、左右兩門輪流;
    //     彈 3.36 u/F × 7F → 減速(上限 2.232)、自由落體重力 0.016;傷害 32 → 16(飛行 9 → 25F);對人 0.2、地形 0.2。
    //   R 砲:每 75F 一發,發射後 32F 內 ZR 不能射(CannonNoShotFrame);直擊 50(貫穿角色)+ 爆炸 30(≤ 4.8,v11.2.0)、
    //     塗 4.5;直線 8F 後阻力 0.12 / 重力 0.05 自由落體(初速 [EST 2.0 u/F]:平射約 5.6 條試射線 = 28 u)。
    //   ZL 滾球:0.1 u/F(JP wiki 約 1.00 DU/F)全方向、可爬塗得到的牆(只能上下)、腳下一直塗、接觸 40、全身受保護、
    //     回血同潛墨;解除後 20F 不能射(PreDelayFrm_Squid_ShotBomb),車身轉向鏡頭方向。
    public static class CrabTankModel
    {
        public const int DurationFrames = 480;
        public const int ArmorDelayFrames = 45;
        public const int EndLagFrames = 25;
        public const float ArmorHp = 500f;

        public const float WalkSpeedLateral = 0.075f;
        public const float WalkSpeedForward = 0.015f;
        public const float TurnDegPerSecond = 70f;
        public const float BallSpeed = 0.1f;

        public const float TurretRepeatFirst = 12f;
        public const float TurretRepeatLast = 4f;
        public const int TurretRampFrames = 90;
        public const float TurretDamageMax = 32f;
        public const float TurretDamageMin = 16f;
        public const int TurretReduceStart = 9;
        public const int TurretReduceEnd = 25;
        public const float TurretSpawnSpeed = 3.36f;
        public const int TurretStraightFrames = 7;
        public const float TurretStraightEndMaxSpeed = 2.232f;
        public const float TurretSpreadDeg = 3f;

        public const int CannonIntervalFrames = 75;
        public const int CannonNoShotFrames = 32;
        public const float CannonDirect = 50f;
        public const float CannonBlast = 30f;
        public const float CannonBlastRadius = 4.8f;
        public const float CannonPaintRadius = 4.5f;
        public const float CannonSpawnSpeed = 2f;
        public const int CannonStraightFrames = 8;

        public const float BallContactDamage = 40f;
        public const int UnrollNoShotFrames = 20;
        public const int StartDelayFrames = 20;

        // 連射 heldFrames 幀時的發射間隔(幀):12 → 4 線性,90F 到底
        public static float TurretRepeatFrames(float heldFrames)
        {
            float t = Mathf.Clamp01(heldFrames / TurretRampFrames);
            return Mathf.Lerp(TurretRepeatFirst, TurretRepeatLast, t);
        }

        // 步行的車身座標速度(u/F):input.x = 橫移、input.y = 前後(搖桿 −1..1)
        public static Vector2 WalkVelocity(Vector2 input)
        {
            input = Vector2.ClampMagnitude(input, 1f);
            return new Vector2(input.x * WalkSpeedLateral, input.y * WalkSpeedForward);
        }

        // 連射彈的彈道(射擊槍類減速/自由落體常數,重力 0.016 同預設)
        public static BulletMotion TurretMotion => BulletMotion.ShooterFamily(TurretSpawnSpeed, TurretStraightFrames,
            TurretStraightEndMaxSpeed);

        // 砲彈:直線 8F → 直接自由落體(下限 20/10),阻力 0.12、重力 0.05
        public static BulletMotion CannonMotion => new BulletMotion
        {
            SpawnSpeed = CannonSpawnSpeed,
            StraightFrames = CannonStraightFrames,
            StraightEndMaxSpeed = 1000f,
            BrakeAirResist = 0.1f,
            BrakeGravity = 0.04f,
            BrakeToFreeVelocityXZ = 20f,
            BrakeToFreeVelocityY = 10f,
            FreeAirResist = 0.12f,
            FreeGravity = 0.05f,
        };
    }
}

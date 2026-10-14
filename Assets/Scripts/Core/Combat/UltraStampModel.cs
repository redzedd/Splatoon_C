using UnityEngine;

namespace SplatoonC.Core.Combat
{
    // Ultra Stamp(S3 SpUltraStamp,research/04 §2.11、JP wiki ウルトラハンコ;v11.3.0 0 AP)的純邏輯。
    //   時間:450F(SPU 660F);發動前隙 17F。
    //   地上 ZR:敲地,前隙 30F(從烏賊 35F),兩次敲之間 20F;直擊 100(射程約 1.2 條 = 6 u)+ 衝擊波 40(≤ 4.0,
    //     SwingBlastParam 400 → 40.0)、擊退 460/6(連敲 8);敲地塗一個印章。敲下的槌面擋正面的子彈與炸彈。
    //   按住 ZR:連續敲(每 13F 一下)自動前進 0.155 u/F(塗不到的地面 0.144);連敲中跳起 = 原地連敲。
    //   空中 ZR:迴旋揮擊 100(半徑約 0.9 條 = 4.5 u),前隙 8F;接連敲要 40F。
    //   R 丟出:起手 42F,槌以 0.85 u/F(+ 上 0.17)飛出、重力 0.013;直擊 220(貫穿角色)、
    //     落地/撞物爆炸 220(≤ 3.6,v11.1.0)/ 60(≤ 8.0)、塗 5.0;丟出即結束大招(能量歸 0)。
    //   丟出的空氣阻力:S3 FlyPositionAirResist 0.03 照字面算平射只有 17 m,與 JP wiki 實測「平射約 5.0 條 = 25 u」不符 →
    //     [EST] 取 0.006 擬合平射 25 u。
    public static class UltraStampModel
    {
        public const int DurationFrames = 450;
        public const int ActivationLagFrames = 17;

        public const int StampFrontLagFrames = 30;
        public const int StampFrontLagSquidFrames = 35;
        public const int StampIntervalFrames = 20;
        public const int RushIntervalFrames = 13;
        public const float RushSpeed = 0.155f;
        public const float RushSpeedNoPaint = 0.144f;
        public const int AirSwingLagFrames = 8;
        public const int AirToRushFrames = 40;

        public const float SwingDamage = 100f;
        public const float SwingBlastDamage = 40f;
        public const float SwingBlastRadius = 4f;
        public const float SwingReach = 6f;
        public const float AirSwingRadius = 4.5f;

        public const int ThrowWindupFrames = 42;
        public const float ThrowSpeed = 0.85f;
        public const float ThrowSpeedY = 0.17f;
        public const float ThrowGravity = 0.013f;
        public const float ThrowAirResist = 0.006f;
        public const float ThrowDirect = 220f;
        public const float ThrowNearDamage = 220f;
        public const float ThrowNearRadius = 3.6f;
        public const float ThrowFarDamage = 60f;
        public const float ThrowFarRadius = 8f;
        public const float ThrowPaintRadius = 5f;
        public const float ThrowGuideRadius = 1.1f;

        // 丟出的槌:沒有直線段,一出手就是阻力 + 重力(下限 20/10 = 跳過減速)
        public static BulletMotion ThrowMotion => new BulletMotion
        {
            SpawnSpeed = ThrowSpeed,
            StraightFrames = 0,
            StraightEndMaxSpeed = 1000f,
            BrakeAirResist = ThrowAirResist,
            BrakeGravity = ThrowGravity,
            BrakeToFreeVelocityXZ = 20f,
            BrakeToFreeVelocityY = 10f,
            FreeAirResist = ThrowAirResist,
            FreeGravity = ThrowGravity,
        };

        // 敲到地面時的槌角度(度):握把往前下 10°,槌面剛好貼地
        public const float ImpactAngle = 100f;

        // 揮擊時間軸(0 = 開始揮、1 = 敲到地面):前 frontLag 幀是舉起 → 砸下,回傳槌角度(度,0 = 舉在頭頂正上方、
        // 90 = 水平往前、100 = 砸在身前地面;負 = 往後)。前 65% 舉到頭後(−25°)蓄力、後 35% 加速砸下 [EST 目視]。
        public static float SwingAngle(float t)
        {
            t = Mathf.Clamp01(t);
            if (t < 0.65f)
            {
                float u = t / 0.65f;
                return Mathf.Lerp(10f, -25f, u * (2f - u));
            }
            float v = (t - 0.65f) / 0.35f;
            return Mathf.Lerp(-25f, ImpactAngle, v * v);
        }
    }
}

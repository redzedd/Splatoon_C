using UnityEngine;

namespace SplatoonC.Core.Combat
{
    // S3 射擊槍參數 → 本專案兩段式彈道(InkProjectile:直飛段近直線、超程急墜 + 水平煞停)的換算規則。
    // 校準點 = 既有 Splattershot 手感(Assets/Data/WeaponConfig,M3 試玩調出來的):初速 65.8125、直飛 10 m、
    // 直飛重力 −1.5、急墜重力 −60、墜落水平阻力 9.45、衰減 3 m、主 splat 1.82、每發 3 滴 r1.2、槍口噴濺 1.18。
    // 各變體以 S3 相對 Splattershot 的比例換算,因此彼此的相對關係(射程、射速、塗地)與 S3 一致。
    public static class ShooterCalibration
    {
        public const float ReferenceSpawnSpeed = 2.266f;
        public const float ReferenceMuzzleSpeed = 65.8125f;
        public const float ReferenceStraightGravity = -1.5f;
        public const float ReferenceDropGravity = -60f;
        public const float ReferenceDropHorizontalDrag = 9.45f;

        // WeaponConfig.EffectiveRange = 直飛射程 + 1.5(直飛結束後水平滑行、幾乎不掉的那一段)
        public const float GlideAllowance = 1.5f;

        // Splattershot 衰減 32F(8→40)對應急墜段 3 m
        public const float ReferenceDecayDistance = 3f;
        public const int ReferenceFalloffFrames = 32;

        // 塗墨:主 splat 半徑 = S3 落點半寬 near/far 平均(Splattershot 1.93/1.71 → 1.82,正是既有值)
        public const float ReferenceSplatRadius = 1.82f;
        public const float ReferenceSplashRadius = 0.84f;
        public const float ReferenceSpawnNum = 1.5f;
        public const int ReferenceDripsPerShot = 3;
        public const float ReferenceDropletWidthHalf = 1.472f;
        public const float ReferenceDripRadius = 1.2f;
        public const float ReferenceFeetDropletWidthHalf = 2.0608f;
        public const float ReferenceMuzzleSplashRadius = 1.18f;

        // 滴墨窗口終點 = 直飛射程 + 5 m(Splattershot 10 × 1.5 = 15;平射從槍口高度落地約在直飛 + 5.8 m)
        public const float DripWindowBeyondStraight = 5f;

        // 速度倍率 s:初速照 S3 SpawnSpeed 比例放大
        public static float SpeedScale(in S3ShooterParams p)
        {
            return p.SpawnSpeed / ReferenceSpawnSpeed;
        }

        public static float MuzzleSpeed(in S3ShooterParams p)
        {
            return ReferenceMuzzleSpeed * SpeedScale(p);
        }

        // 直飛射程:讓 EffectiveRange(直飛 + 1.5)正好等於 S3 有效射程
        public static float StraightRange(in S3ShooterParams p)
        {
            return p.EffectiveRange - GlideAllowance;
        }

        // 幾何相似:初速 ×s、阻力 ×s、重力 ×s² → 彈道「形狀」與 Splattershot 完全相同,只是走得快 s 倍。
        // (時間換成 τ = s·t 後運動方程式不變)所以直飛後的滑行、落地距離只由直飛射程決定。
        public static float StraightGravity(in S3ShooterParams p)
        {
            float s = SpeedScale(p);
            return ReferenceStraightGravity * s * s;
        }

        public static float DropGravity(in S3ShooterParams p)
        {
            float s = SpeedScale(p);
            return ReferenceDropGravity * s * s;
        }

        public static float DropHorizontalDrag(in S3ShooterParams p)
        {
            return ReferenceDropHorizontalDrag * SpeedScale(p);
        }

        // 全傷距離(彈心飛行距離):S3 有效射程扣掉對人判定半徑 = ReduceStartFrame(= ShotGuideFrame)時彈心的位置
        public static float DamageFalloffStart(in S3ShooterParams p)
        {
            return p.EffectiveRange - p.EndRadiusForPlayer;
        }

        // [EST] 衰減段長度與 S3 衰減幀數成正比(Splattershot 32F ↔ 3 m)
        public static float DamageDecayDistance(in S3ShooterParams p)
        {
            return ReferenceDecayDistance * p.FalloffFrames / ReferenceFalloffFrames;
        }

        public static float SplatRadius(in S3ShooterParams p)
        {
            return 0.5f * (p.WidthHalfNear + p.WidthHalfFar);
        }

        public static float SplashRadius(in S3ShooterParams p)
        {
            return ReferenceSplashRadius * SplatRadius(p) / ReferenceSplatRadius;
        }

        // 每發滴數 ∝ S3 SpawnNum(Splattershot 1.5 ↔ 3 滴)
        public static int DripsPerShot(in S3ShooterParams p)
        {
            return Mathf.Max(1, Mathf.RoundToInt(ReferenceDripsPerShot * p.SpawnNum / ReferenceSpawnNum));
        }

        public static float DripRadius(in S3ShooterParams p)
        {
            return ReferenceDripRadius * p.DropletWidthHalf / ReferenceDropletWidthHalf;
        }

        public static float DripRangeMultiplier(in S3ShooterParams p)
        {
            float straight = Mathf.Max(0.1f, StraightRange(p));
            return (straight + DripWindowBeyondStraight) / straight;
        }

        // 槍口噴濺 = S3 腳前強制墨滴;沒有這個樣式的武器(Pro)不噴,腳下塗地差是它的 S3 特性
        public static float MuzzleSplashRadius(in S3ShooterParams p)
        {
            return p.FeetDropPatterns > 0
                ? ReferenceMuzzleSplashRadius * p.DropletWidthHalfNearest / ReferenceFeetDropletWidthHalf
                : 0f;
        }
    }
}

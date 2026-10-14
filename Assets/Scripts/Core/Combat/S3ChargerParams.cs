using System;
using UnityEngine;

namespace SplatoonC.Core.Combat
{
    // 狙擊槍(Charger)的 S3 參數與純邏輯(research 02 §7、DESIGN_REFERENCE §3.3 / §3.5;Leanny v11.3.0
    // ChargerNormal / ChargerLong 原始 JSON)。單位沿用 datamine:速度 u/F、距離 u(= 公尺)、幀 F(60 Hz)、
    // 墨量 = 標準墨槽的分數、傷害 HP。
    //
    // 蓄力語意(research 02 §7.2 推導表;與 Inkipedia Charger 頁一致):
    //   蓄力幀 F(可因空中 / 墨不足而以 1/3 速率累積,見 ChargeRate):
    //   F ≥ FullChargeFrames(Splat 60 / E-liter 92)= 滿蓄 → 跳到滿蓄值(傷害 160 / 180、貫穿、射程與速度用 Full)。
    //   其餘 = 部分蓄力,t = clamp01((F − 8) / (Full − 8)),各值從 Min 線性到 Max(傷害 40 → 80,永遠打不死人);
    //   至少要蓄 MinChargeFrames(8F)才出得了手(提早放開 = 蓄到 8F 那一刻射出最小蓄力彈)。
    //   耗墨從 InkConsumeMin 線性到 InkConsumeFull(部分蓄力的上限就是滿蓄耗墨;59F = 17.7%)。
    [Serializable]
    public struct S3ChargerParams
    {
        [Tooltip("S3 英文名稱")]
        public string Name;

        [Header("蓄力(WeaponChargerParam)")]
        [Tooltip("滿蓄幀數(ChargeFrameFullCharge;Splat 60 / E-liter 92)")]
        public int FullChargeFrames;

        [Tooltip("最短蓄力幀數(放開前至少要蓄這麼久才出手;Inkipedia Charger 8F)")]
        public int MinChargeFrames;

        [Tooltip("空中蓄力速率倍率(Inkipedia:空中蓄力慢 3 倍 = 1/3)")]
        public float AirChargeRate;

        [Tooltip("墨不夠目前蓄力等級時的蓄力速率倍率(Inkipedia:墨空時慢 3 倍 = 1/3)")]
        public float LowInkChargeRate;

        [Tooltip("最小 / 滿蓄耗墨(標準墨槽的分數;InkConsumeMinCharge 0.0225 / InkConsumeFullCharge 0.18、E-liter 0.25)")]
        public float InkConsumeMin;

        public float InkConsumeFull;

        [Tooltip("開槍後的墨量鎖(幀;S3 開完槍 20F 開始回墨);蓄力中與儲存蓄力中持續上鎖")]
        public int InkRecoverStop;

        [Tooltip("開槍後不能潛墨的幀數(S3 開完槍 15F 才能變烏賊)")]
        public int PostShotNoSquidFrames;

        [Tooltip("滿蓄時的走速(u/F;MoveSpeedFullCharge,Splat 0.02 / E-liter 0.015)")]
        public float MoveSpeedFullCharge;

        [Tooltip("剛開始蓄力的走速(u/F)[EST]:S1 datamine「0.20~0.72 DU/F」= 未蓄 0.072 → 滿蓄 0.02,之間依蓄力比例線性")]
        public float MoveSpeedNoCharge;

        [Tooltip("滿蓄時的起跳初速(u/F;JumpHeightFullCharge 0.07;一般人形 0.11)")]
        public float JumpVelocityFullCharge;

        [Tooltip("一般人形起跳初速(u/F;research 07 §3.1 = 0.11)")]
        public float JumpVelocityNormal;

        [Header("傷害(BulletChargerDamageParam;HP)")]
        [Tooltip("最小蓄力 / 部分蓄力上限 / 滿蓄(ValueMinCharge 40 / ValueMaxCharge 80 / ValueFullCharge 160、E-liter 180)")]
        public float DamageMin;

        public float DamageMax;

        public float DamageFull;

        [Header("彈道(BulletChargerMoveParam)")]
        [Tooltip("射程(u,從槍口量;DistanceMinCharge / MaxCharge / FullCharge;Splat 9.033 / 24.037 / 24.037)")]
        public float DistanceMin;

        public float DistanceMax;

        public float DistanceFull;

        [Tooltip("S3 槍口在角色前方的距離(u):Inkipedia「含槍口射程」10.75 / 25.75 − 原始 9.033 / 24.037 = 1.713")]
        public float MuzzleReach;

        [Tooltip("彈速(u/F;SpawnSpeedMinCharge / MaxCharge / FullCharge;Splat 2.4 / 4.8 / 4.8、E-liter 2.4 / 6.4 / 9.6)")]
        public float SpeedMin;

        public float SpeedMax;

        public float SpeedFull;

        [Header("判定(BulletSimpleCollisionParam;u)")]
        [Tooltip("對人判定半徑(InitRadiusForPlayer;Splat 0.125 / E-liter 0.1125)")]
        public float PlayerRadius;

        [Tooltip("對地形判定半徑(InitRadiusForField 0.02)")]
        public float FieldRadius;

        [Header("塗墨(BulletChargerPaintParam / SplashPaintParam / WallDropCollisionPaintParam;u)")]
        [Tooltip("彈道終點的塗墨半徑(RadiusMinCharge / MaxCharge / FullCharge;Splat 0.906 / 2.719 / 3.263)")]
        public float EndRadiusMin;

        public float EndRadiusMax;

        public float EndRadiusFull;

        [Tooltip("沿線墨滴半寬(SplashPaintParam.WidthHalf Min / Max / Full;Splat 0.78 / 1.56 / 1.56)")]
        public float TrailWidthHalfMin;

        public float TrailWidthHalfMax;

        public float TrailWidthHalfFull;

        [Tooltip("沿線墨滴沿彈道的半長(DepthHalf Min / Max / Full;Splat 2.73 / 1.56 / 1.56)")]
        public float TrailDepthHalfMin;

        public float TrailDepthHalfMax;

        public float TrailDepthHalfFull;

        [Tooltip("槍口前這段距離內不落墨滴(RadiusSpawnNearest 1.2;SkipNum 1 = 第一滴略過)[EST 語意]")]
        public float TrailNearestSkip;

        [Tooltip("最後一滴的大小倍率(LastSplashRate;E-liter 1.5、Splat 省略 = 1)")]
        public float LastSplashRate;

        [Tooltip("打到牆的圓半徑(WallDropCollisionPaintParam.PaintRadiusShock Min / Max / Full;1.2 / 1.8 / 1.8)")]
        public float WallRadiusMin;

        public float WallRadiusMax;

        public float WallRadiusFull;

        [Header("儲存蓄力(WeaponKeepChargeParam)")]
        [Tooltip("按住攻擊潛墨時滿蓄可保留的幀數(KeepChargeFullFrame 75)")]
        public int KeepChargeFrames;

        [Tooltip("帶著儲存的蓄力浮出後,雷射重新出現的幀數(Inkipedia 25F)")]
        public int KeepLaserDelayFrames;

        [Tooltip("帶著儲存的蓄力浮出後,可以開槍的幀數(Inkipedia 31F;≈ 變身 8F + KeepChargePreDelayFrame 23)")]
        public int KeepFireDelayFrames;

        // ── 蓄力 ──

        // 幀數比較的容差:蓄力以 Time.deltaTime × 60 累積,60 fps 下 dt 常是 0.016666 而非 1/60,
        // 60 幀累積成 59.998——不給容差會晚一幀才算滿(0.02F = 0.33 ms)
        public const float FrameEpsilon = 0.02f;

        public float FullChargeSeconds => FullChargeFrames / 60f;
        public float MinChargeSeconds => MinChargeFrames / 60f;

        // 蓄力比例 0 → 1(HUD、走速、雷射亮度)
        public float Charge01(float frames)
        {
            return FullChargeFrames > 0 ? Mathf.Clamp01(frames / FullChargeFrames) : 1f;
        }

        public bool IsFull(float frames)
        {
            return frames >= FullChargeFrames - FrameEpsilon;
        }

        public bool CanRelease(float frames)
        {
            return frames >= MinChargeFrames - FrameEpsilon;
        }

        // 部分蓄力的插值位置:8F = 0、滿蓄前一刻 → 1
        public float PartialT(float frames)
        {
            int span = Mathf.Max(1, FullChargeFrames - MinChargeFrames);
            return Mathf.Clamp01((frames - MinChargeFrames) / span);
        }

        // Min → Max(部分蓄力)/ Full(滿蓄)
        public float Tiered(float min, float max, float full, float frames)
        {
            return IsFull(frames) ? full : Mathf.Lerp(min, max, PartialT(frames));
        }

        // 這一幀的蓄力速率倍率:空中或墨不夠 = 1/3(兩者不疊加)
        public float ChargeRate(bool airborne, bool lowInk)
        {
            float rate = 1f;
            if (airborne)
            {
                rate = Mathf.Min(rate, AirChargeRate > 0f ? AirChargeRate : 1f / 3f);
            }
            if (lowInk)
            {
                rate = Mathf.Min(rate, LowInkChargeRate > 0f ? LowInkChargeRate : 1f / 3f);
            }
            return rate;
        }

        // ── 一發的數值 ──

        // 傷害(取整到 0.1):40 → 80 部分蓄力、滿蓄 160 / 180
        public float Damage(float frames)
        {
            return VitalsModel.Floor01(Tiered(DamageMin, DamageMax, DamageFull, frames) + 1e-4f);
        }

        // 從槍口量的射程(u)
        public float Distance(float frames)
        {
            return Tiered(DistanceMin, DistanceMax, DistanceFull, frames);
        }

        // 從角色量的射程(u;含 S3 槍口 1.71 u)= Inkipedia 表列射程 10.75 → 25.75 / 30.76
        public float ReachFromPlayer(float frames)
        {
            return Distance(frames) + MuzzleReach;
        }

        public float Speed(float frames)
        {
            return Tiered(SpeedMin, SpeedMax, SpeedFull, frames);
        }

        // 從槍口飛到射程盡頭的幀數(直線等速)
        public int FlightFrames(float frames)
        {
            float speed = Mathf.Max(1e-4f, Speed(frames));
            return Mathf.CeilToInt(Distance(frames) / speed - 1e-4f);
        }

        // 耗墨:最小 → 滿蓄線性(部分蓄力的上限就是滿蓄耗墨)
        public float InkConsume(float frames)
        {
            return IsFull(frames) ? InkConsumeFull : Mathf.Lerp(InkConsumeMin, InkConsumeFull, PartialT(frames));
        }

        // 墨槽剩 tank(標準墨槽分數)時,付得起的最高蓄力幀數;連最小蓄力都付不起 = −1
        public float AffordableFrames(float tank)
        {
            if (tank >= InkConsumeFull - 1e-6f)
            {
                return FullChargeFrames;
            }
            if (tank < InkConsumeMin - 1e-6f)
            {
                return -1f;
            }
            float t = (tank - InkConsumeMin) / Mathf.Max(1e-6f, InkConsumeFull - InkConsumeMin);
            return MinChargeFrames + t * (FullChargeFrames - MinChargeFrames);
        }

        public float EndPaintRadius(float frames)
        {
            return Tiered(EndRadiusMin, EndRadiusMax, EndRadiusFull, frames);
        }

        public float TrailWidthHalf(float frames)
        {
            return Tiered(TrailWidthHalfMin, TrailWidthHalfMax, TrailWidthHalfFull, frames);
        }

        public float TrailDepthHalf(float frames)
        {
            return Tiered(TrailDepthHalfMin, TrailDepthHalfMax, TrailDepthHalfFull, frames);
        }

        public float WallPaintRadius(float frames)
        {
            return Tiered(WallRadiusMin, WallRadiusMax, WallRadiusFull, frames);
        }

        // ── 蓄力中的移動 ──

        // 蓄力中的走速(u/F):未蓄 MoveSpeedNoCharge → 滿蓄 MoveSpeedFullCharge,依蓄力比例線性 [EST]
        public float MoveSpeed(float charge01)
        {
            return Mathf.Lerp(MoveSpeedNoCharge, MoveSpeedFullCharge, Mathf.Clamp01(charge01));
        }

        // 起跳高度倍率(PlayerLocomotion 的 jumpHeightMultiplier = 初速比的平方):
        // 滿蓄 (0.07 / 0.11)² = 0.405;蓄力中依比例線性內插初速 [EST]
        public float JumpHeightFactor(float charge01)
        {
            if (JumpVelocityNormal <= 0f || JumpVelocityFullCharge <= 0f)
            {
                return 1f;
            }
            float v = Mathf.Lerp(JumpVelocityNormal, JumpVelocityFullCharge, Mathf.Clamp01(charge01));
            float ratio = v / JumpVelocityNormal;
            return ratio * ratio;
        }

        // 一槽滿墨能打幾發滿蓄(Splat 5、E-liter 4)
        public int FullShotsPerTank => InkConsumeFull > 0f ? Mathf.FloorToInt(1f / InkConsumeFull + 1e-4f) : 0;

        // ── S3 預設(Leanny v11.3.0;省略值取 Inkipedia 模板預設)──

        private static S3ChargerParams Common(string name)
        {
            return new S3ChargerParams
            {
                Name = name,
                MinChargeFrames = 8,
                AirChargeRate = 1f / 3f,
                LowInkChargeRate = 1f / 3f,
                InkConsumeMin = 0.0225f,
                InkRecoverStop = 20,
                PostShotNoSquidFrames = 15,
                MoveSpeedNoCharge = 0.072f,
                JumpVelocityFullCharge = 0.07f,
                JumpVelocityNormal = 0.11f,
                DamageMin = 40f,
                DamageMax = 80f,
                FieldRadius = 0.02f,
                TrailNearestSkip = 1.2f,
                LastSplashRate = 1f,
                WallRadiusMin = 1.2f,
                WallRadiusMax = 1.8f,
                WallRadiusFull = 1.8f,
                KeepChargeFrames = 75,
                KeepLaserDelayFrames = 25,
                KeepFireDelayFrames = 31,
            };
        }

        // Splat Charger(ChargerNormal):60F 滿蓄、40 → 80 / 160、9.033 → 24.037 u(含槍口 25.75)、
        // 2.4 → 4.8 u/F、2.25% → 18%、走速 0.02、對人 0.125;終點 0.906 / 2.719 / 3.263、沿線半寬 0.78 → 1.56
        public static S3ChargerParams SplatCharger
        {
            get
            {
                S3ChargerParams p = Common("Splat Charger");
                p.FullChargeFrames = 60;
                p.InkConsumeFull = 0.18f;
                p.MoveSpeedFullCharge = 0.02f;
                p.DamageFull = 160f;
                p.DistanceMin = 9.033f;
                p.DistanceMax = 24.037f;
                p.DistanceFull = 24.037f;
                p.MuzzleReach = 1.713f;
                p.SpeedMin = 2.4f;
                p.SpeedMax = 4.8f;
                p.SpeedFull = 4.8f;
                p.PlayerRadius = 0.125f;
                p.EndRadiusMin = 0.906f;
                p.EndRadiusMax = 2.719f;
                p.EndRadiusFull = 3.263f;
                p.TrailWidthHalfMin = 0.78f;
                p.TrailWidthHalfMax = 1.56f;
                p.TrailWidthHalfFull = 1.56f;
                p.TrailDepthHalfMin = 2.73f;
                p.TrailDepthHalfMax = 1.56f;
                p.TrailDepthHalfFull = 1.56f;
                return p;
            }
        }

        // E-liter 4K(ChargerLong,重量級):92F 滿蓄、40 → 80 / 180、9.045 → 29.05 u(含槍口 30.76)、
        // 2.4 → 6.4 u/F、滿蓄 9.6 u/F、2.25% → 25%、走速 0.015、對人 0.1125;終點 1.068 / 3.204 / 3.845、
        // 沿線半寬 0.925 → 1.85、最後一滴 × 1.5
        public static S3ChargerParams ELiter4K
        {
            get
            {
                S3ChargerParams p = Common("E-liter 4K");
                p.FullChargeFrames = 92;
                p.InkConsumeFull = 0.25f;
                p.MoveSpeedFullCharge = 0.015f;
                p.DamageFull = 180f;
                p.DistanceMin = 9.045f;
                p.DistanceMax = 29.05f;
                p.DistanceFull = 29.05f;
                p.MuzzleReach = 1.71f;
                p.SpeedMin = 2.4f;
                p.SpeedMax = 6.4f;
                p.SpeedFull = 9.6f;
                p.PlayerRadius = 0.1125f;
                p.EndRadiusMin = 1.068f;
                p.EndRadiusMax = 3.204f;
                p.EndRadiusFull = 3.845f;
                p.TrailWidthHalfMin = 0.925f;
                p.TrailWidthHalfMax = 1.85f;
                p.TrailWidthHalfFull = 1.85f;
                p.TrailDepthHalfMin = 3.2375f;
                p.TrailDepthHalfMax = 1.85f;
                p.TrailDepthHalfFull = 1.85f;
                p.LastSplashRate = 1.5f;
                return p;
            }
        }
    }
}

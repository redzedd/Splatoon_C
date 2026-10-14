using System;
using SplatoonC.Core.Combat;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 一種墨彈的完整規格(可序列化,武器設定資產內嵌一份;InkBullet.Fire 讀它)。
    // 單位沿用 S3 datamine:速度 u/F、重力 u/F²、幀數 F(60 Hz);距離/半徑是公尺(1 u = 1 m);傷害是 HP。
    // 布林欄位一律「預設 false = 常見行為」;0 值欄位多半代表「用預設」(見各 Tooltip)。
    //
    // 用法(武器模組):
    //   private InkBulletSpec _bullet = InkBulletSpec.ShooterFamily(2.266f, 4, 1.493f, 36f, 18f, 8, 40, 0.285f);
    //   int attack = CombatLedger.NewAttackId();                 // 一次揮動/潑灑/齊射共用(去重);射擊槍每發可傳 0
    //   InkBullet.Fire(_bullet, muzzle, aimDirection, Owner.Team, Owner, attack, playerVelocity * inheritRate);
    [Serializable]
    public struct InkBulletSpec
    {
        [Header("彈道(S3 MoveParam;u/F)")]
        public BulletMotion Motion;

        [Tooltip("最大飛行距離(公尺,沿彈道;0 = 不限)。狙擊:直線飛到 Distance 就結束")]
        public float MaxDistance;

        [Tooltip("安全壽命(幀;0 = 600F)")]
        public int LifetimeFrames;

        [Header("判定(S3 CollisionParam;公尺)")]
        [Tooltip("對人判定半徑(初始;InitRadiusForPlayer,Splattershot 0.285)")]
        public float RadiusForPlayer;

        [Tooltip("對人判定半徑終值(EndRadiusForPlayer;≤ 0 = 同初始)")]
        public float RadiusForPlayerEnd;

        [Tooltip("對人判定半徑在前幾幀內從初始長到終值(ChangeFrameForPlayer;0 = 不變)。滾筒甩墨 4F 內 0.12 → 1.02")]
        public int RadiusForPlayerChangeFrames;

        [Tooltip("對地形判定半徑(InitRadiusForField;射擊槍 0.2)")]
        public float RadiusForField;

        [Tooltip("對地形判定半徑終值(≤ 0 = 同初始)")]
        public float RadiusForFieldEnd;

        [Tooltip("對地形判定半徑成長幀數(0 = 不變)")]
        public int RadiusForFieldChangeFrames;

        [Tooltip("貫穿角色(Trizooka 直擊、狙擊滿蓄);物件(Splash Wall、裝置)仍會擋下")]
        public bool PiercePlayers;

        [Header("傷害(以飛行幀數衰減,取整到 0.1)")]
        [Tooltip("最大傷害(≤ ReduceStartFrame)")]
        public float DamageMax;

        [Tooltip("最小傷害(≥ ReduceEndFrame)")]
        public float DamageMin;

        [Tooltip("開始衰減的幀(ReduceStartFrame;< 0 = 不衰減,例:爆破槍直擊)")]
        public int ReduceStartFrame;

        [Tooltip("衰減到最小的幀(ReduceEndFrame)")]
        public int ReduceEndFrame;

        [Tooltip("攻擊類別(物件受擊倍率 DamageRates)")]
        public AttackClass AttackClass;

        [Tooltip("傷害種類(主/副/大招)")]
        public DamageKind Kind;

        [Tooltip("同一 attackId 對同一目標的傷害上限(> 0 = 各發累加到上限,傘 81;0 = 同一次攻擊只吃第一發)")]
        public float PerAttackDamageCap;

        [Header("塗墨(S3 PaintParam;公尺)")]
        [Tooltip("地面落點半寬:近(離射手 ≤ 1.1 m;WidthHalfNear,Splattershot 1.93)")]
        public float PaintWidthHalfNear;

        [Tooltip("地面落點半寬:遠(≥ 20 m;WidthHalfFar,Splattershot 1.71)")]
        public float PaintWidthHalfFar;

        [Tooltip("半寬用「遠」值的距離(公尺;PaintParam.DistanceFar;0 = 20 m。滾筒橫甩 Splat 12 / Carbon 8.5)")]
        public float PaintDistanceFar;

        [Tooltip("落點半寬隨飛行幀數縮小的終值倍率(ChangeFrameWidthRate;0 = 不縮。滾筒 0.6)")]
        public float PaintWidthShrinkRate;

        [Tooltip("半寬開始縮小 / 縮到終值的飛行幀(ChangeWidthStartFrame / EndFrame;Splat Roller 橫甩 20 → 50)")]
        public int PaintWidthShrinkStartFrame;

        public int PaintWidthShrinkEndFrame;

        [Tooltip("沿飛行方向拉長倍率:入射角 < 10° 用此值(DepthScaleMax;預設 2.4)")]
        public float PaintDepthScaleMax;

        [Tooltip("入射角 > 35° 用此值(DepthScaleMin;預設 1.4)")]
        public float PaintDepthScaleMin;

        [Tooltip("落差 > 10 m 時用此值(DepthScaleMinBreakFree;0 = 同 Min)")]
        public float PaintDepthScaleBreakFree;

        [Tooltip("打在牆上的圓半徑(PaintRadiusShock;射擊槍 1.56)")]
        public float PaintRadiusShock;

        [Tooltip("塗墨硬度(0 = 預設 0.7)")]
        public float PaintHardness;

        [Tooltip("飛行途中每隔幾公尺滴一滴(SpawnBetweenLength;0 = 不滴)")]
        public float DripSpacing;

        [Tooltip("第一滴的飛行距離(公尺;0 = 等於 DripSpacing)")]
        public float DripStartDistance;

        [Tooltip("每滴塗墨半徑(公尺)")]
        public float DripRadius;

        [Tooltip("每發最多幾滴(0 = 不限)")]
        public int DripMaxCount;

        [Tooltip("飛到 MaxDistance 時往下塗的半徑(狙擊端點塗墨;0 = 不塗)")]
        public float EndPaintRadius;

        [Tooltip("完全不塗墨")]
        public bool NoPaint;

        [Tooltip("大招自己的墨不充大招(勾選 = 不充能)")]
        public bool NoSpecialCharge;

        [Header("爆炸(爆破槍、Trizooka、Luna、Crab Tank 砲彈…)")]
        [Tooltip("碰到地形或目標就爆")]
        public bool ExplodeOnImpact;

        [Tooltip("飛到第 N 幀在空中爆(爆破槍 BurstFrame;0 = 不空爆)")]
        public int ExplodeAtFrame;

        [Tooltip("飛到 MaxDistance 或壽命盡頭時爆")]
        public bool ExplodeAtEnd;

        [Tooltip("打到地形時的爆炸半徑倍率(ShotCollisionHitRadiusRate;爆破槍 0.4234;0 = 1)")]
        public float TerrainHitExplosionScale;

        [Tooltip("直擊目標也吃同一發的爆炸(預設不吃:S3 爆破槍直擊 125 就是全部)")]
        public bool ExplosionHitsDirectTarget;

        public ExplosionSpec Explosion;

        [Header("螺旋(Trizooka;0 = 直線)")]
        [Tooltip("繞彈道軸螺旋的半徑終值(公尺;Trizooka OrbitalRadiusEnd 1.0;0 = 不螺旋)")]
        public float OrbitRadius;

        [Tooltip("螺旋半徑從 0 長到終值的幀數(Trizooka OrbitalRadiusTransitionFrame 10)")]
        public int OrbitGrowFrames;

        [Tooltip("每幀繞軸轉的角度(度;Trizooka 15 [推導])。各發的起始角由 InkBullet.OrbitPhaseDeg 指定")]
        public float OrbitDegPerFrame;

        [Header("外觀與音效")]
        [Tooltip("墨彈外觀縮放(0 = 1)")]
        public float VisualScale;

        [Tooltip("拖尾秒數(0 = prefab 預設;< 0 = 無拖尾)")]
        public float TrailTime;

        [Tooltip("落地音效 id(空 = ink_hit_floor)")]
        public string HitFloorSound;

        [Tooltip("打牆音效 id(空 = ink_hit_wall)")]
        public string HitWallSound;

        [Tooltip("命中音量(0 = 0.55)")]
        public float HitSoundVolume;

        [Tooltip("不播命中音效(大量小彈丸,例:傘的 11 顆只讓一顆出聲)")]
        public bool Silent;

        public int EffectiveLifetimeFrames => LifetimeFrames > 0 ? LifetimeFrames : 600;
        public float EffectiveHardness => PaintHardness > 0f ? PaintHardness : 0.7f;
        public float EffectiveVisualScale => VisualScale > 0f ? VisualScale : 1f;
        public float EffectiveHitVolume => HitSoundVolume > 0f ? HitSoundVolume : 0.55f;
        public string EffectiveHitFloorSound => string.IsNullOrEmpty(HitFloorSound) ? "ink_hit_floor" : HitFloorSound;
        public string EffectiveHitWallSound => string.IsNullOrEmpty(HitWallSound) ? "ink_hit_wall" : HitWallSound;

        // 第 frame 幀的判定半徑(ChangeFrame 內線性成長)
        public float PlayerRadiusAt(int frame)
        {
            return RadiusAt(RadiusForPlayer, RadiusForPlayerEnd, RadiusForPlayerChangeFrames, frame);
        }

        public float FieldRadiusAt(int frame)
        {
            return RadiusAt(RadiusForField, RadiusForFieldEnd, RadiusForFieldChangeFrames, frame);
        }

        private static float RadiusAt(float init, float end, int changeFrames, int frame)
        {
            if (end <= 0f || changeFrames <= 0)
            {
                return init;
            }
            return Mathf.Lerp(init, end, Mathf.Clamp01(frame / (float)changeFrames));
        }

        // 飛行 frame 幀、離射手水平 flatDistance 公尺落地時的地面半寬:近 → 遠(1.1 m → PaintDistanceFar)線性,
        // 再乘隨飛行幀數的縮小倍率(滾筒 ChangeFrameWidthRate;見 SplatoonC.Core.Combat.InkStampWidth)
        public float FloorHalfWidthAt(float flatDistance, int frame)
        {
            return InkStampWidth.HalfWidth(PaintWidthHalfNear, PaintWidthHalfFar, PaintDistanceFar, flatDistance, frame,
                PaintWidthShrinkRate, PaintWidthShrinkStartFrame, PaintWidthShrinkEndFrame);
        }

        // 第 frame 幀命中的傷害(S3:飛行幀數線性衰減、取整到 0.1)
        public float DamageAtFrame(int frame)
        {
            return ProjectileDamageCurve.EvaluateByFrame(frame, DamageMax, DamageMin, ReduceStartFrame, ReduceEndFrame);
        }

        // ── 各類預設(§3.1 表;數值再由各武器模組覆寫)──
        // 彈道常數是 S3 datamine;射擊槍以外各類的塗墨半寬/DepthScale 是 [EST] 起始值,各武器模組依 research 覆寫。

        // 射擊槍、加特林、雙槍、傘彈丸、爆破槍的共通彈:減速 0.36/0.07、下限 0.2355/−0.15、自由 0.02/0.016;
        // 地形半徑 0.2;塗墨 DepthScale 2.4/1.4、牆 1.56(S3 預設值)
        public static InkBulletSpec ShooterFamily(float spawnSpeed, int straightFrames, float straightEndMaxSpeed,
            float damageMax, float damageMin, int reduceStartFrame, int reduceEndFrame, float radiusForPlayer,
            AttackClass attackClass = AttackClass.Shooter)
        {
            return new InkBulletSpec
            {
                Motion = BulletMotion.ShooterFamily(spawnSpeed, straightFrames, straightEndMaxSpeed),
                RadiusForPlayer = radiusForPlayer,
                RadiusForField = 0.2f,
                DamageMax = damageMax,
                DamageMin = damageMin,
                ReduceStartFrame = reduceStartFrame,
                ReduceEndFrame = reduceEndFrame,
                AttackClass = attackClass,
                Kind = DamageKind.Main,
                PaintWidthHalfNear = 1.93f,
                PaintWidthHalfFar = 1.71f,
                PaintDepthScaleMax = 2.4f,
                PaintDepthScaleMin = 1.4f,
                PaintRadiusShock = 1.56f,
                PaintHardness = 0.7f,
                VisualScale = 1f,
            };
        }

        // Splattershot(S3 v11.3.0):36/18 衰減 8→40F;2.266 u/F × 4F 直線、進減速 1.493;對人 0.285 / 地形 0.2;
        // 塗 1.93/1.71 半寬、DepthScale 2.24/1.31(落差 > 10 m 1.12)、牆 1.56;墨滴每 9.2 m
        //(墨滴半徑 1.0、每發最多 2 滴是 [EST]:S3 只公開腳前滴 2.06 與 SpawnNum 1.5)
        public static InkBulletSpec Splattershot
        {
            get
            {
                InkBulletSpec spec = ShooterFamily(2.266f, 4, 1.493f, 36f, 18f, 8, 40, 0.285f);
                spec.PaintDepthScaleMax = 2.24f;
                spec.PaintDepthScaleMin = 1.31f;
                spec.PaintDepthScaleBreakFree = 1.12f;
                spec.DripSpacing = 9.2f;
                spec.DripRadius = 1.0f;
                spec.DripMaxCount = 2;
                return spec;
            }
        }

        // 爆破槍類:直線 BurstFrame − 4 幀(§3.1),第 BurstFrame 幀空爆;碰到地形/人提早爆,打到地形爆炸半徑 × terrainScale;
        // 直擊不衰減(ReduceStartFrame −1)、直擊目標不再吃爆炸。例 Blaster:0.945 u/F、BurstFrame 13、直擊 125、
        // 爆炸 70 (1.025) → 50 (3.385) 線性、×0.4234。
        public static InkBulletSpec BlasterFamily(float spawnSpeed, int burstFrame, float directDamage,
            ExplosionSpec explosion, float terrainScale, float radiusForPlayer,
            AttackClass attackClass = AttackClass.Blaster)
        {
            InkBulletSpec spec = ShooterFamily(spawnSpeed, Mathf.Max(0, burstFrame - 4), spawnSpeed, directDamage,
                directDamage, -1, 0, radiusForPlayer, attackClass);
            spec.ExplodeOnImpact = true;
            spec.ExplodeAtFrame = burstFrame;
            spec.TerrainHitExplosionScale = terrainScale;
            spec.Explosion = explosion;
            spec.VisualScale = 1.6f;
            return spec;
        }

        // 滾筒/筆刷甩墨:減速 0.1/0.04、無下限;對人半徑可在前幾幀長大(Splat Roller 4F 內 0.12 → 1.02)
        public static InkBulletSpec RollerFamily(float spawnSpeed, int straightFrames, float damageMax, float damageMin,
            int reduceStartFrame, int reduceEndFrame, float radiusInit, float radiusEnd, int changeFrames,
            AttackClass attackClass = AttackClass.RollerFlick)
        {
            return new InkBulletSpec
            {
                Motion = BulletMotion.RollerFamily(spawnSpeed, straightFrames),
                RadiusForPlayer = radiusInit,
                RadiusForPlayerEnd = radiusEnd,
                RadiusForPlayerChangeFrames = changeFrames,
                RadiusForField = 0.2f,
                DamageMax = damageMax,
                DamageMin = damageMin,
                ReduceStartFrame = reduceStartFrame,
                ReduceEndFrame = reduceEndFrame,
                AttackClass = attackClass,
                Kind = DamageKind.Main,
                PaintWidthHalfNear = 1.2f,
                PaintWidthHalfFar = 1.2f,
                PaintDepthScaleMax = 1.4f,
                PaintDepthScaleMin = 1.0f,
                PaintRadiusShock = 1.2f,
                PaintHardness = 0.7f,
                VisualScale = 1.2f,
            };
        }

        // 弓:直線後直接自由落體(0.24/0.15),不衰減
        public static InkBulletSpec StringerFamily(float spawnSpeed, int straightFrames, float damage,
            float radiusForPlayer, AttackClass attackClass = AttackClass.Stringer)
        {
            return new InkBulletSpec
            {
                Motion = BulletMotion.StringerFamily(spawnSpeed, straightFrames),
                RadiusForPlayer = radiusForPlayer,
                RadiusForField = 0.2f,
                DamageMax = damage,
                DamageMin = damage,
                ReduceStartFrame = -1,
                AttackClass = attackClass,
                Kind = DamageKind.Main,
                PaintWidthHalfNear = 1.5f,
                PaintWidthHalfFar = 1.3f,
                PaintDepthScaleMax = 2.0f,
                PaintDepthScaleMin = 1.2f,
                PaintRadiusShock = 1.3f,
                PaintHardness = 0.7f,
                VisualScale = 0.8f,
            };
        }

        // 潑桶:直線後直接自由落體(0.12/0.05);傷害隨落差衰減由武器模組算(這裡給固定值)
        public static InkBulletSpec SlosherFamily(float spawnSpeed, int straightFrames, float damage,
            float radiusInit, float radiusEnd, int changeFrames, AttackClass attackClass = AttackClass.Slosher)
        {
            return new InkBulletSpec
            {
                Motion = BulletMotion.SlosherFamily(spawnSpeed, straightFrames),
                RadiusForPlayer = radiusInit,
                RadiusForPlayerEnd = radiusEnd,
                RadiusForPlayerChangeFrames = changeFrames,
                RadiusForField = 0.3f,
                DamageMax = damage,
                DamageMin = damage,
                ReduceStartFrame = -1,
                AttackClass = attackClass,
                Kind = DamageKind.Main,
                PaintWidthHalfNear = 1.6f,
                PaintWidthHalfFar = 1.6f,
                PaintDepthScaleMax = 1.6f,
                PaintDepthScaleMin = 1.2f,
                PaintRadiusShock = 1.5f,
                PaintHardness = 0.7f,
                VisualScale = 1.4f,
            };
        }

        // 刮水刀墨波:無阻力、重力 0(橫砍)/ 0.0027(Wiper 縱)/ 0.02(Stamper 縱),飛到 maxDistance 為止
        public static InkBulletSpec SplatanaFamily(float spawnSpeed, int straightFrames, float gravity, float damage,
            float maxDistance, float radiusForPlayer, AttackClass attackClass = AttackClass.SplatanaWave)
        {
            return new InkBulletSpec
            {
                Motion = BulletMotion.SplatanaFamily(spawnSpeed, straightFrames, gravity),
                MaxDistance = maxDistance,
                RadiusForPlayer = radiusForPlayer,
                RadiusForField = 0.2f,
                DamageMax = damage,
                DamageMin = damage,
                ReduceStartFrame = -1,
                AttackClass = attackClass,
                Kind = DamageKind.Main,
                PaintWidthHalfNear = 1.1f,
                PaintWidthHalfFar = 1.1f,
                PaintDepthScaleMax = 1.4f,
                PaintDepthScaleMin = 1.0f,
                PaintRadiusShock = 1.1f,
                EndPaintRadius = 1.1f,
                PaintHardness = 0.7f,
                VisualScale = 1.3f,
            };
        }
    }
}

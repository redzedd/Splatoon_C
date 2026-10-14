using System;
using SplatoonC.Core.Combat;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 一次爆炸的全部參數(可序列化;武器/副武器/大招設定資產直接內嵌一份)。Explosion.Detonate 讀它。
    // 布林欄位一律寫成「預設 false = S3 預設行為」(struct 沒有欄位初始值),例:IgnoreLineOfSight。
    // 範例(S3 v11.3.0,0 AP):
    //   Splat Bomb     180 (3.6) / 30 (7.0),塗 4.0 + 15 滴 r 1.064,擊退 700 / 12 → ExplosionSpec.SplatBomb
    //   Suction Bomb   180 (4.6) / 30 (8.0),塗 5.0;Burst Bomb 35 (2.8) / 25 (4.0),擊退 470 / 8
    //   Blaster        70 (1.025) → 50 (3.385) 線性,擊退 700;Luna 70 (1.7) → 50 (3.9)
    //   Trizooka 地形爆炸 53 (2.5) / 35 (4.0);Ink Mine 45 (3.6) / 35 (8.0)
    [Serializable]
    public struct ExplosionSpec
    {
        [Header("傷害(半徑量到目標碰撞體最近點)")]
        [Tooltip("近距離傷害(≤ NearRadius)")]
        public float NearDamage;

        [Tooltip("近距離半徑(公尺)")]
        public float NearRadius;

        [Tooltip("遠距離傷害(NearRadius < d ≤ FarRadius;線性模式下是外圈值)")]
        public float FarDamage;

        [Tooltip("遠距離半徑(公尺);再遠無傷")]
        public float FarRadius;

        [Tooltip("線性衰減(爆破槍:近 → 遠線性);關閉 = S3 炸彈的階梯(DamageLinear:false)")]
        public bool DamageLinear;

        [Tooltip("不做視線判定(預設會做:牆擋得住,角色擋不住;S3 炸彈規則)")]
        public bool IgnoreLineOfSight;

        [Tooltip("攻擊類別(物件受擊倍率,DamageRates)")]
        public AttackClass AttackClass;

        [Tooltip("傷害種類(主/副/大招;結算統計)")]
        public DamageKind Kind;

        [Header("塗墨")]
        [Tooltip("主塗墨半徑(公尺):爆心往下找地面,以落點為球心塗(跨台階的水平面都塗到);0 = 不塗")]
        public float PaintRadius;

        [Tooltip("噴墨射線數:隨機方向(偏下半球)打到的第一個面塗一小片,牆面也吃得到")]
        public int PaintRayCount;

        [Tooltip("每條噴墨射線落點的塗墨半徑(公尺;Splat Bomb 1.064)")]
        public float PaintRayRadius;

        [Tooltip("噴墨射線長度(公尺;0 = 用 FarRadius)")]
        public float PaintRayReach;

        [Tooltip("四散墨滴數(落在爆心周圍地面,S3 SplashAroundParam)")]
        public int DropCount;

        [Tooltip("每滴塗墨半徑(公尺)")]
        public float DropRadius;

        [Tooltip("墨滴飛散距離(公尺;落點在 0.5~1.0 倍之間;0 = PaintRadius × 1.6)")]
        public float DropReach;

        [Tooltip("塗墨筆刷硬度(0 = 預設 0.7)")]
        public float PaintHardness;

        [Tooltip("大招自己塗的墨不充大招(S3 規則);勾選 = 不充能")]
        public bool NoSpecialCharge;

        [Header("擊退(只作用於敵方角色)")]
        [Tooltip("擊退強度 KnockBackParam.Accel(必殺炸彈 700;Burst/Auto/Fizzy 470;0 = 無)")]
        public float KnockbackAccel;

        [Tooltip("擊退半徑 KnockBackParam.Distance(公尺;必殺炸彈 12)")]
        public float KnockbackRange;

        [Header("特效與音效")]
        [Tooltip("噴濺粒子數(前幾條噴墨射線的落點各噴一次 InkSplashFxPool)")]
        public int FxBursts;

        [Tooltip("爆炸閃光球半徑(公尺;0 = 用 NearRadius,再為 0 用 FarRadius;< 0 = 不顯示)")]
        public float FlashRadius;

        [Tooltip("爆炸音效 id(空字串 = 無)")]
        public string Sound;

        [Tooltip("音量(0 = 預設 1)")]
        public float SoundVolume;

        public float EffectiveHardness => PaintHardness > 0f ? PaintHardness : 0.7f;
        public float EffectiveRayReach => PaintRayReach > 0f ? PaintRayReach : FarRadius;
        public float EffectiveDropReach => DropReach > 0f ? DropReach : PaintRadius * 1.6f;
        public float EffectiveVolume => SoundVolume > 0f ? SoundVolume : 1f;

        // 最大影響半徑(傷害、擊退取大者):目標粗篩用
        public float MaxReach => Mathf.Max(FarRadius, Mathf.Max(NearRadius, KnockbackAccel > 0f ? KnockbackRange : 0f));

        public float Evaluate(float distance, float radiusScale = 1f)
        {
            float near = NearRadius * radiusScale;
            float far = FarRadius * radiusScale;
            return DamageLinear
                ? ExplosionDamageModel.EvaluateLinear(distance, near, far, NearDamage, FarDamage)
                : ExplosionDamageModel.Evaluate(distance, near, far, NearDamage, FarDamage);
        }

        // S3 必殺炸彈(Splat Bomb):180 (3.6) / 30 (7.0);塗 4.0 + 15 條 r 1.064;擊退 700 / 12 u(§4.3)
        public static ExplosionSpec SplatBomb => new ExplosionSpec
        {
            NearDamage = 180f,
            NearRadius = 3.6f,
            FarDamage = 30f,
            FarRadius = 7f,
            AttackClass = AttackClass.Bomb,
            Kind = DamageKind.Sub,
            PaintRadius = 4f,
            PaintRayCount = 15,
            PaintRayRadius = 1.064f,
            PaintRayReach = 7f,
            PaintHardness = 0.7f,
            KnockbackAccel = 700f,
            KnockbackRange = 12f,
            FxBursts = 6,
            Sound = "bomb_explode",
        };

        // 通用「必殺炸彈」樣板:近/遠傷害 180/30,擊退 700/12,塗墨半徑給定(Suction 5.0、Autobomb 4.0…)
        public static ExplosionSpec LethalBomb(float nearRadius, float farRadius, float paintRadius)
        {
            ExplosionSpec spec = SplatBomb;
            spec.NearRadius = nearRadius;
            spec.FarRadius = farRadius;
            spec.PaintRadius = paintRadius;
            spec.PaintRayReach = farRadius;
            return spec;
        }
    }
}

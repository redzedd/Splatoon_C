using UnityEngine;

namespace SplatoonC.Core.Combat
{
    // Splatoon 3 爆破槍的原始參數(Leanny v11.3.0 Blaster_Middle / Blaster_Short:距離 u、速度 u/F、時間 F、
    // 墨量 = 標準墨槽的分數;research 02 §12、DESIGN_REFERENCE §3.3/§3.5/§3.6),以及只由 S3 語意推得的戰鬥數值
    // (有效射程公式、空爆距離、擊倒發數、一槽發數、墨滴數)。彈道就是 S3 三狀態彈道(BulletBallistics),原值直接進 InkBulletSpec。
    // 省略欄位用 Template:Shooter_data_S3 預設:BrakeAirResist 0.36、XRate 0.4;BurstFrame 省略 = 13(Blaster)。
    // [EST] 只有 Blaster 空爆 / 撞地的爆炸塗墨半徑(SplashPaintRadius / SplashDropPaintRadius / SplashPaintShotColHitRadius
    // 在 Blaster_Middle 省略、預設值未公開):以 Luna 的明確值 × 兩者都公開的 SplashDropPaintShotColHitRadius 比例(2.5 / 2.76)推。
    public struct S3BlasterParams
    {
        public string Name;

        // DamageParam:直擊 ValueMax 1250 = 125(ReduceStartFrame −1 = 不衰減)
        public float DirectDamage;

        // BlastParam(DamageLinear:近 NearRadius 內 NearDamage,線性降到 FarRadius 的 FarDamage,再遠無傷)
        public float NearDamage;
        public float NearRadius;
        public float FarDamage;
        public float FarRadius;
        // KnockBackParam(Accel / Distance;Bias 0.8 意義不明,未用)
        public float KnockbackAccel;
        public float KnockbackDistance;

        // BlasterBurstParam:打到地形時爆炸半徑倍率(ShotCollisionHitRadiusRate)與空爆幀(BurstFrame)
        public float TerrainRadiusRate;
        public int BurstFrame;

        // MoveParam(直線幀 = BurstFrame − 4;JP-SYS「ブラスター種は共通して(B)が爆発フレーム数より4F短く」)
        public float SpawnSpeed;
        public int StraightFrames;
        public float StraightEndMaxSpeed;
        public float BrakeAirResist;

        // CollisionParam
        public float RadiusForPlayer;
        public float RadiusForField;

        // WeaponParam(spl__WeaponShooterParam)
        public int RepeatFrame;
        public float InkConsume;
        public int InkRecoverStop;
        public int ShotGuideFrame;
        public float MoveSpeed;
        // 第一發:人形 4 + PreDelayFrame_HumanShot(10)= 14F;烏賊 9 + PreDelayFrame_SquidShot(15)− SquidShotShorteningFrame(0)= 24F
        public int FirstShotFrames;
        public int SquidFirstShotFrames;
        // PostDelayFrame_Blaster:開火後多久才能變烏賊 / 丟副武器
        public int PostDelayFrames;
        // 散布:地面 0°(完全不散);空中 Jump_DegSwerve、偏向 b 固定 Jump_DegBiasMax 0.5,起跳 25F 後開始收、70F 回到地面值
        public float StandSwerve;
        public float JumpSwerve;
        public float JumpBiasMax;
        public int JumpBiasDecreaseStartFrame;
        public int JumpBiasEndFrame;
        // 玩家速度疊加到墨彈(spl__SpawnBulletAdditionMovePlayerParam:前向 ZRate 2.0、橫向 XRate 預設 0.4)
        public float InheritForwardRate;
        public float InheritSideRate;

        // SplashPaintParam / SplashSpawnParam:沿路墨滴(每 SpawnBetweenLength 一滴、最多 SpawnNum 滴,半寬 WidthHalf)、
        // 腳前墨滴(每發都有;SpawnNearestLength 處、半寬 WidthHalfNearest)。落差 < DepthMaxDropHeight(3 u)時滴長 ×1.2
        public float DropletWidthHalf;
        public float NearestWidthHalf;
        public float SpawnNearestLength;
        public float SpawnBetweenLength;
        public float SpawnNum;

        // BlasterBurstParam 塗墨:空爆 / 撞地(ShotColHit)各一組——爆心附近球形塗墨(SplashPaint*)、爆心正下方地面(SplashDrop*)
        public float BurstPaintRadius;
        public float BurstDropPaintRadius;
        public float TerrainPaintRadius;
        public float TerrainDropPaintRadius;
        // SplashWallDropPaintParam.PaintRadiusShock:爆炸噴到牆上的墨(1.2)
        public float BurstWallPaintRadius;

        // ── 由 S3 參數推得 ──

        public BulletMotion Motion => new BulletMotion
        {
            SpawnSpeed = SpawnSpeed,
            StraightFrames = StraightFrames,
            StraightEndMaxSpeed = StraightEndMaxSpeed,
            BrakeAirResist = BrakeAirResist,
            BrakeGravity = 0.07f,
            BrakeToFreeVelocityXZ = 0.2355f,
            BrakeToFreeVelocityY = -0.15f,
            FreeAirResist = 0.02f,
            FreeGravity = 0.016f,
        };

        // 有效射程(準星鎖定距離,Template:Effective_range;Blaster 10.14、Luna 7.65)
        public float EffectiveRange => BulletBallistics.EffectiveRange(Motion, ShotGuideFrame, RadiusForPlayer);

        // 空爆距離:飛滿 BurstFrame 幀的沿彈道距離(Blaster ≈ 9.86 u、Luna ≈ 7.35 u)
        public float BurstDistance => BulletBallistics.EffectiveRange(Motion, BurstFrame, 0f);

        // 空爆最遠炸得到的距離(爆心 + 外圈半徑)
        public float MaxSplashReach => BurstDistance + FarRadius;

        public float FireIntervalSeconds => RepeatFrame / 60f;
        public float FirstShotSeconds => FirstShotFrames / 60f;
        public float SquidFirstShotSeconds => SquidFirstShotFrames / 60f;
        public float PostDelaySeconds => PostDelayFrames / 60f;
        public float InkLockSeconds => InkRecoverStop / 60f;
        public float MoveSpeedWhileFiring => MoveSpeed * 60f;

        // 滿槽發數(10% → 10 發;7.5% → 13 發)
        public int ShotsPerTank => InkConsume > 0f ? Mathf.FloorToInt(1f / InkConsume + 1e-4f) : int.MaxValue;

        // 直擊幾發擊倒(125 → 1)
        public int DirectHitsToSplat(float hp = 100f)
        {
            return DirectDamage > 0f ? Mathf.CeilToInt(hp / DirectDamage - 1e-4f) : int.MaxValue;
        }

        // 爆風最少幾發擊倒(內圈 70 → 2;外圈 50 → 2)
        public int SplashHitsToSplat(float damage, float hp = 100f)
        {
            return damage > 0f ? Mathf.CeilToInt(hp / damage - 1e-4f) : int.MaxValue;
        }

        // 爆風兩發擊倒的 TTK(首發命中後):(2 − 1) × RepeatFrame = 50F / 40F
        public int SplashTtkFrames(float hp = 100f)
        {
            return (SplashHitsToSplat(FarDamage, hp) - 1) * RepeatFrame;
        }

        // 爆炸傷害(距離量到目標最近點;terrainHit = 打到地形,半徑 × ShotCollisionHitRadiusRate)
        public float ExplosionDamage(float distance, bool terrainHit)
        {
            float scale = terrainHit ? TerrainRadiusRate : 1f;
            return ExplosionDamageModel.EvaluateLinear(distance, NearRadius * scale, FarRadius * scale, NearDamage, FarDamage);
        }

        // 沿路墨滴數:從 SpawnBetweenLength 起每 SpawnBetweenLength 一滴,放到結束點為止,最多 SpawnNum 滴
        //(Blaster 空爆 9.86 u → 6 滴;Luna 7.35 u → 4 滴。JP-SYS:爆破槍的墨滴每發一樣、沒有輪替樣式)
        public int DropletCount(float travelled)
        {
            if (SpawnBetweenLength <= 0f || travelled < SpawnBetweenLength)
            {
                return 0;
            }
            int count = Mathf.FloorToInt(travelled / SpawnBetweenLength + 1e-4f);
            return Mathf.Min(count, Mathf.FloorToInt(SpawnNum + 1e-4f));
        }

        // 墨滴塗墨半徑:S3 落差 < 3 u 時滴長 ×1.2(槍口高度 ~1 u 平射都算這種),取等面積圓 [EST] = 半寬 × √1.2
        public float DropletPaintRadius => DropletWidthHalf * Mathf.Sqrt(1.2f);
        public float NearestPaintRadius => NearestWidthHalf * Mathf.Sqrt(1.2f);

        public SpreadSettings ToSpreadSettings()
        {
            return new SpreadSettings
            {
                StandSwerveDeg = StandSwerve,
                JumpSwerveDeg = JumpSwerve,
                BiasMin = 0f,
                BiasKf = 0f,
                BiasMax = 0f,
                BiasDecreasePerSecond = 0f,
                RepeatSeconds = RepeatFrame / 60f,
                JumpBiasMax = JumpBiasMax,
                JumpShrinkStartSeconds = JumpBiasDecreaseStartFrame / 60f,
                JumpShrinkEndSeconds = JumpBiasEndFrame / 60f,
            };
        }

        // Blaster_Middle(Blaster):research/raw/BlasterMiddle.json
        public static S3BlasterParams Blaster => new S3BlasterParams
        {
            Name = "Blaster",
            DirectDamage = 125f,
            NearDamage = 70f, NearRadius = 1.025f, FarDamage = 50f, FarRadius = 3.385f,
            KnockbackAccel = 700f, KnockbackDistance = 3.5f,
            TerrainRadiusRate = 0.4234f, BurstFrame = 13,
            SpawnSpeed = 0.945f, StraightFrames = 9, StraightEndMaxSpeed = 0.9131f, BrakeAirResist = 0.36f,
            RadiusForPlayer = 0.285f, RadiusForField = 0.2f,
            RepeatFrame = 50, InkConsume = 0.1f, InkRecoverStop = 57, ShotGuideFrame = 13, MoveSpeed = 0.045f,
            FirstShotFrames = 14, SquidFirstShotFrames = 24, PostDelayFrames = 22,
            StandSwerve = 0f, JumpSwerve = 10f, JumpBiasMax = 0.5f, JumpBiasDecreaseStartFrame = 25, JumpBiasEndFrame = 70,
            InheritForwardRate = 2f, InheritSideRate = 0.4f,
            DropletWidthHalf = 1.62f, NearestWidthHalf = 2.43f, SpawnNearestLength = 0.5f, SpawnBetweenLength = 1.5f,
            SpawnNum = 7f,
            // [EST] 以 Luna × (2.5 / 2.76) 推;撞地的地面塗墨 2.5 是 S3 明確值
            BurstPaintRadius = 2.17f, BurstDropPaintRadius = 3.48f, TerrainPaintRadius = 1.52f, TerrainDropPaintRadius = 2.5f,
            BurstWallPaintRadius = 1.2f,
        };

        // Blaster_Short(Luna Blaster):research/raw/BlasterShort.json
        public static S3BlasterParams Luna => new S3BlasterParams
        {
            Name = "Luna Blaster",
            DirectDamage = 125f,
            NearDamage = 70f, NearRadius = 1.7f, FarDamage = 50f, FarRadius = 3.9f,
            KnockbackAccel = 700f, KnockbackDistance = 4f,
            TerrainRadiusRate = 0.4166f, BurstFrame = 11,
            SpawnSpeed = 0.87f, StraightFrames = 7, StraightEndMaxSpeed = 0.8484f, BrakeAirResist = 0.36f,
            RadiusForPlayer = 0.3f, RadiusForField = 0.2f,
            RepeatFrame = 40, InkConsume = 0.075f, InkRecoverStop = 55, ShotGuideFrame = 11, MoveSpeed = 0.05f,
            FirstShotFrames = 14, SquidFirstShotFrames = 24, PostDelayFrames = 26,
            StandSwerve = 0f, JumpSwerve = 5.5f, JumpBiasMax = 0.5f, JumpBiasDecreaseStartFrame = 25, JumpBiasEndFrame = 70,
            InheritForwardRate = 2f, InheritSideRate = 0.4f,
            DropletWidthHalf = 1.56f, NearestWidthHalf = 2.496f, SpawnNearestLength = 1f, SpawnBetweenLength = 1.5f,
            SpawnNum = 6f,
            BurstPaintRadius = 2.4f, BurstDropPaintRadius = 3.84f, TerrainPaintRadius = 1.68f, TerrainDropPaintRadius = 2.76f,
            BurstWallPaintRadius = 1.2f,
        };
    }
}

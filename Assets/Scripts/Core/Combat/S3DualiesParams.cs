using UnityEngine;

namespace SplatoonC.Core.Combat
{
    // Splatoon 3 雙槍的原始參數(Leanny v11.3.0 GameParameters:距離 u、速度 u/F、時間 F、墨量 = 標準墨槽的分數),
    // 以及只由 S3 語意推得的戰鬥數值(有效射程公式、擊倒發數、TTK、一槽發數)。雙槍彈道就是 S3 三狀態彈道
    //(BulletBallistics),不需要射擊槍那套兩段式換算,原值直接進 InkBulletSpec。
    // 省略欄位用 Template:Shooter_data_S3 預設:Stand_DegBiasMax 0.25、BrakeAirResist 0.36、InkRecoverStop 20、PostDelayFrame 4。
    public struct S3DualiesParams
    {
        public string Name;

        // DamageParam(ValueMax/Min ÷ 10)與衰減幀
        public float DamageMax;
        public float DamageMin;
        public int ReduceStartFrame;
        public int ReduceEndFrame;

        // MoveParam
        public float SpawnSpeed;
        public int StraightFrames;
        public float StraightEndMaxSpeed;
        public float BrakeAirResist;

        // CollisionParam(一般)/ CollisionLapOverParam(砲台模式:兩把槍併攏,判定變大)
        public float RadiusForPlayer;
        public float TurretRadiusForPlayer;
        public float RadiusForField;

        // WeaponParam(spl__WeaponManeuverParam)
        public int RepeatFrame;
        // 砲台模式射擊間隔(LapOver_RepeatFrame)
        public int TurretRepeatFrame;
        public float InkConsume;
        public int InkRecoverStop;
        public int ShotGuideFrame;
        public float MoveSpeed;
        public float StandSwerve;
        public float JumpSwerve;
        // 砲台模式偏角(LapOver_DegSwerve;Splat Dualies 0 = 完全不散)
        public float TurretSwerve;
        public float BiasMin;
        public float BiasKf;
        public float BiasMax;
        public float BiasDecrease;
        public float JumpBiasMax;
        public int JumpBiasDecreaseStartFrame;
        public int JumpBiasEndFrame;
        // 第一發:人形 3F;烏賊 9 + PreDelayFrame_SquidShot 4 − SquidShotShorteningFrame(Splat Dualies 0)= 13F
        public int FirstShotFrames;
        public int SquidFirstShotFrames;
        // 開火後多久才能變烏賊/丟副武器(PostDelayFrame)
        public int PostDelayFrames;
        // 玩家移動速度疊加到墨彈(spl__SpawnBulletAdditionMovePlayerParam:前向 ZRate 2.0、橫向 XRate 預設 0.4)
        public float InheritForwardRate;
        public float InheritSideRate;

        // PaintParam
        public float WidthHalfNear;
        public float WidthHalfFar;
        public float DepthScaleMax;
        public float DepthScaleMin;
        public float DepthScaleMinBreakFree;
        // WallDropCollisionPaintParam.PaintRadiusShock(打在牆上的圓)
        public float PaintRadiusShock;

        // SplashPaintParam / SplashSpawnParam:每發 SpawnNum 滴、每 SpawnBetweenLength 一滴、SplitNum 種樣式輪替、沒有腳前強制墨滴
        public float DropletWidthHalf;
        public float SpawnBetweenLength;
        public float SpawnNum;
        public int SplitNum;

        // SideStepParam
        public DodgeRollParams Roll;

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

        // ── 由 S3 參數推得 ──

        // 有效射程(準星鎖定距離,Template:Effective_range;砲台模式同彈速同射程)
        public float EffectiveRange => BulletBallistics.EffectiveRange(Motion, ShotGuideFrame, RadiusForPlayer);

        public float FireIntervalSeconds => RepeatFrame / 60f;
        public float TurretFireIntervalSeconds => TurretRepeatFrame / 60f;
        public float MoveSpeedWhileFiring => MoveSpeed * 60f;
        public float InkLockSeconds => InkRecoverStop / 60f;

        public int ShotsToSplat(float hp = 100f)
        {
            return DamageMax > 0f ? Mathf.CeilToInt(hp / DamageMax - 1e-4f) : int.MaxValue;
        }

        // 首發命中後到擊倒的幀數:一般 (4 − 1) × 5 = 15F、砲台 (4 − 1) × 4 = 12F
        public int TtkFrames(float hp = 100f)
        {
            return (ShotsToSplat(hp) - 1) * RepeatFrame;
        }

        public int TurretTtkFrames(float hp = 100f)
        {
            return (ShotsToSplat(hp) - 1) * TurretRepeatFrame;
        }

        // 滿槽發數(0.72% → 138 發)
        public int ShotsPerTank => InkConsume > 0f ? Mathf.FloorToInt(1f / InkConsume + 1e-4f) : int.MaxValue;

        public float FullTankFireSeconds => ShotsPerTank * FireIntervalSeconds;
        public float TurretFullTankFireSeconds => ShotsPerTank * TurretFireIntervalSeconds;

        // 滿槽可連滾幾次(每滾 7%)
        public int RollsPerTank => Roll.InkConsume > 0f ? Mathf.FloorToInt(1f / Roll.InkConsume + 1e-4f) : int.MaxValue;

        // 第 n 發(從 0 起)的墨滴落點(沿彈道距離)[EST 解讀]:S3 每發滴 floor(SpawnNum·n) − floor(SpawnNum·(n−1)) 滴
        //(Splat Dualies SpawnNum 1.0 → 每發剛好 1 滴)、每 SpawnBetweenLength 一滴、SplitNum 種樣式輪替。
        // 間距 14 u 比彈能飛的距離(準星鎖定幀內 10.58 u)還長,所以把 SplitNum 種相位平均鋪在「間距與可飛距離較短者」之內,
        // 每發都滴得出來,連續 SplitNum 發把沿路錯開填滿。
        public float DropletPhaseDistance(int shotIndex)
        {
            return DropletPhase(shotIndex, SplitNum, SpawnBetweenLength, EffectiveRange - RadiusForPlayer);
        }

        public static float DropletPhase(int shotIndex, int splitNum, float spacing, float reach)
        {
            int split = Mathf.Max(1, splitNum);
            int pattern = ((shotIndex % split) + split) % split;
            float window = reach > 0f ? Mathf.Min(spacing, reach) : spacing;
            return window * (pattern + 0.5f) / split;
        }

        public SpreadSettings ToSpreadSettings()
        {
            return new SpreadSettings
            {
                StandSwerveDeg = StandSwerve,
                JumpSwerveDeg = JumpSwerve,
                BiasMin = BiasMin,
                BiasKf = BiasKf,
                BiasMax = BiasMax,
                BiasDecreasePerSecond = BiasDecrease * 60f,
                RepeatSeconds = RepeatFrame / 60f,
                JumpBiasMax = JumpBiasMax,
                JumpShrinkStartSeconds = JumpBiasDecreaseStartFrame / 60f,
                JumpShrinkEndSeconds = JumpBiasEndFrame / 60f,
            };
        }

        // Maneuver_Normal(Splat Dualies):research 02 §10.2 / raw ManeuverNormal.json
        public static S3DualiesParams SplatDualies => new S3DualiesParams
        {
            Name = "Splat Dualies",
            DamageMax = 30f, DamageMin = 15f, ReduceStartFrame = 7, ReduceEndFrame = 15,
            SpawnSpeed = 2.37f, StraightFrames = 3, StraightEndMaxSpeed = 2.3425f, BrakeAirResist = 0.36f,
            RadiusForPlayer = 0.31f, TurretRadiusForPlayer = 0.335f, RadiusForField = 0.2f,
            RepeatFrame = 5, TurretRepeatFrame = 4, InkConsume = 0.0072f, InkRecoverStop = 20, ShotGuideFrame = 7,
            MoveSpeed = 0.08f,
            StandSwerve = 2f, JumpSwerve = 7.5f, TurretSwerve = 0f,
            BiasMin = 0.01f, BiasKf = 0.01f, BiasMax = 0.25f, BiasDecrease = 0.005f,
            JumpBiasMax = 0.4f, JumpBiasDecreaseStartFrame = 25, JumpBiasEndFrame = 70,
            FirstShotFrames = 3, SquidFirstShotFrames = 13, PostDelayFrames = 4,
            InheritForwardRate = 2f, InheritSideRate = 0.4f,
            WidthHalfNear = 1.71f, WidthHalfFar = 1.66f, DepthScaleMax = 2.24f, DepthScaleMin = 1.31f,
            DepthScaleMinBreakFree = 1.12f, PaintRadiusShock = 1.3f,
            DropletWidthHalf = 1.55825f, SpawnBetweenLength = 14f, SpawnNum = 1f, SplitNum = 7,
            Roll = DodgeRollParams.SplatDualies,
        };
    }
}

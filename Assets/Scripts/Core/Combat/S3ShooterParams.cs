using UnityEngine;

namespace SplatoonC.Core.Combat
{
    // Splatoon 3 射擊槍的原始參數(Leanny v11.3.0 GameParameters:距離 u、速度 u/F、時間 F、墨量 = 標準墨槽的分數),
    // 以及只由 S3 語意推得的戰鬥數值(有效射程公式、擊倒發數、TTK、一槽發數、預估塗地面積)。
    // 換算到本專案兩段式彈道的規則在 ShooterCalibration;這裡不含任何本專案的調參。
    public struct S3ShooterParams
    {
        public string Name;

        // DamageParam(ValueMax/Min ÷ 10)與衰減幀
        public float DamageMax;
        public float DamageMin;
        public int ReduceStartFrame;
        public int ReduceEndFrame;

        // MoveParam:STRAIGHT 以 SpawnSpeed 飛 StraightFrames 幀,進 BRAKE 時速度夾到 StraightEndMaxSpeed
        public float SpawnSpeed;
        public int StraightFrames;
        public float StraightEndMaxSpeed;
        public float BrakeAirResist;

        // CollisionParam:對人判定半徑(有效射程公式要加上它)
        public float EndRadiusForPlayer;

        // WeaponParam
        public int RepeatFrame;
        public float InkConsume;
        // 墨槽容量(標準 = 1;Splattershot Jr. = 1.1,sendou inkTankSize)
        public float InkTankScale;
        public int InkRecoverStop;
        public int ShotGuideFrame;
        public float MoveSpeed;
        public float StandSwerve;
        public float JumpSwerve;
        public float BiasMin;
        public float BiasKf;
        public float BiasMax;
        public float BiasDecrease;
        public float JumpBiasMax;
        public int JumpBiasDecreaseStartFrame;
        public int JumpBiasEndFrame;
        // MainWeaponSetting.WeaponSpeedType == Fast(輕量級)
        public bool LightWeight;

        // PaintParam:地面落點橢圓半寬(≤1.1 u / 20 u)與沿飛行方向的拉長倍率(落差 > 10 u 用 BreakFree)
        public float WidthHalfNear;
        public float WidthHalfFar;
        public float DepthScaleMax;
        public float DepthScaleMin;
        public float DepthScaleMinBreakFree;
        // WallDropCollisionPaintParam.PaintRadiusShock:打在牆上的圓半徑
        public float PaintRadiusShock;
        // CollisionParam:對地形判定半徑
        public float RadiusForField;

        // SplashPaintParam / SplashSpawnParam:飛行途中墨滴(每發 SpawnNum 滴)、腳前強制墨滴(SplitNum 種樣式中佔幾種)
        public float DropletWidthHalf;
        public float DropletWidthHalfNearest;
        public float SpawnNum;
        public int SplitNum;
        public int FeetDropPatterns;
        public float SpawnNearestLength;
        // SplashSpawnParam.SpawnBetweenLength:飛行途中每幾 u 滴一滴
        public float SpawnBetweenLength;

        // ── 由 S3 參數推得 ──

        // 有效射程(準星鎖定距離 = 無衰減距離;Inkipedia Template:Effective_range):
        // R = SpawnSpeed·N + E·(1−r)·(1−(1−r)^(G−N))/r + EndRadiusForPlayer
        public float EffectiveRange
        {
            get
            {
                float r = BrakeAirResist > 0f ? BrakeAirResist : 0.36f;
                float e = Mathf.Min(SpawnSpeed, StraightEndMaxSpeed);
                int brakeFrames = Mathf.Max(0, ShotGuideFrame - StraightFrames);
                float keep = 1f - r;
                float brake = e * keep * (1f - Mathf.Pow(keep, brakeFrames)) / r;
                return SpawnSpeed * StraightFrames + brake + EndRadiusForPlayer;
            }
        }

        public float FireIntervalSeconds => RepeatFrame / 60f;

        public float MoveSpeedWhileFiring => MoveSpeed * 60f;

        public float InkLockSeconds => InkRecoverStop / 60f;

        public int FalloffFrames => ReduceEndFrame - ReduceStartFrame;

        // 擊倒發數(全傷直擊):S3 顯示到 0.1,100 / 36 → 3 發
        public int ShotsToSplat(float hp = 100f)
        {
            return DamageMax > 0f ? Mathf.CeilToInt(hp / DamageMax - 1e-4f) : int.MaxValue;
        }

        // 首發命中後到擊倒的幀數 = (發數 − 1) × 射擊間隔
        public int TtkFrames(float hp = 100f)
        {
            return (ShotsToSplat(hp) - 1) * RepeatFrame;
        }

        // 滿槽可射發數(整發消耗;Jr. 110% ÷ 0.43% = 255)
        public int ShotsPerTank
        {
            get
            {
                float tank = InkTankScale > 0f ? InkTankScale : 1f;
                return InkConsume > 0f ? Mathf.FloorToInt(tank / InkConsume + 1e-4f) : int.MaxValue;
            }
        }

        public float FullTankFireSeconds => ShotsPerTank * FireIntervalSeconds;

        // [EST] 單發塗地面積(u²,不計彼此重疊):落點橢圓(平均半寬 × 平均拉長)+ 飛行墨滴(長 ×1.1)+ 腳前墨滴(出現頻率)。
        // 只用來比較各武器相對 Splattershot 的塗地量,S3 沒有公開逐發面積。
        public float EstimatedPaintAreaPerShot
        {
            get
            {
                float width = 0.5f * (WidthHalfNear + WidthHalfFar);
                float depth = 0.5f * (DepthScaleMax + DepthScaleMin);
                float bullet = Mathf.PI * width * width * depth;
                float droplets = SpawnNum * Mathf.PI * DropletWidthHalf * DropletWidthHalf * 1.1f;
                float feetRate = SplitNum > 0 ? FeetDropPatterns / (float)SplitNum : 0f;
                float feet = feetRate * Mathf.PI * DropletWidthHalfNearest * DropletWidthHalfNearest;
                return bullet + droplets + feet;
            }
        }

        // 第 shotIndex 發(從 0 起)的墨滴起點(沿彈道距離)[EST 解讀]:與雙槍同樣的 SplitNum 種等距相位
        //(S3DualiesParams.DropletPhase:鋪在「間距與可飛距離較短者」之內),但「第幾發用哪個相位」以跨步 DropletStride 打散——
        // 依序輪替時連射 0.5 秒(射擊槍約 5 發)只輪到前半的相位,路後段會有洞;打散後任意連續幾發都均勻鋪滿整段
        //(使用者驗收 2026-09-02「0.5 秒連射即成路」)。一輪 SplitNum 發仍剛好每個相位各一次。
        public static float DropletPhase(int shotIndex, int splitNum, float spacing, float reach)
        {
            int split = Mathf.Max(1, splitNum);
            int index = ((shotIndex % split) + split) % split;
            int pattern = index * DropletStride(split) % split;
            return S3DualiesParams.DropletPhase(pattern, split, spacing, reach);
        }

        // 相位跨步:與 SplitNum 互質、最接近 0.382·SplitNum(黃金分割,連續幾發落點最分散);找不到(≤ 4 或 6)就 1 = 依序
        public static int DropletStride(int splitNum)
        {
            int best = 1;
            float want = splitNum * 0.382f;
            float bestError = float.MaxValue;
            for (int k = 2; k < splitNum - 1; k++)
            {
                if (Gcd(k, splitNum) != 1)
                {
                    continue;
                }
                float error = Mathf.Abs(k - want);
                if (error < bestError)
                {
                    bestError = error;
                    best = k;
                }
            }
            return best;
        }

        private static int Gcd(int a, int b)
        {
            while (b != 0)
            {
                int t = a % b;
                a = b;
                b = t;
            }
            return a;
        }

        // S3 散布參數 → SpreadModel 設定(每幀量 × 60 = 每秒量)
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

        // ── Leanny v11.3.0(省略的欄位用 Template:Shooter_data_S3 預設:RepeatFrame 6、InkRecoverStop 20、
        //    Stand_DegBiasMax 0.25、BrakeAirResist 0.36)──

        // Shooter_Normal
        public static S3ShooterParams Splattershot => new S3ShooterParams
        {
            Name = "Splattershot",
            DamageMax = 36f, DamageMin = 18f, ReduceStartFrame = 8, ReduceEndFrame = 40,
            SpawnSpeed = 2.266f, StraightFrames = 4, StraightEndMaxSpeed = 1.493f, BrakeAirResist = 0.36f,
            EndRadiusForPlayer = 0.285f,
            RepeatFrame = 6, InkConsume = 0.0092f, InkTankScale = 1f, InkRecoverStop = 20, ShotGuideFrame = 8,
            MoveSpeed = 0.072f,
            StandSwerve = 4.86f, JumpSwerve = 11.66f, BiasMin = 0.01f, BiasKf = 0.01f, BiasMax = 0.25f,
            BiasDecrease = 0.015f, JumpBiasMax = 0.4f, JumpBiasDecreaseStartFrame = 25, JumpBiasEndFrame = 70,
            LightWeight = false,
            WidthHalfNear = 1.93f, WidthHalfFar = 1.71f, DepthScaleMax = 2.24f, DepthScaleMin = 1.31f,
            DropletWidthHalf = 1.472f, DropletWidthHalfNearest = 2.0608f, SpawnNum = 1.5f, SplitNum = 8,
            FeetDropPatterns = 1, SpawnNearestLength = 1.2f,
            RadiusForField = 0.2f, DepthScaleMinBreakFree = 1.12f, PaintRadiusShock = 1.56f, SpawnBetweenLength = 9.2f,
        };

        // Shooter_First:110% 墨槽、InkRecoverStop 15、PostDelay 2
        public static S3ShooterParams SplattershotJr => new S3ShooterParams
        {
            Name = "Splattershot Jr.",
            DamageMax = 28f, DamageMin = 14f, ReduceStartFrame = 8, ReduceEndFrame = 24,
            SpawnSpeed = 2.266f, StraightFrames = 3, StraightEndMaxSpeed = 1.9513f, BrakeAirResist = 0.36f,
            EndRadiusForPlayer = 0.3f,
            RepeatFrame = 5, InkConsume = 0.0043f, InkTankScale = 1.1f, InkRecoverStop = 15, ShotGuideFrame = 8,
            MoveSpeed = 0.076f,
            StandSwerve = 11.66f, JumpSwerve = 14.58f, BiasMin = 0.04f, BiasKf = 0.02f, BiasMax = 0.4f,
            BiasDecrease = 0.01f, JumpBiasMax = 0.4f, JumpBiasDecreaseStartFrame = 25, JumpBiasEndFrame = 70,
            LightWeight = true,
            WidthHalfNear = 2.11f, WidthHalfFar = 1.77f, DepthScaleMax = 2.62f, DepthScaleMin = 1.31f,
            DropletWidthHalf = 1.4835f, DropletWidthHalfNearest = 2.0769f, SpawnNum = 1.4f, SplitNum = 7,
            FeetDropPatterns = 1, SpawnNearestLength = 1.2f,
            RadiusForField = 0.2f, DepthScaleMinBreakFree = 1.12f, PaintRadiusShock = 1.56f, SpawnBetweenLength = 9.5f,
        };

        // Shooter_Gravity(.52 Gal):10.1.0 起初速 3.06、直飛 3F、耗墨 1.5%
        public static S3ShooterParams Gal52 => new S3ShooterParams
        {
            Name = ".52 Gal",
            DamageMax = 52f, DamageMin = 30f, ReduceStartFrame = 11, ReduceEndFrame = 27,
            SpawnSpeed = 3.06f, StraightFrames = 3, StraightEndMaxSpeed = 1.667f, BrakeAirResist = 0.36f,
            EndRadiusForPlayer = 0.285f,
            RepeatFrame = 9, InkConsume = 0.015f, InkTankScale = 1f, InkRecoverStop = 20, ShotGuideFrame = 11,
            MoveSpeed = 0.06f,
            StandSwerve = 6f, JumpSwerve = 12f, BiasMin = 0.02f, BiasKf = 0.03f, BiasMax = 0.25f,
            BiasDecrease = 0.015f, JumpBiasMax = 0.4f, JumpBiasDecreaseStartFrame = 25, JumpBiasEndFrame = 70,
            LightWeight = false,
            WidthHalfNear = 2.11f, WidthHalfFar = 2.07f, DepthScaleMax = 2.24f, DepthScaleMin = 1.31f,
            DropletWidthHalf = 1.495f, DropletWidthHalfNearest = 2.16775f, SpawnNum = 2.4f, SplitNum = 5,
            FeetDropPatterns = 1, SpawnNearestLength = 1.3f,
            RadiusForField = 0.2f, DepthScaleMinBreakFree = 1.12f, PaintRadiusShock = 1.56f, SpawnBetweenLength = 5.5f,
        };

        // Shooter_QuickMiddle(N-ZAP '85)
        public static S3ShooterParams NZap85 => new S3ShooterParams
        {
            Name = "N-ZAP '85",
            DamageMax = 30f, DamageMin = 15f, ReduceStartFrame = 8, ReduceEndFrame = 24,
            SpawnSpeed = 2.2f, StraightFrames = 4, StraightEndMaxSpeed = 1.4495f, BrakeAirResist = 0.36f,
            EndRadiusForPlayer = 0.285f,
            RepeatFrame = 5, InkConsume = 0.008f, InkTankScale = 1f, InkRecoverStop = 20, ShotGuideFrame = 8,
            MoveSpeed = 0.088f,
            StandSwerve = 4.75f, JumpSwerve = 12f, BiasMin = 0.01f, BiasKf = 0.005f, BiasMax = 0.25f,
            BiasDecrease = 0.005f, JumpBiasMax = 0.4f, JumpBiasDecreaseStartFrame = 25, JumpBiasEndFrame = 70,
            LightWeight = true,
            WidthHalfNear = 1.93f, WidthHalfFar = 1.71f, DepthScaleMax = 2.24f, DepthScaleMin = 1.31f,
            DropletWidthHalf = 1.334f, DropletWidthHalfNearest = 1.97432f, SpawnNum = 1.4f, SplitNum = 11,
            FeetDropPatterns = 2, SpawnNearestLength = 1f,
            RadiusForField = 0.2f, DepthScaleMinBreakFree = 1.12f, PaintRadiusShock = 1.56f, SpawnBetweenLength = 11.0f,
        };

        // Shooter_Expert(Splattershot Pro):沒有腳前強制墨滴(ForceSpawnNearestAddNumArray 空)
        public static S3ShooterParams SplattershotPro => new S3ShooterParams
        {
            Name = "Splattershot Pro",
            DamageMax = 45f, DamageMin = 22.5f, ReduceStartFrame = 7, ReduceEndFrame = 23,
            SpawnSpeed = 3.833333f, StraightFrames = 3, StraightEndMaxSpeed = 2.2698f, BrakeAirResist = 0.36f,
            EndRadiusForPlayer = 0.235f,
            RepeatFrame = 8, InkConsume = 0.02f, InkTankScale = 1f, InkRecoverStop = 20, ShotGuideFrame = 7,
            MoveSpeed = 0.055f,
            StandSwerve = 2.5f, JumpSwerve = 6f, BiasMin = 0.01f, BiasKf = 0.01f, BiasMax = 0.25f,
            BiasDecrease = 0.005f, JumpBiasMax = 0.4f, JumpBiasDecreaseStartFrame = 25, JumpBiasEndFrame = 70,
            LightWeight = false,
            WidthHalfNear = 2.08f, WidthHalfFar = 1.84f, DepthScaleMax = 2.24f, DepthScaleMin = 1.31f,
            DropletWidthHalf = 1.4375f, DropletWidthHalfNearest = 2.0125f, SpawnNum = 2f, SplitNum = 6,
            FeetDropPatterns = 0, SpawnNearestLength = 0.8f,
            RadiusForField = 0.2f, DepthScaleMinBreakFree = 1.12f, PaintRadiusShock = 1.56f, SpawnBetweenLength = 8.8f,
        };
    }
}

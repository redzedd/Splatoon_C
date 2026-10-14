using System;
using UnityEngine;

namespace SplatoonC.Core.Combat
{
    // 甩墨的一組墨團(S3 UnitGroupParam.Unit[i]):同一單位的 BulletNum 顆依序排開——
    // 橫甩:水平偏角在 ±WideDegree 內等分、出生點在 PositionWidth 寬的橫線上;
    // 直甩:一列逐顆變慢/變低(AfterOffsetSpeed / AfterOffsetPitchDeg / HeightStep)。
    // 單位沿用 datamine:速度 u/F、角度度、長度 u(= 公尺)、幀 F(60 Hz)。
    [Serializable]
    public struct RollerFlingUnit
    {
        [Tooltip("顆數(BulletNum;S3 省略 = 1)")]
        public int BulletNum;

        [Tooltip("墨不足弱甩的顆數(DepletionBulletNum;S3 省略 = 同 BulletNum)")]
        public int DepletionBulletNum;

        [Tooltip("初速(u/F;SpawnSpeedBase)")]
        public float SpawnSpeed;

        [Tooltip("初速隨機 ±(u/F;SpawnSpeedRandom)")]
        public float SpawnSpeedRandom;

        [Tooltip("同一單位第 i 顆初速 += i × 此值(AfterOffsetSpawnSpeed;直甩一列逐顆變慢)")]
        public float AfterOffsetSpeed;

        [Tooltip("水平扇形半角(度;SpawnWideDegree:偏角在 ±此值內等分)")]
        public float WideDegree;

        [Tooltip("出生點橫向總寬(u;SpawnPositionWidth)")]
        public float PositionWidth;

        [Tooltip("仰角(度;SpawnRotateXDegreeBase,正 = 往上)")]
        public float PitchDeg;

        [Tooltip("同一單位第 i 顆仰角 += i × 此值(AfterOffsetSpawnRotateXDegree)")]
        public float AfterOffsetPitchDeg;

        [Tooltip("出生高度相對甩墨基準點(u;SpawnPositionOffsetHeight)")]
        public float HeightOffset;

        [Tooltip("同一單位第 i 顆高度 += i × 此值(SpawnPositionHeight)")]
        public float HeightStep;

        [Tooltip("直線飛行幀數(GoStraightToBrakeStateFrame)")]
        public int StraightFrames;

        [Tooltip("對人判定半徑:初始 / 終值 / 成長幀數(InitRadiusForPlayer / EndRadiusForPlayer / ChangeFrameForPlayer)")]
        public float PlayerRadiusInit;

        public float PlayerRadiusEnd;

        public int PlayerRadiusFrames;

        [Tooltip("對地形判定半徑:初始 / 終值 / 成長幀數(…ForField)")]
        public float FieldRadiusInit;

        public float FieldRadiusEnd;

        public int FieldRadiusFrames;

        [Tooltip("落點塗墨半寬:近 / 遠(u;PaintParam.WidthHalfNear / Far)")]
        public float PaintWidthHalfNear;

        public float PaintWidthHalfFar;

        [Tooltip("落點沿飛行方向拉長:直線中落地 / 斜落(DepthScaleMaxStraight / DepthScaleMinBreakFree)")]
        public float PaintDepthScaleMax;

        public float PaintDepthScaleMin;

        [Tooltip("打牆塗墨半徑(u;WallDropCollisionPaintParam.PaintRadiusShock)")]
        public float WallPaintRadius;

        [Tooltip("傷害倍率(UnitDamageRate;Dynamo 橫甩中央小墨團 0 = 只塗不傷;省略 = 1)")]
        public float DamageRate;

        [Tooltip("墨不足弱甩:初速倍率(DepletionSpeedRate;橫 0.5 / 直 0.7)")]
        public float DepletionSpeedRate;

        [Tooltip("墨不足弱甩:判定半徑倍率(CollisionParam.DepletionRate 0.5)")]
        public float DepletionRadiusRate;

        [Tooltip("墨不足弱甩:塗墨寬度倍率(PaintParam.DepletionDepthWidthRate 0.5)")]
        public float DepletionPaintRate;

        [Tooltip("墨團外觀半徑(u;DrawSizeParam.EndRadius)")]
        public float DrawRadius;

        public int CountFor(bool depleted)
        {
            return depleted ? DepletionBulletNum : BulletNum;
        }
    }

    // 一種甩法(橫甩 WeaponWideSwingParam / 直甩 WeaponVerticalSwingParam)的時序、耗墨與墨團結構
    [Serializable]
    public struct RollerSwingParams
    {
        [Tooltip("舉起到甩出的幀數(SwingFrame;Splat 橫 21 / 直 26)")]
        public int SwingFrame;

        [Tooltip("每甩耗墨(標準墨槽的分數;InkConsume,Splat 0.085)")]
        public float InkConsume;

        [Tooltip("墨不足弱甩的耗墨倍率(InkConsumeRateDepletion 0.5)")]
        public float DepletionInkRate;

        [Tooltip("墨量鎖(幀;InkRecoverStop,Splat 橫 43 / 直 58)")]
        public int InkRecoverStop;

        [Tooltip("甩動中的走速(u/F;SwingMoveSpeed,Splat 0.048)")]
        public float MoveSpeed;

        public RollerFlingDamage Damage;

        public RollerFlingUnit[] Units;

        [Tooltip("腳前濺墨半寬 / 拉長(u;SplashNearestParam.PaintWidthHalf / PaintDepthScale;腳前 0.5 u)")]
        public float FeetSplashWidthHalf;

        public float FeetSplashDepthScale;

        [Tooltip("直甩沿線墨滴:顆數 / 第一顆距離 / 間距(SpawnSplashNum / FirstLength / BetweenLength;橫甩 0)")]
        public int LineSplashNum;

        public float LineSplashFirst;

        public float LineSplashBetween;

        [Tooltip("直甩沿線墨滴半寬 / 拉長(SplashPaintParam.WidthHalf / DepthScale 3.0)")]
        public float LineSplashWidthHalf;

        public float LineSplashDepthScale;

        [Tooltip("墨團落點半寬從「近」到「遠」的距離(u;PaintParam.DistanceFar:Splat 橫 12 / 直 16)")]
        public float PaintDistanceFar;

        [Tooltip("墨團落點半寬隨飛行幀數縮小:終值倍率 / 開始幀 / 結束幀(ChangeFrameWidthRate 0.6 / ChangeWidthStartFrame / EndFrame)")]
        public float PaintShrinkRate;

        public int PaintShrinkStartFrame;

        public int PaintShrinkEndFrame;

        public float SwingSeconds => SwingFrame / 60f;

        public float InkLockSeconds => InkRecoverStop / 60f;

        public float MoveSpeedPerSecond => MoveSpeed * 60f;

        // 甩一下的循環(幀):從按下到可以再甩下一下。[EST] = max(SwingFrame + 6, InkRecoverStop)——
        // S3 沒有公開甩動後搖;S2 資料表的連甩間隔(Splat 45 / Carbon 40 / 直甩 60)與 InkRecoverStop 只差 0–2F
        public int CycleFrames => Mathf.Max(SwingFrame + 6, InkRecoverStop);

        public int BulletCount(bool depleted)
        {
            int count = 0;
            if (Units == null)
            {
                return 0;
            }
            for (int i = 0; i < Units.Length; i++)
            {
                count += Units[i].CountFor(depleted);
            }
            return count;
        }

        // 中央曲線的一擊必殺(≥ 100)飛行距離
        public float LethalDistance => Damage.Inside.LethalDistance();
    }

    // 推滾(WeaponRollParam + BodyParam)
    [Serializable]
    public struct RollerRollParams
    {
        [Tooltip("推滾速度(u/F;SpeedNormal)")]
        public float SpeedNormal;

        [Tooltip("連續推滾 DashFrame 幀後的衝刺速度(u/F;SpeedDash)")]
        public float SpeedDash;

        [Tooltip("衝刺中急轉的速度(u/F;SpeedDashTurnBreak = SpeedNormal)")]
        public float SpeedDashTurnBreak;

        [Tooltip("進入衝刺需連續推滾的幀數(DashFrame;Splat 90 / Carbon 20)")]
        public int DashFrame;

        [Tooltip("耗墨(每幀,標準墨槽分數)隨速度:SpeedInkConsumeMin → Max 之間由 InkConsumeMin → Max 線性")]
        public float InkConsumeMinPerFrame;

        public float InkConsumeMaxPerFrame;

        public float SpeedInkConsumeMin;

        public float SpeedInkConsumeMax;

        [Tooltip("推滾的墨量鎖(幀;InkRecoverStop 20)")]
        public int InkRecoverStop;

        [Tooltip("接觸傷害(HP;BodyParam.Damage ÷ 10:Splat / Dynamo 125、Carbon 70)")]
        public float Damage;

        [Tooltip("接觸判定:滾筒半寬 / 半徑(u;BodyParam.CollisionParam.WidthHalf 1.4 / Radius 0.4)")]
        public float BodyWidthHalf;

        public float BodyRadius;

        [Tooltip("塗墨半寬上限與達到上限的速度(BodyParam.PaintParam.WidthHalfMax / SpeedMax;Splat 2.8 @ 0.132)")]
        public float PaintWidthHalfMax;

        public float PaintSpeedMax;

        [Tooltip("擊退:對手沒被擊倒時被推(KnockBackOpponent.AccelMin → Max,隨推滾速度)")]
        public float KnockbackOpponentMin;

        public float KnockbackOpponentMax;

        [Tooltip("擊退:打到對手(有傷害、沒擊倒)時自己被彈回(KnockBackRollerPlayerDamageOn.AccelMin → Max)")]
        public float KnockbackSelfMin;

        public float KnockbackSelfMax;

        [Tooltip("擊退:打不動對手(0 傷害)時自己被彈回(KnockBackRollerPlayerDamageOff.Accel)")]
        public float KnockbackSelfNoDamage;

        public float InkLockSeconds => InkRecoverStop / 60f;

        // 速度 speed(u/F)時每幀耗墨。[EST] 幾乎靜止(< 0.005 u/F)不耗墨也不塗墨(S3 沒動的滾筒不出墨)
        public float InkPerFrame(float speed)
        {
            if (speed < StillSpeed)
            {
                return 0f;
            }
            float t = SpeedInkConsumeMax > SpeedInkConsumeMin
                ? Mathf.Clamp01((speed - SpeedInkConsumeMin) / (SpeedInkConsumeMax - SpeedInkConsumeMin))
                : 1f;
            return Mathf.Lerp(InkConsumeMinPerFrame, InkConsumeMaxPerFrame, t);
        }

        // 速度 speed(u/F)時的塗墨半寬(u)。S3:滾筒本體塗接觸面、兩側濺墨「隨速度變大」,SpeedMax 時總寬 2 × WidthHalfMax;
        // [EST] 半寬 = WidthHalfMax × lerp(0.5, 1, speed / SpeedMax)(S1 Splat Roller 7.2 u/s 塗 12.48 p/s 反推 5.6 u 寬)
        public float PaintHalfWidth(float speed)
        {
            float t = PaintSpeedMax > 1e-5f ? Mathf.Clamp01(speed / PaintSpeedMax) : 1f;
            return PaintWidthHalfMax * Mathf.Lerp(MinPaintWidthRatio, 1f, t);
        }

        // 推滾速度(u/F):衝刺中且沒在急轉 = SpeedDash;急轉 = SpeedDashTurnBreak;否則 SpeedNormal
        public float SpeedFor(bool dashing, bool turning)
        {
            if (!dashing)
            {
                return SpeedNormal;
            }
            return turning ? SpeedDashTurnBreak : SpeedDash;
        }

        // 擊退速度(m/s):accel 值 × KnockbackModel.SpeedPerAccel(S3 只公開參數值,換算沿用爆炸擊退的 [EST] 比例)
        public float OpponentKnockbackSpeed(float speedNormalized)
        {
            return Mathf.Lerp(KnockbackOpponentMin, KnockbackOpponentMax, Mathf.Clamp01(speedNormalized))
                   * KnockbackModel.SpeedPerAccel;
        }

        public float SelfKnockbackSpeed(float speedNormalized, bool damaged)
        {
            float accel = damaged
                ? Mathf.Lerp(KnockbackSelfMin, KnockbackSelfMax, Mathf.Clamp01(speedNormalized))
                : KnockbackSelfNoDamage;
            return accel * KnockbackModel.SpeedPerAccel;
        }

        // 靜止門檻(u/F)[EST]
        public const float StillSpeed = 0.005f;

        // 最慢時的塗墨寬度比例 [EST]
        public const float MinPaintWidthRatio = 0.5f;
    }

    // Splatoon 3 滾筒的原始參數(Leanny v11.3.0 Roller_Normal / Roller_Compact / Roller_Heavy)。
    // 省略欄位的缺省值:BulletNum 1、DepletionBulletNum = BulletNum、UnitDamageRate 1、InkConsumeRateDepletion 0.5、
    // 直甩 DepletionDamageRate 0.5(Carbon 有列;Splat / Dynamo [EST] 同)。
    // 另有幾個 S3 沒公開、本專案估計的量(標 [EST]):甩墨基準點高度/前距、滾筒前距、推滾起步延遲、接觸重擊間隔。
    public struct S3RollerParams
    {
        public string Name;
        public bool LightWeight;
        public bool HeavyWeight;

        public RollerRollParams Roll;
        public RollerSwingParams Horizontal;
        public RollerSwingParams Vertical;

        // ── [EST] 幾何與時序(S3 沒有公開;依角色 1.65 u 身高與動作估計)──

        // 橫甩墨團的出生基準點:腳底往上 / 往前(滾筒從頭頂砸到身前)
        public float HorizontalSpawnHeight;
        public float HorizontalSpawnForward;

        // 直甩基準點(側揮在肩高;最低一顆在 −1.25)
        public float VerticalSpawnHeight;
        public float VerticalSpawnForward;

        // 推滾時滾筒軸心在腳前的距離(接觸判定與塗墨的中心)
        public float RollForward;

        // 甩出後仍按住:幾幀後滾筒落地開始推滾
        public int RollStartDelayFrames;

        // 同一目標被推滾接觸再次吃傷害的最短間隔(幀)
        public int ContactRehitFrames;

        public float HorizontalLethalRange => Horizontal.LethalDistance;
        public float VerticalLethalRange => Vertical.LethalDistance;

        public float RollSpeedNormalPerSecond => Roll.SpeedNormal * 60f;
        public float RollSpeedDashPerSecond => Roll.SpeedDash * 60f;
        public float DashSeconds => Roll.DashFrame / 60f;

        // 滿槽可以正常甩幾下(之後是弱甩)
        public int FlicksPerTank => Horizontal.InkConsume > 0f ? Mathf.FloorToInt(1f / Horizontal.InkConsume + 1e-4f) : 0;

        // 滿槽以衝刺速度推滾的秒數(sendou mainWeaponRollSeconds = 墨槽 / InkConsumeMaxPerFrame)
        public float FullTankRollSeconds => Roll.InkConsumeMaxPerFrame > 0f ? 1f / Roll.InkConsumeMaxPerFrame / 60f : 0f;

        // ── 共用的 [EST] 幾何 ──
        // 橫甩:滾筒從頭頂往前砸,墨在約胸口高度離開滾筒。對地形判定 0.1 → 0.6 u(2F),墨團中心離地 0.6 就算落地:
        // 1.3 u 起飛 → Splat 1.05 u/F 直飛 7F 7.35 u + 減速約 6F → 落在 ≈ 12 u(= PaintParam.DistanceFar 12 的設計落點)
        private const float EstHorizontalHeight = 1.3f;
        private const float EstHorizontalForward = 0.6f;
        private const float EstVerticalHeight = 1.5f;
        private const float EstVerticalForward = 0.5f;
        private const int EstRollStartDelay = 6;
        private const int EstContactRehit = 20;

        // S3 各滾筒共用的擊退參數(KnockBackOpponent 420–800、DamageOn 410–550、DamageOff 280)
        private static RollerRollParams CommonBody(RollerRollParams roll)
        {
            roll.BodyWidthHalf = 1.4f;
            roll.BodyRadius = 0.4f;
            roll.KnockbackOpponentMin = 420f;
            roll.KnockbackOpponentMax = 800f;
            roll.KnockbackSelfMin = 410f;
            roll.KnockbackSelfMax = 550f;
            roll.KnockbackSelfNoDamage = 280f;
            roll.InkConsumeMinPerFrame = 0.0002f;
            roll.InkConsumeMaxPerFrame = 0.001f;
            roll.SpeedInkConsumeMin = 0.02f;
            roll.InkRecoverStop = 20;
            return roll;
        }

        // 墨團共用欄位(弱甩倍率;塗墨/判定由各單位覆寫)
        private static RollerFlingUnit Unit(int count, int depletionCount, float speed, float speedRandom,
            float wideDegree, float width, int straightFrames, float depletionSpeedRate)
        {
            return new RollerFlingUnit
            {
                BulletNum = count,
                DepletionBulletNum = depletionCount,
                SpawnSpeed = speed,
                SpawnSpeedRandom = speedRandom,
                WideDegree = wideDegree,
                PositionWidth = width,
                StraightFrames = straightFrames,
                DamageRate = 1f,
                DepletionSpeedRate = depletionSpeedRate,
                DepletionRadiusRate = 0.5f,
                DepletionPaintRate = 0.5f,
            };
        }

        private static RollerFlingUnit Radii(RollerFlingUnit u, float playerInit, float playerEnd, int playerFrames,
            float fieldInit, float fieldEnd, int fieldFrames)
        {
            u.PlayerRadiusInit = playerInit;
            u.PlayerRadiusEnd = playerEnd;
            u.PlayerRadiusFrames = playerFrames;
            u.FieldRadiusInit = fieldInit;
            u.FieldRadiusEnd = fieldEnd;
            u.FieldRadiusFrames = fieldFrames;
            return u;
        }

        private static RollerFlingUnit Paint(RollerFlingUnit u, float near, float far, float depthMax, float depthMin,
            float wall, float drawRadius)
        {
            u.PaintWidthHalfNear = near;
            u.PaintWidthHalfFar = far;
            u.PaintDepthScaleMax = depthMax;
            u.PaintDepthScaleMin = depthMin;
            u.WallPaintRadius = wall;
            u.DrawRadius = drawRadius;
            return u;
        }

        // 直甩一列:仰角/逐顆偏移與高度
        private static RollerFlingUnit Column(RollerFlingUnit u, float pitch, float afterPitch, float afterSpeed,
            float height, float heightStep)
        {
            u.PitchDeg = pitch;
            u.AfterOffsetPitchDeg = afterPitch;
            u.AfterOffsetSpeed = afterSpeed;
            u.HeightOffset = height;
            u.HeightStep = heightStep;
            return u;
        }

        private static void ApplyCommonGeometry(ref S3RollerParams p)
        {
            p.HorizontalSpawnHeight = EstHorizontalHeight;
            p.HorizontalSpawnForward = EstHorizontalForward;
            p.VerticalSpawnHeight = EstVerticalHeight;
            p.VerticalSpawnForward = EstVerticalForward;
            p.RollStartDelayFrames = EstRollStartDelay;
            p.ContactRehitFrames = EstContactRehit;
        }

        // Roller_Normal(Splat Roller;中量級)
        public static S3RollerParams SplatRoller
        {
            get
            {
                var p = new S3RollerParams { Name = "Splat Roller", RollForward = 1.08f };
                ApplyCommonGeometry(ref p);
                p.Roll = CommonBody(new RollerRollParams
                {
                    SpeedNormal = 0.108f, SpeedDash = 0.132f, SpeedDashTurnBreak = 0.108f, DashFrame = 90,
                    SpeedInkConsumeMax = 0.132f, Damage = 125f, PaintWidthHalfMax = 2.8f, PaintSpeedMax = 0.132f,
                });
                p.Horizontal = new RollerSwingParams
                {
                    SwingFrame = 21, InkConsume = 0.085f, DepletionInkRate = 0.5f, InkRecoverStop = 43, MoveSpeed = 0.048f,
                    PaintDistanceFar = 12f, PaintShrinkRate = 0.6f, PaintShrinkStartFrame = 20, PaintShrinkEndFrame = 50,
                    Damage = new RollerFlingDamage
                    {
                        Inside = new RollerDamageCurve(150f, 4.2f, 100f, 7.2f, 100f, 7.2f, 35f, 10.7f, 16f),
                        Outside = new RollerDamageCurve(100f, 2.0f, 50f, 7.2f, 50f, 7.2f, 35f, 9.2f, 90f),
                        RejectStartFrame = 25, RejectEndFrame = 45, RejectRate = 0.5f, DepletionDamageRate = 0.25f,
                    },
                    Units = new[]
                    {
                        Paint(Radii(Unit(12, 3, 1.05f, 0.36f, 18f, 0.8f, 7, 0.5f), 0.12f, 1.02f, 4, 0.1f, 0.6f, 2),
                            1.89f, 1.6275f, 2.4f, 1.2f, 1.3f, 0.3f),
                        Paint(Radii(Unit(1, 1, 0.48f, 0.11f, 4f, 0.4f, 7, 0.5f), 0.12f, 1.02f, 4, 0.1f, 0.6f, 2),
                            1.89f, 1.6275f, 2.4f, 1.2f, 1.3f, 0.3f),
                    },
                    FeetSplashWidthHalf = 1.5f, FeetSplashDepthScale = 1.5f,
                };
                p.Vertical = new RollerSwingParams
                {
                    SwingFrame = 26, InkConsume = 0.085f, DepletionInkRate = 0.5f, InkRecoverStop = 58, MoveSpeed = 0.048f,
                    PaintDistanceFar = 16f, PaintShrinkRate = 0.6f, PaintShrinkStartFrame = 30, PaintShrinkEndFrame = 50,
                    Damage = new RollerFlingDamage
                    {
                        Inside = new RollerDamageCurve(150f, 5.2f, 100f, 8.2f, 50f, 11.2f, 40f, 13.2f, 5f),
                        Outside = new RollerDamageCurve(150f, 5.2f, 100f, 8.2f, 50f, 11.2f, 40f, 13.2f, 20f),
                        RejectStartFrame = 30, RejectEndFrame = 45, RejectRate = 0.5f, DepletionDamageRate = 0.5f,
                    },
                    Units = new[]
                    {
                        Paint(Radii(Column(Unit(1, 1, 1.8338f, 0f, 0f, 0f, 6, 0.7f), 7.5f, 0f, 0f, 0.5f, 0f),
                            0.116f, 0.87f, 4, 0.1f, 0.75f, 3), 2.454f, 3.068f, 1.76f, 1.32f, 1.4f, 0.36f),
                        Paint(Radii(Column(Unit(2, 2, 1.6338f, 0f, 0f, 0f, 6, 0.7f), 5f, -2.5f, -0.2f, -0.25f, 0.25f),
                            0.116f, 0.87f, 4, 0.1f, 0.75f, 3), 1.841f, 2.045f, 1.76f, 1.32f, 1.4f, 0.36f),
                        Paint(Radii(Column(Unit(2, 0, 1.2338f, 0f, 0f, 0f, 6, 0.7f), 0f, -2.5f, -0.2f, -1.25f, 0.25f),
                            0.116f, 0.82f, 4, 0.1f, 0.55f, 2), 1.841f, 2.045f, 1.76f, 1.32f, 1.4f, 0.36f),
                    },
                    FeetSplashWidthHalf = 1.462f, FeetSplashDepthScale = 1.462f,
                    LineSplashNum = 5, LineSplashFirst = 1.2f, LineSplashBetween = 4.6f, LineSplashWidthHalf = 1.287f,
                    LineSplashDepthScale = 3f,
                };
                return p;
            }
        }

        // Roller_Compact(Carbon Roller;輕量級)
        public static S3RollerParams CarbonRoller
        {
            get
            {
                var p = new S3RollerParams { Name = "Carbon Roller", LightWeight = true, RollForward = 0.98f };
                ApplyCommonGeometry(ref p);
                p.Roll = CommonBody(new RollerRollParams
                {
                    SpeedNormal = 0.128f, SpeedDash = 0.16f, SpeedDashTurnBreak = 0.128f, DashFrame = 20,
                    SpeedInkConsumeMax = 0.16f, Damage = 70f, PaintWidthHalfMax = 2.2f, PaintSpeedMax = 0.16f,
                });
                p.Horizontal = new RollerSwingParams
                {
                    SwingFrame = 10, InkConsume = 0.0396f, DepletionInkRate = 0.5f, InkRecoverStop = 40, MoveSpeed = 0.06f,
                    PaintDistanceFar = 8.5f, PaintShrinkRate = 0.6f, PaintShrinkStartFrame = 20, PaintShrinkEndFrame = 45,
                    Damage = new RollerFlingDamage
                    {
                        Inside = new RollerDamageCurve(100f, 4.7f, 50f, 6.7f, 50f, 6.7f, 25f, 8.7f, 16f),
                        Outside = new RollerDamageCurve(100f, 3.2f, 50f, 5.7f, 50f, 5.7f, 25f, 7.2f, 25f),
                        RejectStartFrame = 24, RejectEndFrame = 44, RejectRate = 0.5f, DepletionDamageRate = 0.25f,
                    },
                    Units = new[]
                    {
                        Paint(Radii(Unit(10, 3, 0.8f, 0.26f, 18f, 0.6f, 6, 0.5f), 0.12f, 0.95f, 3, 0.1f, 0.5f, 1),
                            1.56f, 1.25f, 2.4f, 1.2f, 1.0f, 0.3f),
                        Paint(Radii(Unit(2, 2, 0.4f, 0.1f, 8f, 0.3f, 6, 0.5f), 0.12f, 0.95f, 3, 0.1f, 0.5f, 1),
                            1.56f, 1.25f, 2.4f, 1.2f, 1.0f, 0.3f),
                        Paint(Radii(Unit(1, 1, 0.24f, 0.05f, 8f, 0.3f, 6, 0.5f), 0.12f, 0.95f, 3, 0.1f, 0.5f, 1),
                            1.4f, 1.1f, 2.4f, 1.2f, 1.0f, 0.3f),
                    },
                    FeetSplashWidthHalf = 1.5f, FeetSplashDepthScale = 1.5f,
                };
                p.Vertical = new RollerSwingParams
                {
                    SwingFrame = 12, InkConsume = 0.0396f, DepletionInkRate = 0.5f, InkRecoverStop = 45, MoveSpeed = 0.048f,
                    PaintDistanceFar = 12f, PaintShrinkRate = 0.6f, PaintShrinkStartFrame = 30, PaintShrinkEndFrame = 50,
                    Damage = new RollerFlingDamage
                    {
                        Inside = new RollerDamageCurve(120f, 4.2f, 50f, 12.43f, 50f, 12.43f, 35f, 14.2f, 5f),
                        Outside = new RollerDamageCurve(120f, 4.2f, 50f, 12.43f, 50f, 12.43f, 35f, 14.2f, 20f),
                        RejectStartFrame = 30, RejectEndFrame = 45, RejectRate = 0.5f, DepletionDamageRate = 0.5f,
                    },
                    Units = new[]
                    {
                        Paint(Radii(Column(Unit(1, 1, 1.5166f, 0f, 0f, 0f, 6, 0.7f), 7.5f, 0f, 0f, 0.5f, 0f),
                            0.124f, 0.87f, 4, 0.1f, 0.65f, 3), 2.2f, 2.6f, 1.6f, 1.2f, 1.2f, 0.3f),
                        Paint(Radii(Column(Unit(2, 2, 1.3366f, 0f, 0f, 0f, 6, 0.7f), 5f, -2.5f, -0.18f, -0.25f, 0.25f),
                            0.124f, 0.87f, 4, 0.1f, 0.65f, 3), 1.45f, 1.75f, 1.6f, 1.2f, 1.2f, 0.3f),
                        Paint(Radii(Column(Unit(2, 0, 0.9766f, 0f, 0f, 0f, 6, 0.7f), 0f, -2.5f, -0.18f, -1.25f, 0.25f),
                            0.124f, 0.82f, 4, 0.1f, 0.5f, 2), 1.45f, 1.75f, 1.6f, 1.2f, 1.2f, 0.3f),
                    },
                    FeetSplashWidthHalf = 1.5f, FeetSplashDepthScale = 1.5f,
                    LineSplashNum = 5, LineSplashFirst = 1.1f, LineSplashBetween = 4.2f, LineSplashWidthHalf = 1.25f,
                    LineSplashDepthScale = 3f,
                };
                return p;
            }
        }

        // Roller_Heavy(Dynamo Roller;重量級)
        public static S3RollerParams DynamoRoller
        {
            get
            {
                var p = new S3RollerParams { Name = "Dynamo Roller", HeavyWeight = true, RollForward = 1.2f };
                ApplyCommonGeometry(ref p);
                p.Roll = CommonBody(new RollerRollParams
                {
                    SpeedNormal = 0.088f, SpeedDash = 0.108f, SpeedDashTurnBreak = 0.088f, DashFrame = 90,
                    SpeedInkConsumeMax = 0.108f, Damage = 125f, PaintWidthHalfMax = 3.4f, PaintSpeedMax = 0.108f,
                });
                RollerFlingUnit center = Paint(Radii(Unit(2, 2, 0.5f, 0.1f, 4f, 0.4f, 8, 0.5f), 0.1058f, 0.67f, 3,
                    0.1f, 0.6f, 2), 2.07f, 1.725f, 2.4f, 1.2f, 1.2f, 0.55f);
                center.DamageRate = 0f;
                p.Horizontal = new RollerSwingParams
                {
                    SwingFrame = 45, InkConsume = 0.21f, DepletionInkRate = 0.5f, InkRecoverStop = 55, MoveSpeed = 0.024f,
                    PaintDistanceFar = 20f, PaintShrinkRate = 0.7f, PaintShrinkStartFrame = 25, PaintShrinkEndFrame = 60,
                    Damage = new RollerFlingDamage
                    {
                        Inside = new RollerDamageCurve(180f, 7.2f, 100f, 12.9f, 50f, 16.49f, 40f, 17.4f, 14.5f),
                        Outside = new RollerDamageCurve(120f, 0.2f, 100f, 3.38f, 50f, 9.91f, 40f, 12.2f, 20f),
                        RejectStartFrame = 30, RejectEndFrame = 55, RejectRate = 0.5f, DepletionDamageRate = 0.25f,
                    },
                    Units = new[]
                    {
                        Paint(Radii(Unit(16, 3, 1.2f, 0.4f, 18f, 0.8f, 9, 0.5f), 0.1058f, 1.27f, 5, 0.1f, 0.8f, 2),
                            2.415f, 2.2425f, 2.4f, 1.2f, 1.3f, 0.55f),
                        center,
                    },
                    FeetSplashWidthHalf = 1.5f, FeetSplashDepthScale = 1.5f,
                };
                p.Vertical = new RollerSwingParams
                {
                    SwingFrame = 55, InkConsume = 0.21f, DepletionInkRate = 0.5f, InkRecoverStop = 70, MoveSpeed = 0.024f,
                    PaintDistanceFar = 30f, PaintShrinkRate = 0.6f, PaintShrinkStartFrame = 40, PaintShrinkEndFrame = 65,
                    Damage = new RollerFlingDamage
                    {
                        Inside = new RollerDamageCurve(180f, 8.2f, 100f, 14.0f, 50f, 19.53f, 40f, 20.6f, 5f),
                        Outside = new RollerDamageCurve(180f, 8.2f, 100f, 14.0f, 50f, 19.53f, 40f, 20.6f, 20f),
                        RejectStartFrame = 40, RejectEndFrame = 65, RejectRate = 0.5f, DepletionDamageRate = 0.5f,
                    },
                    Units = new[]
                    {
                        Paint(Radii(Column(Unit(1, 1, 2.0289f, 0f, 0f, 0f, 9, 0.7f), 7.5f, 0f, 0f, 1.0f, 0f),
                            0.105f, 1.1f, 6, 0.1f, 1.05f, 4), 4.37f, 4.6f, 1.6f, 1.2f, 1.6f, 0.52f),
                        Paint(Radii(Column(Unit(2, 2, 1.8289f, 0f, 0f, 0f, 9, 0.7f), 5f, -2.5f, -0.2f, 0.25f, 0.25f),
                            0.105f, 1.1f, 6, 0.1f, 1.05f, 4), 2.3f, 2.645f, 2.4f, 1.2f, 1.6f, 0.52f),
                        Paint(Radii(Column(Unit(2, 0, 1.4289f, 0f, 0f, 0f, 9, 0.7f), 0f, -2.5f, -0.2f, -0.75f, 0.25f),
                            0.105f, 0.95f, 5, 0.1f, 0.7f, 3), 2.3f, 2.645f, 2.4f, 1.2f, 1.6f, 0.52f),
                    },
                    FeetSplashWidthHalf = 1.5f, FeetSplashDepthScale = 1.5f,
                    LineSplashNum = 6, LineSplashFirst = 1.5f, LineSplashBetween = 5.2f, LineSplashWidthHalf = 1.8f,
                    LineSplashDepthScale = 3f,
                };
                return p;
            }
        }
    }
}

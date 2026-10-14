using NUnit.Framework;
using SplatoonC.Core.Combat;

namespace SplatoonC.Tests
{
    public class S3ShooterParamsTests
    {
        private static readonly S3ShooterParams Shot = S3ShooterParams.Splattershot;
        private static readonly S3ShooterParams Jr = S3ShooterParams.SplattershotJr;
        private static readonly S3ShooterParams Gal = S3ShooterParams.Gal52;
        private static readonly S3ShooterParams Zap = S3ShooterParams.NZap85;
        private static readonly S3ShooterParams Pro = S3ShooterParams.SplattershotPro;

        [Test]
        public void 有效射程公式重現Inkipedia數值()
        {
            // research/02 §4.2:11.56 / 10.19 / 12.35 / 11.23 / 15.09
            Assert.AreEqual(11.56f, Shot.EffectiveRange, 0.01f);
            Assert.AreEqual(10.19f, Jr.EffectiveRange, 0.01f);
            Assert.AreEqual(12.35f, Gal.EffectiveRange, 0.01f);
            Assert.AreEqual(11.23f, Zap.EffectiveRange, 0.01f);
            Assert.AreEqual(15.09f, Pro.EffectiveRange, 0.01f);
        }

        [Test]
        public void 擊倒發數與首發後TTK()
        {
            Assert.AreEqual(3, Shot.ShotsToSplat());
            Assert.AreEqual(12, Shot.TtkFrames());
            Assert.AreEqual(4, Jr.ShotsToSplat());
            Assert.AreEqual(15, Jr.TtkFrames());
            Assert.AreEqual(2, Gal.ShotsToSplat());
            Assert.AreEqual(9, Gal.TtkFrames());
            Assert.AreEqual(4, Zap.ShotsToSplat());
            Assert.AreEqual(15, Zap.TtkFrames());
            Assert.AreEqual(3, Pro.ShotsToSplat());
            Assert.AreEqual(16, Pro.TtkFrames());
        }

        [Test]
        public void 滿槽發數含Jr的110墨槽()
        {
            Assert.AreEqual(108, Shot.ShotsPerTank);
            Assert.AreEqual(255, Jr.ShotsPerTank, "110% ÷ 0.43%");
            Assert.AreEqual(66, Gal.ShotsPerTank);
            Assert.AreEqual(125, Zap.ShotsPerTank);
            Assert.AreEqual(50, Pro.ShotsPerTank);
            // 連射秒數:Jr. 21.25 s、.52 9.9 s、N-ZAP 10.42 s、Pro 6.67 s
            Assert.AreEqual(21.25f, Jr.FullTankFireSeconds, 0.01f);
            Assert.AreEqual(9.9f, Gal.FullTankFireSeconds, 0.01f);
            Assert.AreEqual(10.42f, Zap.FullTankFireSeconds, 0.01f);
            Assert.AreEqual(6.67f, Pro.FullTankFireSeconds, 0.01f);
        }

        [Test]
        public void 射速走速墨量鎖換算()
        {
            Assert.AreEqual(5f / 60f, Jr.FireIntervalSeconds, 1e-6f);
            Assert.AreEqual(0.15f, Gal.FireIntervalSeconds, 1e-6f);
            Assert.AreEqual(8f / 60f, Pro.FireIntervalSeconds, 1e-6f);
            Assert.AreEqual(4.56f, Jr.MoveSpeedWhileFiring, 1e-4f);
            Assert.AreEqual(3.6f, Gal.MoveSpeedWhileFiring, 1e-4f);
            Assert.AreEqual(5.28f, Zap.MoveSpeedWhileFiring, 1e-4f);
            Assert.AreEqual(3.3f, Pro.MoveSpeedWhileFiring, 1e-4f);
            Assert.AreEqual(0.25f, Jr.InkLockSeconds, 1e-6f, "Jr. InkRecoverStop 15F");
            Assert.AreEqual(20f / 60f, Pro.InkLockSeconds, 1e-6f);
        }

        [Test]
        public void 散布參數轉成每秒量()
        {
            SpreadSettings jr = Jr.ToSpreadSettings();
            Assert.AreEqual(11.66f, jr.StandSwerveDeg, 1e-5f);
            Assert.AreEqual(14.58f, jr.JumpSwerveDeg, 1e-5f);
            Assert.AreEqual(0.4f, jr.BiasMax, 1e-6f);
            Assert.AreEqual(0.6f, jr.BiasDecreasePerSecond, 1e-5f);
            Assert.AreEqual(5f / 60f, jr.RepeatSeconds, 1e-6f);
            // Splattershot 的換算要和既有預設完全一致
            SpreadSettings shot = Shot.ToSpreadSettings();
            SpreadSettings legacy = SpreadSettings.Splattershot;
            Assert.AreEqual(legacy.StandSwerveDeg, shot.StandSwerveDeg, 1e-6f);
            Assert.AreEqual(legacy.BiasDecreasePerSecond, shot.BiasDecreasePerSecond, 1e-5f);
            Assert.AreEqual(legacy.RepeatSeconds, shot.RepeatSeconds, 1e-6f);
            Assert.AreEqual(legacy.JumpShrinkEndSeconds, shot.JumpShrinkEndSeconds, 1e-6f);
        }

        [Test]
        public void 預估單發塗地的相對大小()
        {
            float shot = Shot.EstimatedPaintAreaPerShot;
            // .52 Gal 單發塗最多;Pro 沒有腳前墨滴但落點大;N-ZAP 最少
            Assert.Greater(Gal.EstimatedPaintAreaPerShot, shot * 1.3f);
            Assert.Greater(Jr.EstimatedPaintAreaPerShot, shot);
            Assert.Greater(Pro.EstimatedPaintAreaPerShot, shot);
            Assert.Less(Zap.EstimatedPaintAreaPerShot, shot);
            // 一槽塗地:Jr. 約 Splattershot 的 2.7 倍、Pro 約一半
            float perTankShot = shot * Shot.ShotsPerTank;
            Assert.Greater(Jr.EstimatedPaintAreaPerShot * Jr.ShotsPerTank, perTankShot * 2.3f);
            Assert.Less(Pro.EstimatedPaintAreaPerShot * Pro.ShotsPerTank, perTankShot * 0.65f);
        }

        [Test]
        public void 校準點_Splattershot換算回既有手感()
        {
            // Assets/Data/WeaponConfig 的既有值(M3 試玩調出來的)
            Assert.AreEqual(65.8125f, ShooterCalibration.MuzzleSpeed(Shot), 1e-3f);
            Assert.AreEqual(10f, ShooterCalibration.StraightRange(Shot), 0.1f);
            Assert.AreEqual(-60f, ShooterCalibration.DropGravity(Shot), 1e-3f);
            Assert.AreEqual(9.45f, ShooterCalibration.DropHorizontalDrag(Shot), 1e-3f);
            Assert.AreEqual(3f, ShooterCalibration.DamageDecayDistance(Shot), 1e-4f);
            Assert.AreEqual(1.82f, ShooterCalibration.SplatRadius(Shot), 1e-4f);
            Assert.AreEqual(0.84f, ShooterCalibration.SplashRadius(Shot), 1e-4f);
            Assert.AreEqual(3, ShooterCalibration.DripsPerShot(Shot));
            Assert.AreEqual(1.2f, ShooterCalibration.DripRadius(Shot), 1e-4f);
            Assert.AreEqual(1.18f, ShooterCalibration.MuzzleSplashRadius(Shot), 1e-4f);
            Assert.AreEqual(1.5f, ShooterCalibration.DripRangeMultiplier(Shot), 0.02f);
        }

        [Test]
        public void 變體換算_射程與幾何相似()
        {
            // 直飛 + 1.5 = S3 有效射程(WeaponConfig.EffectiveRange 的定義)
            Assert.AreEqual(10.19f, ShooterCalibration.StraightRange(Jr) + ShooterCalibration.GlideAllowance, 0.01f);
            Assert.AreEqual(15.09f, ShooterCalibration.StraightRange(Pro) + ShooterCalibration.GlideAllowance, 0.01f);
            // 全傷距離 = 有效射程 − 對人判定半徑
            Assert.AreEqual(15.09f - 0.235f, ShooterCalibration.DamageFalloffStart(Pro), 0.01f);
            // 幾何相似:重力 / 初速² 與 阻力 / 初速 不變 → 彈道形狀相同
            float refShape = ShooterCalibration.DropGravity(Shot) / (ShooterCalibration.MuzzleSpeed(Shot) * ShooterCalibration.MuzzleSpeed(Shot));
            float proShape = ShooterCalibration.DropGravity(Pro) / (ShooterCalibration.MuzzleSpeed(Pro) * ShooterCalibration.MuzzleSpeed(Pro));
            Assert.AreEqual(refShape, proShape, 1e-6f);
            Assert.AreEqual(ShooterCalibration.DropHorizontalDrag(Shot) / ShooterCalibration.MuzzleSpeed(Shot),
                ShooterCalibration.DropHorizontalDrag(Gal) / ShooterCalibration.MuzzleSpeed(Gal), 1e-6f);
            // .52 Gal 初速 3.06 u/F → 本專案 88.9 m/s;Pro 3.833 → 111.3
            Assert.AreEqual(88.87f, ShooterCalibration.MuzzleSpeed(Gal), 0.05f);
            Assert.AreEqual(111.33f, ShooterCalibration.MuzzleSpeed(Pro), 0.05f);
        }

        [Test]
        public void 連射出膛晚一幀不算停火_b照樣爬到S3上限()
        {
            // N-ZAP:每發 +0.005、每秒回復 0.3。出膛時刻落在幀邊界(排程 + 0~1 幀),間隔在 5F ± 1 幀之間跳動
            SpreadSettings s = Zap.ToSpreadSettings();
            var m = new SpreadModel();
            const float frame = 1f / 60f;
            for (int i = 0; i < 60; i++)
            {
                float lateness = (i % 2 == 0) ? 0f : frame * 0.95f;
                m.NextYawOffset(i * Zap.FireIntervalSeconds + lateness, s, false, 0f, 0.5f, 0.9f);
            }
            Assert.AreEqual(Zap.BiasMax, m.CurrentBias, 1e-4f, "48 發後應到上限 0.25");
            // 真的停火(超過 RepeatFrame + 一幀)才回復
            float t = 60 * Zap.FireIntervalSeconds + 1f;
            m.NextYawOffset(t, s, false, 0f, 0.5f, 0.9f);
            Assert.Less(m.CurrentBias, Zap.BiasMax - 0.2f);
        }

        [Test]
        public void 變體換算_塗墨與墨滴()
        {
            Assert.AreEqual(5, ShooterCalibration.DripsPerShot(Gal), ".52 SpawnNum 2.4");
            Assert.AreEqual(4, ShooterCalibration.DripsPerShot(Pro), "Pro SpawnNum 2.0");
            Assert.AreEqual(3, ShooterCalibration.DripsPerShot(Jr));
            Assert.AreEqual(3, ShooterCalibration.DripsPerShot(Zap));
            Assert.AreEqual(0f, ShooterCalibration.MuzzleSplashRadius(Pro), 1e-6f, "Pro 沒有腳前強制墨滴");
            Assert.AreEqual(2.09f, ShooterCalibration.SplatRadius(Gal), 1e-4f);
            Assert.AreEqual(1.5f, ShooterCalibration.DamageDecayDistance(Jr), 1e-4f, "衰減 16F = Splattershot 的一半");
        }

        [Test]
        public void 墨滴相位_一輪每個相位各一次且跨步打散()
        {
            Assert.AreEqual(3, S3ShooterParams.DropletStride(8), "Splattershot SplitNum 8");
            Assert.AreEqual(3, S3ShooterParams.DropletStride(7), "Jr. 7");
            Assert.AreEqual(2, S3ShooterParams.DropletStride(5), ".52 Gal 5");
            Assert.AreEqual(4, S3ShooterParams.DropletStride(11), "N-ZAP 11");
            Assert.AreEqual(1, S3ShooterParams.DropletStride(6), "Pro 6:沒有互質的跨步 → 依序");
            foreach (int split in new[] { 5, 6, 7, 8, 11 })
            {
                var seen = new System.Collections.Generic.HashSet<float>();
                for (int i = 0; i < split; i++)
                {
                    Assert.IsTrue(seen.Add(S3ShooterParams.DropletPhase(i, split, 9.2f, 11.3f)), $"SplitNum {split} 第 {i} 發相位重複");
                }
                Assert.AreEqual(S3ShooterParams.DropletPhase(0, split, 9.2f, 11.3f),
                    S3ShooterParams.DropletPhase(split, split, 9.2f, 11.3f), 1e-6f, "一輪後重來");
            }
        }

        [Test]
        public void 墨滴相位_連射5發就均勻鋪滿()
        {
            // Splattershot:間距 9.2、8 種相位(每種相差 1.15 m)。依序輪替時 5 發只到 0.58~5.18 m;打散後相鄰落點最多差 2 格(2.3 m),
            // 小於墨滴直徑 2 × 1.54
            var phases = new System.Collections.Generic.List<float>();
            for (int i = 0; i < 5; i++)
            {
                phases.Add(S3ShooterParams.DropletPhase(i, 8, 9.2f, 11.3f));
            }
            phases.Sort();
            float maxGap = phases[0];
            for (int i = 1; i < phases.Count; i++)
            {
                maxGap = UnityEngine.Mathf.Max(maxGap, phases[i] - phases[i - 1]);
            }
            maxGap = UnityEngine.Mathf.Max(maxGap, 9.2f - phases[phases.Count - 1]);
            Assert.LessOrEqual(maxGap, 2.3f + 1e-4f, string.Join(", ", phases));
        }
    }
}

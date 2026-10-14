using NUnit.Framework;
using SplatoonC.Core.Combat;

namespace SplatoonC.Tests
{
    // S3 能力公式對照 research/01 §2.1 的計算表(Leanny v11.3 參數)
    public class AbilityCurveTests
    {
        [Test]
        public void Ratio_0AP為0_57AP封頂為1()
        {
            Assert.AreEqual(0f, AbilityCurve.Ratio(0f), 1e-6f);
            Assert.AreEqual(1f, AbilityCurve.Ratio(57f), 1e-6f);
            Assert.AreEqual(1f, AbilityCurve.Ratio(80f), 1e-6f, "超過 57 AP 視為 57");
            // 10 AP:0.33 − 0.027 = 0.303
            Assert.AreEqual(0.303f, AbilityCurve.Ratio(10f), 1e-4f);
        }

        [Test]
        public void 跑速曲線_對照表()
        {
            Assert.AreEqual(0.096f, AbilityCurve.RunMid.Evaluate(0f), 1e-5f);
            Assert.AreEqual(0.1105f, AbilityCurve.RunMid.Evaluate(10f), 5e-4f);
            Assert.AreEqual(0.1319f, AbilityCurve.RunMid.Evaluate(30f), 5e-4f);
            Assert.AreEqual(0.144f, AbilityCurve.RunMid.Evaluate(57f), 1e-5f);
            Assert.AreEqual(0.1161f, AbilityCurve.RunLight.Evaluate(10f), 5e-4f);
            Assert.AreEqual(0.105f, AbilityCurve.RunHeavy.Evaluate(10f), 5e-4f);
        }

        [Test]
        public void 能量站29AP_跑速倍率約1點365()
        {
            // ratio(29) = 0.957 − 0.22707 = 0.72993 → 0.096 + 0.048 × 0.72993 = 0.13104 u/F = 7.86 m/s
            float ratio = AbilityCurve.RunSpeedRatio(29f, 1);
            Assert.AreEqual(0.13104f / 0.096f, ratio, 1e-3f);
            Assert.AreEqual(7.862f, 5.76f * ratio, 0.01f);
            // 57 AP 三個重量級同速(1.44 DU/F)
            Assert.AreEqual(8.64f, 6.24f * AbilityCurve.RunSpeedRatio(57f, 0), 0.01f);
            Assert.AreEqual(8.64f, 5.28f * AbilityCurve.RunSpeedRatio(57f, 2), 0.01f);
        }

        [Test]
        public void 游速與開火走速曲線()
        {
            Assert.AreEqual(0.2065f, AbilityCurve.SwimMid.Evaluate(10f), 5e-4f);
            Assert.AreEqual(0.2042f, AbilityCurve.SwimHeavy.Evaluate(10f), 5e-4f);
            Assert.AreEqual(0.24f / 0.192f, AbilityCurve.SwimSpeedRatio(57f, 1), 1e-4f);
            Assert.AreEqual(1.076f, AbilityCurve.ShotMoveRatio(10f), 1e-3f);
            Assert.AreEqual(1.25f, AbilityCurve.ShotMoveRatio(57f), 1e-4f);
        }

        [Test]
        public void 非對稱曲線_Mid不在正中()
        {
            // Special Saver 保留比例 [1.0, 0.8, 0.5]:20 AP → 0.823
            Assert.AreEqual(0.823f, AbilityCurve.SpecialKeep.Evaluate(20f), 2e-3f);
            Assert.AreEqual(1f, AbilityCurve.SpecialKeep.Evaluate(57f), 1e-5f);
            // Quick Super Jump 蓄力 [20, 35, 80]:10 AP → 43F
            Assert.AreEqual(43f, AbilityCurve.SuperJumpChargeFrames.Evaluate(10f), 0.6f);
            // 超級跳飛行 [96.6, 132.3, 138]:30 AP → 120F
            Assert.AreEqual(120f, AbilityCurve.SuperJumpFlightFrames.Evaluate(30f), 0.6f);
            // 墨抗敵墨走速 [0.0768, 0.0557, 0.024]:3 AP → 0.0335
            Assert.AreEqual(0.0335f, AbilityCurve.EnemyInkWalk.Evaluate(3f), 3e-4f);
            // 衝牆蓄力 [5, 18, 45]:57 AP → 5F
            Assert.AreEqual(5f, AbilityCurve.SquidSurgeChargeFrames.Evaluate(57f), 1e-4f);
        }

        [Test]
        public void 墨抗_敵墨持續傷害與起跳()
        {
            // 每幀持續傷害取整到 0.1:0 AP 0.3/F、3–25 AP 0.2、26–57 AP 0.1
            Assert.AreEqual(18f, AbilityCurve.EnemyInkDamagePerSecond(0f), 1e-3f);
            Assert.AreEqual(12f, AbilityCurve.EnemyInkDamagePerSecond(10f), 1e-3f);
            Assert.AreEqual(6f, AbilityCurve.EnemyInkDamagePerSecond(57f), 1e-3f);
            Assert.AreEqual(20f, AbilityCurve.EnemyInkDamageCap.Evaluate(57f), 1e-4f);
            Assert.AreEqual(33.9f, AbilityCurve.EnemyInkDamageCap.Evaluate(10f), 0.1f);
            // 0 AP 敵墨起跳高度 = (0.08/0.11)² ≈ 0.529;57 AP 與一般地面相同
            Assert.AreEqual(0.529f, AbilityCurve.EnemyInkJumpHeightRatio(0f), 1e-3f);
            Assert.AreEqual(1f, AbilityCurve.EnemyInkJumpHeightRatio(57f), 1e-4f);
        }

        [Test]
        public void 回墨與重生縮短()
        {
            // IRU 57 AP:潛墨 180F → 117F、非潛墨 600F → 220F
            Assert.AreEqual(180f / 117f, AbilityCurve.InkRecoveryRatio(57f, true), 1e-4f);
            Assert.AreEqual(600f / 220f, AbilityCurve.InkRecoveryRatio(57f, false), 1e-4f);
            Assert.AreEqual(1f, AbilityCurve.InkRecoveryRatio(0f, true), 1e-6f);
            // QR 57 AP:(90−30) + (270−90) = 240F = 4 秒(450F → 210F)
            Assert.AreEqual(4f, AbilityCurve.QuickRespawnSavedSeconds(57f), 1e-4f);
            Assert.AreEqual(0f, AbilityCurve.QuickRespawnSavedSeconds(0f), 1e-6f);
            // 30 AP 自身鏡頭 45F
            Assert.AreEqual(45f, AbilityCurve.OwnCameraFrames.Evaluate(30f), 0.6f);
        }
    }
}

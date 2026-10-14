using NUnit.Framework;
using SplatoonC.Core.Combat;

namespace SplatoonC.Tests
{
    // 狙擊槍 S3 參數(Leanny v11.3.0 ChargerNormal / ChargerLong)與純邏輯:部分蓄力插值(research 02 §7.2 推導表)、
    // 滿蓄跳值、射程 / 彈速 / 耗墨 / 塗墨半徑、蓄力速率、付得起的蓄力、走速與起跳、儲存蓄力時序
    public class ChargerParamsTests
    {
        private static S3ChargerParams Splat => S3ChargerParams.SplatCharger;
        private static S3ChargerParams ELiter => S3ChargerParams.ELiter4K;

        [Test]
        public void Splat部分蓄力傷害_8F40_15F454_30F569_45F684_59F792_滿蓄160()
        {
            Assert.AreEqual(40f, Splat.Damage(8f), 1e-4f);
            Assert.AreEqual(45.3f, Splat.Damage(15f), 1e-4f, "40 + 40 × 7/52 = 45.38 → 取整 45.3");
            Assert.AreEqual(56.9f, Splat.Damage(30f), 1e-4f);
            Assert.AreEqual(68.4f, Splat.Damage(45f), 1e-4f);
            Assert.AreEqual(79.2f, Splat.Damage(59f), 1e-4f, "滿蓄前一刻 < 80:部分蓄力永遠打不死人");
            Assert.AreEqual(160f, Splat.Damage(60f), 1e-4f, "滿蓄跳到 160");
            Assert.AreEqual(40f, Splat.Damage(0f), 1e-4f, "不到 8F 以最小蓄力計");
        }

        [Test]
        public void ELiter_92F滿蓄180_部分40到80()
        {
            Assert.AreEqual(92, ELiter.FullChargeFrames);
            Assert.AreEqual(180f, ELiter.Damage(92f), 1e-4f);
            Assert.AreEqual(40f, ELiter.Damage(8f), 1e-4f);
            Assert.AreEqual(60f, ELiter.Damage(50f), 1e-4f, "t = (50 − 8) / 84 = 0.5");
            Assert.Less(ELiter.Damage(91.9f), 80f);
            Assert.IsFalse(ELiter.IsFull(91.5f));
            Assert.IsTrue(ELiter.IsFull(92f));
            Assert.IsTrue(ELiter.IsFull(91.99f), "60 fps 累積誤差容差 0.02F");
        }

        [Test]
        public void 射程_含槍口_Splat1075到2575_ELiter1075到3076()
        {
            Assert.AreEqual(9.033f, Splat.Distance(8f), 1e-4f);
            Assert.AreEqual(24.037f, Splat.Distance(60f), 1e-4f);
            Assert.AreEqual(15.38f, Splat.Distance(30f), 0.01f, "推導表 30F = 15.38");
            Assert.AreEqual(10.75f, Splat.ReachFromPlayer(8f), 0.01f);
            Assert.AreEqual(25.75f, Splat.ReachFromPlayer(60f), 0.01f);
            Assert.AreEqual(10.755f, ELiter.ReachFromPlayer(8f), 0.01f);
            Assert.AreEqual(30.76f, ELiter.ReachFromPlayer(92f), 0.01f);
        }

        [Test]
        public void 彈速_Splat24到48_ELiter部分到64滿蓄96()
        {
            Assert.AreEqual(2.4f, Splat.Speed(8f), 1e-4f);
            Assert.AreEqual(4.8f, Splat.Speed(60f), 1e-4f);
            Assert.AreEqual(2.4f, ELiter.Speed(8f), 1e-4f);
            Assert.AreEqual(6.4f, ELiter.Speed(91.99f - 0.05f), 0.01f);
            Assert.AreEqual(9.6f, ELiter.Speed(92f), 1e-4f);
            Assert.AreEqual(6, Splat.FlightFrames(60f), "24.037 / 4.8 = 5.01 → 6F");
            Assert.AreEqual(4, ELiter.FlightFrames(92f), "29.05 / 9.6 = 3.03 → 4F");
            Assert.AreEqual(4, Splat.FlightFrames(8f), "9.033 / 2.4 = 3.76 → 4F");
        }

        [Test]
        public void 耗墨_最小225_滿蓄18與25_一槽5發與4發()
        {
            Assert.AreEqual(0.0225f, Splat.InkConsume(8f), 1e-6f);
            Assert.AreEqual(0.18f, Splat.InkConsume(60f), 1e-6f);
            Assert.AreEqual(0.0437f, Splat.InkConsume(15f), 1e-4f, "推導表 15F = 4.4%");
            Assert.AreEqual(0.177f, Splat.InkConsume(59f), 1e-3f, "推導表 59F = 17.7%");
            Assert.AreEqual(0.25f, ELiter.InkConsume(92f), 1e-6f);
            Assert.AreEqual(5, Splat.FullShotsPerTank);
            Assert.AreEqual(4, ELiter.FullShotsPerTank);
        }

        [Test]
        public void 付得起的蓄力_滿槽滿蓄_不足最小為負_中間線性()
        {
            Assert.AreEqual(60f, Splat.AffordableFrames(1f), 1e-4f);
            Assert.AreEqual(60f, Splat.AffordableFrames(0.18f), 1e-4f);
            Assert.Less(Splat.AffordableFrames(0.02f), 0f);
            Assert.AreEqual(8f, Splat.AffordableFrames(0.0225f), 1e-3f);
            float frames = Splat.AffordableFrames(0.1f);
            Assert.AreEqual(0.1f, Splat.InkConsume(frames), 1e-4f, "付得起的等級剛好花掉全部的墨");
        }

        [Test]
        public void 塗墨半徑_終點0906_2719_3263_沿線半寬078到156()
        {
            Assert.AreEqual(0.906f, Splat.EndPaintRadius(8f), 1e-4f);
            Assert.AreEqual(3.263f, Splat.EndPaintRadius(60f), 1e-4f);
            Assert.Less(Splat.EndPaintRadius(59.5f), 2.72f);
            Assert.AreEqual(0.78f, Splat.TrailWidthHalf(8f), 1e-4f);
            Assert.AreEqual(1.56f, Splat.TrailWidthHalf(60f), 1e-4f);
            Assert.AreEqual(2.73f, Splat.TrailDepthHalf(8f), 1e-4f);
            Assert.AreEqual(3.845f, ELiter.EndPaintRadius(92f), 1e-4f);
            Assert.AreEqual(1.85f, ELiter.TrailWidthHalf(92f), 1e-4f);
            Assert.AreEqual(1.5f, ELiter.LastSplashRate, 1e-4f);
            Assert.AreEqual(1.2f, Splat.WallPaintRadius(8f), 1e-4f);
            Assert.AreEqual(1.8f, Splat.WallPaintRadius(60f), 1e-4f);
        }

        [Test]
        public void 蓄力速率_空中或墨不足三分之一_不疊加()
        {
            Assert.AreEqual(1f, Splat.ChargeRate(false, false), 1e-6f);
            Assert.AreEqual(1f / 3f, Splat.ChargeRate(true, false), 1e-6f);
            Assert.AreEqual(1f / 3f, Splat.ChargeRate(false, true), 1e-6f);
            Assert.AreEqual(1f / 3f, Splat.ChargeRate(true, true), 1e-6f);
        }

        [Test]
        public void 最短蓄力8F_部分插值位置()
        {
            Assert.IsFalse(Splat.CanRelease(7.5f));
            Assert.IsTrue(Splat.CanRelease(8f));
            Assert.AreEqual(0f, Splat.PartialT(8f), 1e-6f);
            Assert.AreEqual(0.5f, Splat.PartialT(34f), 1e-6f);
            Assert.AreEqual(0.5f, Splat.Charge01(30f), 1e-6f);
            Assert.AreEqual(1f, Splat.Charge01(80f), 1e-6f);
        }

        [Test]
        public void 走速_未蓄0072_滿蓄002與0015_起跳滿蓄高度0405()
        {
            Assert.AreEqual(0.072f, Splat.MoveSpeed(0f), 1e-6f);
            Assert.AreEqual(0.02f, Splat.MoveSpeed(1f), 1e-6f);
            Assert.AreEqual(0.015f, ELiter.MoveSpeed(1f), 1e-6f);
            Assert.AreEqual(1.2f, Splat.MoveSpeed(1f) * 60f, 1e-4f, "1.2 m/s");
            Assert.AreEqual(1f, Splat.JumpHeightFactor(0f), 1e-6f);
            Assert.AreEqual(0.07f * 0.07f / (0.11f * 0.11f), Splat.JumpHeightFactor(1f), 1e-5f);
            Assert.AreEqual(0.405f, Splat.JumpHeightFactor(1f), 1e-3f);
        }

        [Test]
        public void 儲存蓄力_75F_浮出25F雷射_31F開槍_開槍後15F不能潛_20F回墨()
        {
            Assert.AreEqual(75, Splat.KeepChargeFrames);
            Assert.AreEqual(25, Splat.KeepLaserDelayFrames);
            Assert.AreEqual(31, Splat.KeepFireDelayFrames);
            Assert.AreEqual(15, Splat.PostShotNoSquidFrames);
            Assert.AreEqual(20, Splat.InkRecoverStop);
            Assert.AreEqual(75, ELiter.KeepChargeFrames);
            Assert.AreEqual(1f, Splat.FullChargeSeconds, 1e-6f);
            Assert.AreEqual(92f / 60f, ELiter.FullChargeSeconds, 1e-6f);
        }

        [Test]
        public void 對人判定_0125與01125_對地形002()
        {
            Assert.AreEqual(0.125f, Splat.PlayerRadius, 1e-6f);
            Assert.AreEqual(0.1125f, ELiter.PlayerRadius, 1e-6f);
            Assert.AreEqual(0.02f, Splat.FieldRadius, 1e-6f);
        }
    }
}

using NUnit.Framework;
using SplatoonC.Core.Locomotion;
using UnityEngine;

namespace SplatoonC.Tests
{
    // A4 Squid Roll / Wall Roll / Squid Surge 的純邏輯(DESIGN_REFERENCE §2.7、research/01 §9–§11)
    public class SquidMoveModelTests
    {
        private static readonly SquidMoveParams P = SquidMoveParams.Splatoon3;

        [Test]
        public void S3數值_門檻角度緩衝連滾護甲()
        {
            Assert.AreEqual(8.7f, P.RollMinSpeed, 1e-4f, "0.145 u/F × 60");
            Assert.AreEqual(60f, P.RollMinAngleDeg, 1e-4f);
            Assert.AreEqual(10, P.RollBufferFrames);
            Assert.AreEqual(90, P.RollChainWindowFrames);
            Assert.AreEqual(0.85f, P.RollChainFactor, 1e-4f);
            Assert.AreEqual(30f, P.MoveArmorHp, 1e-4f);
            Assert.AreEqual(18, P.RollArmorFrames);
            Assert.AreEqual(45, P.SurgeChargeFrames);
            Assert.AreEqual(45, P.SurgeArmorFrames);
            Assert.AreEqual(45f, P.WallRollMaxAngleDeg, 1e-4f);
        }

        [Test]
        public void 翻滾_速度達門檻且夾角至少60度才成立()
        {
            Vector3 forward = Vector3.forward;
            // 反向(180°)、側向(90°)、剛好 60° → 成立
            Assert.IsTrue(SquidMoveModel.CanRoll(P, 11.52f, 0f, forward, Vector3.back, 1f));
            Assert.IsTrue(SquidMoveModel.CanRoll(P, 11.52f, 0f, forward, Vector3.right, 1f));
            Vector3 sixty = Quaternion.Euler(0f, 60f, 0f) * forward;
            Assert.IsTrue(SquidMoveModel.CanRoll(P, 11.52f, 0f, forward, sixty, 1f));
            // 59°、同向 → 不成立(一般潛水跳)
            Vector3 fiftyNine = Quaternion.Euler(0f, 59f, 0f) * forward;
            Assert.IsFalse(SquidMoveModel.CanRoll(P, 11.52f, 0f, forward, fiftyNine, 1f));
            Assert.IsFalse(SquidMoveModel.CanRoll(P, 11.52f, 0f, forward, forward, 1f));
            // 搖桿沒推(死區內)→ 不成立;搖桿深度本身不影響(輕推也行)
            Assert.IsFalse(SquidMoveModel.CanRoll(P, 11.52f, 0f, forward, Vector3.back, 0.1f));
            Assert.IsTrue(SquidMoveModel.CanRoll(P, 11.52f, 0f, forward, Vector3.back, 0.3f));
        }

        [Test]
        public void 翻滾_掉到門檻以下10F內仍可翻_超過就不行()
        {
            Vector3 forward = Vector3.forward;
            // 速度已經 5 m/s(< 8.7),最後一次高速在 9F 前 → 緩衝內
            Assert.IsTrue(SquidMoveModel.CanRoll(P, 5f, 9f / 60f, forward, Vector3.back, 1f));
            Assert.IsTrue(SquidMoveModel.CanRoll(P, 5f, 10f / 60f, forward, Vector3.back, 1f));
            Assert.IsFalse(SquidMoveModel.CanRoll(P, 5f, 11f / 60f, forward, Vector3.back, 1f));
            // 從來沒快過
            Assert.IsFalse(SquidMoveModel.CanRoll(P, 5f, float.PositiveInfinity, forward, Vector3.back, 1f));
            // 剛好在門檻上
            Assert.IsTrue(SquidMoveModel.CanRoll(P, 8.7f, float.PositiveInfinity, forward, Vector3.back, 1f));
        }

        [Test]
        public void 連滾_90F內每次乘0點85_超過窗口重置()
        {
            int first = SquidMoveModel.NextChainIndex(0, float.PositiveInfinity, P);
            Assert.AreEqual(0, first);
            int second = SquidMoveModel.NextChainIndex(first, 60f / 60f, P);
            Assert.AreEqual(1, second);
            int third = SquidMoveModel.NextChainIndex(second, 90f / 60f, P);
            Assert.AreEqual(2, third);
            Assert.AreEqual(0, SquidMoveModel.NextChainIndex(third, 91f / 60f, P));
            Assert.AreEqual(1f, SquidMoveModel.ChainSpeedFactor(0, 0.85f), 1e-6f);
            Assert.AreEqual(0.85f, SquidMoveModel.ChainSpeedFactor(1, 0.85f), 1e-6f);
            Assert.AreEqual(0.7225f, SquidMoveModel.ChainSpeedFactor(2, 0.85f), 1e-6f);
            // Intensify Action 57 AP = 1.0:無限連滾不打折
            Assert.AreEqual(1f, SquidMoveModel.ChainSpeedFactor(5, 1f), 1e-6f);
            // 中量級游速 11.52:第三滾 8.32 < 8.7 → 落地後要重新加速才能再滾(游速快的武器能多連幾次)
            Assert.Less(11.52f * SquidMoveModel.ChainSpeedFactor(2, 0.85f), P.RollMinSpeed);
            Assert.Greater(11.52f * SquidMoveModel.ChainSpeedFactor(1, 0.85f), P.RollMinSpeed);
        }

        [Test]
        public void 起跳速度_固定滯空與指定高度()
        {
            // 36F 滯空、重力 10.8 → v = 10.8 × 0.6 / 2 = 3.24 m/s
            Assert.AreEqual(3.24f, SquidMoveModel.LaunchSpeedForAirtime(36, -10.8f), 1e-4f);
            // 升 3.5 m:√(2 × 10.8 × 3.5) = 8.695
            Assert.AreEqual(8.695f, SquidMoveModel.LaunchSpeedForHeight(3.5f, -10.8f), 1e-3f);
            Assert.AreEqual(0f, SquidMoveModel.LaunchSpeedForHeight(-1f, -10.8f), 1e-6f);
        }

        [Test]
        public void 衝牆_蓄力比例決定上衝速度與越頂高度()
        {
            Assert.AreEqual(0f, SquidMoveModel.SurgeCharge01(0f, 0.75f), 1e-6f);
            Assert.AreEqual(0.5f, SquidMoveModel.SurgeCharge01(22.5f / 60f, 45f / 60f), 1e-4f);
            Assert.AreEqual(1f, SquidMoveModel.SurgeCharge01(2f, 0.75f), 1e-6f);
            // 零蓄 = 平常爬牆速度;滿蓄 = 22.5 m/s
            Assert.AreEqual(7.5f, SquidMoveModel.SurgeClimbSpeed(P, 0f, 7.5f), 1e-4f);
            Assert.AreEqual(22.5f, SquidMoveModel.SurgeClimbSpeed(P, 1f, 7.5f), 1e-4f);
            Assert.AreEqual(15f, SquidMoveModel.SurgeClimbSpeed(P, 0.5f, 7.5f), 1e-4f);
            Assert.AreEqual(P.SurgeMinHop, SquidMoveModel.SurgeHopHeight(P, 0f), 1e-4f);
            Assert.AreEqual(P.SurgeMaxHop, SquidMoveModel.SurgeHopHeight(P, 1f), 1e-4f);
        }

        [Test]
        public void 牆滾_搖桿在離牆方向45度內()
        {
            Vector3 normal = Vector3.left;
            Assert.IsTrue(SquidMoveModel.IsWallRollInput(P, Vector3.left, 1f, normal));
            Assert.IsTrue(SquidMoveModel.IsWallRollInput(P, Quaternion.Euler(0f, 45f, 0f) * Vector3.left, 1f, normal));
            Assert.IsFalse(SquidMoveModel.IsWallRollInput(P, Quaternion.Euler(0f, 50f, 0f) * Vector3.left, 1f, normal));
            // 推向牆(上爬)不是牆滾
            Assert.IsFalse(SquidMoveModel.IsWallRollInput(P, Vector3.right, 1f, normal));
            // 取消衝牆要推得夠深(0.5)
            Assert.IsFalse(SquidMoveModel.IsPushingAway(P, Vector3.left * 0.3f, 0.3f, normal));
            Assert.IsTrue(SquidMoveModel.IsPushingAway(P, Vector3.left, 1f, normal));
        }
    }
}

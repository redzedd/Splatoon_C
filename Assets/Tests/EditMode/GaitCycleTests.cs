using NUnit.Framework;
using SplatoonC.Core.Animation;
using UnityEngine;

namespace SplatoonC.Tests.EditMode
{
    public sealed class GaitCycleTests
    {
        [Test]
        public void 相位依距離推進_一步半週期()
        {
            var gait = new GaitCycle();

            // 一步 = 半個週期(π)
            gait.Advance(1.35f, 1.35f);
            Assert.AreEqual(Mathf.PI, gait.Phase, 1e-4f);

            gait.Advance(1.35f, 1.35f);
            Assert.AreEqual(0f, Mathf.Repeat(gait.Phase, Mathf.PI * 2f), 1e-3f, "兩步 = 一整個週期,回到起點");
        }

        [Test]
        public void 零距離或負距離不推進()
        {
            var gait = new GaitCycle();
            gait.Advance(0.3f, 1.35f);
            float before = gait.Phase;

            gait.Advance(0f, 1.35f);
            gait.Advance(-1f, 1.35f);

            Assert.AreEqual(before, gait.Phase, 1e-6f);
        }

        [Test]
        public void S3全速跑_每秒約四步()
        {
            // 中量級跑速 0.096 u/F = 5.76 m/s,步幅 1.35 m(與腳步聲間距相同)
            float stride = GaitCycle.StrideForSpeed(5.76f, 5.76f, 1.35f, 0.55f);
            Assert.AreEqual(1.35f, stride, 1e-4f);
            Assert.AreEqual(4.27f, 5.76f / stride, 0.01f);
        }

        [Test]
        public void 步幅隨速度變小()
        {
            float walk = GaitCycle.StrideForSpeed(4.32f, 5.76f, 1.35f, 0.55f);
            float idle = GaitCycle.StrideForSpeed(0f, 5.76f, 1.35f, 0.55f);

            Assert.Less(walk, 1.35f);
            Assert.Greater(walk, 0.55f);
            Assert.AreEqual(0.55f, idle, 1e-4f);
        }

        [Test]
        public void 支撐中點腳往後滑的速度等於身體速度_不滑步()
        {
            // 腳位置 z = 半幅 × sin(p),相位速度 dp/dt = v·π/步幅 → 支撐中點(p = π)腳速 = 半幅 · v·π/步幅
            float stride = 1.35f;
            float half = GaitCycle.FootSweepHalf(stride);
            float speed = 5.76f;
            float footSpeed = half * speed * Mathf.PI / stride;

            Assert.AreEqual(speed, footSpeed, 1e-3f);
        }

        [Test]
        public void 抬腳只在擺盪期且兩端連續()
        {
            // 支撐期(cos p < 0)貼地
            Assert.AreEqual(0f, GaitCycle.FootLift(Mathf.PI), 1e-6f);
            Assert.AreEqual(0f, GaitCycle.FootLift(Mathf.PI * 0.75f), 1e-6f);
            // 擺盪期中段抬起
            Assert.Greater(GaitCycle.FootLift(0f), 0.8f);
            // 蹬離與著地瞬間接近 0(不跳變)
            Assert.Less(GaitCycle.FootLift(-Mathf.PI * 0.5f + 0.01f), 0.05f);
            Assert.Less(GaitCycle.FootLift(Mathf.PI * 0.5f - 0.01f), 0.05f);
        }

        [Test]
        public void 峰值在腳還在身後時_腳跟上踢()
        {
            float behind = GaitCycle.FootLift(-0.3f);
            float ahead = GaitCycle.FootLift(0.3f);

            Assert.Greater(behind, ahead);
        }

        [Test]
        public void 左右腿與同側手臂反相()
        {
            float p = 0.7f;
            Assert.AreEqual(-GaitCycle.FootForward(p), GaitCycle.FootForward(p + Mathf.PI), 1e-5f);
            Assert.AreEqual(-GaitCycle.FootForward(p), GaitCycle.ArmSwing(p), 1e-5f);
        }

        [Test]
        public void 停下時相位回到雙腳並攏()
        {
            var gait = new GaitCycle();
            gait.Advance(0.5f, 1.35f);
            for (int i = 0; i < 200; i++)
            {
                gait.SettleTowardsRest(6f, 1f / 60f);
            }

            Assert.AreEqual(0f, Mathf.Sin(gait.Phase), 1e-3f);
        }
    }
}

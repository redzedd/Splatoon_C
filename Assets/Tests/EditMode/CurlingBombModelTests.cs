using NUnit.Framework;
using SplatoonC.Core.Combat;
using UnityEngine;

namespace SplatoonC.Tests
{
    // Curling Bomb 純邏輯(S3 v11.3.0 WeaponBombCurling;research 03 §2.7)
    public class CurlingBombModelTests
    {
        private static readonly CurlingBombParams P = CurlingBombParams.S3;

        [Test]
        public void 引信等於210減兩倍蓄力幀()
        {
            Assert.AreEqual(210, CurlingBombModel.FuseFrames(P, 0f));
            Assert.AreEqual(150, CurlingBombModel.FuseFrames(P, 0.5f), "蓄 30F → 210 − 60");
            Assert.AreEqual(90, CurlingBombModel.FuseFrames(P, 1f), "滿蓄 60F → 90F");
        }

        [Test]
        public void 墨量鎖從70F降到滿蓄30F()
        {
            Assert.AreEqual(70, CurlingBombModel.InkLockFrames(P, 0f));
            Assert.AreEqual(50, CurlingBombModel.InkLockFrames(P, 0.5f));
            Assert.AreEqual(30, CurlingBombModel.InkLockFrames(P, 1f));
        }

        [Test]
        public void 蓄力扣掉出手準備5F且上限60F()
        {
            Assert.AreEqual(0f, CurlingBombModel.ChargeFramesAfterHold(P, 4f));
            Assert.AreEqual(10f, CurlingBombModel.ChargeFramesAfterHold(P, 15f));
            Assert.AreEqual(60f, CurlingBombModel.ChargeFramesAfterHold(P, 120f));
            Assert.AreEqual(1f, CurlingBombModel.Charge01(P, 60f));
        }

        [Test]
        public void 爆炸半徑與軌跡半徑依蓄力內插()
        {
            Assert.AreEqual(1.6f, CurlingBombModel.NearRadius(P, 0f), 1e-4f);
            Assert.AreEqual(4.6f, CurlingBombModel.NearRadius(P, 1f), 1e-4f);
            Assert.AreEqual(3.1f, CurlingBombModel.NearRadius(P, 0.5f), 1e-4f);
            Assert.AreEqual(5f, CurlingBombModel.FarRadius(P, 0f), 1e-4f);
            Assert.AreEqual(8f, CurlingBombModel.FarRadius(P, 1f), 1e-4f);
            Assert.AreEqual(2.133f, CurlingBombModel.PaintRadius(P, 0f), 1e-4f);
            Assert.AreEqual(1.29f, CurlingBombModel.TrailRadius(P, 1f), 1e-4f);
        }

        [Test]
        public void 巡航速度從022降到010且最後90F減速到停()
        {
            Assert.AreEqual(0.22f, CurlingBombModel.TargetSpeed(P, 0f, 200), 1e-5f);
            Assert.AreEqual(0.10f, CurlingBombModel.TargetSpeed(P, 1f, 200), 1e-5f);
            Assert.AreEqual(1f, CurlingBombModel.SlowdownFactor(P, 90));
            float mid = CurlingBombModel.SlowdownFactor(P, 45);
            Assert.That(mid, Is.InRange(0.1f, 0.4f), $"剩 45F 巡航倍率 {mid:0.000}");
            Assert.AreEqual(0f, CurlingBombModel.SlowdownFactor(P, 0));
        }

        [Test]
        public void 滑行速度收斂到巡航()
        {
            float fast = CurlingBombModel.StepGroundSpeed(P, 0.30f, 0.22f);
            Assert.AreEqual(0.22f + 0.08f * 0.92f, fast, 1e-5f, "超出量 ×0.92");
            float slow = CurlingBombModel.StepGroundSpeed(P, 0.12f, 0.22f);
            Assert.AreEqual(0.22f - 0.10f * 0.96f, slow, 1e-5f, "不足量 ×0.96");
        }

        [Test]
        public void 出手初速與玩家動量()
        {
            Vector3 level = CurlingBombModel.LaunchVelocity(P, Vector3.forward, 0f, Vector3.zero);
            Assert.AreEqual(0.4f, level.z, 1e-5f);
            Assert.AreEqual(0.12f, level.y, 1e-5f);
            Vector3 charged = CurlingBombModel.LaunchVelocity(P, Vector3.forward, 1f, Vector3.zero);
            Assert.AreEqual(0.2f, charged.z, 1e-5f, "滿蓄出手 0.20");
            Vector3 down = CurlingBombModel.LaunchVelocity(P, Vector3.down, 0f, Vector3.zero);
            Assert.AreEqual(-0.28f, down.y, 1e-5f, "−0.4 + 0.12,未觸及下限 −0.5");
            Vector3 running = CurlingBombModel.LaunchVelocity(P, Vector3.forward, 0f, new Vector3(0.05f, 0f, 0.1f));
            Assert.AreEqual(0.4f + 0.1f * 1.2f, running.z, 1e-5f, "前向 ×1.2");
            Assert.AreEqual(0.05f * 0.8f, running.x, 1e-5f, "橫向 ×0.8");
            Vector3 jumping = CurlingBombModel.LaunchVelocity(P, Vector3.forward, 0f, new Vector3(0f, 0.2f, 0f));
            Assert.AreEqual(0.12f + 0.16f, jumping.y, 1e-5f, "上升 ×2.0 但上限 0.16");
        }

        [Test]
        public void 平地總距離符合JP實測()
        {
            float uncharged = CurlingBombModel.PredictFlatTravel(P, 0f, 0f, 0.55f, out int air);
            Assert.That(uncharged, Is.InRange(30f, 38f), $"未蓄力 {uncharged:0.0} u(JP 6.9 線 = 34.5 u)");
            Assert.That(air, Is.InRange(12, 25), $"低拋空中 {air}F");
            float full = CurlingBombModel.PredictFlatTravel(P, 1f, 0f, 0.55f, out _);
            Assert.That(full, Is.InRange(3f, 6f), $"滿蓄 {full:0.0} u(JP 1.0 線 = 5 u)");
        }

        [Test]
        public void bot蓄力選擇讓爆點落在目標距離()
        {
            Assert.AreEqual(0f, CurlingBombModel.ChargeForDistance(P, 60f, -15f, 0.55f));
            Assert.AreEqual(1f, CurlingBombModel.ChargeForDistance(P, 1f, -15f, 0.55f));
            float charge = CurlingBombModel.ChargeForDistance(P, 12f, -15f, 0.55f);
            float travel = CurlingBombModel.PredictFlatTravel(P, charge, -15f, 0.55f, out _);
            Assert.AreEqual(12f, travel, 0.3f, $"蓄 {charge:0.00} 滑 {travel:0.00} u");
        }
    }
}

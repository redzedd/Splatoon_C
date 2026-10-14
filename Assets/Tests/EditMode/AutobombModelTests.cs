using NUnit.Framework;
using SplatoonC.Core.Combat;
using UnityEngine;

namespace SplatoonC.Tests
{
    // Autobomb 純邏輯(S3 v11.3.0 WeaponBombRobot;research 03 §2.8)
    public class AutobombModelTests
    {
        private static readonly AutobombParams P = AutobombParams.S3;

        [Test]
        public void 搜尋範圍是水平125上下5的圓柱()
        {
            Assert.IsTrue(AutobombModel.InSearchWindow(P, new Vector3(12.4f, 0f, 0f)));
            Assert.IsTrue(AutobombModel.InSearchWindow(P, new Vector3(8f, 4.9f, 9f)), "√(64+81) = 12.04");
            Assert.IsFalse(AutobombModel.InSearchWindow(P, new Vector3(9f, 0f, 9f)), "√162 = 12.7");
            Assert.IsFalse(AutobombModel.InSearchWindow(P, new Vector3(2f, 5.5f, 0f)), "高於 5");
            Assert.IsFalse(AutobombModel.InSearchWindow(P, new Vector3(2f, -5.5f, 0f)), "低於 −5");
        }

        [Test]
        public void 追蹤每幀加速0003到最高009()
        {
            Assert.AreEqual(0.003f, AutobombModel.StepChaseSpeed(P, 0f), 1e-6f);
            Assert.AreEqual(0.09f, AutobombModel.StepChaseSpeed(P, 0.089f), 1e-6f);
            Assert.AreEqual(30, AutobombModel.FramesToMaxSpeed(P), "0.09 / 0.003");
            // 前 30F 平均 0.0465 → 1.395 u;之後每幀 0.09
            Assert.AreEqual(1.395f + 0.09f * 120f, AutobombModel.ChaseDistance(P, 150), 0.01f);
        }

        [Test]
        public void 每幀最多轉2度()
        {
            Assert.AreEqual(2f, AutobombModel.StepYaw(P, 0f, 90f), 1e-4f);
            Assert.AreEqual(-2f, AutobombModel.StepYaw(P, 0f, -170f), 1e-4f);
            Assert.AreEqual(359f, Mathf.Repeat(AutobombModel.StepYaw(P, 1f, 350f), 360f), 1e-3f, "走最短方向");
            Assert.AreEqual(45f, AutobombModel.StepYaw(P, 44f, 45f), 1e-4f);
        }
    }
}

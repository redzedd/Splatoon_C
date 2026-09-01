using NUnit.Framework;
using SplatoonC.Core.Locomotion;
using UnityEngine;

namespace SplatoonC.Tests
{
    public class CharacterMotionSolverTests
    {
        private static LocomotionSettings CreateSettings()
        {
            return new LocomotionSettings(
                moveSpeed: 6f,
                gravity: -25f,
                jumpHeight: 1.6f,
                airControl: 0.6f,
                coyoteTime: 0.12f,
                jumpBufferTime: 0.10f,
                turnSpeedDegPerSec: 720f,
                groundedStickVelocity: -2f);
        }

        [Test]
        public void 重力累積_離地一秒後垂直速度等於重力值()
        {
            var s = CreateSettings();
            var state = MotionState.CreateInitial();
            CharacterMotionSolver.Step(ref state, Vector2.zero, 0f, false, false, s, 0f, 1f);
            Assert.AreEqual(-25f, state.VerticalVelocity, 1e-4f);
        }

        [Test]
        public void 著地時垂直速度壓成下壓值_防著地旗標抖動()
        {
            var s = CreateSettings();
            var state = MotionState.CreateInitial();
            CharacterMotionSolver.Step(ref state, Vector2.zero, 0f, true, false, s, 0f, 1f / 60f);
            Assert.AreEqual(-2f, state.VerticalVelocity, 1e-5f);
        }

        [Test]
        public void 跳躍峰值_由跳躍高度反算_誤差小於百分之五()
        {
            var s = CreateSettings();
            var state = MotionState.CreateInitial();
            const float dt = 1f / 240f;
            float time = 0f;
            float height = 0f;
            float peak = 0f;

            var step = CharacterMotionSolver.Step(ref state, Vector2.zero, 0f, true, true, s, time, dt);
            Assert.IsTrue(step.Jumped, "著地按跳應立即起跳");
            height += step.Displacement.y;

            for (int i = 0; i < 960; i++)
            {
                time += dt;
                step = CharacterMotionSolver.Step(ref state, Vector2.zero, 0f, false, false, s, time, dt);
                height += step.Displacement.y;
                peak = Mathf.Max(peak, height);
            }

            Assert.AreEqual(1.6f, peak, 1.6f * 0.05f);
        }

        [Test]
        public void 土狼時間內離地仍可跳()
        {
            var s = CreateSettings();
            var state = MotionState.CreateInitial();
            CharacterMotionSolver.Step(ref state, Vector2.zero, 0f, true, false, s, 0f, 1f / 60f);
            var step = CharacterMotionSolver.Step(ref state, Vector2.zero, 0f, false, true, s, 0.08f, 1f / 60f);
            Assert.IsTrue(step.Jumped);
        }

        [Test]
        public void 土狼時間過後離地不可跳()
        {
            var s = CreateSettings();
            var state = MotionState.CreateInitial();
            CharacterMotionSolver.Step(ref state, Vector2.zero, 0f, true, false, s, 0f, 1f / 60f);
            var step = CharacterMotionSolver.Step(ref state, Vector2.zero, 0f, false, true, s, 0.2f, 1f / 60f);
            Assert.IsFalse(step.Jumped);
        }

        [Test]
        public void 落地前按跳_緩衝時間內著地自動起跳()
        {
            var s = CreateSettings();
            var state = MotionState.CreateInitial();
            CharacterMotionSolver.Step(ref state, Vector2.zero, 0f, false, true, s, 0f, 1f / 60f);
            var step = CharacterMotionSolver.Step(ref state, Vector2.zero, 0f, true, false, s, 0.05f, 1f / 60f);
            Assert.IsTrue(step.Jumped);
        }

        [Test]
        public void 起跳後土狼窗內再按跳_不可二段跳()
        {
            var s = CreateSettings();
            var state = MotionState.CreateInitial();
            var first = CharacterMotionSolver.Step(ref state, Vector2.zero, 0f, true, true, s, 0f, 1f / 60f);
            Assert.IsTrue(first.Jumped);
            var second = CharacterMotionSolver.Step(ref state, Vector2.zero, 0f, false, true, s, 0.05f, 1f / 60f);
            Assert.IsFalse(second.Jumped);
        }

        [Test]
        public void 無輸入時_不要求轉向()
        {
            var s = CreateSettings();
            var state = MotionState.CreateInitial();
            var step = CharacterMotionSolver.Step(ref state, Vector2.zero, 0f, true, false, s, 0f, 1f / 60f);
            Assert.IsFalse(step.HasMoveInput);
        }

        [Test]
        public void 相機轉九十度_前推輸入變成世界X方向位移()
        {
            var s = CreateSettings();
            var state = MotionState.CreateInitial();
            var step = CharacterMotionSolver.Step(ref state, new Vector2(0f, 1f), 90f, true, false, s, 0f, 1f);
            Assert.AreEqual(6f, step.Displacement.x, 1e-3f);
            Assert.AreEqual(0f, step.Displacement.z, 1e-3f);
            Assert.IsTrue(step.HasMoveInput);
        }

        [Test]
        public void 速度倍率_直接乘在水平位移上()
        {
            var s = CreateSettings();
            var state = MotionState.CreateInitial();
            var step = CharacterMotionSolver.Step(
                ref state, new Vector2(0f, 1f), 0f, true, false, s, 0f, 1f, 1.8f);
            Assert.AreEqual(10.8f, step.Displacement.z, 1e-3f);
        }

        [Test]
        public void 輸入向量超長_被正規化不超速()
        {
            var s = CreateSettings();
            var state = MotionState.CreateInitial();
            var step = CharacterMotionSolver.Step(ref state, new Vector2(1f, 1f), 0f, true, false, s, 0f, 1f);
            var horizontal = new Vector2(step.Displacement.x, step.Displacement.z);
            Assert.AreEqual(6f, horizontal.magnitude, 1e-3f);
        }
    }
}

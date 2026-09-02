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

        private static LocomotionSettings CreateSettingsWithRamp(float accelTime, float decelTime)
        {
            return new LocomotionSettings(
                moveSpeed: 6f,
                gravity: -25f,
                jumpHeight: 1.6f,
                airControl: 0.6f,
                coyoteTime: 0.12f,
                jumpBufferTime: 0.10f,
                turnSpeedDegPerSec: 720f,
                groundedStickVelocity: -2f,
                accelTime: accelTime,
                decelTime: decelTime);
        }

        [Test]
        public void 加速曲線_首幀未滿速_零點一五秒後達滿速()
        {
            var s = CreateSettingsWithRamp(0.12f, 0.08f);
            var state = MotionState.CreateInitial();
            const float dt = 1f / 60f;
            var first = CharacterMotionSolver.Step(
                ref state, new Vector2(0f, 1f), 0f, true, false, s, 0f, dt);
            Assert.Less(first.Displacement.z / dt, 2f, "首幀不得瞬時滿速");

            for (int i = 1; i < 9; i++)
            {
                CharacterMotionSolver.Step(
                    ref state, new Vector2(0f, 1f), 0f, true, false, s, i * dt, dt);
            }
            var later = CharacterMotionSolver.Step(
                ref state, new Vector2(0f, 1f), 0f, true, false, s, 10 * dt, dt);
            Assert.AreEqual(6f, later.Displacement.z / dt, 0.1f, "0.15 秒後應達滿速");
        }

        [Test]
        public void 減速曲線_放開輸入後停下不瞬停()
        {
            var s = CreateSettingsWithRamp(0.12f, 0.08f);
            var state = MotionState.CreateInitial();
            const float dt = 1f / 60f;
            for (int i = 0; i < 12; i++)
            {
                CharacterMotionSolver.Step(
                    ref state, new Vector2(0f, 1f), 0f, true, false, s, i * dt, dt);
            }
            var coast = CharacterMotionSolver.Step(
                ref state, Vector2.zero, 0f, true, false, s, 12 * dt, dt);
            Assert.Greater(coast.Displacement.z / dt, 2f, "放開首幀仍應有慣性");

            for (int i = 13; i < 20; i++)
            {
                CharacterMotionSolver.Step(
                    ref state, Vector2.zero, 0f, true, false, s, i * dt, dt);
            }
            var stopped = CharacterMotionSolver.Step(
                ref state, Vector2.zero, 0f, true, false, s, 20 * dt, dt);
            Assert.AreEqual(0f, stopped.Displacement.z / dt, 0.05f, "0.13 秒後應完全停下");
        }

        [Test]
        public void 曲線時間為零_維持瞬時舊行為()
        {
            var s = CreateSettings();
            var state = MotionState.CreateInitial();
            var step = CharacterMotionSolver.Step(
                ref state, new Vector2(0f, 1f), 0f, true, false, s, 0f, 1f / 60f);
            Assert.AreEqual(6f, step.Displacement.z / (1f / 60f), 1e-3f);
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
    
        [Test]
        public void 保留動量_空中維持起跳時的水平速度與方向()
        {
            var s = CreateSettingsWithRamp(0.12f, 0.08f);
            var state = MotionState.CreateInitial();
            const float dt = 1f / 60f;
            // 先以 1.56 倍(潛水速度)在地面加速到滿速
            for (int i = 0; i < 30; i++)
            {
                CharacterMotionSolver.Step(
                    ref state, new Vector2(0f, 1f), 0f, true, false, s, i * dt, dt, 1.56f);
            }
            Vector3 launched = state.HorizontalVelocity;
            Assert.AreEqual(6f * 1.56f, launched.z, 0.1f);

            // 空中鬆開輸入、倍率掉回 1:保留動量時水平速度不得改變
            for (int i = 30; i < 40; i++)
            {
                CharacterMotionSolver.Step(
                    ref state, Vector2.zero, 0f, false, false, s, i * dt, dt, 1f, true);
            }

            Assert.AreEqual(launched.z, state.HorizontalVelocity.z, 1e-4f, "應維持原潛水速度");
            Assert.AreEqual(launched.x, state.HorizontalVelocity.x, 1e-4f, "應維持原行進方向");
        }

        [Test]
        public void 保留動量_關閉時空中會被輸入與倍率拉走()
        {
            var s = CreateSettingsWithRamp(0.12f, 0.08f);
            var state = MotionState.CreateInitial();
            const float dt = 1f / 60f;
            for (int i = 0; i < 30; i++)
            {
                CharacterMotionSolver.Step(
                    ref state, new Vector2(0f, 1f), 0f, true, false, s, i * dt, dt, 1.56f);
            }
            float launched = state.HorizontalVelocity.z;

            for (int i = 30; i < 40; i++)
            {
                CharacterMotionSolver.Step(
                    ref state, Vector2.zero, 0f, false, false, s, i * dt, dt, 1f, false);
            }

            Assert.Less(state.HorizontalVelocity.z, launched - 0.5f, "沒開保留動量就該被減速拉走");
        }

        [Test]
        public void 保留動量_重力與跳躍仍照常運作()
        {
            var s = CreateSettingsWithRamp(0.12f, 0.08f);
            var state = MotionState.CreateInitial();
            const float dt = 1f / 60f;

            CharacterMotionSolver.Step(
                ref state, Vector2.zero, 0f, false, false, s, 0f, dt, 1f, true);

            Assert.Less(state.VerticalVelocity, 0f, "保留水平動量不該影響重力");
        }
    }
}


using NUnit.Framework;
using SplatoonC.Core.AI;
using UnityEngine;

namespace SplatoonC.Tests
{
    public class BotMathTests
    {
        private const float Eps = 1e-3f;

        [Test]
        public void 移動輸入_視線朝北往東走是向右()
        {
            Vector2 input = BotMath.MoveInputFor(Vector3.right, 0f);
            Assert.AreEqual(1f, input.x, Eps);
            Assert.AreEqual(0f, input.y, Eps);
        }

        [Test]
        public void 移動輸入_視線朝東往東走是向前()
        {
            Vector2 input = BotMath.MoveInputFor(Vector3.right, 90f);
            Assert.AreEqual(0f, input.x, Eps);
            Assert.AreEqual(1f, input.y, Eps);
        }

        [Test]
        public void 移動輸入往返_與角色移動求解器的相機相對換算一致()
        {
            // CharacterMotionSolver:world = Euler(0, yaw, 0) * (x, 0, y)
            Vector3 world = new Vector3(0.3f, 0f, -0.8f).normalized;
            float yaw = 37f;
            Vector2 input = BotMath.MoveInputFor(world, yaw);
            Vector3 back = Quaternion.Euler(0f, yaw, 0f) * new Vector3(input.x, 0f, input.y);
            Assert.AreEqual(world.x, back.x, Eps);
            Assert.AreEqual(world.z, back.z, Eps);
        }

        [Test]
        public void 角度_往下看為正俯角()
        {
            BotMath.AnglesTo(Vector3.up * 2f, new Vector3(0f, 0f, 2f), out float yaw, out float pitch);
            Assert.AreEqual(0f, yaw, Eps);
            Assert.AreEqual(45f, pitch, Eps);
        }

        [Test]
        public void 提前量_目標橫移時瞄準點前移()
        {
            Vector3 lead = BotMath.LeadTarget(Vector3.zero, new Vector3(0f, 0f, 10f), new Vector3(5f, 0f, 0f), 50f);
            Assert.Greater(lead.x, 0.9f);
            Assert.Less(lead.x, 1.1f);
        }

        [Test]
        public void 限速轉向_跨越正負一百八()
        {
            float next = BotMath.TurnTowards(170f, -170f, 100f, 0.1f);
            Assert.AreEqual(180f, Mathf.Repeat(next, 360f), Eps);
        }

        [Test]
        public void 角距()
        {
            Assert.AreEqual(5f, BotMath.AngularError(0f, 0f, 3f, 4f), Eps);
            Assert.AreEqual(2f, BotMath.AngularError(359f, 0f, 1f, 0f), Eps);
        }
    }
}

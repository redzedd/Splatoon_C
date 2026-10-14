using NUnit.Framework;
using SplatoonC.Core.Combat;
using UnityEngine;

namespace SplatoonC.Tests
{
    // S3 三狀態彈道(DESIGN_REFERENCE §3.1):以 Splattershot(2.266 u/F × 4F、進減速 1.493)逐幀手算對照
    public class BulletBallisticsTests
    {
        private static BulletMotion Splattershot => BulletMotion.ShooterFamily(2.266f, 4, 1.493f);

        [Test]
        public void 直線狀態等速無重力()
        {
            BulletMotion motion = Splattershot;
            BulletState state = BulletBallistics.Launch(Vector3.zero, Vector3.forward, motion);
            for (int i = 0; i < 4; i++)
            {
                BulletBallistics.Step(ref state, motion);
            }
            Assert.AreEqual(BulletPhase.Straight, state.Phase);
            Assert.AreEqual(4, state.Frame);
            Assert.AreEqual(9.064f, state.Position.z, 1e-4f, "4F × 2.266");
            Assert.AreEqual(0f, state.Position.y, 1e-6f, "直線狀態沒有重力");
            Assert.AreEqual(9.064f, state.Travelled, 1e-4f);
        }

        [Test]
        public void 仰角不影響直線速度()
        {
            BulletMotion motion = Splattershot;
            BulletState state = BulletBallistics.Launch(Vector3.zero, new Vector3(0f, 1f, 1f), motion);
            BulletBallistics.Step(ref state, motion);
            Assert.AreEqual(2.266f, state.Position.magnitude, 1e-4f);
        }

        [Test]
        public void 進入減速夾速度且第一幀就套阻力與重力()
        {
            BulletMotion motion = Splattershot;
            BulletState state = BulletBallistics.Launch(Vector3.zero, Vector3.forward, motion);
            for (int i = 0; i < 5; i++)
            {
                BulletBallistics.Step(ref state, motion);
            }
            Assert.AreEqual(BulletPhase.Brake, state.Phase);
            Assert.AreEqual(1.493f * 0.64f, state.Velocity.z, 1e-4f, "min(2.266, 1.493) × (1 − 0.36)");
            Assert.AreEqual(-0.07f, state.Velocity.y, 1e-5f);
            Assert.AreEqual(9.064f + 0.95552f, state.Position.z, 1e-3f);
        }

        [Test]
        public void 兩個下限同時觸底才轉自由落體()
        {
            BulletMotion motion = Splattershot;
            BulletState state = BulletBallistics.Launch(Vector3.zero, Vector3.forward, motion);
            for (int i = 0; i < 8; i++)
            {
                BulletBallistics.Step(ref state, motion);
            }
            // 第 8F:vY 已夾在 −0.15,但 XZ 0.2505 還在下限之上 → 仍是減速
            Assert.AreEqual(BulletPhase.Brake, state.Phase);
            Assert.AreEqual(-0.15f, state.Velocity.y, 1e-5f, "vY 夾到下限");
            BulletBallistics.Step(ref state, motion);
            // 第 9F:兩者都低於下限 → 夾到下限並轉 FREE
            Assert.AreEqual(BulletPhase.Free, state.Phase);
            Assert.AreEqual(9, state.Frame);
            Assert.AreEqual(0.2355f, new Vector2(state.Velocity.x, state.Velocity.z).magnitude, 1e-4f);
            Assert.AreEqual(-0.15f, state.Velocity.y, 1e-5f);
            Assert.AreEqual(11.508f, state.Position.z, 2e-3f, "手算:9.064 + .9555 + .6115 + .3914 + .2505 + .2355");
            Assert.AreEqual(-0.628f, state.Position.y, 2e-3f);
        }

        [Test]
        public void 自由落體收斂到終端落速()
        {
            BulletMotion motion = Splattershot;
            BulletState state = BulletBallistics.Launch(Vector3.zero, Vector3.forward, motion);
            for (int i = 0; i < 600; i++)
            {
                BulletBallistics.Step(ref state, motion);
            }
            Assert.AreEqual(-0.8f, BulletBallistics.TerminalFallSpeed(motion), 1e-5f, "−0.016 / 0.02");
            Assert.AreEqual(-0.8f, state.Velocity.y, 1e-3f);
        }

        [Test]
        public void 弓與潑桶的下限20與10等於跳過減速()
        {
            BulletMotion stringer = BulletMotion.StringerFamily(2.1f, 4);
            BulletState state = BulletBallistics.Launch(Vector3.zero, Vector3.forward, stringer);
            for (int i = 0; i < 5; i++)
            {
                BulletBallistics.Step(ref state, stringer);
            }
            Assert.AreEqual(BulletPhase.Free, state.Phase, "直線結束直接自由落體");
            Assert.AreEqual(2.1f * 0.76f, state.Velocity.z, 1e-4f, "第一幀就是自由落體阻力 0.24(沒有被夾到 20)");
            Assert.AreEqual(-0.15f, state.Velocity.y, 1e-5f, "自由落體重力 0.15");

            BulletMotion slosher = BulletMotion.SlosherFamily(1.76f, 0);
            BulletState slosh = BulletBallistics.Launch(Vector3.zero, new Vector3(0f, 0.15f, 1f), slosher);
            Assert.AreEqual(BulletPhase.Free, slosh.Phase, "直線 0 幀 → 發射即自由落體");
        }

        [Test]
        public void 滾筒沒有下限永遠留在減速()
        {
            BulletMotion roller = BulletMotion.RollerFamily(1.05f, 7);
            BulletState state = BulletBallistics.Launch(Vector3.zero, Vector3.forward, roller);
            for (int i = 0; i < 400; i++)
            {
                BulletBallistics.Step(ref state, roller);
            }
            Assert.AreEqual(BulletPhase.Brake, state.Phase);
            Assert.AreEqual(-0.4f, state.Velocity.y, 1e-3f, "−0.04 / 0.1");
        }

        [Test]
        public void 刮水刀橫波等速直線()
        {
            BulletMotion wave = BulletMotion.SplatanaFamily(3.3f, 4, 0f);
            BulletState state = BulletBallistics.Launch(Vector3.zero, Vector3.forward, wave);
            for (int i = 0; i < 8; i++)
            {
                BulletBallistics.Step(ref state, wave);
            }
            Assert.AreEqual(3.3f * 8f, state.Position.z, 1e-3f);
            Assert.AreEqual(0f, state.Position.y, 1e-6f);
        }

        [Test]
        public void 繼承速度加在直線段()
        {
            BulletMotion motion = Splattershot;
            BulletState state = BulletBallistics.Launch(Vector3.zero, Vector3.forward, motion, new Vector3(0.1f, 0f, 0f));
            BulletBallistics.Step(ref state, motion);
            Assert.AreEqual(0.1f, state.Position.x, 1e-5f);
            Assert.AreEqual(2.266f, state.Position.z, 1e-5f);
        }

        [Test]
        public void 有效射程公式對上Splattershot的11點56公尺()
        {
            float range = BulletBallistics.EffectiveRange(Splattershot, 8, 0.285f);
            Assert.AreEqual(11.56f, range, 0.01f);
        }

        [Test]
        public void 平地水平射擊約10到16幀落地()
        {
            bool landed = BulletBallistics.PredictFlatLanding(Splattershot, 1f, 0.2f, out int frames, out float distance);
            Assert.IsTrue(landed);
            Assert.AreEqual(11, frames, "離地 1 m 發射、地形半徑 0.2:第 11F 碰地");
            Assert.AreEqual(11.75f, distance, 0.02f);
            Assert.That(frames, Is.InRange(10, 16));
        }
    }
}

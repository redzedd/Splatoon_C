using NUnit.Framework;
using SplatoonC.Core.Combat;
using UnityEngine;

namespace SplatoonC.Tests
{
    // D2 大招的純邏輯:Ink Vac 吸引錐 / 容量換算、Crab Tank 連射間隔 / 步行 / 砲彈射程、Ultra Stamp 揮擊角度 / 丟出射程、
    // Tacticooler 供應圓柱 / 立起坡度、ModifierStack 的潛墨回血與免敵墨旗標。
    public class SpecialD2MathTests
    {
        // ── Ink Vac ──

        [Test]
        public void InkVac_錐長每幀加0點3334_45F到15()
        {
            Assert.AreEqual(0f, InkVacModel.LengthAt(0f), 1e-5f);
            Assert.AreEqual(10.002f, InkVacModel.LengthAt(30f), 1e-3f);
            Assert.AreEqual(15f, InkVacModel.LengthAt(45f), 1e-5f, "0.3334 × 45 = 15.003 → 夾在 15");
            Assert.AreEqual(15f, InkVacModel.LengthAt(200f), 1e-5f);
        }

        [Test]
        public void InkVac_半徑沿錐長0點8到3點3()
        {
            Assert.AreEqual(0.8f, InkVacModel.RadiusAt(0f), 1e-5f);
            Assert.AreEqual(3.3f, InkVacModel.RadiusAt(15f), 1e-5f);
            Assert.AreEqual(2.05f, InkVacModel.RadiusAt(7.5f), 1e-5f);
        }

        [Test]
        public void InkVac_吸引範圍_錐加槍口球()
        {
            Vector3 apex = Vector3.zero;
            Vector3 axis = Vector3.forward;
            // 錐內 / 錐外(10 m 處半徑 2.467)
            Assert.IsTrue(InkVacModel.Contains(apex, axis, 15f, new Vector3(2.4f, 0f, 10f)));
            Assert.IsFalse(InkVacModel.Contains(apex, axis, 15f, new Vector3(2.55f, 0f, 10f)));
            // 超過目前錐長就不算
            Assert.IsFalse(InkVacModel.Contains(apex, axis, 5f, new Vector3(0f, 0f, 6f)));
            // 槍口球(前方 1.0、半徑 1.2):噴嘴口正後方 0.1 還在球裡,0.5 就不在
            Assert.IsTrue(InkVacModel.Contains(apex, axis, 15f, new Vector3(0f, 0f, -0.1f)));
            Assert.IsFalse(InkVacModel.Contains(apex, axis, 15f, new Vector3(0f, 0f, -0.5f)));
            // 錐長 0 = 沒在吸
            Assert.IsFalse(InkVacModel.Contains(apex, axis, 0f, new Vector3(0f, 0f, 1f)));
        }

        [Test]
        public void InkVac_線段進入點()
        {
            // 從側面橫穿 8 m 處(半徑 2.133):進入點在 x = −2.133,取樣間距 ≤ 0.35
            bool entered = InkVacModel.SegmentEntry(Vector3.zero, Vector3.forward, 15f, new Vector3(-5f, 0f, 8f),
                Vector3.right, 10f, 0f, out float distance);
            Assert.IsTrue(entered);
            Assert.That(distance, Is.InRange(5f - 2.1333f - 1e-3f, 5f - 2.1333f + 0.35f));
            // 平行錐軸、在外面:沒碰到
            Assert.IsFalse(InkVacModel.SegmentEntry(Vector3.zero, Vector3.forward, 15f, new Vector3(6f, 0f, 0f),
                Vector3.forward, 20f, 0f, out _));
            // 起點就在範圍內:距離 0
            Assert.IsTrue(InkVacModel.SegmentEntry(Vector3.zero, Vector3.forward, 15f, new Vector3(0f, 0f, 9f),
                Vector3.back, 5f, 0f, out float inside));
            Assert.AreEqual(0f, inside, 1e-5f);
        }

        [Test]
        public void InkVac_容量換算_爆炸半徑走速初速()
        {
            Assert.AreEqual(0.5f, InkVacModel.Fill01(550f), 1e-5f);
            Assert.AreEqual(1f, InkVacModel.Fill01(2000f), 1e-5f);
            Assert.AreEqual(6f, InkVacModel.BlastRadius(0f), 1e-5f);
            Assert.AreEqual(11f, InkVacModel.BlastRadius(1f), 1e-5f);
            Assert.AreEqual(0.09f, InkVacModel.MoveSpeed(0f), 1e-6f);
            Assert.AreEqual(0.08f, InkVacModel.MoveSpeed(0.5f), 1e-6f);
            Assert.AreEqual(0.07f, InkVacModel.MoveSpeed(1f), 1e-6f);
            Assert.AreEqual(0.55f, InkVacModel.ShotSpeed(0f), 1e-6f);
            Assert.AreEqual(0.7f, InkVacModel.ShotSpeed(1f), 1e-6f);
        }

        [Test]
        public void InkVac_反擊彈_吸越多飛越遠且幾乎不掉速()
        {
            float empty = FlatRange(InkVacModel.ShotMotion(0f), 1.05f, InkVacModel.ShotSpeedY);
            float full = FlatRange(InkVacModel.ShotMotion(1f), 1.05f, InkVacModel.ShotSpeedY);
            Assert.That(empty, Is.InRange(23f, 30f), "JP wiki 平射約 5.7 條 ≈ 28 u");
            Assert.Greater(full, empty + 4f);
        }

        // ── Crab Tank ──

        [Test]
        public void CrabTank_連射間隔12到4_90F到最快()
        {
            Assert.AreEqual(12f, CrabTankModel.TurretRepeatFrames(0f), 1e-5f);
            Assert.AreEqual(8f, CrabTankModel.TurretRepeatFrames(45f), 1e-5f);
            Assert.AreEqual(4f, CrabTankModel.TurretRepeatFrames(90f), 1e-5f);
            Assert.AreEqual(4f, CrabTankModel.TurretRepeatFrames(300f), 1e-5f);
        }

        [Test]
        public void CrabTank_步行橫移快前後慢()
        {
            Vector2 lateral = CrabTankModel.WalkVelocity(new Vector2(1f, 0f));
            Vector2 forward = CrabTankModel.WalkVelocity(new Vector2(0f, -1f));
            Assert.AreEqual(0.075f, lateral.x, 1e-6f);
            Assert.AreEqual(-0.015f, forward.y, 1e-6f);
            Vector2 diagonal = CrabTankModel.WalkVelocity(new Vector2(1f, 1f));
            Assert.AreEqual(0.075f * 0.70710678f, diagonal.x, 1e-5f, "搖桿長度夾到 1");
        }

        [Test]
        public void CrabTank_砲彈平射約28m()
        {
            BulletMotion motion = CrabTankModel.CannonMotion;
            Vector3 dir = Quaternion.Euler(-4f, 0f, 0f) * Vector3.forward;
            float range = FlatRange(motion, 1.3f, 0f, dir);
            Assert.That(range, Is.InRange(25f, 31f), "JP wiki 直撃射程 5.6 條 ≈ 28 u");
        }

        // ── Ultra Stamp ──

        [Test]
        public void UltraStamp_揮擊先往後舉再砸到地面()
        {
            Assert.AreEqual(10f, UltraStampModel.SwingAngle(0f), 1e-4f);
            Assert.AreEqual(-25f, UltraStampModel.SwingAngle(0.65f), 1e-4f);
            Assert.AreEqual(UltraStampModel.ImpactAngle, UltraStampModel.SwingAngle(1f), 1e-4f);
            Assert.Less(UltraStampModel.SwingAngle(0.3f), UltraStampModel.SwingAngle(0f));
            Assert.Greater(UltraStampModel.SwingAngle(0.9f), UltraStampModel.SwingAngle(0.7f));
        }

        [Test]
        public void UltraStamp_丟出平射約25m()
        {
            float range = FlatRange(UltraStampModel.ThrowMotion, 2.3f, UltraStampModel.ThrowSpeedY);
            Assert.That(range, Is.InRange(22f, 28f), "JP wiki 平射約 5.0 條 = 25 u");
        }

        // ── Tacticooler ──

        [Test]
        public void Tacticooler_供應圓柱_水平7往上3往下0()
        {
            Vector3 stand = Vector3.zero;
            const float body = 0.35f;
            const float height = 1.65f;
            Assert.IsTrue(TacticoolerModel.InServeArea(stand, new Vector3(7.3f, 0f, 0f), body, height));
            Assert.IsFalse(TacticoolerModel.InServeArea(stand, new Vector3(7.4f, 0f, 0f), body, height));
            Assert.IsTrue(TacticoolerModel.InServeArea(stand, new Vector3(0f, 3f, 2f), body, height));
            Assert.IsFalse(TacticoolerModel.InServeArea(stand, new Vector3(0f, 3.05f, 2f), body, height));
            // 比機器低:頭(腳 + 1.65)碰到底面就算
            Assert.IsTrue(TacticoolerModel.InServeArea(stand, new Vector3(0f, -1.6f, 2f), body, height));
            Assert.IsFalse(TacticoolerModel.InServeArea(stand, new Vector3(0f, -1.7f, 2f), body, height));
        }

        [Test]
        public void Tacticooler_坡度30度以內才立起()
        {
            Assert.IsTrue(TacticoolerModel.CanPlaceOn(Vector3.up));
            Assert.IsTrue(TacticoolerModel.CanPlaceOn(Quaternion.Euler(29f, 0f, 0f) * Vector3.up));
            Assert.IsFalse(TacticoolerModel.CanPlaceOn(Quaternion.Euler(31f, 0f, 0f) * Vector3.up));
        }

        // ── ModifierStack:潛墨回血 / 免敵墨 ──

        [Test]
        public void Modifier_潛墨回血與免敵墨_旗標取OR_移除即恢復()
        {
            var stack = new ModifierStack();
            object ball = new object();
            object other = new object();
            Assert.IsFalse(stack.SwimRegen);
            Assert.IsFalse(stack.NoSlipDamage);
            stack.Add(ball, ModifierSpec.None.WithSwimRegen().WithoutSlipDamage(), 0f, 0f);
            stack.Add(other, ModifierSpec.None.WithRunSpeed(0.5f), 0f, 0f);
            Assert.IsTrue(stack.SwimRegen);
            Assert.IsTrue(stack.NoSlipDamage);
            stack.Remove(ball);
            Assert.IsFalse(stack.SwimRegen);
            Assert.IsFalse(stack.NoSlipDamage);
            Assert.AreEqual(0.5f, stack.RunSpeed, 1e-5f);
        }

        // 平地(y = 0)平射:從 height 高度沿 dir 發射(預設水平),加上每幀 liftPerFrame 的向上初速,球心落到 0 時的水平距離
        private static float FlatRange(BulletMotion motion, float height, float liftPerFrame, Vector3? direction = null)
        {
            Vector3 dir = direction ?? Vector3.forward;
            BulletState state = BulletBallistics.Launch(new Vector3(0f, height, 0f), dir, motion, Vector3.up * liftPerFrame);
            for (int i = 0; i < 1000; i++)
            {
                Vector3 previous = state.Position;
                BulletBallistics.Step(ref state, motion);
                if (state.Position.y <= 0f)
                {
                    float t = previous.y / Mathf.Max(1e-6f, previous.y - state.Position.y);
                    Vector3 hit = Vector3.Lerp(previous, state.Position, t);
                    return new Vector2(hit.x, hit.z).magnitude;
                }
            }
            return float.PositiveInfinity;
        }
    }
}

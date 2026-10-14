using NUnit.Framework;
using SplatoonC.Core.Combat;
using SplatoonC.Core.Locomotion;
using UnityEngine;

namespace SplatoonC.Tests
{
    // A4 超級跳時間軸與弧線(DESIGN_REFERENCE §2.9;research/07 §6.1:人形 +18F)
    public class SuperJumpModelTests
    {
        private static readonly SuperJumpParams P = SuperJumpParams.Splatoon3;

        [Test]
        public void 時間軸_烏賊218F_人形236F()
        {
            Assert.AreEqual(80, SuperJumpModel.ChargeFramesAt0Ap(P, false));
            Assert.AreEqual(98, SuperJumpModel.ChargeFramesAt0Ap(P, true));
            Assert.AreEqual(138, P.FlightFrames);
            Assert.AreEqual(218, SuperJumpModel.ChargeFramesAt0Ap(P, false) + P.FlightFrames);
            Assert.AreEqual(236, SuperJumpModel.ChargeFramesAt0Ap(P, true) + P.FlightFrames);
            // 蓄力秒數:0 AP 80F(QSJ 曲線的 Low),人形再 + 18F
            float baseCharge = AbilityCurve.SuperJumpChargeFrames.Evaluate(0f) / 60f;
            Assert.AreEqual(80f / 60f, SuperJumpModel.ChargeSeconds(P, baseCharge, false), 1e-4f);
            Assert.AreEqual(98f / 60f, SuperJumpModel.ChargeSeconds(P, baseCharge, true), 1e-4f);
            // QSJ 57 AP:20F 蓄力 + 96.6F 飛行(research/03 §2.9:116F)
            float qsj = AbilityCurve.SuperJumpChargeFrames.Evaluate(57f);
            float flight = AbilityCurve.SuperJumpFlightFrames.Evaluate(57f);
            Assert.AreEqual(116.6f, qsj + flight, 0.2f);
        }

        [Test]
        public void 弧線_起訖點正確_中點到頂點_兩端垂直起落()
        {
            var start = new Vector3(0f, 0f, 0f);
            var end = new Vector3(0f, 2f, 60f);
            float apex = SuperJumpModel.ApexHeight(P, start, end);
            // 10 + 0.3 × 60 = 28(上限 28)
            Assert.AreEqual(28f, apex, 1e-4f);
            Assert.That(Vector3.Distance(start, SuperJumpModel.FlightPosition(start, end, apex, 0f)), Is.LessThan(1e-4f));
            Assert.That(Vector3.Distance(end, SuperJumpModel.FlightPosition(start, end, apex, 1f)), Is.LessThan(1e-4f));
            Vector3 mid = SuperJumpModel.FlightPosition(start, end, apex, 0.5f);
            Assert.AreEqual(2f + 28f, mid.y, 1e-3f, "中點 = 起訖較高者 + 頂點高度");
            Assert.AreEqual(30f, mid.z, 1e-3f);
            // 起飛段幾乎垂直上升:5% 時間時水平移動遠小於垂直
            Vector3 early = SuperJumpModel.FlightPosition(start, end, apex, 0.05f);
            Assert.Greater(early.y - start.y, 4f * Mathf.Abs(early.z - start.z));
            // 全程不低於起訖較低者(穿牆飛越,不會鑽地)
            for (int i = 0; i <= 20; i++)
            {
                Assert.GreaterOrEqual(SuperJumpModel.FlightPosition(start, end, apex, i / 20f).y, -1e-4f);
            }
            // 近距離也至少 10 m 高
            Assert.AreEqual(10f + 0.3f * 5f, SuperJumpModel.ApexHeight(P, start, new Vector3(0f, 0f, 5f)), 1e-4f);
        }

        [Test]
        public void 落點標記_墨水隨飛行流乾_落地前開槍窗口()
        {
            float flight = 138f / 60f;
            Assert.AreEqual(1f, SuperJumpModel.MarkerFill(0f, flight), 1e-6f);
            Assert.AreEqual(0.5f, SuperJumpModel.MarkerFill(flight * 0.5f, flight), 1e-5f);
            Assert.AreEqual(0f, SuperJumpModel.MarkerFill(flight, flight), 1e-6f);
            Assert.IsFalse(SuperJumpModel.InFireWindow(P, flight - 13f / 60f, flight));
            Assert.IsTrue(SuperJumpModel.InFireWindow(P, flight - 12f / 60f, flight));
            Assert.IsTrue(SuperJumpModel.InFireWindow(P, flight, flight));
        }
    }
}

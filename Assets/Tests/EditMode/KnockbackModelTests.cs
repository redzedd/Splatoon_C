using NUnit.Framework;
using SplatoonC.Core.Combat;
using UnityEngine;

namespace SplatoonC.Tests
{
    // 爆炸擊退 [EST] 模型(research 07 §2.7):衝量 ∝ Accel·(1 − d/Distance)+ 小向上;700 的炸彈在 3 m 推 1.5–2 m
    public class KnockbackModelTests
    {
        [Test]
        public void 範圍外或強度0沒有擊退()
        {
            Assert.AreEqual(Vector3.zero, KnockbackModel.Impulse(Vector3.zero, new Vector3(12f, 0f, 0f), 700f, 12f));
            Assert.AreEqual(Vector3.zero, KnockbackModel.Impulse(Vector3.zero, new Vector3(3f, 0f, 0f), 0f, 12f));
        }

        [Test]
        public void 方向由爆心向外且帶小向上()
        {
            Vector3 impulse = KnockbackModel.Impulse(Vector3.zero, new Vector3(0f, 0.3f, 3f), 700f, 12f);
            Assert.Greater(impulse.z, 0f);
            Assert.AreEqual(0f, impulse.x, 1e-5f);
            Assert.Greater(impulse.y, 0f);
            Assert.Less(impulse.y, impulse.z * 0.2f, "向上只是一點偏移");
        }

        [Test]
        public void 強度隨距離線性遞減()
        {
            float at3 = Horizontal(KnockbackModel.Impulse(Vector3.zero, new Vector3(3f, 0f, 0f), 700f, 12f));
            float at6 = Horizontal(KnockbackModel.Impulse(Vector3.zero, new Vector3(6f, 0f, 0f), 700f, 12f));
            Assert.AreEqual(0.75f / 0.5f, at3 / at6, 1e-3f, "(1 − 3/12) : (1 − 6/12)");
        }

        [Test]
        public void 必殺炸彈3公尺推開約1點5到2公尺()
        {
            Vector3 impulse = KnockbackModel.Impulse(Vector3.zero, new Vector3(3f, 0f, 0f), 700f, 12f);
            float distance = KnockbackModel.PredictFlatDistance(impulse, -10.8f);
            Assert.That(distance, Is.InRange(1.5f, 2.0f), $"推 {distance:0.00} m");
        }

        [Test]
        public void 減速不會反向()
        {
            Vector3 velocity = new Vector3(1f, 0f, 0f);
            for (int i = 0; i < 10; i++)
            {
                velocity = KnockbackModel.DecayHorizontal(velocity, true, 1f / 60f);
                Assert.GreaterOrEqual(velocity.x, 0f);
            }
            Assert.AreEqual(Vector3.zero, velocity);
        }

        private static float Horizontal(Vector3 v)
        {
            return new Vector2(v.x, v.z).magnitude;
        }
    }
}

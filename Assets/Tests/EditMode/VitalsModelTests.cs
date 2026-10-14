using NUnit.Framework;
using SplatoonC.Core.Combat;

namespace SplatoonC.Tests
{
    public class VitalsModelTests
    {
        private static readonly VitalsSettings S = VitalsSettings.Splatoon3;

        [Test]
        public void 三發三十六擊倒_兩發不會()
        {
            var v = VitalsModel.Create();
            Assert.IsFalse(v.ApplyHit(36f, 0f, S, out _));
            Assert.IsFalse(v.ApplyHit(36f, 0.1f, S, out _));
            Assert.AreEqual(28f, v.Hp(S), 1e-3f);
            Assert.IsTrue(v.ApplyHit(36f, 0.2f, S, out float dealt));
            Assert.AreEqual(28f, dealt, 1e-3f, "擊倒那一下只記到滿為止");
            Assert.IsTrue(v.IsDead);
            Assert.IsFalse(v.ApplyHit(36f, 0.3f, S, out _), "已死不重複擊倒");
        }

        [Test]
        public void 傷害取整到零點一_九十九點九不秒殺()
        {
            var v = VitalsModel.Create();
            Assert.IsFalse(v.ApplyHit(99.99f, 0f, S, out float dealt));
            Assert.AreEqual(99.9f, dealt, 1e-3f);
            Assert.IsTrue(v.ApplyHit(0.1f, 0.1f, S, out _));
        }

        [Test]
        public void 敵墨持續傷害到四十為止_永不致死()
        {
            var v = VitalsModel.Create();
            for (int i = 0; i < 600; i++)
            {
                v.Tick(i / 60f, 1f / 60f, false, true, S);
            }
            Assert.AreEqual(40f, v.Damage, 1e-3f);
            Assert.IsFalse(v.IsDead);

            var hurt = VitalsModel.Create();
            hurt.ApplyHit(90f, 0f, S, out _);
            for (int i = 0; i < 120; i++)
            {
                hurt.Tick(i / 60f, 1f / 60f, false, true, S);
            }
            Assert.AreEqual(90f, hurt.Damage, 1e-3f, "已超過上限的傷害不再被敵墨推高");
            Assert.IsFalse(hurt.IsDead);
        }

        [Test]
        public void 回血延遲一秒_潛墨快八倍()
        {
            var v = VitalsModel.Create();
            v.ApplyHit(50f, 0f, S, out _);
            v.Tick(0.5f, 0.5f, true, false, S);
            Assert.AreEqual(50f, v.Damage, 1e-3f, "一秒內不回血");
            v.Tick(1.2f, 0.2f, true, false, S);
            Assert.AreEqual(30f, v.Damage, 1e-3f, "潛墨 100/s × 0.2s");

            var exposed = VitalsModel.Create();
            exposed.ApplyHit(50f, 0f, S, out _);
            exposed.Tick(2f, 1f, false, false, S);
            Assert.AreEqual(50f - 12.6f, exposed.Damage, 1e-3f);
        }

        [Test]
        public void 踩到敵墨重置回血延遲()
        {
            var v = VitalsModel.Create();
            v.ApplyHit(50f, 0f, S, out _);
            v.Tick(2f, 1f / 60f, false, true, S);
            float after = v.Damage;
            v.Tick(2.5f, 0.5f, true, false, S);
            Assert.AreEqual(after, v.Damage, 1e-3f, "離開敵墨後仍要再等一秒");
        }

        [Test]
        public void 護甲吸收_溢出漏到本體_超過一百穿甲()
        {
            var v = VitalsModel.Create();
            v.GrantArmor(30f, 5f);
            Assert.IsFalse(v.ApplyHit(20f, 0f, S, out float dealt));
            Assert.AreEqual(0f, dealt, 1e-3f);
            Assert.AreEqual(10f, v.Armor, 1e-3f);
            v.ApplyHit(36f, 0.1f, S, out dealt);
            Assert.AreEqual(26f, dealt, 1e-3f, "護甲剩 10,溢出 26 打本體");

            var pierce = VitalsModel.Create();
            pierce.GrantArmor(30f, 5f);
            Assert.IsFalse(pierce.ApplyHit(180f, 0f, S, out dealt));
            Assert.AreEqual(80f, dealt, 1e-3f, "單發 180 穿甲,本體吃 80");
        }

        [Test]
        public void 護甲過期不再吸收()
        {
            var v = VitalsModel.Create();
            v.GrantArmor(30f, 1f);
            v.ApplyHit(20f, 2f, S, out float dealt);
            Assert.AreEqual(20f, dealt, 1e-3f);
        }
    }
}

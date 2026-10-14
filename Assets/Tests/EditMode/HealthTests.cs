using NUnit.Framework;
using SplatoonC.Core.Combat;

namespace SplatoonC.Tests
{
    public class HealthTests
    {
        [Test]
        public void 建立即滿血()
        {
            var health = Health.Create(100f);
            Assert.AreEqual(100f, health.Current);
            Assert.IsFalse(health.IsDead);
        }

        [Test]
        public void 扣血未死回傳假()
        {
            var health = Health.Create(100f);
            Assert.IsFalse(health.ApplyDamage(30f));
            Assert.AreEqual(70f, health.Current);
        }

        [Test]
        public void 致死那一發回傳真且血量夾零()
        {
            var health = Health.Create(100f);
            health.ApplyDamage(90f);
            Assert.IsTrue(health.ApplyDamage(30f), "90 + 30 溢傷的這發要判定致死");
            Assert.AreEqual(0f, health.Current);
            Assert.IsTrue(health.IsDead);
        }

        [Test]
        public void 已死再打不重複計殺()
        {
            var health = Health.Create(100f);
            health.ApplyDamage(100f);
            Assert.IsFalse(health.ApplyDamage(30f));
        }

        [Test]
        public void 重置回滿()
        {
            var health = Health.Create(100f);
            health.ApplyDamage(100f);
            health.ResetFull();
            Assert.AreEqual(100f, health.Current);
            Assert.IsFalse(health.IsDead);
        }
    }
}

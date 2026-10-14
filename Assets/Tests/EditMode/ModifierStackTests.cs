using NUnit.Framework;
using SplatoonC.Core.Combat;

namespace SplatoonC.Tests
{
    public class ModifierStackTests
    {
        private readonly object _mist = new object();
        private readonly object _cooler = new object();
        private readonly object _other = new object();

        [Test]
        public void 預設Spec_不影響任何值()
        {
            ModifierSpec spec = default;
            Assert.AreEqual(1f, spec.RunSpeed);
            Assert.AreEqual(1f, spec.SwimSpeed);
            Assert.AreEqual(1f, spec.InkRefill);
            Assert.AreEqual(1f, spec.InkCost);
            Assert.AreEqual(1f, spec.JumpHeight);
            Assert.AreEqual(1f, spec.RespawnTime);
            Assert.AreEqual(1f, spec.SpecialCharge);
            Assert.AreEqual(0f, spec.InkDrainPerSecond);
            Assert.IsFalse(spec.NoHealthRegen);
            Assert.IsFalse(spec.Persistent);
            Assert.IsFalse(spec.Abilities.Any);

            var stack = new ModifierStack();
            stack.Add(_other, spec, 0f, 5f);
            Assert.AreEqual(1f, stack.RunSpeed);
            Assert.AreEqual(1f, stack.InkRefill);
            Assert.AreEqual(StatusFlags.None, stack.Status);
        }

        [Test]
        public void 不同來源_倍率相乘_旗標取OR()
        {
            var stack = new ModifierStack();
            stack.Add(_mist, ModifierSpec.None.WithRunSpeed(0.5f).WithStatus(StatusFlags.Slowed), 0f, 5f);
            stack.Add(_other, ModifierSpec.None.WithRunSpeed(0.8f).WithInkCost(0.67f).WithoutHealthRegen(), 0f, 5f);
            Assert.AreEqual(0.4f, stack.RunSpeed, 1e-5f);
            Assert.AreEqual(0.67f, stack.InkCost, 1e-5f);
            Assert.IsTrue(stack.NoHealthRegen);
            Assert.AreEqual(StatusFlags.Slowed, stack.Status);
            Assert.AreEqual(2, stack.Count);
        }

        [Test]
        public void 同來源再加_刷新而非疊加()
        {
            var stack = new ModifierStack();
            stack.Add(_mist, ModifierSpec.None.WithRunSpeed(0.5f), 0f, 1f);
            stack.Add(_mist, ModifierSpec.None.WithRunSpeed(0.5f), 0.8f, 1f);
            Assert.AreEqual(1, stack.Count);
            Assert.AreEqual(0.5f, stack.RunSpeed, 1e-5f, "同一團毒霧不會變成 0.25");
            Assert.AreEqual(1f, stack.Remaining(_mist, 0.8f), 1e-5f, "到期時間從最後一次重設");
            Assert.AreEqual(0, stack.Tick(1.5f));
            Assert.AreEqual(1, stack.Tick(1.85f));
            Assert.AreEqual(1f, stack.RunSpeed);
        }

        [Test]
        public void 到期與永久()
        {
            var stack = new ModifierStack();
            stack.Add(_mist, ModifierSpec.None.WithInkRefill(0f), 10f, 2f);
            stack.Add(_other, ModifierSpec.None.WithJumpHeight(0.5f), 10f, 0f);
            Assert.AreEqual(0, stack.Tick(11.99f));
            Assert.AreEqual(0f, stack.InkRefill);
            Assert.AreEqual(1, stack.Tick(12f), "剛好到期即移除");
            Assert.AreEqual(1f, stack.InkRefill);
            Assert.AreEqual(float.PositiveInfinity, stack.Remaining(_other, 1000f));
            Assert.AreEqual(0, stack.Tick(99999f), "seconds ≤ 0 = 直到移除");
            Assert.IsTrue(stack.Remove(_other));
            Assert.IsFalse(stack.Remove(_other));
            Assert.AreEqual(1f, stack.JumpHeight);
            Assert.AreEqual(0, stack.Count);
        }

        [Test]
        public void 死亡清除_保留Persistent()
        {
            var stack = new ModifierStack();
            stack.Add(_cooler, ModifierSpec.Tacticooler, 0f, ModifierSpec.TacticoolerSeconds);
            stack.Add(_other, ModifierSpec.None.WithSpecialCharge(1.2f).AsPersistent(), 0f, 0f);
            Assert.AreEqual(1, stack.ClearNonPersistent());
            Assert.IsFalse(stack.Contains(_cooler));
            Assert.IsTrue(stack.Contains(_other));
            Assert.AreEqual(0, stack.AbilityPoints(Ability.RunSpeedUp));
            Assert.AreEqual(1.2f, stack.SpecialCharge, 1e-5f);
        }

        [Test]
        public void AP_各來源取最大值_上限57()
        {
            var stack = new ModifierStack();
            stack.Add(_cooler, ModifierSpec.Tacticooler, 0f, 17f);
            stack.Add(_other, ModifierSpec.None.WithAbility(Ability.RunSpeedUp, 10).WithAbility(Ability.InkRecoveryUp, 70), 0f, 17f);
            Assert.AreEqual(29, stack.AbilityPoints(Ability.RunSpeedUp), "能量站 29 與 10 取高者,不相加");
            Assert.AreEqual(29, stack.AbilityPoints(Ability.SwimSpeedUp));
            Assert.AreEqual(57, stack.AbilityPoints(Ability.QuickRespawn));
            Assert.AreEqual(57, stack.AbilityPoints(Ability.SpecialSaver));
            Assert.AreEqual(57, stack.AbilityPoints(Ability.InkResistanceUp));
            Assert.AreEqual(57, stack.AbilityPoints(Ability.IntensifyAction));
            Assert.AreEqual(57, stack.AbilityPoints(Ability.QuickSuperJump));
            Assert.AreEqual(57, stack.AbilityPoints(Ability.InkRecoveryUp), "超過 57 截斷");
            Assert.AreEqual(0, stack.AbilityPoints(Ability.SpecialChargeUp));
            Assert.AreEqual(StatusFlags.Buffed, stack.Status);
        }

        [Test]
        public void 扣墨_各來源上限_刷新保留已扣額度()
        {
            var stack = new ModifierStack();
            stack.Add(_mist, ModifierSpec.None.WithInkDrain(0.08f, 0.4f), 0f, 1f);
            float total = 0f;
            for (int i = 0; i < 600; i++)
            {
                total += stack.ConsumeDrain(1f / 60f);
            }
            Assert.AreEqual(0.4f, total, 1e-4f, "10 秒 × 8%/s 但上限 40%");
            Assert.AreEqual(0f, stack.InkDrainPerSecond, 1e-6f, "額度用完不再計入");
            stack.Add(_mist, ModifierSpec.None.WithInkDrain(0.08f, 0.4f), 5f, 1f);
            Assert.AreEqual(0f, stack.ConsumeDrain(1f), 1e-6f, "同來源刷新不重置額度");

            stack.Add(_other, ModifierSpec.ZipcasterInk, 5f, 0f);
            Assert.AreEqual(0.15f, stack.ConsumeDrain(1f), 1e-5f, "無上限的來源照扣");
            Assert.AreEqual(0.67f, stack.InkCost, 1e-5f);
            Assert.AreEqual(0f, stack.InkRefill, 1e-6f);
        }

        [Test]
        public void 版本號_每次變動遞增()
        {
            var stack = new ModifierStack();
            int v0 = stack.Version;
            stack.Add(_mist, ModifierSpec.ToxicMist, 0f, 1f);
            int v1 = stack.Version;
            stack.Tick(0.5f);
            Assert.AreEqual(v1, stack.Version, "沒到期不變");
            stack.Tick(1f);
            Assert.Greater(stack.Version, v1);
            Assert.Greater(v1, v0);
            stack.Clear();
            Assert.AreEqual(0, stack.Count);
        }

        [Test]
        public void 預設效果_毒霧數值()
        {
            ModifierSpec mist = ModifierSpec.ToxicMist;
            Assert.AreEqual(0.5f, mist.RunSpeed, 1e-6f);
            Assert.AreEqual(0.5f, mist.SwimSpeed, 1e-6f);
            Assert.AreEqual(0f, mist.InkRefill, 1e-6f);
            Assert.AreEqual(0.4f, mist.InkDrainLimit, 1e-6f);
            Assert.AreEqual(StatusFlags.Slowed, mist.Status);
            Assert.AreEqual(1020f / 60f, ModifierSpec.TacticoolerSeconds, 1e-5f);
        }
    }
}

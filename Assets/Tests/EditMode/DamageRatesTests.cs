using System;
using NUnit.Framework;
using SplatoonC.Core.Combat;

namespace SplatoonC.Tests
{
    // 對物件傷害倍率(DESIGN_REFERENCE §3.6;datamine spl__DamageRateInfoConfig)
    public class DamageRatesTests
    {
        [Test]
        public void 表格抽查()
        {
            Assert.AreEqual(2.0f, DamageRates.Get(AttackClass.Blaster, TargetClass.SplashWall), 1e-5f);
            Assert.AreEqual(6.0f, DamageRates.Get(AttackClass.Trizooka, TargetClass.SplashWall), 1e-5f);
            Assert.AreEqual(2.2f, DamageRates.Get(AttackClass.LunaBlaster, TargetClass.SplashWall), 1e-5f);
            Assert.AreEqual(3.6f, DamageRates.Get(AttackClass.LunaBlaster, TargetClass.BrellaCanopy), 1e-5f);
            Assert.AreEqual(0.7f, DamageRates.Get(AttackClass.Shooter, TargetClass.BrellaCanopy), 1e-5f);
            Assert.AreEqual(1.1f, DamageRates.Get(AttackClass.HeavyShooter, TargetClass.SplashWall), 1e-5f);
            Assert.AreEqual(2.64f, DamageRates.Get(AttackClass.RollerFlick, TargetClass.SplashWall), 1e-5f);
            Assert.AreEqual(100f, DamageRates.Get(AttackClass.RollerContact, TargetClass.Sprinkler), 1e-5f);
            Assert.AreEqual(0.5f, DamageRates.Get(AttackClass.Bomb, TargetClass.BubblerWeakPoint), 1e-5f);
            Assert.AreEqual(3.0f, DamageRates.Get(AttackClass.Inkstrike, TargetClass.CrabTank), 1e-5f);
            Assert.AreEqual(2.4f, DamageRates.Get(AttackClass.Blaster, TargetClass.Sprinkler), 1e-5f);
            Assert.AreEqual(4.8f, DamageRates.Get(AttackClass.SplatanaWave, TargetClass.SplashWall), 1e-5f);
        }

        [Test]
        public void 表中沒有的格子是1()
        {
            Assert.AreEqual(1f, DamageRates.Get(AttackClass.Shooter, TargetClass.SplashWall));
            Assert.AreEqual(1f, DamageRates.Get(AttackClass.Shooter, TargetClass.Sprinkler));
            Assert.AreEqual(1f, DamageRates.Get(AttackClass.Other, TargetClass.CrabTank));
            Assert.AreEqual(1f, DamageRates.Get(AttackClass.Special, TargetClass.SplashWall));
        }

        [Test]
        public void 對玩家一律1()
        {
            foreach (AttackClass attack in Enum.GetValues(typeof(AttackClass)))
            {
                Assert.AreEqual(1f, DamageRates.Get(attack, TargetClass.Player), $"{attack} → Player");
            }
        }

        [Test]
        public void InkStorm雨對所有物件為0()
        {
            foreach (TargetClass target in Enum.GetValues(typeof(TargetClass)))
            {
                if (target == TargetClass.Player || target == TargetClass.Decoy)
                {
                    continue;
                }
                Assert.AreEqual(0f, DamageRates.Get(AttackClass.InkStorm, target), $"InkStorm → {target}");
            }
        }

        [Test]
        public void 套倍率後取整到零點一()
        {
            Assert.AreEqual(25.2f, DamageRates.Apply(36f, AttackClass.Shooter, TargetClass.BrellaCanopy), 1e-4f);
            Assert.AreEqual(72f, DamageRates.Apply(30f, AttackClass.Blaster, TargetClass.Sprinkler), 1e-4f);
            Assert.AreEqual(360f, DamageRates.Apply(180f, AttackClass.Bomb, TargetClass.Sprinkler), 1e-4f);
            Assert.AreEqual(33.3f, DamageRates.Apply(33.33f, AttackClass.Shooter, TargetClass.Player), 1e-4f);
        }
    }
}

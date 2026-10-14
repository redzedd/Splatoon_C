using NUnit.Framework;
using SplatoonC.Core.Combat;

namespace SplatoonC.Tests
{
    // 一次攻擊的命中去重與上限(§3.1:同一揮/潑/傘擊對同一目標只算一次;傘每次開火每目標上限 81)
    public class AttackLedgerTests
    {
        private readonly object _targetA = new object();
        private readonly object _targetB = new object();

        [Test]
        public void 攻擊編號0不去重()
        {
            var ledger = new AttackLedger();
            for (int i = 0; i < 5; i++)
            {
                Assert.AreEqual(36f, ledger.Register(0, _targetA, 36f));
            }
        }

        [Test]
        public void 同一次攻擊同一目標只吃第一發()
        {
            var ledger = new AttackLedger();
            int attack = ledger.NewAttackId();
            Assert.AreEqual(36f, ledger.Register(attack, _targetA, 36f));
            Assert.AreEqual(0f, ledger.Register(attack, _targetA, 36f));
            Assert.AreEqual(0f, ledger.Register(attack, _targetA, 99f));
            Assert.AreEqual(36f, ledger.Register(attack, _targetB, 36f), "不同目標各算一次");
            int next = ledger.NewAttackId();
            Assert.AreNotEqual(attack, next);
            Assert.AreEqual(36f, ledger.Register(next, _targetA, 36f), "下一次攻擊重新計算");
            Assert.IsTrue(ledger.HasHit(attack, _targetA));
        }

        [Test]
        public void 傘的彈丸累加到上限81()
        {
            var ledger = new AttackLedger();
            int attack = ledger.NewAttackId();
            float total = 0f;
            for (int i = 0; i < 11; i++)
            {
                total += ledger.Register(attack, _targetA, 16.2f, 81f);
            }
            Assert.AreEqual(81f, total, 1e-4f);
            Assert.AreEqual(81f, ledger.TotalFor(attack, _targetA), 1e-4f);
        }

        [Test]
        public void 攻擊編號永不為0()
        {
            var ledger = new AttackLedger();
            for (int i = 0; i < 1000; i++)
            {
                Assert.AreNotEqual(0, ledger.NewAttackId());
            }
        }

        [Test]
        public void 容量滿了覆蓋最舊的帳目()
        {
            var ledger = new AttackLedger(4);
            int first = ledger.NewAttackId();
            ledger.Register(first, _targetA, 10f);
            for (int i = 0; i < 4; i++)
            {
                ledger.Register(ledger.NewAttackId(), _targetB, 10f);
            }
            Assert.IsFalse(ledger.HasHit(first, _targetA), "最舊的一筆被覆蓋");
        }
    }
}

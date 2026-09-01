using NUnit.Framework;
using SplatoonC.Core.Combat;

namespace SplatoonC.Tests.EditMode
{
    public sealed class InkRefillGateTests
    {
        [Test]
        public void 未開火時可回墨()
        {
            var gate = new InkRefillGate();

            Assert.IsTrue(gate.Evaluate(false, 0f, 0.5f));
        }

        [Test]
        public void 按住扳機期間不回墨()
        {
            var gate = new InkRefillGate();

            Assert.IsFalse(gate.Evaluate(true, 1f, 0.5f));
            Assert.IsFalse(gate.Evaluate(true, 2f, 0.5f));
        }

        [Test]
        public void 放開後延遲內仍不回墨()
        {
            var gate = new InkRefillGate();
            gate.Evaluate(true, 1f, 0.5f);

            Assert.IsFalse(gate.Evaluate(false, 1.2f, 0.5f));
            Assert.IsFalse(gate.Evaluate(false, 1.49f, 0.5f));
        }

        [Test]
        public void 放開超過延遲後恢復回墨()
        {
            var gate = new InkRefillGate();
            gate.Evaluate(true, 1f, 0.5f);

            Assert.IsTrue(gate.Evaluate(false, 1.5f, 0.5f));
        }

        [Test]
        public void 再次按住會重新開始計時()
        {
            var gate = new InkRefillGate();
            gate.Evaluate(true, 1f, 0.5f);
            gate.Evaluate(false, 1.4f, 0.5f);
            gate.Evaluate(true, 1.45f, 0.5f);

            Assert.IsFalse(gate.Evaluate(false, 1.6f, 0.5f), "重新按住後延遲應從 1.45 起算");
            Assert.IsTrue(gate.Evaluate(false, 1.95f, 0.5f));
        }

        [Test]
        public void 延遲為零時放開即可回墨()
        {
            var gate = new InkRefillGate();
            gate.Evaluate(true, 3f, 0f);

            Assert.IsTrue(gate.Evaluate(false, 3f, 0f));
        }
    }
}

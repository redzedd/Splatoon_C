using NUnit.Framework;
using SplatoonC.Core.Locomotion;

namespace SplatoonC.Tests.EditMode
{
    public sealed class SpeedBoostDecayTests
    {
        // 倍率 1.56 掉到 1.0 要花 0.36 秒 → 速率 = 0.56 / 0.36
        private const float Rate = 0.56f / 0.36f;

        [Test]
        public void 首次更新直接採用目標值()
        {
            var decay = new SpeedBoostDecay();

            float value = decay.Update(1f, 0.016f, Rate);

            Assert.AreEqual(1f, value, 0.0001f);
        }

        [Test]
        public void 加速是即時的()
        {
            var decay = new SpeedBoostDecay();
            decay.Update(1f, 0.016f, Rate);

            float value = decay.Update(1.56f, 0.016f, Rate);

            Assert.AreEqual(1.56f, value, 0.0001f, "進墨加速應該即時,不然入墨會鈍");
        }

        [Test]
        public void 減速在指定秒數內走完()
        {
            var decay = new SpeedBoostDecay();
            decay.Update(1.56f, 0.016f, Rate);

            // 0.18 秒(一半時間)應該只掉一半
            decay.Update(1f, 0.18f, Rate);
            Assert.AreEqual(1.28f, decay.Current, 0.005f);

            decay.Update(1f, 0.18f, Rate);
            Assert.AreEqual(1f, decay.Current, 0.005f, "0.36 秒後應剛好回到平時速度");
        }

        [Test]
        public void 減速不會低於目標()
        {
            var decay = new SpeedBoostDecay();
            decay.Update(1.56f, 0.016f, Rate);

            decay.Update(1f, 5f, Rate);

            Assert.AreEqual(1f, decay.Current, 0.0001f);
        }

        [Test]
        public void 下降途中再次進墨立刻拉滿()
        {
            var decay = new SpeedBoostDecay();
            decay.Update(1.56f, 0.016f, Rate);
            decay.Update(1f, 0.1f, Rate);
            Assert.Less(decay.Current, 1.56f);

            decay.Update(1.56f, 0.016f, Rate);

            Assert.AreEqual(1.56f, decay.Current, 0.0001f);
        }

        [Test]
        public void 速率為零時瞬間到位()
        {
            var decay = new SpeedBoostDecay();
            decay.Update(1.56f, 0.016f, Rate);

            decay.Update(1f, 0.016f, 0f);

            Assert.AreEqual(1f, decay.Current, 0.0001f);
        }
    }
}

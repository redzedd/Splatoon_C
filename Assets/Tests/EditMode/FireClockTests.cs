using NUnit.Framework;
using SplatoonC.Core.Combat;

namespace SplatoonC.Tests
{
    public class FireClockTests
    {
        private const float Interval = 0.125f;

        [Test]
        public void 首發立即()
        {
            var clock = FireClock.CreateReady();
            int shots = clock.ConsumeShots(true, 5f, Interval, 4);
            Assert.AreEqual(1, shots);
        }

        [Test]
        public void 按住一秒_六十幀_共八發()
        {
            var clock = FireClock.CreateReady();
            int total = 0;
            for (int i = 0; i < 60; i++)
            {
                total += clock.ConsumeShots(true, i / 60f, Interval, 4);
            }
            Assert.AreEqual(8, total);
        }

        [Test]
        public void 點放不可超速()
        {
            var clock = FireClock.CreateReady();
            Assert.AreEqual(1, clock.ConsumeShots(true, 0f, Interval, 4));
            Assert.AreEqual(0, clock.ConsumeShots(false, 0.05f, Interval, 4));
            Assert.AreEqual(0, clock.ConsumeShots(true, 0.06f, Interval, 4), "冷卻中再按不得出彈");
            Assert.AreEqual(1, clock.ConsumeShots(true, 0.13f, Interval, 4), "冷卻結束後出彈");
        }

        [Test]
        public void 放開不累積欠帳()
        {
            // 照真實使用模式:每幀輪詢(FireClock 的呼叫契約)。
            var clock = FireClock.CreateReady();
            clock.ConsumeShots(true, 0f, Interval, 4);
            for (int i = 1; i < 60; i++)
            {
                int idle = clock.ConsumeShots(false, i / 60f, Interval, 4);
                Assert.AreEqual(0, idle);
            }
            int shots = clock.ConsumeShots(true, 1f, Interval, 4);
            Assert.AreEqual(1, shots, "閒置一秒後重按只該出一發,不是補八發");
        }

        [Test]
        public void 長掉幀_單幀補射有上限且欠帳丟棄()
        {
            var clock = FireClock.CreateReady();
            clock.ConsumeShots(true, 0f, Interval, 4);
            int burst = clock.ConsumeShots(true, 1f, Interval, 4);
            Assert.AreEqual(4, burst, "掉幀一秒理論欠 8 發,上限 4");
            int next = clock.ConsumeShots(true, 1.01f, Interval, 4);
            Assert.AreEqual(0, next, "其餘欠帳應丟棄而不是下一幀續補");
        }
    }
}

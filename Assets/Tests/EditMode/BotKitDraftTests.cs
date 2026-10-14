using System;
using System.Collections.Generic;
using NUnit.Framework;
using SplatoonC.Core.Match;

namespace SplatoonC.Tests
{
    // bot 武器組合抽選:兩隊同位置同種類、本機玩家保留、隊內種類盡量不重複
    public class BotKitDraftTests
    {
        // 5 種類、共 12 套(射擊槍 5、滾筒 3、狙擊 2、雙槍 1、爆破 1)
        private static readonly int[] Classes = { 0, 0, 0, 0, 0, 1, 1, 1, 2, 2, 5, 7 };

        private static int[] FreeTeam(int size)
        {
            var team = new int[size];
            for (int i = 0; i < size; i++)
            {
                team[i] = BotKitDraft.Free;
            }
            return team;
        }

        [Test]
        public void 全部填滿且兩隊同位置同種類()
        {
            for (int seed = 0; seed < 200; seed++)
            {
                int[] alpha = FreeTeam(4);
                int[] bravo = FreeTeam(4);
                BotKitDraft.Fill(Classes, alpha, bravo, new Random(seed));
                for (int s = 0; s < 4; s++)
                {
                    Assert.IsTrue(alpha[s] >= 0 && bravo[s] >= 0, $"seed {seed} 位置 {s} 沒填");
                    Assert.AreEqual(Classes[alpha[s]], Classes[bravo[s]], $"seed {seed} 位置 {s} 種類不對稱");
                }
            }
        }

        [Test]
        public void 種類夠多時隊內不重複()
        {
            for (int seed = 0; seed < 200; seed++)
            {
                int[] alpha = FreeTeam(4);
                int[] bravo = FreeTeam(4);
                BotKitDraft.Fill(Classes, alpha, bravo, new Random(seed));
                var seen = new HashSet<int>();
                foreach (int kit in alpha)
                {
                    Assert.IsTrue(seen.Add(Classes[kit]), $"seed {seed} 隊內種類重複");
                }
            }
        }

        [Test]
        public void 本機玩家保留且對面跟著它的種類()
        {
            for (int seed = 0; seed < 100; seed++)
            {
                int[] alpha = FreeTeam(4);
                int[] bravo = FreeTeam(4);
                alpha[0] = 8; // 狙擊
                BotKitDraft.Fill(Classes, alpha, bravo, new Random(seed));
                Assert.AreEqual(8, alpha[0]);
                Assert.AreEqual(2, Classes[bravo[0]], $"seed {seed} 對面不是狙擊");
                Assert.AreEqual(9, bravo[0], "同種類還有別套時挑沒出現過的");
                for (int s = 1; s < 4; s++)
                {
                    Assert.AreNotEqual(2, Classes[alpha[s]], $"seed {seed} 隊友又抽到狙擊");
                }
            }
        }

        [Test]
        public void 同種類有多套時兩邊不同套()
        {
            for (int seed = 0; seed < 200; seed++)
            {
                int[] alpha = FreeTeam(4);
                int[] bravo = FreeTeam(4);
                BotKitDraft.Fill(Classes, alpha, bravo, new Random(seed));
                for (int s = 0; s < 4; s++)
                {
                    int c = Classes[alpha[s]];
                    if (Array.FindAll(Classes, x => x == c).Length >= 2)
                    {
                        Assert.AreNotEqual(alpha[s], bravo[s], $"seed {seed} 位置 {s}");
                    }
                }
            }
        }

        [Test]
        public void 種類比人數少時仍填滿()
        {
            int[] classes = { 3, 3, 4 };
            int[] alpha = FreeTeam(4);
            int[] bravo = FreeTeam(4);
            BotKitDraft.Fill(classes, alpha, bravo, new Random(7));
            for (int s = 0; s < 4; s++)
            {
                Assert.IsTrue(alpha[s] >= 0 && bravo[s] >= 0);
                Assert.AreEqual(classes[alpha[s]], classes[bravo[s]]);
            }
        }

        [Test]
        public void 人數不同與空清單()
        {
            int[] alpha = FreeTeam(1);
            int[] bravo = FreeTeam(3);
            BotKitDraft.Fill(Classes, alpha, bravo, new Random(1));
            Assert.IsTrue(alpha[0] >= 0);
            Assert.IsTrue(bravo[0] >= 0 && bravo[1] >= 0 && bravo[2] >= 0);
            Assert.AreEqual(Classes[alpha[0]], Classes[bravo[0]]);

            int[] untouched = FreeTeam(2);
            BotKitDraft.Fill(Array.Empty<int>(), untouched, FreeTeam(2), new Random(1));
            Assert.AreEqual(BotKitDraft.Free, untouched[0]);
        }
    }
}

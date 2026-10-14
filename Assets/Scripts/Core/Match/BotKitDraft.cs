using System;
using System.Collections.Generic;

namespace SplatoonC.Core.Match
{
    // 替 bot 抽武器組合,讓兩隊的武器種類大致對稱(S3 配對也會避免一邊全狙擊):
    //   - 兩隊同一個位置用同一種類(射擊槍對射擊槍、滾筒對滾筒…)
    //   - 隊內種類盡量不重複(種類比人數少時才重複)
    //   - 同種類有多套時,盡量挑這場還沒出現過的組合
    // 已經指定的位置(本機玩家)保留,對面同位置跟著它的種類。只處理索引,不依賴場景。
    public static class BotKitDraft
    {
        // 待抽的位置
        public const int Free = -1;

        // kitClasses[i] = 第 i 套組合的種類(任意整數,相同 = 同種類)。
        // alpha / bravo:每隊各位置的組合索引,Free 的位置由本函式填入;已填的保留。
        public static void Fill(IReadOnlyList<int> kitClasses, int[] alpha, int[] bravo, Random rng)
        {
            if (kitClasses == null || kitClasses.Count == 0 || alpha == null || bravo == null || rng == null)
            {
                return;
            }
            var classes = new List<int>();
            for (int i = 0; i < kitClasses.Count; i++)
            {
                if (!classes.Contains(kitClasses[i]))
                {
                    classes.Add(kitClasses[i]);
                }
            }
            classes.Sort();

            int slots = Math.Max(alpha.Length, bravo.Length);
            var used = new HashSet<int>();
            // 已指定位置的種類先算「用過」:隊內其他位置不再抽到它(本機玩家拿狙擊,隊友就不會再是狙擊)
            var taken = new HashSet<int>();
            for (int s = 0; s < slots; s++)
            {
                int fixedKit = FixedAt(alpha, bravo, s, kitClasses.Count);
                if (fixedKit != Free)
                {
                    taken.Add(kitClasses[fixedKit]);
                }
                MarkUsed(alpha, s, kitClasses.Count, used);
                MarkUsed(bravo, s, kitClasses.Count, used);
            }

            var bag = new List<int>();
            for (int s = 0; s < slots; s++)
            {
                int fixedKit = FixedAt(alpha, bravo, s, kitClasses.Count);
                int kitClass = fixedKit != Free ? kitClasses[fixedKit] : DrawClass(classes, bag, taken, rng);
                Pick(kitClasses, kitClass, alpha, s, used, rng);
                Pick(kitClasses, kitClass, bravo, s, used, rng);
            }
        }

        // 這個位置已指定的組合(先看 alpha 再看 bravo);都沒有 = Free
        private static int FixedAt(int[] alpha, int[] bravo, int slot, int kitCount)
        {
            if (slot < alpha.Length && alpha[slot] >= 0 && alpha[slot] < kitCount)
            {
                return alpha[slot];
            }
            if (slot < bravo.Length && bravo[slot] >= 0 && bravo[slot] < kitCount)
            {
                return bravo[slot];
            }
            return Free;
        }

        private static void MarkUsed(int[] team, int slot, int kitCount, HashSet<int> used)
        {
            if (slot < team.Length && team[slot] >= 0 && team[slot] < kitCount)
            {
                used.Add(team[slot]);
            }
        }

        // 從洗好的種類袋抽一個還沒被佔的種類;袋空了(種類比人數少)就重洗、重新允許所有種類
        private static int DrawClass(List<int> classes, List<int> bag, HashSet<int> taken, Random rng)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < bag.Count; i++)
                {
                    if (!taken.Contains(bag[i]))
                    {
                        int c = bag[i];
                        bag.RemoveAt(i);
                        taken.Add(c);
                        return c;
                    }
                }
                bag.Clear();
                bag.AddRange(classes);
                Shuffle(bag, rng);
                if (pass == 0 && AllTaken(bag, taken))
                {
                    taken.Clear();
                }
            }
            return bag.Count > 0 ? bag[0] : classes[0];
        }

        private static bool AllTaken(List<int> bag, HashSet<int> taken)
        {
            for (int i = 0; i < bag.Count; i++)
            {
                if (!taken.Contains(bag[i]))
                {
                    return false;
                }
            }
            return true;
        }

        // 該位置若待抽:在指定種類裡挑一套,優先這場還沒出現過的
        private static void Pick(IReadOnlyList<int> kitClasses, int kitClass, int[] team, int slot, HashSet<int> used, Random rng)
        {
            if (slot >= team.Length || (team[slot] >= 0 && team[slot] < kitClasses.Count))
            {
                return;
            }
            int fresh = 0;
            int any = 0;
            for (int i = 0; i < kitClasses.Count; i++)
            {
                if (kitClasses[i] != kitClass)
                {
                    continue;
                }
                any++;
                if (!used.Contains(i))
                {
                    fresh++;
                }
            }
            if (any == 0)
            {
                return;
            }
            bool preferFresh = fresh > 0;
            int target = rng.Next(preferFresh ? fresh : any);
            for (int i = 0; i < kitClasses.Count; i++)
            {
                if (kitClasses[i] != kitClass || (preferFresh && used.Contains(i)))
                {
                    continue;
                }
                if (target-- == 0)
                {
                    team[slot] = i;
                    used.Add(i);
                    return;
                }
            }
        }

        private static void Shuffle(List<int> list, Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                int t = list[i];
                list[i] = list[j];
                list[j] = t;
            }
        }
    }
}

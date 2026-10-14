using System.Collections;
using System.Collections.Generic;
using SplatoonC.Core.Match;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Match;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 炸彈 I 給真的 bot 用(對戰場景 4v4):七個 bot 輪流換上副武器(Suction / Burst / Curling / Autobomb),
    // 加速跑一段對戰,驗證 BotBrain 經副武器掛勾(BotUsage / BotRange / BotHoldSeconds)真的丟出每一種、而且炸了,
    // 全程零例外。BotBrain 交戰時每 6~11 秒最多嘗試一次、五成機率丟,單一 bot 可能整場都沒遇到合適時機
    // (7 個 bot 分 4 種,總有一種只有一個 bot 裝)→ 某一種「已被 bot 丟出且爆炸」後,裝它的 bot 改換成還沒達成的
    // 種類(不在按住副武器時換),剩下的種類很快集中到多個 bot 身上;判定仍是「每種都由真的 BotBrain 丟出並爆炸」。
    // 受測 bot 開無限墨(BotBrain 要墨量 ≥ 耗墨 + 8% 才丟,交戰中邊射邊扣墨很少達到——這裡只驗副武器掛勾,
    // 不驗 bot 的墨量管理)。
    [AutoTest(Scene = AutoTestAttribute.MatchScene, Timeout = 170f, Order = 234)]
    public sealed class BombsBotAutoTest : AutoTestBase
    {
        private static readonly string[] SubIds = { "suction_bomb", "burst_bomb", "curling_bomb", "autobomb" };
        // 已達成的種類,每隔幾秒(對戰時間)把裝它的 bot 換成還沒達成的種類
        private const float ReassignInterval = 4f;

        private sealed class Tracked
        {
            public Inkling Inkling;
            public WeaponLoadout Loadout;
            public WeaponKit BaseKit;
            public ThrowController Thrower;
            public PlayerInkTank Tank;
            public int Type = -1;
            public int ThrowsAtAssign;
        }

        private readonly List<Tracked> _tracked = new List<Tracked>();
        private readonly SubWeaponConfig[] _configs = new SubWeaponConfig[4];
        // 各種類「已換下的 bot」累計丟出數(換裝時結算)
        private readonly int[] _closedThrows = new int[4];
        private readonly int[] _explosionsAtStart = new int[4];
        private readonly List<string> _assignLog = new List<string>();
        private int _reassignments;
        private int _exceptions;
        private string _firstException;

        public static void Run()
        {
            Launch<BombsBotAutoTest>();
        }

        private void OnEnable()
        {
            Application.logMessageReceived += OnLog;
        }

        private void OnDisable()
        {
            Application.logMessageReceived -= OnLog;
            Time.timeScale = 1f;
            foreach (Tracked t in _tracked)
            {
                if (t.Tank != null)
                {
                    t.Tank.InfiniteInk = false;
                }
            }
        }

        private void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Exception)
            {
                _exceptions++;
                if (_firstException == null)
                {
                    _firstException = message;
                }
            }
        }

        private IEnumerator Start()
        {
            MatchController match = MatchController.Instance;
            if (match == null)
            {
                FailSetup("沒有 MatchController(需在對戰場景)");
                yield break;
            }
            for (int i = 0; i < SubIds.Length; i++)
            {
                _configs[i] = Resources.Load<SubWeaponConfig>("Weapons/Sub_" + SubIds[i]);
                if (_configs[i] == null)
                {
                    FailSetup($"缺 Resources/Weapons/Sub_{SubIds[i]}(先跑 M7 Setup)");
                    yield break;
                }
            }
            yield return WaitUntil(() => match.Roster.Count >= 8, 15f);
            var bots = new List<Inkling>();
            foreach (Inkling inkling in match.Roster)
            {
                if (inkling != null && !inkling.IsLocalPlayer)
                {
                    bots.Add(inkling);
                }
            }
            if (bots.Count < SubIds.Length)
            {
                FailSetup($"bot 不足 {SubIds.Length} 個(名單 {match.Roster.Count})");
                yield break;
            }
            // 兩隊交錯排序後輪流分配:每種炸彈兩隊都有人裝(bot 數不足 8 時最後一種少一個)
            var alpha = new List<Inkling>();
            var bravo = new List<Inkling>();
            foreach (Inkling bot in bots)
            {
                (bot.Team == bots[0].Team ? alpha : bravo).Add(bot);
            }
            var ordered = new List<Inkling>();
            for (int i = 0; i < Mathf.Max(alpha.Count, bravo.Count); i++)
            {
                if (i < alpha.Count)
                {
                    ordered.Add(alpha[i]);
                }
                if (i < bravo.Count)
                {
                    ordered.Add(bravo[i]);
                }
            }
            for (int n = 0; n < ordered.Count; n++)
            {
                Inkling bot = ordered[n];
                var loadout = bot.GetComponent<WeaponLoadout>();
                var thrower = bot.GetComponent<ThrowController>();
                if (loadout == null || loadout.Kit == null || thrower == null)
                {
                    FailSetup($"bot {bot.DisplayName} 沒有 WeaponLoadout/ThrowController");
                    yield break;
                }
                var tank = bot.GetComponent<PlayerInkTank>();
                if (tank != null)
                {
                    tank.InfiniteInk = true;
                    tank.RefillFull();
                }
                var tracked = new Tracked
                {
                    Inkling = bot, Loadout = loadout, BaseKit = loadout.Kit, Thrower = thrower, Tank = tank,
                };
                _tracked.Add(tracked);
                Assign(tracked, (n / 2) % SubIds.Length);
            }

            yield return WaitUntil(() => match.Phase >= MatchPhase.Battle, 20f);
            if (match.Phase < MatchPhase.Battle)
            {
                FailSetup("對戰沒有開始");
                yield break;
            }
            for (int i = 0; i < SubIds.Length; i++)
            {
                _closedThrows[i] = 0;
                _explosionsAtStart[i] = Explosions(i);
            }
            foreach (Tracked t in _tracked)
            {
                t.ThrowsAtAssign = t.Thrower.ThrowCount;
            }

            // 加速 3 倍跑最多 165 秒對戰時間:四種都有 bot 丟過且都炸過就提早結束
            Time.timeScale = 3f;
            float start = Time.time;
            float nextReassign = start + ReassignInterval;
            while (Time.time - start < 165f && match.Phase == MatchPhase.Battle)
            {
                bool all = true;
                for (int i = 0; i < SubIds.Length; i++)
                {
                    all &= Satisfied(i);
                }
                if (all)
                {
                    break;
                }
                if (Time.time >= nextReassign)
                {
                    nextReassign = Time.time + ReassignInterval;
                    Reassign(Time.time - start);
                }
                yield return null;
            }
            // 讓最後一顆有時間爆
            float settle = Time.time + 5f;
            while (Time.time < settle && match.Phase == MatchPhase.Battle)
            {
                yield return null;
            }
            Time.timeScale = 1f;
            float gameSeconds = Time.time - start;

            foreach (Tracked t in _tracked)
            {
                Debug.Log($"[AUTOTEST] INFO bot {t.Inkling.DisplayName}({t.Inkling.Team})最後裝 " +
                          $"{_configs[t.Type].DisplayName}:這一輪丟 {t.Thrower.ThrowCount - t.ThrowsAtAssign} 次");
            }
            foreach (string line in _assignLog)
            {
                Debug.Log("[AUTOTEST] INFO " + line);
            }
            for (int i = 0; i < SubIds.Length; i++)
            {
                Check($"bot 使用 {SubIds[i]}(真的 BotBrain 經副武器掛勾丟出)", ThrowsOf(i) > 0,
                    $"{gameSeconds:0} 秒對戰時間內合計丟 {ThrowsOf(i)} 次(期間換裝 {_reassignments} 次)");
            }
            Check("bot 丟出的四種炸彈都有爆炸",
                Explosions(0) > _explosionsAtStart[0] && Explosions(1) > _explosionsAtStart[1]
                && Explosions(2) > _explosionsAtStart[2] && Explosions(3) > _explosionsAtStart[3],
                $"Suction {Explosions(0) - _explosionsAtStart[0]}、Burst {Explosions(1) - _explosionsAtStart[1]}、" +
                $"Curling {Explosions(2) - _explosionsAtStart[2]}、Autobomb {Explosions(3) - _explosionsAtStart[3]}");
            Check("炸彈 bot 對戰期間零例外", _exceptions == 0,
                _exceptions == 0 ? "0" : $"{_exceptions} 個,第一個:{_firstException}");
            Finish();
        }

        // 換上第 type 種炸彈(主武器、大招沿用 bot 原本的組合)
        private void Assign(Tracked t, int type)
        {
            SubWeaponConfig config = _configs[type];
            WeaponKit baseKit = t.BaseKit;
            var kit = ScriptableObject.CreateInstance<WeaponKit>();
            kit.Setup("bot_" + SubIds[type], config.DisplayName, SubIds[type], "AutoTest", baseKit.Main, config,
                baseKit.Special, baseKit.SpecialPoints, baseKit.Weight, "射程", 50, "威力", 50, "射速", 50);
            t.Loadout.Equip(kit);
            t.Type = type;
            t.ThrowsAtAssign = t.Thrower.ThrowCount;
        }

        // 已達成種類的 bot → 換成還沒達成、目前裝的人最少的種類(按住副武器中的不換,免得吃掉一次投擲)
        private void Reassign(float gameSeconds)
        {
            foreach (Tracked t in _tracked)
            {
                if (!Satisfied(t.Type) || t.Thrower.IsAiming || t.Thrower.SubConfig != _configs[t.Type])
                {
                    continue;
                }
                int best = -1;
                int bestCarriers = int.MaxValue;
                for (int i = 0; i < SubIds.Length; i++)
                {
                    if (Satisfied(i))
                    {
                        continue;
                    }
                    int carriers = Carriers(i);
                    if (carriers < bestCarriers)
                    {
                        bestCarriers = carriers;
                        best = i;
                    }
                }
                if (best < 0)
                {
                    return;
                }
                _closedThrows[t.Type] += t.Thrower.ThrowCount - t.ThrowsAtAssign;
                _assignLog.Add($"{gameSeconds:0} s:{t.Inkling.DisplayName} {SubIds[t.Type]} → {SubIds[best]}");
                Assign(t, best);
                _reassignments++;
            }
        }

        // 這一種已由 bot 丟出且爆炸過
        private bool Satisfied(int type)
        {
            return ThrowsOf(type) > 0 && Explosions(type) > _explosionsAtStart[type];
        }

        private int Carriers(int type)
        {
            int count = 0;
            foreach (Tracked t in _tracked)
            {
                if (t.Type == type)
                {
                    count++;
                }
            }
            return count;
        }

        // 這一種合計被 bot 丟了幾次(已換下的累計 + 目前裝著的這一輪)
        private int ThrowsOf(int type)
        {
            int total = _closedThrows[type];
            foreach (Tracked t in _tracked)
            {
                if (t.Type == type && t.Thrower.SubConfig == _configs[type])
                {
                    total += t.Thrower.ThrowCount - t.ThrowsAtAssign;
                }
            }
            return total;
        }

        private static int Explosions(int type)
        {
            switch (type)
            {
                case 0:
                    return SuctionBomb.ExplosionCount;
                case 1:
                    return BurstBomb.ExplosionCount;
                case 2:
                    return CurlingBomb.ExplosionCount;
                default:
                    return Autobomb.ExplosionCount;
            }
        }
    }
}

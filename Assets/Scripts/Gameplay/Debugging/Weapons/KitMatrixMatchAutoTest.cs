using System.Collections;
using System.Collections.Generic;
using SplatoonC.Core;
using SplatoonC.Core.Match;
using SplatoonC.Gameplay.AI;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Match;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // K 武器組合 × bot:8 個 bot(本機玩家也交給 bot 腦)用 BotDirector.AssignRandomKits 隨機配正式組合
    //(兩隊同位置同種類),以 3 倍速打完一整場塗地對戰:驗證抽選對稱、每個 bot 都用主武器開火塗地、
    // 副武器與大招有人用,全程零例外。種子每次不同(印在 log,可重現)。
    [AutoTest(Scene = AutoTestAttribute.MatchScene, Timeout = 170f, Order = 411)]
    public sealed class KitMatrixMatchAutoTest : AutoTestBase
    {
        private sealed class Tracked
        {
            public Inkling Inkling;
            public WeaponLoadout Loadout;
            public ThrowController Thrower;
            public int AttacksAtStart;
            public int ThrowsAtStart;
            public float TurfAtStart;
            public int SpecialsAtStart;
        }

        private readonly List<Tracked> _tracked = new List<Tracked>();
        private int _exceptions;
        private string _firstException;

        public static void Run()
        {
            Launch<KitMatrixMatchAutoTest>();
        }

        private void OnEnable()
        {
            Application.logMessageReceived += OnLog;
        }

        private void OnDisable()
        {
            Application.logMessageReceived -= OnLog;
            Time.timeScale = 1f;
        }

        private void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Exception)
            {
                _exceptions++;
                if (_firstException == null)
                {
                    _firstException = message + " @ " + (stack.Length > 300 ? stack.Substring(0, 300) : stack);
                }
            }
        }

        private IEnumerator Start()
        {
            MatchController match = MatchController.Instance;
            WeaponCatalog catalog = WeaponCatalog.Load();
            if (match == null || catalog == null)
            {
                FailSetup("沒有 MatchController(需在對戰場景)或 WeaponCatalog");
                yield break;
            }
            yield return WaitUntil(() => match.Roster.Count >= 8 && BotDirector.Instance != null
                                         && BotDirector.Instance.Bots.Count > 0, 15f);
            if (match.Roster.Count < 8 || BotDirector.Instance == null)
            {
                FailSetup($"名單不足 8 人或沒有 BotDirector(名單 {match.Roster.Count})");
                yield break;
            }
            // 本機玩家也交給 bot 腦:8 bot
            if (match.LocalPlayer != null && match.LocalPlayer.GetComponent<BotBrain>() == null)
            {
                match.LocalPlayer.gameObject.AddComponent<BotBrain>().Configure(BotBrain.Difficulty.Normal);
            }
            int seed = System.Environment.TickCount;
            Debug.Log($"[AUTOTEST] 武器組合抽選種子 {seed}");
            BotDirector.Instance.AssignRandomKits(match.Roster, catalog.Kits, new System.Random(seed), true);

            // 抽選結果:兩隊同位置同種類、隊內種類不重複(清單有 5 種以上)
            var alpha = new List<WeaponKit>();
            var bravo = new List<WeaponKit>();
            foreach (Inkling inkling in match.Roster)
            {
                var loadout = inkling != null ? inkling.GetComponent<WeaponLoadout>() : null;
                if (loadout == null)
                {
                    continue;
                }
                (inkling.Team == Team.Alpha ? alpha : bravo).Add(loadout.Kit);
                _tracked.Add(new Tracked
                {
                    Inkling = inkling,
                    Loadout = loadout,
                    Thrower = inkling.GetComponent<ThrowController>(),
                });
            }
            bool symmetric = alpha.Count == bravo.Count && alpha.Count == 4;
            var classesA = new HashSet<WeaponClass>();
            for (int i = 0; symmetric && i < alpha.Count; i++)
            {
                symmetric = alpha[i] != null && bravo[i] != null && alpha[i].Main.Class == bravo[i].Main.Class;
                if (symmetric)
                {
                    classesA.Add(alpha[i].Main.Class);
                }
            }
            Check("抽選:兩隊同位置同種類、隊內種類不重複", symmetric && classesA.Count == alpha.Count,
                $"Alpha {Names(alpha)} / Bravo {Names(bravo)}");

            yield return WaitUntil(() => match.Phase >= MatchPhase.Battle, 20f);
            if (match.Phase < MatchPhase.Battle)
            {
                FailSetup("對戰沒有開始");
                yield break;
            }
            foreach (Tracked t in _tracked)
            {
                t.AttacksAtStart = t.Loadout.Main != null ? t.Loadout.Main.AttackCount : 0;
                t.ThrowsAtStart = t.Thrower != null ? t.Thrower.ThrowCount : 0;
                t.TurfAtStart = t.Inkling.TurfInkedArea;
                t.SpecialsAtStart = t.Inkling.SpecialsUsed;
            }

            // 3 倍速打完整場(180 秒對戰 ≈ 60 秒)
            Time.timeScale = 3f;
            float realEnd = Time.realtimeSinceStartup + 110f;
            while (!match.HasVerdict && Time.realtimeSinceStartup < realEnd)
            {
                yield return null;
            }
            Time.timeScale = 1f;
            Check("整場打完並判定", match.HasVerdict,
                $"phase={match.Phase} 判定 Alpha {match.Verdict.AlphaPercent:0.0}% vs Bravo {match.Verdict.BravoPercent:0.0}%");

            bool allFired = true;
            bool allPainted = true;
            int subUsers = 0;
            int specials = 0;
            var evidence = new System.Text.StringBuilder();
            foreach (Tracked t in _tracked)
            {
                int attacks = t.Loadout.Main != null ? t.Loadout.Main.AttackCount - t.AttacksAtStart : 0;
                int throws = t.Thrower != null ? t.Thrower.ThrowCount - t.ThrowsAtStart : 0;
                float turf = t.Inkling.TurfInkedArea - t.TurfAtStart;
                int used = t.Inkling.SpecialsUsed - t.SpecialsAtStart;
                allFired &= attacks > 0;
                allPainted &= turf > 1f;
                subUsers += throws > 0 ? 1 : 0;
                specials += used;
                evidence.Append($"{t.Loadout.Kit.EnglishName}:出手{attacks} 副{throws} 大招{used} 塗{turf:0}m²; ");
            }
            Check("每個 bot 都用主武器開火並塗地", allFired && allPainted, evidence.ToString());
            Check("副武器與大招有人用(≥ 4 個 bot 丟過副武器、大招 ≥ 2 次)", subUsers >= 4 && specials >= 2,
                $"丟過副武器 {subUsers} 人、大招 {specials} 次");
            Check("全程零例外", _exceptions == 0, _exceptions == 0 ? "0" : $"{_exceptions} 個,第一個:{_firstException}");
            Finish();
        }

        private static string Names(List<WeaponKit> kits)
        {
            var names = new List<string>();
            foreach (WeaponKit kit in kits)
            {
                names.Add(kit != null ? kit.EnglishName : "?");
            }
            return string.Join("、", names);
        }
    }
}

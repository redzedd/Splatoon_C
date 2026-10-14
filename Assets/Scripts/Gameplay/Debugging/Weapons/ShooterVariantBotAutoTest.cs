using System.Collections;
using System.Collections.Generic;
using SplatoonC.Core.Match;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Match;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 射擊槍變體給真的 bot 用(對戰場景 4v4):四個 bot 各換上一把變體(Jr. / .52 Gal / N-ZAP / Pro),
    // 加速跑一段對戰,驗證 BotBrain 透過主武器掛勾真的用它們開火、塗地、對敵人造成傷害,全程零例外。
    [AutoTest(Scene = AutoTestAttribute.MatchScene, Timeout = 150f, Order = 214)]
    public sealed class ShooterVariantBotAutoTest : AutoTestBase
    {
        private static readonly string[] VariantIds = { "splattershot_jr", "52_gal", "n_zap_85", "splattershot_pro" };

        private sealed class Tracked
        {
            public Inkling Inkling;
            public WeaponConfig Config;
            public int ShotsAtStart;
            public float TurfAtStart;
            public float EnemyDamage;
            public int Hits;
            public System.Action<IDamageable, float, bool> Handler;
        }

        private readonly List<Tracked> _tracked = new List<Tracked>();
        private int _exceptions;
        private string _firstException;

        public static void Run()
        {
            Launch<ShooterVariantBotAutoTest>();
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
                if (t.Inkling != null)
                {
                    t.Inkling.DamageDealt -= t.Handler;
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
            yield return WaitUntil(() => match.Roster.Count >= 8, 15f);
            var bots = new List<Inkling>();
            foreach (Inkling inkling in match.Roster)
            {
                if (inkling != null && !inkling.IsLocalPlayer)
                {
                    bots.Add(inkling);
                }
            }
            if (bots.Count < VariantIds.Length)
            {
                FailSetup($"bot 不足 {VariantIds.Length} 個(名單 {match.Roster.Count})");
                yield break;
            }
            // 兩隊各分到兩把:依隊伍交錯挑 bot
            bots.Sort((a, b) => a.Team.CompareTo(b.Team));
            var picked = new List<Inkling>();
            int half = bots.Count / 2;
            for (int i = 0; i < VariantIds.Length; i++)
            {
                picked.Add(bots[(i % 2 == 0 ? 0 : half) + i / 2]);
            }

            for (int i = 0; i < VariantIds.Length; i++)
            {
                Inkling bot = picked[i];
                var config = Resources.Load<WeaponConfig>("Weapons/Main_" + VariantIds[i]);
                var loadout = bot.GetComponent<WeaponLoadout>();
                if (config == null || loadout == null || loadout.Kit == null)
                {
                    FailSetup($"缺 Main_{VariantIds[i]} 設定或 bot 沒有 WeaponLoadout");
                    yield break;
                }
                var kit = ScriptableObject.CreateInstance<WeaponKit>();
                kit.Setup(VariantIds[i], config.name, config.name, "AutoTest", config, loadout.Kit.Sub, loadout.Kit.Special,
                    200f, loadout.Kit.Weight, "射程", 50, "威力", 50, "射速", 50);
                loadout.Equip(kit);
                var tracked = new Tracked { Inkling = bot, Config = config };
                tracked.Handler = (target, amount, splatted) =>
                {
                    if (target is TargetDummy)
                    {
                        return;
                    }
                    tracked.EnemyDamage += amount;
                    tracked.Hits++;
                };
                bot.DamageDealt += tracked.Handler;
                _tracked.Add(tracked);
            }

            yield return WaitUntil(() => match.Phase >= MatchPhase.Battle, 20f);
            if (match.Phase < MatchPhase.Battle)
            {
                FailSetup("對戰沒有開始");
                yield break;
            }
            foreach (Tracked t in _tracked)
            {
                var shooter = t.Inkling.GetComponent<InkShooter>();
                t.ShotsAtStart = shooter != null ? shooter.ShotsFired : 0;
                t.TurfAtStart = t.Inkling.TurfInkedArea;
            }

            // 加速 2.5 倍跑最多 120 秒對戰時間,四把都開過火、塗過地且有人打中敵人就提早結束
            Time.timeScale = 2.5f;
            float start = Time.time;
            while (Time.time - start < 120f && match.Phase == MatchPhase.Battle && !AllSatisfied())
            {
                yield return null;
            }
            Time.timeScale = 1f;
            float gameSeconds = Time.time - start;

            float totalDamage = 0f;
            foreach (Tracked t in _tracked)
            {
                var loadout = t.Inkling.GetComponent<WeaponLoadout>();
                var shooter = loadout != null ? loadout.Main as InkShooter : null;
                int shots = shooter != null ? shooter.ShotsFired - t.ShotsAtStart : 0;
                float turf = t.Inkling.TurfInkedArea - t.TurfAtStart;
                totalDamage += t.EnemyDamage;
                Check($"bot {t.Inkling.DisplayName}({t.Inkling.Team})使用 {t.Config.name}:開火、塗地",
                    shooter != null && shooter.Config == t.Config && shots >= 10 && turf > 0f,
                    $"出膛 {shots} 發、塗地 {TurfPoints.FromArea(turf):0}p、命中敵人 {t.Hits} 發 {t.EnemyDamage:0} 傷");
            }
            Check("bot 用變體和敵人交戰:造成傷害", totalDamage > 0f,
                $"{gameSeconds:0} 秒對戰時間內四把變體合計 {totalDamage:0} 傷");
            Check("變體 bot 對戰期間零例外", _exceptions == 0,
                _exceptions == 0 ? "0" : $"{_exceptions} 個,第一個:{_firstException}");
            Finish();
        }

        private bool AllSatisfied()
        {
            float damage = 0f;
            foreach (Tracked t in _tracked)
            {
                var shooter = t.Inkling.GetComponent<InkShooter>();
                int shots = shooter != null ? shooter.ShotsFired - t.ShotsAtStart : 0;
                if (shots < 10 || t.Inkling.TurfInkedArea - t.TurfAtStart <= 0f)
                {
                    return false;
                }
                damage += t.EnemyDamage;
            }
            return damage > 0f;
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using SplatoonC.Core.Match;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Match;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 雙槍給真的 bot 用(對戰場景 4v4):兩隊各兩個 bot 換上 Splat Dualies,加速跑一段對戰,
    // 驗證 BotBrain 透過主武器掛勾真的用它開火、塗地、打中敵人,交戰中會閃避翻滾(同玩家的翻滾路徑),全程零例外。
    [AutoTest(Scene = AutoTestAttribute.MatchScene, Timeout = 150f, Order = 222)]
    public sealed class DualiesBotAutoTest : AutoTestBase
    {
        private const int BotCount = 4;

        private sealed class Tracked
        {
            public Inkling Inkling;
            public DualiesWeapon Weapon;
            public int ShotsAtStart;
            public int RollsAtStart;
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
            Launch<DualiesBotAutoTest>();
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
                _firstException ??= message;
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
            var config = Resources.Load<DualiesConfig>(DualiesAutoTest.ConfigPath);
            if (config == null)
            {
                FailSetup($"找不到 Resources/{DualiesAutoTest.ConfigPath}(先跑 M7 Setup)");
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
            if (bots.Count < BotCount)
            {
                FailSetup($"bot 不足 {BotCount} 個(名單 {match.Roster.Count})");
                yield break;
            }
            // 兩隊各分到兩把:依隊伍排序後從兩端挑
            bots.Sort((a, b) => a.Team.CompareTo(b.Team));
            var picked = new List<Inkling> { bots[0], bots[1], bots[bots.Count - 1], bots[bots.Count - 2] };

            foreach (Inkling bot in picked)
            {
                var loadout = bot.GetComponent<WeaponLoadout>();
                if (loadout == null || loadout.Kit == null)
                {
                    FailSetup("bot 沒有 WeaponLoadout");
                    yield break;
                }
                var kit = ScriptableObject.CreateInstance<WeaponKit>();
                kit.Setup("splat_dualies", "斯普拉雙槍", "Splat Dualies", "AutoTest", config, loadout.Kit.Sub,
                    loadout.Kit.Special, 200f, WeightClass.Mid, "射程", 50, "威力", 29, "機動", 60);
                loadout.Equip(kit);
                var tracked = new Tracked { Inkling = bot, Weapon = loadout.Main as DualiesWeapon };
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
                t.ShotsAtStart = t.Weapon != null ? t.Weapon.ShotsFired : 0;
                t.RollsAtStart = t.Weapon != null ? t.Weapon.RollCount : 0;
                t.TurfAtStart = t.Inkling.TurfInkedArea;
            }

            // 加速 2.5 倍跑最多 120 秒對戰時間:四個都開過火、塗過地、有人打中敵人、有人翻滾過就提早結束
            Time.timeScale = 2.5f;
            float start = Time.time;
            while (Time.time - start < 120f && match.Phase == MatchPhase.Battle && !AllSatisfied())
            {
                yield return null;
            }
            Time.timeScale = 1f;
            float gameSeconds = Time.time - start;

            float totalDamage = 0f;
            int totalRolls = 0;
            bool reticleOnBot = false;
            foreach (Tracked t in _tracked)
            {
                DualiesWeapon weapon = t.Weapon;
                int shots = weapon != null ? weapon.ShotsFired - t.ShotsAtStart : 0;
                int rolls = weapon != null ? weapon.RollCount - t.RollsAtStart : 0;
                float turf = t.Inkling.TurfInkedArea - t.TurfAtStart;
                totalDamage += t.EnemyDamage;
                totalRolls += rolls;
                reticleOnBot |= weapon != null && weapon.Reticle != null;
                Check($"bot {t.Inkling.DisplayName}({t.Inkling.Team})使用 Splat Dualies:開火、塗地",
                    weapon != null && weapon == t.Inkling.GetComponent<WeaponLoadout>().Main && shots >= 10 && turf > 0f,
                    $"出膛 {shots} 發、塗地 {TurfPoints.FromArea(turf):0}p、翻滾 {rolls} 次、命中敵人 {t.Hits} 發 {t.EnemyDamage:0} 傷");
            }
            Check("雙槍 bot 和敵人交戰:造成傷害", totalDamage > 0f, $"{gameSeconds:0} 秒對戰時間內合計 {totalDamage:0} 傷");
            Check("雙槍 bot 交戰中會閃避翻滾", totalRolls > 0, $"合計翻滾 {totalRolls} 次");
            Check("bot 沒有雙準星 HUD(只給本機玩家)", !reticleOnBot, $"bot 有準星={reticleOnBot}");
            Check("雙槍 bot 對戰期間零例外", _exceptions == 0,
                _exceptions == 0 ? "0" : $"{_exceptions} 個,第一個:{_firstException}");
            Finish();
        }

        private bool AllSatisfied()
        {
            float damage = 0f;
            int rolls = 0;
            foreach (Tracked t in _tracked)
            {
                if (t.Weapon == null)
                {
                    return false;
                }
                if (t.Weapon.ShotsFired - t.ShotsAtStart < 10 || t.Inkling.TurfInkedArea - t.TurfAtStart <= 0f)
                {
                    return false;
                }
                damage += t.EnemyDamage;
                rolls += t.Weapon.RollCount - t.RollsAtStart;
            }
            return damage > 0f && rolls > 0;
        }
    }
}

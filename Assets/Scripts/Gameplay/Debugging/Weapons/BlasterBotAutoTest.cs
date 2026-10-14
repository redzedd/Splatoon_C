using System.Collections;
using System.Collections.Generic;
using SplatoonC.Core.Match;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Match;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 爆破槍給真的 bot 用(對戰場景 4v4):兩隊各兩個 bot 換上 Blaster / Luna Blaster,加速跑一段對戰,
    // 驗證 BotBrain 透過主武器掛勾(BotAimPoint / BotAttackIntent / 塗地掛勾)真的用它開火、塗地、以直擊或爆風打中敵人,全程零例外。
    [AutoTest(Scene = AutoTestAttribute.MatchScene, Timeout = 150f, Order = 232)]
    public sealed class BlasterBotAutoTest : AutoTestBase
    {
        private const int BotCount = 4;
        private const int MinShots = 5;

        private sealed class Tracked
        {
            public Inkling Inkling;
            public BlasterWeapon Weapon;
            public string WeaponName;
            public int ShotsAtStart;
            public int BurstsAtStart;
            public int DirectAtStart;
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
            Launch<BlasterBotAutoTest>();
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
            var blaster = Resources.Load<BlasterConfig>(BlasterAutoTest.BlasterPath);
            var luna = Resources.Load<BlasterConfig>(BlasterAutoTest.LunaPath);
            if (blaster == null || luna == null)
            {
                FailSetup($"找不到 Resources/{BlasterAutoTest.BlasterPath} 或 {BlasterAutoTest.LunaPath}(先跑 M7 Setup)");
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
            // 兩隊各分到一把 Blaster、一把 Luna:依隊伍排序後從兩端挑
            bots.Sort((a, b) => a.Team.CompareTo(b.Team));
            var picked = new List<Inkling> { bots[0], bots[1], bots[bots.Count - 1], bots[bots.Count - 2] };

            for (int i = 0; i < picked.Count; i++)
            {
                Inkling bot = picked[i];
                var loadout = bot.GetComponent<WeaponLoadout>();
                if (loadout == null || loadout.Kit == null)
                {
                    FailSetup("bot 沒有 WeaponLoadout");
                    yield break;
                }
                bool isLuna = i % 2 == 1;
                BlasterConfig config = isLuna ? luna : blaster;
                var kit = ScriptableObject.CreateInstance<WeaponKit>();
                kit.Setup(isLuna ? "luna_blaster" : "blaster", isLuna ? "新星爆破槍" : "爆破槍",
                    isLuna ? "Luna Blaster" : "Blaster", "AutoTest", config, loadout.Kit.Sub, loadout.Kit.Special,
                    isLuna ? 170f : 190f, isLuna ? WeightClass.Light : WeightClass.Mid, "射程", 27, "衝擊", 70, "射速", 20);
                loadout.Equip(kit);
                var tracked = new Tracked
                {
                    Inkling = bot, Weapon = loadout.Main as BlasterWeapon, WeaponName = isLuna ? "Luna Blaster" : "Blaster",
                };
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
                t.BurstsAtStart = t.Weapon != null ? t.Weapon.BurstCount : 0;
                t.DirectAtStart = t.Weapon != null ? t.Weapon.DirectHits : 0;
                t.TurfAtStart = t.Inkling.TurfInkedArea;
            }

            // 加速 2.5 倍跑最多 120 秒對戰時間:四個都開過火、塗過地、有人打中敵人就提早結束
            Time.timeScale = 2.5f;
            float start = Time.time;
            while (Time.time - start < 120f && match.Phase == MatchPhase.Battle && !AllSatisfied())
            {
                yield return null;
            }
            Time.timeScale = 1f;
            float gameSeconds = Time.time - start;

            float totalDamage = 0f;
            int totalDirect = 0;
            foreach (Tracked t in _tracked)
            {
                BlasterWeapon weapon = t.Weapon;
                int shots = weapon != null ? weapon.ShotsFired - t.ShotsAtStart : 0;
                int bursts = weapon != null ? weapon.BurstCount - t.BurstsAtStart : 0;
                int direct = weapon != null ? weapon.DirectHits - t.DirectAtStart : 0;
                float turf = t.Inkling.TurfInkedArea - t.TurfAtStart;
                totalDamage += t.EnemyDamage;
                totalDirect += direct;
                Check($"bot {t.Inkling.DisplayName}({t.Inkling.Team})使用 {t.WeaponName}:開火、爆炸、塗地",
                    weapon != null && weapon == t.Inkling.GetComponent<WeaponLoadout>().Main && shots >= MinShots
                    && bursts > 0 && turf > 0f,
                    $"出膛 {shots} 發、爆炸 {bursts} 次、塗地 {TurfPoints.FromArea(turf):0}p、命中敵人 {t.Hits} 次 {t.EnemyDamage:0} 傷" +
                    $"(直擊 {direct})");
            }
            Check("爆破槍 bot 和敵人交戰:造成傷害(直擊或爆風)", totalDamage > 0f,
                $"{gameSeconds:0} 秒對戰時間內合計 {totalDamage:0} 傷、直擊 {totalDirect} 次");
            Check("爆破槍 bot 對戰期間零例外", _exceptions == 0,
                _exceptions == 0 ? "0" : $"{_exceptions} 個,第一個:{_firstException}");
            Finish();
        }

        private bool AllSatisfied()
        {
            float damage = 0f;
            foreach (Tracked t in _tracked)
            {
                if (t.Weapon == null)
                {
                    return false;
                }
                if (t.Weapon.ShotsFired - t.ShotsAtStart < MinShots || t.Inkling.TurfInkedArea - t.TurfAtStart <= 0f)
                {
                    return false;
                }
                damage += t.EnemyDamage;
            }
            return damage > 0f;
        }
    }
}

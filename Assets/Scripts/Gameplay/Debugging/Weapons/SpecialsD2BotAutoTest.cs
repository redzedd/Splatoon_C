using System.Collections;
using System.Collections.Generic;
using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Core.Match;
using SplatoonC.Gameplay.AI;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Match;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // D2 大招給真的 bot 用(對戰場景 4v4):四個 bot 各換上 Tacticooler / Crab Tank / Ink Vac / Ultra Stamp,
    // 能量一空就補滿,加速跑一段對戰,驗證 BotWantsActivate / BotDrive 真的讓它們發動並產生效果
    //(飲料機立起且有人喝、戰車連射且開砲、吸墨器發射反擊彈、印章槌敲地或丟出);隊友 bot 會走去喝飲料(BotHazards 吸引點),
    // 敵方 bot 會跑出 Ink Vac 的落地爆炸圈(BotHazards 逃離);全程零例外。
    [AutoTest(Scene = AutoTestAttribute.MatchScene, Timeout = 220f, Order = 244)]
    public sealed class SpecialsD2BotAutoTest : AutoTestBase
    {
        private sealed class Tracked
        {
            public Inkling Bot;
            public SpecialWeaponConfig Config;
            public PlayerSpecialGauge Gauge;
            public int ActivationsAtStart;
        }

        private readonly List<Tracked> _tracked = new List<Tracked>();
        private int _exceptions;
        private string _firstException;

        public static void Run()
        {
            Launch<SpecialsD2BotAutoTest>();
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
            var configs = new SpecialWeaponConfig[]
            {
                Resources.Load<TacticoolerConfig>("Weapons/Special_tacticooler"),
                Resources.Load<CrabTankConfig>("Weapons/Special_crab_tank"),
                Resources.Load<InkVacConfig>("Weapons/Special_ink_vac"),
                Resources.Load<UltraStampConfig>("Weapons/Special_ultra_stamp"),
            };
            foreach (SpecialWeaponConfig config in configs)
            {
                if (config == null)
                {
                    FailSetup("缺 Resources/Weapons/Special_* 設定(先跑 M7 Setup)");
                    yield break;
                }
            }
            yield return WaitUntil(() => match.Roster.Count >= 8, 15f);
            var bots = new List<Inkling>();
            foreach (Inkling inkling in match.Roster)
            {
                if (inkling != null && !inkling.IsLocalPlayer && inkling.GetComponent<BotBrain>() != null)
                {
                    bots.Add(inkling);
                }
            }
            if (bots.Count < configs.Length)
            {
                FailSetup($"bot 不足 {configs.Length} 個(名單 {match.Roster.Count})");
                yield break;
            }
            bots.Sort((x, y) => x.Team.CompareTo(y.Team));
            int half = bots.Count / 2;
            for (int i = 0; i < configs.Length; i++)
            {
                Inkling bot = bots[(i % 2 == 0 ? 0 : half) + i / 2];
                if (!SpecialTestKit.EquipSpecial(bot, configs[i]))
                {
                    FailSetup($"bot {bot.DisplayName} 換裝 {configs[i].name} 失敗");
                    yield break;
                }
                var gauge = bot.GetComponent<PlayerSpecialGauge>();
                _tracked.Add(new Tracked
                {
                    Bot = bot,
                    Config = configs[i],
                    Gauge = gauge,
                    ActivationsAtStart = gauge.ActivationCount,
                });
            }

            yield return WaitUntil(() => match.Phase >= MatchPhase.Battle, 20f);
            if (match.Phase < MatchPhase.Battle)
            {
                FailSetup("對戰沒有開始");
                yield break;
            }
            int landAtStart = TacticoolerDevice.LandCount;
            int drinksAtStart = TacticoolerDevice.DrinkCount;
            int visitsAtStart = BotHazards.VisitsTriggered;
            int fleesAtStart = BotHazards.FleesTriggered;

            Time.timeScale = 2.5f;
            float start = Time.time;
            float nextTopUp = 0f;
            while (Time.time - start < 120f && match.Phase == MatchPhase.Battle
                   && !AllSatisfied(landAtStart, drinksAtStart))
            {
                if (Time.time >= nextTopUp)
                {
                    nextTopUp = Time.time + 1.5f;
                    foreach (Tracked t in _tracked)
                    {
                        if (!t.Gauge.IsActive && !t.Gauge.IsLocked && !t.Gauge.IsFull)
                        {
                            t.Gauge.SetPointsForTest(100f);
                        }
                    }
                }
                yield return null;
            }
            float gameSeconds = Time.time - start;

            // bot 逃離 Ink Vac 爆炸圈:對戰中沒剛好發生,就在一個敵方 bot 旁邊放一個倒數爆炸確認掛勾
            if (BotHazards.FleesTriggered == fleesAtStart)
            {
                yield return ControlledBlast(bots, configs[2] as InkVacConfig);
            }
            Time.timeScale = 1f;

            foreach (Tracked t in _tracked)
            {
                int activations = t.Gauge.ActivationCount - t.ActivationsAtStart;
                string effect = Effect(t, landAtStart, drinksAtStart, out bool ok);
                Check($"bot {t.Bot.DisplayName}({t.Bot.Team})自己發動 {t.Config.SpecialName} 並產生效果",
                    activations >= 1 && ok, $"發動 {activations} 次;{effect}");
            }
            int visits = BotHazards.VisitsTriggered - visitsAtStart;
            Check("隊友 bot 走去喝 Tacticooler(BotHazards 吸引點)", visits >= 1,
                $"出發 {visits} 次;喝到 {TacticoolerDevice.DrinkCount - drinksAtStart} 罐");
            int flees = BotHazards.FleesTriggered - fleesAtStart;
            Check("敵方 bot 跑出 Ink Vac 落地爆炸圈(BotHazards 逃離)", flees >= 1, $"逃離 {flees} 次");
            Check("D2 大招 bot 對戰期間零例外", _exceptions == 0,
                _exceptions == 0 ? $"{gameSeconds:0} 秒對戰時間" : $"{_exceptions} 個,第一個:{_firstException}");
            Finish();
        }

        private bool AllSatisfied(int land, int drinks)
        {
            foreach (Tracked t in _tracked)
            {
                if (t.Gauge.ActivationCount - t.ActivationsAtStart < 1)
                {
                    return false;
                }
                Effect(t, land, drinks, out bool ok);
                if (!ok)
                {
                    return false;
                }
            }
            return BotHazards.VisitsTriggered > 0;
        }

        private string Effect(Tracked t, int land, int drinks, out bool ok)
        {
            switch (t.Config)
            {
                case TacticoolerConfig _:
                    int landed = TacticoolerDevice.LandCount - land;
                    int drunk = TacticoolerDevice.DrinkCount - drinks;
                    ok = landed >= 1 && drunk >= 1;
                    return $"飲料機立起 {landed} 台、喝掉 {drunk} 罐";
                case CrabTankConfig _:
                    var tank = t.Bot.GetComponent<CrabTankSpecial>();
                    int shots = tank != null ? tank.TurretShots : 0;
                    ok = shots >= 5 && tank.CannonShots >= 1;
                    return $"連射 {(tank != null ? tank.TurretShots : 0)} 發、砲擊 {(tank != null ? tank.CannonShots : 0)} 發";
                case InkVacConfig _:
                    var vac = t.Bot.GetComponent<InkVacSpecial>();
                    int fired = vac != null ? vac.ShotsFired : 0;
                    ok = fired >= 1;
                    return $"反擊彈 {fired} 發";
                case UltraStampConfig _:
                    var stamp = t.Bot.GetComponent<UltraStampSpecial>();
                    int stamps = stamp != null ? stamp.Stamps + stamp.AirSwings : 0;
                    bool thrown = stamp != null && stamp.LastThrown != null;
                    ok = stamps >= 1 || thrown;
                    return $"敲地/迴旋 {stamps} 下、丟出={thrown}";
                default:
                    ok = false;
                    return "?";
            }
        }

        // 在站在地上的敵方 bot 旁 3 m 放一個半徑 6 的 Ink Vac 倒數爆炸,等 50F
        private IEnumerator ControlledBlast(List<Inkling> bots, InkVacConfig config)
        {
            if (config == null || config.BlastPrefab == null)
            {
                yield break;
            }
            Tracked owner = null;
            foreach (Tracked t in _tracked)
            {
                if (t.Config == config)
                {
                    owner = t;
                }
            }
            Team team = owner != null ? owner.Bot.Team : Team.Alpha;
            Inkling victim = null;
            // 挑一個站在地上、沒在用大招的敵方 bot(大招啟動中不會分心逃)
            for (int attempt = 0; attempt < 40 && victim == null; attempt++)
            {
                foreach (Inkling bot in bots)
                {
                    var controller = bot.GetComponent<CharacterController>();
                    var life = bot.GetComponent<InklingLifecycle>();
                    var gauge = bot.GetComponent<PlayerSpecialGauge>();
                    if (bot.Team != team && controller != null && controller.enabled && controller.isGrounded
                        && (life == null || !life.IsDead) && (gauge == null || !gauge.IsActive))
                    {
                        victim = bot;
                        break;
                    }
                }
                if (victim == null)
                {
                    yield return new WaitForSeconds(0.1f);
                }
            }
            if (victim == null)
            {
                yield break;
            }
            Time.timeScale = 1f;
            Vector3 center = victim.transform.position + victim.transform.right * 3f;
            if (Physics.Raycast(center + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 8f, CombatMasks.Terrain,
                    QueryTriggerInteraction.Ignore))
            {
                center = hit.point;
            }
            InkVacBlast blast = SharedPool.Spawn(config.BlastPrefab);
            blast.Arm(center, InkVacModel.BlastRadiusMin, config, owner != null ? owner.Bot : null, team);
            yield return new WaitForSeconds(1.2f);
        }
    }
}

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
    // D1 大招給真的 bot 用(對戰場景 4v4):四個 bot 各換上 Trizooka / Big Bubbler / Killer Wail 5.1 / Wave Breaker,
    // 能量一空就補滿,加速跑一段對戰,驗證 BotWantsActivate / BotDrive 真的讓它們發動並產生效果(墨團出膛、屏障展開、
    // 光束照射、裝置落地放波),敵方 bot 會照預兆跳過 Wave Breaker 的波(BotHazards),全程零例外。
    [AutoTest(Scene = AutoTestAttribute.MatchScene, Timeout = 200f, Order = 234)]
    public sealed class SpecialsD1BotAutoTest : AutoTestBase
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
        private bool _beamsSeen;

        public static void Run()
        {
            Launch<SpecialsD1BotAutoTest>();
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
                Resources.Load<TrizookaConfig>("Weapons/Special_trizooka"),
                Resources.Load<BigBubblerConfig>("Weapons/Special_big_bubbler"),
                Resources.Load<KillerWailConfig>("Weapons/Special_killer_wail"),
                Resources.Load<WaveBreakerConfig>("Weapons/Special_wave_breaker"),
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
            // 兩隊各分到兩種:依隊伍交錯挑 bot
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
            int globsAtStart = 0;
            int placedAtStart = BigBubblerDevice.PlacedCount;
            int landAtStart = WaveBreakerDevice.LandCount;
            int wavesAtStart = WaveBreakerWave.EmitCount;
            int jumpsAtStart = BotHazards.JumpsTriggered;

            // 加速 2.5 倍,最多 110 秒對戰時間:能量一空(沒在發動、沒在鎖定)就補滿,四種都發動過且有效果就提早結束
            Time.timeScale = 2.5f;
            float start = Time.time;
            float nextTopUp = 0f;
            while (Time.time - start < 110f && match.Phase == MatchPhase.Battle
                   && !AllSatisfied(globsAtStart, placedAtStart, landAtStart, wavesAtStart))
            {
                if (KillerWailSpeaker.ActiveBeams > 0)
                {
                    _beamsSeen = true;
                }
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

            // bot 預兆閃避:對戰中若沒有剛好發生,在一個站在地上的敵方 bot 旁邊放一道波確認掛勾
            if (BotHazards.JumpsTriggered == jumpsAtStart)
            {
                yield return ControlledWave(bots, configs[3] as WaveBreakerConfig);
            }
            Time.timeScale = 1f;

            foreach (Tracked t in _tracked)
            {
                int activations = t.Gauge.ActivationCount - t.ActivationsAtStart;
                string effect = Effect(t, globsAtStart, placedAtStart, landAtStart, wavesAtStart, out bool ok);
                Check($"bot {t.Bot.DisplayName}({t.Bot.Team})自己發動 {t.Config.SpecialName} 並產生效果",
                    activations >= 1 && ok, $"發動 {activations} 次;{effect}");
            }
            int jumps = BotHazards.JumpsTriggered - jumpsAtStart;
            Check("敵方 bot 照預兆起跳躲 Wave Breaker 的波(BotHazards)", jumps >= 1, $"起跳 {jumps} 次");
            Check("D1 大招 bot 對戰期間零例外", _exceptions == 0,
                _exceptions == 0 ? $"{gameSeconds:0} 秒對戰時間" : $"{_exceptions} 個,第一個:{_firstException}");
            Finish();
        }

        private bool AllSatisfied(int globs, int placed, int land, int waves)
        {
            foreach (Tracked t in _tracked)
            {
                if (t.Gauge.ActivationCount - t.ActivationsAtStart < 1)
                {
                    return false;
                }
                Effect(t, globs, placed, land, waves, out bool ok);
                if (!ok)
                {
                    return false;
                }
            }
            return true;
        }

        private string Effect(Tracked t, int globs, int placed, int land, int waves, out bool ok)
        {
            switch (t.Config)
            {
                case TrizookaConfig _:
                    var trizooka = t.Bot.GetComponent<TrizookaSpecial>();
                    int fired = trizooka != null ? trizooka.GlobsFired - globs : 0;
                    ok = fired >= 3;
                    return $"墨團出膛 {fired} 顆";
                case BigBubblerConfig _:
                    int deployed = BigBubblerDevice.PlacedCount - placed;
                    ok = deployed >= 1;
                    return $"屏障展開 {deployed} 次";
                case KillerWailConfig _:
                    ok = _beamsSeen && KillerWailSpecial.TargetLocks >= 1;
                    return $"光束照射={_beamsSeen}、鎖到敵人 {KillerWailSpecial.TargetLocks} 組(沒鎖到 {KillerWailSpecial.EmptyLocks} 組)、" +
                           $"命中 {KillerWailSpeaker.TotalHits} 次";
                case WaveBreakerConfig _:
                    int landed = WaveBreakerDevice.LandCount - land;
                    int emitted = WaveBreakerWave.EmitCount - waves;
                    ok = landed >= 1 && emitted >= 1;
                    return $"裝置落地 {landed} 次、放波 {emitted} 道";
                default:
                    ok = false;
                    return "?";
            }
        }

        // 在站在地上的敵方 bot 旁 7 m 放一道 Wave Breaker 波,等波前經過
        private IEnumerator ControlledWave(List<Inkling> bots, WaveBreakerConfig config)
        {
            if (config == null || config.WavePrefab == null)
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
            foreach (Inkling bot in bots)
            {
                var controller = bot.GetComponent<CharacterController>();
                var life = bot.GetComponent<InklingLifecycle>();
                if (bot.Team != team && controller != null && controller.enabled && controller.isGrounded
                    && (life == null || !life.IsDead))
                {
                    victim = bot;
                    break;
                }
            }
            if (victim == null)
            {
                yield break;
            }
            Time.timeScale = 1f;
            Vector3 center = victim.transform.position + victim.transform.right * 7f;
            if (Physics.Raycast(center + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 8f, CombatMasks.Terrain,
                    QueryTriggerInteraction.Ignore))
            {
                center = hit.point;
            }
            WaveBreakerWave wave = SharedPool.Spawn(config.WavePrefab);
            wave.transform.position = center;
            wave.Emit(center, config, owner != null ? owner.Bot : null, team);
            yield return new WaitForSeconds(2f);
        }
    }
}

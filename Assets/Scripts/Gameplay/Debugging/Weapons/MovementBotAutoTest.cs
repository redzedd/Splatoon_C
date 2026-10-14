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
    // A4 bot 驗收(對戰場景 4v4):
    //   bot 離前線太遠(重生後在基地)時自己超級跳到安全的隊友/信標,整場至少 1 次且安全落地;
    //   副武器是 Squid Beakon 的 bot 會在前線附近放信標;
    //   Round-1 整合缺口:拿雙槍、按著開火又推方向的 bot 遇到 Wave Breaker 的波時要「跳」而不是閃避翻滾(翻滾是貼地的,躲不過);
    //   全程零例外。
    // 若對戰中一直沒有 bot 重生在基地(沒人陣亡),挑一個隊友已經壓到前線的 bot 擊倒它 —— 只是讓「重生在基地」的情境出現,
    // 跳不跳、跳向誰仍由 BotBrain 自己決定。
    [AutoTest(Scene = AutoTestAttribute.MatchScene, Timeout = 240f, Order = 244)]
    public sealed class MovementBotAutoTest : AutoTestBase
    {
        private int _exceptions;
        private string _firstException;

        public static void Run()
        {
            Launch<MovementBotAutoTest>();
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
            var beakonConfig = Resources.Load<SquidBeakonConfig>(MovementTestKit.BeakonConfigPath);
            var dualiesConfig = Resources.Load<MainWeaponConfig>(DualiesAutoTest.ConfigPath);
            var waveConfig = Resources.Load<WaveBreakerConfig>("Weapons/Special_wave_breaker");
            if (beakonConfig == null || dualiesConfig == null || waveConfig == null || waveConfig.WavePrefab == null)
            {
                FailSetup("缺 Sub_squid_beakon / Main_splat_dualies / Special_wave_breaker(先跑 M7 Setup)");
                yield break;
            }
            yield return WaitUntil(() => match.Roster.Count >= 8, 15f);
            var bots = new List<BotBrain>();
            foreach (Inkling inkling in match.Roster)
            {
                var brain = inkling != null ? inkling.GetComponent<BotBrain>() : null;
                if (brain != null && !inkling.IsLocalPlayer)
                {
                    bots.Add(brain);
                }
            }
            BotBrain beakonBot = null;
            BotBrain dualiesBot = null;
            foreach (BotBrain bot in bots)
            {
                if (beakonBot == null && bot.Team == Team.Alpha)
                {
                    beakonBot = bot;
                }
                else if (dualiesBot == null && bot.Team == Team.Bravo)
                {
                    dualiesBot = bot;
                }
            }
            if (beakonBot == null || dualiesBot == null)
            {
                FailSetup($"bot 不足(名單 {match.Roster.Count})");
                yield break;
            }
            Inkling beakonInkling = beakonBot.GetComponent<Inkling>();
            Inkling dualiesInkling = dualiesBot.GetComponent<Inkling>();
            bool equipped = MovementTestKit.EquipSub(beakonInkling, beakonConfig)
                            && MovementTestKit.EquipMain(dualiesInkling, dualiesConfig);
            if (!equipped)
            {
                FailSetup("換裝(信標 / 雙槍)失敗");
                yield break;
            }

            yield return WaitUntil(() => match.Phase >= MatchPhase.Battle, 20f);
            if (match.Phase < MatchPhase.Battle)
            {
                FailSetup("對戰沒有開始");
                yield break;
            }
            match.DebugSetRemaining(170f);
            int placedAtStart = SquidBeakon.PlacedCount;
            Time.timeScale = 2.5f;
            float start = Time.time;
            float nextNudge = start + 40f;
            int nudges = 0;
            while (Time.time - start < 120f && match.Phase == MatchPhase.Battle)
            {
                int started = SuperJumpsStarted(bots);
                int landed = SuperJumpsLanded(bots);
                if (started >= 1 && landed >= 1 && SquidBeakon.PlacedCount > placedAtStart)
                {
                    break;
                }
                if (started == 0 && Time.time >= nextNudge)
                {
                    nextNudge = Time.time + 20f;
                    if (NudgeRespawn(bots))
                    {
                        nudges++;
                    }
                }
                yield return null;
            }
            float gameSeconds = Time.time - start;
            Time.timeScale = 1f;
            // 讓進行中的超級跳落地
            yield return WaitUntil(() => SuperJumpsLanded(bots) >= 1 || AnyBusy(bots) == false, 5f);
            int totalStarted = SuperJumpsStarted(bots);
            int totalLanded = SuperJumpsLanded(bots);
            Check("bot 離前線太遠時自己超級跳到安全的隊友/信標(整場 ≥ 1 次、安全落地)",
                totalStarted >= 1 && totalLanded >= 1,
                $"{gameSeconds:0} 秒對戰:開始 {totalStarted} 次、落地 {totalLanded} 次(輔助擊倒 {nudges} 次)");
            int placed = SquidBeakon.PlacedCount - placedAtStart;
            Check("副武器是 Squid Beakon 的 bot 會在前線附近放信標", placed >= 1 && beakonBot.DevicesPlaced >= 1,
                $"放下 {placed} 個(bot 放置動作 {beakonBot.DevicesPlaced} 次)");

            yield return DualiesHazardJump(dualiesBot, waveConfig, match);
            yield return CampLandingMarker(bots, match);

            Check("A4 bot 對戰期間零例外", _exceptions == 0, _exceptions == 0 ? "0" : $"{_exceptions} 個,第一個:{_firstException}");
            Finish();
        }

        private static int SuperJumpsStarted(List<BotBrain> bots)
        {
            int total = 0;
            foreach (BotBrain bot in bots)
            {
                total += bot != null ? bot.SuperJumpsStarted : 0;
            }
            return total;
        }

        private static int SuperJumpsLanded(List<BotBrain> bots)
        {
            int total = 0;
            foreach (BotBrain bot in bots)
            {
                var jump = bot != null ? bot.GetComponent<SuperJump>() : null;
                total += jump != null ? jump.JumpsCompleted : 0;
            }
            return total;
        }

        private static bool AnyBusy(List<BotBrain> bots)
        {
            foreach (BotBrain bot in bots)
            {
                var jump = bot != null ? bot.GetComponent<SuperJump>() : null;
                if (jump != null && jump.IsBusy)
                {
                    return true;
                }
            }
            return false;
        }

        // 挑一個「有隊友已經壓到前線(比它的出生點靠近敵方基地 40 m 以上)」的 bot 擊倒它 → 7.5 秒後在基地重生
        private static bool NudgeRespawn(List<BotBrain> bots)
        {
            foreach (BotBrain bot in bots)
            {
                var life = bot.GetComponent<InklingLifecycle>();
                var inkling = bot.GetComponent<Inkling>();
                TeamBase enemyBase = TeamBase.Of(TeamUtil.Opponent(bot.Team));
                if (life == null || life.IsDead || enemyBase == null)
                {
                    continue;
                }
                float spawnDepth = Flat(life.SpawnPosition, enemyBase.transform.position);
                foreach (BotBrain mate in bots)
                {
                    if (mate == bot || mate.Team != bot.Team)
                    {
                        continue;
                    }
                    var mateLife = mate.GetComponent<InklingLifecycle>();
                    if (mateLife != null && !mateLife.IsDead
                        && spawnDepth - Flat(mate.transform.position, enemyBase.transform.position) > 40f)
                    {
                        inkling.GetComponent<InklingHealth>().ApplyDamage(new DamageInfo(999f, inkling.transform.position,
                            TeamUtil.Opponent(bot.Team), null, DamageKind.Environment));
                        return true;
                    }
                }
            }
            return false;
        }

        // Round-1 缺口:雙槍 bot 開火 + 移動中遇到波 → 要跳(人形跳),不能被改成閃避翻滾。
        // 最多試 4 次(bot 剛好在空中跳、或走出波的範圍時預兆不會觸發):至少一次觸發,且每次觸發都跳起來、沒有翻滾
        private IEnumerator DualiesHazardJump(BotBrain bot, WaveBreakerConfig config, MatchController match)
        {
            var loadout = bot.GetComponent<WeaponLoadout>();
            var weapon = loadout != null ? loadout.Main as DualiesWeapon : null;
            if (weapon == null)
            {
                Check("雙槍 bot 遇到 Wave Breaker 的波會跳而不是翻滾", false, "bot 手上不是雙槍");
                yield break;
            }
            int triggered = 0;
            int jumped = 0;
            int rolledOnHazard = 0;
            var log = new System.Text.StringBuilder();
            for (int attempt = 0; attempt < 4 && triggered < 2; attempt++)
            {
                var result = new HazardAttempt();
                yield return HazardAttemptOnce(bot, weapon, config, match, result);
                log.Append(result.Summary).Append("; ");
                if (!result.Ready)
                {
                    continue;
                }
                if (result.Triggered)
                {
                    triggered++;
                    // 腳離地超過波的判定高度(WaveHitHeight 0.5 m)就躲得過;閃避翻滾貼地(升高 ≈ 0)。
                    // 敵墨中起跳只有 ≈ 1.1 m、坡地上更低,門檻取判定高度 + 0.05
                    jumped += result.MaxRise >= config.WaveHitHeight + 0.05f ? 1 : 0;
                    rolledOnHazard += result.RolledOnHazard ? 1 : 0;
                }
                yield return new WaitForSeconds(1.5f);
            }
            Check($"雙槍 bot(開火 + 推方向中)遇到 Wave Breaker 的波:照預兆起跳(人形跳離地 > 判定高度 {config.WaveHitHeight:0.0} m),不是閃避翻滾",
                triggered >= 1 && jumped == triggered && rolledOnHazard == 0,
                $"預兆觸發 {triggered} 次、跳起 {jumped} 次、預兆時翻滾 {rolledOnHazard} 次|{log}");
        }

        private sealed class HazardAttempt
        {
            public bool Ready;
            public bool Triggered;
            public float MaxRise;
            public bool RolledOnHazard;
            public string AtTrigger = "";
            public string Summary = "";
        }

        private IEnumerator HazardAttemptOnce(BotBrain bot, DualiesWeapon weapon, WaveBreakerConfig config,
            MatchController match, HazardAttempt result)
        {
            var router = bot.GetComponent<PlayerInputRouter>();
            var controller = bot.GetComponent<CharacterController>();
            var squid = bot.GetComponent<SquidController>();
            var life = bot.GetComponent<InklingLifecycle>();
            // 等到「按著開火、推著方向、人形、站在地上、沒在翻滾」的時刻(塗地或交戰中都會有)
            float deadline = Time.time + 30f;
            while (Time.time < deadline && match.Phase == MatchPhase.Battle)
            {
                if (!life.IsDead && router.AttackHeld && router.MoveInput.magnitude >= 0.5f && !squid.IsSquid
                    && controller.enabled && controller.isGrounded && !weapon.IsRolling)
                {
                    result.Ready = true;
                    break;
                }
                yield return null;
            }
            if (!result.Ready)
            {
                result.Summary = "等不到開火中移動";
                yield break;
            }
            int rolls = weapon.RollCount;
            int jumps = BotHazards.JumpsTriggered;
            Vector3 start = bot.transform.position;
            Vector3 side = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
            Vector3 center = start + side * 4f;
            if (Physics.Raycast(center + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 4f, CombatMasks.Terrain,
                    QueryTriggerInteraction.Ignore))
            {
                center = hit.point;
            }
            else
            {
                center.y = start.y;
            }
            WaveBreakerWave wave = SharedPool.Spawn(config.WavePrefab);
            wave.transform.position = center;
            wave.Emit(center, config, null, TeamUtil.Opponent(bot.Team));
            float baseY = start.y;
            float t0 = Time.time;
            float triggeredAt = -1f;
            // 預兆起跳那一下若被雙槍改成翻滾,RollCount 會在觸發的同一幀(或下一幀)+1;只看觸發前一幀到觸發後 0.12 s,
            // 之後落地、波過去了恢復開火再翻滾是正常交戰。沒能跳起來(例如正在翻滾中、跳躍鍵被吃掉)由升高量抓
            int rollsBeforeTrigger = weapon.RollCount;
            while (Time.time - t0 < 1.6f)
            {
                if (!result.Triggered && BotHazards.JumpsTriggered > jumps)
                {
                    result.Triggered = true;
                    triggeredAt = Time.time;
                    // 升高量以觸發當下的腳底為準(觸發前 bot 可能已經走下坡)
                    baseY = bot.transform.position.y;
                    result.MaxRise = 0f;
                    result.AtTrigger = $"觸發時 地面={controller.isGrounded} 烏賊={squid.IsSquid} 翻滾中={weapon.IsRolling}";
                }
                if (result.Triggered && Time.time - triggeredAt <= 0.12f)
                {
                    result.RolledOnHazard |= weapon.RollCount > rollsBeforeTrigger;
                }
                else if (!result.Triggered)
                {
                    rollsBeforeTrigger = weapon.RollCount;
                }
                if (result.Triggered)
                {
                    // 波的判定看「腳離地面多高」(WaveBreakerWave.Grounded 往下打射線),這裡量同一個量
                    Vector3 feet = bot.transform.position + Vector3.up * 0.05f;
                    float clearance = Physics.Raycast(feet, Vector3.down, out RaycastHit ground, 6f, CombatMasks.Terrain,
                        QueryTriggerInteraction.Ignore)
                        ? ground.distance - 0.05f
                        : bot.transform.position.y - baseY;
                    result.MaxRise = Mathf.Max(result.MaxRise, clearance);
                }
                yield return null;
            }
            result.Summary = $"觸發={result.Triggered} 離地最高 {result.MaxRise:0.00} m 預兆當下翻滾={result.RolledOnHazard}"
                             + $"(整段翻滾 {weapon.RollCount - rolls} 次;{result.AtTrigger})";
        }

        // 守落點(S3 落點標記所有人都看得到):布置一個乾淨的情境 —— 其他 bot 暫停(Alpha 送回出生點),
        // 敵方(Alpha)隊友 T 潛伏在 Bravo bot B 前方約 14 m(B 看不到),Alpha 的 J 從基地超級跳向 T。
        // 驗:B 在 J 著地前自己進入 CampMarker、往落點靠近並預瞄著地點,J 著地後 B 把 J 當目標交戰。
        private IEnumerator CampLandingMarker(List<BotBrain> bots, MatchController match)
        {
            if (match.Phase != MatchPhase.Battle)
            {
                Check("敵方 bot 會去守超級跳落點", false, "對戰已結束");
                yield break;
            }
            match.DebugSetRemaining(90f);
            // 陣亡中的 bot 先重生(選得到人)
            foreach (BotBrain bot in bots)
            {
                var life = bot.GetComponent<InklingLifecycle>();
                if (life != null && life.IsDead)
                {
                    life.ForceRespawnNow();
                }
            }
            yield return new WaitForSeconds(0.3f);
            BotBrain camper = null;
            BotBrain jumper = null;
            BotBrain anchor = null;
            foreach (BotBrain bot in bots)
            {
                var life = bot.GetComponent<InklingLifecycle>();
                var gauge = bot.GetComponent<PlayerSpecialGauge>();
                if (life == null || life.IsDead || (gauge != null && gauge.ActiveSpecial != null))
                {
                    continue;
                }
                if (bot.Team == Team.Bravo && camper == null)
                {
                    camper = bot;
                }
                else if (bot.Team == Team.Alpha && jumper == null)
                {
                    jumper = bot;
                }
                else if (bot.Team == Team.Alpha && anchor == null)
                {
                    anchor = bot;
                }
            }
            if (camper == null || jumper == null || anchor == null)
            {
                Check("敵方 bot 會去守超級跳落點", false, "找不到可用的 bot(都陣亡或大招使用中)");
                yield break;
            }
            if (camper.Level == BotBrain.Difficulty.Easy)
            {
                // Easy 不守落點(設計如此);這裡驗的是 Normal 以上的行為
                camper.Configure(BotBrain.Difficulty.Normal);
            }
            // 布置期間所有 bot(含 B)暫停、意圖歸零,全部送回出生點:B 看不到任何 Alpha,也沒有比 B 更前線的 Bravo 隊友
            //(否則 B 自己會超級跳走)。B 要等 T 潛好才放出來:T 剛瞬移過去還是人形,B 先看到就會開火把它打到曝光或擊倒
            var frozen = new List<BotBrain>();
            foreach (BotBrain bot in bots)
            {
                bot.enabled = false;
                ScriptedIntent.Drive(bot);
                if (bot != camper)
                {
                    frozen.Add(bot);
                }
                var life = bot.GetComponent<InklingLifecycle>();
                if (life != null && !life.IsDead)
                {
                    bot.GetComponent<Inkling>().Teleport(life.SpawnPosition, life.SpawnYaw);
                }
            }
            // B 放到 Bravo 半場(離 Alpha 出生點 > 100 m),T 潛伏在 B 前方約 14 m 的自家墨裡
            Inkling camperInkling = camper.GetComponent<Inkling>();
            TeamBase bravoBase = TeamBase.Of(Team.Bravo);
            Vector3 campSpot = bravoBase != null ? bravoBase.transform.position + new Vector3(0f, 0f, -35f) : Vector3.zero;
            if (UnityEngine.AI.NavMesh.SamplePosition(campSpot, out UnityEngine.AI.NavMeshHit campHit, 8f,
                    UnityEngine.AI.NavMesh.AllAreas))
            {
                campSpot = campHit.position;
            }
            Vector3 anchorSpot = campSpot + new Vector3(0f, 0f, -14f);
            if (UnityEngine.AI.NavMesh.SamplePosition(anchorSpot, out UnityEngine.AI.NavMeshHit anchorHit, 5f,
                    UnityEngine.AI.NavMesh.AllAreas))
            {
                anchorSpot = anchorHit.position;
            }
            Inkling anchorInkling = anchor.GetComponent<Inkling>();
            var anchorHealth = anchor.GetComponent<InklingHealth>();
            anchorHealth.ResetForRespawn();
            anchorInkling.Teleport(anchorSpot, 0f);
            SplatoonC.Gameplay.Painting.InkPainter.PaintSphere(anchorSpot, 2.5f, 1f, anchorInkling.InkSource(false));
            ScriptedIntent anchorIntent = ScriptedIntent.Drive(anchor);
            anchorIntent.Squid = true;
            yield return new WaitForSeconds(0.8f);
            var anchorSquid = anchor.GetComponent<SquidController>();
            bool hidden = anchorSquid != null && anchorSquid.IsSubmerged && !anchorHealth.IsRevealed;
            string anchorState = $"T 烏賊={anchorSquid != null && anchorSquid.IsSquid} 自家墨={anchorSquid != null && anchorSquid.OnOwnInk} "
                                 + $"潛入={anchorSquid != null && anchorSquid.IsSubmerged} 傷 {anchorHealth.Damage:0}";

            // T 潛好了才把 B 放過去、恢復 B 的大腦(OnEnable 把意圖來源接回 bot 自己)
            camperInkling.Teleport(campSpot, 180f);
            // 守點要在「沒目標、不缺墨、沒開大招」時才會想:墨補滿、大招能量清空(不讓它中途開大招)
            camper.GetComponent<PlayerInkTank>().RefillFull();
            var camperGauge = camper.GetComponent<PlayerSpecialGauge>();
            if (camperGauge != null)
            {
                camperGauge.SetPointsForTest(0f);
            }
            yield return null;

            // J 開始超級跳的同一幀放出 B(不然 B 先照塗地目標游走,標記出現時可能已經離很遠)
            var jump = jumper.GetComponent<SuperJump>();
            int completedBefore = jump.JumpsCompleted;
            bool started = jump.TryBegin(SuperJumpTarget.ToTeammate(anchorInkling));
            string refusal = jump.LastRefusal;
            camper.enabled = true;
            float startDistance = -1f;
            bool camped = false;
            float aimErrorAtLanding = 999f;
            float distanceAtLanding = -1f;
            BotBrain.Mode modeAtLanding = BotBrain.Mode.Idle;
            float viewErrorAtLanding = -1f;
            int campFrames = 0;
            int frames = 0;
            int watchedBefore = camper.CampMarkersWatched;
            var camperView = camper.GetComponent<BotAimView>();
            float deadline = Time.time + 6f;
            while (started && jump.IsBusy && Time.time < deadline)
            {
                frames++;
                if (camper.CurrentMode == BotBrain.Mode.CampMarker)
                {
                    campFrames++;
                    if (!camped)
                    {
                        camped = true;
                        startDistance = Flat(camper.transform.position, jump.LandingPoint);
                    }
                }
                if (jump.InFlight && jump.SecondsToLanding <= 0.1f && camperView != null)
                {
                    Vector3 toLanding = jump.LandingPoint + Vector3.up * 0.9f - camperView.AimOrigin;
                    aimErrorAtLanding = Vector3.Angle(camperView.AimForward, toLanding);
                    distanceAtLanding = Flat(camper.transform.position, jump.LandingPoint);
                    modeAtLanding = camper.CurrentMode;
                    viewErrorAtLanding = camperView.ErrorToTarget;
                }
                yield return null;
            }
            bool landed = !jump.IsBusy && jump.JumpsCompleted > completedBefore;
            yield return WaitUntil(() => camper.Target == jumper.GetComponent<Inkling>(), 1.5f);
            bool engaged = camper.Target == jumper.GetComponent<Inkling>();
            Check("敵方 bot 守超級跳落點:著地前自己走向落點、預瞄著地點,著地後交戰跳下來的人",
                hidden && started && landed && camped && camper.CampMarkersWatched > watchedBefore
                && distanceAtLanding >= 0f && distanceAtLanding <= startDistance + 0.5f && aimErrorAtLanding < 25f && engaged,
                $"T 潛伏={hidden}({anchorState})開始={started}{(started ? "" : "(" + refusal + ")")} 著地={landed} 守點={camped} "
                + $"距落點 {startDistance:0.0} → {distanceAtLanding:0.0} m 著地時瞄準誤差 {aimErrorAtLanding:0.0}°"
                + $"(模式 {modeAtLanding}、視線落後 {viewErrorAtLanding:0.0}°、守點 {campFrames}/{frames} 幀)交戰={engaged}");
            foreach (BotBrain bot in frozen)
            {
                bot.enabled = true;
            }
        }

        private static float Flat(Vector3 a, Vector3 b)
        {
            return new Vector2(a.x - b.x, a.z - b.z).magnitude;
        }
    }
}

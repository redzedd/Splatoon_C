using System.Collections;
using System.Collections.Generic;
using SplatoonC.Core.Match;
using SplatoonC.Gameplay.AI;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Match;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 狙擊槍給真的 bot 用(對戰場景 4v4),三部分:
    //   1. 擺位狙擊:Splat Charger bot(真的 BotBrain)與一個站著不動的敵人相距 18 m、其他角色凍結 →
    //      bot 自己看到、蓄滿、雷射掃到才放,滿蓄 160 打中敵人。
    //   2. 儲存蓄力藏不住:敵方 bot(真的 BotBrain)看著 12 m 外潛在墨裡的狙擊手——沒蓄力時看不到(> 4 m 潛伏),
    //      保留滿蓄時頭上白圈 → 敵方 bot 仍鎖定它(MainWeapon.RevealsSubmerged → BotBrain 感知)。
    //   3. 自然對戰:四個 bot 各換一把(Alpha:Splat / E-liter;Bravo:E-liter / Splat),加速跑一段,
    //      驗證 BotBrain 透過主武器掛勾真的蓄力開槍、塗地、對敵人造成傷害;全程零例外。
    [AutoTest(Scene = AutoTestAttribute.MatchScene, Timeout = 200f, Order = 242)]
    public sealed class ChargerBotAutoTest : AutoTestBase
    {
        private static readonly string[] Loadouts = { "splat_charger", "e_liter_4k", "e_liter_4k", "splat_charger" };

        private sealed class Tracked
        {
            public Inkling Inkling;
            public ChargerConfig Config;
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
            Launch<ChargerBotAutoTest>();
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
            var alpha = new List<Inkling>();
            var bravo = new List<Inkling>();
            foreach (Inkling inkling in match.Roster)
            {
                if (inkling == null || inkling.IsLocalPlayer)
                {
                    continue;
                }
                (inkling.Team == SplatoonC.Core.Team.Alpha ? alpha : bravo).Add(inkling);
            }
            if (alpha.Count < 2 || bravo.Count < 4)
            {
                FailSetup($"bot 不足(Alpha {alpha.Count}、Bravo {bravo.Count})");
                yield break;
            }
            Inkling[] picked = { alpha[0], bravo[0], alpha[1], bravo[1] };
            for (int i = 0; i < picked.Length; i++)
            {
                var config = Resources.Load<ChargerConfig>("Weapons/Main_" + Loadouts[i]);
                var loadout = picked[i].GetComponent<WeaponLoadout>();
                if (config == null || loadout == null || loadout.Kit == null)
                {
                    FailSetup($"缺 Main_{Loadouts[i]} 設定或 bot 沒有 WeaponLoadout(先跑 M7 Setup)");
                    yield break;
                }
                var kit = ScriptableObject.CreateInstance<WeaponKit>();
                kit.Setup(Loadouts[i], config.WeaponName, config.WeaponName, "AutoTest", config, loadout.Kit.Sub,
                    loadout.Kit.Special, 200f, config.HeavyWeight ? WeightClass.Heavy : WeightClass.Mid,
                    "射程", 90, "威力", 90, "射速", 20);
                loadout.Equip(kit);
                var tracked = new Tracked { Inkling = picked[i], Config = config };
                tracked.Handler = (target, amount, splatted) =>
                {
                    if (target is TargetDummy)
                    {
                        return;
                    }
                    tracked.EnemyDamage += amount;
                    tracked.Hits++;
                };
                picked[i].DamageDealt += tracked.Handler;
                _tracked.Add(tracked);
            }

            yield return WaitUntil(() => match.Phase >= MatchPhase.Battle, 20f);
            if (match.Phase < MatchPhase.Battle)
            {
                FailSetup("對戰沒有開始");
                yield break;
            }
            yield return new WaitForSeconds(1f);

            // 1) 擺位狙擊:Alpha 的 Splat Charger bot 打 18 m 外站著的 Bravo(非狙擊)敵人
            yield return StagedSnipe(match, _tracked[0].Inkling, bravo[2]);
            // 2) 儲存蓄力藏不住:另一個 Bravo 的普通 bot(上一段的靶子已經被擊倒)看 Alpha 的狙擊手
            yield return StagedReveal(match, _tracked[0].Inkling, bravo[3]);

            // 3) 自然對戰
            foreach (Tracked t in _tracked)
            {
                ChargerWeapon charger = ChargerOf(t.Inkling);
                t.ShotsAtStart = charger != null ? charger.ShotsFired : 0;
                t.TurfAtStart = t.Inkling.TurfInkedArea;
                t.EnemyDamage = 0f;
                t.Hits = 0;
            }
            Time.timeScale = 2.5f;
            float start = Time.time;
            while (Time.time - start < 110f && match.Phase == MatchPhase.Battle && !AllSatisfied())
            {
                yield return null;
            }
            Time.timeScale = 1f;
            float gameSeconds = Time.time - start;
            float totalDamage = 0f;
            int fullShots = 0;
            foreach (Tracked t in _tracked)
            {
                ChargerWeapon charger = ChargerOf(t.Inkling);
                int shots = charger != null ? charger.ShotsFired - t.ShotsAtStart : 0;
                float turf = t.Inkling.TurfInkedArea - t.TurfAtStart;
                totalDamage += t.EnemyDamage;
                fullShots += charger != null ? charger.FullShots : 0;
                Check($"bot {t.Inkling.DisplayName}({t.Inkling.Team})使用 {t.Config.WeaponName}:蓄力開槍、塗地",
                    charger != null && charger.Config == t.Config && shots >= 2 && turf > 0f,
                    $"開槍 {shots} 發(累計滿蓄 {charger?.FullShots})、塗地 {TurfPoints.FromArea(turf):0}p、" +
                    $"命中敵人 {t.Hits} 次 {t.EnemyDamage:0} 傷");
            }
            Check("狙擊 bot 和敵人交戰:造成傷害、有滿蓄", totalDamage > 0f && fullShots > 0,
                $"{gameSeconds:0} 秒對戰時間內四把狙擊槍合計 {totalDamage:0} 傷、滿蓄 {fullShots} 發");
            Check("狙擊 bot 對戰期間零例外", _exceptions == 0,
                _exceptions == 0 ? "0" : $"{_exceptions} 個,第一個:{_firstException}");
            Finish();
        }

        private bool AllSatisfied()
        {
            float damage = 0f;
            foreach (Tracked t in _tracked)
            {
                ChargerWeapon charger = ChargerOf(t.Inkling);
                if (charger == null || charger.ShotsFired - t.ShotsAtStart < 2 || t.Inkling.TurfInkedArea - t.TurfAtStart <= 0f)
                {
                    return false;
                }
                damage += t.EnemyDamage;
            }
            return damage > 0f;
        }

        private static ChargerWeapon ChargerOf(Inkling inkling)
        {
            var loadout = inkling != null ? inkling.GetComponent<WeaponLoadout>() : null;
            return loadout != null ? loadout.Main as ChargerWeapon : null;
        }

        // 擺位狙擊:其他角色全部凍結(輸入換成 ScriptedIntent、大腦停用),只留狙擊 bot 的大腦;敵人站在 18 m 外的平地
        private IEnumerator StagedSnipe(MatchController match, Inkling sniper, Inkling enemy)
        {
            ChargerWeapon charger = ChargerOf(sniper);
            const float distance = 18f;
            if (charger == null || enemy == null || !IsUsable(sniper) || !IsUsable(enemy)
                || !TryFindArena(sniper.transform.position, distance, out Vector3 arena, out Vector3 dir))
            {
                Check("狙擊 bot 自己蓄滿、雷射掃到才放、打中 18 m 外的敵人(擺位)", false, "找不到可用的 bot / 敵人 / 18 m 視線");
                yield break;
            }
            List<Inkling> frozen = Freeze(match, sniper);
            var enemyHealth = enemy.GetComponent<InklingHealth>();
            var sniperHealth = sniper.GetComponent<InklingHealth>();
            Teleport(sniper, arena + Vector3.up * 0.1f);
            Teleport(enemy, arena + dir * distance + Vector3.up * 0.3f);
            enemyHealth.ResetForRespawn();
            sniperHealth.Invulnerable = true;
            var view = sniper.GetComponent<BotAimView>();
            if (view != null)
            {
                view.Snap(Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, 0f);
            }
            var brain = sniper.GetComponent<BotBrain>();
            if (brain != null)
            {
                brain.enabled = false;
                brain.enabled = true;
            }
            int shots = charger.ShotsFired;
            int full = charger.FullShots;
            float hitDamage = -1f;
            System.Action<float, DamageInfo> onDamaged = (dealt, info) =>
            {
                if (ReferenceEquals(info.Attacker, sniper) && info.AttackClass == charger.Config.FullAttackClass && hitDamage < 0f)
                {
                    hitDamage = info.Amount;
                }
            };
            enemyHealth.Damaged += onDamaged;
            float deadline = Time.time + 14f;
            while (hitDamage < 0f && Time.time < deadline)
            {
                yield return null;
            }
            enemyHealth.Damaged -= onDamaged;
            Check($"{charger.Config.WeaponName} bot 自己蓄滿、雷射掃到才放、打中 {distance:0} m 外的敵人(擺位)",
                hitDamage > 0f && Mathf.Approximately(hitDamage, charger.Config.S3.DamageFull) && charger.FullShots > full,
                $"{sniper.DisplayName} → {enemy.DisplayName}:開槍 {charger.ShotsFired - shots} 發(滿蓄 {charger.FullShots - full})、" +
                $"命中傷害 {hitDamage:0.0}、模式 {brain?.CurrentMode}");
            sniperHealth.Invulnerable = false;
            Release(frozen);
            yield return new WaitForSeconds(0.3f);
        }

        // 儲存蓄力藏不住:狙擊手(ScriptedIntent)潛在 12 m 外的自家墨裡;觀察者是真的 BotBrain
        private IEnumerator StagedReveal(MatchController match, Inkling sniper, Inkling observer)
        {
            ChargerWeapon charger = ChargerOf(sniper);
            const float distance = 12f;
            if (charger == null || observer == null || !IsUsable(sniper) || !IsUsable(observer)
                || !TryFindArena(sniper.transform.position, distance, out Vector3 arena, out Vector3 dir))
            {
                Check("儲存蓄力的白圈讓敵方 bot 看得到潛伏的狙擊手(擺位)", false, "找不到可用的 bot / 觀察者 / 12 m 視線");
                yield break;
            }
            List<Inkling> frozen = Freeze(match, observer);
            ScriptedIntent intent = ScriptedIntent.Drive(sniper);
            var sniperHealth = sniper.GetComponent<InklingHealth>();
            var observerHealth = observer.GetComponent<InklingHealth>();
            var squid = sniper.GetComponent<SquidController>();
            var observerBrain = observer.GetComponent<BotBrain>();
            sniperHealth.Invulnerable = true;
            observerHealth.Invulnerable = true;
            // 觀察者只「看」:不能走、不能開槍 / 丟副武器(否則會往狙擊手腳下塗敵墨,把它逼出墨裡)
            var observerLocomotion = observer.GetComponent<PlayerLocomotion>();
            var observerThrow = observer.GetComponent<ThrowController>();
            MainWeapon observerMain = observer.GetComponent<WeaponLoadout>().Main;
            observerLocomotion.enabled = false;
            if (observerThrow != null)
            {
                observerThrow.enabled = false;
            }
            observerMain.enabled = false;
            // 狙擊手的隊友暫時送到對方基地(離擺位點 > 40 m,觀察者看得到的 Alpha 只剩狙擊手);測完送回原位
            TeamBase far = TeamBase.Of(observer.Team);
            var parkedInklings = new List<Inkling>();
            var parkedPositions = new List<Vector3>();
            foreach (Inkling inkling in match.Roster)
            {
                if (inkling != null && inkling != sniper && inkling.Team == sniper.Team && far != null)
                {
                    parkedInklings.Add(inkling);
                    parkedPositions.Add(inkling.transform.position);
                    int k = parkedInklings.Count - 1;
                    Teleport(inkling, far.transform.position + new Vector3(k % 3, 0.3f, k / 3) * 1.2f);
                }
            }
            Teleport(sniper, arena + Vector3.up * 0.1f);
            Teleport(observer, arena + dir * distance + Vector3.up * 0.3f);
            InkPainter.PaintSphere(arena, 2.5f, 1f, sniper.InkSource(false));
            var view = observer.GetComponent<BotAimView>();
            if (view != null)
            {
                view.Snap(Mathf.Atan2(-dir.x, -dir.z) * Mathf.Rad2Deg, 0f);
            }
            if (observerBrain != null)
            {
                observerBrain.enabled = false;
                observerBrain.enabled = true;
            }
            // (a) 沒蓄力潛墨:看不到
            intent.Squid = true;
            yield return new WaitForSeconds(1.2f);
            bool hiddenWhenPlain = squid != null && squid.IsSubmerged && observerBrain != null
                                   && !ReferenceEquals(observerBrain.Target, sniper);
            string plainTarget = observerBrain != null && observerBrain.Target != null ? observerBrain.Target.DisplayName : "無";
            // (b) 浮出蓄滿 → 按住攻擊潛墨保留:頭上白圈,仍被鎖定
            intent.Squid = false;
            yield return new WaitForSeconds(0.3f);
            var aim = sniper.GetComponent<BotAimView>();
            if (aim != null)
            {
                aim.Snap(Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, 0f);
            }
            intent.Attack = true;
            yield return WaitUntil(() => charger.IsFullyCharged, charger.Config.S3.FullChargeSeconds + 1.5f);
            bool seenCharging = observerBrain != null && ReferenceEquals(observerBrain.Target, sniper);
            InkPainter.PaintSphere(arena, 2.5f, 1f, sniper.InkSource(false));
            intent.Squid = true;
            yield return WaitUntil(() => charger.IsKeepingCharge && squid.IsSubmerged, 1f);
            yield return new WaitForSeconds(0.7f);
            bool seenWhenKeeping = charger.IsKeepingCharge && squid.IsSubmerged && observerBrain != null
                                   && ReferenceEquals(observerBrain.Target, sniper) && charger.Laser != null
                                   && charger.Laser.RingVisible;
            Check("儲存蓄力的白圈讓敵方 bot 看得到潛伏的狙擊手(沒蓄力潛伏時 12 m 外看不到)",
                hiddenWhenPlain && seenWhenKeeping,
                $"沒蓄力潛伏:觀察者目標={plainTarget} → 看不到={hiddenWhenPlain}、" +
                $"保留蓄力潛伏:保留={charger.IsKeepingCharge} 白圈={charger.Laser?.RingVisible} 被鎖定={seenWhenKeeping}" +
                $"(蓄力中人形時被鎖定={seenCharging}、觀察者目標={(observerBrain != null && observerBrain.Target != null ? observerBrain.Target.DisplayName : "無")}、" +
                $"模式 {observerBrain?.CurrentMode}、距離 {Vector3.Distance(sniper.transform.position, observer.transform.position):0.0} m、" +
                $"視線={!Physics.Linecast(observer.transform.position + Vector3.up * 1.35f, sniper.transform.position + Vector3.up, ~GameLayers.InklingMask, QueryTriggerInteraction.Ignore)})");
            intent.Attack = false;
            intent.Squid = false;
            sniperHealth.Invulnerable = false;
            observerHealth.Invulnerable = false;
            observerLocomotion.enabled = true;
            if (observerThrow != null)
            {
                observerThrow.enabled = true;
            }
            observerMain.enabled = true;
            for (int i = 0; i < parkedInklings.Count; i++)
            {
                if (parkedInklings[i] != null)
                {
                    Teleport(parkedInklings[i], parkedPositions[i] + Vector3.up * 0.2f);
                }
            }
            frozen.Add(sniper);
            Release(frozen);
            yield return new WaitForSeconds(0.3f);
        }

        // 凍結除了 keep 之外的所有角色(大腦停用、輸入換成站著不動的 ScriptedIntent)
        private static List<Inkling> Freeze(MatchController match, Inkling keep)
        {
            var frozen = new List<Inkling>();
            foreach (Inkling inkling in match.Roster)
            {
                if (inkling == null || inkling == keep)
                {
                    continue;
                }
                var brain = inkling.GetComponent<BotBrain>();
                if (brain != null)
                {
                    brain.enabled = false;
                }
                ScriptedIntent.Drive(inkling);
                frozen.Add(inkling);
            }
            return frozen;
        }

        // 找一塊擺位用的地方:從 bot 腳下開始,再依序試塗地取樣點(近的先);
        // 條件:沿某方向 distance 外的地面與腳下同高(± 0.3 m)、胸口高度的視線不被擋
        private static bool TryFindArena(Vector3 near, float distance, out Vector3 arena, out Vector3 dir)
        {
            if (GroundAt(near, near.y, out Vector3 start) && TryFindLine(start, distance, out dir))
            {
                arena = start;
                return true;
            }
            TurfMap map = BotDirector.Instance != null ? BotDirector.Instance.Map : null;
            if (map != null)
            {
                var candidates = new List<Vector3>();
                for (int i = 0; i < map.Samples.Count; i++)
                {
                    candidates.Add(map.Samples[i].Position);
                }
                candidates.Sort((a, b) => (a - near).sqrMagnitude.CompareTo((b - near).sqrMagnitude));
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (GroundAt(candidates[i], candidates[i].y, out Vector3 ground) && TryFindLine(ground, distance, out dir))
                    {
                        arena = ground;
                        return true;
                    }
                }
            }
            arena = near;
            dir = Vector3.forward;
            return false;
        }

        private static bool TryFindLine(Vector3 feet, float distance, out Vector3 dir)
        {
            for (int k = 0; k < 24; k++)
            {
                dir = Quaternion.Euler(0f, k * 15f, 0f) * Vector3.forward;
                if (GroundAt(feet + dir * distance, feet.y, out _) && GroundAt(feet + dir * (distance * 0.5f), feet.y, out _)
                    && ClearLine(feet + Vector3.up * 1.1f, feet + dir * distance + Vector3.up * 1.1f))
                {
                    return true;
                }
            }
            dir = Vector3.forward;
            return false;
        }

        private static bool ClearLine(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            return distance < 0.01f || !Physics.SphereCast(from, 0.45f, delta / distance, out _, distance,
                CombatMasks.Terrain, QueryTriggerInteraction.Ignore);
        }

        private static bool GroundAt(Vector3 point, float referenceY, out Vector3 ground)
        {
            ground = point;
            if (!Physics.Raycast(point + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 6f, CombatMasks.Terrain,
                    QueryTriggerInteraction.Ignore))
            {
                return false;
            }
            ground = hit.point;
            return Mathf.Abs(hit.point.y - referenceY) < 0.3f && hit.normal.y > 0.95f;
        }

        // 還給 bot 大腦(BotBrain.OnEnable 會重新接管輸入與視線)
        private static void Release(List<Inkling> inklings)
        {
            foreach (Inkling inkling in inklings)
            {
                if (inkling == null)
                {
                    continue;
                }
                var brain = inkling.GetComponent<BotBrain>();
                if (brain != null)
                {
                    brain.enabled = false;
                    brain.enabled = true;
                }
                else
                {
                    inkling.GetComponent<PlayerInputRouter>().ClearOverrideSource();
                }
            }
        }

        private static bool IsUsable(Inkling inkling)
        {
            if (inkling == null || !inkling.isActiveAndEnabled)
            {
                return false;
            }
            var lifecycle = inkling.GetComponent<InklingLifecycle>();
            return lifecycle == null || !lifecycle.IsDead;
        }
    }
}

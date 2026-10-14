using System.Collections;
using System.Collections.Generic;
using SplatoonC.Core.Combat;
using SplatoonC.Core.Match;
using SplatoonC.Gameplay.AI;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Match;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 滾筒給真的 bot 用(對戰場景 4v4),兩部分:
    //   1. 擺位(真實角色,不靠假人):Carbon bot 推滾撞上滿血敵人 → 70 傷、對手被推開、自己被彈回;
    //      Splat bot 推滾撞上敵人 → 125 一擊擊倒。相關角色的輸入先換成 ScriptedIntent(bot 會亂跑),測完還給大腦。
    //   2. 自然對戰:四個 bot 各換一把(Alpha:Splat / Dynamo;Bravo:Carbon / Splat),加速跑一段,
    //      驗證 BotBrain 透過主武器掛勾真的甩墨、推滾塗地、對敵人造成傷害;全程零例外。
    [AutoTest(Scene = AutoTestAttribute.MatchScene, Timeout = 180f, Order = 223)]
    public sealed class RollerBotAutoTest : AutoTestBase
    {
        private static readonly string[] Loadouts = { "splat_roller", "carbon_roller", "dynamo_roller", "splat_roller" };

        private sealed class Tracked
        {
            public Inkling Inkling;
            public RollerConfig Config;
            public int FlicksAtStart;
            public float RolledAtStart;
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
            Launch<RollerBotAutoTest>();
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
            if (alpha.Count < 2 || bravo.Count < 2)
            {
                FailSetup($"bot 不足(Alpha {alpha.Count}、Bravo {bravo.Count})");
                yield break;
            }
            Inkling[] picked = { alpha[0], bravo[0], alpha[1], bravo[1] };
            for (int i = 0; i < picked.Length; i++)
            {
                var config = Resources.Load<RollerConfig>("Weapons/Main_" + Loadouts[i]);
                var loadout = picked[i].GetComponent<WeaponLoadout>();
                if (config == null || loadout == null || loadout.Kit == null)
                {
                    FailSetup($"缺 Main_{Loadouts[i]} 設定或 bot 沒有 WeaponLoadout(先跑 M7 Setup)");
                    yield break;
                }
                WeightClass weight = config.LightWeight ? WeightClass.Light : config.HeavyWeight ? WeightClass.Heavy : WeightClass.Mid;
                var kit = ScriptableObject.CreateInstance<WeaponKit>();
                kit.Setup(Loadouts[i], config.WeaponName, config.WeaponName, "AutoTest", config, loadout.Kit.Sub,
                    loadout.Kit.Special, 180f, weight, "射程", 50, "塗墨", 50, "機動", 50);
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

            // 1) 擺位:Carbon(Bravo)撞 Alpha 敵人、Splat(Alpha)撞 Bravo 敵人
            yield return StagedContact(match, _tracked[1].Inkling, false);
            yield return StagedContact(match, _tracked[0].Inkling, true);

            // 2) 自然對戰
            foreach (Tracked t in _tracked)
            {
                RollerWeapon roller = RollerOf(t.Inkling);
                t.FlicksAtStart = roller != null ? roller.FlickCount : 0;
                t.RolledAtStart = roller != null ? roller.RolledDistance : 0f;
                t.TurfAtStart = t.Inkling.TurfInkedArea;
                t.EnemyDamage = 0f;
                t.Hits = 0;
            }
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
                RollerWeapon roller = RollerOf(t.Inkling);
                int flicks = roller != null ? roller.FlickCount - t.FlicksAtStart : 0;
                float rolled = roller != null ? roller.RolledDistance - t.RolledAtStart : 0f;
                float turf = t.Inkling.TurfInkedArea - t.TurfAtStart;
                totalDamage += t.EnemyDamage;
                Check($"bot {t.Inkling.DisplayName}({t.Inkling.Team})使用 {t.Config.WeaponName}:甩墨、推滾塗地",
                    roller != null && roller.Config == t.Config && flicks >= 2 && rolled >= 8f && turf > 0f,
                    $"甩 {flicks} 下、推滾 {rolled:0} m、塗地 {TurfPoints.FromArea(turf):0}p、命中敵人 {t.Hits} 次 {t.EnemyDamage:0} 傷");
            }
            Check("滾筒 bot 和敵人交戰:造成傷害", totalDamage > 0f,
                $"{gameSeconds:0} 秒對戰時間內四把滾筒合計 {totalDamage:0} 傷");
            Check("滾筒 bot 對戰期間零例外", _exceptions == 0,
                _exceptions == 0 ? "0" : $"{_exceptions} 個,第一個:{_firstException}");
            Finish();
        }

        private bool AllSatisfied()
        {
            float damage = 0f;
            foreach (Tracked t in _tracked)
            {
                RollerWeapon roller = RollerOf(t.Inkling);
                if (roller == null || roller.FlickCount - t.FlicksAtStart < 2 || roller.RolledDistance - t.RolledAtStart < 8f
                    || t.Inkling.TurfInkedArea - t.TurfAtStart <= 0f)
                {
                    return false;
                }
                damage += t.EnemyDamage;
            }
            return damage > 0f;
        }

        private static RollerWeapon RollerOf(Inkling inkling)
        {
            var loadout = inkling != null ? inkling.GetComponent<WeaponLoadout>() : null;
            return loadout != null ? loadout.Main as RollerWeapon : null;
        }

        // 推滾 bot 與一個敵人擺在平地直線上:其他角色全部凍結(不然凍住的敵人會被別的 bot 打死);
        // 先朝後甩(墨不落在敵人腳下)→ 轉回來推滾撞上去
        private IEnumerator StagedContact(MatchController match, Inkling rollerBot, bool expectSplat)
        {
            RollerWeapon roller = RollerOf(rollerBot);
            Inkling enemy = FindEnemy(match, rollerBot);
            string label = roller != null ? roller.Config.WeaponName : "?";
            if (roller == null || enemy == null || !IsUsable(rollerBot)
                || !TryFindArena(rollerBot.transform.position, out Vector3 arena, out Vector3 dir))
            {
                Check($"{label} bot 推滾撞真實敵人(擺位)", false, "找不到可用的 bot / 敵人 / 平地直線");
                yield break;
            }
            var frozen = new List<Inkling>();
            foreach (Inkling inkling in match.Roster)
            {
                if (inkling == null)
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
            ScriptedIntent botIntent = ScriptedIntent.Drive(rollerBot);
            var view = rollerBot.GetComponent<BotAimView>();
            var enemyHealth = enemy.GetComponent<InklingHealth>();
            var enemyLocomotion = enemy.GetComponent<PlayerLocomotion>();
            var selfLocomotion = rollerBot.GetComponent<PlayerLocomotion>();
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            Teleport(rollerBot, arena + Vector3.up * 0.1f);
            // 敵人先擺到遠處,等推滾開始後再放到跑道上
            Teleport(enemy, arena + dir * 14f + Vector3.up * 0.3f);
            enemyHealth.ResetForRespawn();
            if (view != null)
            {
                view.Snap(yaw + 180f, 0f);
            }
            yield return new WaitForSeconds(0.3f);
            float contactDamage = -1f;
            System.Action<float, DamageInfo> onDamaged = (dealt, info) =>
            {
                if (info.AttackClass == SplatoonC.Core.Combat.AttackClass.RollerContact && contactDamage < 0f)
                {
                    contactDamage = dealt;
                }
            };
            enemyHealth.Damaged += onDamaged;
            int rolls = roller.RollStartCount;
            botIntent.Attack = true;
            yield return WaitUntil(() => roller.RollStartCount > rolls, 2f);
            if (view != null)
            {
                view.Snap(yaw, 0f);
            }
            yield return new WaitForSeconds(0.4f);
            Vector3 lane = rollerBot.transform.position + dir * 3.6f;
            Teleport(enemy, new Vector3(lane.x, arena.y + 0.1f, lane.z));
            enemyHealth.ResetForRespawn();
            yield return null;
            yield return null;
            Vector3 enemyStart = enemy.transform.position;
            int contacts = roller.ContactHits;
            botIntent.Move = new Vector2(0f, 1f);
            yield return WaitUntil(() => roller.ContactHits > contacts, 2.5f);
            float push = 0f;
            float bounce = 0f;
            for (int i = 0; i < 3; i++)
            {
                push = Mathf.Max(push, Vector3.Dot(enemyLocomotion.ExternalVelocity, dir));
                bounce = Mathf.Min(bounce, Vector3.Dot(selfLocomotion.ExternalVelocity, dir));
                yield return null;
            }
            botIntent.Move = Vector2.zero;
            botIntent.Attack = false;
            yield return new WaitForSeconds(0.35f);
            Vector3 moved = enemy.transform.position - enemyStart;
            moved.y = 0f;
            bool dead = !enemyHealth.IsAlive;
            float expected = roller.Config.Roll.Damage;
            if (expectSplat)
            {
                // 擊倒那一下只扣到剩餘血量(敵墨可能先扣了一點):看攻擊值 = 125、接觸那一下致命
                Check($"{label} bot 推滾撞上真實敵人:{expected} 一擊擊倒",
                    roller.ContactHits > contacts && Mathf.Abs(roller.LastContactDamage - expected) < 0.01f
                    && roller.LastContactSplatted && contactDamage > 0f && dead,
                    $"{rollerBot.DisplayName} → {enemy.DisplayName}:接觸攻擊 {roller.LastContactDamage:0.0}" +
                    $"(扣到剩餘血量 {contactDamage:0.0})擊倒={dead}");
            }
            else
            {
                // 扣血值可能因站在敵墨裡先扣的零頭(0.3/F)取整少 0.1:攻擊值看 LastContactDamage、扣血值容許 0.5
                Check($"{label} bot 推滾撞上真實敵人:{expected} 傷、對手被推開、自己被彈回",
                    roller.ContactHits > contacts && Mathf.Abs(roller.LastContactDamage - expected) < 0.01f
                    && Mathf.Abs(contactDamage - expected) < 0.5f && !dead
                    && push > 3f && bounce < -3f && Vector3.Dot(moved, dir) > 0.4f,
                    $"{rollerBot.DisplayName} → {enemy.DisplayName}:接觸傷害 {contactDamage:0.0} 存活={!dead} " +
                    $"對手外力 {push:0.0} m/s 位移 {Vector3.Dot(moved, dir):0.00} m、自己反彈 {bounce:0.0} m/s");
            }
            enemyHealth.Damaged -= onDamaged;
            Release(frozen);
            yield return new WaitForSeconds(0.3f);
        }

        private static Inkling FindEnemy(MatchController match, Inkling self)
        {
            foreach (Inkling other in match.Roster)
            {
                if (other != null && other.Team != self.Team && !other.IsLocalPlayer && IsUsable(other)
                    && RollerOf(other) == null)
                {
                    return other;
                }
            }
            return null;
        }

        // 找一塊擺位用的平地:從 bot 腳下開始,再依序試 bot 導航用的塗地取樣點(離 bot 近的先);
        // 條件:沿某方向前方 0~8 m、後方 3 m 都是平地且視線不被擋
        private static bool TryFindArena(Vector3 near, out Vector3 arena, out Vector3 dir)
        {
            if (TryFindLine(near, out dir))
            {
                arena = near;
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
                    if (GroundAt(candidates[i], candidates[i].y, out Vector3 ground) && TryFindLine(ground, out dir))
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

        private static bool TryFindLine(Vector3 feet, out Vector3 dir)
        {
            for (int k = 0; k < 24; k++)
            {
                dir = Quaternion.Euler(0f, k * 15f, 0f) * Vector3.forward;
                bool ok = GroundAt(feet - dir * 3f, feet.y, out _);
                for (float d = 1f; ok && d <= 8f; d += 1f)
                {
                    ok = GroundAt(feet + dir * d, feet.y, out _);
                }
                if (ok && ClearLine(feet + Vector3.up * 0.9f, feet + dir * 8f + Vector3.up * 0.9f)
                    && ClearLine(feet + Vector3.up * 0.9f, feet - dir * 3f + Vector3.up * 0.9f))
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
            return Mathf.Abs(hit.point.y - referenceY) < 0.25f && hit.normal.y > 0.95f;
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
            var health = inkling.GetComponent<InklingHealth>();
            return (lifecycle == null || !lifecycle.IsDead) && health != null && health.IsAlive;
        }
    }
}

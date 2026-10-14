using System.Collections;
using System.Collections.Generic;
using SplatoonC.Core.Match;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Match;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 戰鬥基礎設施(I1)在 4v4 對 bot 對戰中的整合驗收(對戰場景):
    //   1. 以 bot 的視線(BotAimView.AimOrigin)發射 InkBullet,打中排在它前方 4 m 的敵方角色(真實 InklingHealth、隊伍過濾、池化)
    //   2. 貫穿角色:PiercePlayers 的彈打穿排成一直線(4 m / 7 m)的兩個敵人
    //   3. bot 自己丟的斯普拉炸彈走重構後的共用 Explosion(傷害 + 塗墨 + 擊退)
    // 1、2 先把相關角色的輸入換成靜止的 ScriptedIntent 並擺位(bot 會亂跑,自然遭遇不穩定),測完還給 bot 大腦。
    // 其他錯誤(例外)由批次驗證的 log 掃描抓。
    [AutoTest(Scene = AutoTestAttribute.MatchScene, Timeout = 150f, Order = 201)]
    public sealed class CombatInfraMatchAutoTest : AutoTestBase
    {
        private readonly List<InkBulletImpact> _impacts = new List<InkBulletImpact>();

        public static void Run()
        {
            Launch<CombatInfraMatchAutoTest>();
        }

        private void OnEnable()
        {
            InkBullet.Impacted += OnImpact;
        }

        private void OnDisable()
        {
            InkBullet.Impacted -= OnImpact;
        }

        private void OnImpact(in InkBulletImpact impact)
        {
            _impacts.Add(impact);
        }

        private IEnumerator Start()
        {
            MatchController match = MatchController.Instance;
            if (match == null || CombatAssets.Instance == null)
            {
                FailSetup("沒有 MatchController(需在對戰場景)或 CombatAssets(先跑 M7 Setup)");
                yield break;
            }
            yield return WaitUntil(() => match.Phase >= MatchPhase.Battle, 20f);
            int detonationsAtBattle = Explosion.DetonationCount;
            int knockbacksAtBattle = Explosion.TotalKnockbacks;
            yield return new WaitForSeconds(1f);

            yield return BotLineOfFire(match);

            // 3) bot 的斯普拉炸彈走共用 Explosion(自然遭遇,開戰後 80 秒內)
            yield return WaitUntil(() => Explosion.DetonationCount > detonationsAtBattle, 80f);
            Check("bot 丟的斯普拉炸彈經由共用 Explosion 爆炸(無例外)",
                Explosion.DetonationCount > detonationsAtBattle,
                $"開戰後爆炸 {Explosion.DetonationCount - detonationsAtBattle} 次,擊退 {Explosion.TotalKnockbacks - knockbacksAtBattle} 次,"
                + $"最近爆心 {Explosion.LastCenter} 隊伍 {Explosion.LastTeam} 命中 {Explosion.LastResult.TargetsHit}");
            Finish();
        }

        private IEnumerator BotLineOfFire(MatchController match)
        {
            if (!TryStageLine(match, out Inkling shooter, out Inkling first, out Inkling second, out Vector3 p1,
                    out Vector3 p2))
            {
                Check("bot 視線發射的 InkBullet 打中敵方角色", false, "找不到可以排成 7 m 直線的 bot 與兩個敵人");
                Check("貫穿角色", false, "同上");
                yield break;
            }
            Inkling[] frozen = { shooter, first, second };
            foreach (Inkling inkling in frozen)
            {
                ScriptedIntent.Drive(inkling);
            }
            var health1 = first.GetComponent<InklingHealth>();
            var health2 = second.GetComponent<InklingHealth>();
            health1.Invulnerable = true;
            health2.Invulnerable = true;
            // 第二個敵人先擺到射手身後,不擋第一個測項
            Vector3 feet = shooter.transform.position;
            Vector3 lineDir = p1 - feet;
            lineDir.y = 0f;
            lineDir.Normalize();
            Teleport(first, p1 + Vector3.up * 0.1f);
            Teleport(second, feet - lineDir * 3f + Vector3.up * 0.3f);
            yield return null;
            yield return null;

            // 1) 一般彈:bot 視線 → 4 m 外的敵人胸口,直線段命中、全額 36
            InkBulletSpec spec = InkBulletSpec.Splattershot;
            IAimView view = shooter.AimView;
            Vector3 origin = view != null ? view.AimOrigin : feet + Vector3.up * 1.2f;
            // 其他 bot 的射擊槍也發 InkBullet:只看這一發自己的事件
            int start = _impacts.Count;
            InkBullet fired = InkBullet.Fire(spec, origin, first.transform.position + Vector3.up * 1f - origin, shooter.Team, shooter);
            yield return WaitUntil(() => FirstEventOf(fired, start) >= 0, 1f);
            int hitIndex = FirstEventOf(fired, start);
            InkBulletImpact hit = hitIndex >= 0 ? _impacts[hitIndex] : default;
            Check("bot 視線發射的 InkBullet 打中敵方角色(依飛行幀數全額 36)",
                hit.End == InkBulletEnd.Target && ReferenceEquals(hit.Target, health1) && Mathf.Abs(hit.Damage - 36f) < 0.001f,
                $"{shooter.DisplayName}({shooter.Team})→ {first.DisplayName}:{hit.End} 第 {hit.Frame}F 傷害 {hit.Damage}");

            // 2) 貫穿:兩個敵人排在 4 m / 7 m
            Teleport(second, p2 + Vector3.up * 0.1f);
            yield return null;
            yield return null;
            spec.PiercePlayers = true;
            origin = view != null ? view.AimOrigin : origin;
            start = _impacts.Count;
            // 瞄兩個敵人胸口的中點:兩人落腳處與射手可各差 ±0.8 m(GroundAt),只瞄第一個人時,
            // 第一個人站得低的話延長線在 7 m 前就先打到地面(與貫穿無關的假紅)
            Vector3 pierceAim = (first.transform.position + second.transform.position) * 0.5f + Vector3.up * 1f;
            InkBullet piercing = InkBullet.Fire(spec, origin, pierceAim - origin, shooter.Team, shooter);
            yield return new WaitForSeconds(0.5f);
            bool hitFirst = false;
            bool hitSecond = false;
            for (int i = start; i < _impacts.Count; i++)
            {
                // 同一個池化物件結束後可能被別的 bot 重用:只看貫穿事件(一般射擊槍彈不貫穿)
                if (!ReferenceEquals(_impacts[i].Bullet, piercing) || _impacts[i].End != InkBulletEnd.Pierce)
                {
                    continue;
                }
                hitFirst |= ReferenceEquals(_impacts[i].Target, health1) && _impacts[i].Damage > 0f;
                hitSecond |= ReferenceEquals(_impacts[i].Target, health2) && _impacts[i].Damage > 0f;
            }
            Check("貫穿角色:一發打穿排成一直線的兩個敵人", hitFirst && hitSecond,
                $"{shooter.DisplayName} → {first.DisplayName}(4 m)={hitFirst}、{second.DisplayName}(7 m)={hitSecond};事件 {_impacts.Count - start} 筆"
                + $"(落腳高低差 {p1.y - feet.y:+0.00;-0.00} / {p2.y - feet.y:+0.00;-0.00} m)");

            health1.Invulnerable = false;
            health2.Invulnerable = false;
            Release(frozen);
        }

        // 這一發(fired)在 from 之後的第一筆事件(沒有 = −1)
        private int FirstEventOf(InkBullet fired, int from)
        {
            for (int i = from; i < _impacts.Count; i++)
            {
                if (ReferenceEquals(_impacts[i].Bullet, fired))
                {
                    return i;
                }
            }
            return -1;
        }

        // 找一個 bot(射手)+ 兩個它的敵人,並在射手周圍找一條 7 m 的平地直線(0.35 m 粗的視線不被地形擋),
        // 射手身後 3 m 也要是平地(暫放第二個敵人)
        private static bool TryStageLine(MatchController match, out Inkling shooter, out Inkling first,
            out Inkling second, out Vector3 p1, out Vector3 p2)
        {
            shooter = null;
            first = null;
            second = null;
            p1 = Vector3.zero;
            p2 = Vector3.zero;
            IReadOnlyList<Inkling> roster = match.Roster;
            for (int i = 0; i < roster.Count; i++)
            {
                Inkling candidate = roster[i];
                if (!IsUsable(candidate) || candidate.IsLocalPlayer)
                {
                    continue;
                }
                Inkling e1 = null;
                Inkling e2 = null;
                for (int j = 0; j < roster.Count; j++)
                {
                    Inkling other = roster[j];
                    if (!IsUsable(other) || other.Team == candidate.Team)
                    {
                        continue;
                    }
                    if (e1 == null)
                    {
                        e1 = other;
                    }
                    else if (e2 == null)
                    {
                        e2 = other;
                    }
                }
                if (e1 == null || e2 == null)
                {
                    continue;
                }
                Vector3 feet = candidate.transform.position;
                Vector3 eye = candidate.AimView != null ? candidate.AimView.AimOrigin : feet + Vector3.up * 1.2f;
                for (int k = 0; k < 24; k++)
                {
                    Vector3 dir = Quaternion.Euler(0f, k * 15f, 0f) * Vector3.forward;
                    if (GroundAt(feet + dir * 4f, feet.y, out Vector3 a) && GroundAt(feet + dir * 7f, feet.y, out Vector3 b)
                        && GroundAt(feet - dir * 3f, feet.y, out _)
                        && ClearLine(eye, b + Vector3.up * 1f) && ClearLine(eye, a + Vector3.up * 1f))
                    {
                        shooter = candidate;
                        first = e1;
                        second = e2;
                        p1 = a;
                        p2 = b;
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool ClearLine(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            return distance < 0.01f || !Physics.SphereCast(from, 0.35f, delta / distance, out _, distance,
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
            return Mathf.Abs(hit.point.y - referenceY) < 0.8f && hit.normal.y > 0.85f;
        }

        // 還給 bot 大腦(BotBrain.OnEnable 會重新接管輸入)
        private static void Release(Inkling[] inklings)
        {
            foreach (Inkling inkling in inklings)
            {
                var brain = inkling.GetComponent<SplatoonC.Gameplay.AI.BotBrain>();
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

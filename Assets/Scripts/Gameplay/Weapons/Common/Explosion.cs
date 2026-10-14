using System.Collections.Generic;
using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 一次爆炸的結果(測試、HUD、bot 評估用)
    public struct ExplosionResult
    {
        // 受到傷害(> 0)的目標數
        public int TargetsHit;
        // 結算出去的傷害總和(套護甲/物件倍率之前)
        public float TotalDamage;
        // 在傷害或擊退範圍內、但被牆擋住視線的目標數
        public int BlockedByCover;
        // 被擊退的敵方角色數
        public int Knockbacks;
        // 塗墨次數(主塗墨 + 射線 + 墨滴)
        public int Splats;
        // 新塗到的地盤面積(m²)
        public float PaintedArea;
        // 整個爆炸被敵方 Ink Vac 的吸引漩渦吸走(沒有傷害、擊退、塗墨)
        public bool Absorbed;
    }

    // 爆炸解算(S3 炸彈共通規則,DESIGN_REFERENCE §4.3;research 03 §6-4):
    //   傷害:DamageableRegistry 全部目標,距離量到「目標碰撞體最近點」,近/遠(或線性)查 ExplosionSpec;
    //         友軍無傷(DamageRules);視線判定:地形擋、角色不擋、場上裝置只有 BlocksAttacksFrom(隊伍) 為真時擋(Splash Wall)。
    //   擊退:敵方角色在 KnockbackRange 內(與傷害同一條視線)→ PlayerLocomotion.AddImpulse(KnockbackModel)。
    //   塗墨:爆心往下找地面、以落點為球心塗 PaintRadius(跨台階的水平面都塗到)+ 隨機方向噴墨射線(牆面)+ 四散墨滴。
    //   特效:射線落點噴濺粒子、團隊色閃光球;音效 spec.Sound。
    // 呼叫端:SplatBomb.Explode、InkBullet(爆破槍/Trizooka/…的撞擊/空爆)、之後的各種炸彈與大招。
    public static class Explosion
    {
        private static readonly List<IDamageable> Snapshot = new List<IDamageable>(32);
        private static readonly RaycastHit[] LosHits = new RaycastHit[16];

        // 測試/除錯:累計爆炸數、最近一次的爆心與結果、累計擊退次數
        public static int DetonationCount { get; private set; }
        public static Vector3 LastCenter { get; private set; }
        public static Team LastTeam { get; private set; }
        public static ExplosionResult LastResult { get; private set; }
        public static int TotalKnockbacks { get; private set; }
        // 被 Ink Vac 吸走的爆炸數
        public static int AbsorbedCount { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetOnLoad()
        {
            AbsorbedCount = 0;
            DetonationCount = 0;
            TotalKnockbacks = 0;
            LastResult = default;
            Snapshot.Clear();
        }

        // attackId ≠ 0:同一次攻擊已打過的目標不再吃這次爆炸(CombatLedger)。
        // radiusScale:傷害半徑倍率(爆破槍打到地形 ×0.4234)。exclude:不吃這次爆炸的目標(直擊目標)。
        public static ExplosionResult Detonate(in ExplosionSpec spec, Vector3 center, Team team, Inkling owner,
            int attackId = 0, float radiusScale = 1f, IDamageable exclude = null)
        {
            return Detonate(spec, center, team, owner, CombatMasks.Terrain, CombatMasks.Terrain, attackId,
                radiusScale, exclude);
        }

        // 自訂遮罩版:losMask = 視線會被哪些圖層擋(不含角色/目標圖層);paintMask = 噴墨射線打哪些圖層
        public static ExplosionResult Detonate(in ExplosionSpec spec, Vector3 center, Team team, Inkling owner,
            int losMask, int paintMask, int attackId, float radiusScale, IDamageable exclude)
        {
            // 爆心在敵方 Ink Vac 的吸引範圍內 → 整個爆炸被吸走(S3:漩渦裡的爆炸傷不到使用者與隊友)
            if (InkVacuum.Active.Count > 0)
            {
                InkVacuum vacuum = InkVacuum.Containing(team, center);
                if (vacuum != null)
                {
                    vacuum.Absorb(center, Mathf.Max(spec.NearDamage, spec.FarDamage), attackId);
                    AbsorbedCount++;
                    var absorbed = new ExplosionResult { Absorbed = true };
                    LastResult = absorbed;
                    return absorbed;
                }
            }
            DetonationCount++;
            LastCenter = center;
            LastTeam = team;
            var result = new ExplosionResult();
            Color color = TeamPalette.ColorOf(team);
            if (!string.IsNullOrEmpty(spec.Sound))
            {
                GameAudio.Play(spec.Sound, center, spec.EffectiveVolume, 0.06f);
            }

            ResolveTargets(spec, center, team, owner, losMask, attackId, radiusScale, exclude, ref result);
            Paint(spec, center, team, owner, paintMask, color, ref result);

            float flash = spec.FlashRadius != 0f ? spec.FlashRadius
                : (spec.NearRadius > 0f ? spec.NearRadius * radiusScale : spec.FarRadius * radiusScale);
            if (flash > 0f)
            {
                ExplosionFlash.Spawn(center, flash, color);
            }

            TotalKnockbacks += result.Knockbacks;
            LastResult = result;
            return result;
        }

        private static void ResolveTargets(in ExplosionSpec spec, Vector3 center, Team team, Inkling owner,
            int losMask, int attackId, float radiusScale, IDamageable exclude, ref ExplosionResult result)
        {
            // 快照:結算中物件可能被摧毀並自註冊表移除,不能邊迭代邊改
            Snapshot.Clear();
            List<IDamageable> active = DamageableRegistry.Active;
            for (int i = 0; i < active.Count; i++)
            {
                // 逐一加入(List.AddRange 對 ICollection 會配置暫存陣列)
                Snapshot.Add(active[i]);
            }
            bool knockback = spec.KnockbackAccel > 0f && spec.KnockbackRange > 0f;
            for (int i = 0; i < Snapshot.Count; i++)
            {
                IDamageable target = Snapshot[i];
                if (target == null || ReferenceEquals(target, exclude) || !target.IsAlive
                    || !DamageRules.CanDamage(team, target))
                {
                    continue;
                }
                Vector3 closest = target.ClosestPoint(center);
                float distance = Vector3.Distance(center, closest);
                float damage = spec.Evaluate(distance, radiusScale);
                var health = target as InklingHealth;
                bool push = knockback && health != null && distance < spec.KnockbackRange;
                if (damage <= 0f && !push)
                {
                    continue;
                }
                if (!spec.IgnoreLineOfSight && IsBlocked(center, closest, distance, team, target, losMask))
                {
                    result.BlockedByCover++;
                    continue;
                }
                if (damage > 0f)
                {
                    damage = CombatLedger.Register(attackId, target, damage);
                    if (damage > 0f)
                    {
                        target.ApplyDamage(new DamageInfo(damage, closest, team, owner, spec.Kind, spec.AttackClass));
                        result.TargetsHit++;
                        result.TotalDamage += damage;
                    }
                }
                if (push && target.IsAlive)
                {
                    var locomotion = health.GetComponent<PlayerLocomotion>();
                    if (locomotion != null && locomotion.isActiveAndEnabled)
                    {
                        locomotion.AddImpulse(KnockbackModel.Impulse(center, closest, spec.KnockbackAccel,
                            spec.KnockbackRange));
                        result.Knockbacks++;
                    }
                }
            }
            Snapshot.Clear();
        }

        // 視線:地形擋;目標圖層上只有「明確要擋這一隊攻擊」的場上裝置(Splash Wall)才擋;角色與假人不擋
        public static bool IsBlocked(Vector3 from, Vector3 to, float distance, Team attackerTeam, IDamageable target,
            int losMask)
        {
            // 縮短一點:終點就在目標表面,避免浮點誤差打到目標背後的地面
            float length = distance - 0.05f;
            if (length <= 0.01f)
            {
                return false;
            }
            // 單向屏障(Big Bubbler):罩外的爆炸炸不到罩內/另一側(爆心在罩內則不擋)
            if (BarrierSphere.Blocks(attackerTeam, from, to, target))
            {
                return true;
            }
            Vector3 direction = (to - from) / distance;
            int count = Physics.RaycastNonAlloc(from, direction, LosHits, length, losMask | CombatMasks.TargetLayerMask,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider collider = LosHits[i].collider;
                if (collider == null)
                {
                    continue;
                }
                int layerBit = 1 << collider.gameObject.layer;
                if ((layerBit & losMask) != 0 && (layerBit & CombatMasks.Targets) == 0)
                {
                    return true;
                }
                var device = collider.GetComponentInParent<DeployableObject>();
                if (device != null && !ReferenceEquals(device, target) && device.IsAlive
                    && device.BlocksAttacksFrom(attackerTeam))
                {
                    return true;
                }
            }
            return false;
        }

        private static void Paint(in ExplosionSpec spec, Vector3 center, Team team, Inkling owner, int paintMask,
            Color color, ref ExplosionResult result)
        {
            bool charges = !spec.NoSpecialCharge;
            InkSource source = owner != null ? owner.InkSource(charges) : new InkSource(team, null, charges);
            float hardness = spec.EffectiveHardness;
            InkSplashFxPool fx = InkSplashFxPool.Instance;

            // 主塗墨:爆心往下找地面(炸彈停在地上時爆心離地約 0.25 m),以落點為球心 → 地面上是完整半徑的圓;
            // 空爆太高(找不到地面)就以爆心為球心,只塗得到球內的面
            if (spec.PaintRadius > 0f)
            {
                Vector3 origin = center + Vector3.up * 0.2f;
                if (Physics.Raycast(origin, Vector3.down, out RaycastHit ground, spec.PaintRadius + 0.2f, paintMask,
                        QueryTriggerInteraction.Ignore))
                {
                    result.PaintedArea += InkPainter.PaintSphere(ground.point, spec.PaintRadius, hardness, source);
                    if (fx != null)
                    {
                        fx.Spawn(ground.point, ground.normal, color);
                    }
                }
                else
                {
                    result.PaintedArea += InkPainter.PaintSphere(center, spec.PaintRadius, hardness, source);
                }
                result.Splats++;
            }

            // 噴墨射線:隨機方向(上半球太高的翻到下半球),打到的第一個可塗面塗一小片;牆面也吃得到
            float reach = spec.EffectiveRayReach;
            for (int i = 0; i < spec.PaintRayCount; i++)
            {
                Vector3 direction = Random.onUnitSphere;
                if (direction.y > 0.6f)
                {
                    direction.y = -direction.y;
                }
                if (!Physics.Raycast(center, direction, out RaycastHit hit, reach, paintMask,
                        QueryTriggerInteraction.Ignore))
                {
                    continue;
                }
                if (hit.collider.GetComponent<PaintableSurface>() != null)
                {
                    result.PaintedArea += InkPainter.PaintSphere(hit.point, spec.PaintRayRadius, hardness, source);
                    result.Splats++;
                }
                if (fx != null && i < spec.FxBursts)
                {
                    fx.Spawn(hit.point, hit.normal, color);
                }
            }

            // 四散墨滴:落在爆心周圍 0.5~1.0 × DropReach 的地面(S3 SplashAroundParam 的落點分佈,[EST] 均勻)
            float dropReach = spec.EffectiveDropReach;
            for (int i = 0; i < spec.DropCount; i++)
            {
                float angle = (i + Random.value * 0.8f) * Mathf.PI * 2f / spec.DropCount;
                float distance = dropReach * (0.5f + Random.value * 0.5f);
                Vector3 origin = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance
                    + Vector3.up * 2f;
                if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 8f, paintMask,
                        QueryTriggerInteraction.Ignore))
                {
                    result.PaintedArea += InkPainter.PaintSphere(hit.point, spec.DropRadius, hardness, source);
                    result.Splats++;
                }
            }
        }
    }
}

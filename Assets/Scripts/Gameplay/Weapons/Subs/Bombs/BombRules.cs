using SplatoonC.Core;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 投擲物一步路徑上碰到的東西
    public enum BombContact : byte
    {
        None = 0,
        // 地形(地面、牆、天花板、格柵)
        Terrain = 1,
        // 敵方(或中立)可受傷目標:角色、假人、場上裝置(只有會「打到人」的炸彈才回報,例:Burst Bomb)
        Target = 2,
        // 會讓炸彈立刻爆炸的物件(敵方 Splash Wall / Big Bubbler / 傘面…)
        Detonator = 3,
    }

    // 炸彈共通規則(DESIGN_REFERENCE §4.3、research 03 §1):
    //   - 碰到敵方 Splash Wall、傘面、Big Bubbler、Rainmaker 屏障、變身中的 Torpedo 立刻爆炸(IBombDetonator,
    //     沒實作的場上裝置以 DeployableObject.BlocksAttacksFrom 判定);
    //   - 同隊的東西一律穿過(Splatoon 沒有友軍碰撞);
    //   - 爆炸傷害的視線與擊退由 Explosion 處理。
    // CastPath:一步移動的 sphere cast(地形 + 目標圖層),回傳最近的有效接觸。零配置(共用緩衝,主執行緒)。
    // TouchDetonator:貼地滑行 / 走路的炸彈(Curling、Autobomb)移動只查地形,每步用它補上「碰到敵方屏障就爆」。
    // CastDetonator:只找引爆物的 sphere cast(ThrowController 預設拋物線預覽用:預覽也停在敵方屏障 / 球罩前)。
    // Big Bubbler 的球罩(BarrierSphere)沒有 Collider,兩者都另外用 BarrierSphere.Raycast 查:從罩外碰到敵方球面就引爆,
    // 爆心留在球面外(罩內的人被球面擋住爆炸視線)。
    // 敵方 Ink Vac 的吸引漩渦(InkVacuum)也當成引爆物回報,爆心在吸引範圍內 → Explosion 整個被吸走(S3:炸彈會被吸進去)。
    public static class BombRules
    {
        private const int BufferSize = 16;
        // 碰到球罩時回報的接觸點沿法線外推:SphereBarrierMath.Contains 含球面,爆心剛好落在球面上會被當成罩內而不擋
        private const float ShellSkin = 0.1f;
        private static readonly RaycastHit[] Hits = new RaycastHit[BufferSize];
        private static readonly Collider[] Overlaps = new Collider[BufferSize];

        public static bool Detonates(Collider collider, Team bombTeam)
        {
            if (collider == null)
            {
                return false;
            }
            var detonator = collider.GetComponentInParent<IBombDetonator>();
            if (detonator != null)
            {
                return detonator.DetonatesBombsFrom(bombTeam);
            }
            var device = collider.GetComponentInParent<DeployableObject>();
            return device != null && device.IsAlive && device.BlocksAttacksFrom(bombTeam);
        }

        // 這一步掃過的膠囊(from → to,半徑 radius;from = to 時就是一顆球)碰到會引爆這隊炸彈的物件嗎?
        // center:炸彈從來的方向貼著引爆物表面的位置——爆炸在這裡解算,不會穿進屏障後面炸(屏障擋得住敵方爆炸視線)。
        // 只查目標圖層(場上裝置、角色),零配置(共用緩衝,主執行緒)。
        public static bool TouchDetonator(Vector3 from, Vector3 to, float radius, Team team, out Vector3 center)
        {
            center = to;
            int count = Physics.OverlapCapsuleNonAlloc(from, to, Mathf.Max(0.01f, radius), Overlaps,
                CombatMasks.Targets, QueryTriggerInteraction.Ignore);
            bool found = false;
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                Collider collider = Overlaps[i];
                Overlaps[i] = null;
                if (collider == null || !Detonates(collider, team))
                {
                    continue;
                }
                // 非凸網格碰撞體不支援 ClosestPoint:退回包圍盒
                Vector3 surface = collider is MeshCollider mesh && !mesh.convex
                    ? collider.bounds.ClosestPoint(from)
                    : collider.ClosestPoint(from);
                float distance = Vector3.Distance(from, surface);
                if (distance >= best)
                {
                    continue;
                }
                best = distance;
                found = true;
                center = distance > 1e-4f ? surface + (from - surface) / distance * Mathf.Min(radius, distance) : from;
            }
            // 敵方球罩(沒有 Collider):這一步從罩外碰到球面 → 停在球面外那一點(炸彈中心,離球面 radius)
            Vector3 step = to - from;
            float length = step.magnitude;
            if (length > 1e-6f && BarrierSphere.Raycast(team, from, step / length, length, radius, out BarrierHit barrier)
                && (!found || barrier.Distance < best))
            {
                found = true;
                best = barrier.Distance;
                center = from + step / length * barrier.Distance;
            }
            // 敵方 Ink Vac 吸引範圍:炸彈中心進入範圍 → 在範圍內那一點「爆炸」= 被吸走
            if (length > 1e-6f && InkVacuum.Active.Count > 0
                && InkVacuum.Segment(team, from, step / length, length, 0f, out _, out float vacuumDistance)
                && (!found || vacuumDistance < best))
            {
                found = true;
                center = from + step / length * vacuumDistance;
            }
            return found;
        }

        // 只找「會引爆這隊炸彈」的東西:from 沿 direction 掃 distance(半徑 radius;0 = 射線粗細 0.01)。
        // 給只查地形的拋物線預覽(ThrowController 預設預覽)補上「停在敵方屏障 / 球罩」:hit.point / normal 為接觸點與法線。
        public static bool CastDetonator(Vector3 from, Vector3 direction, float distance, float radius, Team team,
            out RaycastHit hit)
        {
            hit = default;
            if (distance <= 1e-6f)
            {
                return false;
            }
            float best = float.MaxValue;
            bool found = false;
            int count = Physics.SphereCastNonAlloc(from, Mathf.Max(0.01f, radius), direction, Hits, distance,
                CombatMasks.Targets, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                RaycastHit h = Hits[i];
                Hits[i] = default;
                if (h.collider == null || h.distance >= best || !Detonates(h.collider, team))
                {
                    continue;
                }
                if (h.distance <= 0f && h.point == Vector3.zero)
                {
                    h.point = from;
                    h.normal = -direction;
                }
                best = h.distance;
                hit = h;
                found = true;
            }
            if (BarrierSphere.Raycast(team, from, direction, found ? best : distance, radius, out BarrierHit barrier))
            {
                hit = default;
                hit.distance = barrier.Distance;
                hit.point = barrier.Point;
                hit.normal = barrier.Normal;
                found = true;
            }
            return found;
        }

        // from → to 以半徑 radius 掃過:地形(CombatMasks.Terrain)、會引爆的物件,以及(hitTargets 時)敵方/中立的
        // IDamageable。回傳最近的一個;起點就重疊的碰撞體視為在起點接觸(地形除外:貼地時的相切不算)。
        public static BombContact CastPath(Vector3 from, Vector3 to, float radius, Team team, bool hitTargets,
            out RaycastHit hit, out IDamageable target)
        {
            hit = default;
            target = null;
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 1e-6f)
            {
                return BombContact.None;
            }
            Vector3 direction = delta / distance;
            int count = Physics.SphereCastNonAlloc(from, Mathf.Max(0.01f, radius), direction, Hits, distance,
                CombatMasks.Terrain | CombatMasks.Targets, QueryTriggerInteraction.Ignore);
            BombContact best = BombContact.None;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                RaycastHit h = Hits[i];
                Collider collider = h.collider;
                if (collider == null || h.distance >= bestDistance)
                {
                    continue;
                }
                bool overlapAtStart = h.distance <= 0f && h.point == Vector3.zero;
                int layerBit = 1 << collider.gameObject.layer;
                BombContact kind;
                IDamageable damageable = null;
                if ((layerBit & CombatMasks.Targets) == 0)
                {
                    if (overlapAtStart)
                    {
                        continue;
                    }
                    kind = BombContact.Terrain;
                }
                else if (Detonates(collider, team))
                {
                    kind = BombContact.Detonator;
                }
                else if (hitTargets)
                {
                    damageable = collider.GetComponentInParent<IDamageable>();
                    if (damageable == null || !damageable.IsAlive || !DamageRules.CanDamage(team, damageable))
                    {
                        continue;
                    }
                    kind = BombContact.Target;
                }
                else
                {
                    continue;
                }
                if (overlapAtStart)
                {
                    h.point = from;
                    h.normal = -direction;
                }
                best = kind;
                bestDistance = h.distance;
                hit = h;
                target = damageable;
            }
            // 靜態緩衝不留參照
            for (int i = 0; i < count; i++)
            {
                Hits[i] = default;
            }
            // 敵方球罩(沒有 Collider,sphere cast 看不到):比其他接觸更近就算碰到引爆物
            float reach = best == BombContact.None ? distance : Mathf.Min(distance, bestDistance);
            if (BarrierSphere.Raycast(team, from, direction, reach, radius, out BarrierHit barrier))
            {
                best = BombContact.Detonator;
                hit = default;
                hit.distance = barrier.Distance;
                hit.point = barrier.Point + barrier.Normal * ShellSkin;
                hit.normal = barrier.Normal;
                target = null;
                reach = barrier.Distance;
            }
            // 敵方 Ink Vac 吸引範圍(沒有 Collider):炸彈中心先進入範圍 → 當成引爆物,爆心在範圍內 → 整個爆炸被吸走
            if (InkVacuum.Active.Count > 0
                && InkVacuum.Segment(team, from, direction, reach, 0f, out _, out float vacuumDistance))
            {
                best = BombContact.Detonator;
                hit = default;
                hit.distance = vacuumDistance;
                hit.point = from + direction * vacuumDistance;
                hit.normal = -direction;
                target = null;
            }
            return best;
        }
    }
}

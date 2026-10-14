using SplatoonC.Core;
using SplatoonC.Core.Combat;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // Angle Shooter 走一段路徑時遇到的事件
    public enum AngleShooterEvent : byte
    {
        // 這一段走完,沒碰到東西
        None = 0,
        // 碰到地形反彈(牆面鏡射、地面出射角 × 0.1)
        Bounce = 1,
        // 直擊敵方/中立目標(角色、假人、場上裝置):停下
        Target = 2,
        // 撞上敵方 Big Bubbler 球罩:停下
        Barrier = 3,
    }

    // Angle Shooter 的直線路徑(AngleShooter 實飛與 AngleShooterConfig.BuildPreview 共用 → 預覽 = 實際):
    // 以碰撞半徑 0.25 的 sphere cast 前進,地形 → AngleShooterModel.Reflect 反彈並繼續,敵方/中立 IDamageable → 停下,
    // 同隊穿過;總長 Remaining(6.3 × 7 = 44.1 u)用完就停。零配置(共用緩衝,主執行緒)。
    // 格柵:專案場景目前沒有格柵圖層,S3「格柵以外都反彈」的例外暫無對象。
    public struct AngleShooterTrace
    {
        private const int BufferSize = 16;
        private const float Skin = 0.02f;
        private static readonly RaycastHit[] Hits = new RaycastHit[BufferSize];

        public Vector3 Position;
        public Vector3 Direction;
        // 剩下可以走的路徑長(公尺)
        public float Remaining;
        public int Bounces;

        public void Init(Vector3 origin, Vector3 direction, float range)
        {
            Position = origin;
            Direction = direction.sqrMagnitude > 1e-8f ? direction.normalized : Vector3.forward;
            Remaining = range;
            Bounces = 0;
        }

        public bool Finished => Remaining <= 1e-4f;

        // 往前走最多 distance(不超過 Remaining),停在第一個事件。travelled = 實際走了多少;
        // Bounce / Target 時 hit = 接觸資訊(point 在表面上);Target 時 target = 被打的目標;Barrier 時 barrier = 球罩
        public AngleShooterEvent Advance(in AngleShooterParams p, float distance, Team team, bool hitTargets,
            out float travelled, out RaycastHit hit, out IDamageable target, out BarrierHit barrier)
        {
            hit = default;
            target = null;
            barrier = default;
            float step = Mathf.Min(distance, Remaining);
            if (step <= 1e-5f)
            {
                travelled = 0f;
                return AngleShooterEvent.None;
            }
            Vector3 from = Position;
            Vector3 dir = Direction;
            float radius = Mathf.Max(0.01f, p.CollisionRadius);
            int mask = hitTargets ? CombatMasks.Terrain | CombatMasks.Targets : CombatMasks.Terrain;
            int count = Physics.SphereCastNonAlloc(from, radius, dir, Hits, step, mask, QueryTriggerInteraction.Ignore);
            AngleShooterEvent best = AngleShooterEvent.None;
            float bestDistance = step;
            for (int i = 0; i < count; i++)
            {
                RaycastHit h = Hits[i];
                Hits[i] = default;
                Collider collider = h.collider;
                if (collider == null || h.distance > bestDistance)
                {
                    continue;
                }
                bool overlapAtStart = h.distance <= 0f && h.point == Vector3.zero;
                int layerBit = 1 << collider.gameObject.layer;
                if ((layerBit & CombatMasks.Targets) == 0)
                {
                    // 起點就貼著的面(剛反彈離開)、或正在離開的面:不算
                    if (overlapAtStart || Vector3.Dot(h.normal, dir) >= -1e-4f)
                    {
                        continue;
                    }
                    best = AngleShooterEvent.Bounce;
                    bestDistance = h.distance;
                    hit = h;
                    target = null;
                    continue;
                }
                var damageable = collider.GetComponentInParent<IDamageable>();
                if (damageable == null || !damageable.IsAlive || !DamageRules.CanDamage(team, damageable))
                {
                    continue;
                }
                if (overlapAtStart)
                {
                    h.point = from;
                    h.normal = -dir;
                }
                best = AngleShooterEvent.Target;
                bestDistance = h.distance;
                hit = h;
                target = damageable;
            }
            if (hitTargets && BarrierSphere.Raycast(team, from, dir, bestDistance, radius, out BarrierHit shell)
                && shell.Distance < bestDistance)
            {
                best = AngleShooterEvent.Barrier;
                bestDistance = shell.Distance;
                barrier = shell;
                hit = default;
                hit.point = shell.Point;
                hit.normal = shell.Normal;
                hit.distance = shell.Distance;
                target = null;
            }
            travelled = best == AngleShooterEvent.None ? step : bestDistance;
            Position = from + dir * travelled;
            Remaining -= travelled;
            switch (best)
            {
                case AngleShooterEvent.Bounce:
                {
                    Vector3 normal = hit.normal.sqrMagnitude > 1e-6f ? hit.normal.normalized : -dir;
                    Direction = AngleShooterModel.Reflect(p, dir, normal);
                    Position += normal * Skin;
                    Bounces++;
                    break;
                }
                case AngleShooterEvent.Target:
                case AngleShooterEvent.Barrier:
                    Remaining = 0f;
                    break;
            }
            return best;
        }
    }
}

using System.Collections.Generic;
using SplatoonC.Core;
using SplatoonC.Core.Combat;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 攻擊撞上屏障的結果(BarrierSphere.Raycast)
    public struct BarrierHit
    {
        public BarrierSphere Barrier;
        // 沿射線的距離(公尺)、球面接觸點、朝外法線
        public float Distance;
        public Vector3 Point;
        public Vector3 Normal;
    }

    public delegate void BarrierDamageHandler(in DamageInfo info);

    // 單向球形屏障(S3 Big Bubbler 的球罩,research/04 §2.2、JP wiki グレートバリア):
    //   從罩外打進來的敵方子彈停在球面上並扣屏障耐久;敵方爆炸的視線被球面擋住(罩內的人炸不到);
    //   敵人本身可以走進罩內;罩內往外打、友軍的攻擊一律不擋;敵方炸彈碰到球面立刻引爆。
    // 攻擊端查詢(靜態、零配置):InkBullet / InkProjectile → Raycast、Explosion.IsBlocked → Blocks、炸彈飛行 → Raycast。
    // 本身是 IDamageable、展開期間登記在 DamageableRegistry:罩外的爆炸量到球面最近點扣耐久(罩內的爆炸炸不到屏障);
    // 受傷交給擁有者換算(S3:屏障受擊 ×0.64、與弱點共用耐久)。沒有 Collider——角色走得進來、瞄準射線穿得過。
    public sealed class BarrierSphere : IDamageable
    {
        // 罩內的點「最近點」回傳這麼遠的點:範圍攻擊(爆炸、龍捲風)從罩內打不到屏障本身
        private const float Unreachable = 10000f;

        public static readonly List<BarrierSphere> Active = new List<BarrierSphere>();

        private readonly BarrierDamageHandler _onDamaged;
        private bool _registered;

        public BarrierSphere(Component owner, BarrierDamageHandler onDamaged)
        {
            Owner = owner;
            _onDamaged = onDamaged;
        }

        // 屏障所屬的物件(Big Bubbler 裝置)
        public Component Owner { get; }
        public Team Team { get; private set; } = Team.None;
        public Vector3 Center { get; set; }
        public float Radius { get; set; }

        // 展開完成才擋(S3 設置前隙 50F 內攻擊會穿過)
        public bool Blocking { get; set; }

        public bool IsAlive => _registered && Blocking && Radius > 0f && Owner != null;

        // 測試/HUD:累計被擋下的攻擊次數
        public int BlockedHits { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetOnLoad()
        {
            Active.Clear();
        }

        // 屏障上場(登記進查詢清單與範圍傷害清單);可重複呼叫
        public void Enable(Team team)
        {
            Team = team;
            if (_registered)
            {
                return;
            }
            _registered = true;
            Active.Add(this);
            DamageableRegistry.Active.Add(this);
        }

        public void Disable()
        {
            Blocking = false;
            if (!_registered)
            {
                return;
            }
            _registered = false;
            Active.Remove(this);
            DamageableRegistry.Active.Remove(this);
        }

        // 會擋 attackerTeam 的攻擊嗎(自己隊伍不擋)
        public bool BlocksTeam(Team attackerTeam)
        {
            return IsAlive && attackerTeam != Team;
        }

        public bool Contains(Vector3 point)
        {
            return SphereBarrierMath.Contains(Center, Radius, point);
        }

        public Vector3 ClosestPoint(Vector3 from)
        {
            return SphereBarrierMath.ClosestOnShell(Center, Radius, from, out Vector3 point)
                ? point
                : from + Vector3.up * Unreachable;
        }

        public void ApplyDamage(in DamageInfo info)
        {
            if (!IsAlive || info.Amount <= 0f || (info.Team != Team.None && info.Team == Team))
            {
                return;
            }
            BlockedHits++;
            _onDamaged?.Invoke(info);
        }

        // ── 攻擊端查詢 ──

        // 半徑 castRadius 的攻擊沿 from + dir·s(s ≤ maxDistance)從外面打進敵方屏障:回傳最近的一個
        public static bool Raycast(Team attackerTeam, Vector3 from, Vector3 direction, float maxDistance, float castRadius,
            out BarrierHit hit)
        {
            hit = default;
            if (Active.Count == 0 || maxDistance <= 0f)
            {
                return false;
            }
            float bestDistance = float.MaxValue;
            bool found = false;
            for (int i = 0; i < Active.Count; i++)
            {
                BarrierSphere barrier = Active[i];
                if (!barrier.BlocksTeam(attackerTeam))
                {
                    continue;
                }
                if (SphereBarrierMath.Entry(barrier.Center, barrier.Radius, from, direction, maxDistance, castRadius,
                        out float distance, out Vector3 point, out Vector3 normal) && distance < bestDistance)
                {
                    bestDistance = distance;
                    hit = new BarrierHit { Barrier = barrier, Distance = distance, Point = point, Normal = normal };
                    found = true;
                }
            }
            return found;
        }

        // 範圍攻擊的視線:from(爆心)→ to(目標最近點)有沒有被敵方屏障擋住(目標就是該屏障時不算)
        public static bool Blocks(Team attackerTeam, Vector3 from, Vector3 to, IDamageable target)
        {
            for (int i = 0; i < Active.Count; i++)
            {
                BarrierSphere barrier = Active[i];
                if (ReferenceEquals(barrier, target) || !barrier.BlocksTeam(attackerTeam))
                {
                    continue;
                }
                if (SphereBarrierMath.BlocksSegment(barrier.Center, barrier.Radius, from, to))
                {
                    return true;
                }
            }
            return false;
        }

        // point 在不在 attackerTeam 的敵方屏障裡(bot / 測試用)
        public static BarrierSphere EnemyBarrierContaining(Team team, Vector3 point)
        {
            for (int i = 0; i < Active.Count; i++)
            {
                BarrierSphere barrier = Active[i];
                if (barrier.BlocksTeam(team) && barrier.Contains(point))
                {
                    return barrier;
                }
            }
            return null;
        }
    }
}

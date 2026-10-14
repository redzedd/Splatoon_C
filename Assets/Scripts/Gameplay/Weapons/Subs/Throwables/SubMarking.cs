using SplatoonC.Core;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 副武器的標記判定(Point Sensor 感應球、Angle Shooter 線段),標記本身交給 I2 的 Marking(穿牆、重複取長者)。
    // 只標記敵方角色(Marking 只作用於 Inkling;試射場假人沒有標記)。零配置(共用緩衝,主執行緒)。
    public static class SubMarking
    {
        private static readonly Collider[] Overlaps = new Collider[16];

        // 累計標記次數(測試用;每次 Marking.Apply 算一次,含刷新)
        public static int ApplyCount { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetOnLoad()
        {
            ApplyCount = 0;
        }

        // 半徑 radius 球內(量到角色膠囊最近點;不看視線 = 穿牆)的 byTeam 敵方角色標記 seconds 秒;回傳標記人數
        public static int MarkSphere(Vector3 center, float radius, Team byTeam, float seconds)
        {
            if (seconds <= 0f || byTeam == Team.None)
            {
                return 0;
            }
            var active = DamageableRegistry.Active;
            float radiusSq = radius * radius;
            int marked = 0;
            for (int i = 0; i < active.Count; i++)
            {
                if (!(active[i] is InklingHealth health) || !health.IsAlive || health.Team == byTeam
                    || health.Team == Team.None)
                {
                    continue;
                }
                if ((health.ClosestPoint(center) - center).sqrMagnitude > radiusSq)
                {
                    continue;
                }
                var inkling = health.GetComponent<Inkling>();
                if (inkling == null)
                {
                    continue;
                }
                Marking.Apply(inkling, byTeam, seconds);
                ApplyCount++;
                marked++;
            }
            return marked;
        }

        // 碰到線段 a → b(粗 radius)的 byTeam 敵方角色標記 seconds 秒;回傳標記人數
        public static int MarkSegment(Vector3 a, Vector3 b, float radius, Team byTeam, float seconds)
        {
            if (seconds <= 0f || byTeam == Team.None)
            {
                return 0;
            }
            int count = Physics.OverlapCapsuleNonAlloc(a, b, Mathf.Max(0.01f, radius), Overlaps, GameLayers.InklingMask,
                QueryTriggerInteraction.Ignore);
            int marked = 0;
            for (int i = 0; i < count; i++)
            {
                Collider collider = Overlaps[i];
                Overlaps[i] = null;
                if (collider == null)
                {
                    continue;
                }
                var inkling = collider.GetComponentInParent<Inkling>();
                if (inkling == null || inkling.Team == byTeam || inkling.Team == Team.None)
                {
                    continue;
                }
                var health = inkling.GetComponent<InklingHealth>();
                if (health != null && !health.IsAlive)
                {
                    continue;
                }
                Marking.Apply(inkling, byTeam, seconds);
                ApplyCount++;
                marked++;
            }
            return marked;
        }
    }
}

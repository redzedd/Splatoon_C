using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 池化快速炸彈(S3 Bomb_Quick):拋物線飛行(與預覽同一個 SimpleBallistics 步進,60 Hz),
    // 碰到地形、敵方/中立目標或會引爆炸彈的物件就立刻爆炸;同隊穿過。
    // 直擊敵人:先結算直擊 25(AttackClass.BombDirectHit),再以接觸點爆炸(近距 35 含直擊目標)→ 共 60。
    public sealed class BurstBomb : SimulatedBomb
    {
        private BurstBombConfig _config;
        private Vector3 _velocity;
        private Vector3 _spin;

        // 測試/除錯:累計爆炸數與直擊數、最近一次的爆心/時間/直擊目標
        public static int ExplosionCount { get; private set; }
        public static int DirectHitCount { get; private set; }
        public static Vector3 LastExplosionCenter { get; private set; }
        public static float LastExplosionTime { get; private set; }
        public static float LastLaunchTime { get; private set; }
        public static IDamageable LastDirectTarget { get; private set; }
        public static BombContact LastContact { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetCounters()
        {
            ExplosionCount = 0;
            DirectHitCount = 0;
            LastDirectTarget = null;
            LastExplosionTime = float.NegativeInfinity;
            LastLaunchTime = float.NegativeInfinity;
        }

        public override void Launch(Vector3 position, Vector3 velocity, SubWeaponConfig config, Inkling owner)
        {
            _config = config as BurstBombConfig;
            if (_config == null)
            {
                Debug.LogError($"BurstBomb:設定型別不符({config})", this);
                Despawn();
                return;
            }
            Begin(position, owner);
            _velocity = velocity;
            // 飛行中翻滾(視覺)
            _spin = new Vector3(Random.Range(-540f, 540f), Random.Range(-180f, 180f), Random.Range(-540f, 540f));
            transform.rotation = Random.rotation;
            LastLaunchTime = Time.time;
        }

        protected override bool Step()
        {
            if (Frame > SimFrames.ToFrames(_config.Lifetime))
            {
                Release();
                return false;
            }
            Vector3 from = SimPosition;
            Vector3 to = from;
            Vector3 velocity = _velocity;
            SimpleBallistics.Step(ref to, ref velocity, _config.Gravity, SimFrames.Dt);
            BombContact contact = BombRules.CastPath(from, to, _config.Radius, BombTeam, true, out RaycastHit hit,
                out IDamageable target);
            if (contact == BombContact.None)
            {
                SimPosition = to;
                _velocity = velocity;
                return true;
            }
            Vector3 delta = to - from;
            float length = delta.magnitude;
            Vector3 direction = length > 1e-6f ? delta / length : Vector3.down;
            SimPosition = from + direction * hit.distance;
            LastContact = contact;
            if (contact == BombContact.Target && target != null)
            {
                // S3:直擊 25 在投擲者端先算,近距爆炸 35 下一幀到(這裡同一幀依序結算)
                DirectHitCount++;
                LastDirectTarget = target;
                target.ApplyDamage(new DamageInfo(_config.DirectDamage, hit.point, BombTeam, Owner, DamageKind.Sub,
                    AttackClass.BombDirectHit));
            }
            // 爆心:地形 = 接觸點沿法線外推一點(噴墨射線才不會從面裡面打);目標 = 接觸點
            Vector3 center = contact == BombContact.Terrain ? hit.point + hit.normal * 0.1f : hit.point;
            ExplosionCount++;
            LastExplosionCenter = center;
            LastExplosionTime = Time.time;
            Explosion.Detonate(_config.Blast, center, BombTeam, Owner);
            Release();
            return false;
        }

        protected override void OnReleased()
        {
            _config = null;
        }

        protected override void UpdateVisual()
        {
            transform.Rotate(_spin * Time.deltaTime, Space.Self);
        }
    }
}

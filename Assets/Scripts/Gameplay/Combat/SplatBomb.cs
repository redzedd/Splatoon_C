using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 池化斯普拉炸彈:單一重力彈道 + SphereCast 碰撞(球體貼著面停,不半埋);任何面都反彈,
    // 慢下來且面朝上就停;引信只在「貼地」時倒數(S3:60F,空中暫停——從高處丟下要落地後才開始算),
    // 到時交給共用的 Explosion 解算近/遠傷害、擊退並向四周噴墨。
    public sealed class SplatBomb : SubProjectile
    {
        private const float ContactSkin = 0.01f;

        private SplatBombConfig _config;
        private Vector3 _velocity;
        private float _fuse;
        private int _beeps;
        private float _life;
        private bool _resting;
        private bool _released;
        private float _lastGroundContact = float.NegativeInfinity;
        private Team _team;

        // 測試用:最近一次爆心與累計爆炸數
        public static int ExplosionCount { get; private set; }
        public static Vector3 LastExplosionCenter { get; private set; }

        public bool IsResting => _resting;

        public Team Team => _team;

        public override void Launch(Vector3 position, Vector3 velocity, SubWeaponConfig config, Inkling owner)
        {
            _config = config as SplatBombConfig;
            if (_config == null)
            {
                Debug.LogError($"SplatBomb:設定型別不符({config})", this);
                Despawn();
                return;
            }
            Owner = owner;
            _team = owner != null ? owner.Team : Team.Alpha;
            TeamTint.ApplyAll(this, TeamPalette.ColorOf(_team));
            transform.position = position;
            transform.localScale = Vector3.one * (_config.VisualRadius * 2f);
            _velocity = velocity;
            _fuse = _config.FuseSeconds;
            _beeps = 0;
            _life = _config.Lifetime;
            _resting = false;
            _released = false;
            _lastGroundContact = float.NegativeInfinity;
        }

        private void Update()
        {
            if (_config == null)
            {
                return;
            }
            float dt = Time.deltaTime;
            // 貼地(靜止或 0.1 秒內碰過朝上的面)才倒數
            bool grounded = _resting || Time.time - _lastGroundContact < 0.1f;
            if (grounded)
            {
                // 引信開始倒數與過半時各嗶一聲(S3 落地閃爍)
                if (_beeps == 0 || (_beeps == 1 && _fuse <= _config.FuseSeconds * 0.45f))
                {
                    _beeps++;
                    GameAudio.Play("bomb_fuse_beep", transform.position, 1f, 0f);
                }
                _fuse -= dt;
            }
            if (_fuse <= 0f)
            {
                Explode();
                return;
            }
            _life -= dt;
            if (_life <= 0f)
            {
                Release();
                return;
            }
            if (_resting)
            {
                return;
            }

            Vector3 previous = transform.position;
            Vector3 next = previous;
            Vector3 velocity = _velocity;
            SimpleBallistics.Step(ref next, ref velocity, _config.Gravity, dt);
            Vector3 delta = next - previous;
            float distance = delta.magnitude;
            RaycastHit hit = default;
            bool touched = distance > 0.0001f && Physics.SphereCast(previous, _config.VisualRadius, delta / distance,
                out hit, distance, _config.HitMask, QueryTriggerInteraction.Ignore);
            // S3 炸彈共通規則(§4.3):碰到敵方屏障(Big Bubbler 球罩)立刻引爆——爆心在球面外,罩內的人炸不到
            if (distance > 0.0001f && BarrierSphere.Raycast(_team, previous, delta / distance,
                    touched ? hit.distance : distance, _config.VisualRadius, out BarrierHit barrier))
            {
                transform.position = previous + delta / distance * barrier.Distance;
                Explode();
                return;
            }
            // 敵方 Ink Vac 吸引範圍:飛進去就在範圍內「爆炸」= 整個被吸走(Explosion 判定爆心在範圍內)
            if (distance > 0.0001f && InkVacuum.Active.Count > 0 && InkVacuum.Segment(_team, previous, delta / distance,
                    touched ? hit.distance : distance, 0f, out _, out float vacuumAt))
            {
                transform.position = previous + delta / distance * vacuumAt;
                Explode();
                return;
            }
            if (touched)
            {
                // S3 炸彈共通規則:碰到敵方 Splash Wall / 傘面 / Big Bubbler 等(BombRules.Detonates)立刻爆炸
                if (BombRules.Detonates(hit.collider, _team))
                {
                    transform.position = previous + delta / distance * hit.distance;
                    Explode();
                    return;
                }
                // 接觸點沿法線外推一點皮:球貼著面時 SphereCast 會忽略「起點就重疊」的 collider,
                // 貼地滑行的下一幀就會穿過地面一路掉下去(2026-09-08 實測:爆心 y=-39)。
                transform.position = previous + delta / distance * hit.distance + hit.normal * ContactSkin;
                Vector3 reflected = SimpleBallistics.Reflect(
                    velocity, hit.normal, _config.Restitution, _config.TangentialFriction);
                float impact = Mathf.Abs(Vector3.Dot(velocity, hit.normal));
                if (impact > 1.5f)
                {
                    GameAudio.Play("bomb_bounce", transform.position, Mathf.InverseLerp(1.5f, 10f, impact) * 0.7f + 0.3f);
                }
                if (hit.normal.y > 0.5f)
                {
                    _lastGroundContact = Time.time;
                }
                // 面朝上且反彈的垂直速度已小 → 停住(彈一下就停,Splatoon 手感);否則(牆、假人、快速落地)彈開
                if (hit.normal.y > 0.5f && Mathf.Abs(Vector3.Dot(reflected, hit.normal)) < _config.RestSpeed)
                {
                    _resting = true;
                    _velocity = Vector3.zero;
                }
                else
                {
                    _velocity = reflected;
                }
                return;
            }
            transform.position = next;
            _velocity = velocity;
        }

        private void Explode()
        {
            Vector3 center = transform.position;
            ExplosionCount++;
            LastExplosionCenter = center;
            // 共用爆炸解算(Explosion):近/遠傷害量到目標最近點、視線(遮罩沿用設定:HitMask 扣掉目標圖層)、
            // 地面主塗墨 + 噴墨射線、擊退(S3 必殺炸彈 700 / 12 u)、音效與閃光
            int losMask = _config.HitMask.value & ~_config.TargetMask.value;
            ExplosionSpec spec = _config.ToExplosionSpec();
            Explosion.Detonate(spec, center, _team, Owner, losMask, _config.HitMask.value, 0, 1f, null);
            Release();
        }

        private void Release()
        {
            if (_released)
            {
                return;
            }
            _released = true;
            _config = null;
            Despawn();
        }
    }
}

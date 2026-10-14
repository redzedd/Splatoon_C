using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 池化冰壺炸彈(S3 Bomb_Curling):CurlingMotion 逐幀低拋 → 貼地滑行(牆面鏡射、爬小台階、滑出邊緣掉落),
    // 引信從放開起算 210 − 2 × 蓄力幀,時間到就地爆炸(空中也爆);滑行時每 TrailSpacing 公尺塗一筆可游的自隊墨;
    // 滑過敵人(角色、假人、場上裝置)造成 20 傷(AttackClass.CurlingContact,對裝置 ×100 等由 DamageRates 套),
    // 同一目標每 40F 最多一次,角色另受小擊退(350、仰角 60°)。指示燈綠 → 黃 → 紅,剩 90F 起閃爍 + 警告音。
    // 炸彈共通規則(§4.3):滑行/飛行中碰到敵方 Splash Wall 等引爆物(BombRules.TouchDetonator)立刻爆炸,不吃接觸傷害。
    public sealed class CurlingBomb : SimulatedBomb
    {
        private const int MaxContacts = 8;
        private static readonly Collider[] Overlaps = new Collider[16];

        private CurlingBombConfig _config;
        private CurlingBombParams _p;
        private CurlingMotion _motion;
        private float _trailPending;
        private bool _warned;
        private AudioLoop _slideLoop;
        private float _spin;
        private readonly IDamageable[] _contactTargets = new IDamageable[MaxContacts];
        private readonly int[] _contactFrames = new int[MaxContacts];
        private int _contactCount;
        private float _trailArea;
        private float _maxGroundSpeed;
        private int _bounces;

        // 測試/除錯:累計爆炸數、接觸傷害次數、反彈次數;最近一次的爆心/時間/引信/蓄力/軌跡塗地
        public static int ExplosionCount { get; private set; }
        public static int ContactHitCount { get; private set; }
        public static int BounceCount { get; private set; }
        // 碰到引爆物(敵方 Splash Wall 等)提前爆炸的次數
        public static int DetonatorHitCount { get; private set; }
        public static Vector3 LastExplosionCenter { get; private set; }
        public static float LastExplosionTime { get; private set; }
        public static float LastLaunchTime { get; private set; }
        public static int LastFuseFrames { get; private set; }
        public static int LastExplosionFrame { get; private set; }
        public static float LastCharge01 { get; private set; }
        public static float LastTrailArea { get; private set; }
        public static float LastMaxGroundSpeed { get; private set; }
        public static float LastNearRadius { get; private set; }
        public static float LastFarRadius { get; private set; }

        public bool IsGrounded => _motion.Grounded;
        public int RestFrames => _motion.RestFrames;
        public int FuseFrames => _motion.Fuse;
        public float Charge01 => _motion.Charge01;
        public int Bounces => _bounces;
        public float TrailArea => _trailArea;
        // 目前水平速度(m/s)
        public float GroundSpeed => new Vector2(_motion.Velocity.x, _motion.Velocity.z).magnitude * SimFrames.Rate;
        public Vector3 Heading => _motion.Heading;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetCounters()
        {
            ExplosionCount = 0;
            ContactHitCount = 0;
            BounceCount = 0;
            DetonatorHitCount = 0;
            LastExplosionTime = float.NegativeInfinity;
            LastLaunchTime = float.NegativeInfinity;
        }

        public override void Launch(Vector3 position, Vector3 velocity, SubWeaponConfig config, Inkling owner)
        {
            LaunchCharged(position, velocity, config, owner, 0f);
        }

        public override void LaunchCharged(Vector3 position, Vector3 velocity, SubWeaponConfig config, Inkling owner,
            float charge01)
        {
            _config = config as CurlingBombConfig;
            if (_config == null)
            {
                Debug.LogError($"CurlingBomb:設定型別不符({config})", this);
                Despawn();
                return;
            }
            Begin(position, owner);
            _p = _config.S3;
            _motion.Init(position, velocity * SimFrames.Dt, charge01, _p, _config.Radius, _config.StepHeight);
            _trailPending = 0f;
            _warned = false;
            _spin = Random.Range(0f, 360f);
            _contactCount = 0;
            _trailArea = 0f;
            _maxGroundSpeed = 0f;
            _bounces = 0;
            LastLaunchTime = Time.time;
            LastCharge01 = _motion.Charge01;
            LastFuseFrames = _motion.Fuse;
            SetLightColor(IndicatorColor(0f));
            ApplyPose();
        }

        protected override bool Step()
        {
            bool alive = _motion.Step(_p);
            SimPosition = _motion.Position;
            // S3 §4.3:碰到敵方 Splash Wall / 傘面 / Big Bubbler 立刻爆炸(滑行只查地形,屏障在目標圖層,這裡補上;
            // 爆心停在屏障這一側的表面,屏障本身吃 180 × 2.0,後面的人被它擋住視線)
            if (BombRules.TouchDetonator(PrevPosition, SimPosition, _config.Radius, BombTeam, out Vector3 touch))
            {
                _motion.Position = touch;
                SimPosition = touch;
                DetonatorHitCount++;
                Explode();
                return false;
            }
            if (_motion.Landed)
            {
                GameAudio.Play("bomb_bounce", _motion.GroundPoint, 0.5f);
                if (_slideLoop == null)
                {
                    _slideLoop = GameAudio.StartLoop(_config.SlideLoopSound, transform, true, 0.7f);
                }
            }
            if (_motion.Bounced)
            {
                _bounces++;
                BounceCount++;
                GameAudio.Play(_config.BounceSound, SimPosition, 0.8f);
            }
            if (_motion.Grounded)
            {
                float speed = new Vector2(_motion.Velocity.x, _motion.Velocity.z).magnitude;
                _maxGroundSpeed = Mathf.Max(_maxGroundSpeed, speed * SimFrames.Rate);
                PaintTrail();
                if (_slideLoop != null)
                {
                    _slideLoop.SetVolume(Mathf.Clamp01(speed / Mathf.Max(0.01f, _p.BaseSpeedMinCharge)));
                }
            }
            else if (_slideLoop != null)
            {
                _slideLoop.SetVolume(0f);
            }
            ResolveContacts();
            if (!_warned && _motion.RestFrames <= _p.SlowdownRestFrames)
            {
                // S3 WarningAnimRestFrame 90:剩 90F 起閃爍警告(同時開始減速)
                _warned = true;
                GameAudio.Play(_config.WarningSound, SimPosition, 0.8f, 0f);
            }
            if (!alive)
            {
                Explode();
                return false;
            }
            return true;
        }

        // 軌跡:每滑 TrailSpacing 公尺在腳下地面塗一筆(r 依蓄力 1.075 → 1.29),可游的自隊墨
        private void PaintTrail()
        {
            float spacing = Mathf.Max(0.1f, _config.TrailSpacing);
            if (_motion.Landed)
            {
                // 落地點先塗一筆
                _trailPending = 0f;
            }
            else
            {
                _trailPending += _motion.GroundTravel;
                if (_trailPending < spacing)
                {
                    return;
                }
                _trailPending = Mathf.Repeat(_trailPending, spacing);
            }
            InkSource source = Owner != null ? Owner.InkSource() : new InkSource(BombTeam);
            _trailArea += InkPainter.PaintSphere(_motion.GroundPoint,
                CurlingBombModel.TrailRadius(_p, _motion.Charge01), _config.PaintHardness, source);
        }

        // 滑過敵人:20 傷,同一目標每 40F 最多一次;角色另受小擊退(S3 Accel 350、60°)
        private void ResolveContacts()
        {
            int count = Physics.OverlapSphereNonAlloc(_motion.Position, _config.ContactRadius, Overlaps,
                CombatMasks.Targets, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider collider = Overlaps[i];
                Overlaps[i] = null;
                if (collider == null)
                {
                    continue;
                }
                // 引爆物(敵方 Splash Wall 等)不吃接觸傷害:本體碰到時直接爆炸(Step 開頭的 TouchDetonator)
                if (BombRules.Detonates(collider, BombTeam))
                {
                    continue;
                }
                var target = collider.GetComponentInParent<IDamageable>();
                if (target == null || !target.IsAlive || !DamageRules.CanDamage(BombTeam, target)
                    || !ContactReady(target))
                {
                    continue;
                }
                Vector3 point = target.ClosestPoint(_motion.Position);
                target.ApplyDamage(new DamageInfo(_p.ContactDamage, point, BombTeam, Owner, DamageKind.Sub,
                    AttackClass.CurlingContact));
                ContactHitCount++;
                if (target is InklingHealth health && target.IsAlive)
                {
                    var locomotion = health.GetComponent<PlayerLocomotion>();
                    if (locomotion != null && locomotion.isActiveAndEnabled)
                    {
                        locomotion.AddImpulse(ContactImpulse(point));
                    }
                }
            }
        }

        private bool ContactReady(IDamageable target)
        {
            for (int i = 0; i < _contactCount; i++)
            {
                if (ReferenceEquals(_contactTargets[i], target))
                {
                    if (Frame - _contactFrames[i] < _p.ContactIntervalFrames)
                    {
                        return false;
                    }
                    _contactFrames[i] = Frame;
                    return true;
                }
            }
            int slot = _contactCount < MaxContacts ? _contactCount++ : OldestContact();
            _contactTargets[slot] = target;
            _contactFrames[slot] = Frame;
            return true;
        }

        private int OldestContact()
        {
            int oldest = 0;
            for (int i = 1; i < _contactCount; i++)
            {
                if (_contactFrames[i] < _contactFrames[oldest])
                {
                    oldest = i;
                }
            }
            return oldest;
        }

        // 接觸擊退 [EST]:水平速度 = Accel × (1 − d/Distance) × KnockbackModel.SpeedPerAccel,再依仰角分成水平與向上
        private Vector3 ContactImpulse(Vector3 targetPoint)
        {
            Vector3 offset = targetPoint - _motion.Position;
            float distance = offset.magnitude;
            float range = Mathf.Max(0.01f, _config.ContactKnockbackRange);
            float speed = _config.ContactKnockbackAccel * Mathf.Max(0f, 1f - distance / range)
                          * KnockbackModel.SpeedPerAccel;
            Vector3 flat = new Vector3(offset.x, 0f, offset.z);
            if (flat.sqrMagnitude < 1e-6f)
            {
                flat = _motion.Heading;
            }
            flat.Normalize();
            float rad = _config.ContactKnockbackDegree * Mathf.Deg2Rad;
            return flat * (speed * Mathf.Cos(rad)) + Vector3.up * (speed * Mathf.Sin(rad));
        }

        private void Explode()
        {
            ExplosionSpec spec = _config.BlastAt(_motion.Charge01);
            Vector3 center = _motion.Position;
            ExplosionCount++;
            LastExplosionCenter = center;
            LastExplosionTime = Time.time;
            LastExplosionFrame = _motion.Frame;
            LastTrailArea = _trailArea;
            LastMaxGroundSpeed = _maxGroundSpeed;
            LastNearRadius = spec.NearRadius;
            LastFarRadius = spec.FarRadius;
            Explosion.Detonate(spec, center, BombTeam, Owner);
            Release();
        }

        protected override void OnReleased()
        {
            if (_slideLoop != null)
            {
                _slideLoop.Stop(0.1f);
                _slideLoop = null;
            }
            for (int i = 0; i < MaxContacts; i++)
            {
                _contactTargets[i] = null;
            }
            _contactCount = 0;
            _config = null;
        }

        protected override void UpdateVisual()
        {
            float speed = new Vector2(_motion.Velocity.x, _motion.Velocity.z).magnitude;
            // 冰壺慢慢自轉(速度越快轉越快)
            _spin = Mathf.Repeat(_spin + speed * SimFrames.Rate * 40f * Time.deltaTime, 360f);
            ApplyPose();
            float elapsed = _motion.Fuse > 0 ? (float)_motion.Frame / _motion.Fuse : 1f;
            SetLightColor(IndicatorColor(elapsed));
            SetLights(!_warned || Blink(Time.time, _motion.RestFrames < 30 ? 0.1f : 0.25f));
            if (_warned)
            {
                float t = 1f - Mathf.Clamp01((float)_motion.RestFrames / Mathf.Max(1, _p.SlowdownRestFrames));
                SetSwell(1f + 0.12f * t * t);
            }
        }

        // 貼地時順著地面法線;空中保持水平
        private void ApplyPose()
        {
            Vector3 up = _motion.Grounded ? _motion.GroundNormal : Vector3.up;
            transform.rotation = Quaternion.FromToRotation(Vector3.up, up) * Quaternion.Euler(0f, _spin, 0f);
        }

        // 指示燈:綠 → 黃 → 紅(S3 燈號由綠轉紅)
        private static Color IndicatorColor(float elapsed01)
        {
            Color green = new Color(0.25f, 1f, 0.3f);
            Color yellow = new Color(1f, 0.9f, 0.2f);
            Color red = new Color(1f, 0.2f, 0.15f);
            return elapsed01 < 0.5f
                ? Color.Lerp(green, yellow, elapsed01 * 2f)
                : Color.Lerp(yellow, red, (elapsed01 - 0.5f) * 2f);
        }
    }
}

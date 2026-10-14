using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // Wave Breaker 的場上裝置(research/04 §2.7、JP wiki ホップソナー):
    //   飛行:ThrowController 算好的初速 + 投擲物單一重力(與拋物線預覽同一步進),穿過角色與屏障;
    //         碰到牆 → 抵消垂直牆面的速度沿牆落下(JP wiki);碰到地面(法線朝上)→ 設置。
    //   設置:耐久 480(DeployableObject,TargetClass.WaveBreaker);第 4–57F 設置感應(半徑 3.8 → 12.3)標記附近敵人,
    //         標記一律在設置後第 102F 結束;第 90 / 240 / 390F 各放一道衝擊波(WaveBreakerWave),放完第三波後收起。
    //   接觸:敵人碰到裝置(頭部半徑 0.75、柱高 2.75)吃 30 + 擊退,同一人每 0.5 秒一次。
    //   被打壞 → 擁有者能量鎖定立刻歸 0;已放出的波照樣擴張。設置在敵方 Big Bubbler 球面上 → 立刻消失(JP wiki)。
    //   擁有者被擊倒不影響(S3 鎖定型)。彈跳球依 SphereAnimCurve 在兩波之間慢慢升起、放波時落下。
    public sealed class WaveBreakerDevice : DeployableObject
    {
        private const int MaxTracked = 16;
        private const float FlightRadius = 0.25f;
        private const float MaxFlightSeconds = 6f;
        private const float BodyRadius = 0.4f;

        [SerializeField, Tooltip("彈跳球(放波前升起、放波時落下)")]
        private Transform _ball;

        [SerializeField, Tooltip("彈跳球的最低 / 最高本地高度(公尺)")]
        private Vector2 _ballHeight = new Vector2(1.05f, 2.1f);

        private WaveBreakerConfig _config;
        private PlayerSpecialGauge _gauge;
        private int _lockToken;
        private bool _flying;
        private Vector3 _velocity;
        private float _thrownAt;
        private float _placedAt;
        private int _wavesEmitted;
        private Team _flightTeam;
        private readonly Inkling[] _sensed = new Inkling[MaxTracked];
        private int _sensedCount;
        private readonly Inkling[] _contactTargets = new Inkling[MaxTracked];
        private readonly float[] _contactReadyAt = new float[MaxTracked];
        private int _contactCount;
        private Renderer[] _neutralRenderers;
        private bool _flashState;

        public override float MaxHp => _config != null ? _config.MaxHp : 480f;
        public override TargetClass TargetClass => TargetClass.WaveBreaker;

        // ── 狀態(測試、bot)──
        public bool Flying => _flying;
        public bool Placed => IsDeployed;
        public float PlacedAt => _placedAt;
        public float ThrownAt => _thrownAt;
        public int WavesEmitted => _wavesEmitted;
        public int SensedCount => _sensedCount;
        public WaveBreakerWave LastWave { get; private set; }
        public float ElapsedFrames => IsDeployed ? (Time.time - _placedAt) * 60f : 0f;

        public static int LandCount { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetCounts()
        {
            LandCount = 0;
        }

        protected override void Awake()
        {
            base.Awake();
            var neutral = new System.Collections.Generic.List<Renderer>();
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                if (!r.name.StartsWith("Ink", System.StringComparison.Ordinal))
                {
                    neutral.Add(r);
                }
            }
            _neutralRenderers = neutral.ToArray();
        }

        // SubProjectile 介面(Wave Breaker 由大招丟出,改用 Throw)
        public override void Launch(Vector3 position, Vector3 velocity, SubWeaponConfig config, Inkling owner)
        {
            Debug.LogError("WaveBreakerDevice:請用 Throw(由 WaveBreakerSpecial 丟出)", this);
            Expire();
        }

        public void Throw(Vector3 origin, Vector3 velocity, WaveBreakerConfig config, Inkling owner,
            PlayerSpecialGauge gauge, int lockToken)
        {
            _config = config;
            _gauge = gauge;
            _lockToken = lockToken;
            Owner = owner;
            _flightTeam = owner != null ? owner.Team : Team.Alpha;
            _velocity = velocity;
            _thrownAt = Time.time;
            _flying = true;
            _wavesEmitted = 0;
            LastWave = null;
            _sensedCount = 0;
            _contactCount = 0;
            transform.SetPositionAndRotation(origin, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            TeamTint.ApplyAll(this, TeamPalette.ColorOf(_flightTeam));
            RetintNeutral();
            SetBall(0f);
        }

        private void Update()
        {
            if (_config == null)
            {
                return;
            }
            if (_flying)
            {
                Fly(Time.deltaTime);
                return;
            }
            if (!IsDeployed)
            {
                return;
            }
            float frames = ElapsedFrames;
            Sense(frames);
            EmitWaves(frames);
            Contact();
            AnimateBall(frames);
            // 放完第三波後收起(S3:三波之後裝置破掉)
            int[] emit = WaveBreakerModel.WaveEmitFrames;
            if (_wavesEmitted >= emit.Length && frames >= emit[emit.Length - 1] + 20f)
            {
                Finish();
            }
        }

        // ── 飛行 ──

        private void Fly(float dt)
        {
            Vector3 previous = transform.position;
            Vector3 next = previous;
            Vector3 velocity = _velocity;
            SimpleBallistics.Step(ref next, ref velocity, _config.Gravity, dt);
            Vector3 delta = next - previous;
            float distance = delta.magnitude;
            if (distance > 1e-5f && Physics.SphereCast(previous, FlightRadius, delta / distance, out RaycastHit hit,
                    distance, CombatMasks.Terrain, QueryTriggerInteraction.Ignore))
            {
                Vector3 contact = previous + delta / distance * hit.distance;
                if (hit.normal.y >= 0.6f)
                {
                    Land(hit.point, hit.normal);
                    return;
                }
                // 牆/天花板:抵消垂直表面的速度,沿牆滑落(外推一點避免卡進牆裡)
                transform.position = contact + hit.normal * 0.02f;
                _velocity = velocity - hit.normal * Mathf.Min(0f, Vector3.Dot(velocity, hit.normal));
                if (hit.normal.y < -0.5f)
                {
                    _velocity.y = Mathf.Min(0f, _velocity.y);
                }
                return;
            }
            transform.position = next;
            _velocity = velocity;
            if (Time.time - _thrownAt > MaxFlightSeconds || next.y < -30f)
            {
                // 掉出場外:裝置沒了,能量歸 0
                _flying = false;
                if (_gauge != null)
                {
                    _gauge.EndLock(_lockToken);
                }
                Despawn();
            }
        }

        private void Land(Vector3 point, Vector3 normal)
        {
            _flying = false;
            transform.position = point;
            _placedAt = Time.time;
            Deploy(Owner, _flightTeam);
            RetintNeutral();
            LandCount++;
            GameAudio.Play(_config.LandSound, point + Vector3.up, 1f, 0f);
            if (InkSplashFxPool.Instance != null)
            {
                InkSplashFxPool.Instance.Spawn(point + normal * 0.05f, normal, TeamPalette.ColorOf(Team));
            }
            // 設置當下的範圍預告光(JP wiki:展開時に範囲予告の光とセンサーが出る)
            if (_config.WavePrefab != null)
            {
                WaveBreakerWave pulse = SharedPool.Spawn(_config.WavePrefab);
                pulse.transform.position = point;
                pulse.EmitSensor(point, _config, Team);
            }
            // 設置在敵方 Big Bubbler 的球面上 → 立刻消失(JP wiki グレートバリア 対ホップソナー)
            for (int i = 0; i < BarrierSphere.Active.Count; i++)
            {
                BarrierSphere barrier = BarrierSphere.Active[i];
                if (!barrier.BlocksTeam(Team))
                {
                    continue;
                }
                float shell = Mathf.Abs(Vector3.Distance(point + Vector3.up * 1f, barrier.Center) - barrier.Radius);
                if (shell < 0.9f)
                {
                    DestroyByBarrier();
                    return;
                }
            }
        }

        private void DestroyByBarrier()
        {
            if (_gauge != null)
            {
                _gauge.EndLock(_lockToken);
            }
            if (InkSplashFxPool.Instance != null)
            {
                InkSplashFxPool.Instance.Spawn(transform.position + Vector3.up, Vector3.up, TeamPalette.ColorOf(Team));
            }
            Expire();
        }

        // ── 設置感應:57F 內圓柱範圍擴張,碰到的敵人標記到第 102F ──

        private void Sense(float frames)
        {
            float radius = WaveBreakerModel.SensorRadius(frames, _config.WaveMaxRadius);
            if (radius <= 0f)
            {
                return;
            }
            float until = _placedAt + _config.SensorMarkEndFrame / 60f;
            float remaining = until - Time.time;
            if (remaining <= 0f)
            {
                return;
            }
            Vector3 center = transform.position;
            var all = Inkling.All;
            for (int i = 0; i < all.Count; i++)
            {
                Inkling inkling = all[i];
                if (inkling == null || inkling.Team == Team || Contains(_sensed, _sensedCount, inkling))
                {
                    continue;
                }
                Vector3 feet = inkling.transform.position;
                // 圓柱:水平距離 ≤ 半徑,且身體(腳 ~ 頭 1.6 m)與垂直範圍 +2.8 / −5.2 有重疊(頭碰到就算)
                float dy = feet.y - center.y;
                if (dy > WaveBreakerModel.DamageYMax || dy + 1.6f < WaveBreakerModel.DamageYMin)
                {
                    continue;
                }
                float dx = feet.x - center.x;
                float dz = feet.z - center.z;
                if (dx * dx + dz * dz > (radius + BodyRadius) * (radius + BodyRadius))
                {
                    continue;
                }
                var life = inkling.GetComponent<InklingLifecycle>();
                if (life != null && life.IsDead)
                {
                    continue;
                }
                if (_sensedCount < MaxTracked)
                {
                    _sensed[_sensedCount++] = inkling;
                }
                Marking.Apply(inkling, Team, remaining);
            }
        }

        // ── 衝擊波 ──

        private void EmitWaves(float frames)
        {
            int[] emit = WaveBreakerModel.WaveEmitFrames;
            while (_wavesEmitted < emit.Length && frames >= emit[_wavesEmitted])
            {
                _wavesEmitted++;
                if (_config.WavePrefab == null)
                {
                    continue;
                }
                WaveBreakerWave wave = SharedPool.Spawn(_config.WavePrefab);
                wave.transform.position = transform.position;
                wave.Emit(transform.position, _config, Owner, Team);
                LastWave = wave;
                GameAudio.Play(_config.WaveSound, transform.position + Vector3.up, 1f, 0.02f);
                if (InkSplashFxPool.Instance != null)
                {
                    InkSplashFxPool.Instance.Spawn(transform.position + Vector3.up * 0.2f, Vector3.up,
                        TeamPalette.ColorOf(Team));
                }
            }
        }

        // ── 接觸傷害:敵人碰到裝置 30 + 擊退,同一人 0.5 秒一次 ──

        private void Contact()
        {
            Vector3 basePoint = transform.position;
            float reach = _config.ContactRadius + BodyRadius;
            var all = Inkling.All;
            for (int i = 0; i < all.Count; i++)
            {
                Inkling inkling = all[i];
                if (inkling == null || inkling.Team == Team)
                {
                    continue;
                }
                Vector3 feet = inkling.transform.position;
                if (feet.y > basePoint.y + _config.ContactHeight || feet.y + 1.6f < basePoint.y)
                {
                    continue;
                }
                float dx = feet.x - basePoint.x;
                float dz = feet.z - basePoint.z;
                if (dx * dx + dz * dz > reach * reach)
                {
                    continue;
                }
                int slot = ContactSlot(inkling);
                if (slot < 0 || Time.time < _contactReadyAt[slot])
                {
                    continue;
                }
                var health = inkling.GetComponent<InklingHealth>();
                if (health == null || !health.IsAlive)
                {
                    continue;
                }
                _contactReadyAt[slot] = Time.time + _config.ContactInterval;
                Vector3 closest = health.ClosestPoint(basePoint + Vector3.up * 1f);
                health.ApplyDamage(new DamageInfo(_config.ContactDamage, closest, Team, Owner, DamageKind.Special,
                    AttackClass.WaveBreaker));
                var locomotion = inkling.GetComponent<PlayerLocomotion>();
                if (locomotion != null && locomotion.isActiveAndEnabled && health.IsAlive)
                {
                    locomotion.AddImpulse(KnockbackModel.Impulse(basePoint + Vector3.up * 1f, closest,
                        _config.ContactKnockback, reach + 1f));
                }
            }
        }

        private int ContactSlot(Inkling inkling)
        {
            for (int i = 0; i < _contactCount; i++)
            {
                if (_contactTargets[i] == inkling)
                {
                    return i;
                }
            }
            if (_contactCount >= MaxTracked)
            {
                return -1;
            }
            _contactTargets[_contactCount] = inkling;
            _contactReadyAt[_contactCount] = 0f;
            return _contactCount++;
        }

        private static bool Contains(Inkling[] list, int count, Inkling inkling)
        {
            for (int i = 0; i < count; i++)
            {
                if (list[i] == inkling)
                {
                    return true;
                }
            }
            return false;
        }

        // 彈跳球:兩波之間慢慢升起、放波的瞬間落下(SphereAnimCurve)
        private void AnimateBall(float frames)
        {
            int[] emit = WaveBreakerModel.WaveEmitFrames;
            float from = 0f;
            float to = emit[0];
            for (int i = 0; i < emit.Length; i++)
            {
                if (frames < emit[i])
                {
                    to = emit[i];
                    break;
                }
                from = emit[i];
                to = i + 1 < emit.Length ? emit[i + 1] : emit[i] + 30f;
            }
            float t = to > from ? Mathf.Clamp01((frames - from) / (to - from)) : 0f;
            SetBall(HermiteCurve.WaveBreakerBall.Evaluate(t));
        }

        private void SetBall(float lift)
        {
            if (_ball != null)
            {
                _ball.localPosition = new Vector3(0f, Mathf.Lerp(_ballHeight.x, _ballHeight.y, lift), 0f);
            }
        }

        // ── 結束 ──

        protected override void OnBroken(in DamageInfo info)
        {
            // 被打壞:能量鎖定立刻歸 0(已放出的波照樣擴張)
            if (_gauge != null)
            {
                _gauge.EndLock(_lockToken);
            }
        }

        private void Finish()
        {
            if (InkSplashFxPool.Instance != null)
            {
                InkSplashFxPool.Instance.Spawn(transform.position + Vector3.up, Vector3.up, TeamPalette.ColorOf(Team));
            }
            Expire();
        }

        public override void Expire()
        {
            _flying = false;
            for (int i = 0; i < MaxTracked; i++)
            {
                _sensed[i] = null;
                _contactTargets[i] = null;
            }
            _sensedCount = 0;
            _contactCount = 0;
            base.Expire();
        }

        // 基底把整個物件上隊伍色:非 Ink* 的部位改回材質原色
        private void RetintNeutral()
        {
            for (int i = 0; i < _neutralRenderers.Length; i++)
            {
                if (_neutralRenderers[i] != null)
                {
                    _neutralRenderers[i].SetPropertyBlock(null);
                }
            }
        }

        // 基底只在受擊閃白切換時重上整個物件的顏色 → 跟著把原色部位改回來
        protected override void LateUpdate()
        {
            base.LateUpdate();
            if (IsDeployed && IsFlashing != _flashState)
            {
                _flashState = IsFlashing;
                RetintNeutral();
            }
        }
    }
}

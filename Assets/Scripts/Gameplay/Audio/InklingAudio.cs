using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Audio
{
    // 角色身上的聲音(只觀察狀態,不改任何玩法):腳步、敵墨黏腳、起跳/落地、潛入/浮出、游動與爬牆循環、
    // 受傷/被擊倒/重生、命中與擊倒確認、大招就緒/啟動、墨量不足。
    // 本機玩家走 2D(自己的聲音不該忽左忽右);其他角色走 3D 並略小聲——S3 裡聽敵人游動聲是實際戰術。
    [DisallowMultipleComponent]
    public sealed class InklingAudio : MonoBehaviour
    {
        [SerializeField, Tooltip("人形跑步一步的距離(公尺)")]
        private float _strideLength = 1.35f;

        [SerializeField, Tooltip("踩在敵墨上的黏腳聲間隔(秒)")]
        private float _enemyInkStepInterval = 0.42f;

        [SerializeField, Tooltip("其他角色的聲音音量倍率")]
        private float _othersVolume = 0.85f;

        [SerializeField, Tooltip("命中標記音的最短間隔(秒)")]
        private float _hitMarkerInterval = 0.07f;

        private Inkling _inkling;
        private CharacterController _controller;
        private PlayerLocomotion _locomotion;
        private SquidController _squid;
        private InklingHealth _health;
        private InklingLifecycle _lifecycle;
        private PlayerSpecialGauge _special;
        private PlayerInkTank _tank;

        private AudioLoop _swimLoop;
        private AudioLoop _climbLoop;
        private float _stepDistance;
        private float _nextEnemyStep;
        private float _nextHitMarker;
        private bool _wasGrounded = true;
        private float _lastVerticalSpeed;
        private bool _wasDiving;
        private bool _wasFull;
        private bool _wasActive;

        private bool Local => _inkling != null && _inkling.IsLocalPlayer;

        private void Awake()
        {
            _inkling = GetComponent<Inkling>();
            _controller = GetComponent<CharacterController>();
            _locomotion = GetComponent<PlayerLocomotion>();
            _squid = GetComponent<SquidController>();
            _health = GetComponent<InklingHealth>();
            _lifecycle = GetComponent<InklingLifecycle>();
            _special = GetComponent<PlayerSpecialGauge>();
            _tank = GetComponent<PlayerInkTank>();
        }

        private void OnEnable()
        {
            if (_inkling != null)
            {
                _inkling.DamageDealt += OnDamageDealt;
            }
            if (_health != null)
            {
                _health.Damaged += OnDamaged;
            }
            if (_lifecycle != null)
            {
                _lifecycle.Died += OnDied;
                _lifecycle.Respawned += OnRespawned;
            }
            if (_tank != null)
            {
                _tank.Shortage += OnInkShortage;
            }
        }

        private void OnDisable()
        {
            if (_inkling != null)
            {
                _inkling.DamageDealt -= OnDamageDealt;
            }
            if (_health != null)
            {
                _health.Damaged -= OnDamaged;
            }
            if (_lifecycle != null)
            {
                _lifecycle.Died -= OnDied;
                _lifecycle.Respawned -= OnRespawned;
            }
            if (_tank != null)
            {
                _tank.Shortage -= OnInkShortage;
            }
            StopLoops();
        }

        private void Emit(string id, float volume = 1f, float pitchJitter = 0.04f)
        {
            if (Local)
            {
                GameAudio.Play2D(id, volume, pitchJitter);
            }
            else
            {
                GameAudio.Play(id, transform.position + Vector3.up * 0.5f, volume * _othersVolume, pitchJitter);
            }
        }

        private void Update()
        {
            bool dead = _lifecycle != null && _lifecycle.IsDead;
            if (_controller == null || !_controller.enabled || dead)
            {
                StopLoops();
                _wasGrounded = true;
                return;
            }
            Vector3 velocity = _controller.velocity;
            float planar = new Vector2(velocity.x, velocity.z).magnitude;
            bool grounded = _controller.isGrounded;
            bool squid = _squid != null && _squid.IsSquid;
            bool climbing = _locomotion != null && _locomotion.IsInsideInkedWall;
            bool diving = squid && _squid.IsInOwnInk;

            // 起跳 / 落地
            if (_wasGrounded && !grounded && velocity.y > 2f)
            {
                Emit("jump", squid ? 0.8f : 0.7f);
            }
            if (grounded && !_wasGrounded && _lastVerticalSpeed < -4f)
            {
                Emit("land", Mathf.InverseLerp(4f, 14f, -_lastVerticalSpeed) * 0.7f + 0.3f);
            }
            _wasGrounded = grounded;
            _lastVerticalSpeed = velocity.y;

            // 潛入 / 浮出(與 SquidController 的過場同一判準)
            if (diving != _wasDiving)
            {
                Emit(diving ? "squid_dive" : "squid_surface", diving ? 0.9f : 0.7f);
                _wasDiving = diving;
            }

            // 游動 / 爬牆循環
            bool swimming = diving && !climbing && planar > 1.5f;
            UpdateLoop(ref _swimLoop, "swim_loop", swimming, Mathf.Lerp(0.45f, 1f, Mathf.InverseLerp(1.5f, 11f, planar)));
            UpdateLoop(ref _climbLoop, "squid_climb_loop", climbing && velocity.sqrMagnitude > 0.25f, 1f);

            // 人形腳步
            if (!squid && grounded && planar > 0.8f)
            {
                if (_squid != null && _squid.OnEnemyInk)
                {
                    if (Time.time >= _nextEnemyStep)
                    {
                        Emit("step_enemy_ink", 0.9f);
                        _nextEnemyStep = Time.time + _enemyInkStepInterval;
                    }
                }
                else
                {
                    _stepDistance += planar * Time.deltaTime;
                    if (_stepDistance >= _strideLength)
                    {
                        _stepDistance = 0f;
                        Emit("footstep", 0.6f, 0.08f);
                    }
                }
            }
            else
            {
                // 停下後再起步,第一步很快就響
                _stepDistance = _strideLength * 0.6f;
            }

            // 大招
            if (_special != null)
            {
                bool active = _special.IsActive;
                bool full = _special.IsFull && !active;
                if (full && !_wasFull && Local)
                {
                    GameAudio.Play2D("special_ready");
                }
                if (active && !_wasActive)
                {
                    Emit("special_activate");
                }
                _wasFull = full;
                _wasActive = active;
            }
        }

        private void UpdateLoop(ref AudioLoop loop, string id, bool wanted, float volume)
        {
            if (wanted)
            {
                if (loop == null || !loop.IsAlive)
                {
                    loop = GameAudio.StartLoop(id, transform, !Local, volume * (Local ? 1f : _othersVolume));
                }
                else
                {
                    loop.SetVolume(volume * (Local ? 1f : _othersVolume));
                }
            }
            else if (loop != null)
            {
                loop.Stop(0.18f);
                loop = null;
            }
        }

        private void StopLoops()
        {
            if (_swimLoop != null)
            {
                _swimLoop.Stop(0.1f);
                _swimLoop = null;
            }
            if (_climbLoop != null)
            {
                _climbLoop.Stop(0.1f);
                _climbLoop = null;
            }
        }

        private void OnDamageDealt(IDamageable target, float amount, bool splatted)
        {
            if (!Local)
            {
                return;
            }
            if (splatted)
            {
                GameAudio.Play2D("splat_confirm");
                return;
            }
            if (amount > 0f && Time.time >= _nextHitMarker)
            {
                _nextHitMarker = Time.time + _hitMarkerInterval;
                GameAudio.Play2D("hit_marker", 0.8f, 0.03f);
            }
        }

        private void OnDamaged(float dealt, DamageInfo info)
        {
            // 敵墨持續傷害每幀都會來,不出受傷聲(已有黏腳聲)
            if (Local && info.Kind != DamageKind.EnemyInk && dealt >= 1f)
            {
                GameAudio.Play2D("player_hurt", Mathf.Lerp(0.6f, 1f, Mathf.InverseLerp(10f, 60f, dealt)));
            }
        }

        private void OnDied(DamageInfo info)
        {
            StopLoops();
            Emit("player_splatted");
        }

        private void OnRespawned()
        {
            _wasDiving = false;
            _wasFull = false;
            _wasActive = false;
            Emit("respawn_launch", 0.8f, 0f);
        }

        private void OnInkShortage()
        {
            if (Local)
            {
                GameAudio.Play2D("low_ink");
            }
        }
    }
}

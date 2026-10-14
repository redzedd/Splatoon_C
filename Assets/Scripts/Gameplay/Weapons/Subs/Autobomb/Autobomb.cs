using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // Autobomb 的狀態
    public enum AutobombState : byte
    {
        Flying = 0,
        // 落地後搜尋(搜尋圈展開期間)
        Searching = 1,
        // 找到目標,轉向 10F
        Turning = 2,
        // 追蹤(≤ 150F)
        Chasing = 3,
        // 引爆倒數(放慢、膨脹、警告音)
        Warning = 4,
    }

    // 引爆倒數的原因
    public enum AutobombWarnReason : byte
    {
        None = 0,
        // 追到目標(進入近距半徑)或走到死亡目標的最後位置
        Approach = 1,
        // 追滿 150F
        ChaseTimeout = 2,
        // 落地沒找到目標
        NoTarget = 3,
    }

    // 池化機器人炸彈(S3 Bomb_Robot):拋物線飛行(與預覽同一個 SimpleBallistics 步進,60 Hz;撞牆反彈 0.7),
    // 落地 → 搜尋(水平 12.5、上下 ±5 內最近的敵方角色/假人,含潛伏者;不需視線)→ 轉向 10F → 追蹤:每幀 +0.003、
    // 最高 0.09 u/F、每幀最多轉 2°,GroundWalker 貼地走(不會爬牆:撞牆原地踏步;能走下平台)→ 進入近距半徑 / 追滿 150F /
    // 走到死亡目標的最後位置 → 放慢膨脹 60F 後爆炸;沒找到目標 → 落地 60F 爆炸。沒有碰撞體(打不掉)。
    // 炸彈共通規則(§4.3):飛行中(BombRules.CastPath)或走路時(BombRules.TouchDetonator)碰到敵方 Splash Wall 等立刻爆炸。
    public sealed class Autobomb : SimulatedBomb
    {
        private const float RingFadeSeconds = 0.25f;

        [SerializeField, Tooltip("左腳(走路擺動)")]
        private Transform _footLeft;

        [SerializeField, Tooltip("右腳(走路擺動)")]
        private Transform _footRight;

        [SerializeField, Tooltip("落地搜尋圈(外半徑 1 的平面環,執行期縮放到搜尋半徑)")]
        private Renderer _searchRing;

        private AutobombConfig _config;
        private AutobombParams _p;
        private AutobombState _state;
        private AutobombWarnReason _warnReason;
        private Vector3 _velocity;
        private bool _grounded;
        private Vector3 _groundNormal = Vector3.up;
        private Vector3 _groundPoint;
        private float _yaw;
        private float _turnFromYaw;
        private float _speed;
        private int _landFrame;
        private int _stateFrame;
        private int _chaseFrames;
        private int _warningFrames;
        private int _footstepCounter;
        private int _blockedFrames;
        private float _walkPhase;
        private float _ringStart = float.NegativeInfinity;
        private IDamageable _target;
        private Transform _targetTransform;
        private Vector3 _goal;
        private float _maxSpeed;

        // 測試/除錯:累計數與最近一次的時間點/位置/目標
        public static int ExplosionCount { get; private set; }
        public static int FootstepCount { get; private set; }
        // 走路時碰到引爆物(敵方 Splash Wall 等)提前爆炸的次數
        public static int DetonatorHitCount { get; private set; }
        public static float LastLandTime { get; private set; }
        public static float LastFindTime { get; private set; }
        public static float LastWarningTime { get; private set; }
        public static float LastExplosionTime { get; private set; }
        public static Vector3 LastLandPoint { get; private set; }
        public static Vector3 LastExplosionCenter { get; private set; }
        public static IDamageable LastTarget { get; private set; }
        public static AutobombWarnReason LastWarnReason { get; private set; }
        public static int LastChaseFrames { get; private set; }
        public static int LastBlockedFrames { get; private set; }
        public static int LastLandToExplodeFrames { get; private set; }
        // 最近一顆的最高追蹤速度(m/s)
        public static float LastMaxSpeed { get; private set; }

        public AutobombState State => _state;
        public IDamageable Target => _target;
        // 目前走速(m/s)
        public float Speed => _speed * SimFrames.Rate;
        public bool IsGrounded => _grounded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetCounters()
        {
            ExplosionCount = 0;
            FootstepCount = 0;
            DetonatorHitCount = 0;
            LastTarget = null;
            LastLandTime = float.NegativeInfinity;
            LastFindTime = float.NegativeInfinity;
            LastWarningTime = float.NegativeInfinity;
            LastExplosionTime = float.NegativeInfinity;
        }

        public override void Launch(Vector3 position, Vector3 velocity, SubWeaponConfig config, Inkling owner)
        {
            _config = config as AutobombConfig;
            if (_config == null)
            {
                Debug.LogError($"Autobomb:設定型別不符({config})", this);
                Despawn();
                return;
            }
            Begin(position, owner);
            _p = _config.S3;
            _state = AutobombState.Flying;
            _warnReason = AutobombWarnReason.None;
            _velocity = velocity;
            _grounded = false;
            _groundNormal = Vector3.up;
            _groundPoint = position;
            _yaw = YawOf(velocity, 0f);
            _speed = 0f;
            _chaseFrames = 0;
            _footstepCounter = 0;
            _blockedFrames = 0;
            _walkPhase = 0f;
            _maxSpeed = 0f;
            _target = null;
            _targetTransform = null;
            _ringStart = float.NegativeInfinity;
            if (_searchRing != null)
            {
                _searchRing.enabled = false;
            }
            SetLightColor(new Color(1f, 0.85f, 0.2f));
            ApplyPose();
        }

        protected override bool Step()
        {
            if (_state == AutobombState.Flying || !_grounded)
            {
                if (!Fly())
                {
                    return false;
                }
                if (_state == AutobombState.Flying)
                {
                    return true;
                }
            }
            switch (_state)
            {
                case AutobombState.Searching:
                    StepSearching();
                    break;
                case AutobombState.Turning:
                    StepTurning();
                    break;
                case AutobombState.Chasing:
                    StepChasing();
                    break;
                case AutobombState.Warning:
                    if (StepWarning())
                    {
                        return false;
                    }
                    break;
            }
            // 貼地走路只查地形(GroundWalker),屏障在目標圖層:這一步掃過的範圍碰到敵方屏障就在屏障前爆炸
            if (_grounded && BombRules.TouchDetonator(PrevPosition, SimPosition, _config.Radius, BombTeam,
                    out Vector3 touch))
            {
                SimPosition = touch;
                DetonatorHitCount++;
                Explode();
                return false;
            }
            return true;
        }

        // ── 飛行(投出、走下平台後的掉落)──

        private bool Fly()
        {
            if (_state == AutobombState.Flying && Frame > SimFrames.ToFrames(_config.Lifetime))
            {
                Release();
                return false;
            }
            Vector3 from = SimPosition;
            Vector3 to = from;
            Vector3 velocity = _velocity;
            SimpleBallistics.Step(ref to, ref velocity, _config.Gravity, SimFrames.Dt);
            BombContact contact = BombRules.CastPath(from, to, _config.Radius, BombTeam, false, out RaycastHit hit,
                out _);
            if (contact == BombContact.None)
            {
                SimPosition = to;
                _velocity = velocity;
                return true;
            }
            Vector3 delta = to - from;
            float length = delta.magnitude;
            Vector3 direction = length > 1e-6f ? delta / length : Vector3.down;
            Vector3 center = from + direction * hit.distance;
            if (contact == BombContact.Detonator)
            {
                SimPosition = center;
                Explode();
                return false;
            }
            Vector3 normal = hit.normal;
            if (normal.y >= GroundWalker.FloorNormalY)
            {
                SimPosition = hit.point + normal * (_config.Radius + GroundWalker.Skin);
                _groundNormal = normal;
                _groundPoint = hit.point;
                _grounded = true;
                _velocity = Vector3.zero;
                if (_state == AutobombState.Flying)
                {
                    OnLanded();
                }
                return true;
            }
            SimPosition = center + normal * GroundWalker.Skin;
            if (normal.y <= -GroundWalker.FloorNormalY)
            {
                velocity.y = -Mathf.Abs(velocity.y) * 0.3f;
                _velocity = velocity;
                return true;
            }
            // 撞牆:反彈(S2 HitVerticalWallReboundMaxRate 0.7)
            _velocity = SimpleBallistics.Reflect(velocity, normal, _config.WallRestitution, 1f);
            if (Vector3.Dot(velocity, normal) < -2f)
            {
                GameAudio.Play("bomb_bounce", hit.point, 0.6f);
            }
            return true;
        }

        private void OnLanded()
        {
            _state = AutobombState.Searching;
            _landFrame = Frame;
            _stateFrame = Frame;
            _ringStart = Time.time;
            LastLandTime = Time.time;
            LastLandPoint = _groundPoint;
            GameAudio.Play("bomb_bounce", _groundPoint, 0.6f);
        }

        // ── 搜尋 → 轉向 → 追蹤 → 倒數 ──

        private void StepSearching()
        {
            if (FindTarget())
            {
                _state = AutobombState.Turning;
                _stateFrame = Frame;
                _turnFromYaw = _yaw;
                LastFindTime = Time.time;
                LastTarget = _target;
                GameAudio.Play(_config.FindSound, SimPosition, 0.9f, 0f);
                return;
            }
            if (Frame - _landFrame >= _p.SearchFrames)
            {
                // S3 UnfindBurstWaitFrame 60:從落地起算
                BeginWarning(AutobombWarnReason.NoTarget,
                    Mathf.Max(1, _p.UnfindBurstWaitFrames - (Frame - _landFrame)));
            }
        }

        private void StepTurning()
        {
            UpdateGoal();
            float desired = YawOf(_goal - SimPosition, _yaw);
            float t = Mathf.Clamp01((float)(Frame - _stateFrame) / Mathf.Max(1, _p.FindRotateFrames));
            _yaw = Mathf.LerpAngle(_turnFromYaw, desired, t);
            if (t >= 1f)
            {
                _state = AutobombState.Chasing;
                _stateFrame = Frame;
                _chaseFrames = 0;
                _speed = 0f;
            }
        }

        private void StepChasing()
        {
            bool alive = UpdateGoal();
            _yaw = AutobombModel.StepYaw(_p, _yaw, YawOf(_goal - SimPosition, _yaw));
            _speed = AutobombModel.StepChaseSpeed(_p, _speed);
            Walk();
            _chaseFrames++;
            if (alive)
            {
                // 進入近距(必殺)半徑 → 60F 後爆炸(S3 FindApproachBurstWaitFrame)
                float distance = Vector3.Distance(SimPosition, _target.ClosestPoint(SimPosition));
                if (distance <= _config.Blast.NearRadius)
                {
                    BeginWarning(AutobombWarnReason.Approach, _p.ApproachBurstWaitFrames);
                    return;
                }
            }
            else
            {
                Vector3 flat = _goal - SimPosition;
                flat.y = 0f;
                if (flat.magnitude <= _p.DeathTargetApproachDistance)
                {
                    BeginWarning(AutobombWarnReason.Approach, _p.ApproachBurstWaitFrames);
                    return;
                }
            }
            if (_chaseFrames >= _p.ChaseFrames)
            {
                BeginWarning(AutobombWarnReason.ChaseTimeout, _p.ChaseBurstWaitFrames);
            }
        }

        private void BeginWarning(AutobombWarnReason reason, int frames)
        {
            _state = AutobombState.Warning;
            _warnReason = reason;
            _stateFrame = Frame;
            _warningFrames = frames;
            LastWarningTime = Time.time;
            LastWarnReason = reason;
            GameAudio.Play(_config.WarningSound, SimPosition, 0.9f, 0f);
        }

        // 回傳 true = 爆炸了
        private bool StepWarning()
        {
            UpdateGoal();
            if (_target != null || _warnReason == AutobombWarnReason.Approach)
            {
                _yaw = AutobombModel.StepYaw(_p, _yaw, YawOf(_goal - SimPosition, _yaw));
            }
            _speed *= _p.WarningSpeedKeep;
            Walk();
            if (Frame - _stateFrame >= _warningFrames)
            {
                Explode();
                return true;
            }
            return false;
        }

        // 目標還活著就更新追蹤點;死了/消失就走向最後位置
        private bool UpdateGoal()
        {
            if (_target != null && _targetTransform != null && _target.IsAlive)
            {
                _goal = _targetTransform.position;
                return true;
            }
            return false;
        }

        private bool FindTarget()
        {
            var active = DamageableRegistry.Active;
            float bestSq = float.MaxValue;
            IDamageable best = null;
            Transform bestTransform = null;
            for (int i = 0; i < active.Count; i++)
            {
                IDamageable candidate = active[i];
                // 只追角色(含試射場假人),不追場上裝置
                Component component = candidate as InklingHealth;
                if (component == null)
                {
                    component = candidate as TargetDummy;
                }
                if (component == null || !candidate.IsAlive || !DamageRules.CanDamage(BombTeam, candidate))
                {
                    continue;
                }
                Vector3 offset = component.transform.position - _groundPoint;
                if (!AutobombModel.InSearchWindow(_p, offset))
                {
                    continue;
                }
                float sq = offset.x * offset.x + offset.z * offset.z;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = candidate;
                    bestTransform = component.transform;
                }
            }
            _target = best;
            _targetTransform = bestTransform;
            if (best != null)
            {
                _goal = bestTransform.position;
            }
            return best != null;
        }

        // 貼地走一幀:撞牆原地踏步(不會爬牆),走出邊緣就掉落
        private void Walk()
        {
            if (!_grounded)
            {
                return;
            }
            _maxSpeed = Mathf.Max(_maxSpeed, _speed * SimFrames.Rate);
            _walkPhase += Mathf.Max(_speed, 0.02f) * 9f;
            Vector3 direction = new Vector3(Mathf.Sin(_yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(_yaw * Mathf.Deg2Rad));
            GroundStep step = GroundWalker.Move(ref SimPosition, ref _groundNormal, ref _groundPoint, direction,
                _speed, _config.Radius, _config.StepHeight);
            if (step.Blocked)
            {
                _blockedFrames++;
            }
            if (!step.Grounded)
            {
                _grounded = false;
                _velocity = direction * (_speed * SimFrames.Rate);
            }
            if (_speed > 0.004f && ++_footstepCounter >= Mathf.Max(1, _config.FootstepFrames))
            {
                _footstepCounter = 0;
                FootstepCount++;
                GameAudio.Play(_config.FootstepSound, _groundPoint, 0.45f, 0.1f);
            }
        }

        private void Explode()
        {
            ExplosionCount++;
            LastExplosionTime = Time.time;
            LastExplosionCenter = SimPosition;
            LastChaseFrames = _chaseFrames;
            LastBlockedFrames = _blockedFrames;
            LastMaxSpeed = _maxSpeed;
            LastLandToExplodeFrames = _state == AutobombState.Flying ? 0 : Frame - _landFrame;
            Explosion.Detonate(_config.Blast, SimPosition, BombTeam, Owner);
            Release();
        }

        protected override void OnReleased()
        {
            _target = null;
            _targetTransform = null;
            _config = null;
            if (_searchRing != null)
            {
                _searchRing.enabled = false;
            }
        }

        // ── 外觀 ──

        protected override void UpdateVisual()
        {
            if (_state == AutobombState.Flying)
            {
                float flightYaw = YawOf(_velocity, _yaw);
                _yaw = Mathf.MoveTowardsAngle(_yaw, flightYaw, 720f * Time.deltaTime);
            }
            ApplyPose();
            // 走路:兩腳交替前後擺、身體上下彈
            float swing = _state == AutobombState.Flying ? 0f : Mathf.Sin(_walkPhase) * 35f;
            if (_footLeft != null)
            {
                _footLeft.localRotation = Quaternion.Euler(swing, 0f, 0f);
            }
            if (_footRight != null)
            {
                _footRight.localRotation = Quaternion.Euler(-swing, 0f, 0f);
            }
            bool warning = _state == AutobombState.Warning;
            float remaining = warning ? Mathf.Max(0, _warningFrames - (Frame - _stateFrame)) : 999f;
            SetLights(!warning ? Blink(Time.time, 0.6f) : Blink(Time.time, remaining < 20f ? 0.08f : 0.16f));
            if (warning)
            {
                float t = 1f - Mathf.Clamp01(remaining / Mathf.Max(1f, _warningFrames));
                SetSwell(1f + 0.25f * t + 0.04f * Mathf.Sin(Time.time * 50f));
            }
            else
            {
                SetSwell(1f + Mathf.Abs(Mathf.Sin(_walkPhase)) * 0.05f * Mathf.Clamp01(_speed / 0.05f));
            }
            UpdateRing();
        }

        private void ApplyPose()
        {
            Vector3 up = _grounded ? _groundNormal : Vector3.up;
            // 走得快時往前傾
            float lean = _state == AutobombState.Chasing ? Mathf.Clamp01(_speed / Mathf.Max(0.01f, _p.ChaseMaxSpeed)) * 10f : 0f;
            transform.rotation = Quaternion.FromToRotation(Vector3.up, up) * Quaternion.Euler(lean, _yaw, 0f);
        }

        // 落地搜尋圈:0 → 12.5 m 展開後淡出(隊伍色半透明)
        private void UpdateRing()
        {
            if (_searchRing == null)
            {
                return;
            }
            float elapsed = Time.time - _ringStart;
            float grow = Mathf.Max(0.05f, _config.SearchRingSeconds);
            bool visible = elapsed >= 0f && elapsed < grow + RingFadeSeconds;
            if (_searchRing.enabled != visible)
            {
                _searchRing.enabled = visible;
            }
            if (!visible)
            {
                return;
            }
            float radius = _p.SearchRadius * Mathf.Clamp01(elapsed / grow);
            Transform ring = _searchRing.transform;
            ring.position = _groundPoint + _groundNormal * 0.04f;
            ring.rotation = Quaternion.FromToRotation(Vector3.up, _groundNormal);
            ring.localScale = new Vector3(radius, 1f, radius);
            float alpha = elapsed < grow ? 0.55f : 0.55f * (1f - (elapsed - grow) / RingFadeSeconds);
            TeamTint.Apply(_searchRing, TeamPalette.ColorOf(BombTeam), Mathf.Clamp01(alpha));
        }

        private static float YawOf(Vector3 direction, float fallback)
        {
            if (direction.x * direction.x + direction.z * direction.z < 1e-8f)
            {
                return fallback;
            }
            return Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        }
    }
}

using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // Killer Wail 5.1 的一支喇叭(SharedPool;KillerWailSpecial 生成與指派):
    //   跟隨:在施放者身後六角形隊形上的位置,面向準心 → 鎖定:20F 預告線(大家都看得到的預兆)→
    //   照射 180F:停在原地、光束追著目標約 0.5 秒前的位置(InterpRate 0.4、最大 30°/F),每 5F 對光束上的敵人 3.5 傷。
    //   沒鎖到人:沿鎖定當下的準心方向照,之後隨機往左或右掃(0.3°/F);目標被擊倒 → 照原本的轉向繼續掃。
    //   光束無限長、穿牆、穿 Big Bubbler(屏障本身也吃傷害)。施放者在照射前被擊倒 → 取消(Cancel)。
    public sealed class KillerWailSpeaker : MonoBehaviour
    {
        private const int HistorySize = 64;

        public enum State : byte
        {
            Idle,
            Following,
            Warning,
            Firing,
        }

        [SerializeField, Tooltip("光束(LineRenderer,世界座標)")]
        private LineRenderer _beam;

        [SerializeField, Tooltip("預告線(LineRenderer,世界座標)")]
        private LineRenderer _warning;

        [SerializeField, Tooltip("喇叭口(光束起點);留空用自己")]
        private Transform _mouth;

        private KillerWailConfig _config;
        private Inkling _owner;
        private Team _team;
        private int _slot;
        private Inkling _target;
        private InklingLifecycle _targetLife;
        private Vector3 _direction = Vector3.forward;
        private float _sweepSign = 1f;
        private float _lastYawRate;
        private float _fireAt;
        private float _endAt;
        private int _ticksDone;
        private bool _placed;
        private AudioLoop _loop;
        private Renderer[] _inkRenderers;
        private Color _tinted = Color.clear;
        // 目標位置歷史(光束追「半秒前」的位置)
        private readonly Vector3[] _history = new Vector3[HistorySize];
        private readonly float[] _historyTime = new float[HistorySize];
        private int _historyCount;
        private int _historyNext;

        public State Phase { get; private set; }

        // 每次 Begin +1:池化重用後,舊的施放者拿著的參照就對不上(不會誤取消別人的喇叭)
        public int Generation { get; private set; }
        public Inkling Target => _target;
        public Vector3 Direction => _direction;
        public Vector3 Origin => _mouth != null ? _mouth.position : transform.position;
        public float FireAt => _fireAt;
        public float EndAt => _endAt;
        public int Slot => _slot;
        public float DamageDealt { get; private set; }
        // 已結算的傷害次數(每 5F 一次,不論打到幾個目標)與打中目標的累計次數
        public int Ticks { get; private set; }
        public int Hits { get; private set; }

        // 累計(測試用)
        public static int ActiveBeams { get; private set; }
        public static int TotalHits { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetCounts()
        {
            ActiveBeams = 0;
            TotalHits = 0;
        }

        private void Awake()
        {
            var ink = new System.Collections.Generic.List<Renderer>();
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                if (r.name.StartsWith("Ink", System.StringComparison.Ordinal))
                {
                    ink.Add(r);
                }
            }
            _inkRenderers = ink.ToArray();
        }

        public void Begin(Inkling owner, KillerWailConfig config, int slot)
        {
            Generation++;
            _owner = owner;
            _config = config;
            _slot = slot;
            _team = owner != null ? owner.Team : Team.Alpha;
            _target = null;
            _targetLife = null;
            _historyCount = 0;
            _historyNext = 0;
            _placed = false;
            DamageDealt = 0f;
            Ticks = 0;
            Hits = 0;
            Phase = State.Following;
            TeamTint.ApplyCached(_inkRenderers, ref _tinted, TeamPalette.ColorOf(_team));
            if (_beam != null)
            {
                TeamTint.Apply(_beam, Color.Lerp(TeamPalette.ColorOf(_team), Color.white, 0.25f), 0.9f);
                _beam.enabled = false;
            }
            if (_warning != null)
            {
                TeamTint.Apply(_warning, TeamPalette.ColorOf(_team), 0.55f);
                _warning.enabled = false;
            }
            Vector3 aim = owner != null && owner.AimView != null ? owner.AimView.AimForward
                : (owner != null ? owner.transform.forward : Vector3.forward);
            _direction = aim.sqrMagnitude > 1e-6f ? aim.normalized : Vector3.forward;
            transform.position = FormationPosition();
            transform.rotation = Quaternion.LookRotation(_direction);
        }

        // 鎖定:target 為 null = 沒鎖到人(沿 aimForward 照、之後隨機掃)
        public void Lock(Inkling target, Vector3 aimForward, float fireAt, float endAt)
        {
            if (Phase != State.Following)
            {
                return;
            }
            _target = target;
            _targetLife = target != null ? target.GetComponent<InklingLifecycle>() : null;
            _fireAt = fireAt;
            _endAt = endAt;
            _sweepSign = Random.value < 0.5f ? -1f : 1f;
            _historyCount = 0;
            _historyNext = 0;
            if (target != null)
            {
                RecordTarget(Time.time);
                Vector3 toward = TargetPoint(target.transform.position) - Origin;
                _direction = toward.sqrMagnitude > 1e-6f ? toward.normalized : _direction;
            }
            else if (aimForward.sqrMagnitude > 1e-6f)
            {
                _direction = aimForward.normalized;
            }
            Phase = State.Warning;
            if (_warning != null)
            {
                _warning.enabled = true;
            }
        }

        // 施放者在照射前被擊倒:還沒開始照射的喇叭消失(已在照射的照常照完)
        public void Cancel()
        {
            if (Phase == State.Firing)
            {
                return;
            }
            if (InkSplashFxPool.Instance != null)
            {
                InkSplashFxPool.Instance.Spawn(transform.position, Vector3.up, TeamPalette.ColorOf(_team));
            }
            Release();
        }

        private void Update()
        {
            if (Phase == State.Idle || _config == null)
            {
                return;
            }
            float now = Time.time;
            float dt = Time.deltaTime;
            if (Phase == State.Following || Phase == State.Warning)
            {
                // 照射前跟著施放者(隊形位置平滑跟上)
                if (_owner != null && _owner.isActiveAndEnabled)
                {
                    Vector3 goal = FormationPosition();
                    transform.position = _placed ? Vector3.Lerp(transform.position, goal, 1f - Mathf.Exp(-18f * dt)) : goal;
                    _placed = true;
                }
                if (Phase == State.Following && _owner != null && _owner.AimView != null)
                {
                    _direction = _owner.AimView.AimForward;
                }
            }
            if (Phase == State.Warning || Phase == State.Firing)
            {
                RecordTarget(now);
                Steer(dt, now);
            }
            if (Phase == State.Warning && now >= _fireAt)
            {
                StartFiring(now);
            }
            if (Phase == State.Firing)
            {
                // 照射第 0、5、10…175F 各判定一次(180F / 5F = 36 次;以次數計,不受浮點累加誤差影響)
                int totalTicks = _config.LaserFrames / Mathf.Max(1, _config.DamageIntervalFrames);
                while (_ticksDone < totalTicks
                       && now >= _fireAt + _ticksDone * _config.DamageIntervalFrames / 60f)
                {
                    _ticksDone++;
                    Ticks++;
                    DealDamage();
                }
                if (now >= _endAt)
                {
                    Release();
                    return;
                }
            }
            if (_direction.sqrMagnitude > 1e-6f)
            {
                transform.rotation = Quaternion.LookRotation(_direction);
            }
            UpdateLines();
        }

        private void StartFiring(float now)
        {
            Phase = State.Firing;
            _ticksDone = 0;
            ActiveBeams++;
            if (_warning != null)
            {
                _warning.enabled = false;
            }
            if (_beam != null)
            {
                _beam.enabled = true;
            }
            if (_slot % 2 == 0)
            {
                // 一組兩支只開一個循環音(6 個同時響太吵)
                _loop = GameAudio.StartLoop(_config.BeamLoop, transform, true, 0.8f);
            }
        }

        // 光束轉向:追目標半秒前的位置(內插 + 最大轉速);沒目標/目標倒下 → 繞垂直軸單向掃
        private void Steer(float dt, float now)
        {
            bool tracking = _target != null && _target.isActiveAndEnabled && (_targetLife == null || !_targetLife.IsDead);
            if (!tracking)
            {
                if (_target != null)
                {
                    // 鎖定的人被擊倒:照原本的轉向繼續轉(Inkipedia)
                    _sweepSign = _lastYawRate >= 0f ? 1f : -1f;
                    _target = null;
                }
                float sweep = _sweepSign * _config.FreeSweepDegPerFrame * 60f * dt;
                _direction = Quaternion.AngleAxis(sweep, Vector3.up) * _direction;
                return;
            }
            Vector3 delayed = TargetPoint(DelayedTargetPosition(now - _config.TrackDelaySeconds));
            Vector3 desired = delayed - Origin;
            if (desired.sqrMagnitude < 1e-6f)
            {
                return;
            }
            desired.Normalize();
            float blend = 1f - Mathf.Pow(1f - Mathf.Clamp01(_config.TrackInterp), dt * 60f);
            Vector3 blended = Vector3.Slerp(_direction, desired, blend);
            float maxRadians = _config.MaxTurnDegPerFrame * Mathf.Deg2Rad * 60f * dt;
            Vector3 next = Vector3.RotateTowards(_direction, blended, maxRadians, 0f);
            float yawBefore = Mathf.Atan2(_direction.x, _direction.z);
            float yawAfter = Mathf.Atan2(next.x, next.z);
            float yawDelta = Mathf.DeltaAngle(yawBefore * Mathf.Rad2Deg, yawAfter * Mathf.Rad2Deg);
            if (Mathf.Abs(yawDelta) > 0.01f)
            {
                _lastYawRate = yawDelta;
            }
            _direction = next;
        }

        private static Vector3 TargetPoint(Vector3 feet)
        {
            return feet + Vector3.up * 0.8f;
        }

        private void RecordTarget(float now)
        {
            if (_target == null)
            {
                return;
            }
            _history[_historyNext] = _target.transform.position;
            _historyTime[_historyNext] = now;
            _historyNext = (_historyNext + 1) % HistorySize;
            _historyCount = Mathf.Min(_historyCount + 1, HistorySize);
        }

        // time 時刻的目標位置(歷史內插;比最舊的還早 → 最舊的)
        private Vector3 DelayedTargetPosition(float time)
        {
            if (_historyCount == 0)
            {
                return _target != null ? _target.transform.position : Origin + _direction;
            }
            int newest = (_historyNext - 1 + HistorySize) % HistorySize;
            int oldest = (_historyNext - _historyCount + HistorySize) % HistorySize;
            if (time >= _historyTime[newest])
            {
                return _history[newest];
            }
            if (time <= _historyTime[oldest])
            {
                return _history[oldest];
            }
            int index = newest;
            for (int i = 0; i < _historyCount - 1; i++)
            {
                int previous = (index - 1 + HistorySize) % HistorySize;
                if (_historyTime[previous] <= time)
                {
                    float span = _historyTime[index] - _historyTime[previous];
                    float t = span > 1e-6f ? (time - _historyTime[previous]) / span : 1f;
                    return Vector3.Lerp(_history[previous], _history[index], t);
                }
                index = previous;
            }
            return _history[oldest];
        }

        // 光束判定:光束線段(起點沿方向 BeamLength)半徑 BeamRadius 內的敵方目標;穿牆、穿屏障(屏障本身也吃)
        private void DealDamage()
        {
            Vector3 origin = Origin;
            Vector3 dir = _direction;
            float length = _config.BeamLength;
            float radius = _config.BeamRadius;
            var targets = DamageableRegistry.Active;
            for (int i = targets.Count - 1; i >= 0; i--)
            {
                if (i >= targets.Count)
                {
                    continue;
                }
                IDamageable target = targets[i];
                if (target == null || !target.IsAlive || !DamageRules.CanDamage(_team, target))
                {
                    continue;
                }
                Vector3 point;
                if (target is BarrierSphere barrier)
                {
                    // 光束穿過球罩:屏障吃傷害,光束照樣打到罩內
                    if (SphereBarrierMath.SegmentDistanceSq(barrier.Center, origin, origin + dir * length)
                        > barrier.Radius * barrier.Radius)
                    {
                        continue;
                    }
                    point = barrier.ClosestPoint(origin);
                }
                else if (!BeamTouches(target, origin, dir, length, radius, out point))
                {
                    continue;
                }
                target.ApplyDamage(new DamageInfo(_config.DamagePerTick, point, _team, _owner, DamageKind.Special,
                    AttackClass.KillerWail));
                DamageDealt += _config.DamagePerTick;
                Hits++;
                TotalHits++;
            }
        }

        // 目標最近點離光束線段 ≤ radius(兩次交替求最近點;膠囊/盒子這種凸形狀收斂很快)
        private static bool BeamTouches(IDamageable target, Vector3 origin, Vector3 dir, float length, float radius,
            out Vector3 point)
        {
            point = target.ClosestPoint(origin);
            for (int k = 0; k < 2; k++)
            {
                float s = Mathf.Clamp(Vector3.Dot(point - origin, dir), 0f, length);
                point = target.ClosestPoint(origin + dir * s);
            }
            float along = Mathf.Clamp(Vector3.Dot(point - origin, dir), 0f, length);
            return (origin + dir * along - point).sqrMagnitude <= radius * radius;
        }

        private void UpdateLines()
        {
            Vector3 origin = Origin;
            if (Phase == State.Firing && _beam != null)
            {
                _beam.SetPosition(0, origin);
                _beam.SetPosition(1, origin + _direction * _config.BeamLength);
            }
            else if (Phase == State.Warning && _warning != null)
            {
                _warning.SetPosition(0, origin);
                _warning.SetPosition(1, origin + _direction * _config.BeamLength);
            }
        }

        private Vector3 FormationPosition()
        {
            if (_owner == null)
            {
                return transform.position;
            }
            float yaw = _owner.AimView != null ? _owner.AimView.Yaw : _owner.transform.eulerAngles.y;
            Quaternion facing = Quaternion.Euler(0f, yaw, 0f);
            Vector2 hex = KillerWailModel.HexSlot(_slot, _config.FormationRadius);
            Vector3 local = _config.FormationCenter + new Vector3(hex.x, hex.y, 0f);
            return _owner.transform.position + facing * local;
        }

        private void Release()
        {
            if (Phase == State.Firing)
            {
                ActiveBeams = Mathf.Max(0, ActiveBeams - 1);
            }
            Phase = State.Idle;
            if (_loop != null)
            {
                _loop.Stop(0.15f);
                _loop = null;
            }
            if (_beam != null)
            {
                _beam.enabled = false;
            }
            if (_warning != null)
            {
                _warning.enabled = false;
            }
            _target = null;
            _targetLife = null;
            _owner = null;
            SharedPool.Despawn(this);
        }

        private void OnDisable()
        {
            if (Phase == State.Firing)
            {
                ActiveBeams = Mathf.Max(0, ActiveBeams - 1);
            }
            Phase = State.Idle;
            if (_loop != null)
            {
                _loop.Stop(0f);
                _loop = null;
            }
        }
    }
}

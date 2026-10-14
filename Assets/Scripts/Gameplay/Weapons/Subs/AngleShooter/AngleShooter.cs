using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 池化標線器(S3 LineMarker;參數見 AngleShooterConfig / AngleShooterModel)。以 60 Hz 固定步模擬(SimulatedBomb):
    //   飛行(7F):每幀沿 AngleShooterTrace 走 6.3 u(與預覽同一套),反彈 → 反彈點塗 2.6 + 音效;
    //         直擊敵方/中立目標 → 40 傷(對物件 ×1.0)、腳下塗 2.0、停下;撞上敵方球罩 → 扣球罩耐久、停下。
    //   殘留(90F):線段(原點 + 反彈點 + 終點)留在場上,每幀把碰到線的敵方角色標記(從丟出起算 300F,SubMarking → Marking)。
    //   外觀:飛鏢頭 + 世界座標 LineRenderer(隊伍色),最後 FadeSeconds 淡出。
    public sealed class AngleShooter : SimulatedBomb
    {
        private const int MaxPoints = AngleShooterConfig.MaxBounces + 3;
        private const int MaxStepsPerFrame = 12;

        [SerializeField, Tooltip("線段(世界座標 LineRenderer,放在根物件下)")]
        private LineRenderer _line;

        private AngleShooterConfig _config;
        private AngleShooterParams _p;
        private AngleShooterTrace _trace;
        private readonly Vector3[] _points = new Vector3[MaxPoints];
        private int _pointCount;
        private bool _ended;
        private int _endFrame;
        private float _pathLength;
        private Color _appliedLine = Color.clear;
        private int _marksThisShot;

        // 測試/除錯
        public static int FireCount { get; private set; }
        public static int BounceTotal { get; private set; }
        public static int DirectHitCount { get; private set; }
        public static int FinishedCount { get; private set; }
        public static float LastFireTime { get; private set; }
        public static float LastEndTime { get; private set; }
        public static float LastReleaseTime { get; private set; }
        public static int LastEndFrame { get; private set; }
        public static int LastBounces { get; private set; }
        public static float LastPathLength { get; private set; }
        public static Vector3 LastOrigin { get; private set; }
        public static Vector3 LastEndPoint { get; private set; }
        public static IDamageable LastDirectTarget { get; private set; }
        public static Vector3 LastFirstBouncePoint { get; private set; }
        public static Vector3 LastFirstBounceNormal { get; private set; }
        public static Vector3 LastFirstBounceIn { get; private set; }
        public static Vector3 LastFirstBounceOut { get; private set; }
        public static int LastMarkApplications { get; private set; }

        public bool Ended => _ended;
        public int PointCount => _pointCount;
        public Vector3 PointAt(int index)
        {
            return _points[Mathf.Clamp(index, 0, MaxPoints - 1)];
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetCounters()
        {
            FireCount = 0;
            BounceTotal = 0;
            DirectHitCount = 0;
            FinishedCount = 0;
            LastDirectTarget = null;
            LastFireTime = float.NegativeInfinity;
            LastEndTime = float.NegativeInfinity;
            LastReleaseTime = float.NegativeInfinity;
        }

        protected override void Awake()
        {
            base.Awake();
            if (_line != null)
            {
                _line.useWorldSpace = true;
                _line.positionCount = 0;
                _line.enabled = false;
            }
        }

        public override void Launch(Vector3 position, Vector3 velocity, SubWeaponConfig config, Inkling owner)
        {
            _config = config as AngleShooterConfig;
            if (_config == null)
            {
                Debug.LogError($"AngleShooter:設定型別不符({config})", this);
                Despawn();
                return;
            }
            Begin(position, owner);
            _p = _config.S3;
            _trace.Init(position, velocity, AngleShooterModel.Range(_p));
            _pointCount = 0;
            AddPoint(position);
            _ended = false;
            _endFrame = 0;
            _pathLength = 0f;
            _marksThisShot = 0;
            Model.gameObject.SetActive(true);
            transform.rotation = Quaternion.LookRotation(_trace.Direction);
            if (_line != null)
            {
                _line.enabled = true;
                _line.widthMultiplier = _config.LineWidth;
                _appliedLine = Color.clear;
            }
            FireCount++;
            LastFireTime = Time.time;
            LastOrigin = position;
            LastFirstBouncePoint = Vector3.zero;
            LastFirstBounceNormal = Vector3.zero;
            RefreshLine(position);
        }

        protected override bool Step()
        {
            if (!_ended)
            {
                Fly();
            }
            // 線段存在期間:碰到線的敵人標記(從丟出起算 300F)
            float seconds = AngleShooterModel.MarkSecondsAt(_p, Frame);
            if (seconds > 0f)
            {
                for (int i = 1; i < _pointCount; i++)
                {
                    _marksThisShot += SubMarking.MarkSegment(_points[i - 1], _points[i], _p.TouchRadius, BombTeam, seconds);
                }
                if (!_ended)
                {
                    _marksThisShot += SubMarking.MarkSegment(_points[_pointCount - 1], _trace.Position, _p.TouchRadius,
                        BombTeam, seconds);
                }
            }
            if (_ended && Frame - _endFrame >= _p.TailFrames)
            {
                LastMarkApplications = _marksThisShot;
                LastReleaseTime = Time.time;
                Release();
                return false;
            }
            return true;
        }

        // 一幀 6.3 u(S3 FlyingFrame 7 → 第 7 幀走完 44.1 u)
        private void Fly()
        {
            float budget = _p.SpeedPerFrame;
            for (int guard = 0; guard < MaxStepsPerFrame && budget > 1e-5f && !_ended; guard++)
            {
                Vector3 before = _trace.Position;
                Vector3 direction = _trace.Direction;
                AngleShooterEvent e = _trace.Advance(_p, budget, BombTeam, true, out float travelled,
                    out RaycastHit hit, out IDamageable target, out BarrierHit barrier);
                budget -= travelled;
                _pathLength += Vector3.Distance(before, _trace.Position);
                switch (e)
                {
                    case AngleShooterEvent.Bounce:
                        OnBounce(hit, direction);
                        if (_trace.Bounces >= AngleShooterConfig.MaxBounces)
                        {
                            End();
                        }
                        break;
                    case AngleShooterEvent.Target:
                        OnDirectHit(target, hit);
                        End();
                        break;
                    case AngleShooterEvent.Barrier:
                        if (barrier.Barrier != null)
                        {
                            barrier.Barrier.ApplyDamage(new DamageInfo(_p.DirectDamage, barrier.Point, BombTeam, Owner,
                                DamageKind.Sub, AttackClass.Other));
                        }
                        End();
                        break;
                    default:
                        if (_trace.Finished)
                        {
                            End();
                        }
                        break;
                }
            }
            SimPosition = _trace.Position;
            // 超過飛行幀數(浮點誤差)也結束
            if (!_ended && Frame >= _p.FlyingFrames)
            {
                End();
            }
        }

        private void OnBounce(in RaycastHit hit, Vector3 incoming)
        {
            AddPoint(_trace.Position);
            BounceTotal++;
            if (_trace.Bounces == 1)
            {
                LastFirstBouncePoint = hit.point;
                LastFirstBounceNormal = hit.normal;
                LastFirstBounceIn = incoming;
                LastFirstBounceOut = _trace.Direction;
            }
            // 每次反彈在反彈點塗 2.6(S3 PaintRadius)
            InkSource source = Owner != null ? Owner.InkSource() : new InkSource(BombTeam);
            InkPainter.PaintSphere(hit.point, _p.BouncePaintRadius, _config.PaintHardness, source);
            GameAudio.Play(_config.BounceSound, hit.point, 0.7f, 0.06f);
        }

        // 直擊:40 傷、目標腳下塗 2.0(往下找 ≤ 3 u 的地面)、角色順便標記
        private void OnDirectHit(IDamageable target, in RaycastHit hit)
        {
            AddPoint(_trace.Position);
            DirectHitCount++;
            LastDirectTarget = target;
            target.ApplyDamage(new DamageInfo(_p.DirectDamage, hit.point, BombTeam, Owner, DamageKind.Sub,
                AttackClass.Other));
            if (Physics.Raycast(hit.point + Vector3.up * 0.1f, Vector3.down, out RaycastHit ground,
                    _p.HitPaintHeight + 0.1f, CombatMasks.Terrain, QueryTriggerInteraction.Ignore))
            {
                InkSource source = Owner != null ? Owner.InkSource() : new InkSource(BombTeam);
                InkPainter.PaintSphere(ground.point, _p.HitPaintRadius, _config.PaintHardness, source);
            }
            if (target is InklingHealth health)
            {
                var inkling = health.GetComponent<Inkling>();
                if (inkling != null)
                {
                    Marking.Apply(inkling, BombTeam, AngleShooterModel.MarkSecondsAt(_p, Frame));
                }
            }
            GameAudio.Play(_config.HitSound, hit.point, 0.9f, 0.04f);
        }

        private void End()
        {
            if (_ended)
            {
                return;
            }
            _ended = true;
            _endFrame = Frame;
            Vector3 last = _pointCount > 0 ? _points[_pointCount - 1] : _trace.Position;
            if ((last - _trace.Position).sqrMagnitude > 1e-6f)
            {
                AddPoint(_trace.Position);
            }
            FinishedCount++;
            LastEndFrame = Frame;
            LastEndTime = Time.time;
            LastEndPoint = _trace.Position;
            LastBounces = _trace.Bounces;
            LastPathLength = _pathLength;
            Model.gameObject.SetActive(false);
        }

        private void AddPoint(Vector3 point)
        {
            if (_pointCount < MaxPoints)
            {
                _points[_pointCount++] = point;
            }
            else
            {
                _points[MaxPoints - 1] = point;
            }
        }

        protected override void OnReleased()
        {
            if (_line != null)
            {
                _line.positionCount = 0;
                _line.enabled = false;
            }
            Model.gameObject.SetActive(true);
            _config = null;
        }

        // ── 外觀 ──

        protected override void UpdateVisual()
        {
            if (!_ended)
            {
                if (_trace.Direction.sqrMagnitude > 1e-6f)
                {
                    transform.rotation = Quaternion.LookRotation(_trace.Direction);
                }
                RefreshLine(transform.position);
            }
            else
            {
                RefreshLine(Vector3.zero);
            }
            if (_line == null)
            {
                return;
            }
            float alpha = 0.95f;
            if (_ended)
            {
                float rest = (_p.TailFrames - (Frame - _endFrame)) / 60f;
                alpha *= Mathf.Clamp01(rest / Mathf.Max(0.05f, _config.FadeSeconds));
            }
            Color color = TeamPalette.ColorOf(BombTeam);
            color.a = Mathf.Round(alpha * 32f) / 32f;
            if (color != _appliedLine)
            {
                _appliedLine = color;
                TeamTint.Apply(_line, color);
            }
        }

        // 線段 = 已走過的點 +(飛行中)目前的飛鏢頭
        private void RefreshLine(Vector3 head)
        {
            if (_line == null)
            {
                return;
            }
            int count = _pointCount + (_ended ? 0 : 1);
            if (_line.positionCount != count)
            {
                _line.positionCount = count;
            }
            for (int i = 0; i < _pointCount; i++)
            {
                _line.SetPosition(i, _points[i]);
            }
            if (!_ended)
            {
                _line.SetPosition(_pointCount, head);
            }
        }
    }
}

using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // Fizzy Bomb 的狀態
    public enum FizzyState : byte
    {
        // 投出後的拋物線(沿途往下落墨)
        Flying = 0,
        // 著地滾動(等下一爆)
        Rolling = 1,
        // 爆完彈起、還沒再著地
        Hopping = 2,
    }

    // 池化碳酸炸彈(S3 Bomb_Fizzy;參數見 FizzyBombConfig / FizzyBombModel)。以 60 Hz 固定步模擬(SimulatedBomb):
    //   Flying:S3 通用彈道(與預覽同一個 S3ThrownFlight),穿過角色;每水平 2.2 u 往正下方落一筆墨(半寬依段數)。
    //           撞牆反彈(法線、切線同比例 0.7 → 入射角 = 反射角);第一次碰到地面 → Rolling,排定第 1 爆(30F 後)。
    //   Rolling:BombRoll 滾(阻力 0.19);時間到 → 爆炸(第 n 爆的半徑/塗墨)→ 還有下一爆就 Hopping(0.24~0.32 u/F、48~58°)。
    //   Hopping:再次著地 → 排定下一爆(8F 後)。滾出邊緣/彈跳中照常計時。
    //   碰到敵方屏障/球罩(飛行中 CastPath、滾動中 TouchDetonator):立刻爆這一爆(S3 4.0.0 修正後無延遲),之後照常彈跳。
    // 爆炸次數 = 放開時的蓄力段數(1~3)。
    public sealed class FizzyBomb : SimulatedBomb
    {
        private const int MaxBursts = 3;

        private FizzyBombConfig _config;
        private FizzyBombParams _p;
        private FizzyState _state;
        private S3ThrownFlight _flight;
        private BombRoll _roll;
        private int _level;
        private int _bursts;
        private int _burstDue = -1;
        private int _hopFrame;
        private Vector3 _heading = Vector3.forward;
        private float _trailPending;
        private float _trailArea;
        private int _trailStamps;
        private float _spin;
        private bool _hissed;
        private readonly Vector3[] _burstCenters = new Vector3[MaxBursts];
        private readonly int[] _burstFrames = new int[MaxBursts];

        // 測試/除錯:累計與最近一顆的量測
        public static int LaunchCount { get; private set; }
        public static int BurstCount { get; private set; }
        public static int FinishedCount { get; private set; }
        public static int DetonatorHitCount { get; private set; }
        public static int LastLevel { get; private set; }
        public static int LastBurstTotal { get; private set; }
        public static int LastFirstLandFrame { get; private set; }
        public static float LastLaunchTime { get; private set; }
        public static float LastFirstLandTime { get; private set; }
        public static float LastBurstTime { get; private set; }
        public static Vector3 LastFirstLandPoint { get; private set; }
        public static Vector3 LastBurstCenter { get; private set; }
        public static int LastBurstIndex { get; private set; }
        public static float LastTrailArea { get; private set; }
        public static int LastTrailStamps { get; private set; }
        private static readonly Vector3[] LastCenters = new Vector3[MaxBursts];
        private static readonly int[] LastFrames = new int[MaxBursts];
        private static readonly int[] LastLandFrames = new int[MaxBursts];

        // 最近一顆第 index 爆的爆心與模擬幀(投出起算)
        public static Vector3 LastCenterOf(int index)
        {
            return LastCenters[Mathf.Clamp(index, 0, MaxBursts - 1)];
        }

        public static int LastFrameOf(int index)
        {
            return LastFrames[Mathf.Clamp(index, 0, MaxBursts - 1)];
        }

        // 最近一顆:排定第 index 爆的那次著地的模擬幀
        public static int LastLandFrameOf(int index)
        {
            return LastLandFrames[Mathf.Clamp(index, 0, MaxBursts - 1)];
        }

        public FizzyState State => _state;
        public int Level => _level;
        public int Bursts => _bursts;
        // 下一爆還有幾 F(沒排定 = −1)
        public int BurstRestFrames => _burstDue > 0 ? _burstDue - Frame : -1;
        // 滾動中的水平速度(m/s;不在地上 = −1)
        public float RollSpeed => _state == FizzyState.Rolling ? _roll.Speed * SimFrames.Rate : -1f;
        // 滾動中:照目前速度與阻力滾到停的位置(幾何級數;測試擺假人用)
        public Vector3 RestEstimate
        {
            get
            {
                if (_state != FizzyState.Rolling || _config == null)
                {
                    return SimPosition;
                }
                float r = Mathf.Clamp(_p.GroundResist, 0.01f, 1f);
                return SimPosition + _roll.Heading * (_roll.Speed * (1f - r) / r);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetCounters()
        {
            LaunchCount = 0;
            BurstCount = 0;
            FinishedCount = 0;
            DetonatorHitCount = 0;
            LastLaunchTime = float.NegativeInfinity;
            LastFirstLandTime = float.NegativeInfinity;
            LastBurstTime = float.NegativeInfinity;
        }

        public override void Launch(Vector3 position, Vector3 velocity, SubWeaponConfig config, Inkling owner)
        {
            LaunchCharged(position, velocity, config, owner, 0f);
        }

        public override void LaunchCharged(Vector3 position, Vector3 velocity, SubWeaponConfig config, Inkling owner,
            float charge01)
        {
            _config = config as FizzyBombConfig;
            if (_config == null)
            {
                Debug.LogError($"FizzyBomb:設定型別不符({config})", this);
                Despawn();
                return;
            }
            Begin(position, owner);
            _p = _config.S3;
            _level = Mathf.Clamp(_config.LevelAt01(charge01), 1, MaxBursts);
            _state = FizzyState.Flying;
            _flight.Init(position, velocity * SimFrames.Dt);
            Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
            _heading = flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.forward;
            _bursts = 0;
            _burstDue = -1;
            _hopFrame = 0;
            _trailPending = 0f;
            _trailArea = 0f;
            _trailStamps = 0;
            _spin = Random.Range(0f, 360f);
            _hissed = false;
            for (int i = 0; i < MaxBursts; i++)
            {
                _burstCenters[i] = Vector3.zero;
                _burstFrames[i] = 0;
            }
            SetLightColor(LevelColor(_level));
            LaunchCount++;
            LastLaunchTime = Time.time;
            LastLevel = _level;
        }

        protected override bool Step()
        {
            if (_burstDue > 0 && Frame >= _burstDue)
            {
                if (!Burst())
                {
                    return false;
                }
            }
            switch (_state)
            {
                case FizzyState.Rolling:
                    return StepRolling();
                default:
                    return StepAir();
            }
        }

        // ── 空中(投擲飛行 / 爆炸後彈跳)──

        private bool StepAir()
        {
            if (_state == FizzyState.Flying && Frame > SimFrames.ToFrames(_config.Lifetime))
            {
                Release();
                return false;
            }
            if (_state == FizzyState.Hopping && _burstDue < 0 && Frame - _hopFrame >= _config.HopTimeoutFrames)
            {
                // 保險:彈跳後一直沒著地(掉進很深的坑)就直接爆下一爆
                return Burst();
            }
            Vector3 from = _flight.Position;
            BombContact contact = _flight.Step(_p.Throw, _config.Radius, BombTeam, false, out RaycastHit hit, out _);
            SimPosition = _flight.Position;
            if (_state == FizzyState.Flying)
            {
                PaintTrail(from, SimPosition);
            }
            Vector3 flat = new Vector3(_flight.Velocity.x, 0f, _flight.Velocity.z);
            if (flat.sqrMagnitude > 1e-8f)
            {
                _heading = flat.normalized;
            }
            if (contact == BombContact.None)
            {
                return true;
            }
            if (contact == BombContact.Detonator)
            {
                // 炸彈共通規則:碰到敵方屏障立刻爆這一爆(之後照常彈跳)
                DetonatorHitCount++;
                return Burst();
            }
            Vector3 normal = hit.normal.sqrMagnitude > 1e-6f ? hit.normal.normalized : Vector3.up;
            if (normal.y >= GroundWalker.FloorNormalY)
            {
                Land(hit.point, normal);
                return true;
            }
            SimPosition = _flight.Position + normal * GroundWalker.Skin;
            _flight.Position = SimPosition;
            if (normal.y <= -GroundWalker.FloorNormalY)
            {
                Vector3 v = _flight.Velocity;
                v.y = -Mathf.Abs(v.y) * 0.3f;
                _flight.Velocity = v;
                return true;
            }
            // 撞牆:入射角 = 反射角(法線與切線同比例 0.7)[EST]
            _flight.Velocity = SimpleBallistics.Reflect(_flight.Velocity, normal, _p.WallRebound, _p.WallRebound);
            GameAudio.Play("bomb_bounce", hit.point, 0.55f);
            return true;
        }

        private void Land(Vector3 point, Vector3 normal)
        {
            bool first = _state == FizzyState.Flying;
            _roll.Land(point, normal, _flight.Velocity, _config.Radius, _p.LandingKeep);
            SimPosition = _roll.Position;
            _state = FizzyState.Rolling;
            if (_burstDue < 0)
            {
                // S3:第 1 爆 = 第一次著地 15 + 15 = 30F 後(與飛多久無關);之後 = 再次著地 1 + 7 = 8F 後
                _burstDue = Frame + FizzyBombModel.BurstDelayFrames(_p, _bursts);
                LastLandFrames[Mathf.Clamp(_bursts, 0, MaxBursts - 1)] = Frame;
            }
            if (first)
            {
                LastFirstLandFrame = Frame;
                LastFirstLandTime = Time.time;
                LastFirstLandPoint = point;
            }
            GameAudio.Play("bomb_bounce", point, 0.5f);
        }

        private bool StepRolling()
        {
            bool grounded = _roll.Step(_p.GroundResist, _p.WallRebound, _config.Radius, _config.StepHeight);
            SimPosition = _roll.Position;
            if (_roll.Speed > 1e-4f)
            {
                _heading = _roll.Heading;
            }
            if (BombRules.TouchDetonator(PrevPosition, SimPosition, _config.Radius, BombTeam, out Vector3 touch))
            {
                SimPosition = touch;
                DetonatorHitCount++;
                return Burst();
            }
            if (!grounded)
            {
                _state = FizzyState.Hopping;
                _hopFrame = Frame;
                _flight.Init(SimPosition, _roll.AirVelocity);
            }
            // 第 1 爆前半段(Wait 15)過後的 Warn 段開始嘶嘶聲
            if (!_hissed && _bursts == 0 && _burstDue > 0 && _burstDue - Frame <= _p.FirstBurstWarnFrames)
            {
                _hissed = true;
                GameAudio.Play(_config.HissSound, SimPosition, 0.8f, 0.05f);
            }
            return true;
        }

        // ── 爆炸 ──

        // 回傳 false = 最後一爆,已回池
        private bool Burst()
        {
            int index = Mathf.Clamp(_bursts, 0, MaxBursts - 1);
            Vector3 center = SimPosition;
            ExplosionSpec spec = _config.BlastAt(index);
            Explosion.Detonate(spec, center, BombTeam, Owner);
            _burstCenters[index] = center;
            _burstFrames[index] = Frame;
            LastCenters[index] = center;
            LastFrames[index] = Frame;
            _bursts++;
            BurstCount++;
            LastBurstIndex = index;
            LastBurstCenter = center;
            LastBurstTime = Time.time;
            _burstDue = -1;
            if (_bursts >= _level)
            {
                FinishedCount++;
                LastBurstTotal = _bursts;
                LastTrailArea = _trailArea;
                LastTrailStamps = _trailStamps;
                Release();
                return false;
            }
            // S3 Hop:往前進方向(加一點左右亂數)彈起,再次著地 8F 後下一爆
            Vector3 hop = FizzyBombModel.HopVelocity(_p, _heading, Random.value, Random.value, Random.value);
            _flight.Init(center + Vector3.up * GroundWalker.Skin, hop);
            _state = FizzyState.Hopping;
            _hopFrame = Frame;
            return true;
        }

        // 飛行軌跡:每水平 TrailSpacing 往正下方(≤ 10 u)落一筆墨,半寬依段數(1.2 / 1.26 / 1.32),
        // 沿飛行方向拉長 1.6 倍(兩顆半徑 = 半寬的球前後錯開)→ 間距 2.2 的各筆相連成約 2.4 寬的帶
        private void PaintTrail(Vector3 from, Vector3 to)
        {
            Vector3 flat = to - from;
            flat.y = 0f;
            float moved = flat.magnitude;
            _trailPending += moved;
            float spacing = Mathf.Max(0.2f, _p.TrailSpacing);
            if (_trailPending < spacing)
            {
                return;
            }
            _trailPending -= spacing;
            if (!Physics.Raycast(to, Vector3.down, out RaycastHit ground, _p.TrailDropHeight, CombatMasks.Terrain,
                    QueryTriggerInteraction.Ignore))
            {
                return;
            }
            InkSource source = Owner != null ? Owner.InkSource() : new InkSource(BombTeam);
            float halfWidth = FizzyBombModel.TrailHalfWidth(_p, _level);
            Vector3 along = moved > 1e-5f ? flat / moved : _heading;
            float offset = halfWidth * Mathf.Max(0f, _p.TrailDepthScale - 1f);
            _trailArea += InkPainter.PaintSphere(ground.point + along * offset, halfWidth, _config.PaintHardness, source);
            _trailArea += InkPainter.PaintSphere(ground.point - along * offset, halfWidth, _config.PaintHardness, source);
            _trailStamps++;
        }

        protected override void OnReleased()
        {
            _config = null;
        }

        // ── 外觀 ──

        protected override void UpdateVisual()
        {
            float speed = _state == FizzyState.Rolling ? _roll.Speed : _flight.Velocity.magnitude;
            _spin = Mathf.Repeat(_spin + speed * SimFrames.Rate * 90f * Time.deltaTime + 60f * Time.deltaTime, 360f);
            Vector3 heading = _heading.sqrMagnitude > 1e-6f ? _heading : Vector3.forward;
            // 罐子橫躺翻滾(飛行、滾動)
            transform.rotation = Quaternion.LookRotation(heading) * Quaternion.Euler(_spin, 0f, 90f);
            int rest = BurstRestFrames;
            bool warning = rest >= 0 && rest <= (_bursts == 0 ? _p.FirstBurstWarnFrames : _p.NextBurstWarnFrames);
            // 警告段:膨脹 + 抖動(S3 罐子鼓起來噴氣)
            if (warning)
            {
                float t = 1f - rest / (float)Mathf.Max(1, _bursts == 0 ? _p.FirstBurstWarnFrames : _p.NextBurstWarnFrames);
                SetSwell(1f + 0.18f * t + 0.04f * Mathf.Sin(Time.time * 70f));
                transform.position += Random.insideUnitSphere * 0.02f;
            }
            else
            {
                SetSwell(1f);
            }
            SetLights(!warning || Blink(Time.time, 0.08f));
        }

        private static Color LevelColor(int level)
        {
            switch (level)
            {
                case 3:
                    return new Color(1f, 0.3f, 0.25f);
                case 2:
                    return new Color(1f, 0.8f, 0.2f);
                default:
                    return new Color(0.45f, 0.85f, 1f);
            }
        }
    }
}

using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 墨彈命中/結束的原因
    public enum InkBulletEnd : byte
    {
        // 打到地形(落地/牆/天花板)
        Terrain = 0,
        // 打到目標並停下
        Target = 1,
        // 貫穿目標(沒有停下;同一發可能有多筆)
        Pierce = 2,
        // 第 ExplodeAtFrame 幀空爆
        AirBurst = 3,
        // 飛到 MaxDistance
        MaxDistance = 4,
        // 壽命盡頭 / 掉出場外
        Expired = 5,
        // 被敵方 Ink Vac 的吸引漩渦吸走(不傷人、不塗墨、不爆炸)
        Absorbed = 6,
    }

    // 一筆墨彈事件(InkBullet.Impacted 事件、InkBullet.LastImpact):測試量測與 HUD 用
    public struct InkBulletImpact
    {
        public InkBullet Bullet;
        public InkBulletEnd End;
        public Vector3 Point;
        public Vector3 Normal;
        // 命中/結束時的飛行幀數(第一次 60 Hz 步進 = 1)與沿彈道距離(公尺;本步終點)
        public int Frame;
        public float Travelled;
        // 打到目標時「接觸瞬間」的沿彈道距離(sphere cast 碰到的位置;一步可達 2 m,距離分段傷害要用這個)。
        // 其他結束原因 = Travelled
        public float ContactTravelled;
        public BulletPhase Phase;
        public IDamageable Target;
        // 實際結算的傷害(去重/上限後;0 = 被同一次攻擊去重掉)
        public float Damage;
        public bool Exploded;
        public Team Team;
        public int AttackId;
    }

    public delegate void InkBulletImpactHandler(in InkBulletImpact impact);

    // 自訂傷害(可選,Fire 時傳入):frameDamage = 規格依飛行幀數算出的傷害;回傳值會再取整到 0.1。
    // 用途:潑桶依「低於槍口的落差」衰減(bullet.Origin / StraightEndPosition / State.Position)、
    // 滾筒依飛行距離分段衰減(bullet.ContactTravelled:接觸瞬間的距離,不是本步終點)等。武器模組快取委派實例,避免每發配置。
    public delegate float InkBulletDamageFunc(InkBullet bullet, IDamageable target, float frameDamage);

    // 通用池化墨彈:S3 三狀態彈道(BulletBallistics)以固定 60 Hz 子步模擬,任何幀率結果相同;渲染在兩步之間插值。
    // 碰撞:每步兩次 sphere cast——對地形用 RadiusForField、對目標(角色/假人/場上裝置)用 RadiusForPlayer(可在前幾幀長大);
    //   同隊直接穿過(Splatoon 沒有友傷、子彈不被隊友擋);敵方/中立 IDamageable 依飛行幀數吃傷害(ProjectileDamageCurve)。
    // 同一次攻擊去重:Fire 傳同一個 attackId(CombatLedger.NewAttackId),同一目標只吃第一發或累加到 PerAttackDamageCap。
    // 落地塗墨:地面 = 沿飛行方向拉長的橢圓(1~3 顆球近似,見 PaintFloorStamp);牆 = 圓 PaintRadiusShock;
    //   途中每 DripSpacing 公尺往正下方滴一滴(落點在滴下時就決定;InkBulletDrip 播落下動畫,著地才塗)。
    // 可選:撞擊/空爆/射程盡頭爆炸(ExplosionSpec)、貫穿角色、最大距離(狙擊)。
    // 發射:InkBullet.Fire(spec, 位置, 方向, 隊伍, 擁有者, attackId, 繼承速度)。prefab 預設取 Resources/Weapons/CombatAssets。
    public sealed class InkBullet : MonoBehaviour
    {
        private const int MaxStepsPerFrame = 8;
        private const int CastBufferSize = 16;
        private const int MaxPierced = 8;
        private const float KillPlaneY = -40f;

        // 全墨彈共用的查詢緩衝(主執行緒單線使用)
        private static readonly RaycastHit[] TerrainHits = new RaycastHit[CastBufferSize];
        private static readonly RaycastHit[] TargetHits = new RaycastHit[CastBufferSize];
        private static readonly IDamageable[] CandidateTargets = new IDamageable[CastBufferSize];
        private static readonly float[] CandidateDistances = new float[CastBufferSize];
        private static readonly Vector3[] CandidatePoints = new Vector3[CastBufferSize];
        private static bool _missingPrefabLogged;

        [SerializeField, Tooltip("外觀根(沿速度方向旋轉、依規格縮放);留空用自己")]
        private Transform _visual;

        private TrailRenderer _trail;
        private Renderer[] _renderers;
        private Color _appliedColor = Color.clear;
        private float _defaultTrailTime;
        private Vector3 _baseVisualScale = Vector3.one;

        private InkBulletSpec _spec;
        private BulletState _state;
        private FixedStepClock _clock;
        private Vector3 _renderFrom;
        private Vector3 _origin;
        private Team _team;
        private Inkling _owner;
        private int _attackId;
        private InkBulletDamageFunc _damageFunc;
        private Vector3 _straightEnd;
        private bool _active;
        private float _nextDripAt;
        private int _drips;
        private readonly IDamageable[] _pierced = new IDamageable[MaxPierced];
        private int _piercedCount;
        // 命中結算中:接觸瞬間的沿彈道距離(< 0 = 不在結算中)
        private float _contactTravelled = -1f;
        // 螺旋(Trizooka):以發射方向為軸的右/上基底
        private Vector3 _orbitRight;
        private Vector3 _orbitUp;

        // 統計(測試用):場上數量、累計發射數、累計事件數、最近一筆事件
        public static int ActiveCount { get; private set; }
        public static int FiredCount { get; private set; }
        public static int ImpactCount { get; private set; }
        public static InkBulletImpact LastImpact { get; private set; }

        // 每筆命中/結束事件(同一發貫穿時會有多筆)
        public static event InkBulletImpactHandler Impacted;

        public BulletState State => _state;

        // 目前結算中那次命中的「接觸瞬間」沿彈道距離(公尺):本步 sphere cast 碰到目標的位置,
        // 而不是本步終點的 State.Travelled(高速彈一步可達 2 m)。依飛行距離分段傷害的武器(滾筒、筆刷)在 damageFunc 用它;
        // 不在命中結算中 = State.Travelled
        public float ContactTravelled => _contactTravelled >= 0f ? _contactTravelled : _state.Travelled;

        // 螺旋起始角(度;規格 OrbitRadius > 0 才有作用)。Fire 之後、第一次 Update 之前設定(Trizooka 三顆差 180°)
        public float OrbitPhaseDeg { get; set; }

        // 目前的實際位置(彈道軸 + 螺旋偏移;不螺旋時 = State.Position)
        public Vector3 Position => _state.Position + OrbitOffset(_state.Frame);
        public Team Team => _team;
        public Inkling Owner => _owner;
        public int AttackId => _attackId;
        public bool IsFlying => _active;

        // 發射點(槍口)與直線狀態結束時的位置(潑桶落差衰減:直線段的落差不算)
        public Vector3 Origin => _origin;
        public Vector3 StraightEndPosition => _straightEnd;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetOnLoad()
        {
            ActiveCount = 0;
            FiredCount = 0;
            ImpactCount = 0;
            LastImpact = default;
            Impacted = null;
            _missingPrefabLogged = false;
        }

        // 發射一發墨彈。direction 不必正規化;inheritVelocity(m/s)加在初速上(S3:射擊槍前向 ×2.0/橫向 ×0.4 的玩家速度,
        // 由武器模組先乘好)。prefab 留空 = CombatAssets 的通用墨彈。damageFunc 留空 = 依規格的飛行幀數衰減。
        // 回傳 null = 缺 prefab(未跑 M7 Setup)。
        public static InkBullet Fire(in InkBulletSpec spec, Vector3 position, Vector3 direction, Team team,
            Inkling owner, int attackId = 0, Vector3 inheritVelocity = default, InkBullet prefab = null,
            InkBulletDamageFunc damageFunc = null)
        {
            if (prefab == null)
            {
                CombatAssets assets = CombatAssets.Instance;
                prefab = assets != null ? assets.InkBulletPrefab : null;
            }
            if (prefab == null)
            {
                if (!_missingPrefabLogged)
                {
                    _missingPrefabLogged = true;
                    Debug.LogError("InkBullet:找不到墨彈 prefab(Resources/Weapons/CombatAssets;先跑 Tools/SplatoonC/M7 Setup)");
                }
                return null;
            }
            InkBullet bullet = SharedPool.Spawn(prefab);
            bullet.Launch(spec, position, direction, team, owner, attackId, inheritVelocity);
            bullet._damageFunc = damageFunc;
            return bullet;
        }

        private void Awake()
        {
            if (_visual == null)
            {
                _visual = transform;
            }
            _baseVisualScale = _visual.localScale;
            _trail = GetComponentInChildren<TrailRenderer>(true);
            _defaultTrailTime = _trail != null ? _trail.time : 0f;
            _renderers = GetComponentsInChildren<Renderer>(true);
        }

        private void Launch(in InkBulletSpec spec, Vector3 position, Vector3 direction, Team team, Inkling owner,
            int attackId, Vector3 inheritVelocity)
        {
            _spec = spec;
            _team = team;
            _owner = owner;
            _attackId = attackId;
            _origin = position;
            _straightEnd = position;
            _damageFunc = null;
            _state = BulletBallistics.Launch(position, direction, _spec.Motion, inheritVelocity * SimFrames.Dt);
            OrbitPhaseDeg = 0f;
            if (_spec.OrbitRadius > 0f)
            {
                TrizookaModel.OrbitBasis(direction, out _orbitRight, out _orbitUp);
            }
            _clock.Reset();
            _renderFrom = position;
            _piercedCount = 0;
            _contactTravelled = -1f;
            _drips = 0;
            _nextDripAt = _spec.DripStartDistance > 0f ? _spec.DripStartDistance : _spec.DripSpacing;
            _active = true;
            ActiveCount++;
            FiredCount++;

            TeamTint.ApplyCached(_renderers, ref _appliedColor, TeamPalette.ColorOf(team));
            transform.position = position;
            if (_visual != transform)
            {
                _visual.localScale = _baseVisualScale * _spec.EffectiveVisualScale;
            }
            else
            {
                transform.localScale = _baseVisualScale * _spec.EffectiveVisualScale;
            }
            OrientAlong(_state.Velocity);
            if (_trail != null)
            {
                // 池化重用鐵律:清掉上一輪殘留拖尾,否則會從回收點畫一條線到槍口
                _trail.Clear();
                _trail.time = _spec.TrailTime > 0f ? _spec.TrailTime : _defaultTrailTime;
                _trail.emitting = _spec.TrailTime >= 0f;
            }
        }

        private void Update()
        {
            if (!_active)
            {
                return;
            }
            int steps = _clock.Advance(Time.deltaTime, MaxStepsPerFrame);
            for (int i = 0; i < steps; i++)
            {
                _renderFrom = Position;
                if (!StepOnce())
                {
                    return;
                }
            }
            // 渲染插值:上一步 → 這一步之間(落後一步,但不會畫進牆裡)
            transform.position = Vector3.LerpUnclamped(_renderFrom, Position, _clock.Alpha);
            OrientAlong(_state.Velocity);
        }

        private void OrientAlong(Vector3 velocity)
        {
            if (velocity.sqrMagnitude > 1e-8f)
            {
                transform.rotation = Quaternion.LookRotation(velocity);
            }
        }

        // 一個 60 Hz 步:彈道 → 碰撞 → 結算;回傳 false = 本發已結束回池
        private bool StepOnce()
        {
            // 碰撞線段用實際位置(彈道軸 + 螺旋偏移);不螺旋時偏移為 0
            Vector3 from = Position;
            Vector3 axisFrom = _state.Position;
            int frame = _state.Frame + 1;
            float fieldRadius = _spec.FieldRadiusAt(frame);
            float playerRadius = _spec.PlayerRadiusAt(frame);
            if (_state.Phase == BulletPhase.Straight)
            {
                _straightEnd = from;
            }
            BulletBallistics.Step(ref _state, _spec.Motion);
            if (_state.Phase == BulletPhase.Straight)
            {
                _straightEnd = _state.Position;
            }

            bool reachedEnd = false;
            if (_spec.MaxDistance > 0f && _state.Travelled >= _spec.MaxDistance)
            {
                // 狙擊:剛好停在 Distance(把這一步截短)
                float stepLength = _state.LastDisplacement.magnitude;
                float over = _state.Travelled - _spec.MaxDistance;
                if (stepLength > 1e-5f)
                {
                    _state.Position = Vector3.Lerp(axisFrom, _state.Position, Mathf.Clamp01((stepLength - over) / stepLength));
                }
                _state.Travelled = _spec.MaxDistance;
                reachedEnd = true;
            }

            Vector3 to = Position;
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            Vector3 direction = distance > 1e-6f ? delta / distance : transform.forward;

            bool terrainHit = CastTerrain(from, direction, distance, fieldRadius, out RaycastHit terrain);
            float limit = terrainHit ? terrain.distance : distance;
            // 單向屏障(Big Bubbler):從罩外打進敵方屏障就停在球面上;屏障後面(罩內)的目標打不到
            bool barrierHit = BarrierSphere.Raycast(_team, from, direction, limit, fieldRadius, out BarrierHit barrier);
            if (barrierHit)
            {
                limit = barrier.Distance;
            }
            // Ink Vac 吸引漩渦(敵方):這一步進入吸引範圍 → 被吸走;範圍後面的目標打不到
            InkVacuum vacuum = null;
            float vacuumDistance = 0f;
            bool vacuumHit = InkVacuum.Active.Count > 0
                             && InkVacuum.Segment(_team, from, direction, limit, fieldRadius, out vacuum,
                                 out vacuumDistance);
            if (vacuumHit)
            {
                limit = vacuumDistance;
            }

            if (ResolveTargets(from, direction, distance, playerRadius, limit))
            {
                return false;
            }
            if (vacuumHit)
            {
                OnVacuumed(vacuum, from + direction * vacuumDistance, direction);
                return false;
            }
            if (barrierHit)
            {
                OnBarrierHit(barrier);
                return false;
            }
            if (terrainHit)
            {
                OnTerrainHit(from, direction, terrain);
                return false;
            }

            ReleaseDueDrips(from, direction, distance);

            if (_spec.ExplodeAtFrame > 0 && _state.Frame >= _spec.ExplodeAtFrame)
            {
                Finish(InkBulletEnd.AirBurst, to, -direction, null, 0f, true, 1f, null);
                return false;
            }
            if (reachedEnd)
            {
                if (_spec.EndPaintRadius > 0f && !_spec.NoPaint)
                {
                    PaintBelow(to, _spec.EndPaintRadius, 20f);
                }
                Finish(InkBulletEnd.MaxDistance, to, -direction, null, 0f, _spec.ExplodeAtEnd, 1f, null);
                return false;
            }
            if (_state.Frame >= _spec.EffectiveLifetimeFrames || to.y < KillPlaneY)
            {
                Finish(InkBulletEnd.Expired, to, -direction, null, 0f, _spec.ExplodeAtEnd && to.y >= KillPlaneY, 1f,
                    null);
                return false;
            }
            return true;
        }

        private static bool CastTerrain(Vector3 from, Vector3 direction, float distance, float radius, out RaycastHit best)
        {
            best = default;
            int count = Physics.SphereCastNonAlloc(from, Mathf.Max(0.01f, radius), direction, TerrainHits,
                Mathf.Max(distance, 1e-4f), CombatMasks.Terrain, QueryTriggerInteraction.Ignore);
            float bestDistance = float.MaxValue;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = TerrainHits[i];
                if (hit.distance < bestDistance)
                {
                    best = hit;
                    bestDistance = hit.distance;
                    found = true;
                }
            }
            if (found && best.distance <= 0f && best.point == Vector3.zero)
            {
                // 起點就在地形裡(貼牆開槍):當作在起點命中
                best.point = from;
                best.normal = -direction;
            }
            return found;
        }

        // 目標判定:回傳 true = 本發停在某個目標上(已結束)
        private bool ResolveTargets(Vector3 from, Vector3 direction, float distance, float radius, float limit)
        {
            int count = Physics.SphereCastNonAlloc(from, Mathf.Max(0.01f, radius), direction, TargetHits,
                Mathf.Max(distance, 1e-4f), CombatMasks.Targets, QueryTriggerInteraction.Ignore);
            int candidates = 0;
            for (int i = 0; i < count && candidates < CastBufferSize; i++)
            {
                RaycastHit hit = TargetHits[i];
                if (hit.distance > limit + 1e-3f)
                {
                    continue;
                }
                var target = hit.collider.GetComponentInParent<IDamageable>();
                if (target == null || !target.IsAlive || !DamageRules.CanDamage(_team, target) || WasPierced(target)
                    || Contains(target, candidates))
                {
                    continue;
                }
                bool overlapAtStart = hit.distance <= 0f && hit.point == Vector3.zero;
                CandidateTargets[candidates] = target;
                CandidateDistances[candidates] = hit.distance;
                CandidatePoints[candidates] = overlapAtStart ? from : hit.point;
                candidates++;
            }
            if (candidates == 0)
            {
                return false;
            }
            SortCandidates(candidates);
            IDamageable stoppedBy = null;
            Vector3 stopPoint = Vector3.zero;
            float stopDamage = 0f;
            float stopTravelled = 0f;
            for (int i = 0; i < candidates; i++)
            {
                IDamageable target = CandidateTargets[i];
                Vector3 point = CandidatePoints[i];
                // 接觸瞬間的距離 = 本步終點距離 − (本步長 − 碰到時已走的長度)
                _contactTravelled = Mathf.Max(0f, _state.Travelled - Mathf.Max(0f, distance - CandidateDistances[i]));
                float damage = DealDamage(target, point);
                bool pierce = _spec.PiercePlayers && target is InklingHealth && _piercedCount < MaxPierced;
                if (pierce)
                {
                    _pierced[_piercedCount++] = target;
                    Report(InkBulletEnd.Pierce, point, -direction, target, damage, false);
                    SpawnSplashFx(point, -direction);
                    continue;
                }
                stoppedBy = target;
                stopPoint = point;
                stopDamage = damage;
                stopTravelled = _contactTravelled;
                break;
            }
            _contactTravelled = -1f;
            // 靜態緩衝不留參照
            for (int i = 0; i < candidates; i++)
            {
                CandidateTargets[i] = null;
            }
            if (stoppedBy == null)
            {
                return false;
            }
            _contactTravelled = stopTravelled;
            Finish(InkBulletEnd.Target, stopPoint, -direction, stoppedBy, stopDamage, _spec.ExplodeOnImpact, 1f,
                _spec.ExplosionHitsDirectTarget ? null : stoppedBy);
            _contactTravelled = -1f;
            return true;
        }

        private float DealDamage(IDamageable target, Vector3 point)
        {
            float damage = _spec.DamageAtFrame(_state.Frame);
            if (_damageFunc != null)
            {
                damage = VitalsModel.Floor01(_damageFunc(this, target, damage));
            }
            damage = CombatLedger.Register(_attackId, target, damage, _spec.PerAttackDamageCap);
            if (damage > 0f)
            {
                target.ApplyDamage(new DamageInfo(damage, point, _team, _owner, _spec.Kind, _spec.AttackClass));
            }
            return damage;
        }

        private void OnTerrainHit(Vector3 from, Vector3 direction, in RaycastHit hit)
        {
            Vector3 point = hit.point;
            Vector3 normal = hit.normal.sqrMagnitude > 1e-6f ? hit.normal : -direction;
            // 球心停在接觸位置(渲染最後一幀不要穿牆)
            _state.Position = from + direction * hit.distance;
            bool floor = normal.y >= PaintableSurface.FloorNormalY;
            if (!_spec.NoPaint)
            {
                if (floor)
                {
                    PaintFloorStamp(point, direction);
                }
                else
                {
                    InkPainter.PaintSphere(point, _spec.PaintRadiusShock, _spec.EffectiveHardness, Source());
                }
            }
            if (!_spec.Silent)
            {
                GameAudio.Play(floor ? _spec.EffectiveHitFloorSound : _spec.EffectiveHitWallSound, point,
                    _spec.EffectiveHitVolume, 0.08f);
            }
            float scale = _spec.TerrainHitExplosionScale > 0f ? _spec.TerrainHitExplosionScale : 1f;
            Finish(InkBulletEnd.Terrain, point + normal * 0.05f, normal, null, 0f, _spec.ExplodeOnImpact, scale, null,
                point);
        }

        // 打在敵方屏障(Big Bubbler 球罩)上:屏障吃這一發的傷害(物件倍率由屏障擁有者換算),墨彈停在球面;
        // 會爆炸的彈照樣在球面外爆(屏障本身不再吃這發的爆炸,罩內的目標被球面擋住視線)
        private void OnBarrierHit(in BarrierHit hit)
        {
            BarrierSphere barrier = hit.Barrier;
            float damage = DealDamage(barrier, hit.Point);
            Finish(InkBulletEnd.Target, hit.Point + hit.Normal * 0.05f, hit.Normal, barrier, damage, _spec.ExplodeOnImpact,
                1f, barrier, hit.Point);
        }

        // 被敵方 Ink Vac 吸走:吸入量 = 這一發的傷害(會爆炸的彈取直擊與近爆風的大者;同一次攻擊只算一次);
        // 不塗墨、不爆炸、不出命中特效,回報 Absorbed 後回池
        private void OnVacuumed(InkVacuum vacuum, Vector3 point, Vector3 direction)
        {
            _state.Position = point;
            float amount = _spec.DamageAtFrame(_state.Frame);
            if (_spec.ExplodeOnImpact || _spec.ExplodeAtFrame > 0 || _spec.ExplodeAtEnd)
            {
                amount = Mathf.Max(amount, Mathf.Max(_spec.Explosion.NearDamage, _spec.Explosion.FarDamage));
            }
            vacuum.Absorb(point, amount, _attackId);
            Report(InkBulletEnd.Absorbed, point, -direction, null, 0f, false);
            Release();
        }

        // 螺旋偏移(規格 OrbitRadius = 0 時為 0)
        private Vector3 OrbitOffset(int frame)
        {
            if (_spec.OrbitRadius <= 0f)
            {
                return Vector3.zero;
            }
            float radius = _spec.OrbitGrowFrames > 0
                ? _spec.OrbitRadius * Mathf.Clamp01(frame / (float)_spec.OrbitGrowFrames)
                : _spec.OrbitRadius;
            return TrizookaModel.OrbitOffset(_orbitRight, _orbitUp, OrbitPhaseDeg + _spec.OrbitDegPerFrame * frame, radius);
        }

        // 地面落點:半寬依落點離射手的水平距離(≤ 1.1 m 近 → 20 m 遠線性);沿飛行方向拉長 DepthScale
        //(入射角 < 10° 用 Max、> 35° 用 Min、中間線性;落差 > 10 m 用 BreakFree;已離開直線狀態的彈縮一半差距 [EST])。
        // 橢圓以 1~3 顆半徑 = 半寬的球沿飛行方向排開近似(外側兩顆的外緣剛好到半長軸 = 半寬 × DepthScale)。
        private void PaintFloorStamp(Vector3 point, Vector3 direction)
        {
            Vector3 flatFromOrigin = point - _origin;
            flatFromOrigin.y = 0f;
            // 近 1.1 m → 遠 PaintDistanceFar(預設 20 m);滾筒另有隨飛行幀數縮小(見 InkBulletSpec.FloorHalfWidthAt)
            float halfWidth = _spec.FloorHalfWidthAt(flatFromOrigin.magnitude, _state.Frame);
            if (halfWidth <= 0f)
            {
                return;
            }
            float depthMax = _spec.PaintDepthScaleMax > 0f ? _spec.PaintDepthScaleMax : 2.4f;
            float depthMin = _spec.PaintDepthScaleMin > 0f ? _spec.PaintDepthScaleMin : 1.4f;
            float incidence = Mathf.Asin(Mathf.Clamp(-direction.y, -1f, 1f)) * Mathf.Rad2Deg;
            float depth = Mathf.Lerp(depthMax, depthMin, Mathf.InverseLerp(10f, 35f, incidence));
            if (_origin.y - point.y > 10f && _spec.PaintDepthScaleBreakFree > 0f)
            {
                depth = _spec.PaintDepthScaleBreakFree;
            }
            else if (_state.Phase != BulletPhase.Straight)
            {
                depth = Mathf.Min(depth, (depth + depthMin) * 0.5f);
            }
            InkSource source = Source();
            float hardness = _spec.EffectiveHardness;
            Vector3 along = new Vector3(direction.x, 0f, direction.z);
            float offset = halfWidth * (depth - 1f);
            if (along.sqrMagnitude < 1e-6f || offset <= 0.05f * halfWidth)
            {
                InkPainter.PaintSphere(point, halfWidth, hardness, source);
                return;
            }
            along.Normalize();
            InkPainter.PaintSphere(point + along * offset, halfWidth, hardness, source);
            InkPainter.PaintSphere(point - along * offset, halfWidth, hardness, source);
            if (depth > 1.6f)
            {
                InkPainter.PaintSphere(point, halfWidth, hardness, source);
            }
        }

        private void ReleaseDueDrips(Vector3 from, Vector3 direction, float distance)
        {
            if (_spec.DripSpacing <= 0f || _spec.NoPaint)
            {
                return;
            }
            float stepStart = _state.Travelled - distance;
            while (_state.Travelled >= _nextDripAt && (_spec.DripMaxCount <= 0 || _drips < _spec.DripMaxCount))
            {
                float along = Mathf.Clamp(_nextDripAt - stepStart, 0f, distance);
                InkBulletDrip.Drop(from + direction * along, _spec.DripRadius, _spec.EffectiveHardness, Source(), _team, 30f);
                _drips++;
                _nextDripAt += _spec.DripSpacing;
            }
        }

        private void PaintBelow(Vector3 position, float radius, float reach)
        {
            if (radius <= 0f)
            {
                return;
            }
            if (Physics.Raycast(position, Vector3.down, out RaycastHit hit, reach, CombatMasks.Terrain,
                    QueryTriggerInteraction.Ignore))
            {
                InkPainter.PaintSphere(hit.point, radius, _spec.EffectiveHardness, Source());
            }
        }

        private InkSource Source()
        {
            bool charges = !_spec.NoSpecialCharge;
            return _owner != null ? _owner.InkSource(charges) : new InkSource(_team, null, charges);
        }

        private void SpawnSplashFx(Vector3 point, Vector3 normal)
        {
            if (InkSplashFxPool.Instance != null)
            {
                InkSplashFxPool.Instance.Spawn(point, normal, TeamPalette.ColorOf(_team));
            }
        }

        // 結束:特效、可選爆炸、事件、回池
        private void Finish(InkBulletEnd end, Vector3 point, Vector3 normal, IDamageable target, float damage,
            bool explode, float explosionScale, IDamageable exclude, Vector3? splashPoint = null)
        {
            if (end != InkBulletEnd.Expired)
            {
                SpawnSplashFx(splashPoint ?? point, normal);
            }
            if (explode)
            {
                Explosion.Detonate(_spec.Explosion, point, _team, _owner, _attackId, explosionScale, exclude);
            }
            Report(end, splashPoint ?? point, normal, target, damage, explode);
            Release();
        }

        private void Report(InkBulletEnd end, Vector3 point, Vector3 normal, IDamageable target, float damage,
            bool exploded)
        {
            var impact = new InkBulletImpact
            {
                Bullet = this,
                End = end,
                Point = point,
                Normal = normal,
                Frame = _state.Frame,
                Travelled = _state.Travelled,
                ContactTravelled = ContactTravelled,
                Phase = _state.Phase,
                Target = target,
                Damage = damage,
                Exploded = exploded,
                Team = _team,
                AttackId = _attackId,
            };
            ImpactCount++;
            LastImpact = impact;
            Impacted?.Invoke(impact);
        }

        private void Release()
        {
            if (!_active)
            {
                return;
            }
            _active = false;
            ActiveCount = Mathf.Max(0, ActiveCount - 1);
            _owner = null;
            _damageFunc = null;
            for (int i = 0; i < _piercedCount; i++)
            {
                _pierced[i] = null;
            }
            _piercedCount = 0;
            SharedPool.Despawn(this);
        }

        private bool WasPierced(IDamageable target)
        {
            for (int i = 0; i < _piercedCount; i++)
            {
                if (ReferenceEquals(_pierced[i], target))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool Contains(IDamageable target, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (ReferenceEquals(CandidateTargets[i], target))
                {
                    return true;
                }
            }
            return false;
        }

        // 依距離插入排序(候選很少)
        private static void SortCandidates(int count)
        {
            for (int i = 1; i < count; i++)
            {
                IDamageable target = CandidateTargets[i];
                float distance = CandidateDistances[i];
                Vector3 point = CandidatePoints[i];
                int j = i - 1;
                while (j >= 0 && CandidateDistances[j] > distance)
                {
                    CandidateTargets[j + 1] = CandidateTargets[j];
                    CandidateDistances[j + 1] = CandidateDistances[j];
                    CandidatePoints[j + 1] = CandidatePoints[j];
                    j--;
                }
                CandidateTargets[j + 1] = target;
                CandidateDistances[j + 1] = distance;
                CandidatePoints[j + 1] = point;
            }
        }
    }
}

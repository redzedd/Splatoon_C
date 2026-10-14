using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // Torpedo 的狀態
    public enum TorpedoState : byte
    {
        // 投出後的拋物線(搜尋中;碰過地形後不再搜尋)
        Flying = 0,
        // 找到目標:停在空中變身(轉向 25F + 20F),HP 20 可被打壞
        Transforming = 1,
        // 朝目標當時的位置直線衝刺
        Chasing = 2,
        // 先碰到地形:像炸彈一樣滾,落地 30F 後爆
        Rolling = 3,
        // 追蹤爆炸後:8 顆散彈飛行中(本體已隱藏)
        Droplets = 4,
    }

    // 池化 Torpedo(S3 Bomb_Torpedo;參數見 TorpedoConfig / TorpedoModel)。以 60 Hz 固定步模擬(SimulatedBomb):
    //   Flying:S3 通用彈道(與預覽同一個 S3ThrownFlight),穿過敵人;第 6F 起每幀在水平 9.0、上下窗內找最近的敵人
    //           (角色與試射場假人,含潛伏者;不需視線)→ Transforming。碰到牆:反彈 0.7、失去搜尋;碰到地面 → Rolling。
    //           碰到敵方屏障/球罩 → 就地爆(ThrowBlast)。一直沒碰到東西 → 150F 空中自爆 [EST]。
    //   Transforming:停住、開啟受擊碰撞體(Target 圖層)並登記 DamageableRegistry(HP 20,DamageRates 欄 Torpedo:
    //           炸彈 ×0.6、Curling 接觸 / Sprinkler ×0.5、Brella 傘面 ×5…);變身中的 Torpedo 也是 IBombDetonator
    //           (敵方炸彈碰到立刻爆)。正下方塗一小片(6 滴 r 0.7),目標腳下顯示虛線瞄準圈。
    //   Chasing:方向 = 變身結束時目標的位置(不再追蹤),碰到地形/敵人/屏障 → ChaseBlast + 8 顆散彈(碰地形各 12 / 2.6、塗 2.0)。
    //   被打壞(HP ≤ 0):不爆不塗,碎裂特效 + 音效。
    //   爆炸後本體把 Owner 清掉(ThrowController 的「同時 1 顆」就此解除),散彈用保留的投擲者結算。
    public sealed class Torpedo : SimulatedBomb, IDamageable, IBombDetonator
    {
        private const int MaxDroplets = 12;
        private const float RingFadeSeconds = 0.15f;

        [SerializeField, Tooltip("變身後的受擊碰撞體(Target 圖層,平時關閉)")]
        private SphereCollider _hull;

        [SerializeField, Tooltip("尾翼根(變身時展開)")]
        private Transform _fins;

        [SerializeField, Tooltip("瞄準/落點圈(虛線環,外半徑 1,放在根物件下)")]
        private Renderer _reticle;

        [SerializeField, Tooltip("散彈外觀(小墨球,放在根物件下,平時關閉)")]
        private Renderer[] _droplets = new Renderer[0];

        private TorpedoConfig _config;
        private TorpedoParams _p;
        private TorpedoState _state;
        private S3ThrownFlight _flight;
        private BombRoll _roll;
        private bool _searchLost;
        private int _stateFrame;
        private int _burstAt = -1;
        private IDamageable _target;
        private Transform _targetTransform;
        private Vector3 _aimPoint;
        private Vector3 _chaseDirection;
        private Vector3 _impactPoint;
        private bool _impactFound;
        private Quaternion _transformFrom;
        private float _hp;
        private bool _hullActive;
        private bool _registered;
        private bool _broken;
        private float _hitPulseUntil;
        private float _spin;
        private Inkling _attacker;
        private readonly Vector3[] _dropPosition = new Vector3[MaxDroplets];
        private readonly Vector3[] _dropPrevious = new Vector3[MaxDroplets];
        private readonly Vector3[] _dropVelocity = new Vector3[MaxDroplets];
        private readonly bool[] _dropAlive = new bool[MaxDroplets];
        private int _dropCount;
        private int _dropsAlive;
        private int _dropFrame;
        private Color _appliedRing = Color.clear;

        // 測試/除錯:累計數與最近一次的時間點/位置
        public static int LaunchCount { get; private set; }
        public static int TransformCount { get; private set; }
        public static int ChaseExplosionCount { get; private set; }
        public static int ThrowExplosionCount { get; private set; }
        public static int DropletBlastCount { get; private set; }
        public static int BrokenCount { get; private set; }
        public static float LastLaunchTime { get; private set; }
        public static float LastTransformTime { get; private set; }
        public static float LastChaseStartTime { get; private set; }
        public static float LastLandTime { get; private set; }
        public static float LastExplosionTime { get; private set; }
        public static int LastTransformFrame { get; private set; }
        public static int LastChaseStartFrame { get; private set; }
        public static int LastLandFrame { get; private set; }
        public static int LastExplosionFrame { get; private set; }
        public static Vector3 LastTransformPosition { get; private set; }
        public static Vector3 LastExplosionCenter { get; private set; }
        public static Vector3 LastLandPoint { get; private set; }
        public static IDamageable LastTarget { get; private set; }
        public static ExplosionResult LastBlastResult { get; private set; }
        // 最近一顆爆炸時的爆炸種類(true = 追蹤命中 ChaseBlast)
        public static bool LastWasChase { get; private set; }

        public TorpedoState State => _state;
        public float Hp => _hp;
        public bool IsTransformed => _hullActive && !_broken;
        public IDamageable Target => _target;
        public Vector3 AimPoint => _aimPoint;
        public bool SearchLost => _searchLost;
        public int LaunchFrame => Frame;

        // ── IDamageable(變身後;Team 由 SimulatedBomb 提供)──
        public bool IsAlive => _hullActive && !_broken && IsActive;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetCounters()
        {
            LaunchCount = 0;
            TransformCount = 0;
            ChaseExplosionCount = 0;
            ThrowExplosionCount = 0;
            DropletBlastCount = 0;
            BrokenCount = 0;
            LastTarget = null;
            LastLaunchTime = float.NegativeInfinity;
            LastTransformTime = float.NegativeInfinity;
            LastChaseStartTime = float.NegativeInfinity;
            LastLandTime = float.NegativeInfinity;
            LastExplosionTime = float.NegativeInfinity;
        }

        protected override void Awake()
        {
            base.Awake();
            SetHull(false);
            if (_reticle != null)
            {
                _reticle.enabled = false;
            }
            HideDroplets();
        }

        public override void Launch(Vector3 position, Vector3 velocity, SubWeaponConfig config, Inkling owner)
        {
            _config = config as TorpedoConfig;
            if (_config == null)
            {
                Debug.LogError($"Torpedo:設定型別不符({config})", this);
                Despawn();
                return;
            }
            Begin(position, owner);
            _p = _config.S3;
            _attacker = owner;
            _state = TorpedoState.Flying;
            _flight.Init(position, velocity * SimFrames.Dt);
            _searchLost = false;
            _stateFrame = 0;
            _burstAt = -1;
            _target = null;
            _targetTransform = null;
            _impactFound = false;
            _hp = _p.Hp;
            _broken = false;
            _hitPulseUntil = 0f;
            _spin = Random.Range(0f, 360f);
            _dropCount = 0;
            _dropsAlive = 0;
            SetHull(false);
            HideDroplets();
            Model.gameObject.SetActive(true);
            if (_fins != null)
            {
                _fins.localScale = new Vector3(0.35f, 0.35f, 1f);
            }
            if (_reticle != null)
            {
                _reticle.enabled = false;
            }
            SetLightColor(new Color(1f, 0.85f, 0.25f));
            LaunchCount++;
            LastLaunchTime = Time.time;
            OrientAlong(velocity);
            PaintUnderThrower(owner);
        }

        // S3:投げた時の足元塗り(他のサブにない特徴)
        private void PaintUnderThrower(Inkling owner)
        {
            if (owner == null || _config.ThrowPaintRadius <= 0f)
            {
                return;
            }
            Vector3 origin = owner.transform.position + Vector3.up * 0.5f;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit ground, 2f, CombatMasks.Terrain,
                    QueryTriggerInteraction.Ignore))
            {
                InkPainter.PaintSphere(ground.point, _config.ThrowPaintRadius, _config.PaintHardness, owner.InkSource());
            }
        }

        protected override bool Step()
        {
            if (_broken)
            {
                Release();
                return false;
            }
            switch (_state)
            {
                case TorpedoState.Flying:
                    return StepFlying();
                case TorpedoState.Transforming:
                    return StepTransforming();
                case TorpedoState.Chasing:
                    return StepChasing();
                case TorpedoState.Rolling:
                    return StepRolling();
                default:
                    return StepDroplets();
            }
        }

        // ── 投擲飛行與搜尋 ──

        private bool StepFlying()
        {
            if (_burstAt > 0 && Frame >= _burstAt)
            {
                ExplodeThrow(SimPosition);
                return false;
            }
            if (_burstAt < 0 && Frame >= _p.AirborneTimeoutFrames)
            {
                ExplodeThrow(SimPosition);
                return false;
            }
            if (!_searchLost && TorpedoModel.CanSearch(_p, Frame) && FindTarget())
            {
                BeginTransform();
                return true;
            }
            BombContact contact = _flight.Step(_p.Throw, _config.Radius, BombTeam, false, out RaycastHit hit, out _);
            SimPosition = _flight.Position;
            if (contact == BombContact.None)
            {
                return true;
            }
            if (contact == BombContact.Detonator)
            {
                // 碰到敵方屏障/球罩:在屏障這一側就地爆(爆心 = 接觸時的球心)
                ExplodeThrow(SimPosition);
                return false;
            }
            // 碰到地形:S3「壁や地面に当てるとサーチ性能が失われる」
            _searchLost = true;
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
            // 撞牆反彈(S3 HitVerticalWallReboundMaxRate 0.7)
            _flight.Velocity = SimpleBallistics.Reflect(_flight.Velocity, normal, _p.WallRebound, 1f);
            GameAudio.Play("bomb_bounce", hit.point, 0.6f);
            return true;
        }

        private void Land(Vector3 point, Vector3 normal)
        {
            _roll.Land(point, normal, _flight.Velocity, _config.Radius, _p.LandingKeep);
            SimPosition = _roll.Position;
            _state = TorpedoState.Rolling;
            if (_burstAt < 0)
            {
                // S3 BurstFrame 30:第一次著地起算(之後滾出邊緣落下也照算)
                _burstAt = Frame + _p.BurstFrame;
                LastLandFrame = Frame;
                LastLandTime = Time.time;
                LastLandPoint = point;
                if (Frame + _p.WarningRestFrame >= _burstAt)
                {
                    GameAudio.Play(_config.RollWarningSound, point, 0.85f, 0f);
                }
            }
            GameAudio.Play("bomb_bounce", point, 0.5f);
        }

        private bool StepRolling()
        {
            if (Frame >= _burstAt)
            {
                ExplodeThrow(SimPosition);
                return false;
            }
            bool grounded = _roll.Step(_p.GroundResist, _p.WallRebound, _config.Radius, _config.StepHeight);
            SimPosition = _roll.Position;
            if (BombRules.TouchDetonator(PrevPosition, SimPosition, _config.Radius, BombTeam, out Vector3 touch))
            {
                SimPosition = touch;
                ExplodeThrow(touch);
                return false;
            }
            if (!grounded)
            {
                // 滾出邊緣:回到空中(不再搜尋),引信照算
                _state = TorpedoState.Flying;
                _flight.Init(SimPosition, _roll.AirVelocity);
            }
            return true;
        }

        // 水平 9.0、上下窗內最近的敵人(角色與試射場假人;場上裝置不算),量到目標碰撞體最近點
        private bool FindTarget()
        {
            var active = DamageableRegistry.Active;
            float bestSq = float.MaxValue;
            IDamageable best = null;
            Transform bestTransform = null;
            for (int i = 0; i < active.Count; i++)
            {
                IDamageable candidate = active[i];
                Component component = candidate as InklingHealth;
                if (component == null)
                {
                    component = candidate as TargetDummy;
                }
                if (component == null || !candidate.IsAlive || !DamageRules.CanDamage(BombTeam, candidate))
                {
                    continue;
                }
                Vector3 offset = candidate.ClosestPoint(SimPosition) - SimPosition;
                if (!TorpedoModel.InSearchWindow(_p, offset))
                {
                    continue;
                }
                float sq = offset.sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = candidate;
                    bestTransform = component.transform;
                }
            }
            _target = best;
            _targetTransform = bestTransform;
            return best != null;
        }

        // ── 變身 ──

        private void BeginTransform()
        {
            _state = TorpedoState.Transforming;
            _stateFrame = Frame;
            _flight.Velocity = Vector3.zero;
            _hp = _p.Hp;
            _transformFrom = transform.rotation;
            UpdateAimPoint();
            SetHull(true);
            TransformCount++;
            LastTransformTime = Time.time;
            LastTransformFrame = Frame;
            LastTransformPosition = SimPosition;
            LastTarget = _target;
            GameAudio.Play(_config.LockSound, SimPosition, 0.9f, 0f);
            GameAudio.Play(_config.TransformSound, SimPosition, 0.8f, 0.03f);
            PaintBelow();
        }

        // S3 FindSpawnSplashAroundParam:變身時正下方散 6 滴 r 0.7
        private void PaintBelow()
        {
            if (!Physics.Raycast(SimPosition, Vector3.down, out RaycastHit ground, 20f, CombatMasks.Terrain,
                    QueryTriggerInteraction.Ignore))
            {
                return;
            }
            InkSource source = _attacker != null ? _attacker.InkSource() : new InkSource(BombTeam);
            int count = Mathf.Max(0, _config.TransformDropCount);
            float offset = Random.Range(0f, 360f);
            for (int i = 0; i < count; i++)
            {
                float angle = (offset + 360f * i / Mathf.Max(1, count)) * Mathf.Deg2Rad;
                float reach = _config.TransformDropReach * (0.4f + 0.6f * Random.value);
                Vector3 probe = ground.point + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * reach
                                + Vector3.up * 1.5f;
                if (Physics.Raycast(probe, Vector3.down, out RaycastHit hit, 4f, CombatMasks.Terrain,
                        QueryTriggerInteraction.Ignore))
                {
                    InkPainter.PaintSphere(hit.point, _config.TransformDropRadius, _config.PaintHardness, source);
                }
            }
        }

        private bool StepTransforming()
        {
            UpdateAimPoint();
            if (Frame - _stateFrame >= TorpedoModel.TransformFrames(_p))
            {
                BeginChase();
            }
            return true;
        }

        // 目標還活著就更新瞄準點(腰部高度);死了/消失就瞄最後位置
        private void UpdateAimPoint()
        {
            if (_target != null && _targetTransform != null && _target.IsAlive)
            {
                _aimPoint = _targetTransform.position + Vector3.up * 0.6f;
            }
        }

        // ── 衝刺 ──

        private void BeginChase()
        {
            _state = TorpedoState.Chasing;
            _stateFrame = Frame;
            Vector3 toAim = _aimPoint - SimPosition;
            _chaseDirection = toAim.sqrMagnitude > 1e-6f ? toAim.normalized : Vector3.down;
            _impactFound = Physics.SphereCast(SimPosition, _config.Radius, _chaseDirection, out RaycastHit impact,
                80f, CombatMasks.Terrain, QueryTriggerInteraction.Ignore);
            _impactPoint = _impactFound ? impact.point : _aimPoint;
            LastChaseStartFrame = Frame;
            LastChaseStartTime = Time.time;
            GameAudio.Play(_config.DashSound, SimPosition, 0.9f, 0.03f);
        }

        private bool StepChasing()
        {
            Vector3 from = SimPosition;
            Vector3 to = from + _chaseDirection * _p.ChaseSpeed;
            BombContact contact = BombRules.CastPath(from, to, _config.Radius, BombTeam, true, out RaycastHit hit,
                out _);
            if (contact != BombContact.None)
            {
                SimPosition = from + _chaseDirection * hit.distance;
                Vector3 center = contact == BombContact.Terrain ? hit.point + hit.normal * 0.1f : hit.point;
                ExplodeChase(center);
                return true;
            }
            SimPosition = to;
            if (Frame - _stateFrame >= _p.ChaseMaxFrames)
            {
                ExplodeChase(SimPosition);
            }
            return true;
        }

        // ── 爆炸 ──

        // 追蹤命中:60 / 35 + 8 顆散彈
        private void ExplodeChase(Vector3 center)
        {
            LastWasChase = true;
            ChaseExplosionCount++;
            RecordExplosion(center);
            LastBlastResult = Explosion.Detonate(_config.ChaseBlast, center, BombTeam, _attacker);
            StartDroplets(center);
        }

        // 落地滾動 / 空中逾時 / 碰到屏障:60 / 35,沒有散彈
        private void ExplodeThrow(Vector3 center)
        {
            LastWasChase = false;
            ThrowExplosionCount++;
            RecordExplosion(center);
            LastBlastResult = Explosion.Detonate(_config.ThrowBlast, center, BombTeam, _attacker);
            Release();
        }

        private void RecordExplosion(Vector3 center)
        {
            LastExplosionTime = Time.time;
            LastExplosionFrame = Frame;
            LastExplosionCenter = center;
            SetHull(false);
            // 本體已經爆了:不再佔用 ThrowController 的「同時 1 顆」(散彈用 _attacker 結算)
            Owner = null;
        }

        private void StartDroplets(Vector3 center)
        {
            _state = TorpedoState.Droplets;
            Model.gameObject.SetActive(false);
            if (_reticle != null)
            {
                _reticle.enabled = false;
            }
            _dropCount = Mathf.Clamp(_p.DropletCount, 0, MaxDroplets);
            _dropsAlive = _dropCount;
            _dropFrame = 0;
            float yaw = Random.Range(0f, 360f);
            Vector3 start = center + Vector3.up * 0.15f;
            Color ink = TeamPalette.ColorOf(BombTeam);
            for (int i = 0; i < _dropCount; i++)
            {
                _dropPosition[i] = start;
                _dropPrevious[i] = start;
                _dropVelocity[i] = TorpedoModel.DropletVelocity(_p, i, yaw, Random.value);
                _dropAlive[i] = true;
                if (i < _droplets.Length && _droplets[i] != null)
                {
                    _droplets[i].enabled = true;
                    _droplets[i].transform.position = start;
                    TeamTint.Apply(_droplets[i], ink);
                }
            }
            if (_dropCount == 0)
            {
                Release();
            }
        }

        private bool StepDroplets()
        {
            _dropFrame++;
            for (int i = 0; i < _dropCount; i++)
            {
                if (!_dropAlive[i])
                {
                    continue;
                }
                Vector3 from = _dropPosition[i];
                Vector3 to = from;
                Vector3 velocity = _dropVelocity[i];
                S3ThrowModel.StepAir(_p.Throw, ref to, ref velocity);
                _dropPrevious[i] = from;
                Vector3 delta = to - from;
                float length = delta.magnitude;
                // 散彈只碰地形(穿過角色與金網之外的東西),碰到就小爆炸
                if (length > 1e-6f && Physics.SphereCast(from, _config.DropletRadius, delta / length, out RaycastHit hit,
                        length, CombatMasks.Terrain, QueryTriggerInteraction.Ignore))
                {
                    Vector3 center = hit.point + hit.normal * 0.1f;
                    DropletBlastCount++;
                    Explosion.Detonate(_config.DropletBlast, center, BombTeam, _attacker);
                    KillDroplet(i);
                    continue;
                }
                _dropPosition[i] = to;
                _dropVelocity[i] = velocity;
                if (to.y < KillPlaneY || _dropFrame >= _p.DropletMaxFrames)
                {
                    KillDroplet(i);
                }
            }
            if (_dropsAlive <= 0)
            {
                Release();
                return false;
            }
            return true;
        }

        private void KillDroplet(int index)
        {
            _dropAlive[index] = false;
            _dropsAlive--;
            if (index < _droplets.Length && _droplets[index] != null)
            {
                _droplets[index].enabled = false;
            }
        }

        // ── 受擊(變身後)──

        public Vector3 ClosestPoint(Vector3 from)
        {
            Vector3 center = SimPosition;
            float radius = _config != null ? _config.HullRadius : 0.35f;
            Vector3 offset = from - center;
            float distance = offset.magnitude;
            return distance <= radius || distance < 1e-6f ? from : center + offset / distance * radius;
        }

        public void ApplyDamage(in DamageInfo info)
        {
            if (!IsAlive || info.Amount <= 0f || (info.Team != Team.None && info.Team == BombTeam))
            {
                return;
            }
            float dealt = DamageRates.Apply(info.Amount, info.AttackClass, TargetClass.Torpedo);
            if (dealt <= 0f)
            {
                return;
            }
            dealt = Mathf.Min(dealt, _hp);
            _hp = VitalsModel.Floor01(_hp - dealt);
            _hitPulseUntil = Time.time + 0.1f;
            // 命中回饋(準星命中標記、命中音效);打壞物件不算擊倒
            info.Attacker?.OnDamageDealt(this, dealt, false);
            if (_hp <= 0f)
            {
                Break();
            }
        }

        // 被打壞:不爆炸、不塗墨(S3「HP20 … 爆発も塗りも発生しない」);回池延到下一步(不在別人迭代註冊表時移除)
        private void Break()
        {
            if (_broken)
            {
                return;
            }
            _broken = true;
            BrokenCount++;
            Vector3 center = SimPosition;
            if (InkSplashFxPool.Instance != null)
            {
                Color color = TeamPalette.ColorOf(BombTeam);
                for (int i = 0; i < 4; i++)
                {
                    InkSplashFxPool.Instance.Spawn(center + Random.insideUnitSphere * 0.3f, Vector3.up, color);
                }
            }
            GameAudio.Play(_config.DestroyedSound, center, 1f, 0.05f);
            Owner = null;
        }

        // S3:變身中的 Torpedo 讓敵方炸彈立刻爆(同 Splash Wall)
        public bool DetonatesBombsFrom(Team bombTeam)
        {
            return IsAlive && bombTeam != BombTeam;
        }

        private void SetHull(bool on)
        {
            _hullActive = on;
            if (_hull != null)
            {
                _hull.enabled = on;
                if (on && _config != null)
                {
                    _hull.radius = _config.HullRadius / Mathf.Max(1e-4f, Mathf.Abs(_hull.transform.lossyScale.x));
                }
            }
            if (on && !_registered)
            {
                DamageableRegistry.Active.Add(this);
                _registered = true;
            }
            else if (!on && _registered)
            {
                DamageableRegistry.Active.Remove(this);
                _registered = false;
            }
            if (on)
            {
                // 剛開啟的碰撞體要到下一個物理步才進射線查詢(autoSyncTransforms 關閉):這一幀就讓子彈打得到
                Physics.SyncTransforms();
            }
        }

        protected override void OnReleased()
        {
            SetHull(false);
            HideDroplets();
            if (_reticle != null)
            {
                _reticle.enabled = false;
            }
            Model.gameObject.SetActive(true);
            _target = null;
            _targetTransform = null;
            _attacker = null;
            _config = null;
            _broken = false;
        }

        private void OnDisable()
        {
            // 場景卸載/物件被停用:確保不留在註冊表
            if (_registered)
            {
                DamageableRegistry.Active.Remove(this);
                _registered = false;
            }
            _hullActive = false;
        }

        private void HideDroplets()
        {
            for (int i = 0; i < _droplets.Length; i++)
            {
                if (_droplets[i] != null)
                {
                    _droplets[i].enabled = false;
                }
            }
        }

        // ── 外觀 ──

        protected override void UpdateVisual()
        {
            switch (_state)
            {
                case TorpedoState.Flying:
                    OrientAlong(_flight.Velocity);
                    SetLights(Blink(Time.time, 0.5f));
                    break;
                case TorpedoState.Transforming:
                {
                    float t = Mathf.Clamp01((Frame - _stateFrame) / (float)Mathf.Max(1, _p.RotateFrames));
                    Vector3 look = _aimPoint - SimPosition;
                    Quaternion goal = look.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(look.normalized) : _transformFrom;
                    transform.rotation = Quaternion.Slerp(_transformFrom, goal, t * t * (3f - 2f * t));
                    // 懸停微微上下
                    transform.position += Vector3.up * (Mathf.Sin(Time.time * 18f) * 0.02f);
                    if (_fins != null)
                    {
                        float open = Mathf.Lerp(0.35f, 1f, t);
                        _fins.localScale = new Vector3(open, open, 1f);
                    }
                    SetLights(Blink(Time.time, 0.12f));
                    ShowReticle(_targetTransform != null ? _targetTransform.position : _aimPoint - Vector3.up * 0.6f,
                        true);
                    break;
                }
                case TorpedoState.Chasing:
                    if (_chaseDirection.sqrMagnitude > 1e-6f)
                    {
                        transform.rotation = Quaternion.LookRotation(_chaseDirection);
                    }
                    SetLights(true);
                    ShowReticle(_impactPoint, false);
                    break;
                case TorpedoState.Rolling:
                {
                    _spin += _roll.Speed * SimFrames.Rate * 120f * Time.deltaTime;
                    Vector3 heading = _roll.Heading.sqrMagnitude > 1e-6f ? _roll.Heading : Vector3.forward;
                    // 沿前進方向打轉(魚雷橫躺著滾)
                    transform.rotation = Quaternion.AngleAxis(_spin, heading) * Quaternion.LookRotation(heading);
                    SetLights(Blink(Time.time, 0.1f));
                    break;
                }
                default:
                    for (int i = 0; i < _dropCount && i < _droplets.Length; i++)
                    {
                        if (_dropAlive[i] && _droplets[i] != null)
                        {
                            _droplets[i].transform.position = _dropPosition[i];
                        }
                    }
                    break;
            }
            SetSwell(Time.time < _hitPulseUntil ? 1.18f : 1f);
            if (_state != TorpedoState.Transforming && _state != TorpedoState.Chasing && _reticle != null
                && _reticle.enabled)
            {
                _reticle.enabled = false;
            }
        }

        // 瞄準圈:變身中貼在目標腳下(慢轉),衝刺中貼在預定落點
        private void ShowReticle(Vector3 point, bool spin)
        {
            if (_reticle == null)
            {
                return;
            }
            Vector3 origin = point + Vector3.up * 0.6f;
            Vector3 position = point;
            Vector3 normal = Vector3.up;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit ground, 3f, CombatMasks.Terrain,
                    QueryTriggerInteraction.Ignore))
            {
                position = ground.point;
                normal = ground.normal;
            }
            Transform ring = _reticle.transform;
            ring.position = position + normal * 0.05f;
            float yaw = spin ? Time.time * 90f : 0f;
            ring.rotation = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0f, yaw, 0f);
            ring.localScale = spin ? Vector3.one * 0.9f : Vector3.one * 0.7f;
            if (!_reticle.enabled)
            {
                _reticle.enabled = true;
            }
            Color color = TeamPalette.ColorOf(BombTeam);
            color.a = spin ? 0.85f : 0.6f;
            TeamTint.ApplyCached(ReticleArray, ref _appliedRing, color);
        }

        private Renderer[] _reticleArray;

        private Renderer[] ReticleArray => _reticleArray ?? (_reticleArray = new[] { _reticle });

        private void OrientAlong(Vector3 velocity)
        {
            if (velocity.sqrMagnitude > 1e-8f)
            {
                _spin += 240f * Time.deltaTime;
                transform.rotation = Quaternion.LookRotation(velocity.normalized) * Quaternion.Euler(0f, 0f, _spin);
            }
        }
    }
}

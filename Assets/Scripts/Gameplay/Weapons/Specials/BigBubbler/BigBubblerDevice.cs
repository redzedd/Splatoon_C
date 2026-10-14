using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // Big Bubbler 的場上裝置(research/04 §2.2、JP wiki グレートバリア):
    //   地面發射座(Launcher)+ 頂端無人機(Drone)= 弱點(DeployableObject 本體,TargetClass.BubblerWeakPoint,×1);
    //   球形屏障(BarrierSphere,沒有碰撞體)擋罩外敵方攻擊,受擊 × 0.64 × DamageRates(攻擊, BubblerShield),與弱點共用耐久 1536。
    //   時間軸(設置起):第 15F 無人機點火、30F 內沿 AscendCurve 升到頂端,屏障跟著撐開;第 50F(前隙結束)才開始擋。
    //   耐久每秒自然扣 92.1(≈ 1001F 消失);屏障半徑 = 2.255 + (7.5 − 2.255) × RadiusRatioCurve(耐久比例),無人機跟著降。
    //   低天花板:無人機升不到 8.5 → 最大半徑跟著縮小(JP wiki)。耐久歸零 → 破掉(特效 + 音效)、擁有者的能量鎖定立刻歸 0。
    //   擁有者被擊倒不影響(S3 鎖定型)。受擊外觀:弱點閃白;屏障被打時球面變亮一下並在命中點濺墨。
    // 子物件命名:Ink* = 隊伍色(基底處理)、Glass* = 半透明屏障(這裡重新上色)、其他 = 材質原色(清掉基底的隊伍色)。
    public sealed class BigBubblerDevice : DeployableObject
    {
        private const float LauncherTop = 1.2f;
        private const float GlassAlpha = 0.2f;
        private const float GlassHitAlpha = 0.42f;
        private const float RingAlpha = 0.55f;

        [SerializeField, Tooltip("地面發射座的碰撞體(弱點)")]
        private Collider _launcherCollider;

        [SerializeField, Tooltip("無人機(隨屏障高度移動;底下的碰撞體是弱點)")]
        private Transform _drone;

        [SerializeField, Tooltip("無人機的碰撞體(弱點)")]
        private Collider _droneCollider;

        [SerializeField, Tooltip("無人機螺旋槳(持續旋轉)")]
        private Transform _propeller;

        [SerializeField, Tooltip("屏障外觀根(直徑 1 的球;縮放 = 直徑)")]
        private Transform _barrier;

        private BigBubblerConfig _config;
        private BarrierSphere _surface;
        private PlayerSpecialGauge _gauge;
        private int _lockToken;
        private Renderer[] _allRenderers;
        private Renderer[] _glassRenderers;
        private Renderer[] _neutralRenderers;
        private float _placedAt;
        private float _maxRadius;
        private float _radius;
        private float _droneHeight;
        private bool _barrierHit;
        private bool _flashState;
        private float _glassFlashUntil;
        private bool _glassFlashApplied;
        private bool _popped;
        private float _nextHitSoundAt;

        public override float MaxHp => _config != null ? _config.MaxHp : 1536f;

        // 弱點(發射座、無人機)與屏障面共用耐久:屏障被打時暫時切成屏障欄查倍率
        public override TargetClass TargetClass => _barrierHit ? TargetClass.BubblerShield : TargetClass.BubblerWeakPoint;

        protected override string BrokenSound => null;

        // ── 狀態(測試、bot、之後的超級跳)──
        public BarrierSphere Surface => _surface;
        public float Radius => _radius;
        public float MaxRadiusLimit => _maxRadius;
        public float DroneHeight => _droneHeight;
        public Vector3 BarrierCenter => _surface != null ? _surface.Center : transform.position;
        public bool BarrierUp => _surface != null && _surface.IsAlive;
        public float ElapsedFrames => (Time.time - _placedAt) * 60f;
        public float BarrierDamageTaken { get; private set; }
        public float WeakPointDamageTaken { get; private set; }
        public bool Popped => _popped;

        // 兼作 Squid Beakon(S3:隊友可以超級跳到這裡;超級跳由之後的移動單元實作)
        public bool IsSuperJumpBeacon => IsAlive;

        // 累計(測試用)
        public static int PlacedCount { get; private set; }
        public static int PopCount { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetCounts()
        {
            PlacedCount = 0;
            PopCount = 0;
        }

        protected override void Awake()
        {
            base.Awake();
            _surface = new BarrierSphere(this, OnBarrierDamaged);
            _allRenderers = GetComponentsInChildren<Renderer>(true);
            var glass = new System.Collections.Generic.List<Renderer>();
            var neutral = new System.Collections.Generic.List<Renderer>();
            foreach (Renderer r in _allRenderers)
            {
                if (r.name.StartsWith("Glass", System.StringComparison.Ordinal))
                {
                    glass.Add(r);
                }
                else if (!r.name.StartsWith("Ink", System.StringComparison.Ordinal))
                {
                    neutral.Add(r);
                }
            }
            _glassRenderers = glass.ToArray();
            _neutralRenderers = neutral.ToArray();
        }

        // SubProjectile 介面(Big Bubbler 不經 ThrowController 丟出,改用 Place)
        public override void Launch(Vector3 position, Vector3 velocity, SubWeaponConfig config, Inkling owner)
        {
            Debug.LogError("BigBubblerDevice:請用 Place(由 BigBubblerSpecial 設置)", this);
            Expire();
        }

        // 設置在 ground(腳下地面點);ceiling = 腳下到天花板的距離(沒有天花板給大值)
        public void Place(Vector3 ground, float yaw, BigBubblerConfig config, Inkling owner, PlayerSpecialGauge gauge,
            int lockToken, float ceiling)
        {
            _config = config;
            _gauge = gauge;
            _lockToken = lockToken;
            _placedAt = Time.time;
            _popped = false;
            _radius = 0f;
            BarrierDamageTaken = 0f;
            WeakPointDamageTaken = 0f;
            _glassFlashUntil = 0f;
            transform.SetPositionAndRotation(ground, Quaternion.Euler(0f, yaw, 0f));
            // 低天花板:無人機頂到天花板(留 0.4 m)→ 屏障最大半徑跟著縮小,但不小於最小半徑
            float droneTop = Mathf.Min(config.DroneMaxHeight, ceiling - 0.4f);
            _maxRadius = Mathf.Clamp(droneTop - config.CenterHeight, config.MinRadius, config.MaxRadius);
            _droneHeight = LauncherTop;
            Team team = owner != null ? owner.Team : Team.Alpha;
            Deploy(owner, team);
            _surface.Enable(team);
            _surface.Blocking = false;
            _surface.Radius = 0f;
            _surface.Center = ground + Vector3.up * config.CenterHeight;
            ApplyVisual();
            RetintParts();
            PlacedCount++;
            // 設置時腳下塗墨(大招自己的墨不充能)
            InkSource source = owner != null ? owner.InkSource(false) : new InkSource(team, null, false);
            InkPainter.PaintSphere(ground, config.PaintRadius, 0.7f, source);
            if (InkSplashFxPool.Instance != null)
            {
                InkSplashFxPool.Instance.Spawn(ground + Vector3.up * 0.1f, Vector3.up, TeamPalette.ColorOf(team));
            }
            GameAudio.Play(config.DeploySound, ground + Vector3.up, 1f, 0f);
        }

        private void Update()
        {
            if (!IsDeployed || _config == null)
            {
                return;
            }
            // 自然扣耐久(S3 TimeDamage 92.1/s;扣到 0 = 自然消失)
            Drain(_config.DecayPerSecond * Time.deltaTime);
            ApplyVisual();
            if (_propeller != null)
            {
                _propeller.Rotate(0f, 900f * Time.deltaTime, 0f, Space.Self);
            }
        }

        // 無人機高度與屏障半徑:點火前停在發射座頂、上升期間沿 AscendCurve、之後 = 球心高度 + 耐久換算的半徑
        private void ApplyVisual()
        {
            float frames = ElapsedFrames;
            float ratio = MaxHp > 0f ? Hp / MaxHp : 0f;
            float target = _config.MinRadius
                           + (_maxRadius - _config.MinRadius) * HermiteCurve.BubblerRadiusRatio.Evaluate(ratio);
            float top = _config.CenterHeight + target;
            float height;
            if (frames < _config.IgnitionFrame)
            {
                height = LauncherTop;
            }
            else if (frames < _config.IgnitionFrame + _config.AscendFrames)
            {
                float t = (frames - _config.IgnitionFrame) / Mathf.Max(1, _config.AscendFrames);
                height = Mathf.Lerp(LauncherTop, top, HermiteCurve.BubblerAscend.Evaluate(t));
            }
            else
            {
                height = top;
            }
            _droneHeight = height;
            _radius = Mathf.Max(0f, height - _config.CenterHeight);
            _surface.Center = transform.position + Vector3.up * _config.CenterHeight;
            _surface.Radius = _radius;
            // 前隙(50F)結束 = 展開完成,才開始擋
            _surface.Blocking = frames >= _config.FrontLagFrames && IsAlive;
            if (_drone != null && Mathf.Abs(_drone.localPosition.y - height) > 0.01f)
            {
                _drone.localPosition = new Vector3(0f, height, 0f);
            }
            if (_barrier != null)
            {
                float diameter = _radius * 2f;
                if (Mathf.Abs(_barrier.localScale.x - diameter) > 0.005f)
                {
                    _barrier.localScale = Vector3.one * Mathf.Max(0.001f, diameter);
                }
                _barrier.localPosition = new Vector3(0f, _config.CenterHeight, 0f);
            }
        }

        // 範圍傷害量距離:發射座與無人機取較近者
        public override Vector3 ClosestPoint(Vector3 from)
        {
            Vector3 best = transform.position;
            float bestSq = float.MaxValue;
            ConsiderCollider(_launcherCollider, from, ref best, ref bestSq);
            ConsiderCollider(_droneCollider, from, ref best, ref bestSq);
            return bestSq < float.MaxValue ? best : base.ClosestPoint(from);
        }

        private static void ConsiderCollider(Collider collider, Vector3 from, ref Vector3 best, ref float bestSq)
        {
            if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy)
            {
                return;
            }
            Vector3 point = collider.ClosestPoint(from);
            float sq = (point - from).sqrMagnitude;
            if (sq < bestSq)
            {
                bestSq = sq;
                best = point;
            }
        }

        // 屏障面受擊(BarrierSphere 轉來):× 0.64 再照屏障欄的物件倍率扣共用耐久
        private void OnBarrierDamaged(in DamageInfo info)
        {
            if (!IsAlive || _config == null)
            {
                return;
            }
            _barrierHit = true;
            ApplyDamage(info.WithAmount(info.Amount * _config.BarrierDamageRatio));
            _barrierHit = false;
            _glassFlashUntil = Time.time + 0.1f;
            if (InkSplashFxPool.Instance != null)
            {
                Vector3 normal = (info.Point - _surface.Center).normalized;
                InkSplashFxPool.Instance.Spawn(info.Point, normal, TeamPalette.ColorOf(info.Team));
            }
            if (Time.time >= _nextHitSoundAt)
            {
                _nextHitSoundAt = Time.time + 0.08f;
                GameAudio.Play(_config.HitSound, info.Point, 0.7f, 0.08f);
            }
        }

        protected override void OnDamaged(float dealt, in DamageInfo info)
        {
            if (_barrierHit)
            {
                BarrierDamageTaken += dealt;
            }
            else
            {
                WeakPointDamageTaken += dealt;
            }
        }

        protected override void LateUpdate()
        {
            base.LateUpdate();
            if (!IsDeployed)
            {
                return;
            }
            // 基底只在受擊閃白切換時重上整個物件的隊伍色 → 跟著把原色部位與半透明屏障改回來
            if (IsFlashing != _flashState)
            {
                _flashState = IsFlashing;
                RetintParts();
            }
            bool glassFlash = Time.time < _glassFlashUntil;
            if (glassFlash != _glassFlashApplied)
            {
                _glassFlashApplied = glassFlash;
                TintGlass(glassFlash ? GlassHitAlpha : GlassAlpha);
            }
        }

        private void RetintParts()
        {
            for (int i = 0; i < _neutralRenderers.Length; i++)
            {
                if (_neutralRenderers[i] != null)
                {
                    _neutralRenderers[i].SetPropertyBlock(null);
                }
            }
            _glassFlashApplied = false;
            TintGlass(GlassAlpha);
        }

        private void TintGlass(float alpha)
        {
            Color color = TeamPalette.ColorOf(Team);
            for (int i = 0; i < _glassRenderers.Length; i++)
            {
                Renderer r = _glassRenderers[i];
                if (r == null)
                {
                    continue;
                }
                // 球面半透明;球面上的線(LineRenderer)較實
                TeamTint.Apply(r, color, r is LineRenderer ? Mathf.Min(1f, alpha + (RingAlpha - GlassAlpha)) : alpha);
            }
        }

        // 被打破 / 自然消失 / 被擠掉:破掉特效、能量鎖定歸 0、屏障下場
        public override void Expire()
        {
            Pop();
            base.Expire();
        }

        protected override void OnDisable()
        {
            _surface?.Disable();
            base.OnDisable();
        }

        private void Pop()
        {
            if (_popped || _config == null)
            {
                _surface?.Disable();
                return;
            }
            _popped = true;
            PopCount++;
            Vector3 center = _surface.Center;
            float radius = Mathf.Max(1f, _radius);
            _surface.Disable();
            if (InkSplashFxPool.Instance != null)
            {
                Color color = TeamPalette.ColorOf(Team);
                for (int i = 0; i < 10; i++)
                {
                    float angle = i * Mathf.PI * 2f / 10f;
                    Vector3 dir = new Vector3(Mathf.Cos(angle), 0.35f, Mathf.Sin(angle)).normalized;
                    InkSplashFxPool.Instance.Spawn(center + dir * radius, dir, color);
                }
            }
            GameAudio.Play(_config.PopSound, center, 1f, 0.03f);
            if (_gauge != null)
            {
                _gauge.EndLock(_lockToken);
            }
        }
    }
}

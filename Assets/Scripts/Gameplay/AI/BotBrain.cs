using System.Collections.Generic;
using SplatoonC.Core;
using SplatoonC.Core.AI;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Match;
using SplatoonC.Gameplay.Player;
using UnityEngine;
using UnityEngine.AI;

namespace SplatoonC.Gameplay.AI
{
    // bot 的意圖:BotBrain 每幀寫、PlayerInputRouter 讀(與玩家走同一條輸入路徑)
    public sealed class BotIntent : IPlayerIntentSource
    {
        public Vector2 Move;
        public bool Attack;
        public bool Squid;
        public bool Sub;
        public bool JumpPulse;
        public bool SpecialPulse;
        // 跳躍鍵按住(預設按住 = 跳滿高度);貼牆衝牆蓄滿時 BotBrain 放開一幀才會衝出去(A4)
        public bool JumpHold = true;

        public Vector2 MoveInput => Move;
        public Vector2 LookDeltaDeg => Vector2.zero;
        public bool AttackHeld => Attack;
        public bool SquidHeld => Squid;
        public bool SubWeaponHeld => Sub;
        public bool JumpHeld => JumpHold;

        public bool JumpPressedThisFrame
        {
            get
            {
                bool v = JumpPulse;
                JumpPulse = false;
                return v;
            }
        }

        public bool SpecialPressedThisFrame
        {
            get
            {
                bool v = SpecialPulse;
                SpecialPulse = false;
                return v;
            }
        }
    }

    // bot 大腦:感知(視線、敵人)→ 模式選擇(交戰/撤退/回墨/塗地/大招)→ 導航(NavMesh 路徑)+ 瞄準(BotAimView)。
    // 只產生「意圖」,實際移動、射擊、烏賊化全交給與玩家相同的元件——bot 不會作弊,也不需要另一套物理。
    [RequireComponent(typeof(Inkling))]
    public sealed class BotBrain : MonoBehaviour, IBotControl
    {
        public enum Difficulty
        {
            Easy = 0,
            Normal = 1,
            Hard = 2,
        }

        public enum Mode
        {
            Idle,
            Paint,
            Engage,
            Retreat,
            Refill,
            Special,
            // 逃出大招的範圍預兆(Ink Vac 落地爆炸圈…)
            Evade,
            // A4 超級跳蓄力/飛行中(交給 SuperJump,不下任何意圖)
            SuperJump,
            // A4 守敵方超級跳落點(S3 落點標記所有人都看得到):走到落點旁、預瞄著地點
            CampMarker,
        }

        private const float PerceptionInterval = 0.2f;
        private const float SubmergedVisibleRange = 4f;
        private const int MaxCorners = 64;
        // 逃離方向被擋住時依序嘗試的偏轉角(度)
        private static readonly float[] FleeTurns = { 50f, -50f, 95f, -95f };

        private Inkling _self;
        private InklingHealth _health;
        private InklingLifecycle _life;
        private PlayerInputRouter _router;
        private PlayerInkTank _tank;
        private SquidController _squid;
        private WeaponLoadout _loadout;
        private ThrowController _throw;
        private PlayerSpecialGauge _special;
        private CharacterController _controller;
        private BotAimView _view;
        private readonly BotIntent _intent = new BotIntent();

        private Difficulty _difficulty = Difficulty.Normal;
        private float _reactionTime = 0.35f;
        private float _fireToleranceDeg = 7f;
        // 大招預兆的起跳提前量(秒):波前還要這麼久才到時就跳(Easy 太晚常被打中)
        private float _hazardJumpLead = 0.22f;

        // 導航
        private NavMeshPath _path;
        private readonly Vector3[] _corners = new Vector3[MaxCorners];
        private int _cornerCount;
        private int _cornerIndex;
        private Vector3 _destination;
        private bool _hasDestination;
        private float _nextRepathAt;
        private Vector3 _progressAnchor;
        private float _progressCheckAt;
        private int _stuckStrikes;

        // 感知
        private float _nextPerceiveAt;
        private Inkling _target;
        private bool _targetVisible;
        private float _targetAcquiredAt;
        private Vector3 _targetLastSeen;
        private float _targetLastSeenAt;
        private float _lastHurtAt = -99f;

        // 塗地
        private int _paintTarget = -1;
        private float _paintTargetUntil;
        private float _sweepPhase;
        private float _lingerUntil;

        // 交戰
        private float _strafeSign = 1f;
        private float _nextStrafeFlipAt;
        private float _nextJumpAt;
        private float _nextSubAt;
        private float _subReleaseAt;
        private float _nextSpecialThinkAt;

        // 大招範圍預兆 / 吸引點(BotHazards)
        private bool _fleeing;
        private bool _visiting;

        // A4 移動:超級跳(離前線太遠 → 跳向安全的隊友/信標)、裝置放置(信標)、衝牆放開、預兆起跳
        private SuperJump _superJump;
        private SquidMoves _moves;
        private readonly List<SuperJumpTarget> _jumpTargets = new List<SuperJumpTarget>(16);
        private float _nextSuperJumpThinkAt;
        private bool _wasSuperJumping;
        private bool _hazardJump;
        private bool _hazardSoon;
        // 預兆快到時提早放開扳機的秒數(疊在起跳提前量上):雙槍按著開火會自己閃避翻滾,
        // 一滾連硬直 48F(0.8 s)期間跳躍鍵都被吃掉 → 起跳前 0.9 s 起就不再開新的一滾
        private const float HazardPrepareSeconds = 0.9f;
        private float _placeReleaseAt;
        private float _nextPlaceAt;

        // 超級跳決策門檻:往前線推進至少這麼多(公尺)、直線距離至少這麼遠、落點周圍這麼近沒有已知的敵人才跳。
        // S3 超級跳 218F ≈ 3.6 s,同時間潛游約 40 m → 推進不到 45 m 用游的比較快(ScorchGorge 基地到中線約 79 m)
        private const float SuperJumpMinGain = 45f;
        private const float SuperJumpMinTravel = 45f;
        private const float SuperJumpSafeRadius = 12f;
        // 守落點:敵方落點在這個水平距離內、距著地不到這麼多秒才去守(Easy 不守)。
        // S3 標記在蓄力一開始就出現:4 秒涵蓋人形開始的整段 236F(3.93 s),看到標記就可以動身
        private const float CampMarkerRange = 22f;
        private const float CampMarkerLeadSeconds = 4f;

        // 累計(測試/探針):bot 自己決定開始的超級跳次數、放下的裝置數
        public int SuperJumpsStarted { get; private set; }
        public int DevicesPlaced { get; private set; }
        // 守敵方落點的幀數(測試/探針)
        public int CampMarkersWatched { get; private set; }

        public Mode CurrentMode { get; private set; } = Mode.Idle;
        public Team Team => _self != null ? _self.Team : Team.None;
        public Inkling Target => _target;
        public Vector3 Destination => _destination;
        public Difficulty Level => _difficulty;

        private void Awake()
        {
            _self = GetComponent<Inkling>();
            _health = GetComponent<InklingHealth>();
            _life = GetComponent<InklingLifecycle>();
            _router = GetComponent<PlayerInputRouter>();
            _tank = GetComponent<PlayerInkTank>();
            _squid = GetComponent<SquidController>();
            _loadout = GetComponent<WeaponLoadout>();
            _throw = GetComponent<ThrowController>();
            _special = GetComponent<PlayerSpecialGauge>();
            _controller = GetComponent<CharacterController>();
            _view = GetComponent<BotAimView>();
            if (_view == null)
            {
                _view = gameObject.AddComponent<BotAimView>();
            }
            // PlayerLocomotion.Awake 掛上的移動元件(SuperJump / SquidMoves);Awake 順序不保證 → 沒有就補掛
            _superJump = SuperJump.For(this);
            _moves = SquidMoves.For(this);
            _path = new NavMeshPath();
            _sweepPhase = Random.value * 10f;
        }

        private void OnEnable()
        {
            _self.AimView = _view;
            if (_router != null)
            {
                _router.SetOverrideSource(_intent);
            }
            if (_health != null)
            {
                _health.Damaged += OnDamaged;
            }
            if (_life != null)
            {
                _life.Respawned += OnRespawned;
            }
        }

        private void OnDisable()
        {
            if (_health != null)
            {
                _health.Damaged -= OnDamaged;
            }
            if (_life != null)
            {
                _life.Respawned -= OnRespawned;
            }
        }

        public void Configure(Difficulty difficulty)
        {
            _difficulty = difficulty;
            switch (difficulty)
            {
                case Difficulty.Easy:
                    _view.TurnSpeed = 220f;
                    _view.AimJitterDeg = 4f;
                    _reactionTime = 0.65f;
                    _fireToleranceDeg = 11f;
                    _hazardJumpLead = 0.04f;
                    break;
                case Difficulty.Hard:
                    _view.TurnSpeed = 600f;
                    _view.AimJitterDeg = 1f;
                    _reactionTime = 0.18f;
                    _fireToleranceDeg = 5f;
                    _hazardJumpLead = 0.3f;
                    break;
                default:
                    _view.TurnSpeed = 380f;
                    _view.AimJitterDeg = 2.2f;
                    _reactionTime = 0.35f;
                    _fireToleranceDeg = 7f;
                    _hazardJumpLead = 0.22f;
                    break;
            }
        }

        private void OnDamaged(float amount, DamageInfo info)
        {
            _lastHurtAt = Time.time;
            // 被打會轉頭找攻擊者(即使原本沒看到)
            if (info.Attacker is Inkling attacker && attacker != null && _target == null)
            {
                _target = attacker;
                _targetAcquiredAt = Time.time;
                _targetLastSeen = attacker.transform.position;
                _targetLastSeenAt = Time.time;
            }
        }

        private void OnRespawned()
        {
            _hasDestination = false;
            _paintTarget = -1;
            _target = null;
            _view.Snap(transform.eulerAngles.y, 10f);
        }

        private void Update()
        {
            ResetIntent();
            if (_life != null && _life.IsDead)
            {
                CurrentMode = Mode.Idle;
                return;
            }
            MatchController match = MatchController.Instance;
            if (match != null && !match.PlayersCanAct)
            {
                CurrentMode = Mode.Idle;
                return;
            }
            // 超級跳蓄力/飛行中:交給 SuperJump(輸入本來就被鎖);落地後重新規劃路線
            if (_superJump != null && _superJump.IsBusy)
            {
                CurrentMode = Mode.SuperJump;
                _wasSuperJumping = true;
                return;
            }
            if (_wasSuperJumping)
            {
                _wasSuperJumping = false;
                _hasDestination = false;
                _paintTarget = -1;
                _cornerCount = 0;
            }

            if (Time.time >= _nextPerceiveAt)
            {
                _nextPerceiveAt = Time.time + PerceptionInterval * Random.Range(0.8f, 1.2f);
                Perceive();
            }

            DodgeHazards();
            ThinkSpecial();
            if (_special != null && _special.ActiveSpecial != null)
            {
                CurrentMode = Mode.Special;
                RunSpecial();
                UpdateStuck();
                return;
            }
            // 站在敵方大招的範圍預兆裡(Ink Vac 落地爆炸圈 50F…):先跑出圈(§5.3)
            if (FleeHazards())
            {
                UpdateStuck();
                return;
            }

            if (ThinkSuperJump())
            {
                CurrentMode = Mode.SuperJump;
                return;
            }

            float hp = _health != null ? _health.Normalized : 1f;
            float ink = _tank != null ? _tank.Normalized : 1f;
            MainWeapon engageMain = Main;
            float engageRange = engageMain != null ? engageMain.BotEngageRange : WeaponRange * 1.35f;
            float targetDistance = _target != null ? Vector3.Distance(transform.position, _target.transform.position) : 999f;
            bool hasTarget = _target != null && (_targetVisible || Time.time - _targetLastSeenAt < 1.2f);

            if (hasTarget && targetDistance < engageRange + 4f && hp < 0.35f && ink > 0.05f
                && Time.time - _lastHurtAt < 2f)
            {
                CurrentMode = Mode.Retreat;
                RunRetreat();
            }
            else if (hasTarget && targetDistance < engageRange && ink > 0.03f)
            {
                CurrentMode = Mode.Engage;
                RunEngage(targetDistance);
            }
            else if (!hasTarget && VisitAttractor())
            {
                // 隊友的 Tacticooler 飲料機:走過去喝(BotHazards 吸引點)
                CurrentMode = Mode.Paint;
            }
            else if (ink < 0.12f || (CurrentMode == Mode.Refill && ink < 0.85f && !hasTarget))
            {
                CurrentMode = Mode.Refill;
                RunRefill();
            }
            else if (!hasTarget && ThinkCampMarker())
            {
                CurrentMode = Mode.CampMarker;
            }
            else
            {
                CurrentMode = Mode.Paint;
                RunPaint();
            }
            UpdateDevicePlacement();
            ReleaseChargedSurge();
            if (_hazardJump)
            {
                // 預兆起跳這一幀:放開攻擊與烏賊 → 一般的人形跳(雙槍按著開火 + 推方向時跳躍鍵會被改成閃避翻滾,
                // 翻滾是貼地的,躲不過 Wave Breaker 的波;潛游中按跳又可能變成低空的 Squid Roll)
                _hazardJump = false;
                _intent.Attack = false;
                _intent.Squid = false;
            }
            else if (_hazardSoon && Main is ILocomotionOverride)
            {
                // 波快到了:接管跳躍鍵的主武器(雙槍)先放開扳機,不再開新的一滾
                _intent.Attack = false;
            }
            UpdateStuck();
        }

        private void ResetIntent()
        {
            _intent.Move = Vector2.zero;
            _intent.Attack = false;
            _intent.Squid = false;
            _intent.Sub = false;
            _intent.JumpHold = true;
        }

        // 目前裝備的主武器(換裝後元件會換,每次取)
        private MainWeapon Main => _loadout != null && _loadout.Main != null ? _loadout.Main : GetComponent<MainWeapon>();
        private float WeaponRange => Main != null ? Main.EffectiveRange : 11f;
        private float ProjectileSpeed => Main != null ? Main.ProjectileSpeed : 60f;

        // ───────── 感知 ─────────

        private void Perceive()
        {
            Inkling best = null;
            float bestScore = float.MaxValue;
            bool bestVisible = false;
            bool bestMarked = false;
            Vector3 eye = _view.AimOrigin;
            var all = Inkling.All;
            for (int i = 0; i < all.Count; i++)
            {
                Inkling other = all[i];
                if (other == null || other.Team == _self.Team)
                {
                    continue;
                }
                var life = other.GetComponent<InklingLifecycle>();
                if (life != null && life.IsDead)
                {
                    continue;
                }
                Vector3 chest = other.transform.position + Vector3.up * 1f;
                float distance = Vector3.Distance(eye, chest);
                if (distance > 40f)
                {
                    continue;
                }
                // 超級跳飛行中完全無敵:不當目標(A4)
                var otherJump = other.GetComponent<SuperJump>();
                if (otherJump != null && otherJump.InFlight)
                {
                    continue;
                }
                var squid = other.GetComponent<SquidController>();
                var health = other.GetComponent<InklingHealth>();
                bool submerged = squid != null && squid.IsSubmerged;
                // 累積傷害 ≥ 18 在地圖上曝光;潛伏中儲存蓄力(狙擊槍白圈)也藏不住(§2.11)
                bool revealed = (health != null && health.IsRevealed) || RevealsWhileSubmerged(other);
                // 被我方標記(Point Sensor、Ink Mine、Angle Shooter、Wave Breaker):穿牆、潛伏中都知道位置(§2.11);
                // 我方 Squid Beakon 的雷達範圍內(S3 半徑 15 u、上下 ±15 u 顯示在地圖上)同樣知道位置(A4)
                bool marked = Marking.IsMarked(other, _self.Team)
                              || SquidBeakon.InRadar(other.transform.position, _self.Team);
                if (submerged && !revealed && !marked && distance > SubmergedVisibleRange)
                {
                    continue;
                }
                bool visible = !Physics.Linecast(eye, chest, ~GameLayers.InklingMask, QueryTriggerInteraction.Ignore);
                if (!visible && other != _target && !marked)
                {
                    continue;
                }
                // 距離優先,已鎖定的目標加權(不要一直換人)
                float score = distance - (other == _target ? 4f : 0f) + (visible ? 0f : 10f);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = other;
                    bestVisible = visible;
                    bestMarked = marked;
                }
            }
            if (best != _target)
            {
                _target = best;
                _targetAcquiredAt = Time.time;
            }
            _targetVisible = best != null && bestVisible;
            // 看得到,或被標記(已知位置,穿牆追過去;開火仍要看得到)
            if (_targetVisible || bestMarked)
            {
                _targetLastSeen = best.transform.position;
                _targetLastSeenAt = Time.time;
            }
        }

        // ───────── 交戰 ─────────

        private void RunEngage(float distance)
        {
            Vector3 aimPoint = _targetVisible ? _target.transform.position + Vector3.up * 0.9f : _targetLastSeen + Vector3.up * 0.9f;
            Vector3 targetVelocity = Vector3.zero;
            if (_targetVisible)
            {
                var targetController = _target.GetComponent<CharacterController>();
                targetVelocity = targetController != null ? targetController.velocity : Vector3.zero;
                aimPoint = BotMath.LeadTarget(_view.AimOrigin, aimPoint, targetVelocity, ProjectileSpeed);
            }
            MainWeapon main = Main;
            if (main != null)
            {
                // 主武器可改瞄準點(爆破槍瞄腳邊 / 預備時間的提前量 / 掩體上方空爆);預設回傳原本的胸口 + 提前量
                var aimContext = new BotAimContext
                {
                    Eye = _view.AimOrigin,
                    AimPoint = aimPoint,
                    TargetFeet = aimPoint - Vector3.up * 0.9f,
                    TargetVelocity = targetVelocity,
                    TargetVisible = _targetVisible,
                    Distance = distance,
                    SecondsSinceSeen = _targetVisible ? 0f : Time.time - _targetLastSeenAt,
                };
                aimPoint = main.BotAimPoint(aimContext);
            }
            _view.LookAt(aimPoint);

            bool reacted = Time.time - _targetAcquiredAt >= _reactionTime;
            var context = new BotCombatContext
            {
                Distance = distance,
                TargetVisible = _targetVisible,
                Reacted = reacted,
                AimError = _view.ErrorToTarget,
                FireTolerance = _fireToleranceDeg,
            };
            _intent.Attack = main != null
                ? main.BotAttackIntent(context)
                : reacted && _targetVisible && distance <= WeaponRange * 1.1f && _view.ErrorToTarget < _fireToleranceDeg;

            // 走位:保持約 0.75 倍射程、左右橫移、偶爾跳
            Vector3 toTarget = _target.transform.position - transform.position;
            toTarget.y = 0f;
            Vector3 forward = toTarget.sqrMagnitude > 0.01f ? toTarget.normalized : transform.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            if (Time.time >= _nextStrafeFlipAt)
            {
                _strafeSign = Random.value < 0.5f ? -1f : 1f;
                _nextStrafeFlipAt = Time.time + Random.Range(0.5f, 1.4f);
            }
            float desired = main != null ? main.BotPreferredDistance : WeaponRange * 0.72f;
            float approach = distance > desired + 1.5f ? 1f : distance < desired - 2f ? -0.8f : 0f;
            if (!_targetVisible)
            {
                // 看不到:走向最後看見的位置
                SetDestination(_targetLastSeen);
                MoveAlongPath(1f, false);
                return;
            }
            Vector3 move = right * _strafeSign * 0.8f + forward * approach;
            if (!IsWalkable(transform.position + move.normalized * 1.2f))
            {
                _strafeSign = -_strafeSign;
                move = right * _strafeSign * 0.8f + forward * approach;
            }
            // 距離太遠且腳下是自家墨:潛游接近(比走路快一倍)
            float swimBeyond = main != null ? main.BotSwimApproachDistance : WeaponRange * 1.2f;
            bool swim = distance > swimBeyond && _squid != null && _squid.OnOwnInk;
            if (swim)
            {
                _intent.Attack = false;
                _intent.Squid = true;
                move = forward;
            }
            _intent.Move = BotMath.MoveInputFor(move, _view.Yaw, Mathf.Clamp01(move.magnitude));

            if (Time.time >= _nextJumpAt && _difficulty != Difficulty.Easy)
            {
                _nextJumpAt = Time.time + Random.Range(1.8f, 4f);
                if (Random.value < 0.4f)
                {
                    _intent.JumpPulse = true;
                }
            }
            TryThrowSub(distance);
        }

        private void TryThrowSub(float distance)
        {
            if (_throw == null || _tank == null)
            {
                return;
            }
            if (_subReleaseAt > 0f)
            {
                // 按住中:瞄準落點,時間到就放開(ThrowController 在放開時丟出)
                Vector3 feet = _targetLastSeen;
                AimThrowAt(feet);
                _intent.Attack = false;
                _intent.Sub = Time.time < _subReleaseAt;
                if (Time.time >= _subReleaseAt)
                {
                    _subReleaseAt = 0f;
                }
                return;
            }
            SubWeaponConfig sub = _throw.SubConfig;
            if (sub == null || sub.BotUsage != BotSubUsage.ThrowAtEnemy)
            {
                return;
            }
            Vector2 range = sub.BotRange;
            // 「使用不可」(S3 Torpedo 自己的還在場上)時不嘗試,也不吃掉這次冷卻
            if (Time.time < _nextSubAt || distance < range.x || distance > range.y
                || _tank.Normalized < sub.InkCost + 0.08f || _throw.SubUnavailable)
            {
                return;
            }
            _nextSubAt = Time.time + Random.Range(6f, 11f);
            if (Random.value < (_difficulty == Difficulty.Easy ? 0.25f : 0.5f))
            {
                // 按住多久由副武器決定(預設 0.45 秒;蓄力型如 Curling 依目標距離決定蓄多少)
                _subReleaseAt = Time.time + sub.BotHoldSeconds(distance);
            }
        }

        // 投擲瞄準:讓視線射線落在目標點(ThrowController 以視線命中點當目標,拋物線自己補);
        // 看哪裡由副武器決定(SubWeaponConfig.BotAimPoint:預設目標腳下 + 0.3 m,S3 阻力彈道的副武器解仰角)
        private void AimThrowAt(Vector3 point)
        {
            SubWeaponConfig sub = _throw != null ? _throw.SubConfig : null;
            _view.LookAt(sub != null ? sub.BotAimPoint(transform.position, point) : point + Vector3.up * 0.3f);
        }

        // ───────── 撤退 ─────────

        private void RunRetreat()
        {
            TeamBase home = TeamBase.Of(_self.Team);
            Vector3 goal = home != null ? home.transform.position : transform.position - transform.forward * 10f;
            SetDestination(goal);
            bool onOwn = _squid != null && _squid.OnOwnInk;
            MoveAlongPath(1f, onOwn);
            if (_target != null && _targetVisible)
            {
                // 邊退邊回頭補槍(沒潛游時)
                if (!onOwn)
                {
                    _view.LookAt(_target.transform.position + Vector3.up * 0.9f);
                    MainWeapon main = Main;
                    var context = new BotCombatContext
                    {
                        Distance = Vector3.Distance(transform.position, _target.transform.position),
                        TargetVisible = true,
                        Reacted = true,
                        AimError = _view.ErrorToTarget,
                        FireTolerance = _fireToleranceDeg,
                    };
                    // 預設 = 準心誤差 < 容許 × 1.5 就按住;蓄力類(狙擊槍)由武器決定蓄 / 放
                    _intent.Attack = main != null
                        ? main.BotRetreatAttackIntent(context)
                        : _view.ErrorToTarget < _fireToleranceDeg * 1.5f;
                }
            }
        }

        // ───────── 回墨 ─────────

        private void RunRefill()
        {
            if (_squid != null && _squid.OnOwnInk)
            {
                _intent.Squid = true;
                _hasDestination = false;
                return;
            }
            int own = FindNearestOwnInk(28f);
            if (own >= 0)
            {
                SetDestination(BotDirector.Instance.Map.Samples[own].Position);
                MoveAlongPath(1f, false);
                return;
            }
            // 附近沒有自家墨:往腳邊塗一點再潛(蓄力類由武器決定「按一下 → 蓄到能出手就放」)
            _view.SetTarget(_view.Yaw, 55f);
            MainWeapon feetMain = Main;
            _intent.Attack = feetMain != null ? feetMain.BotFeetPaintIntent(_tank.Normalized) : _tank.Normalized > 0.02f;
        }

        // ───────── 塗地 ─────────

        private void RunPaint()
        {
            TurfMap map = BotDirector.Instance != null ? BotDirector.Instance.Map : null;
            if (map == null || map.Samples.Count == 0)
            {
                return;
            }
            bool targetDone = _paintTarget < 0 || Time.time > _paintTargetUntil
                || (map.OwnerAt(_paintTarget) == (byte)_self.Team && Time.time > _lingerUntil);
            if (targetDone)
            {
                _paintTarget = BotDirector.Instance.PickPaintTarget(this, _self.Team, transform.position);
                _paintTargetUntil = Time.time + Random.Range(6f, 10f);
                _lingerUntil = 0f;
            }
            if (_paintTarget < 0)
            {
                return;
            }
            Vector3 goal = map.Samples[_paintTarget].Position;
            float distance = FlatDistance(transform.position, goal);
            SetDestination(goal);

            _sweepPhase += Time.deltaTime * 2.1f;
            bool onOwn = _squid != null && _squid.OnOwnInk;
            bool onEnemy = _squid != null && _squid.OnEnemyInk;

            if (distance > 3f)
            {
                // 趕路:腳下自家墨且目標還遠 → 潛游;否則人形邊走邊往前方地面掃射鋪路
                bool swim = onOwn && distance > 9f;
                Vector3 steer = MoveAlongPath(1f, swim);
                if (!swim)
                {
                    float yaw = steer.sqrMagnitude > 0.01f ? Mathf.Atan2(steer.x, steer.z) * Mathf.Rad2Deg : _view.Yaw;
                    float sweep = Mathf.Sin(_sweepPhase) * 28f;
                    MainWeapon main = Main;
                    float paintDistance = main != null ? main.BotPaintAimDistance : 7.5f;
                    float pitch = BotMath.PitchToGround(_view.EyeHeight,
                        onEnemy ? 3.5f : paintDistance + Mathf.Sin(_sweepPhase * 0.7f) * paintDistance * 0.3f);
                    _view.SetTarget(yaw + sweep, pitch);
                    _intent.Attack = main != null ? main.BotPaintIntent(_tank.Normalized) : _tank.Normalized > 0.05f;
                }
                else
                {
                    float yaw = steer.sqrMagnitude > 0.01f ? Mathf.Atan2(steer.x, steer.z) * Mathf.Rad2Deg : _view.Yaw;
                    _view.SetTarget(yaw, 12f);
                }
            }
            else
            {
                // 到點:原地繞圈掃射,把周圍塗滿一段時間再換目標
                if (_lingerUntil <= 0f)
                {
                    _lingerUntil = Time.time + Random.Range(1.2f, 2.4f);
                }
                float yaw = _view.Yaw + 140f * Time.deltaTime;
                MainWeapon lingerMain = Main;
                float lingerDistance = lingerMain != null ? lingerMain.BotPaintAimDistance * 0.75f : 5.5f;
                float pitch = BotMath.PitchToGround(_view.EyeHeight, lingerDistance + Mathf.Sin(_sweepPhase) * lingerDistance * 0.4f);
                _view.SetTarget(yaw, pitch);
                _intent.Attack = lingerMain != null ? lingerMain.BotPaintIntent(_tank.Normalized) : _tank.Normalized > 0.05f;
                _intent.Move = BotMath.MoveInputFor(new Vector3(Mathf.Cos(_sweepPhase), 0f, Mathf.Sin(_sweepPhase)), _view.Yaw, 0.35f);
            }
        }

        // ───────── 大招預兆閃避(DESIGN_REFERENCE §5.3)─────────

        // 場上大招物件登記的預兆(BotHazards):目前是 Wave Breaker 的波——波前快到時起跳(任何模式都會跳)
        private void DodgeHazards()
        {
            _hazardSoon = false;
            if (BotHazards.Count == 0 || _controller == null)
            {
                return;
            }
            _hazardSoon = BotHazards.ShouldJump(_self.Team, transform.position, _hazardJumpLead + HazardPrepareSeconds);
            if (!_controller.isGrounded)
            {
                return;
            }
            if (BotHazards.ShouldJump(_self.Team, transform.position, _hazardJumpLead))
            {
                _intent.JumpPulse = true;
                _hazardJump = true;
                BotHazards.ReportJump();
            }
        }

        // 範圍型預兆(BotHazards.TryGetEscape):背離圈心往外跑,腳下自家墨就潛游;前方走不過去就偏轉。回傳 true = 本幀在逃
        private bool FleeHazards()
        {
            if (BotHazards.Count == 0
                || !BotHazards.TryGetEscape(_self.Team, transform.position, out Vector3 away, out float remaining))
            {
                _fleeing = false;
                return false;
            }
            if (!_fleeing)
            {
                _fleeing = true;
                BotHazards.ReportFlee();
            }
            CurrentMode = Mode.Evade;
            Vector3 direction = away;
            float probe = Mathf.Clamp(remaining, 1.2f, 3f);
            if (!IsWalkable(transform.position + direction * probe))
            {
                // 正後方是牆/懸崖:往兩側偏轉找路
                for (int i = 0; i < FleeTurns.Length; i++)
                {
                    Vector3 candidate = Quaternion.Euler(0f, FleeTurns[i], 0f) * away;
                    if (IsWalkable(transform.position + candidate * probe))
                    {
                        direction = candidate;
                        break;
                    }
                }
            }
            bool swim = _squid != null && _squid.OnOwnInk;
            _intent.Squid = swim;
            _view.SetTarget(Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg, 10f);
            _intent.Move = BotMath.MoveInputFor(direction, _view.Yaw, 1f);
            _hasDestination = false;
            return true;
        }

        // 吸引點(BotHazards.TryGetAttraction:隊友的 Tacticooler):導航過去,最後一段以人形走(烏賊態拿不到飲料)
        private bool VisitAttractor()
        {
            if (BotHazards.AttractorCount == 0 || !BotHazards.TryGetAttraction(_self, out Vector3 spot))
            {
                _visiting = false;
                return false;
            }
            if (!_visiting)
            {
                _visiting = true;
                BotHazards.ReportVisit();
            }
            float distance = FlatDistance(transform.position, spot);
            SetDestination(spot);
            bool swim = distance > 9f && _squid != null && _squid.OnOwnInk;
            Vector3 steer = MoveAlongPath(1f, swim);
            if (!swim && steer.sqrMagnitude > 0.01f)
            {
                _view.SetTarget(Mathf.Atan2(steer.x, steer.z) * Mathf.Rad2Deg, 15f);
            }
            return true;
        }

        // ───────── 超級跳(A4)─────────

        // 離前線太遠(剛重生、回家補墨)時,跳向「往前推進夠多、而且安全」的隊友 / 信標 / Big Bubbler;
        // 不跳回出生點(S3 bot 怎麼做不重要,這裡取合理行為)。
        private bool ThinkSuperJump()
        {
            if (_superJump == null || Time.time < _nextSuperJumpThinkAt)
            {
                return false;
            }
            _nextSuperJumpThinkAt = Time.time + Random.Range(1.2f, 2.2f);
            // 交戰/追蹤中(3 秒內看到或知道敵人位置)不跳;大招預兆快到了(要起跳躲波)也不跳 ——
            // 蓄力中動不了又打得到,而且蓄力會把預兆起跳的那一下跳躍吃掉
            if (HasTarget || _hazardSoon || _hazardJump || _controller == null || !_controller.isGrounded
                || !_superJump.CanStartNow(out _))
            {
                return false;
            }
            TeamBase enemyBase = TeamBase.Of(TeamUtil.Opponent(_self.Team));
            if (enemyBase == null)
            {
                return false;
            }
            Vector3 front = enemyBase.transform.position;
            Vector3 position = transform.position;
            float myDepth = FlatDistance(position, front);
            int count = SuperJumpTargets.Collect(_self, _jumpTargets, false);
            int best = -1;
            float bestGain = SuperJumpMinGain;
            for (int i = 0; i < count; i++)
            {
                SuperJumpTarget candidate = _jumpTargets[i];
                Vector3 anchor = candidate.AnchorPosition;
                float gain = myDepth - FlatDistance(anchor, front);
                if (gain < bestGain || FlatDistance(position, anchor) < SuperJumpMinTravel
                    || !IsSafeLanding(candidate, anchor))
                {
                    continue;
                }
                best = i;
                bestGain = gain;
            }
            if (best < 0 || Random.value > 0.7f)
            {
                return false;
            }
            if (!_superJump.TryBegin(_jumpTargets[best]))
            {
                return false;
            }
            SuperJumpsStarted++;
            _hasDestination = false;
            _paintTarget = -1;
            return true;
        }

        // 落點安全:隊友沒在交戰(3 秒內沒受傷、傷勢 < 25、沒在超級跳),落點附近沒有「我方知道的」活著的敵人。
        // 不作弊(§2.11):潛伏中、沒曝光、沒被標記、不在我方信標雷達內的敵人不算 —— 跳下去照樣可能被埋伏
        private bool IsSafeLanding(in SuperJumpTarget target, Vector3 anchor)
        {
            if (target.Kind == SuperJumpTargetKind.Teammate)
            {
                Inkling mate = target.Teammate;
                var health = mate != null ? mate.GetComponent<InklingHealth>() : null;
                if (health == null || health.Damage > 25f || Time.time - health.LastDamageTime < 3f)
                {
                    return false;
                }
                var mateJump = mate.GetComponent<SuperJump>();
                if (mateJump != null && mateJump.IsBusy)
                {
                    return false;
                }
            }
            var all = Inkling.All;
            for (int i = 0; i < all.Count; i++)
            {
                Inkling other = all[i];
                if (other == null || other.Team == _self.Team)
                {
                    continue;
                }
                var life = other.GetComponent<InklingLifecycle>();
                if (life != null && life.IsDead)
                {
                    continue;
                }
                if ((other.transform.position - anchor).sqrMagnitude < SuperJumpSafeRadius * SuperJumpSafeRadius
                    && IsKnownToTeam(other))
                {
                    return false;
                }
            }
            return true;
        }

        // 敵人的位置我方是否知道:浮在外面(人形或沒潛入的烏賊)、累積傷害曝光、被我方標記、在我方信標雷達內
        private bool IsKnownToTeam(Inkling enemy)
        {
            var squid = enemy.GetComponent<SquidController>();
            if (squid == null || !squid.IsSubmerged)
            {
                return true;
            }
            var health = enemy.GetComponent<InklingHealth>();
            return (health != null && health.IsRevealed) || Marking.IsMarked(enemy, _self.Team)
                   || SquidBeakon.InRadar(enemy.transform.position, _self.Team);
        }

        // 守敵方超級跳落點(S3:落點標記所有人都看得到,敵人會去守):附近(CampMarkerRange 內、上下 8 m)有敵人
        // 快要著地(CampMarkerLeadSeconds 內)→ 走到落點旁的交戰距離、預瞄著地點,著地前一刻開火。
        // 著地的人現形後由 Perceive 接成一般目標(進入交戰)。跳向信標的落點標記是隱藏的,不會被守
        private bool ThinkCampMarker()
        {
            if (_difficulty == Difficulty.Easy)
            {
                return false;
            }
            SuperJump best = null;
            float bestSq = CampMarkerRange * CampMarkerRange;
            Vector3 position = transform.position;
            var jumps = SuperJump.All;
            for (int i = 0; i < jumps.Count; i++)
            {
                SuperJump jump = jumps[i];
                if (jump == null || !jump.MarkerShowing || jump.Owner == null || jump.Owner.Team == _self.Team
                    || jump.SecondsToLanding > CampMarkerLeadSeconds)
                {
                    continue;
                }
                Vector3 landing = jump.LandingPoint;
                if (Mathf.Abs(landing.y - position.y) > 8f)
                {
                    continue;
                }
                float dx = landing.x - position.x;
                float dz = landing.z - position.z;
                float sq = dx * dx + dz * dz;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = jump;
                }
            }
            if (best == null)
            {
                return false;
            }
            RunCampMarker(best);
            return true;
        }

        private void RunCampMarker(SuperJump jump)
        {
            Vector3 landing = jump.LandingPoint;
            float distance = FlatDistance(transform.position, landing);
            MainWeapon main = Main;
            float desired = Mathf.Min(main != null ? main.BotPreferredDistance : WeaponRange * 0.72f, 8f);
            if (distance > desired + 1f)
            {
                Vector3 away = transform.position - landing;
                away.y = 0f;
                away = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.back;
                SetDestination(landing + away * desired);
                MoveAlongPath(1f, false);
            }
            Vector3 aim = landing + Vector3.up * 0.9f;
            _view.LookAt(aim);
            // 著地前一刻起朝落點開火(飛行中無敵;落地的人要一點時間才能反擊)
            bool landingNow = jump.InFlight && jump.SecondsToLanding <= 0.3f;
            if (landingNow && distance <= WeaponRange * 1.05f && _tank != null && _tank.Normalized > 0.05f)
            {
                var context = new BotCombatContext
                {
                    Distance = distance,
                    TargetVisible = true,
                    Reacted = true,
                    AimError = _view.ErrorToTarget,
                    FireTolerance = _fireToleranceDeg,
                };
                _intent.Attack = main != null
                    ? main.BotAttackIntent(context)
                    : _view.ErrorToTarget < _fireToleranceDeg;
            }
            CampMarkersWatched++;
        }

        // 放置型副武器(BotUsage.PlaceNearby:信標…):塗地中每隔幾秒問副武器要不要在這裡放,按住一下放開
        private void UpdateDevicePlacement()
        {
            if (_throw == null || _tank == null)
            {
                return;
            }
            if (_placeReleaseAt > 0f)
            {
                _intent.Attack = false;
                _intent.Squid = false;
                _intent.Sub = Time.time < _placeReleaseAt;
                if (Time.time >= _placeReleaseAt)
                {
                    _placeReleaseAt = 0f;
                    DevicesPlaced++;
                }
                return;
            }
            if (CurrentMode != Mode.Paint || Time.time < _nextPlaceAt)
            {
                return;
            }
            SubWeaponConfig sub = _throw.SubConfig;
            if (sub == null || sub.BotUsage != BotSubUsage.PlaceNearby)
            {
                return;
            }
            _nextPlaceAt = Time.time + Random.Range(3f, 6f);
            if (_tank.Normalized < sub.InkCost + 0.05f || !sub.BotShouldPlace(_self))
            {
                return;
            }
            _placeReleaseAt = Time.time + 0.2f;
            _intent.Sub = true;
            _intent.Attack = false;
            _intent.Squid = false;
        }

        // 貼牆衝牆蓄滿就放開(bot 的跳躍鍵平常一直按著;卡在牆邊時 UpdateStuck 的跳會變成衝牆蓄力)
        private void ReleaseChargedSurge()
        {
            if (_moves != null && _moves.IsChargingSurge && _moves.SurgeFullyCharged)
            {
                _intent.JumpHold = false;
            }
        }

        // ───────── 大招 ─────────

        private void ThinkSpecial()
        {
            if (_special == null || !_special.IsFull || _special.IsActive || Time.time < _nextSpecialThinkAt)
            {
                return;
            }
            _nextSpecialThinkAt = Time.time + 1f;
            SpecialWeapon special = _special.Special;
            if (special != null ? special.BotWantsActivate(this) : (_target != null && _targetVisible))
            {
                _intent.SpecialPulse = true;
            }
        }

        // 大招啟動中:交給大招模組驅動(視線、按鍵、移動都透過 IBotControl)
        private void RunSpecial()
        {
            SpecialWeapon special = _special.ActiveSpecial;
            if (special != null)
            {
                special.BotDrive(this);
            }
        }

        // ───────── IBotControl(給武器/大招模組用)─────────

        public Vector3 Position => transform.position;
        public bool HasTarget => _target != null && Time.time - _targetLastSeenAt < 3f;
        public bool TargetVisible => _targetVisible;
        public Vector3 TargetPosition => _target != null && _targetVisible ? _target.transform.position : _targetLastSeen;

        public Vector3 TargetVelocity
        {
            get
            {
                var controller = _target != null ? _target.GetComponent<CharacterController>() : null;
                return controller != null ? controller.velocity : Vector3.zero;
            }
        }

        public float TargetDistance => _target != null ? Vector3.Distance(transform.position, TargetPosition) : 999f;

        public Vector3 EnemyBaseDirection
        {
            get
            {
                TeamBase enemyBase = TeamBase.Of(TeamUtil.Opponent(_self.Team));
                Vector3 toward = enemyBase != null ? enemyBase.transform.position - transform.position : transform.forward;
                toward.y = 0f;
                return toward.sqrMagnitude > 0.01f ? toward.normalized : transform.forward;
            }
        }

        public float AimError => _view.ErrorToTarget;
        public float InkRatio => _tank != null ? _tank.Normalized : 1f;

        public void LookAt(Vector3 worldPoint)
        {
            _view.LookAt(worldPoint);
        }

        public void HoldAttack(bool held)
        {
            _intent.Attack = held;
        }

        public void HoldSub(bool held)
        {
            _intent.Sub = held;
        }

        public void PressSpecial()
        {
            _intent.SpecialPulse = true;
        }

        public void Move(Vector3 worldDirection, float magnitude)
        {
            _intent.Move = BotMath.MoveInputFor(worldDirection, _view.Yaw, magnitude);
        }

        public void HoldSquid(bool held)
        {
            _intent.Squid = held;
        }

        public void PressJump()
        {
            _intent.JumpPulse = true;
        }

        // ───────── 導航 ─────────

        private void SetDestination(Vector3 destination)
        {
            bool same = _hasDestination && (destination - _destination).sqrMagnitude < 1.5f;
            if (same && Time.time < _nextRepathAt)
            {
                return;
            }
            _destination = destination;
            _hasDestination = true;
            _nextRepathAt = Time.time + 1.2f;
            _cornerCount = 0;
            _cornerIndex = 0;
            if (!NavMesh.SamplePosition(transform.position, out NavMeshHit from, 2.5f, NavMesh.AllAreas)
                || !NavMesh.SamplePosition(destination, out NavMeshHit to, 2.5f, NavMesh.AllAreas))
            {
                return;
            }
            if (NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, _path)
                && _path.status != NavMeshPathStatus.PathInvalid)
            {
                _cornerCount = _path.GetCornersNonAlloc(_corners);
                _cornerIndex = _cornerCount > 1 ? 1 : 0;
            }
        }

        // 依路徑給移動意圖;回傳本幀的世界移動方向(供瞄準對齊)
        private Vector3 MoveAlongPath(float speed, bool swim)
        {
            Vector3 position = transform.position;
            Vector3 next;
            if (_cornerCount > 0)
            {
                while (_cornerIndex < _cornerCount - 1 && FlatDistance(position, _corners[_cornerIndex]) < 0.9f)
                {
                    _cornerIndex++;
                }
                next = _corners[Mathf.Min(_cornerIndex, _cornerCount - 1)];
            }
            else if (_hasDestination)
            {
                next = _destination;
            }
            else
            {
                return Vector3.zero;
            }
            Vector3 direction = next - position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.04f)
            {
                return Vector3.zero;
            }
            _intent.Squid = swim;
            if (swim)
            {
                // 潛游時視線跟著前進方向,移動輸入才會是「往前」
                _view.SetTarget(Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg, 12f);
            }
            _intent.Move = BotMath.MoveInputFor(direction, _view.Yaw, speed);
            return direction;
        }

        private void UpdateStuck()
        {
            if (Time.time < _progressCheckAt)
            {
                return;
            }
            bool tryingToMove = _intent.Move.sqrMagnitude > 0.2f;
            float moved = FlatDistance(transform.position, _progressAnchor);
            _progressAnchor = transform.position;
            _progressCheckAt = Time.time + 1f;
            if (!tryingToMove || moved > 0.6f)
            {
                _stuckStrikes = 0;
                return;
            }
            _stuckStrikes++;
            _intent.JumpPulse = true;
            if (_stuckStrikes >= 3)
            {
                // 卡太久:換目標重新規劃
                _stuckStrikes = 0;
                _paintTarget = -1;
                _hasDestination = false;
            }
        }

        // 對方潛伏中仍露出位置(主武器掛勾:狙擊槍儲存蓄力的白圈)
        private static bool RevealsWhileSubmerged(Inkling other)
        {
            var loadout = other.GetComponent<WeaponLoadout>();
            MainWeapon main = loadout != null ? loadout.Main : null;
            return main != null && main.isActiveAndEnabled && main.RevealsSubmerged;
        }

        private static bool IsWalkable(Vector3 point)
        {
            return NavMesh.SamplePosition(point, out _, 0.8f, NavMesh.AllAreas);
        }

        private int FindNearestOwnInk(float maxDistance)
        {
            TurfMap map = BotDirector.Instance != null ? BotDirector.Instance.Map : null;
            if (map == null)
            {
                return -1;
            }
            int best = -1;
            float bestSq = maxDistance * maxDistance;
            Vector3 position = transform.position;
            var samples = map.Samples;
            for (int i = 0; i < samples.Count; i++)
            {
                float sq = (samples[i].Position - position).sqrMagnitude;
                if (sq < bestSq && map.OwnerAt(i) == (byte)_self.Team)
                {
                    bestSq = sq;
                    best = i;
                }
            }
            return best;
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}

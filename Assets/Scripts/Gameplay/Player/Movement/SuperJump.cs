using System;
using System.Collections.Generic;
using SplatoonC.Core;
using SplatoonC.Core.Locomotion;
using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.CameraRig;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Painting;
using UnityEngine;

namespace SplatoonC.Gameplay.Player
{
    // 超級跳(S3 DESIGN_REFERENCE §2.9、research/01 §15、research/07 §6.1):
    //   目標:出生點、活著的隊友(他最後著地的位置;他正在超級跳 → 同一個落點;不能跳向正在跳向自己的人)、
    //         自家 Squid Beakon(每個 2 次)、自家 Big Bubbler(BigBubblerDevice.IsSuperJumpBeacon)。
    //   蓄力:80F(QSJ 由 InklingModifiers 提供),從人形開始再 +18F;烏賊形態蹲著、不能動、可被打;
    //         空中開始時要等著地(或貼牆)才開始計時;確認後不能取消 [EST]。目標在蓄力中失效(隊友陣亡、信標被打壞)→ 取消。
    //   飛行:138F(與距離無關),完全無敵、穿牆(CharacterController 關閉、InklingHealth.Invulnerable);
    //         最後 FireWindowFrames 變回人形著地姿勢、可以開槍(副武器要落地後才能丟)。
    //   落點標記:所有人都看得到(名字 + 往下箭頭,墨水隨飛行流乾);跳向信標時隱藏(信標在途中被打壞才顯示)。
    // 輸入:蓄力/飛行期間用 PlayerInputRouter.ActionBlock 鎖住移動/跳/攻擊/副武器/大招(視角照常)。
    // 玩家從 SuperJumpHud(目標選單)呼叫 TryBegin;bot 由 BotBrain 直接呼叫。由 PlayerLocomotion.Awake 自動掛上。
    [DisallowMultipleComponent]
    public sealed class SuperJump : MonoBehaviour
    {
        public enum JumpPhase : byte
        {
            None = 0,
            Charging = 1,
            Flight = 2,
        }

        // 場上所有角色的超級跳元件(HUD 查「誰正跳向我」、bot 找目標)
        public static readonly List<SuperJump> All = new List<SuperJump>();

        // 累計(測試用)
        public static int BeginCount { get; private set; }
        public static int LaunchCount { get; private set; }
        public static int LandCount { get; private set; }
        public static int CancelCount { get; private set; }

        private Inkling _inkling;
        private InklingLifecycle _life;
        private InklingHealth _health;
        private PlayerInputRouter _router;
        private SquidController _squid;
        private PlayerLocomotion _locomotion;
        private CharacterController _controller;
        private PlayerSpecialGauge _special;
        private Transform _visualRoot;
        private MovementConfig _config;
        private SuperJumpMarker _marker;
        private Action<DamageInfo> _onDied;
        private Action _onRespawned;

        private JumpPhase _phase;
        private SuperJumpTarget _target;
        private bool _fromHumanoid;
        private float _chargeSeconds;
        private float _chargeElapsed;
        private float _flightSeconds;
        private float _flightElapsed;
        private Vector3 _start;
        private Vector3 _landing;
        private float _apex;
        private float _landingYaw;
        private bool _markerHidden;
        private bool _landingNow;
        private bool _chargeSoundPlayed;
        private float _cameraPitch;
        private Vector3 _lastGrounded;
        private bool _hasGrounded;
        private Vector3 _flightVelocity;

        // ── 狀態(HUD、視覺、bot、測試)──
        public JumpPhase Phase => _phase;
        public bool IsBusy => _phase != JumpPhase.None;
        public bool IsCharging => _phase == JumpPhase.Charging;
        public bool InFlight => _phase == JumpPhase.Flight;
        public SuperJumpTarget Target => _target;
        // 跳躍者(bot 守落點、HUD 提示讀隊伍與名字)
        public Inkling Owner => _inkling;
        public Vector3 LandingPoint => _landing;
        public bool FromHumanoid => _fromHumanoid;
        public float ChargeSeconds => _chargeSeconds;
        public float ChargeElapsed => _chargeElapsed;
        public float Charge01 => _chargeSeconds > 0f ? Mathf.Clamp01(_chargeElapsed / _chargeSeconds) : 1f;
        public float FlightSeconds => _flightSeconds;
        public float FlightElapsed => _flightElapsed;
        public float Flight01 => _flightSeconds > 0f ? Mathf.Clamp01(_flightElapsed / _flightSeconds) : 1f;

        // 距離著地還有幾秒(蓄力中 = 剩餘蓄力 + 整段飛行;空中開始、還沒著地計時的也照此估);沒在跳 = +∞
        public float SecondsToLanding
        {
            get
            {
                switch (_phase)
                {
                    case JumpPhase.Charging:
                        return Mathf.Max(0f, _chargeSeconds - _chargeElapsed) + _flightSeconds;
                    case JumpPhase.Flight:
                        return Mathf.Max(0f, _flightSeconds - _flightElapsed);
                    default:
                        return float.PositiveInfinity;
                }
            }
        }
        public Vector3 FlightStart => _start;
        public Vector3 FlightVelocity => _flightVelocity;
        public float BeganAt { get; private set; } = float.NegativeInfinity;
        public float LaunchedAt { get; private set; } = float.NegativeInfinity;
        public float LandedAt { get; private set; } = float.NegativeInfinity;
        public int JumpsCompleted { get; private set; }
        public string LastRefusal { get; private set; }
        public SuperJumpMarker Marker => _marker;
        public bool MarkerShowing => _marker != null && _marker.Showing;

        // 落地前可開槍的窗口(人形著地姿勢)
        public bool InFireWindow => InFlight && SuperJumpModel.InFireWindow(P, _flightElapsed, _flightSeconds);

        // SquidController 讀:蓄力與飛行(開槍窗口之前)強制烏賊形態;全程浮出(不潛入、看得見)
        public bool ForcesSquidForm => IsCharging || (InFlight && !InFireWindow);
        public bool ForcesSurfaced => IsBusy;

        // 最後一次著地(站在地上)的位置:隊友跳向我時的落點
        public Vector3 LastGroundedPosition => _hasGrounded ? _lastGrounded : transform.position;

        public event Action<SuperJump> Began;
        public event Action<SuperJump> Launched;
        public event Action<SuperJump> Landed;
        public event Action<SuperJump> Cancelled;

        private MovementConfig Config => _config != null ? _config : (_config = MovementConfig.Current);
        private SuperJumpParams P => Config.SuperJump;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            All.Clear();
            BeginCount = 0;
            LaunchCount = 0;
            LandCount = 0;
            CancelCount = 0;
        }

        // 取得(沒有就掛上):PlayerLocomotion.Awake 呼叫
        public static SuperJump For(Component component)
        {
            if (component == null)
            {
                return null;
            }
            var existing = component.GetComponent<SuperJump>();
            return existing != null ? existing : component.gameObject.AddComponent<SuperJump>();
        }

        private void Awake()
        {
            _inkling = GetComponent<Inkling>();
            _life = GetComponent<InklingLifecycle>();
            _health = GetComponent<InklingHealth>();
            _router = GetComponent<PlayerInputRouter>();
            _squid = GetComponent<SquidController>();
            _locomotion = GetComponent<PlayerLocomotion>();
            _controller = GetComponent<CharacterController>();
            _special = GetComponent<PlayerSpecialGauge>();
            _visualRoot = transform.Find("Visual");
            _onDied = OnDied;
            _onRespawned = OnRespawned;
        }

        private void OnEnable()
        {
            All.Add(this);
            if (_life != null)
            {
                _life.Died += _onDied;
                _life.Respawned += _onRespawned;
            }
        }

        private void OnDisable()
        {
            All.Remove(this);
            if (_life != null)
            {
                _life.Died -= _onDied;
                _life.Respawned -= _onRespawned;
            }
            if (IsBusy)
            {
                Cancel("停用");
            }
        }

        private void OnDestroy()
        {
            if (_marker != null)
            {
                Destroy(_marker.gameObject);
            }
        }

        private void OnDied(DamageInfo info)
        {
            // S3:蓄力中被擊倒 = 跳躍取消(飛行中無敵,不會發生)
            if (IsBusy)
            {
                Cancel("被擊倒");
            }
        }

        private void OnRespawned()
        {
            if (IsBusy)
            {
                Cancel("重生");
            }
            _hasGrounded = false;
        }

        private bool IsDead => _life != null && _life.IsDead;

        // ── 目標 ──

        // 這個目標現在能不能跳(選單列表、bot、蓄力中每幀重驗)
        public bool IsValidTarget(in SuperJumpTarget target)
        {
            switch (target.Kind)
            {
                case SuperJumpTargetKind.Spawn:
                    return true;
                case SuperJumpTargetKind.Teammate:
                {
                    Inkling mate = target.Teammate;
                    if (mate == null || mate == _inkling || !mate.isActiveAndEnabled || _inkling == null
                        || mate.Team != _inkling.Team)
                    {
                        return false;
                    }
                    var life = mate.GetComponent<InklingLifecycle>();
                    if (life != null && life.IsDead)
                    {
                        return false;
                    }
                    // S3:不能跳向「正在跳向自己」的人
                    var other = mate.GetComponent<SuperJump>();
                    return other == null || !other.IsBusy || other._target.Kind != SuperJumpTargetKind.Teammate
                           || other._target.Teammate != _inkling;
                }
                case SuperJumpTargetKind.Beakon:
                    return target.Beakon != null && target.Beakon.CanBeJumpedBy(_inkling);
                case SuperJumpTargetKind.Bubbler:
                    return target.Bubbler != null && _inkling != null && target.Bubbler.IsSuperJumpBeacon
                           && target.Bubbler.Team == _inkling.Team;
                default:
                    return false;
            }
        }

        // 此刻能不能開始超級跳(不含目標)
        public bool CanStartNow(out string reason)
        {
            reason = null;
            if (IsBusy)
            {
                reason = "跳躍中";
            }
            else if (!isActiveAndEnabled || IsDead || _controller == null || !_controller.enabled)
            {
                reason = "無法行動";
            }
            else if (_special != null && _special.ActiveSpecial != null)
            {
                // S3:部分大招使用中不能超級跳 → 這裡一律不行
                reason = "大招使用中";
            }
            else if (_router != null && _router.InputLocked)
            {
                reason = "無法行動";
            }
            return reason == null;
        }

        // 開始超級跳(玩家選單確定 / bot)。回傳 false 時 LastRefusal 記原因
        public bool TryBegin(in SuperJumpTarget target)
        {
            if (!CanStartNow(out string reason))
            {
                LastRefusal = reason;
                return false;
            }
            if (!IsValidTarget(target))
            {
                LastRefusal = "目標無效";
                return false;
            }
            LastRefusal = null;
            _target = target;
            _fromHumanoid = _squid == null || !_squid.IsSquid;
            float baseCharge = _inkling != null ? _inkling.Modifiers.SuperJumpChargeSeconds : P.ChargeFrames / 60f;
            _chargeSeconds = SuperJumpModel.ChargeSeconds(P, baseCharge, _fromHumanoid);
            _flightSeconds = _inkling != null ? _inkling.Modifiers.SuperJumpFlightSeconds : P.FlightFrames / 60f;
            _chargeElapsed = 0f;
            _flightElapsed = 0f;
            _flightVelocity = Vector3.zero;
            _chargeSoundPlayed = false;
            _phase = JumpPhase.Charging;
            BeganAt = Time.time;
            _landing = ResolveLanding(target);
            _markerHidden = target.Kind == SuperJumpTargetKind.Beakon;
            ShowMarker();
            ApplyInputBlock();
            BeginCount++;
            Began?.Invoke(this);
            return true;
        }

        // 外部瞬移(重生、測試搬位置)或停用:中止跳躍。自己落地時的瞬移不算
        public void Abort()
        {
            if (IsBusy && !_landingNow)
            {
                Cancel("瞬移");
            }
        }

        // ── 每幀 ──

        private void Update()
        {
            if (_phase == JumpPhase.None)
            {
                TrackGrounded();
                return;
            }
            if (IsDead)
            {
                Cancel("被擊倒");
                return;
            }
            if (_phase == JumpPhase.Charging)
            {
                StepCharge(Time.deltaTime);
            }
            else
            {
                StepFlight(Time.deltaTime);
            }
        }

        private void TrackGrounded()
        {
            if (IsDead || _controller == null || !_controller.enabled)
            {
                return;
            }
            if (_controller.isGrounded)
            {
                _lastGrounded = transform.position;
                _hasGrounded = true;
            }
        }

        private void StepCharge(float deltaTime)
        {
            if (!IsValidTarget(_target))
            {
                Cancel("目標消失");
                return;
            }
            // 比賽流程鎖上(GAME! 之後)或開了大招:蓄力中止(已起飛的照常落地)
            if ((_router != null && _router.InputLocked) || (_special != null && _special.ActiveSpecial != null))
            {
                Cancel("無法行動");
                return;
            }
            // 隊友在移動:標記跟著他最後著地的位置(起飛那一刻才鎖定)
            _landing = ResolveLanding(_target);
            if (_marker != null)
            {
                _marker.MoveTo(_landing);
            }
            // 空中開始的要等著地(或貼牆)才開始計時(S3:不能拿來躲墜落)
            bool planted = (_controller != null && _controller.enabled && _controller.isGrounded)
                           || (_locomotion != null && _locomotion.IsClimbing);
            if (planted)
            {
                if (!_chargeSoundPlayed)
                {
                    _chargeSoundPlayed = true;
                    PlaySound(Config.SuperJumpChargeSound, transform.position, 0.9f);
                }
                _chargeElapsed += deltaTime;
            }
            ApplyInputBlock();
            if (_chargeElapsed >= _chargeSeconds)
            {
                Launch();
            }
        }

        private void Launch()
        {
            _landing = ResolveLanding(_target);
            _start = transform.position;
            _apex = SuperJumpModel.ApexHeight(P, _start, _landing);
            Vector3 flat = _landing - _start;
            flat.y = 0f;
            _landingYaw = flat.sqrMagnitude > 0.01f ? Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg : transform.eulerAngles.y;
            _phase = JumpPhase.Flight;
            _flightElapsed = 0f;
            LaunchedAt = Time.time;
            // 飛行:無敵、穿牆(關掉 CharacterController:子彈打不到、也不擋路)
            if (_controller != null)
            {
                _controller.enabled = false;
            }
            if (_health != null)
            {
                _health.Invulnerable = true;
            }
            if (_visualRoot != null)
            {
                _visualRoot.rotation = Quaternion.Euler(0f, _landingYaw, 0f);
            }
            if (_marker != null)
            {
                _marker.MoveTo(_landing);
                _marker.SetFill(1f);
            }
            _cameraPitch = 12f;
            PlaySound(Config.SuperJumpLaunchSound, _start, 1f);
            if (!_markerHidden)
            {
                // 落點標記的聲音在落點播(附近的敵人聽得到)
                GameAudio.Play(Config.SuperJumpMarkerSound, _landing + Vector3.up, 0.9f, 0f);
            }
            if (InkSplashFxPool.Instance != null)
            {
                InkSplashFxPool.Instance.Spawn(_start + Vector3.up * 0.1f, Vector3.up, InkColor);
            }
            ApplyInputBlock();
            LaunchCount++;
            Launched?.Invoke(this);
        }

        private void StepFlight(float deltaTime)
        {
            _flightElapsed += deltaTime;
            float t = _flightSeconds > 0f ? _flightElapsed / _flightSeconds : 1f;
            Vector3 previous = transform.position;
            Vector3 position = SuperJumpModel.FlightPosition(_start, _landing, _apex, t);
            transform.position = position;
            _flightVelocity = deltaTime > 0f ? (position - previous) / deltaTime : Vector3.zero;
            if (_visualRoot != null)
            {
                _visualRoot.rotation = Quaternion.Euler(0f, _landingYaw, 0f);
            }
            // 跳向信標:信標在途中被打壞 → 落點標記現形(S3)
            if (_markerHidden && _target.Kind == SuperJumpTargetKind.Beakon
                && (_target.Beakon == null || !_target.Beakon.IsAlive))
            {
                _markerHidden = false;
                ShowMarker();
                if (_marker != null)
                {
                    _marker.MoveTo(_landing);
                }
            }
            if (_marker != null && _marker.Showing)
            {
                _marker.SetFill(SuperJumpModel.MarkerFill(_flightElapsed, _flightSeconds));
            }
            SteerCamera(t, deltaTime);
            ApplyInputBlock();
            if (t >= 1f)
            {
                Land();
            }
        }

        private void Land()
        {
            _landingNow = true;
            try
            {
                if (_inkling != null)
                {
                    _inkling.Teleport(_landing, _landingYaw, false);
                }
                else
                {
                    transform.position = _landing;
                }
            }
            finally
            {
                _landingNow = false;
            }
            if (_controller != null && !IsDead)
            {
                _controller.enabled = true;
            }
            if (_health != null)
            {
                _health.Invulnerable = false;
            }
            _phase = JumpPhase.None;
            _flightVelocity = Vector3.zero;
            ApplyInputBlock();
            HideMarker();
            LandedAt = Time.time;
            _lastGrounded = _landing;
            _hasGrounded = true;
            // S3:信標的使用次數在著地時才算
            if (_target.Kind == SuperJumpTargetKind.Beakon && _target.Beakon != null && _target.Beakon.IsAlive)
            {
                _target.Beakon.OnJumpLanded(_inkling);
            }
            PlaySound(Config.SuperJumpLandSound, _landing, 1f);
            if (InkSplashFxPool.Instance != null)
            {
                Color ink = InkColor;
                for (int i = 0; i < 3; i++)
                {
                    Vector3 offset = new Vector3(Mathf.Cos(i * 2.1f), 0f, Mathf.Sin(i * 2.1f)) * 0.5f;
                    InkSplashFxPool.Instance.Spawn(_landing + offset + Vector3.up * 0.05f, Vector3.up, ink);
                }
            }
            JumpsCompleted++;
            LandCount++;
            Landed?.Invoke(this);
        }

        private void Cancel(string reason)
        {
            bool wasFlying = _phase == JumpPhase.Flight;
            _phase = JumpPhase.None;
            LastRefusal = reason;
            _flightVelocity = Vector3.zero;
            if (wasFlying)
            {
                if (_controller != null && !IsDead)
                {
                    _controller.enabled = true;
                }
                if (_health != null)
                {
                    _health.Invulnerable = false;
                }
            }
            ApplyInputBlock();
            HideMarker();
            CancelCount++;
            Cancelled?.Invoke(this);
        }

        // ── 落點 ──

        private Vector3 ResolveLanding(in SuperJumpTarget target)
        {
            float clearance = Config.LandingClearance;
            Vector3 from = transform.position;
            switch (target.Kind)
            {
                case SuperJumpTargetKind.Teammate:
                {
                    Inkling mate = target.Teammate;
                    if (mate == null)
                    {
                        return from;
                    }
                    var other = mate.GetComponent<SuperJump>();
                    if (other != null && other.IsBusy)
                    {
                        // 隊友正在超級跳 → 跟他同一個落點(稍微錯開)
                        return Ground(Beside(other._landing, from, clearance), other._landing);
                    }
                    Vector3 anchor = other != null ? other.LastGroundedPosition : mate.transform.position;
                    // 隊友還站在那裡 → 落在他旁邊(不跟他的碰撞膠囊重疊)
                    Vector3 mateNow = mate.transform.position;
                    bool occupied = (mateNow - anchor).sqrMagnitude < clearance * clearance;
                    return Ground(occupied ? Beside(mateNow, from, clearance) : anchor, anchor);
                }
                case SuperJumpTargetKind.Beakon:
                    return target.Beakon != null
                        ? Ground(Beside(target.Beakon.transform.position, from, clearance), target.Beakon.transform.position)
                        : _landing;
                case SuperJumpTargetKind.Bubbler:
                    return target.Bubbler != null
                        ? Ground(Beside(target.Bubbler.transform.position, from, clearance + 0.5f),
                            target.Bubbler.transform.position)
                        : _landing;
                default:
                {
                    Vector3 spawn = _life != null ? _life.SpawnPosition : from;
                    return Ground(spawn, spawn);
                }
            }
        }

        // anchor 往 from 方向錯開 distance(水平)
        private static Vector3 Beside(Vector3 anchor, Vector3 from, float distance)
        {
            Vector3 dir = from - anchor;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.back;
            return anchor + dir * distance;
        }

        // 往下找地面(找不到或落差太大 → 用 fallback 本身,S3 落點一定是站得住的點)
        private static Vector3 Ground(Vector3 point, Vector3 fallback)
        {
            if (Physics.Raycast(point + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 4.5f, CombatMasks.Terrain,
                    QueryTriggerInteraction.Ignore))
            {
                return hit.point + Vector3.up * 0.05f;
            }
            if (Physics.Raycast(fallback + Vector3.up * 2f, Vector3.down, out hit, 4.5f, CombatMasks.Terrain,
                    QueryTriggerInteraction.Ignore))
            {
                return hit.point + Vector3.up * 0.05f;
            }
            return fallback;
        }

        // ── 標記、輸入、鏡頭 ──

        private void ShowMarker()
        {
            if (_markerHidden)
            {
                HideMarker();
                return;
            }
            if (_marker == null && Config.MarkerPrefab != null)
            {
                _marker = Instantiate(Config.MarkerPrefab);
                _marker.name = "SuperJumpMarker_" + (_inkling != null ? _inkling.DisplayName : name);
            }
            if (_marker != null)
            {
                _marker.Show(_inkling, _landing);
            }
        }

        private void HideMarker()
        {
            if (_marker != null)
            {
                _marker.Hide();
            }
        }

        private void ApplyInputBlock()
        {
            if (_router == null)
            {
                return;
            }
            InputBlock block = InputBlock.None;
            if (_phase == JumpPhase.Charging)
            {
                block = InputBlock.Actions;
            }
            else if (_phase == JumpPhase.Flight)
            {
                // S3:落地前一點點就能開槍(副武器、大招要落地後)
                block = InFireWindow ? InputBlock.Actions & ~InputBlock.Attack : InputBlock.Actions;
            }
            _router.ActionBlock = block;
        }

        // 本機玩家飛行中:鏡頭轉向落點、上升段慢慢俯視,下降段看著落點(S3 超級跳鏡頭)
        private void SteerCamera(float t, float deltaTime)
        {
            if (_inkling == null || !_inkling.IsLocalPlayer || !(_inkling.AimView is ThirdPersonCameraRig rig))
            {
                return;
            }
            float targetPitch = Mathf.Lerp(20f, 50f, Mathf.SmoothStep(0f, 1f, t));
            _cameraPitch = Mathf.MoveTowards(_cameraPitch, targetPitch, 60f * deltaTime);
            float yaw = Mathf.MoveTowardsAngle(rig.Yaw, _landingYaw, 180f * deltaTime);
            rig.SetAngles(yaw, _cameraPitch);
        }

        private Color InkColor => TeamPalette.ColorOf(_inkling != null ? _inkling.Team : Team.Alpha);

        private void PlaySound(string id, Vector3 position, float volume)
        {
            if (string.IsNullOrEmpty(id))
            {
                return;
            }
            GameAudio.PlayFrom(_inkling != null && _inkling.IsLocalPlayer, id, position, volume, 0.03f);
        }
    }
}

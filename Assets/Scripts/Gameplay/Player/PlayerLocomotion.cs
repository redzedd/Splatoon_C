using SplatoonC.Core;
using SplatoonC.Core.Locomotion;
using SplatoonC.Gameplay.Painting;
using UnityEngine;

namespace SplatoonC.Gameplay.Player
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerLocomotion : MonoBehaviour
    {
        private enum ClimbPhase
        {
            None,
            Climbing,
            Mantle,
        }

        [SerializeField, Tooltip("角色移動設定資產(Assets/Data/PlayerLocomotionConfig)")]
        private PlayerLocomotionConfig _config;

        [SerializeField, Tooltip("輸入來源;留空自動抓同物件上的 PlayerInputRouter")]
        private PlayerInputRouter _input;

        [SerializeField, Tooltip("轉向用的視覺根(只轉這個,不轉含 CharacterController 的根)")]
        private Transform _visualRoot;

        [SerializeField, Tooltip("爬牆探測圖層(場景接線時排除 Player)")]
        private LayerMask _climbMask = ~0;

        private CharacterController _controller;
        private Inkling _inkling;
        private SplatoonC.Gameplay.Combat.WeaponLoadout _loadout;
        private float _weightRunMultiplier = 1f;
        private float _weightSwimMultiplier = 1f;
        private MotionState _state;
        private SquidController _squid;
        private ClimbPhase _climbPhase;
        private Vector3 _climbWallNormal;
        private float _mantleEndTime;
        // 潛水跳躍:起跳當下鎖住水平速度,整段滯空維持原方向與原潛水速度
        private bool _squidJumpMomentum;
        // 外力(爆炸擊退等)的水平速度(m/s):疊在求解器位移之上,地面/空中各自減速(KnockbackModel)
        private Vector3 _externalVelocity;
        // A4 烏賊動作(翻滾/牆滾/衝牆)與超級跳:Awake 自動掛上(不改 prefab)
        private SquidMoves _moves;
        private SuperJump _superJump;
        // 牆滾/衝牆越頂離牆後短暫不重新貼牆(否則離牆第一幀就被探測拉回牆上)
        private float _noClimbUntil;
        // 翻滾/牆滾/衝牆越頂的固定弧線:整段不吃「放開跳躍鍵 → 重力加大」(S3 翻滾滯空固定;衝牆是放開跳才衝出去的)
        private bool _fixedArc;

        public bool IsClimbing => _climbPhase != ClimbPhase.None;

        // 只有「貼在自家墨牆上爬」才算泡在墨裡;翻越階段是躍出牆面,該現形。
        public bool IsInsideInkedWall => _climbPhase == ClimbPhase.Climbing;

        public Vector3 ClimbWallNormal => _climbWallNormal;

        // 目前殘留的外力水平速度(m/s;擊退)
        public Vector3 ExternalVelocity => _externalVelocity;

        // 求解器的水平速度(m/s;意圖速度,撞牆時可能大於實際位移)——翻滾判定與測試讀
        public Vector3 HorizontalVelocity => _state.HorizontalVelocity;
        public float VerticalVelocity => _state.VerticalVelocity;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _state = MotionState.CreateInitial();
            if (_config == null)
            {
                Debug.LogError("PlayerLocomotion:缺少移動設定資產,角色不會動", this);
            }
            if (_input == null)
            {
                _input = GetComponent<PlayerInputRouter>();
            }
            _inkling = GetComponent<Inkling>();
            _loadout = GetComponent<SplatoonC.Gameplay.Combat.WeaponLoadout>();
            _squid = GetComponent<SquidController>();
            _moves = SquidMoves.For(this);
            _superJump = SuperJump.For(this);
            if (_config != null)
            {
                Modifiers.GearSwimSpeedAp = _config.SwimSpeedUpAp;
            }
        }

        private void Update()
        {
            if (_config == null || _input == null)
            {
                return;
            }
            // 超級跳飛行中:角色由 SuperJump 沿弧線搬運(CharacterController 已關、穿牆、無敵),這裡完全不動
            if (_superJump != null && _superJump.InFlight)
            {
                return;
            }

            // 移動基準 = 視線 yaw(玩家 = 相機,bot = BotAimView)
            IAimView view = _inkling != null ? _inkling.AimView : null;
            float cameraYaw = view != null ? view.Yaw : 0f;
            // 跳躍鍵每幀只讀一次(意圖來源的脈衝讀了就清):牆上的衝牆/牆滾、潛游翻滾、雙槍翻滾、一般起跳共用這一個
            bool jumpPressed = _input.JumpPressedThisFrame;

            // 爬牆:烏賊態 + 前方自家墨牆 → 接管本幀位移(重力與水平慣性歸零,離牆不滑步)。
            if (TryClimb(_input.MoveInput, cameraYaw, ref jumpPressed))
            {
                _fixedArc = false;
                _state.VerticalVelocity = 0f;
                _state.HorizontalVelocity = Vector3.zero;
                _squidJumpMomentum = false;
                _externalVelocity = Vector3.zero;
                return;
            }

            float speedMultiplier = _squid != null ? _squid.CurrentSpeedMultiplier : 1f;
            bool swimming = _squid != null && _squid.IsSquid && _squid.IsInOwnInk;
            bool humanoid = _squid == null || !_squid.IsSquid;
            // 重量級別(S3:輕 跑 +8.3% 游 +5%、重 跑 −8.3% 游 −10%)
            speedMultiplier *= swimming ? _weightSwimMultiplier : _weightRunMultiplier;
            SplatoonC.Gameplay.Combat.MainWeapon main = _loadout != null ? _loadout.Main
                : GetComponent<SplatoonC.Gameplay.Combat.MainWeapon>();
            // S3 狀態速度:投擲姿勢固定 4.32、開火時用武器自己的走速(Splattershot 4.32),都是人形才有
            bool throwStance = humanoid && _input.SubWeaponHeld;
            bool firing = humanoid && !throwStance && main != null && main.IsAttacking;
            float baseSpeed = Mathf.Max(0.01f, _config.ToSettings().MoveSpeed);
            if (throwStance)
            {
                speedMultiplier *= Mathf.Min(1f, _config.ThrowStanceSpeed / baseSpeed);
            }
            else if (firing)
            {
                // S3:開火走速就是武器自己的 MoveSpeed(只受 Run Speed Up 影響),不吃重量級別的跑速加成
                //(sendou shootingRunSpeed;輕量級 Jr./N-ZAP 開火仍是 4.56 / 5.28,不是 ×1.083)
                // 滾筒推滾等比跑還快的攻擊移動(AttackMoveSpeedCanExceedRun)不夾上限
                float firingRatio = main.MoveSpeedWhileFiring / (baseSpeed * _weightRunMultiplier);
                speedMultiplier *= main.AttackMoveSpeedCanExceedRun ? firingRatio : Mathf.Min(1f, firingRatio);
            }
            bool onEnemyInk = _squid != null && _squid.OnEnemyInk;
            // 暫時效果:毒霧減速、能量站 RSU/SSU、墨抗的敵墨走速(InklingModifiers;無效果 = 1)
            speedMultiplier *= Modifiers.MoveSpeedFactor(swimming, humanoid, firing, throwStance, onEnemyInk);

            // 主武器接管移動(ILocomotionOverride;雙槍閃避翻滾):跳躍鍵先讓它攔截(開火中推方向 + 跳 = 翻滾),
            // 接管期間水平速度由它指定(翻滾位移、硬直定身),不吃搖桿。沒實作的主武器 → 原路徑不變。
            // 啟動中的大招實作 ILocomotionOverride 時優先(Crab Tank 蟹步/滾球、Ultra Stamp 衝鋒)
            bool grounded = _controller.isGrounded;
            SplatoonC.Gameplay.Combat.SpecialWeapon activeSpecial = ActiveSpecial;
            ILocomotionOverride movementOverride = activeSpecial as ILocomotionOverride;
            if (movementOverride == null && humanoid && main != null && main.isActiveAndEnabled)
            {
                movementOverride = main as ILocomotionOverride;
            }
            if (jumpPressed && movementOverride != null
                && movementOverride.InterceptJump(_input.MoveInput, cameraYaw, grounded))
            {
                jumpPressed = false;
            }
            // 主武器的起跳高度倍率(狙擊槍滿蓄 × 0.405;人形才有,其他武器 1)
            float weaponJumpFactor = humanoid && main != null && main.isActiveAndEnabled ? main.JumpHeightFactor : 1f;
            // S3 Squid Roll:潛游(烏賊態、自家墨地面)中按跳 → 速度與搖桿夾角條件成立就翻滾,不是一般的潛水跳
            float swimTargetSpeed = _config.ToSettings().MoveSpeed * speedMultiplier;
            if (jumpPressed && swimming && grounded && _moves != null
                && _moves.TryStartRoll(_input.MoveInput, cameraYaw, _state.HorizontalVelocity, swimTargetSpeed,
                    _config.ToSettings().Gravity, out Vector3 rollHorizontal, out float rollVertical))
            {
                jumpPressed = false;
                _state.HorizontalVelocity = rollHorizontal;
                _state.VerticalVelocity = rollVertical;
                // 作廢土狼時間:翻滾中再按跳不會變成二段跳
                _state.LastGroundedTime = float.NegativeInfinity;
                _squidJumpMomentum = false;
                _fixedArc = true;
            }
            Vector3 controlledVelocity = Vector3.zero;
            float forcedVertical = float.NaN;
            bool controlled = movementOverride != null && movementOverride.TryGetControlledMotion(
                Time.time, Time.deltaTime, grounded, out controlledVelocity, out forcedVertical);
            // 翻滾/牆滾的空中段:水平速度由 SquidMoves 決定(往搖桿方向加速;人形取消後保留動量),重力照常
            if (!controlled && _moves != null && _moves.TryGetAirMotion(Time.deltaTime, grounded, IsClimbing,
                    _input.MoveInput, cameraYaw, _state.HorizontalVelocity, out Vector3 rollMotion))
            {
                controlled = true;
                controlledVelocity = rollMotion;
            }

            MotionStep step = CharacterMotionSolver.Step(
                ref _state,
                controlled ? Vector2.zero : _input.MoveInput,
                cameraYaw,
                grounded,
                jumpPressed,
                _config.ToSettings(),
                Time.time,
                Time.deltaTime,
                speedMultiplier,
                _squidJumpMomentum,
                SquidJumpMomentumDecayRate(),
                _input.JumpHeld || _fixedArc,
                Modifiers.JumpHeightFactor(onEnemyInk, _config.EnemyInkJumpMultiplier) * weaponJumpFactor,
                throwStance || firing ? _config.AttackAccelMultiplier : 1f);

            Vector3 displacement = step.Displacement;
            if (controlled)
            {
                // 求解器照算重力/著地;水平改用接管速度(存回狀態,接管結束時從這個速度接續)
                _state.HorizontalVelocity = controlledVelocity;
                if (!float.IsNaN(forcedVertical))
                {
                    _state.VerticalVelocity = forcedVertical;
                }
                displacement = (controlledVelocity + Vector3.up * _state.VerticalVelocity) * Time.deltaTime;
            }
            _controller.Move(displacement + ConsumeExternalDisplacement(Time.deltaTime));
            if (_fixedArc && _controller.isGrounded && _state.VerticalVelocity <= 0f)
            {
                _fixedArc = false;
            }
            if (_moves != null)
            {
                // 翻滾判定的速度歷史:只記「烏賊態踩在自家墨地面上」的求解器速度
                _moves.Observe(_state.HorizontalVelocity, swimming && _controller.isGrounded);
            }

            // 潛水中起跳 → 鎖住這一幀算出的水平速度,直到落地為止都不受輸入與倍率影響。
            // 判定放在 Step 之後:起跳當幀的水平速度已是潛水速度,鎖從下一幀生效才連續。
            if (step.Jumped && swimming)
            {
                _squidJumpMomentum = true;
            }
            else if (_controller.isGrounded && !step.Jumped)
            {
                _squidJumpMomentum = false;
            }

            // 開火或投擲瞄準中一律面向瞄準方向(槍口/拋物線才對得上準星);否則面向移動方向。
            // 主武器動作進行中(滾筒點按後的舉起/甩出)即使已放開按鍵也面向瞄準方向
            bool aiming = (_input.AttackHeld || _input.SubWeaponHeld || firing) && (_squid == null || !_squid.IsSquid);
            // 翻滾/牆滾中(烏賊態):面向翻滾的行進方向(S3 以新方向跳出墨面),轉得比平常快
            Vector3 rollVelocity = _state.HorizontalVelocity;
            bool rollFacing = !aiming && _moves != null && _moves.IsRolling && rollVelocity.sqrMagnitude > 0.25f;
            // 大招接管面向(IFacingOverride:Crab Tank 車身慢慢轉、Ultra Stamp 衝鋒方向)優先
            if (_visualRoot != null && activeSpecial is IFacingOverride facing
                && facing.TryGetFacingYaw(out float facingYaw))
            {
                _visualRoot.rotation = Quaternion.Euler(0f, facingYaw, 0f);
            }
            else if (_visualRoot != null && (aiming || step.HasMoveInput || rollFacing))
            {
                float targetYaw = aiming ? cameraYaw
                    : rollFacing ? Mathf.Atan2(rollVelocity.x, rollVelocity.z) * Mathf.Rad2Deg
                    : step.DesiredYawDeg;
                float currentYaw = _visualRoot.eulerAngles.y;
                float nextYaw = Mathf.MoveTowardsAngle(
                    currentYaw, targetYaw, _config.TurnSpeedDegPerSec * (rollFacing ? 3f : 1f) * Time.deltaTime);
                _visualRoot.rotation = Quaternion.Euler(0f, nextYaw, 0f);
            }
        }

        private Team MyTeam => _inkling != null ? _inkling.Team : Team.Alpha;

        private PlayerSpecialGauge _specialGauge;
        private bool _specialGaugeSearched;

        // 啟動中的大招(接管移動/面向用;沒有 = null)
        private SplatoonC.Gameplay.Combat.SpecialWeapon ActiveSpecial
        {
            get
            {
                if (!_specialGaugeSearched)
                {
                    _specialGaugeSearched = true;
                    _specialGauge = GetComponent<PlayerSpecialGauge>();
                }
                return _specialGauge != null ? _specialGauge.ActiveSpecial : null;
            }
        }

        private InklingModifiers _modifiers;

        // 暫時效果(移動速度、起跳高度、牆上游速倍率);第一次用時取得,不依賴 Awake 順序
        private InklingModifiers Modifiers => _modifiers != null ? _modifiers : (_modifiers = InklingModifiers.For(this));

        // 武器組合的重量級別(WeaponLoadout 呼叫)
        public void SetWeight(SplatoonC.Gameplay.Combat.WeightClass weight)
        {
            _weightRunMultiplier = SplatoonC.Gameplay.Combat.WeightClassUtil.RunMultiplier(weight);
            _weightSwimMultiplier = SplatoonC.Gameplay.Combat.WeightClassUtil.SwimMultiplier(weight);
        }

        // 瞬移/重生後清掉殘留速度與爬牆狀態
        public void ResetMotion()
        {
            _state = MotionState.CreateInitial();
            _climbPhase = ClimbPhase.None;
            _squidJumpMomentum = false;
            _externalVelocity = Vector3.zero;
            _noClimbUntil = 0f;
            _fixedArc = false;
            if (_moves != null)
            {
                _moves.ResetState();
            }
            // 接管移動的主武器(雙槍翻滾)放棄進行中的動作:瞬移後不該帶著翻滾位移
            SplatoonC.Gameplay.Combat.MainWeapon main = _loadout != null ? _loadout.Main
                : GetComponent<SplatoonC.Gameplay.Combat.MainWeapon>();
            if (main is ILocomotionOverride movementOverride)
            {
                movementOverride.OnLocomotionReset();
            }
            if (ActiveSpecial is ILocomotionOverride specialOverride)
            {
                specialOverride.OnLocomotionReset();
            }
        }

        // 外加速度變化(m/s;爆炸擊退 Explosion、滾筒接觸等):水平分量進外力速度,之後在地面以 S3 基礎加速度、
        // 空中以較小減速度衰減(KnockbackModel);垂直分量直接加進求解器的垂直速度,重力照常作用(著地時先清掉下壓速度)。
        // 求解器本身維持純邏輯,外力在這裡疊加。
        public void AddImpulse(Vector3 velocityChange)
        {
            _externalVelocity.x += velocityChange.x;
            _externalVelocity.z += velocityChange.z;
            if (velocityChange.y != 0f)
            {
                if (_state.VerticalVelocity < 0f && _controller != null && _controller.isGrounded)
                {
                    _state.VerticalVelocity = 0f;
                }
                _state.VerticalVelocity += velocityChange.y;
            }
            // 被炸離牆面:爬牆狀態交還給下一幀重新判定
            if (_climbPhase == ClimbPhase.Climbing && new Vector2(velocityChange.x, velocityChange.z).sqrMagnitude > 1f)
            {
                _climbPhase = ClimbPhase.None;
            }
        }

        // 本幀外力位移,並衰減外力速度(地面 / 空中)
        private Vector3 ConsumeExternalDisplacement(float deltaTime)
        {
            if (_externalVelocity.x == 0f && _externalVelocity.z == 0f)
            {
                return Vector3.zero;
            }
            Vector3 displacement = _externalVelocity * deltaTime;
            _externalVelocity = SplatoonC.Core.Combat.KnockbackModel.DecayHorizontal(
                _externalVelocity, _controller.isGrounded, deltaTime);
            return displacement;
        }

        // 潛水跳躍的水平速度衰減率:與「離墨緩降」共用同一段秒數,
        // 從潛水速度滑落到平時速度所需的加速度。
        private float SquidJumpMomentumDecayRate()
        {
            float duration = _config.InkExitSpeedDecayDuration;
            if (duration <= 0f)
            {
                return 0f;
            }
            LocomotionSettings settings = _config.ToSettings();
            // 常駐游速裝備讓潛游更快:衰減率跟著放大,離墨後仍在同樣秒數內回到跑速
            float swimSpeed = settings.MoveSpeed * _config.SquidInkSpeedMultiplier * Modifiers.GearSwimRatio;
            return (swimSpeed - settings.MoveSpeed) / duration;
        }

        // 回傳 true = 本幀位移已由爬牆/翻越接管。
        // A4:貼在自家墨牆上時先問 SquidMoves —— 按跳 + 搖桿離牆 = 牆滾(離牆,回傳 false 交給一般路徑以
        // _state 的初速飛出去,jumpPressed 被吃掉);按住跳 = 衝牆蓄力(定在牆上);放開 = 衝牆上衝(強制往上、速度隨蓄力),
        // 上衝到頂 → 越頂彈起(回傳 false,以潛水跳躍的水平動量鎖越過牆緣)。
        private bool TryClimb(Vector2 moveInput, float cameraYaw, ref bool jumpPressed)
        {
            if (_climbPhase == ClimbPhase.Mantle)
            {
                if (Time.time < _mantleEndTime)
                {
                    // 到頂翻越:斜上位移把角色送過牆緣(上分量大避免卡邊)。
                    Vector3 mantleDir = Vector3.up * 1.4f - _climbWallNormal * 0.8f;
                    _controller.Move(mantleDir * (_config.MantleSpeed * Time.deltaTime));
                    return true;
                }
                _climbPhase = ClimbPhase.None;
                return false;
            }

            if (_squid == null || !_squid.IsSquid)
            {
                if (_climbPhase != ClimbPhase.None && _moves != null)
                {
                    _moves.OnLeftWall();
                }
                _climbPhase = ClimbPhase.None;
                return false;
            }
            if (Time.time < _noClimbUntil)
            {
                return false;
            }

            Vector3 probeDirection;
            if (_climbPhase == ClimbPhase.Climbing)
            {
                probeDirection = -_climbWallNormal;
            }
            else
            {
                Vector3 worldDir = Quaternion.Euler(0f, cameraYaw, 0f)
                    * new Vector3(moveInput.x, 0f, moveInput.y);
                if (worldDir.sqrMagnitude < 0.01f)
                {
                    return false;
                }
                probeDirection = worldDir.normalized;
            }

            Vector3 chest = transform.position + Vector3.up * 1f;
            if (Physics.Raycast(chest, probeDirection, out RaycastHit hit,
                    _config.ClimbProbeDistance, _climbMask, QueryTriggerInteraction.Ignore))
            {
                var surface = hit.collider.GetComponent<PaintableSurface>();
                if (surface != null && surface.SampleOwnership(hit.point, hit.normal) == (byte)MyTeam)
                {
                    _climbWallNormal = hit.normal;
                    _climbPhase = ClimbPhase.Climbing;
                    float climbSpeed = _config.ClimbSpeed * Modifiers.ClimbSpeedFactor;
                    float climbInput = moveInput.y;
                    if (_moves != null)
                    {
                        SquidMoves.WallAction action = WallAction(moveInput, cameraYaw, jumpPressed, climbSpeed,
                            out float surgeSpeed);
                        if (action == SquidMoves.WallAction.WallRoll)
                        {
                            jumpPressed = false;
                            return false;
                        }
                        if (action == SquidMoves.WallAction.Charging)
                        {
                            // 衝牆蓄力:定在牆上(只保留貼牆分量)
                            climbInput = 0f;
                        }
                        else if (action == SquidMoves.WallAction.Surging)
                        {
                            // 衝牆上衝:不看搖桿,以蓄力決定的速度往上
                            climbInput = 1f;
                            climbSpeed = surgeSpeed;
                        }
                        jumpPressed = false;
                    }
                    // 面向牆時前推 = 上爬、後拉 = 下滑
                    _controller.Move(WallClimbSolver.Step(
                        climbInput, _climbWallNormal, climbSpeed, _config.WallStickSpeed, Time.deltaTime));
                    return true;
                }
            }

            if (_climbPhase == ClimbPhase.Climbing)
            {
                // 爬升中探測落空 = 到頂:衝牆上衝中 → 越頂彈起(S3 衝過牆頂上方 3–4 u);否則進入一般翻越
                if (_moves != null && _moves.TryConsumeSurgeTop(_config.ToSettings().Gravity, 1f,
                        out float hopSpeed, out float lipPush))
                {
                    _climbPhase = ClimbPhase.None;
                    _noClimbUntil = Time.time + 0.35f;
                    _state = MotionState.CreateInitial();
                    _state.VerticalVelocity = hopSpeed;
                    _state.HorizontalVelocity = -_climbWallNormal * lipPush;
                    _squidJumpMomentum = true;
                    _fixedArc = true;
                    jumpPressed = false;
                    return false;
                }
                _climbPhase = ClimbPhase.Mantle;
                _mantleEndTime = Time.time + _config.MantleDuration;
                return true;
            }
            return false;
        }

        // 牆上動作(SquidMoves.StepWall);牆滾時直接把離牆初速寫進求解器狀態並離開牆面
        private SquidMoves.WallAction WallAction(Vector2 moveInput, float cameraYaw, bool jumpPressed, float climbSpeed,
            out float surgeSpeed)
        {
            LocomotionSettings settings = _config.ToSettings();
            float swimSpeed = settings.MoveSpeed * _config.SquidInkSpeedMultiplier * _weightSwimMultiplier;
            SquidMoves.WallAction action = _moves.StepWall(jumpPressed, _input.JumpHeld, moveInput, cameraYaw,
                _climbWallNormal, swimSpeed, climbSpeed, settings.Gravity, out surgeSpeed,
                out Vector3 rollHorizontal, out float rollVertical);
            if (action == SquidMoves.WallAction.WallRoll)
            {
                _climbPhase = ClimbPhase.None;
                _noClimbUntil = Time.time + 0.3f;
                _state = MotionState.CreateInitial();
                _state.HorizontalVelocity = rollHorizontal;
                _state.VerticalVelocity = rollVertical;
                _squidJumpMomentum = false;
                _fixedArc = true;
            }
            return action;
        }
    }
}

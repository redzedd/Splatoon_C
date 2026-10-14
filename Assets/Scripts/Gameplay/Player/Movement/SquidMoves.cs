using SplatoonC.Core.Locomotion;
using SplatoonC.Gameplay.Audio;
using UnityEngine;

namespace SplatoonC.Gameplay.Player
{
    // S3 烏賊態的新動作(DESIGN_REFERENCE §2.7;research/01 §9–§11):
    //   Squid Roll:潛游中游速 ≥ 8.7 m/s(或掉到門檻以下還不到 10F)、搖桿與實際速度夾角 ≥ 60° 時按跳 →
    //     以新方向跳出墨面(水平速度 = 游速,滯空固定),空中往搖桿方向加速;90F 內連滾每次 × 0.85;
    //     護甲 30 HP / 18F,變回人形立刻結束(按開火取消翻滾 = 人形出招並保留翻滾動量)。
    //   Wall Roll:貼在自家墨牆上、搖桿在牆面法線(離牆)45° 內按跳 → 往牆外彈出;與翻滾共用連滾懲罰;護甲 45F。
    //   Squid Surge:貼牆按住跳蓄力(45F 滿蓄,眼睛閃光 + 音效;定在牆上),放開 → 沿牆上衝(零蓄 = 爬牆速度、
    //     滿蓄 22.5 m/s [EST])越過牆頂彈起(0.6 → 3.5 m [EST]);可部分蓄力;搖桿往牆外推取消;滿蓄護甲 30 HP / 45F。
    // 本元件只管「狀態與數值」;位移仍由 PlayerLocomotion 做(它在對應時機呼叫這裡的掛勾)。
    // 由 PlayerLocomotion.Awake 自動掛上(不改 prefab)。雙槍的閃避翻滾(ILocomotionOverride,人形)與這裡(烏賊態)互不重疊。
    [DisallowMultipleComponent]
    public sealed class SquidMoves : MonoBehaviour
    {
        public enum WallAction : byte
        {
            // 一般爬牆
            None = 0,
            // 衝牆蓄力中(定在牆上)
            Charging = 1,
            // 衝牆上衝中(沿牆往上,速度 SurgeSpeed)
            Surging = 2,
            // 這一幀發動牆滾(離牆)
            WallRoll = 3,
        }

        private enum SurgePhase : byte
        {
            None,
            Charging,
            Surging,
        }

        private Inkling _inkling;
        private InklingHealth _health;
        private SquidController _squid;
        private MovementConfig _config;

        // 速度歷史(翻滾條件)
        private float _lastFastTime = float.NegativeInfinity;
        private Vector3 _lastFastDir = Vector3.forward;
        private Vector3 _lastVelocity;

        // 翻滾(地面翻滾與牆滾共用空中段)
        private bool _rollActive;
        private bool _wallRoll;
        private int _rollAirFrames;
        private float _rollSpeed;
        private Vector3 _rollDir;
        private float _rollStartTime = float.NegativeInfinity;
        private float _lastRollTime = float.NegativeInfinity;
        private int _chainIndex;

        // 衝牆
        private SurgePhase _surge;
        private float _chargeStart;
        private float _charge01;
        private bool _readyCuePlayed;
        private float _surgeSpeed;
        private float _surgeLaunchTime = float.NegativeInfinity;

        // 本元件給的護甲(到期時間);變回人形時只清自己給的
        private float _armorUntil = float.NegativeInfinity;

        // ── 狀態(測試、視覺、音效)──
        public bool IsRolling => _rollActive;
        public bool IsWallRoll => _rollActive && _wallRoll;
        public float RollElapsed => _rollActive ? Time.time - _rollStartTime : -1f;
        public Vector3 RollDirection => _rollDir;
        public int ChainIndex => _chainIndex;
        public float LastRollSpeed { get; private set; }
        public float LastRollTime => _lastRollTime;
        public int RollCount { get; private set; }
        public int WallRollCount { get; private set; }
        public int SurgeCount { get; private set; }

        public bool IsChargingSurge => _surge == SurgePhase.Charging;
        public bool IsSurging => _surge == SurgePhase.Surging;
        // 蓄力中 = 目前蓄力比例;上衝中 = 放開時的比例
        public float SurgeCharge01 => _surge != SurgePhase.None ? _charge01 : 0f;
        public bool SurgeFullyCharged => _surge == SurgePhase.Charging && _charge01 >= 1f;
        public float LastSurgeCharge01 { get; private set; }
        public float LastSurgeSpeed { get; private set; }
        public float LastSurgeHop { get; private set; }
        public float LastSurgeLaunchTime => _surgeLaunchTime;

        // 本元件給的護甲還在(翻滾 18F / 牆滾・衝牆 45F)
        public bool HasMoveArmor => _health != null && _health.HasArmor && Mathf.Approximately(_health.ArmorUntil, _armorUntil);
        public float MoveArmorUntil => _armorUntil;
        public int ArmorGrantCount { get; private set; }

        // 最後一次「游速 ≥ 翻滾門檻」到現在的秒數
        public float SecondsSinceFast => Time.time - _lastFastTime;

        private MovementConfig Config => _config != null ? _config : (_config = MovementConfig.Current);
        private SquidMoveParams P => Config.SquidMoves;

        // 取得(沒有就掛上):PlayerLocomotion.Awake 呼叫
        public static SquidMoves For(Component component)
        {
            if (component == null)
            {
                return null;
            }
            var existing = component.GetComponent<SquidMoves>();
            return existing != null ? existing : component.gameObject.AddComponent<SquidMoves>();
        }

        private void Awake()
        {
            _inkling = GetComponent<Inkling>();
            _health = GetComponent<InklingHealth>();
            _squid = GetComponent<SquidController>();
        }

        private void Update()
        {
            // S3:翻滾/衝牆的護甲在變回人形的瞬間結束(開火取消翻滾時不能帶著護甲打)
            if (_armorUntil > Time.time && _squid != null && !_squid.IsSquid && HasMoveArmor)
            {
                _health.ClearArmor();
                _armorUntil = float.NegativeInfinity;
            }
        }

        // 瞬移/重生:放棄進行中的動作
        public void ResetState()
        {
            _rollActive = false;
            _wallRoll = false;
            _surge = SurgePhase.None;
            _charge01 = 0f;
            _lastFastTime = float.NegativeInfinity;
            _lastVelocity = Vector3.zero;
        }

        // ── 翻滾 ──

        // 每幀(PlayerLocomotion 求解後):記錄潛游速度歷史。swimmingGrounded = 烏賊態、踩在自家墨地面上
        public void Observe(Vector3 horizontalVelocity, bool swimmingGrounded)
        {
            horizontalVelocity.y = 0f;
            _lastVelocity = horizontalVelocity;
            if (swimmingGrounded && horizontalVelocity.magnitude >= P.RollMinSpeed)
            {
                _lastFastTime = Time.time;
                _lastFastDir = horizontalVelocity.normalized;
            }
        }

        // 潛游中按跳:條件成立就翻滾(回傳 true,呼叫端不再當一般跳)。
        // swimSpeed = 目前的游速(公尺/秒,含重量級與暫時效果);gravity = 移動設定的重力(負值)
        public bool TryStartRoll(Vector2 moveInput, float cameraYaw, Vector3 horizontalVelocity, float swimSpeed,
            float gravity, out Vector3 launchHorizontal, out float launchVertical)
        {
            launchHorizontal = Vector3.zero;
            launchVertical = 0f;
            SquidMoveParams p = P;
            Vector3 stick = StickToWorld(moveInput, cameraYaw);
            float stickMagnitude = Mathf.Min(1f, moveInput.magnitude);
            horizontalVelocity.y = 0f;
            float speed = horizontalVelocity.magnitude;
            Vector3 velocityDir = speed > 0.5f ? horizontalVelocity / speed : _lastFastDir;
            if (!SquidMoveModel.CanRoll(p, speed, Time.time - _lastFastTime, velocityDir, stick, stickMagnitude))
            {
                return false;
            }
            Vector3 dir = stick.sqrMagnitude > 1e-6f ? stick.normalized : -velocityDir;
            StartRoll(dir, swimSpeed, 1f, false);
            launchHorizontal = _rollDir * _rollSpeed;
            launchVertical = SquidMoveModel.LaunchSpeedForAirtime(p.RollAirFrames, gravity);
            GrantArmor(p.RollArmorFrames / 60f);
            RollCount++;
            return true;
        }

        // 翻滾/牆滾空中段的水平速度(回傳 true = 本幀水平速度由這裡決定)。落地或貼上牆即結束。
        // 烏賊態:往搖桿方向以 RollAirAccel 加速(速度上限 = 翻滾速度),放開搖桿自然減速;
        // 人形(開火取消翻滾):保留翻滾動量、不再轉向。
        public bool TryGetAirMotion(float deltaTime, bool grounded, bool climbing, Vector2 moveInput, float cameraYaw,
            Vector3 currentHorizontal, out Vector3 horizontal)
        {
            horizontal = currentHorizontal;
            if (!_rollActive)
            {
                return false;
            }
            _rollAirFrames++;
            if (climbing || (grounded && _rollAirFrames > 2))
            {
                _rollActive = false;
                _wallRoll = false;
                return false;
            }
            horizontal.y = 0f;
            bool squid = _squid == null || _squid.IsSquid;
            if (squid)
            {
                SquidMoveParams p = P;
                Vector3 stick = StickToWorld(moveInput, cameraYaw);
                Vector3 target = stick.sqrMagnitude > p.MinStick * p.MinStick
                    ? Vector3.ClampMagnitude(stick, 1f) * _rollSpeed
                    : Vector3.zero;
                float accel = stick.sqrMagnitude > p.MinStick * p.MinStick ? p.RollAirAccel : p.RollAirAccel * 0.5f;
                horizontal = Vector3.MoveTowards(horizontal, target, accel * deltaTime);
            }
            return true;
        }

        private void StartRoll(Vector3 dir, float baseSpeed, float speedScale, bool wall)
        {
            float now = Time.time;
            _chainIndex = SquidMoveModel.NextChainIndex(_chainIndex, now - _lastRollTime, P);
            float factor = _inkling != null ? _inkling.Modifiers.RollChainSpeedFactor : P.RollChainFactor;
            _rollSpeed = Mathf.Max(0f, baseSpeed) * speedScale * SquidMoveModel.ChainSpeedFactor(_chainIndex, factor);
            dir.y = 0f;
            _rollDir = dir.sqrMagnitude > 1e-6f ? dir.normalized : transform.forward;
            _rollActive = true;
            _wallRoll = wall;
            _rollAirFrames = 0;
            _rollStartTime = now;
            _lastRollTime = now;
            LastRollSpeed = _rollSpeed;
            PlaySound(Config.RollSound, 0.9f);
        }

        // ── 牆上(衝牆 / 牆滾)──

        // 每幀(貼在自家墨牆上爬的時候)由 PlayerLocomotion 呼叫。回傳本幀的牆上動作:
        //   Charging → 定在牆上;Surging → 以 surgeSpeed 沿牆往上;WallRoll → 以 wallRollHorizontal / wallRollVertical 離牆。
        public WallAction StepWall(bool jumpPressed, bool jumpHeld, Vector2 moveInput, float cameraYaw, Vector3 wallNormal,
            float swimSpeed, float normalClimbSpeed, float gravity, out float surgeSpeed, out Vector3 wallRollHorizontal,
            out float wallRollVertical)
        {
            surgeSpeed = normalClimbSpeed;
            wallRollHorizontal = Vector3.zero;
            wallRollVertical = 0f;
            SquidMoveParams p = P;
            Vector3 stick = StickToWorld(moveInput, cameraYaw);
            float stickMagnitude = Mathf.Min(1f, moveInput.magnitude);

            // 按跳 + 搖桿離牆 = 牆滾(上衝中也能用,S3「衝牆取消成翻滾」)
            if (jumpPressed && SquidMoveModel.IsWallRollInput(p, stick, stickMagnitude, wallNormal))
            {
                _surge = SurgePhase.None;
                _charge01 = 0f;
                StartRoll(wallNormal, swimSpeed, p.WallRollSpeedScale, true);
                wallRollHorizontal = _rollDir * _rollSpeed;
                wallRollVertical = p.WallRollUpSpeed;
                GrantArmor(p.SurgeArmorFrames / 60f);
                WallRollCount++;
                return WallAction.WallRoll;
            }

            if (_surge == SurgePhase.Surging)
            {
                if (SquidMoveModel.IsPushingAway(p, stick, stickMagnitude, wallNormal))
                {
                    _surge = SurgePhase.None;
                    return WallAction.None;
                }
                surgeSpeed = _surgeSpeed;
                return WallAction.Surging;
            }

            if (jumpPressed && _surge == SurgePhase.None)
            {
                _surge = SurgePhase.Charging;
                _chargeStart = Time.time;
                _charge01 = 0f;
                _readyCuePlayed = false;
                PlaySound(Config.SurgeChargeSound, 0.7f);
            }

            if (_surge == SurgePhase.Charging)
            {
                if (SquidMoveModel.IsPushingAway(p, stick, stickMagnitude, wallNormal))
                {
                    // S3:蓄力中搖桿往牆外推 = 取消
                    _surge = SurgePhase.None;
                    _charge01 = 0f;
                    return WallAction.None;
                }
                float chargeSeconds = _inkling != null ? _inkling.Modifiers.SquidSurgeChargeSeconds : p.SurgeChargeFrames / 60f;
                _charge01 = SquidMoveModel.SurgeCharge01(Time.time - _chargeStart, chargeSeconds);
                if (_charge01 >= 1f && !_readyCuePlayed)
                {
                    // 滿蓄:眼睛閃光 + 音效(CharacterVisual 讀 SurgeFullyCharged)
                    _readyCuePlayed = true;
                    PlaySound(Config.SurgeReadySound, 0.9f);
                }
                if (jumpHeld)
                {
                    return WallAction.Charging;
                }
                // 放開 → 上衝(可部分蓄力:速度與越頂高度隨蓄力比例)
                _surge = SurgePhase.Surging;
                _surgeSpeed = SquidMoveModel.SurgeClimbSpeed(p, _charge01, normalClimbSpeed);
                _surgeLaunchTime = Time.time;
                LastSurgeCharge01 = _charge01;
                LastSurgeSpeed = _surgeSpeed;
                LastSurgeHop = SquidMoveModel.SurgeHopHeight(p, _charge01);
                SurgeCount++;
                if (_charge01 >= 1f)
                {
                    GrantArmor(p.SurgeArmorFrames / 60f);
                }
                PlaySound(Config.SurgeSound, 1f);
                surgeSpeed = _surgeSpeed;
                return WallAction.Surging;
            }
            return WallAction.None;
        }

        // 上衝中爬到牆頂(胸口探測落空):回傳越頂彈起的垂直速度與往平台推的水平速度。
        // belowTop = 角色腳底在牆頂下方多少公尺(探測點在胸口,落空時腳底約低 1 m),彈起高度以牆頂為準。
        public bool TryConsumeSurgeTop(float gravity, float belowTop, out float verticalSpeed, out float lipPush)
        {
            verticalSpeed = 0f;
            lipPush = 0f;
            if (_surge != SurgePhase.Surging)
            {
                _surge = SurgePhase.None;
                return false;
            }
            _surge = SurgePhase.None;
            verticalSpeed = SquidMoveModel.LaunchSpeedForHeight(LastSurgeHop + Mathf.Max(0f, belowTop), gravity);
            lipPush = P.SurgeLipPush;
            return true;
        }

        // 離開牆面(變回人形、探測不到牆但不是到頂):放棄蓄力/上衝
        public void OnLeftWall()
        {
            _surge = SurgePhase.None;
            _charge01 = 0f;
        }

        // ── 共用 ──

        // 護甲(S3:30 HP;身上已有更久的護甲——例如出生護甲 235F——就不覆蓋)
        private void GrantArmor(float seconds)
        {
            if (_health == null)
            {
                return;
            }
            float until = Time.time + seconds;
            if (_health.HasArmor && _health.ArmorUntil >= until && !HasMoveArmor)
            {
                return;
            }
            _health.GrantArmor(P.MoveArmorHp, seconds);
            _armorUntil = _health.ArmorUntil;
            ArmorGrantCount++;
        }

        private static Vector3 StickToWorld(Vector2 moveInput, float cameraYaw)
        {
            Vector2 clamped = moveInput.sqrMagnitude > 1f ? moveInput.normalized : moveInput;
            return Quaternion.Euler(0f, cameraYaw, 0f) * new Vector3(clamped.x, 0f, clamped.y);
        }

        private void PlaySound(string id, float volume)
        {
            if (string.IsNullOrEmpty(id))
            {
                return;
            }
            bool local = _inkling != null && _inkling.IsLocalPlayer;
            GameAudio.PlayFrom(local, id, transform.position + Vector3.up * 0.5f, volume, 0.05f);
        }
    }
}

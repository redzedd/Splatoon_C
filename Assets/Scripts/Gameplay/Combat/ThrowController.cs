using System.Collections.Generic;
using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.CameraRig;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using Unity.Collections;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 副武器迴路:按住 SubWeapon → 鏡頭拉近 + 拋物線預覽 + 落點標記;放開 → 丟出(扣墨、上墨量鎖)。
    // 丟什麼由武器組合的副武器設定決定(SetSub);大招啟動中若接管投擲(SpecialWeapon.ThrowOverride),
    // 放開改為把原點與初速交給大招(三重龍捲風的信標)。
    // 預覽與實飛用同一個初速向量與同一個步進(SimpleBallistics),所以預覽 = 實際落點。
    // 蓄力型副武器(SubWeaponConfig.MaxChargeFrames > 0,Curling Bomb):按住起算、扣掉出手準備幀後累積蓄力
    // (SubChargeFrames / SubCharge01 給 HUD 與測試),放開時依蓄力決定墨量鎖並交給投擲物(LaunchCharged)。
    // 自訂出手(SubWeaponConfig.TryComputeLaunch 回傳 true,Curling 的低拋 + 滑行):初速與預覽路徑都由設定提供。
    // 蓄力連打(ChargeMash*,Fizzy):按住期間跳躍鍵/搖桿甩出的邊緣折抵蓄力幀(每幀兌現有上限)。
    // 達上限不能丟(BlockWhileMaxActive,Torpedo):放開時不扣墨、不上鎖,只播「使用不可」提示(SubUnavailable 給 HUD)。
    // 預設拋物線預覽也照炸彈共通規則停在敵方屏障(Splash Wall 類裝置、Big Bubbler 球罩:BombRules.CastDetonator)。
    // 瞄準不共用 InkShooter.TryComputeAim(那是步槍直飛段的重力補償):視線中心射線取 AimReach 處
    // (或先撞到的點)當目標,從投擲原點指過去再上抬 lift 角。
    public sealed class ThrowController : MonoBehaviour
    {
        // S3:兩次副武器之間至少 30F
        private const float MinInterval = 0.5f;
        // 預覽點緩衝上限(步數)
        private const int MaxPreviewPoints = 181;
        // 副武器「使用不可」時放開的提示音(新 id)
        private const string UnavailableSound = "sub_unavailable";

        [SerializeField, Tooltip("預設副武器設定(未由武器組合指定時使用)")]
        private SubWeaponConfig _bombConfig;

        [SerializeField, Tooltip("預設投擲物 prefab(設定沒有指定 prefab 時使用)")]
        private SubProjectile _bombPrefab;

        [SerializeField, Tooltip("投擲原點(Player 底下的 Muzzle)")]
        private Transform _throwOrigin;

        [SerializeField, Tooltip("拋物線 LineRenderer(Player 底下的 ThrowArc,世界座標)")]
        private LineRenderer _arc;

        [SerializeField, Tooltip("落點標記(Player 底下的 ThrowMarker,壓扁圓柱)")]
        private Transform _marker;

        [SerializeField, Tooltip("落點標記離命中面的抬升(公尺),防 z-fighting")]
        private float _markerLift = 0.03f;

        [SerializeField, Tooltip("瞄準射線圖層(排除角色)")]
        private LayerMask _aimMask = ~(1 << 8);

        [SerializeField, Tooltip("目標離投擲原點不到此距離(公尺)時改用相機方向丟:貼牆時撞牆點可能就在槍口斜上方 1m,槍口→目標會變成往天上丟(2026-09-08 實測 63° 仰角)")]
        private float _nearAimDistance = 6f;

        [SerializeField, Tooltip("副武器池預熱數")]
        private int _bombPrewarm = 4;

        private PlayerInputRouter _input;
        private CharacterController _body;
        private SquidController _squid;
        private PlayerInkTank _tank;
        private PlayerSpecialGauge _special;
        private WeaponLoadout _loadout;
        private ThirdPersonCameraRig _rig;
        private Inkling _owner;
        // 預覽點緩衝:Persistent NativeArray,SetPositions 吃 Slice,零配置
        private NativeArray<Vector3> _arcPoints;
        private bool _wasHeld;
        private bool _heldOverride;
        private float _lastThrowTime = float.NegativeInfinity;
        private float _holdStartTime;
        private bool _chargedCuePlayed;
        // 蓄力連打加速(Fizzy):尚未兌現的折抵幀、已兌現的折抵幀、上一幀的跳躍鍵/搖桿狀態(邊緣偵測)
        private float _mashBank;
        private float _mashFrames;
        private bool _mashJumpWasHeld;
        private bool _mashStickWasOut;
        // 自己丟出、仍在場上的副武器(同時存在上限用)
        private readonly List<SubProjectile> _mine = new List<SubProjectile>();

        public bool IsAiming { get; private set; }
        public Vector3 PredictedLanding { get; private set; }
        public bool HasPredictedLanding { get; private set; }
        public int ThrowCount { get; private set; }
        public int DeviceThrowCount { get; private set; }

        // 蓄力(副武器按住中;不蓄力的副武器恆為 0)
        public float SubChargeFrames { get; private set; }
        public float SubCharge01 { get; private set; }
        public bool IsChargingSub => IsAiming && !WillThrowDevice && _bombConfig != null && _bombConfig.MaxChargeFrames > 0;
        // 最近一次副武器出手的蓄力比例與墨量鎖秒數(測試用)
        public float LastThrowCharge01 { get; private set; }
        public float LastInkLockSeconds { get; private set; }
        // 連打加速已兌現的蓄力幀(Fizzy;HUD/測試)
        public float SubMashFrames => _mashFrames;
        // 「使用不可」:副武器達到同時存在上限且規定不能再丟(S3 Torpedo 還在場上)。HUD 用
        public bool SubUnavailable => _bombConfig != null && _bombConfig.BlockWhileMaxActive && _bombConfig.MaxActive > 0
                                      && CountMine() >= _bombConfig.MaxActive;
        // 因「使用不可」被拒絕的投擲次數(測試用;不扣墨、不上墨量鎖)
        public int RefusedThrowCount { get; private set; }
        public SubWeaponConfig SubConfig => _bombConfig;
        public SubProjectile SubPrefab => _bombConfig != null && _bombConfig.Prefab != null ? _bombConfig.Prefab : _bombPrefab;

        // ── 舊 API 相容(M4 AutoTest)──
        public SplatBombConfig BombConfig => _bombConfig as SplatBombConfig;
        public TripleInkstrikeConfig InkstrikeConfig => _special != null ? _special.Config : null;
        public int ActiveBombs => SharedPool.ActiveCount(SubPrefab);
        public int ActiveDevices => _special != null && _special.Special is TripleInkstrikeSpecial s ? s.ActiveDevices : 0;
        public int ActiveTornadoes => _special != null && _special.Special is TripleInkstrikeSpecial s ? s.ActiveTornadoes : 0;

        // 探針/除錯:最近一次投擲的原點與初速
        public Vector3 LastLaunchOrigin { get; private set; }
        public Vector3 LastLaunchVelocity { get; private set; }

        // 目前放開會不會丟大招物件(而不是副武器)
        public bool WillThrowDevice => ThrowOverride != null;

        private ThrowableConfig ThrowOverride
        {
            get
            {
                SpecialWeapon active = _special != null ? _special.ActiveSpecial : null;
                return active != null ? active.ThrowOverride : null;
            }
        }

        private ThrowableConfig ActiveProfile => ThrowOverride ?? _bombConfig;

        private void Awake()
        {
            _input = GetComponent<PlayerInputRouter>();
            _body = GetComponent<CharacterController>();
            _squid = GetComponent<SquidController>();
            _tank = GetComponent<PlayerInkTank>();
            _special = GetComponent<PlayerSpecialGauge>();
            _loadout = GetComponent<WeaponLoadout>();
            _owner = GetComponent<Inkling>();
            if (_throwOrigin == null)
            {
                _throwOrigin = FindRecursive(transform, "Muzzle");
            }
            if (_throwOrigin == null)
            {
                Debug.LogError("ThrowController:缺少投擲原點(Muzzle)", this);
            }
            _arcPoints = new NativeArray<Vector3>(MaxPreviewPoints, Allocator.Persistent);
            if (_bombConfig != null)
            {
                SharedPool.Prewarm(SubPrefab, _bombPrewarm);
            }
            HidePreview();
        }

        // 換副武器(WeaponLoadout 呼叫)
        public void SetSub(SubWeaponConfig config)
        {
            if (config == null)
            {
                return;
            }
            _bombConfig = config;
            _mine.Clear();
            SharedPool.Prewarm(SubPrefab, _bombPrewarm);
        }

        private static Transform FindRecursive(Transform parent, string childName)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name == childName)
                {
                    return child;
                }
                Transform found = FindRecursive(child, childName);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        private void OnDestroy()
        {
            if (_arcPoints.IsCreated)
            {
                _arcPoints.Dispose();
            }
        }

        private void OnDisable()
        {
            // 被擊倒等停用時收起預覽與鏡頭拉近
            if (_wasHeld)
            {
                EndAim();
                _wasHeld = false;
                IsAiming = false;
            }
        }

        private void Update()
        {
            if (_bombConfig == null || _input == null || _throwOrigin == null)
            {
                return;
            }
            IAimView view = _owner != null ? _owner.AimView : null;
            if (view == null)
            {
                return;
            }
            // 拉近鏡頭只對本機玩家的相機有意義;bot 的視線不是相機
            _rig = view as ThirdPersonCameraRig;

            SpecialWeapon active = _special != null ? _special.ActiveSpecial : null;
            ThrowableConfig overrideProfile = active != null ? active.ThrowOverride : null;
            bool subBlocked = active != null && active.BlocksSubWeapon && overrideProfile == null;
            // 主武器的出手硬直(爆破槍出膛後 PostDelayFrame_Blaster)也不能丟副武器:視同沒按,硬直過後仍按著才開始瞄準
            MainWeapon main = _loadout != null ? _loadout.Main : null;
            subBlocked |= overrideProfile == null && main != null && main.isActiveAndEnabled && main.BlocksSubWeapon;

            // 烏賊態不能瞄準/投擲(視為未按住);按住途中變烏賊 → 取消,不丟
            bool squid = _squid != null && _squid.IsSquid;
            bool held = _input.SubWeaponHeld && !squid && !subBlocked;
            if (held)
            {
                if (!_wasHeld)
                {
                    BeginAim();
                    _holdStartTime = Time.time;
                    _chargedCuePlayed = false;
                    ResetMash();
                }
                _heldOverride = overrideProfile != null;
                UpdateCharge(_heldOverride);
                UpdatePreview(overrideProfile ?? _bombConfig);
            }
            else if (_wasHeld)
            {
                EndAim();
                if (!squid && !subBlocked)
                {
                    if (_heldOverride && active != null && overrideProfile != null)
                    {
                        ThrowSpecial(active, overrideProfile);
                    }
                    else if (!_heldOverride)
                    {
                        TryThrowSub();
                    }
                }
            }
            _wasHeld = held;
            IsAiming = held;
            if (!held)
            {
                SubChargeFrames = 0f;
                SubCharge01 = 0f;
                _mashBank = 0f;
                _mashFrames = 0f;
            }
        }

        // 蓄力:按住秒數 × 60 扣掉出手準備幀,上限 MaxChargeFrames(S3 Curling:5F 後開始、60F 蓄滿)
        private void UpdateCharge(bool overrideHeld)
        {
            int max = _bombConfig.MaxChargeFrames;
            if (overrideHeld || max <= 0)
            {
                SubChargeFrames = 0f;
                SubCharge01 = 0f;
                return;
            }
            float heldFrames = (Time.time - _holdStartTime) * SimFrames.Rate;
            // 炸彈出現在手上(出手準備結束)後才吃連打折抵(S3 Fizzy)
            if (heldFrames >= _bombConfig.ChargeWindupFrames)
            {
                UpdateMash();
            }
            SubChargeFrames = Mathf.Clamp(heldFrames - _bombConfig.ChargeWindupFrames + _mashFrames, 0f, max);
            SubCharge01 = SubChargeFrames / max;
            if (!_chargedCuePlayed && SubCharge01 >= 1f)
            {
                _chargedCuePlayed = true;
                string cue = _bombConfig.ChargedSound;
                if (!string.IsNullOrEmpty(cue))
                {
                    GameAudio.PlayFrom(_owner != null && _owner.IsLocalPlayer, cue, _throwOrigin.position, 0.8f, 0f);
                }
            }
        }

        // 連打加速的邊緣狀態從「按下副武器」這一刻的輸入開始算:按著跳躍鍵/推著搖桿起手不算一次
        private void ResetMash()
        {
            _mashBank = 0f;
            _mashFrames = 0f;
            _mashJumpWasHeld = _input != null && _input.JumpHeld;
            _mashStickWasOut = _input != null && _input.MoveInput.sqrMagnitude >= StickOutSq;
        }

        // 搖桿「甩出」判定的遲滯:推過 0.7 算一次,回到 0.3 內才能再算(JP:入力の絶対値が一定値未満から一定値以上に)
        private const float StickOutSq = 0.7f * 0.7f;
        private const float StickBackSq = 0.3f * 0.3f;

        // S3 Fizzy:按住副武器時每按一次跳躍 −8F、搖桿從中間甩出 −11F;折抵量每幀最多兌現 2.5F(連打上限)
        private void UpdateMash()
        {
            float perFrame = _bombConfig.ChargeMashMaxPerFrame;
            if (perFrame <= 0f)
            {
                return;
            }
            bool jump = _input.JumpHeld;
            if (jump && !_mashJumpWasHeld)
            {
                _mashBank += _bombConfig.ChargeMashJumpFrames;
            }
            _mashJumpWasHeld = jump;
            float stick = _input.MoveInput.sqrMagnitude;
            if (!_mashStickWasOut && stick >= StickOutSq)
            {
                _mashStickWasOut = true;
                _mashBank += _bombConfig.ChargeMashStickFrames;
            }
            else if (_mashStickWasOut && stick < StickBackSq)
            {
                _mashStickWasOut = false;
            }
            if (_mashBank > 0f)
            {
                float take = Mathf.Min(_mashBank, perFrame * Time.deltaTime * SimFrames.Rate);
                _mashBank -= take;
                _mashFrames += take;
            }
        }

        // 投擲者速度(m/s):自訂出手的副武器繼承動量用(S3 SpawnBulletAdditionMovePlayerParam)
        private Vector3 OwnerVelocity => _body != null && _body.enabled ? _body.velocity : Vector3.zero;

        private void BeginAim()
        {
            Color ink = TeamPalette.ColorOf(_owner != null ? _owner.Team : Team.Alpha);
            TeamTint.Apply(_arc, ink, 0.85f);
            if (_marker != null)
            {
                TeamTint.ApplyAll(_marker, ink);
            }
            if (_rig != null)
            {
                _rig.SetAimZoom(true);
            }
            if (_arc != null)
            {
                _arc.enabled = true;
            }
        }

        private void EndAim()
        {
            if (_rig != null)
            {
                _rig.SetAimZoom(false);
            }
            HidePreview();
        }

        private void HidePreview()
        {
            if (_arc != null)
            {
                _arc.positionCount = 0;
                _arc.enabled = false;
            }
            if (_marker != null)
            {
                _marker.gameObject.SetActive(false);
            }
            HasPredictedLanding = false;
        }

        // 自訂出手的副武器(TryComputeLaunch 回傳 true)才走設定的預覽;大招接管的投擲一律用預設拋物線
        private bool ComputeLaunch(ThrowableConfig config, out Vector3 origin, out Vector3 velocity)
        {
            origin = _throwOrigin.position;
            Vector3 direction = ComputeAimDirection(config, origin);
            if (config is SubWeaponConfig sub && ReferenceEquals(config, _bombConfig)
                && sub.TryComputeLaunch(ref origin, direction, OwnerVelocity, SubCharge01, out velocity))
            {
                return true;
            }
            velocity = SimpleBallistics.LaunchVelocity(direction, config.ThrowSpeed, config.LiftAngleDeg);
            return false;
        }

        private Vector3 ComputeAimDirection(ThrowableConfig config, Vector3 origin)
        {
            IAimView view = _owner.AimView;
            Vector3 camPos = view.AimOrigin;
            Vector3 camForward = view.AimForward;
            var ray = new Ray(camPos, camForward);
            Vector3 target = Physics.Raycast(ray, out RaycastHit hit, config.AimReach, _aimMask,
                QueryTriggerInteraction.Ignore)
                ? hit.point
                : ray.GetPoint(config.AimReach);
            Vector3 toTarget = target - origin;
            float targetDistance = toTarget.magnitude;
            Vector3 direction = camForward;
            if (targetDistance > 0.01f)
            {
                // 遠處目標:槍口指向準心點(拋物線收斂到準心);近處目標:漸變成相機方向。
                // 槍口離牆 1m 時撞牆點在斜上方,槍口→目標會是 40° 仰角、再上抬就丟上天;相機方向才是玩家的意圖。
                float half = Mathf.Max(0.01f, _nearAimDistance * 0.5f);
                float t = Mathf.Clamp01((targetDistance - half) / half);
                direction = Vector3.Slerp(camForward, toTarget / targetDistance, t);
                // 俯角很大時相機在後上方,中心射線可能先撞到玩家身後/腳邊的地 → 目標在投擲原點後面 → 用相機方向
                if (Vector3.Dot(direction, camForward) < 0.3f)
                {
                    direction = camForward;
                }
            }
            return direction;
        }

        private void UpdatePreview(ThrowableConfig config)
        {
            bool custom = ComputeLaunch(config, out Vector3 position, out Vector3 velocity);
            int count = 0;
            bool found = false;
            Vector3 landing = position;
            Vector3 normal = Vector3.up;
            if (custom)
            {
                count = _bombConfig.BuildPreview(position, velocity, SubCharge01,
                    _owner != null ? _owner.Team : Team.Alpha, _arcPoints, out found, out landing, out normal);
                ShowPreview(count, found, landing, normal);
                return;
            }
            _arcPoints[count++] = position;
            float dt = Mathf.Max(0.005f, config.PreviewStepTime);
            int maxSteps = Mathf.Min(config.PreviewMaxSteps, _arcPoints.Length - 1);
            // 副武器可指定預覽碰撞半徑(PreviewRadius > 0:與投擲物同半徑的 sphere cast);預設中心線射線
            float radius = config is SubWeaponConfig sub ? sub.PreviewRadius : 0f;
            int mask = config.HitMask & ~GameLayers.InklingMask;
            // 炸彈共通規則(§4.3):碰到敵方 Splash Wall 類裝置 / Big Bubbler 球罩就爆 → 副武器的預覽也停在那裡
            //(Suction / Autobomb 的 HitMask 只有地形,屏障在目標圖層、球罩沒有 Collider,要另外查;大招接管的投擲不套)
            bool stopsAtDetonators = config is SubWeaponConfig && ReferenceEquals(config, _bombConfig);
            Team team = _owner != null ? _owner.Team : Team.Alpha;
            for (int i = 0; i < maxSteps; i++)
            {
                Vector3 previous = position;
                SimpleBallistics.Step(ref position, ref velocity, config.Gravity, dt);
                Vector3 delta = position - previous;
                float distance = delta.magnitude;
                if (distance <= 0.0001f)
                {
                    _arcPoints[count++] = position;
                    continue;
                }
                Vector3 direction = delta / distance;
                bool touched = CastPreview(previous, direction, distance, radius, mask, out RaycastHit hit);
                if (stopsAtDetonators && BombRules.CastDetonator(previous, direction,
                        touched ? hit.distance : distance, radius, team, out RaycastHit detonator))
                {
                    hit = detonator;
                    touched = true;
                }
                if (touched)
                {
                    _arcPoints[count++] = hit.point;
                    landing = hit.point;
                    normal = hit.normal;
                    found = true;
                    break;
                }
                _arcPoints[count++] = position;
            }
            ShowPreview(count, found, landing, normal);
        }

        private static bool CastPreview(Vector3 from, Vector3 direction, float distance, float radius, int mask,
            out RaycastHit hit)
        {
            return radius > 0f
                ? Physics.SphereCast(from, radius, direction, out hit, distance, mask, QueryTriggerInteraction.Ignore)
                : Physics.Raycast(from, direction, out hit, distance, mask, QueryTriggerInteraction.Ignore);
        }

        private void ShowPreview(int count, bool found, Vector3 landing, Vector3 normal)
        {
            count = Mathf.Clamp(count, 0, _arcPoints.Length);
            if (_arc != null)
            {
                _arc.positionCount = count;
                _arc.SetPositions(_arcPoints.Slice(0, count));
            }
            PredictedLanding = landing;
            HasPredictedLanding = found;
            if (_marker != null)
            {
                if (found)
                {
                    _marker.SetPositionAndRotation(landing + normal * _markerLift,
                        Quaternion.FromToRotation(Vector3.up, normal));
                    if (!_marker.gameObject.activeSelf)
                    {
                        _marker.gameObject.SetActive(true);
                    }
                }
                else if (_marker.gameObject.activeSelf)
                {
                    _marker.gameObject.SetActive(false);
                }
            }
        }

        private void TryThrowSub()
        {
            if (Time.time - _lastThrowTime < MinInterval)
            {
                return;
            }
            SubProjectile prefab = SubPrefab;
            if (prefab == null)
            {
                return;
            }
            // S3 Torpedo:自己丟的還在場上 → 使用不可(不扣墨、不上墨量鎖、不算一次使用)
            if (SubUnavailable)
            {
                RefusedThrowCount++;
                GameAudio.PlayFrom(_owner != null && _owner.IsLocalPlayer, UnavailableSound, _throwOrigin.position, 0.8f, 0f);
                return;
            }
            // 墨不夠不丟(整顆判定,與步槍同規則);S3:嘗試就上墨量鎖(蓄力型依蓄力:Curling 70F → 滿蓄 30F)
            float charge = _bombConfig.MaxChargeFrames > 0 ? SubCharge01 : 0f;
            float inkLock = _bombConfig.InkLockSecondsAt(charge);
            if (_tank != null)
            {
                _tank.LockRefill(inkLock);
                if (!_tank.TryConsume(_bombConfig.InkCost))
                {
                    return;
                }
            }
            ComputeLaunch(_bombConfig, out Vector3 origin, out Vector3 velocity);
            LastLaunchOrigin = origin;
            LastLaunchVelocity = velocity;
            LastThrowCharge01 = charge;
            LastInkLockSeconds = inkLock;
            EnforceMaxActive();
            SubProjectile projectile = SharedPool.Spawn(prefab);
            projectile.LaunchCharged(origin, velocity, _bombConfig, _owner, charge);
            GameAudio.PlayFrom(_owner != null && _owner.IsLocalPlayer, _bombConfig.ThrowSound, origin, 1f, 0.06f);
            _mine.Add(projectile);
            _lastThrowTime = Time.time;
            ThrowCount++;
        }

        // 自己丟出、仍在場上的副武器數(已回池、或投擲物把 Owner 清掉 = 不再算數,例:Torpedo 爆炸後只剩散彈)
        private int CountMine()
        {
            int count = 0;
            for (int i = 0; i < _mine.Count; i++)
            {
                if (IsMine(_mine[i]))
                {
                    count++;
                }
            }
            return count;
        }

        private bool IsMine(SubProjectile p)
        {
            return p != null && p.gameObject.activeInHierarchy && p.Owner == _owner;
        }

        // 同時存在上限:超過時最舊的一個消失(S3 灑水器/護盾同時 1 個)
        private void EnforceMaxActive()
        {
            for (int i = _mine.Count - 1; i >= 0; i--)
            {
                if (!IsMine(_mine[i]))
                {
                    _mine.RemoveAt(i);
                }
            }
            int max = _bombConfig.MaxActive;
            while (max > 0 && _mine.Count >= max)
            {
                _mine[0].Expire();
                _mine.RemoveAt(0);
            }
        }

        // 大招接管的投擲:免墨,原點與初速交給大招行為
        private void ThrowSpecial(SpecialWeapon active, ThrowableConfig profile)
        {
            ComputeLaunch(profile, out Vector3 origin, out Vector3 velocity);
            LastLaunchOrigin = origin;
            LastLaunchVelocity = velocity;
            active.OnThrow(origin, velocity);
            DeviceThrowCount++;
        }
    }
}

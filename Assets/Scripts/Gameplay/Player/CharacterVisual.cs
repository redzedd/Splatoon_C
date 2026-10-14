using System.Diagnostics;
using SplatoonC.Core;
using SplatoonC.Core.Animation;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Painting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace SplatoonC.Gameplay.Player
{
    // 角色外觀:Inkling / Squid 模型的程序動畫、隊伍色、墨水槽。只「觀察」玩法狀態,不改任何玩法數值。
    //
    // 人形:待機呼吸;跑步步態以實際移動距離推進(步幅 = 腳步聲間距 1.35 m,S3 中量級跑速 5.76 m/s 時
    // 每秒 4.3 步);腿用兩骨 IK 踩地(支撐期腳不滑步)、骨盆上下擺與扭轉、隨加速度前傾/側傾;
    // 空中收腿/伸腿;落地蹲(疊在 SquidController 的 Visual 壓扁彈簧上);
    // 開火:上身與頭轉向準星、右手 IK 握槍對準、左手扶槍、每發後座力(MainWeapon.AttackCount);
    // 副武器:左手後拉蓄勢、放開甩出;大招:發動時雙手高舉、啟動中/集滿時觸手與墨水脈動發光;
    // 觸手(頭髮)以彈簧跟著頭的加速度甩動。
    // 烏賊:SquidController.IsSquid 時換成烏賊模型;面向移動方向、空中依垂直速度俯仰、爬牆時頭朝上背朝外;
    // 外套膜與觸手依速度擺動;乾地上跳著走。
    //
    // 與 SquidController 共存:它會切換 Visual 底下所有 Renderer.enabled(潛入隱形)並壓扁/下沉 Visual——
    // 本元件從不碰 Renderer.enabled,型態切換只用 GameObject.SetActive;烏賊模型放在 SquidModel 節點下,
    // 該節點抵銷 Visual 的非等比縮放(否則烏賊會被壓成 0.3 倍高,且俯仰時會剪切變形),壓扁感改在烏賊自己的軸上重做。
    [DisallowMultipleComponent]
    public sealed partial class CharacterVisual : MonoBehaviour
    {
        // S3 中量級人形跑速 0.096 u/F、游速 0.192 u/F(× 60 = m/s)
        private const float RunSpeed = 5.76f;
        private const float SwimSpeed = 11.52f;
        private const float TwoPi = Mathf.PI * 2f;

        [Header("接線(CharacterSetup 自動設定)")]
        [SerializeField, Tooltip("視覺根(Player/Visual;PlayerLocomotion 轉向、SquidController 壓扁/下沉的對象)")]
        private Transform _visualRoot;

        [SerializeField, Tooltip("人形模型(Visual/InklingModel,Inkling.fbx 實例)")]
        private Transform _inklingModel;

        [SerializeField, Tooltip("烏賊形態根(Visual/SquidModel):抵銷 Visual 的非等比縮放,底下放烏賊模型")]
        private Transform _squidRoot;

        [SerializeField, Tooltip("烏賊模型(SquidModel/Squid,Squid.fbx 實例;程式轉向、定位、壓扁它)")]
        private Transform _squidMesh;

        [SerializeField, Tooltip("投擲姿勢時左手拿著的副武器示意球(可留空);副武器有手持模型(SubWeaponConfig.HeldModel)時執行期換掉")]
        private GameObject _heldSubItem;

        [SerializeField, Tooltip("移動設定資產(讀烏賊態的 Visual 壓扁比例)")]
        private PlayerLocomotionConfig _locomotionConfig;

        [SerializeField, Tooltip("要染隊伍色的材質(觸手、墨水、烏賊身體);Visual 底下用到它們的子網格都會染色")]
        private Material[] _teamMaterials;

        [SerializeField, Tooltip("各隊伍色材質的基礎自發光倍率(與上欄同序;墨水微微發光)")]
        private float[] _teamBaseGlow;

        [Header("步態")]
        [SerializeField, Tooltip("全速時一步的距離(公尺;與 InklingAudio 腳步聲間距相同,腳著地與腳步聲同拍)")]
        private float _strideLength = 1.35f;

        [SerializeField, Tooltip("慢走時一步的距離(公尺)")]
        private float _minStrideLength = 0.55f;

        [SerializeField, Tooltip("擺盪期抬腳高度(公尺,全速時;腳跟在身後往上踢)")]
        private float _footLift = 0.24f;

        [SerializeField, Tooltip("骨盆上下擺動幅度(公尺,全速時;騰空期兩腳離地)")]
        private float _hipBob = 0.04f;

        [SerializeField, Tooltip("全速跑步的上身前傾(度)")]
        private float _runLean = 12f;

        [SerializeField, Tooltip("加速度前傾係數(度 / (m/s²));S3 人形加速度 0.01 u/F² = 36 m/s²")]
        private float _accelLean = 0.3f;

        [Header("瞄準")]
        [SerializeField, Tooltip("舉槍的半衰期(秒);S3 人形第一發 3F 出彈,手要在那之前舉好")]
        private float _aimRiseHalfLife = 0.012f;

        [SerializeField, Tooltip("放下槍的半衰期(秒)")]
        private float _aimFallHalfLife = 0.09f;

        [SerializeField, Tooltip("最後一發後維持舉槍的秒數")]
        private float _aimHoldAfterShot = 0.45f;

        [SerializeField, Tooltip("瞄準時上身往右扭的角度(度;持槍的步槍站姿,左手才搆得到護木)")]
        private float _stanceTwist = 24f;

        [SerializeField, Tooltip("握把點相對右肩的位置(公尺;槍的座標:右、上、前)")]
        private Vector3 _gripOffset = new Vector3(0.07f, -0.08f, 0.27f);

        [SerializeField, Tooltip("左手扶槍點相對握把的位置(公尺;槍的座標)")]
        private Vector3 _supportOffset = new Vector3(-0.045f, -0.035f, 0.075f);

        [SerializeField, Tooltip("每發後座力:槍口上揚(度)")]
        private float _recoilPitch = 7f;

        [SerializeField, Tooltip("每發後座力:槍往後退(公尺)")]
        private float _recoilBack = 0.03f;

        [SerializeField, Tooltip("頭往上看的上限(度);超過約 20° 後觸手尖會碰到墨水槽蓋")]
        private float _headPitchUpLimit = 24f;

        [SerializeField, Tooltip("頭往下看的上限(度)")]
        private float _headPitchDownLimit = 38f;

        [Header("雙持(雙槍;主武器實作 IDualWieldPose 時)")]
        [SerializeField, Tooltip("雙持瞄準:握把點相對同側肩膀(公尺;槍的座標:外側、上、前;左手鏡射 x)")]
        private Vector3 _dualGripOffset = new Vector3(0.035f, -0.11f, 0.34f);

        [SerializeField, Tooltip("砲台模式:握把點相對兩肩中點(公尺;槍的座標:外側、上、前)——雙槍併攏在胸前")]
        private Vector3 _turretGripOffset = new Vector3(0.075f, -0.17f, 0.34f);

        [SerializeField, Tooltip("砲台模式蹲低(公尺;S3 翻滾後雙腳大開、壓低重心)")]
        private float _turretCrouch = 0.12f;

        [SerializeField, Tooltip("翻滾時骨盆降低(公尺;身體蜷起來滾,轉軸貼近地面)")]
        private float _rollHipDrop = 0.3f;

        [SerializeField, Tooltip("翻滾轉軸在骨盆上方多高(公尺;約軀幹中心,身體繞它滾一圈)")]
        private float _rollPivotHeight = 0.22f;

        [Header("副武器 / 大招")]
        [SerializeField, Tooltip("放開副武器後甩手動作的秒數(S3 人形副武器 5F 出手,動作本身約 18F)")]
        private float _throwReleaseDuration = 0.3f;

        [SerializeField, Tooltip("大招發動時高舉雙手的秒數")]
        private float _specialFlourishDuration = 0.6f;

        [Header("觸手 / 烏賊")]
        [SerializeField, Tooltip("觸手彈簧剛性(1/s²);頭加速 a 時穩態落後 a/k 公尺")]
        private float _tentacleStiffness = 200f;

        [SerializeField, Tooltip("觸手彈簧阻尼(1/s;越小甩得越久)")]
        private float _tentacleDamping = 11f;

        [SerializeField, Tooltip("觸手甩動量(度 / 公尺落後量)")]
        private float _tentacleGain = 190f;

        [SerializeField, Tooltip("等速移動時觸手往後飄的量(公尺 / (m/s));跑速 5.76 → 約 10°")]
        private float _tentacleDrag = 0.01f;

        [SerializeField, Tooltip("乾地上烏賊跳一下前進的距離(公尺)")]
        private float _squidHopLength = 0.6f;

        [SerializeField, Tooltip("乾地上烏賊跳的高度(公尺)")]
        private float _squidHopHeight = 0.09f;

        [SerializeField, Tooltip("墨水槽液面平滑半衰期(秒)")]
        private float _inkFillHalfLife = 0.05f;

        // ── 效能量測(全部角色合計;測試讀取)──
        public static long TotalTicks;
        public static int TotalSamples;

        // 測試開關:量本元件 LateUpdate 內的 managed 配置(穩態應為 0)
        public static bool MeasureAllocations;
        public static long AllocatedBytes;

        public static void ResetCostCounters()
        {
            TotalTicks = 0;
            TotalSamples = 0;
            AllocatedBytes = 0;
        }

        public static double TicksToMilliseconds(long ticks)
        {
            return ticks * 1000.0 / Stopwatch.Frequency;
        }

        // 生成序號(錯開各角色的待機相位)
        private static int _spawnCounter;

        private readonly HumanoidRig _rig = new HumanoidRig();
        private readonly SquidRig _squidRig = new SquidRig();
        // MaterialPropertyBlock 不能在 MonoBehaviour 建構時建立 → Awake 才 new
        private CharacterTint _tint;

        private Inkling _inkling;
        private SquidController _squid;
        private PlayerLocomotion _locomotion;
        private CharacterController _controller;
        private PlayerInkTank _tank;
        private ThrowController _thrower;
        private PlayerSpecialGauge _special;
        private WeaponLoadout _loadout;
        private InklingHealth _health;
        private MainWeapon _main;
        private Transform _weaponModel;
        private System.Action _onColorsChanged;
        private bool _ready;
        private bool _wasHidden = true;
        private bool _squidForm;
        private bool _formApplied;
        private float _timeOffset;

        // 動態狀態
        private GaitCycle _gait;
        private float _stride = 1.35f;
        private float _moveAmount;
        private float _airWeight;
        private float _airTime;
        private bool _wasGrounded = true;
        private float _lastVerticalSpeed;
        private Vector3 _lastPosition;
        private Vector3 _smoothedVelocity;
        private Vector3 _smoothedAccel;
        private float _crouch;
        private float _crouchVelocity;
        private float _recoil;
        private float _recoilVelocity;
        private float _flinch;
        private float _flinchVelocity;
        private float _lastDamageTime = float.NegativeInfinity;
        private int _lastAttackCount;
        private MainWeapon _lastMain;
        private float _lastShotTime = float.NegativeInfinity;
        private float _aimWeight;
        private float _throwWeight;
        private int _lastThrowCount;
        private float _throwRelease = 1f;
        private bool _wasSpecialActive;
        private float _flourish = 1f;
        private float _inkFill = 1f;
        private Vector3 _tentacleLag;
        private Vector3 _tentacleLagVelocity;
        private Vector3 _prevHeadWorld;
        private Vector3 _prevHeadVelocity;
        private bool _tentacleInitialized;
        private float _swimPhase;
        private float _hopPhase;
        private float _jelly = 1f;
        private float _jellyVelocity;
        // 副武器手持模型:預設示意球、目前的模型實例、對應的副武器設定
        private GameObject _defaultHeldSub;
        private GameObject _heldSubInstance;
        private SubWeaponConfig _heldSubConfig;
        private bool _heldSubSynced;
        private Quaternion _squidRotation = Quaternion.identity;
        private Vector3 _squidOffset;

        // 雙持(IDualWieldPose):各手後座力彈簧、砲台權重、翻滾權重與本幀的翻滾旋轉(模型空間)
        private IDualWieldPose _dualPose;
        private bool _dual;
        private int _lastShotsL;
        private int _lastShotsR;
        private float _recoilL;
        private float _recoilLVelocity;
        private float _recoilR;
        private float _recoilRVelocity;
        private float _turretWeight;
        private float _rollWeight;
        private Quaternion _rollRotation = Quaternion.identity;
        private float _rollAngle;

        // 觸手的靜止軸(頭部空間):局部 X 轉 = 往外甩(沿 Z 位移)、局部 Z 轉 = 側擺(沿 -X 位移)
        private readonly Vector3[] _tentacleOut = new Vector3[6];
        private readonly Vector3[] _tentacleSide = new Vector3[6];

        public bool IsReady => _ready;

        // 非主武器的持武器瞄準(大招 Trizooka 扛著發射器時):true = 上身與右手照開火姿勢對準準星
        public bool AimOverride { get; set; }

        // 非主武器的一次出手(大招發射):後座力與「最後一發」時間(維持舉槍),strength 1 = 主武器一發的後座力
        public void NotifyExternalShot(float strength = 1f)
        {
            _lastShotTime = Time.time;
            _recoilVelocity += 26f * strength;
        }

        // 右手掌心的武器插槽(+Z 槍管、+Y 拇指側):其他武器種類要換手上模型時掛在這裡——
        // 插槽在人形模型底下,烏賊態整個人形停用,掛上去的模型自動跟著隱藏(不必登記到 SquidController)
        public Transform WeaponSocket => _rig.WeaponSocket;

        // 預設的手上武器模型(Splattershot 風格的 GunBarrel);換成其他主武器的模型時把它關掉
        public Transform WeaponModel => _weaponModel;

        // 左手武器插槽(雙持武器的左手那把;右手插槽對稱鏡射,第一次呼叫時在 Hand.L 底下建立)。只在換裝時呼叫
        public Transform EnsureWeaponSocketLeft()
        {
            return _ready ? _rig.EnsureWeaponSocketLeft() : null;
        }

        public Transform WeaponSocketLeft => _rig.WeaponSocketLeft;

        // 雙持狀態(測試讀):本幀是否用雙持姿勢、砲台權重、翻滾權重與目前翻滾角度(度)
        public bool DualWieldPosing => _dual;
        public float TurretWeight => _turretWeight;
        public float RollWeight => _rollWeight;
        public float RollAngle => _rollAngle;
        public bool IsSquidForm => _squidForm;
        public Transform InklingModel => _inklingModel;
        public Transform SquidModel => _squidRoot;
        public Transform SquidMesh => _squidMesh;
        public HumanoidRig Rig => _rig;
        public float DisplayedInk => _inkFill;
        public float AimWeight => _aimWeight;
        public float ThrowWeight => Mathf.Max(_throwWeight, ThrowReleaseWeight);
        public float Recoil => _recoil;
        public float GaitPhase => _gait.Phase;
        public float MoveAmount => _moveAmount;
        public bool SpecialFlourishing => _flourish < 1f;
        public Color TintColor => _tint != null ? _tint.AppliedColor : Color.clear;
        public int TintSlotCount => _tint != null ? _tint.SlotCount : 0;

        // 目前左手的副武器物件(副武器的手持模型實例,或預設示意球);投擲姿勢時才啟用
        public GameObject HeldSubItem => _heldSubItem;

        private float ThrowReleaseWeight => _throwRelease < 1f
            ? (_throwRelease < 0.75f ? 1f : 1f - (_throwRelease - 0.75f) / 0.25f)
            : 0f;

        private void Awake()
        {
            _inkling = GetComponent<Inkling>();
            _squid = GetComponent<SquidController>();
            _locomotion = GetComponent<PlayerLocomotion>();
            _controller = GetComponent<CharacterController>();
            _tank = GetComponent<PlayerInkTank>();
            _thrower = GetComponent<ThrowController>();
            _special = GetComponent<PlayerSpecialGauge>();
            _loadout = GetComponent<WeaponLoadout>();
            _health = GetComponent<InklingHealth>();
            _onColorsChanged = InvalidateTint;
            _tint = new CharacterTint();
            _defaultHeldSub = _heldSubItem;
            // 每個角色的呼吸/擺動錯開相位(8 個角色不會同步點頭)
            _timeOffset = (_spawnCounter++ % 97) * 0.731f;

            if (_visualRoot == null)
            {
                _visualRoot = transform.Find("Visual");
            }
            if (_visualRoot == null || _inklingModel == null || _squidRoot == null || _squidMesh == null)
            {
                Debug.LogError("CharacterVisual:缺少 Visual / InklingModel / SquidModel 接線(先跑 Tools/SplatoonC/Character Setup)", this);
                return;
            }
            if (!_rig.Bind(_inklingModel) || !_squidRig.Bind(_squidMesh))
            {
                return;
            }
            _weaponModel = HumanoidRig.FindDeep(_rig.WeaponSocket, "GunBarrel");
            _tint.Bind(_visualRoot, _teamMaterials, _teamBaseGlow);
            CacheTentacleAxes();
            _ready = true;
            _lastPosition = transform.position;
            _squidRotation = _squidMesh.localRotation;
            ApplyForm(_squid != null && _squid.IsSquid, true);
        }

        private void OnEnable()
        {
            TeamPalette.ColorsChanged += _onColorsChanged;
            _wasHidden = true;
        }

        private void OnDisable()
        {
            TeamPalette.ColorsChanged -= _onColorsChanged;
        }

        // 換上新的手上模型(或其他掛在 Visual 底下、用隊伍色材質的網格)後呼叫:重新掃描要染色的子網格
        //(會配置,只在換裝時呼叫,不要每幀)
        public void RefreshTintTargets()
        {
            if (_tint == null || _visualRoot == null)
            {
                return;
            }
            _tint.Bind(_visualRoot, _teamMaterials, _teamBaseGlow);
        }

        // 換副武器(WeaponLoadout → ThrowController.SetSub)時,把左手的示意球換成該副武器的手持模型
        //(SubWeaponConfig.HeldModel;程序化 prefab 以公尺建模):掛在示意球同一個父節點(Hand.L)同位置,
        // 縮放抵銷手骨的世界縮放;名稱以 Ink 開頭的網格用角色隊伍色材質,RefreshTintTargets 後由 CharacterTint 染色。
        // 每幀只比對參照(零配置);換裝才實例化。
        private void SyncHeldSubModel()
        {
            SubWeaponConfig config = _thrower != null ? _thrower.SubConfig : null;
            if (_heldSubSynced && ReferenceEquals(config, _heldSubConfig))
            {
                return;
            }
            _heldSubSynced = true;
            _heldSubConfig = config;
            if (_heldSubInstance != null)
            {
                // 先移出角色階層再銷毀:Destroy 延到幀尾,同一幀的 RefreshTintTargets 不該再綁到它
                _heldSubInstance.SetActive(false);
                _heldSubInstance.transform.SetParent(null, false);
                Destroy(_heldSubInstance);
                _heldSubInstance = null;
            }
            GameObject prefab = config != null ? config.HeldModel : null;
            Transform parent = _defaultHeldSub != null ? _defaultHeldSub.transform.parent : null;
            if (prefab != null && parent != null)
            {
                GameObject instance = Instantiate(prefab, parent, false);
                instance.name = prefab.name;
                Transform t = instance.transform;
                t.localPosition = _defaultHeldSub.transform.localPosition;
                t.localRotation = Quaternion.identity;
                t.localScale = Vector3.one / Mathf.Max(1e-4f, Mathf.Abs(parent.lossyScale.x));
                SetLayerRecursive(t, _defaultHeldSub.layer);
                instance.SetActive(false);
                _defaultHeldSub.SetActive(false);
                _heldSubInstance = instance;
                _heldSubItem = instance;
            }
            else
            {
                if (_heldSubItem != null && _heldSubItem != _defaultHeldSub)
                {
                    _heldSubItem.SetActive(false);
                }
                _heldSubItem = _defaultHeldSub;
            }
            RefreshTintTargets();
        }

        private static void SetLayerRecursive(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++)
            {
                SetLayerRecursive(root.GetChild(i), layer);
            }
        }

        // 配色變更(TeamPalette.Set):下一幀強制重設;隊伍變更由每幀比對顏色涵蓋
        private void InvalidateTint()
        {
            _tint?.Invalidate();
        }

        private void CacheTentacleAxes()
        {
            Quaternion headRest = Quaternion.Inverse(_rig.RestModelRotation(HumanoidRig.Bone.Head));
            for (int i = 0; i < 6; i++)
            {
                Quaternion rest = headRest * _rig.RestModelRotation(TentacleA(i));
                _tentacleOut[i] = rest * Vector3.forward;
                _tentacleSide[i] = rest * Vector3.right;
            }
        }

        private static HumanoidRig.Bone TentacleA(int index)
        {
            return (HumanoidRig.Bone)((int)HumanoidRig.Bone.Tentacle1A + index * 2);
        }

        private static HumanoidRig.Bone TentacleB(int index)
        {
            return (HumanoidRig.Bone)((int)HumanoidRig.Bone.Tentacle1A + index * 2 + 1);
        }

        private void LateUpdate()
        {
            if (!_ready)
            {
                return;
            }
            if (!_visualRoot.gameObject.activeInHierarchy)
            {
                // 被擊倒:Visual 整個被 InklingLifecycle 關掉,重生時重置動態(瞬移不該甩觸手)
                _wasHidden = true;
                UpdateMovementOverlays(Time.deltaTime, true);
                return;
            }
            float dt = Time.deltaTime;
            if (dt <= 0f)
            {
                return;
            }
            long start = Stopwatch.GetTimestamp();
            long allocatedBefore = MeasureAllocations ? System.GC.GetAllocatedBytesForCurrentThread() : 0;

            Vector3 position = transform.position;
            if (_wasHidden || (position - _lastPosition).sqrMagnitude > 9f)
            {
                ResetDynamics();
                _wasHidden = false;
            }
            _lastPosition = position;

            SyncHeldSubModel();
            UpdateTint();
            UpdateInkTank(dt);

            bool squid = _squid != null && _squid.IsSquid;
            ApplyForm(squid, false);
            // 大招姿勢(CharacterVisual.Special.cs):取樣本幀的持握姿勢,滾球模式時藏起人形
            SampleSpecialPose(dt);
            SampleMotion(dt, out float planarSpeed, out bool grounded);
            // A4 超級跳飛行中(CharacterController 關著)改用飛行速度
            OverrideMovementMotion(dt, ref planarSpeed, ref grounded);
            if (_squidForm)
            {
                AnimateSquid(dt, planarSpeed, grounded);
            }
            else
            {
                AnimateHuman(dt, planarSpeed, grounded);
            }
            // A4 護甲光暈、衝牆滿蓄閃光(不在 Visual 底下,潛伏中也看得到)
            UpdateMovementOverlays(dt, false);

            if (MeasureAllocations)
            {
                AllocatedBytes += System.GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            }
            TotalTicks += Stopwatch.GetTimestamp() - start;
            TotalSamples++;
        }

        private void ResetDynamics()
        {
            _smoothedVelocity = Vector3.zero;
            _smoothedAccel = Vector3.zero;
            _crouch = 0f;
            _crouchVelocity = 0f;
            _recoil = 0f;
            _recoilVelocity = 0f;
            _flinch = 0f;
            _flinchVelocity = 0f;
            _tentacleLag = Vector3.zero;
            _tentacleLagVelocity = Vector3.zero;
            _tentacleInitialized = false;
            _wasGrounded = true;
            _lastVerticalSpeed = 0f;
            _jelly = 1f;
            _jellyVelocity = 0f;
            _throwRelease = 1f;
            _flourish = 1f;
            _recoilL = 0f;
            _recoilLVelocity = 0f;
            _recoilR = 0f;
            _recoilRVelocity = 0f;
            _turretWeight = 0f;
            _rollWeight = 0f;
            _rollAngle = 0f;
            _rollRotation = Quaternion.identity;
            _gait.Reset();
            if (_health != null)
            {
                _lastDamageTime = _health.LastDamageTime;
            }
            if (_tank != null)
            {
                _inkFill = _tank.Normalized;
            }
            _lastPosition = transform.position;
        }

        // ───────────────────────── 共用 ─────────────────────────

        private void ApplyForm(bool squid, bool force)
        {
            if (!force && _formApplied && squid == _squidForm)
            {
                return;
            }
            bool wasSquid = _squidForm;
            _squidForm = squid;
            _formApplied = true;
            if (_inklingModel.gameObject.activeSelf == squid)
            {
                _inklingModel.gameObject.SetActive(!squid);
            }
            if (_squidRoot.gameObject.activeSelf != squid)
            {
                _squidRoot.gameObject.SetActive(squid);
            }
            if (squid && !wasSquid && !force)
            {
                // 變身的「啵」:烏賊從略扁彈回原形(Visual 的壓扁彈簧同時在往 0.3 走)
                _jelly = 0.7f;
                _jellyVelocity = 6f;
                _squidRotation = Quaternion.identity;
            }
        }

        private void UpdateTint()
        {
            Team team = _inkling != null ? _inkling.Team : Team.Alpha;
            float glow = 0f;
            if (_special != null)
            {
                float t = Time.time + _timeOffset;
                if (_special.IsActive)
                {
                    // 大招啟動中:明顯的脈動光
                    glow = 1.4f + 0.8f * Mathf.Sin(t * 9f);
                }
                else if (_special.IsFull)
                {
                    // 大招集滿:柔和的呼吸光(提示「可以按了」)
                    glow = 0.55f + 0.45f * Mathf.Sin(t * 5f);
                }
            }
            // A4 衝牆滿蓄、超級跳蓄力的發光
            glow = Mathf.Max(glow, MovementGlow());
            _tint.Apply(TeamPalette.ColorOf(team), glow);
        }

        private void UpdateInkTank(float dt)
        {
            Transform fill = _rig.InkTankFill;
            if (fill == null || _tank == null)
            {
                return;
            }
            _inkFill = SpringMotion.Damp(_inkFill, Mathf.Clamp01(_tank.Normalized), _inkFillHalfLife, dt);
            // InkTankFill 原點在墨水底部、局部 +Y 朝上:y 縮放 = 墨量(0 用極小值,避免退化矩陣)
            Vector3 scale = fill.localScale;
            float y = Mathf.Max(0.001f, _inkFill);
            if (Mathf.Abs(scale.y - y) > 0.0005f)
            {
                fill.localScale = new Vector3(1f, y, 1f);
            }
        }

        private void SampleMotion(float dt, out float planarSpeed, out bool grounded)
        {
            Vector3 velocity = _controller != null && _controller.enabled ? _controller.velocity : Vector3.zero;
            grounded = _controller == null || _controller.isGrounded;
            Vector3 previous = _smoothedVelocity;
            _smoothedVelocity = SpringMotion.Damp(_smoothedVelocity, velocity, 0.03f, dt);
            Vector3 accel = (_smoothedVelocity - previous) / dt;
            accel.y = 0f;
            _smoothedAccel = SpringMotion.Damp(_smoothedAccel, accel, 0.08f, dt);
            planarSpeed = new Vector2(velocity.x, velocity.z).magnitude;

            // 落地:下落夠快就蹲一下(膝蓋吃掉衝擊),SquidController 的 Visual 壓扁同時在跑
            if (grounded && !_wasGrounded && _lastVerticalSpeed < -2f)
            {
                _crouchVelocity += Mathf.Min(-_lastVerticalSpeed, 16f) * 0.09f;
                _jellyVelocity -= Mathf.Min(-_lastVerticalSpeed, 16f) * 0.35f;
            }
            _wasGrounded = grounded;
            _lastVerticalSpeed = velocity.y;
            SpringMotion.Step(ref _crouch, ref _crouchVelocity, 0f, 260f, 20f, dt);
            _crouch = Mathf.Clamp(_crouch, -0.02f, 0.16f);
            // CharacterController.isGrounded 在下坡、小台階會閃一兩幀:離地超過 0.08 秒(或明顯往上跳)才算空中
            _airTime = grounded ? 0f : _airTime + dt;
            bool airborne = !grounded && (_airTime > 0.08f || velocity.y > 2f);
            _airWeight = SpringMotion.Damp(_airWeight, airborne ? 1f : 0f, airborne ? 0.06f : 0.035f, dt);
        }

        // 準心方向(模型空間):視點 + 視線 × 12 m 的點,從胸口指過去(近距離視差修正;bot 的視點在槍口高度)
        private Vector3 AimDirectionModel()
        {
            IAimView view = _inkling != null ? _inkling.AimView : null;
            if (view == null)
            {
                return Vector3.forward;
            }
            Vector3 chest = transform.position + Vector3.up * 1.15f;
            Vector3 dir = view.AimOrigin + view.AimForward * 12f - chest;
            if (dir.sqrMagnitude < 0.0001f)
            {
                return Vector3.forward;
            }
            return Quaternion.Inverse(_visualRoot.rotation) * dir.normalized;
        }

        // ───────────────────────── 人形 ─────────────────────────

        private void AnimateHuman(float dt, float planarSpeed, bool grounded)
        {
            float time = Time.time + _timeOffset;
            UpdateActionState(dt);

            // 步態:只在地上推進;停下時把腳帶回並攏。雙持翻滾(高速位移由翻滾姿勢表現)與砲台定身時不走步態
            float gaitSuppress = Mathf.Max(_rollWeight, _turretWeight);
            float targetMove = grounded
                ? Mathf.Clamp(planarSpeed / RunSpeed, 0f, 1.25f) * (1f - gaitSuppress)
                : _moveAmount;
            _moveAmount = SpringMotion.Damp(_moveAmount, targetMove, 0.06f, dt);
            if (grounded)
            {
                _stride = GaitCycle.StrideForSpeed(planarSpeed, RunSpeed, _strideLength, _minStrideLength);
                if (planarSpeed > 0.25f && gaitSuppress < 0.5f)
                {
                    _gait.Advance(planarSpeed * dt, _stride);
                }
                else
                {
                    _gait.SettleTowardsRest(6f, dt);
                }
            }
            float phase = _gait.Phase;
            float ground = 1f - _airWeight;
            float move = Mathf.Min(_moveAmount, 1f) * ground;

            Vector3 aimDir = AimDirectionModel();
            float aimYaw = Mathf.Atan2(aimDir.x, aimDir.z) * Mathf.Rad2Deg;
            float aimPitch = Mathf.Asin(Mathf.Clamp(aimDir.y, -1f, 1f)) * Mathf.Rad2Deg;
            Quaternion toModel = Quaternion.Inverse(_visualRoot.rotation);
            Vector3 accel = toModel * _smoothedAccel;

            HumanoidRig rig = _rig;
            rig.BeginPose();

            // ── 軀幹 ──
            float breathe = Mathf.Sin(time * TwoPi * 0.28f);
            float throwW = ThrowWeight;
            float aimW = _aimWeight * (1f - throwW);
            // 雙持:砲台模式壓低重心、翻滾時蜷身貼地
            float hipDrop = 0.01f + 0.03f * move + _crouch + 0.008f * aimW * (1f - move)
                            + _turretCrouch * _turretWeight + _rollHipDrop * _rollWeight + SpecialCrouch;
            float bob = GaitCycle.HipBob(phase) * _hipBob * move;
            // 翻滾:身體繞「骨盆上方 _rollPivotHeight」的軀幹中心轉,而不是繞骨盆(骨盆位移補上 v − R·v)
            Vector3 rollPivot = Vector3.up * _rollPivotHeight;
            Vector3 rollShift = rollPivot - _rollRotation * rollPivot;
            rig.SetHipsOffset(new Vector3(0f, -hipDrop + bob + breathe * 0.003f * (1f - move), 0f) + rollShift);

            float hipYaw = 8f * move * Mathf.Sin(phase);
            float leanPitch = _runLean * move + Mathf.Clamp(accel.z * _accelLean, -8f, 11f) * ground * (1f - _rollWeight)
                              + _crouch * 110f - _flinch * 6f + 7f * _turretWeight + 38f * _rollWeight;
            float leanRoll = Mathf.Clamp(-accel.x * _accelLean * 0.8f, -9f, 9f) * ground * (1f - _rollWeight);

            float yawToAim = Mathf.Clamp(aimYaw, -80f, 80f);
            float pitchToAim = Mathf.Clamp(aimPitch, -65f, 65f);
            // 副武器蓄勢:左肩往後拉;甩出瞬間往右轉
            float throwTwist = ThrowTwist();
            // 雙持正面對準(兩把槍左右對稱伸出),單槍是步槍側身站姿
            float stanceTwist = _dual ? 0f : _stanceTwist;
            float torsoYaw = (stanceTwist + yawToAim * 0.45f) * aimW + throwTwist * throwW + 0.35f * yawToAim * throwW;
            float torsoPitch = -pitchToAim * 0.3f * (aimW + throwW * 0.5f) - _recoil * 1.5f + ThrowLean() * throwW;
            // 其他武器種類的持握姿勢(滾筒:CharacterVisual.Roller.cs)接手上身
            ApplyRollerTorso(dt, yawToAim, throwW, throwTwist, ref torsoYaw, ref torsoPitch, ref leanPitch);
            // 大招自訂姿勢(Ultra Stamp 揮槌、Crab Tank 握操縱桿)最後接手上身
            ApplySpecialTorso(ref torsoYaw, ref torsoPitch);
            float lookWeight = Mathf.Lerp(0.7f - 0.35f * move, 1f, Mathf.Max(aimW, throwW));
            float headYaw = Mathf.Clamp((yawToAim - torsoYaw) * lookWeight, -70f, 70f);
            float headPitch = Mathf.Clamp(-pitchToAim * Mathf.Lerp(0.5f, 0.7f, aimW) - torsoPitch * 0.3f,
                -_headPitchUpLimit, _headPitchDownLimit) - _flinch * 8f + breathe * 0.8f * (1f - move);

            rig.RotateModel(HumanoidRig.Bone.Hips, _rollRotation
                * Quaternion.AngleAxis(hipYaw, Vector3.up) * Quaternion.AngleAxis(leanRoll * 0.3f, Vector3.forward));
            rig.RotateModel(HumanoidRig.Bone.Spine,
                Quaternion.AngleAxis(torsoYaw * 0.4f - hipYaw * 0.6f, Vector3.up)
                * Quaternion.AngleAxis(leanPitch * 0.45f + torsoPitch * 0.4f + breathe * 0.5f, Vector3.right)
                * Quaternion.AngleAxis(leanRoll * 0.35f, Vector3.forward));
            rig.RotateModel(HumanoidRig.Bone.Chest,
                Quaternion.AngleAxis(torsoYaw * 0.6f - hipYaw * 0.7f, Vector3.up)
                * Quaternion.AngleAxis(leanPitch * 0.35f + torsoPitch * 0.6f + breathe * 0.9f, Vector3.right)
                * Quaternion.AngleAxis(leanRoll * 0.35f, Vector3.forward));
            // 頭:抵銷上身的前傾,視線保持水平;轉向準星(翻滾時不抵銷,頭跟著縮進胸口)
            float counterLean = -(leanPitch * 0.8f + torsoPitch) * (1f - _rollWeight) + 20f * _rollWeight;
            rig.RotateModel(HumanoidRig.Bone.Neck,
                Quaternion.AngleAxis(headYaw * 0.35f + hipYaw * 0.5f, Vector3.up)
                * Quaternion.AngleAxis(headPitch * 0.35f + counterLean * 0.4f, Vector3.right));
            rig.RotateModel(HumanoidRig.Bone.Head,
                Quaternion.AngleAxis(headYaw * 0.65f + hipYaw * 0.3f, Vector3.up)
                * Quaternion.AngleAxis(headPitch * 0.65f + counterLean * 0.6f
                                       + 2.5f * GaitCycle.HipBob(phase) * move, Vector3.right));
            rig.SolveFk();

            PoseLegs(phase, move, ground, hipYaw);
            PoseArms(phase, move, aimDir, time);
            PoseTentacles(dt, time, headPitch, aimW);
            // A4 超級跳著地姿勢(疊在上面的手臂之上)
            ApplyMovementHumanPose(dt);
            rig.Apply();

            if (_heldSubItem != null)
            {
                bool holding = _throwWeight > 0.5f && _throwRelease >= 1f;
                if (_heldSubItem.activeSelf != holding)
                {
                    _heldSubItem.SetActive(holding);
                }
            }
        }

        // 讀武器/副武器/大招/受傷狀態 → 權重與觸發(後座力、甩手、大招高舉、受擊縮一下)
        private void UpdateActionState(float dt)
        {
            MainWeapon main = _loadout != null ? _loadout.Main : null;
            if (main == null)
            {
                if (_main == null)
                {
                    _main = GetComponent<MainWeapon>();
                }
                main = _main;
            }
            bool firing = AimOverride;
            TrackRoller(main);
            TrackLongGun(main);
            if (main != null)
            {
                firing |= main.IsAttacking;
                int count = main.AttackCount;
                if (main != _lastMain)
                {
                    // 換裝:新武器的計數從頭算,不當成開火
                    _lastMain = main;
                    _lastAttackCount = count;
                    _dualPose = main as IDualWieldPose;
                    if (_dualPose != null)
                    {
                        _lastShotsL = _dualPose.LeftShots;
                        _lastShotsR = _dualPose.RightShots;
                    }
                    TrackGunStance(main);
                }
                if (count != _lastAttackCount)
                {
                    _lastAttackCount = count;
                    _lastShotTime = Time.time;
                    // 長槍(狙擊槍)的後座力依這一發放大(滿蓄比部分蓄力重);大型單發槍(IGunStancePose,爆破槍)較大;
                    // 兩者沒實作時各為 × 1(一把武器只會實作其中一種)
                    _recoilVelocity += 26f * ShotRecoilScale * GunStanceRecoilScale();
                }
            }
            SpringMotion.Step(ref _recoil, ref _recoilVelocity, 0f, 520f, 34f, dt);
            UpdateDualState(main, dt);
            UpdateGunStance(dt);

            bool throwHold = _thrower != null && _thrower.IsAiming;
            int throws = _thrower != null ? _thrower.ThrowCount + _thrower.DeviceThrowCount : 0;
            if (throws != _lastThrowCount)
            {
                _lastThrowCount = throws;
                _throwRelease = 0f;
            }
            if (_throwRelease < 1f)
            {
                _throwRelease = Mathf.Min(1f, _throwRelease + dt / Mathf.Max(0.05f, _throwReleaseDuration));
            }
            _throwWeight = SpringMotion.DampAsymmetric(_throwWeight, throwHold ? 1f : 0f, 0.035f, 0.06f, dt);

            bool aimWanted = firing || Time.time - _lastShotTime < _aimHoldAfterShot;
            _aimWeight = SpringMotion.DampAsymmetric(_aimWeight, aimWanted && !throwHold ? 1f : 0f,
                _aimRiseHalfLife, _aimFallHalfLife, dt);

            bool specialActive = _special != null && _special.IsActive;
            if (specialActive && !_wasSpecialActive)
            {
                _flourish = 0f;
            }
            _wasSpecialActive = specialActive;
            if (_flourish < 1f)
            {
                _flourish = Mathf.Min(1f, _flourish + dt / Mathf.Max(0.05f, _specialFlourishDuration));
            }

            if (_health != null && _health.LastDamageTime > _lastDamageTime)
            {
                _lastDamageTime = _health.LastDamageTime;
                _flinchVelocity += 9f;
            }
            SpringMotion.Step(ref _flinch, ref _flinchVelocity, 0f, 300f, 22f, dt);
        }

        // 雙持:各手後座力(出膛的那隻手)、砲台蹲低權重、翻滾的身體旋轉
        private void UpdateDualState(MainWeapon main, float dt)
        {
            // 拿著大招(SpecialHeldModel 顯示中)時不用雙持姿勢:左手的槍也被藏起來
            _dual = !HeldSpecialActive && _dualPose != null && main != null && main.isActiveAndEnabled
                    && _dualPose.DualWieldActive;
            if (_dual)
            {
                int left = _dualPose.LeftShots;
                int right = _dualPose.RightShots;
                if (left != _lastShotsL)
                {
                    _lastShotsL = left;
                    _recoilLVelocity += 26f;
                }
                if (right != _lastShotsR)
                {
                    _lastShotsR = right;
                    _recoilRVelocity += 26f;
                }
            }
            SpringMotion.Step(ref _recoilL, ref _recoilLVelocity, 0f, 520f, 34f, dt);
            SpringMotion.Step(ref _recoilR, ref _recoilRVelocity, 0f, 520f, 34f, dt);

            bool turret = _dual && _dualPose.TurretStance;
            _turretWeight = SpringMotion.DampAsymmetric(_turretWeight, turret ? 1f : 0f, 0.03f, 0.07f, dt);

            // 翻滾:進度 0 → 0.25(S3 起手 4F / 共 16F)蜷身蓄勢,之後身體沿翻滾方向滾一圈(ease in-out);
            // 滾完權重快速退掉(轉滿 360° = 回到原姿勢,不會跳)
            float progress = _dual ? _dualPose.RollProgress : -1f;
            if (progress >= 0f)
            {
                Vector3 dir = Quaternion.Inverse(_visualRoot.rotation) * _dualPose.RollDirection;
                dir.y = 0f;
                Vector3 axis = dir.sqrMagnitude > 1e-4f ? Vector3.Cross(Vector3.up, dir.normalized) : Vector3.right;
                float spin = Mathf.Clamp01((progress - 0.25f) / 0.75f);
                spin = spin * spin * (3f - 2f * spin);
                _rollAngle = 360f * spin;
                _rollRotation = Quaternion.AngleAxis(_rollAngle, axis);
                _rollWeight = Mathf.Max(_rollWeight, Mathf.Clamp01(progress / 0.25f));
            }
            else
            {
                _rollAngle = 0f;
                _rollRotation = Quaternion.identity;
                _rollWeight = SpringMotion.Damp(_rollWeight, 0f, 0.04f, dt);
                if (_rollWeight < 0.001f)
                {
                    _rollWeight = 0f;
                }
            }
        }

        private float FlourishWeight => _flourish < 1f ? Mathf.Sin(Mathf.Clamp01(_flourish) * Mathf.PI) : 0f;

        // 投擲的上身扭轉(度,+ = 往右):蓄勢時左肩後拉(上身往左扭、背對一點準星),甩出時轉回並越過正面
        private float ThrowTwist()
        {
            if (_throwRelease >= 1f)
            {
                return -26f;
            }
            float t = _throwRelease;
            return t < 0.45f ? Mathf.Lerp(-26f, 22f, t / 0.45f) : Mathf.Lerp(22f, 0f, (t - 0.45f) / 0.55f);
        }

        // 投擲蓄勢的上身後仰(度,負 = 後仰):手拉到肩後上方時胸口略往後、甩出時往前壓
        private float ThrowLean()
        {
            if (_throwRelease >= 1f)
            {
                return -6f;
            }
            float t = _throwRelease;
            return t < 0.45f ? Mathf.Lerp(-6f, 10f, t / 0.45f) : Mathf.Lerp(10f, 0f, (t - 0.45f) / 0.55f);
        }

        private void PoseLegs(float phase, float move, float ground, float hipYaw)
        {
            HumanoidRig rig = _rig;
            float sweep = GaitCycle.FootSweepHalf(_stride) * 0.9f * move;
            float lift = _footLift * move;
            float aimStance = _aimWeight * (1f - move) * ground;
            float rising = Mathf.Clamp01(_lastVerticalSpeed / 4f + 0.5f);
            Quaternion yaw = Quaternion.AngleAxis(hipYaw * 0.5f, Vector3.up);
            for (int side = 0; side < 2; side++)
            {
                bool left = side == 0;
                float s = left ? -1f : 1f;
                HumanoidRig.Bone upper = left ? HumanoidRig.Bone.UpperLegL : HumanoidRig.Bone.UpperLegR;
                HumanoidRig.Bone lower = left ? HumanoidRig.Bone.LowerLegL : HumanoidRig.Bone.LowerLegR;
                HumanoidRig.Bone foot = left ? HumanoidRig.Bone.FootL : HumanoidRig.Bone.FootR;
                float p = left ? phase : phase + Mathf.PI;
                Vector3 rest = rig.RestModelPosition(foot);
                Vector3 hip = rig.ModelPosition(upper);

                // 地面:支撐期貼地往後滑(= 身體前進)、擺盪期抬起往前;瞄準站定時左腳在前、右腳在後;
                // 雙槍砲台模式雙腳大開(膝蓋隨骨盆壓低自然彎曲)
                Vector3 groundTarget = new Vector3(
                    rest.x + s * (0.012f + 0.085f * _turretWeight),
                    rest.y + lift * GaitCycle.FootLift(p),
                    rest.z + sweep * GaitCycle.FootForward(p) + (left ? 0.07f : -0.05f) * aimStance
                    + (left ? 0.05f : -0.06f) * _turretWeight);
                // 空中:上升時一腳收起(膝蓋抬高)、下落時兩腳伸直準備著地
                Vector3 tuck = hip + new Vector3(s * 0.01f, left ? -0.32f : -0.48f, left ? 0.18f : -0.12f);
                Vector3 fall = hip + new Vector3(s * 0.035f, -0.57f, left ? 0.07f : -0.04f);
                Vector3 airTarget = Vector3.Lerp(fall, tuck, rising);
                Vector3 target = Vector3.Lerp(groundTarget, airTarget, _airWeight);

                float pitch = GaitCycle.FootPitch(p) * move + 22f * _airWeight;
                Quaternion footRot = yaw * Quaternion.AngleAxis(pitch, Vector3.right) * rig.RestModelRotation(foot);
                Vector3 pole = yaw * new Vector3(s * 0.15f, 0f, 1f);
                if (_rollWeight > 0.001f)
                {
                    // 翻滾:膝蓋收到胸前(身體座標,跟著翻滾旋轉)
                    Vector3 rollTuck = hip + _rollRotation * new Vector3(s * 0.03f, -0.27f, 0.2f);
                    target = Vector3.Lerp(target, rollTuck, _rollWeight);
                    footRot = Quaternion.Slerp(footRot,
                        _rollRotation * Quaternion.AngleAxis(-35f, Vector3.right) * rig.RestModelRotation(foot), _rollWeight);
                    pole = Vector3.Lerp(pole, _rollRotation * new Vector3(s * 0.15f, 0.2f, 1f), _rollWeight);
                }
                rig.SolveLimb(upper, lower, foot, target, pole, footRot, true);
            }
        }

        private void PoseArms(float phase, float move, Vector3 aimDir, float time)
        {
            // 大招自訂姿勢優先於滾筒/雙槍(拿著大招的東西時主武器的持握姿勢不該出現)
            if (PoseSpecialArms())
            {
                return;
            }
            if (PoseRollerArms(phase, move, time))
            {
                return;
            }
            if (_dual)
            {
                PoseDualArms(phase, move, aimDir, time);
                return;
            }
            HumanoidRig rig = _rig;
            Vector3 shoulderL = rig.ModelPosition(HumanoidRig.Bone.UpperArmL);
            Vector3 shoulderR = rig.ModelPosition(HumanoidRig.Bone.UpperArmR);
            Quaternion chest = rig.ModelRotation(HumanoidRig.Bone.Chest);
            float swingL = GaitCycle.ArmSwing(phase) * move;
            float swingR = GaitCycle.ArmSwing(phase + Mathf.PI) * move;
            float air = _airWeight;
            float aimW = _aimWeight;
            float throwW = ThrowWeight;
            float flourishW = FlourishWeight;
            float idle = Mathf.Sin(time * 1.1f) * 0.006f * (1f - move);

            // ── 右手(持槍)──
            // 放低持槍:槍口朝前下方,跑步時小幅前後擺
            Quaternion lowGun = chest * Quaternion.AngleAxis(LowGunPitch(move), Vector3.right);
            Vector3 lowGrip = shoulderR + chest * new Vector3(0.07f, -0.28f + 0.04f * move + idle + LowGripLift,
                0.15f + 0.07f * swingR);
            Quaternion airGun = chest * Quaternion.AngleAxis(8f, Vector3.right);
            Vector3 airGrip = shoulderR + chest * new Vector3(0.1f, -0.15f, 0.22f);
            float airBlend = air * (1f - aimW);
            Quaternion gunRot = Quaternion.Slerp(lowGun, airGun, airBlend);
            Vector3 grip = Vector3.Lerp(lowGrip, airGrip, airBlend);
            Vector3 poleR = Vector3.Lerp(new Vector3(0.35f, -0.4f, -1f), new Vector3(0.55f, -0.8f, -0.25f), aimW);

            // 瞄準:槍管對準準心方向(拇指朝上的完整旋轉,不是最短弧)+ 後座力;
            // 大型單發槍(IGunStancePose,爆破槍)換握把 / 扶槍點,出膛前的預備時間蓄勢往後收
            GunStanceOffsets(out Vector3 gripOffset, out Vector3 supportOffset);
            Quaternion aimGun = Quaternion.LookRotation(aimDir, Vector3.up)
                                * Quaternion.AngleAxis(-_recoil * _recoilPitch, Vector3.right);
            Vector3 aimGrip = shoulderR + aimGun * gripOffset - (aimGun * Vector3.forward) * (_recoil * _recoilBack);
            ApplyGunStanceWindup(ref aimGun, ref aimGrip);
            if (aimW > 0.001f)
            {
                gunRot = Quaternion.Slerp(gunRot, aimGun, aimW);
                grip = Vector3.Lerp(grip, aimGrip, aimW);
            }
            if (flourishW > 0.001f)
            {
                // 大招發動:槍朝天高舉
                Quaternion upGun = chest * Quaternion.AngleAxis(-70f, Vector3.right);
                Vector3 upGrip = shoulderR + chest * new Vector3(0.1f, 0.3f, 0.08f);
                gunRot = Quaternion.Slerp(gunRot, upGun, flourishW);
                grip = Vector3.Lerp(grip, upGrip, flourishW);
                poleR = Vector3.Lerp(poleR, new Vector3(1f, -0.2f, -0.3f), flourishW);
            }
            Quaternion handR = gunRot * Quaternion.Inverse(rig.SocketLocalRot);
            Vector3 wristR = grip - handR * rig.SocketLocalPos;
            rig.SolveLimb(HumanoidRig.Bone.UpperArmR, HumanoidRig.Bone.LowerArmR, HumanoidRig.Bone.HandR,
                wristR, poleR, handR, false);

            // ── 左手(空手 / 扶槍 / 丟副武器)──
            // 空手:待機時自然下垂(手肘微彎),跑步時手肘彎約 90° 前後擺、往前擺時抬高
            Vector3 lowL = shoulderL + chest * new Vector3(-0.08f + 0.02f * move,
                -0.365f + 0.13f * move + 0.06f * Mathf.Max(0f, swingL) + idle,
                0.03f + 0.02f * move + 0.18f * swingL);
            // 空中:手往外上方張開保持平衡
            Vector3 airL = shoulderL + chest * new Vector3(-0.27f, -0.02f, 0.06f);
            Vector3 handPosL = Vector3.Lerp(lowL, airL, air * (1f - aimW));
            Quaternion handRotL = Quaternion.identity;
            float straightL = 1f;
            Vector3 poleL = new Vector3(-0.35f, -0.4f, -1f);

            // 扶槍:射擊槍只在瞄準時扶;長槍(狙擊槍)放低時左手也托著護木(雙手抱槍),依實際槍的位置
            float supportW = aimW;
            Vector3 supportGrip = aimGrip;
            Quaternion supportGun = aimGun;
            if (LongGunStanceActive)
            {
                supportW = Mathf.Max(aimW, 0.9f * (1f - air));
                supportGrip = grip;
                supportGun = gunRot;
            }
            if (supportW > 0.001f)
            {
                Vector3 support = supportGrip + supportGun * supportOffset;
                // 扶槍:手指從槍左下方繞到右側(沿骨 +Y)、拇指(+Z)朝上前
                Vector3 fingers = supportGun * new Vector3(0.85f, 0.2f, 0.45f).normalized;
                Vector3 thumb = supportGun * new Vector3(0f, 0.7f, 0.7f);
                handPosL = Vector3.Lerp(handPosL, support, supportW);
                handRotL = LookAlong(fingers, thumb);
                straightL = 1f - supportW;
                poleL = Vector3.Lerp(poleL, new Vector3(-0.5f, -0.85f, -0.2f), supportW);
            }
            if (throwW > 0.001f)
            {
                Vector3 throwPos = ThrowHandOffset();
                Vector3 throwTarget = shoulderL + chest * throwPos;
                handPosL = Vector3.Lerp(handPosL, throwTarget, throwW);
                // 掌心朝前托著副武器
                Quaternion throwRot = LookAlong(chest * new Vector3(-0.1f, 0.9f, 0.35f), chest * new Vector3(0.3f, 0f, 1f));
                handRotL = straightL >= 1f ? throwRot : Quaternion.Slerp(handRotL, throwRot, throwW);
                straightL *= 1f - throwW;
                // 手肘往外、抬到接近肩高(蓄勢的「拉弓」感);甩出時跟著手往前
                poleL = Vector3.Lerp(poleL, new Vector3(-1f, 0.05f, -0.3f), throwW);
            }
            if (flourishW > 0.001f)
            {
                Vector3 up = shoulderL + chest * new Vector3(-0.1f, 0.32f, 0.08f);
                handPosL = Vector3.Lerp(handPosL, up, flourishW);
                poleL = Vector3.Lerp(poleL, new Vector3(-1f, -0.2f, -0.3f), flourishW);
                straightL = Mathf.Max(straightL, flourishW);
            }
            if (straightL >= 0.999f)
            {
                rig.SolveLimb(HumanoidRig.Bone.UpperArmL, HumanoidRig.Bone.LowerArmL, HumanoidRig.Bone.HandL,
                    handPosL, poleL, Quaternion.identity, false);
                rig.SetModelRotation(HumanoidRig.Bone.HandL,
                    rig.StraightEndRotation(HumanoidRig.Bone.LowerArmL, HumanoidRig.Bone.HandL));
            }
            else
            {
                rig.SolveLimb(HumanoidRig.Bone.UpperArmL, HumanoidRig.Bone.LowerArmL, HumanoidRig.Bone.HandL,
                    handPosL, poleL, handRotL, false);
                if (straightL > 0.001f)
                {
                    Quaternion straight = rig.StraightEndRotation(HumanoidRig.Bone.LowerArmL, HumanoidRig.Bone.HandL);
                    rig.SetModelRotation(HumanoidRig.Bone.HandL, Quaternion.Slerp(handRotL, straight, straightL));
                }
            }
        }

        // 雙持(雙槍):兩手各握一把(右 WeaponSocket、左 WeaponSocketLeft)。
        // 放低:兩把槍口朝前下方、跑步前後擺;瞄準:兩把都對準準心方向、左右對稱伸出,各自吃自己那一發的後座力;
        // 砲台模式:雙槍併攏在胸前正中;翻滾:收在胸口跟著身體滾;丟副武器時左手照樣甩(槍握在手上);大招發動雙手高舉。
        private void PoseDualArms(float phase, float move, Vector3 aimDir, float time)
        {
            HumanoidRig rig = _rig;
            Vector3 shoulderL = rig.ModelPosition(HumanoidRig.Bone.UpperArmL);
            Vector3 shoulderR = rig.ModelPosition(HumanoidRig.Bone.UpperArmR);
            Vector3 shoulderMid = (shoulderL + shoulderR) * 0.5f;
            Quaternion chest = rig.ModelRotation(HumanoidRig.Bone.Chest);
            float air = _airWeight;
            float aimW = _aimWeight;
            float turretW = _turretWeight;
            float rollW = _rollWeight;
            float throwW = ThrowWeight;
            float flourishW = FlourishWeight;
            float idle = Mathf.Sin(time * 1.1f) * 0.006f * (1f - move);
            Quaternion aimRot = Quaternion.LookRotation(aimDir, Vector3.up);

            for (int side = 0; side < 2; side++)
            {
                bool left = side == 0;
                float s = left ? -1f : 1f;
                float recoil = left ? _recoilL : _recoilR;
                Vector3 shoulder = left ? shoulderL : shoulderR;
                float swing = GaitCycle.ArmSwing(left ? phase : phase + Mathf.PI) * move;

                Quaternion gunRot = chest * Quaternion.AngleAxis(26f - 10f * move, Vector3.right);
                Vector3 grip = shoulder + chest * new Vector3(s * 0.07f, -0.28f + 0.04f * move + idle, 0.15f + 0.07f * swing);
                // 空中:兩手往外張開平衡,槍口朝前
                float airBlend = air * (1f - aimW);
                if (airBlend > 0.001f)
                {
                    gunRot = Quaternion.Slerp(gunRot, chest * Quaternion.AngleAxis(8f, Vector3.right), airBlend);
                    grip = Vector3.Lerp(grip, shoulder + chest * new Vector3(s * 0.17f, -0.13f, 0.17f), airBlend);
                }
                Vector3 pole = new Vector3(s * 0.35f, -0.4f, -1f);

                if (aimW > 0.001f)
                {
                    Quaternion aimGun = aimRot * Quaternion.AngleAxis(-recoil * _recoilPitch, Vector3.right);
                    Vector3 back = (aimGun * Vector3.forward) * (recoil * _recoilBack);
                    Vector3 dualGrip = shoulder + aimGun * new Vector3(s * _dualGripOffset.x, _dualGripOffset.y, _dualGripOffset.z);
                    Vector3 turretGrip = shoulderMid
                                         + aimGun * new Vector3(s * _turretGripOffset.x, _turretGripOffset.y, _turretGripOffset.z);
                    Vector3 aimGrip = Vector3.Lerp(dualGrip, turretGrip, turretW) - back;
                    gunRot = Quaternion.Slerp(gunRot, aimGun, aimW);
                    grip = Vector3.Lerp(grip, aimGrip, aimW);
                    pole = Vector3.Lerp(pole, new Vector3(s * 0.6f, -0.85f, -0.2f), aimW);
                }
                if (rollW > 0.001f)
                {
                    // 翻滾:雙槍收在胸前(胸口座標,跟著身體滾),手肘往外
                    Quaternion tuckGun = chest * Quaternion.AngleAxis(-12f, Vector3.right);
                    Vector3 tuckGrip = shoulder + chest * new Vector3(-s * 0.05f, -0.2f, 0.2f);
                    gunRot = Quaternion.Slerp(gunRot, tuckGun, rollW);
                    grip = Vector3.Lerp(grip, tuckGrip, rollW);
                    pole = Vector3.Lerp(pole, chest * new Vector3(s * 0.9f, -0.3f, -0.3f), rollW);
                }
                if (left && throwW > 0.001f)
                {
                    // 副武器:左手握著槍照樣拉到肩後上方再甩出
                    Vector3 throwTarget = shoulderL + chest * ThrowHandOffset();
                    grip = Vector3.Lerp(grip, throwTarget, throwW);
                    gunRot = Quaternion.Slerp(gunRot, chest * Quaternion.AngleAxis(-70f, Vector3.right), throwW);
                    pole = Vector3.Lerp(pole, new Vector3(-1f, 0.05f, -0.3f), throwW);
                }
                if (flourishW > 0.001f)
                {
                    Quaternion upGun = chest * Quaternion.AngleAxis(-70f, Vector3.right);
                    Vector3 upGrip = shoulder + chest * new Vector3(s * 0.1f, 0.3f, 0.08f);
                    gunRot = Quaternion.Slerp(gunRot, upGun, flourishW);
                    grip = Vector3.Lerp(grip, upGrip, flourishW);
                    pole = Vector3.Lerp(pole, new Vector3(s, -0.2f, -0.3f), flourishW);
                }

                Quaternion socketRot = left ? rig.SocketLocalRotLeft : rig.SocketLocalRot;
                Vector3 socketPos = left ? rig.SocketLocalPosLeft : rig.SocketLocalPos;
                Quaternion hand = gunRot * Quaternion.Inverse(socketRot);
                Vector3 wrist = grip - hand * socketPos;
                if (left)
                {
                    rig.SolveLimb(HumanoidRig.Bone.UpperArmL, HumanoidRig.Bone.LowerArmL, HumanoidRig.Bone.HandL,
                        wrist, pole, hand, false);
                }
                else
                {
                    rig.SolveLimb(HumanoidRig.Bone.UpperArmR, HumanoidRig.Bone.LowerArmR, HumanoidRig.Bone.HandR,
                        wrist, pole, hand, false);
                }
            }
        }

        // 左手投擲軌跡(胸口座標,相對左肩):蓄勢在肩後上方(手肘抬到肩高往外撐,前臂豎起)→ 甩過頭頂 →
        // 往前放開 → 越過身體收手(甩向右前方,也比較靠近右側槍口高度的投擲原點)
        private Vector3 ThrowHandOffset()
        {
            Vector3 hold = new Vector3(-0.17f, 0.2f, -0.17f);
            if (_throwRelease >= 1f)
            {
                return hold;
            }
            Vector3 over = new Vector3(-0.07f, 0.27f, 0.04f);
            Vector3 release = new Vector3(0.02f, 0.1f, 0.35f);
            Vector3 follow = new Vector3(0.12f, -0.14f, 0.26f);
            float t = _throwRelease;
            if (t < 0.3f)
            {
                return Vector3.Lerp(hold, over, t / 0.3f);
            }
            if (t < 0.5f)
            {
                return Vector3.Lerp(over, release, (t - 0.3f) / 0.2f);
            }
            return Vector3.Lerp(release, follow, Mathf.Clamp01((t - 0.5f) / 0.3f));
        }

        // 骨 +Y 沿 along、+Z 盡量朝 front 的旋轉
        private static Quaternion LookAlong(Vector3 along, Vector3 front)
        {
            Vector3 y = along.normalized;
            Vector3 z = front - y * Vector3.Dot(front, y);
            if (z.sqrMagnitude < 1e-6f)
            {
                z = Vector3.Cross(y, Vector3.right);
            }
            return Quaternion.LookRotation(z.normalized, y);
        }

        // 觸手(頭髮):頭部的慣性落後量(彈簧)+ 重力(頭仰/俯時垂向下)+ 輕微擺動 → 每條觸手依自己的軸甩
        private void PoseTentacles(float dt, float time, float headPitch, float aimW)
        {
            HumanoidRig rig = _rig;
            Quaternion headModel = rig.ModelRotation(HumanoidRig.Bone.Head);
            Vector3 headModelPos = rig.ModelPosition(HumanoidRig.Bone.Head) + headModel * new Vector3(0f, 0.12f, -0.06f);
            Vector3 headWorld = _inklingModel.TransformPoint(headModelPos);
            if (!_tentacleInitialized)
            {
                _prevHeadWorld = headWorld;
                _prevHeadVelocity = Vector3.zero;
                _tentacleInitialized = true;
            }
            // 相對彈簧:頭的「速度變化量」當衝量打在觸手上(等速移動不累積落後,只有加減速、轉身、起跳落地會甩),
            // 再加一點與速度成正比的拖曳(跑步時頭髮往後飄)
            Vector3 headVelocity = (headWorld - _prevHeadWorld) / dt;
            _prevHeadWorld = headWorld;
            Vector3 deltaV = Vector3.ClampMagnitude(headVelocity - _prevHeadVelocity, 3f);
            _prevHeadVelocity = headVelocity;
            _tentacleLagVelocity -= deltaV;
            SpringMotion.Step(ref _tentacleLag, ref _tentacleLagVelocity, Vector3.zero,
                _tentacleStiffness, _tentacleDamping, dt);
            _tentacleLag = Vector3.ClampMagnitude(_tentacleLag, 0.3f);
            Vector3 lagWorld = Vector3.ClampMagnitude(_tentacleLag - headVelocity * _tentacleDrag, 0.3f);
            Quaternion headWorldRot = _visualRoot.rotation * headModel;
            Quaternion toHead = Quaternion.Inverse(headWorldRot);
            Vector3 lagHead = toHead * lagWorld;
            // 重力:頭的「下」偏離靜止方向多少,觸手就往那邊垂
            Vector3 gravity = (toHead * Vector3.down - Quaternion.Inverse(rig.RestModelRotation(HumanoidRig.Bone.Head))
                * Vector3.down) * 0.18f;
            Vector3 sway = new Vector3(Mathf.Sin(time * 1.7f) * 0.01f, 0f, Mathf.Sin(time * 1.3f + 0.7f) * 0.008f);
            Vector3 deflect = lagHead + gravity + sway;
            // 仰頭時後面四條往外甩(不然尖端碰墨水槽);瞄準舉手時前面兩條往外撥(不插進上臂)
            float backLift = Mathf.Max(0f, -headPitch - 8f) * 1.2f;
            for (int i = 0; i < 6; i++)
            {
                float outDeg = _tentacleGain * Vector3.Dot(deflect, _tentacleOut[i]);
                float sideDeg = -_tentacleGain * Vector3.Dot(deflect, _tentacleSide[i]);
                outDeg += i < 2 ? 14f * aimW : backLift;
                outDeg = Mathf.Clamp(outDeg, -6f, 50f);
                sideDeg = Mathf.Clamp(sideDeg, -30f, 30f);
                rig.RotateLocal(TentacleA(i), Quaternion.Euler(outDeg, 0f, sideDeg));
                rig.RotateLocal(TentacleB(i), Quaternion.Euler(outDeg * 0.55f, 0f, sideDeg * 0.6f));
            }
        }

        // ───────────────────────── 烏賊 ─────────────────────────

        private void AnimateSquid(float dt, float planarSpeed, bool grounded)
        {
            float time = Time.time + _timeOffset;
            UpdateActionState(dt);
            if (_heldSubItem != null && _heldSubItem.activeSelf)
            {
                _heldSubItem.SetActive(false);
            }

            // 抵銷 Visual 的非等比縮放:底下的烏賊在等比座標裡自由旋轉,不會剪切
            Vector3 vs = _visualRoot.localScale;
            _squidRoot.localScale = new Vector3(1f / Mathf.Max(vs.x, 0.05f), 1f / Mathf.Max(vs.y, 0.05f),
                1f / Mathf.Max(vs.z, 0.05f));

            // 貼在墨牆裡才用「頭朝上」的爬牆朝向;翻越階段是躍出牆面,改走空中俯仰
            bool climbing = _locomotion != null && _locomotion.IsInsideInkedWall;
            bool submerged = _squid != null && _squid.IsSubmerged;
            bool inInk = _squid != null && _squid.IsInOwnInk;
            float speed = climbing && _controller != null ? _controller.velocity.magnitude : planarSpeed;
            float swimNorm = Mathf.Clamp01(speed / SwimSpeed);
            bool dryHop = grounded && !inInk && !climbing && planarSpeed > 0.3f;

            // 壓扁感:Visual 的 y 縮放相對「烏賊態目標值」的比例 → 烏賊自己的上下軸;下沉時的橫向收縮 → 等比縮小
            float restY = _locomotionConfig != null ? _locomotionConfig.SquidVisualScaleY : 0.3f;
            float squash = Mathf.Clamp(vs.y / Mathf.Max(0.05f, restY), 0.6f, 1.45f);
            SpringMotion.Step(ref _jelly, ref _jellyVelocity, 1f, 220f, 9f, dt);
            float jelly = Mathf.Clamp(_jelly, 0.6f, 1.4f);
            float shrink = Mathf.Clamp(vs.x, 0.3f, 1f);
            float sy = squash * jelly;
            float sxz = 1f / Mathf.Sqrt(sy);
            _squidMesh.localScale = new Vector3(sxz * shrink, sy * shrink, sxz * shrink);

            // 朝向:爬牆 = 頭朝上、背朝牆外;空中 = 依垂直速度俯仰;地上 = 跟著 Visual(移動方向)
            Quaternion visualRot = _visualRoot.rotation;
            Quaternion targetLocal;
            Vector3 targetOffset;
            if (climbing)
            {
                Vector3 normal = _locomotion.ClimbWallNormal;
                Vector3 upWall = Vector3.ProjectOnPlane(Vector3.up, normal);
                if (upWall.sqrMagnitude < 0.0001f)
                {
                    upWall = Vector3.ProjectOnPlane(_visualRoot.forward, normal);
                }
                Quaternion world = Quaternion.LookRotation(upWall.normalized, normal);
                targetLocal = Quaternion.Inverse(visualRot) * world;
                // 貼到牆面上(膠囊半徑 0.4 m;胸口高度附近)
                targetOffset = Quaternion.Inverse(visualRot) * (-normal * 0.36f + Vector3.up * 0.55f);
            }
            else
            {
                float vy = _controller != null ? _controller.velocity.y : 0f;
                float pitch = grounded ? 0f : -Mathf.Clamp(Mathf.Atan2(vy, Mathf.Max(planarSpeed, 2.5f)) * Mathf.Rad2Deg,
                    -55f, 55f);
                Vector3 accel = Quaternion.Inverse(visualRot) * _smoothedAccel;
                float roll = Mathf.Clamp(-accel.x * 0.5f, -14f, 14f);
                float hopPitch = 0f;
                float hop = 0f;
                if (dryHop)
                {
                    _hopPhase = Mathf.Repeat(_hopPhase + planarSpeed * dt / Mathf.Max(0.1f, _squidHopLength) * Mathf.PI,
                        Mathf.PI);
                    hop = Mathf.Sin(_hopPhase) * _squidHopHeight;
                    hopPitch = -Mathf.Cos(_hopPhase) * 12f;
                }
                else
                {
                    _hopPhase = 0f;
                }
                targetLocal = Quaternion.AngleAxis(pitch + hopPitch, Vector3.right) * Quaternion.AngleAxis(roll, Vector3.forward);
                targetOffset = new Vector3(0f, hop, 0f);
            }
            float k = 1f - Mathf.Pow(0.5f, dt / (climbing ? 0.02f : 0.04f));
            _squidRotation = Quaternion.Slerp(_squidRotation, targetLocal, k);
            _squidOffset = Vector3.Lerp(_squidOffset, targetOffset, climbing ? 1f - Mathf.Pow(0.5f, dt / 0.03f) : 1f);
            _squidMesh.localRotation = _squidRotation;
            _squidMesh.localPosition = _squidOffset;
            // A4 翻滾扭轉、超級跳蓄力/飛行姿勢(疊在上面的朝向與壓扁之上)
            ApplyMovementSquidPose(dt, grounded);

            if (submerged || !_squidRig.IsValid)
            {
                // 完全潛入(看不見):省掉骨頭動畫
                return;
            }
            // 觸手擺動頻率隨速度(靜止 2.2 Hz,全速游約 6 Hz);外套膜反相擺
            float frequency = 2.2f + speed * 0.33f;
            _swimPhase = Mathf.Repeat(_swimPhase + TwoPi * frequency * dt, TwoPi);
            float sideAmp = 7f + 15f * swimNorm;
            float pitchAmp = 5f + 9f * swimNorm;
            float spread = 1f - swimNorm;
            for (int i = 0; i < SquidRig.TentacleCount; i++)
            {
                float lag = i * 0.55f;
                float side = sideAmp * Mathf.Sin(_swimPhase - lag) + (i - 2) * -5f * spread;
                float pitch = pitchAmp * Mathf.Sin(_swimPhase * 0.5f - lag + i) + 6f * swimNorm;
                _squidRig.PoseTentacle(i, side, pitch);
            }
            float mantleSide = 6f * swimNorm * Mathf.Sin(_swimPhase + Mathf.PI) + 2f * Mathf.Sin(time * 1.3f);
            float mantlePitch = 3f * Mathf.Sin(_swimPhase * 2f) + (dryHop ? 6f * Mathf.Sin(_hopPhase * 2f) : 0f);
            _squidRig.PoseMantle(Mathf.Clamp(mantleSide, -12f, 12f), mantlePitch);
        }
    }
}

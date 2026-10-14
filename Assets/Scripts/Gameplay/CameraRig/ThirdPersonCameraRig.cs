using SplatoonC.Core.Locomotion;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.CameraRig
{
    // 手寫第三人稱環繞相機(M1 決策:不裝 Cinemachine)。
    // 獨立物件、不掛在角色底下——子物件相機會繼承角色轉向造成鏡頭甩動。
    // 同時是本機玩家的視線來源(IAimView):準心 = 相機中心射線。
    public sealed class ThirdPersonCameraRig : MonoBehaviour, IAimView
    {
        [SerializeField, Tooltip("跟隨目標(Player 底下的 CameraPivot)")]
        private Transform _target;

        [SerializeField, Tooltip("輸入來源(Player 身上的 PlayerInputRouter)")]
        private PlayerInputRouter _input;

        [SerializeField, Tooltip("相機距離(公尺)")]
        private float _distance = 5f;

        [SerializeField, Tooltip("俯仰角下限(度,負值 = 仰視;-80 幾乎可垂直向上瞄)")]
        private float _minPitch = -80f;

        [SerializeField, Tooltip("俯仰角上限(度,正值 = 俯視)")]
        private float _maxPitch = 60f;

        [SerializeField, Tooltip("樞紐位置平滑時間(秒);只平滑位置,角度零延遲")]
        private float _followSmoothTime = 0.04f;

        [SerializeField, Tooltip("取景抬高(公尺):把角色壓到畫面下半,準星(畫面中心)落在角色頭上方")]
        private float _aimHeightOffset = 1.1f;

        [SerializeField, Tooltip("遮擋偵測球半徑(公尺)")]
        private float _occlusionRadius = 0.25f;

        [SerializeField, Tooltip("遮擋偵測圖層(場景接線時排除 Player 層)")]
        private LayerMask _occlusionMask = ~0;

        [SerializeField, Tooltip("啟動時鎖定滑鼠游標")]
        private bool _lockCursor = true;

        [Header("FOV(S3:人形 55°、烏賊 60°、投擲瞄準拉近 38°,皆為垂直 FOV)")]
        [SerializeField, Tooltip("人形 FOV(度)")]
        private float _baseFov = 55f;

        [SerializeField, Tooltip("烏賊態 FOV(度)")]
        private float _squidFov = 60f;

        [SerializeField, Tooltip("最大 FOV 增量(度),高速時的衝刺感(S3 沒有,保留少量)")]
        private float _maxFovBoost = 3f;

        [SerializeField, Tooltip("開始增 FOV 的速度門檻(公尺/秒;基準跑速 6)")]
        private float _fovSpeedThreshold = 7f;

        [SerializeField, Tooltip("滿 FOV 增量的速度(公尺/秒;烏賊墨上衝刺約 10.8)")]
        private float _fovMaxSpeed = 11f;

        [SerializeField, Tooltip("FOV 過渡速度(每秒)")]
        private float _fovLerpSpeed = 6f;

        [Header("投擲瞄準拉近")]
        [SerializeField, Tooltip("按住副武器時相機拉近的距離(公尺;基準距離 6.2)")]
        private float _aimZoomDistance = 1.2f;

        [SerializeField, Tooltip("按住副武器時的 FOV(度;S3 拉滿 38°)")]
        private float _aimZoomFov = 38f;

        [SerializeField, Tooltip("拉近/拉遠的過渡速度(每秒)")]
        private float _aimZoomLerpSpeed = 10f;

        private const float OcclusionSkin = 0.1f;
        private const float MinCameraDistance = 0.3f;

        private float _yaw;
        // 預設略帶俯角(第三人稱慣例);瞄準準確度不依賴此值——彈道補償讓任何俯角都打得到準心
        private float _pitch = 12f;
        private Vector3 _smoothedPivot;
        private Vector3 _pivotVelocity;
        private Camera _camera;
        private Vector3 _lastTargetPosition;
        private bool _hasLastTargetPosition;
        private float _currentDistance;
        private bool _aimZoom;
        // 主武器蓄力拉近的 FOV(度;≤ 0 = 無):狙擊槍蓄力中由武器每幀設定(本機玩家)
        private float _weaponZoomFov;
        private SquidController _squid;

        // 目前實際相機距離(含拉近過渡);測試用
        public float CurrentDistance => _currentDistance;

        public Vector3 AimOrigin => transform.position;
        public Vector3 AimForward => transform.forward;
        public float Yaw => transform.eulerAngles.y;

        // 跟隨目標(重生、換角色時改接)
        public void SetTarget(Transform target, PlayerInputRouter input)
        {
            _target = target;
            _input = input;
            _squid = input != null ? input.GetComponent<SquidController>() : null;
            if (_target != null)
            {
                _smoothedPivot = _target.position + Vector3.up * _aimHeightOffset;
                _pivotVelocity = Vector3.zero;
            }
        }

        // 直接設定絕對角度(重生、過場、AutoTest 基準歸位用)。
        public void SetAngles(float yawDeg, float pitchDeg)
        {
            _yaw = Mathf.Repeat(yawDeg, 360f);
            _pitch = Mathf.Clamp(pitchDeg, _minPitch, _maxPitch);
        }

        // 目標瞬移後(Inkling.Teleport):取景點直接跳到新位置,不從舊位置滑過來;也不把這一跳算成速度(FOV 不暴衝)
        public void SnapToTarget()
        {
            if (_target == null)
            {
                return;
            }
            _smoothedPivot = _target.position + Vector3.up * _aimHeightOffset;
            _pivotVelocity = Vector3.zero;
            _hasLastTargetPosition = false;
        }

        // 投擲瞄準中拉近鏡頭(ThrowController 按住/放開時呼叫);過渡在 LateUpdate 內平滑。
        public void SetAimZoom(bool active)
        {
            _aimZoom = active;
        }

        // 主武器蓄力拉近(狙擊槍蓄力中;fov ≤ 0 = 取消):人形時 FOV 取「平常 / 拉近」較小者,過渡同樣平滑
        public void SetWeaponZoom(float fov)
        {
            _weaponZoomFov = fov;
        }

        public float WeaponZoomFov => _weaponZoomFov;

        // 水平轉向速度上限(度/秒;≤ 0 = 不限):大招要求鏡頭跟著車身慢慢轉時設定(S3 Crab Tank 步行模式),結束時設回 0
        public float YawSpeedLimit { get; set; }

        private void Start()
        {
            if (_lockCursor)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            _yaw = transform.eulerAngles.y;
            _camera = GetComponent<Camera>();
            if (_input != null && _squid == null)
            {
                _squid = _input.GetComponent<SquidController>();
            }
            // 初始化成基準距離,否則第一幀從 0 跳到 6.2
            _currentDistance = _distance;
            if (_target != null)
            {
                _smoothedPivot = _target.position;
            }
        }

        private void LateUpdate()
        {
            if (_target == null)
            {
                return;
            }

            Vector2 lookDelta = _input != null ? _input.LookDeltaDeg : Vector2.zero;
            if (YawSpeedLimit > 0f)
            {
                float maxStep = YawSpeedLimit * Time.deltaTime;
                lookDelta.x = Mathf.Clamp(lookDelta.x, -maxStep, maxStep);
            }
            CameraOrbitSolver.AdvanceAngles(ref _yaw, ref _pitch, lookDelta, _minPitch, _maxPitch);

            // 取景點抬高於角色:相機中心(=準星)看向角色頭上方,角色落在畫面下半
            Vector3 framedTarget = _target.position + Vector3.up * _aimHeightOffset;
            _smoothedPivot = Vector3.SmoothDamp(
                _smoothedPivot, framedTarget, ref _pivotVelocity, _followSmoothTime);

            float targetDistance = _distance - (_aimZoom ? _aimZoomDistance : 0f);
            _currentDistance = Mathf.Lerp(_currentDistance, targetDistance,
                1f - Mathf.Exp(-_aimZoomLerpSpeed * Time.deltaTime));

            Vector3 desired = CameraOrbitSolver.ResolvePosition(_smoothedPivot, _yaw, _pitch, _currentDistance);
            Vector3 toCamera = desired - _smoothedPivot;
            float length = toCamera.magnitude;
            if (length > 0.001f)
            {
                Vector3 direction = toCamera / length;
                // 單一命中版 SphereCast:SphereCastAll 每呼叫配置陣列,LateUpdate 內就是每幀 GC。
                if (Physics.SphereCast(
                        _smoothedPivot, _occlusionRadius, direction, out RaycastHit hit,
                        length, _occlusionMask, QueryTriggerInteraction.Ignore))
                {
                    float pulled = Mathf.Max(hit.distance - OcclusionSkin, MinCameraDistance);
                    desired = _smoothedPivot + direction * pulled;
                }
            }

            transform.SetPositionAndRotation(desired, Quaternion.Euler(_pitch, _yaw, 0f));

            UpdateSpeedFov();
        }

        // 高速(烏賊墨上衝刺)時微增 FOV,強化速度感;投擲瞄準時再減一點(拉近感),兩者疊加不互搶。
        private void UpdateSpeedFov()
        {
            if (_camera == null || Time.deltaTime <= 0f)
            {
                return;
            }
            float speed = 0f;
            if (_hasLastTargetPosition)
            {
                Vector3 delta = _target.position - _lastTargetPosition;
                delta.y = 0f;
                speed = delta.magnitude / Time.deltaTime;
            }
            _lastTargetPosition = _target.position;
            _hasLastTargetPosition = true;

            float boost01 = Mathf.InverseLerp(_fovSpeedThreshold, _fovMaxSpeed, speed);
            bool squid = _squid != null && _squid.IsSquid;
            float targetFov = _aimZoom ? _aimZoomFov : (squid ? _squidFov : _baseFov) + _maxFovBoost * boost01;
            if (_weaponZoomFov > 0f && !squid)
            {
                targetFov = Mathf.Min(targetFov, _weaponZoomFov);
            }
            _camera.fieldOfView = Mathf.Lerp(
                _camera.fieldOfView, targetFov, 1f - Mathf.Exp(-_fovLerpSpeed * Time.deltaTime));
        }
    }
}

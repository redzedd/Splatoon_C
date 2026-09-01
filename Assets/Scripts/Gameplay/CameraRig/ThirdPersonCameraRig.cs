using SplatoonC.Core.Locomotion;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.CameraRig
{
    // 手寫第三人稱環繞相機(M1 決策:不裝 Cinemachine)。
    // 獨立物件、不掛在角色底下——子物件相機會繼承角色轉向造成鏡頭甩動。
    public sealed class ThirdPersonCameraRig : MonoBehaviour
    {
        [SerializeField, Tooltip("跟隨目標(Player 底下的 CameraPivot)")]
        private Transform _target;

        [SerializeField, Tooltip("輸入來源(Player 身上的 PlayerInputRouter)")]
        private PlayerInputRouter _input;

        [SerializeField, Tooltip("相機距離(公尺)")]
        private float _distance = 5f;

        [SerializeField, Tooltip("俯仰角下限(度,負值 = 仰視)")]
        private float _minPitch = -30f;

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

        [Header("速度感 FOV")]
        [SerializeField, Tooltip("基準 FOV(度)")]
        private float _baseFov = 60f;

        [SerializeField, Tooltip("最大 FOV 增量(度),高速時的衝刺感")]
        private float _maxFovBoost = 8f;

        [SerializeField, Tooltip("開始增 FOV 的速度門檻(公尺/秒;基準跑速 6)")]
        private float _fovSpeedThreshold = 7f;

        [SerializeField, Tooltip("滿 FOV 增量的速度(公尺/秒;烏賊墨上衝刺約 10.8)")]
        private float _fovMaxSpeed = 11f;

        [SerializeField, Tooltip("FOV 過渡速度(每秒)")]
        private float _fovLerpSpeed = 6f;

        private const float OcclusionSkin = 0.1f;
        private const float MinCameraDistance = 0.3f;

        private float _yaw;
        private float _pitch = 10f;
        private Vector3 _smoothedPivot;
        private Vector3 _pivotVelocity;
        private Camera _camera;
        private Vector3 _lastTargetPosition;
        private bool _hasLastTargetPosition;

        // 直接設定絕對角度(重生、過場、AutoTest 基準歸位用)。
        public void SetAngles(float yawDeg, float pitchDeg)
        {
            _yaw = Mathf.Repeat(yawDeg, 360f);
            _pitch = Mathf.Clamp(pitchDeg, _minPitch, _maxPitch);
        }

        private void Start()
        {
            if (_lockCursor)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            _yaw = transform.eulerAngles.y;
            _camera = GetComponent<Camera>();
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
            CameraOrbitSolver.AdvanceAngles(ref _yaw, ref _pitch, lookDelta, _minPitch, _maxPitch);

            // 取景點抬高於角色:相機中心(=準星)看向角色頭上方,角色落在畫面下半
            Vector3 framedTarget = _target.position + Vector3.up * _aimHeightOffset;
            _smoothedPivot = Vector3.SmoothDamp(
                _smoothedPivot, framedTarget, ref _pivotVelocity, _followSmoothTime);

            Vector3 desired = CameraOrbitSolver.ResolvePosition(_smoothedPivot, _yaw, _pitch, _distance);
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

        // 高速(烏賊墨上衝刺)時微增 FOV,強化速度感。
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
            float targetFov = _baseFov + _maxFovBoost * boost01;
            _camera.fieldOfView = Mathf.Lerp(
                _camera.fieldOfView, targetFov, 1f - Mathf.Exp(-_fovLerpSpeed * Time.deltaTime));
        }
    }
}

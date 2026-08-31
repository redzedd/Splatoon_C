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

        [SerializeField, Tooltip("遮擋偵測球半徑(公尺)")]
        private float _occlusionRadius = 0.25f;

        [SerializeField, Tooltip("遮擋偵測圖層(場景接線時排除 Player 層)")]
        private LayerMask _occlusionMask = ~0;

        [SerializeField, Tooltip("啟動時鎖定滑鼠游標")]
        private bool _lockCursor = true;

        private const float OcclusionSkin = 0.1f;
        private const float MinCameraDistance = 0.3f;

        private float _yaw;
        private float _pitch = 10f;
        private Vector3 _smoothedPivot;
        private Vector3 _pivotVelocity;

        private void Start()
        {
            if (_lockCursor)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            _yaw = transform.eulerAngles.y;
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

            _smoothedPivot = Vector3.SmoothDamp(
                _smoothedPivot, _target.position, ref _pivotVelocity, _followSmoothTime);

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
        }
    }
}

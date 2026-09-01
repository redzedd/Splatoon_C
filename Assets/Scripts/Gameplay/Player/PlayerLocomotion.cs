using SplatoonC.Core.Locomotion;
using UnityEngine;

namespace SplatoonC.Gameplay.Player
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerLocomotion : MonoBehaviour
    {
        [SerializeField, Tooltip("角色移動設定資產(Assets/Data/PlayerLocomotionConfig)")]
        private PlayerLocomotionConfig _config;

        [SerializeField, Tooltip("輸入來源;留空自動抓同物件上的 PlayerInputRouter")]
        private PlayerInputRouter _input;

        [SerializeField, Tooltip("轉向用的視覺根(只轉這個,不轉含 CharacterController 的根)")]
        private Transform _visualRoot;

        [SerializeField, Tooltip("取 yaw 用的相機;留空自動抓 Main Camera")]
        private Transform _cameraTransform;

        private CharacterController _controller;
        private MotionState _state;
        private SquidController _squid;

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
            if (_cameraTransform == null && Camera.main != null)
            {
                _cameraTransform = Camera.main.transform;
            }
            _squid = GetComponent<SquidController>();
        }

        private void Update()
        {
            if (_config == null || _input == null)
            {
                return;
            }

            float cameraYaw = _cameraTransform != null ? _cameraTransform.eulerAngles.y : 0f;
            float speedMultiplier = _squid != null ? _squid.CurrentSpeedMultiplier : 1f;
            MotionStep step = CharacterMotionSolver.Step(
                ref _state,
                _input.MoveInput,
                cameraYaw,
                _controller.isGrounded,
                _input.JumpPressedThisFrame,
                _config.ToSettings(),
                Time.time,
                Time.deltaTime,
                speedMultiplier);

            _controller.Move(step.Displacement);

            if (step.HasMoveInput && _visualRoot != null)
            {
                float currentYaw = _visualRoot.eulerAngles.y;
                float nextYaw = Mathf.MoveTowardsAngle(
                    currentYaw, step.DesiredYawDeg, _config.TurnSpeedDegPerSec * Time.deltaTime);
                _visualRoot.rotation = Quaternion.Euler(0f, nextYaw, 0f);
            }
        }
    }
}

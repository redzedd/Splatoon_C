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

        [SerializeField, Tooltip("取 yaw 用的相機;留空自動抓 Main Camera")]
        private Transform _cameraTransform;

        [SerializeField, Tooltip("爬牆探測圖層(場景接線時排除 Player)")]
        private LayerMask _climbMask = ~0;

        private CharacterController _controller;
        private MotionState _state;
        private SquidController _squid;
        private ClimbPhase _climbPhase;
        private Vector3 _climbWallNormal;
        private float _mantleEndTime;

        public bool IsClimbing => _climbPhase != ClimbPhase.None;

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

            // 爬牆:烏賊態 + 前方自家墨牆 → 接管本幀位移(重力與水平慣性歸零,離牆不滑步)。
            if (TryClimb(_input.MoveInput, cameraYaw))
            {
                _state.VerticalVelocity = 0f;
                _state.HorizontalVelocity = Vector3.zero;
                return;
            }

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

        // 回傳 true = 本幀位移已由爬牆/翻越接管。
        private bool TryClimb(Vector2 moveInput, float cameraYaw)
        {
            if (_climbPhase == ClimbPhase.Mantle)
            {
                if (Time.time < _mantleEndTime)
                {
                    // 到頂翻越:斜上位移把角色送過牆緣(上分量大避免卡邊)。
                    Vector3 mantleDir = Vector3.up * 1.4f - _climbWallNormal * 0.8f;
                    _controller.Move(mantleDir * (_config.ClimbSpeed * Time.deltaTime));
                    return true;
                }
                _climbPhase = ClimbPhase.None;
                return false;
            }

            if (_squid == null || !_squid.IsSquid)
            {
                _climbPhase = ClimbPhase.None;
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
                if (surface != null && surface.SampleOwnership(hit.point) == 1)
                {
                    _climbWallNormal = hit.normal;
                    _climbPhase = ClimbPhase.Climbing;
                    // 面向牆時前推 = 上爬、後拉 = 下滑
                    _controller.Move(WallClimbSolver.Step(
                        moveInput.y, _climbWallNormal,
                        _config.ClimbSpeed, _config.WallStickSpeed, Time.deltaTime));
                    return true;
                }
            }

            if (_climbPhase == ClimbPhase.Climbing)
            {
                // 爬升中探測落空 = 到頂,進入翻越
                _climbPhase = ClimbPhase.Mantle;
                _mantleEndTime = Time.time + _config.MantleDuration;
                return true;
            }
            return false;
        }
    }
}

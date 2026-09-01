using UnityEngine;

namespace SplatoonC.Core.Locomotion
{
    // 跨幀運動狀態——由呼叫端(PlayerLocomotion)持有,求解器每幀更新。
    public struct MotionState
    {
        public float VerticalVelocity;
        public float LastGroundedTime;
        public float LastJumpRequestTime;

        public static MotionState CreateInitial()
        {
            return new MotionState
            {
                VerticalVelocity = 0f,
                LastGroundedTime = float.NegativeInfinity,
                LastJumpRequestTime = float.NegativeInfinity,
            };
        }
    }

    public readonly struct MotionStep
    {
        public readonly Vector3 Displacement;
        public readonly bool Jumped;
        // HasMoveInput 為假時 DesiredYawDeg 無意義,呼叫端應維持現有朝向。
        public readonly float DesiredYawDeg;
        public readonly bool HasMoveInput;

        public MotionStep(Vector3 displacement, bool jumped, float desiredYawDeg, bool hasMoveInput)
        {
            Displacement = displacement;
            Jumped = jumped;
            DesiredYawDeg = desiredYawDeg;
            HasMoveInput = hasMoveInput;
        }
    }

    // 重力/跳躍/土狼時間/跳躍緩衝/相機相對移動的全部數學。純邏輯,不碰 CharacterController。
    public static class CharacterMotionSolver
    {
        public static MotionStep Step(
            ref MotionState state,
            Vector2 moveInput,
            float cameraYawDeg,
            bool isGrounded,
            bool jumpPressed,
            in LocomotionSettings settings,
            float time,
            float deltaTime,
            float speedMultiplier = 1f)
        {
            if (isGrounded)
            {
                state.LastGroundedTime = time;
            }
            if (jumpPressed)
            {
                state.LastJumpRequestTime = time;
            }

            // 著地時壓成固定下壓速度而不是 0:留 0 會讓 isGrounded 下一幀抖成 false,跳躍偶發失效。
            if (isGrounded && state.VerticalVelocity <= 0f)
            {
                state.VerticalVelocity = settings.GroundedStickVelocity;
            }
            else
            {
                state.VerticalVelocity += settings.Gravity * deltaTime;
            }

            bool jumped = false;
            bool withinCoyote = time - state.LastGroundedTime <= settings.CoyoteTime;
            bool withinBuffer = time - state.LastJumpRequestTime <= settings.JumpBufferTime;
            if (withinCoyote && withinBuffer)
            {
                // 初速由跳躍高度反算,設計師調「跳多高」而不是「衝量」。
                state.VerticalVelocity = Mathf.Sqrt(2f * settings.JumpHeight * -settings.Gravity);
                // 起跳即作廢兩個時間戳,防止土狼窗內二段跳。
                state.LastGroundedTime = float.NegativeInfinity;
                state.LastJumpRequestTime = float.NegativeInfinity;
                jumped = true;
            }

            Vector2 clamped = moveInput.sqrMagnitude > 1f ? moveInput.normalized : moveInput;
            bool hasMove = clamped.sqrMagnitude > 0.0001f;
            Vector3 worldDir = Quaternion.Euler(0f, cameraYawDeg, 0f) * new Vector3(clamped.x, 0f, clamped.y);
            float control = isGrounded ? 1f : settings.AirControl;
            // speedMultiplier:烏賊態在自家墨加速/乾地減速(由 Gameplay 層決定值,人形恆為 1)。
            Vector3 horizontal = worldDir * (settings.MoveSpeed * speedMultiplier * control);
            float desiredYaw = hasMove ? Mathf.Atan2(worldDir.x, worldDir.z) * Mathf.Rad2Deg : 0f;

            Vector3 displacement = (horizontal + Vector3.up * state.VerticalVelocity) * deltaTime;
            return new MotionStep(displacement, jumped, desiredYaw, hasMove);
        }
    }
}

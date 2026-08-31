namespace SplatoonC.Core.Locomotion
{
    // 角色移動調參集合——由 Gameplay 層的 ScriptableObject 轉換而來,Core 只認這個純 struct。
    public readonly struct LocomotionSettings
    {
        public readonly float MoveSpeed;
        public readonly float Gravity;
        public readonly float JumpHeight;
        public readonly float AirControl;
        public readonly float CoyoteTime;
        public readonly float JumpBufferTime;
        public readonly float TurnSpeedDegPerSec;
        public readonly float GroundedStickVelocity;

        public LocomotionSettings(
            float moveSpeed,
            float gravity,
            float jumpHeight,
            float airControl,
            float coyoteTime,
            float jumpBufferTime,
            float turnSpeedDegPerSec,
            float groundedStickVelocity)
        {
            MoveSpeed = moveSpeed;
            Gravity = gravity;
            JumpHeight = jumpHeight;
            AirControl = airControl;
            CoyoteTime = coyoteTime;
            JumpBufferTime = jumpBufferTime;
            TurnSpeedDegPerSec = turnSpeedDegPerSec;
            GroundedStickVelocity = groundedStickVelocity;
        }
    }
}

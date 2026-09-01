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
        // 加減速時間(秒);≤0 視為瞬時(舊行為)
        public readonly float AccelTime;
        public readonly float DecelTime;

        public LocomotionSettings(
            float moveSpeed,
            float gravity,
            float jumpHeight,
            float airControl,
            float coyoteTime,
            float jumpBufferTime,
            float turnSpeedDegPerSec,
            float groundedStickVelocity,
            float accelTime = 0f,
            float decelTime = 0f)
        {
            MoveSpeed = moveSpeed;
            Gravity = gravity;
            JumpHeight = jumpHeight;
            AirControl = airControl;
            CoyoteTime = coyoteTime;
            JumpBufferTime = jumpBufferTime;
            TurnSpeedDegPerSec = turnSpeedDegPerSec;
            GroundedStickVelocity = groundedStickVelocity;
            AccelTime = accelTime;
            DecelTime = decelTime;
        }
    }
}

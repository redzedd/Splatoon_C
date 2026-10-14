using UnityEngine;

namespace SplatoonC.Core.Locomotion
{
    // 爬牆位移純邏輯:沿「牆面上的上方向」爬升/下滑 + 貼牆分量。
    // up 投影到牆切面,斜面(如 30° 斜坡)也會沿坡面爬。
    public static class WallClimbSolver
    {
        public static Vector3 UpAlongWall(Vector3 wallNormal)
        {
            Vector3 projected = Vector3.up - wallNormal * Vector3.Dot(Vector3.up, wallNormal);
            // 天花板/地板類法線沒有爬升方向
            return projected.sqrMagnitude < 1e-6f ? Vector3.zero : projected.normalized;
        }

        public static Vector3 Step(
            float verticalInput, Vector3 wallNormal, float climbSpeed, float stickSpeed, float deltaTime)
        {
            Vector3 upAlongWall = UpAlongWall(wallNormal);
            return (upAlongWall * (verticalInput * climbSpeed) - wallNormal * stickSpeed) * deltaTime;
        }
    }
}

using UnityEngine;

namespace SplatoonC.Core.Combat
{
    // 投擲物(炸彈、大招裝置)與拋物線預覽共用的單一重力彈道:三處用同一個步進,預覽 = 實飛。
    public static class SimpleBallistics
    {
        // 半隱式歐拉:先更新速度再更新位置(與 InkProjectile 一致)。
        public static void Step(ref Vector3 position, ref Vector3 velocity, float gravity, float dt)
        {
            velocity.y += gravity * dt;
            position += velocity * dt;
        }

        // 沿瞄準方向、繞水平右軸往上抬 liftAngleDeg 後乘上初速;方向近乎垂直時無法定義右軸,退回原方向。
        public static Vector3 LaunchVelocity(Vector3 aimDirection, float speed, float liftAngleDeg)
        {
            Vector3 direction = aimDirection.normalized;
            Vector3 right = Vector3.Cross(Vector3.up, direction);
            if (right.sqrMagnitude < 0.000001f)
            {
                return direction * speed;
            }
            right.Normalize();
            // Unity 繞 x 軸正角度會把 forward 往下壓,故上抬用負角
            Vector3 lifted = Quaternion.AngleAxis(-liftAngleDeg, right) * direction;
            return lifted * speed;
        }

        // 反彈:法線分量反向乘 restitution,切向分量乘 tangentialFriction(1 = 不損失)。
        public static Vector3 Reflect(Vector3 velocity, Vector3 normal, float restitution, float tangentialFriction)
        {
            float along = Vector3.Dot(velocity, normal);
            Vector3 normalPart = normal * along;
            Vector3 tangentPart = velocity - normalPart;
            return tangentPart * tangentialFriction - normalPart * restitution;
        }
    }
}

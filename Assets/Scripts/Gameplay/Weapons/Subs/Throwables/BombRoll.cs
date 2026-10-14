using SplatoonC.Core.Combat;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 著地後像炸彈一樣滾(Torpedo 先碰地形、Fizzy Bomb 爆炸之間):水平速度每幀 ×(1 − resist)(S3 GroundPositionHorizonAirResist),
    // GroundWalker 沿地面平面前進(爬小台階、滾出邊緣 → 回到空中);撞牆 → 水平鏡射並 × wallRebound。
    // 位置公尺、速度 u/F;只查地形,零配置。
    public struct BombRoll
    {
        public Vector3 Position;
        public Vector3 GroundNormal;
        public Vector3 GroundPoint;
        // 水平前進方向(單位向量)與速度(u/F)
        public Vector3 Heading;
        public float Speed;

        // 本步事件
        public bool Bounced;
        public Vector3 WallNormal;
        public float Travel;

        // 從空中著地:groundPoint / groundNormal = 地面接觸點與法線,velocity = 著地前的速度(u/F),
        // keep = 保留的水平速度比例;球心放在接觸點沿法線 radius 外
        public void Land(Vector3 groundPoint, Vector3 groundNormal, Vector3 velocity, float radius, float keep)
        {
            GroundNormal = groundNormal.sqrMagnitude > 1e-6f ? groundNormal.normalized : Vector3.up;
            GroundPoint = groundPoint;
            Position = groundPoint + GroundNormal * (radius + GroundWalker.Skin);
            Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
            float speed = flat.magnitude;
            Heading = speed > 1e-6f ? flat / speed : (Heading.sqrMagnitude > 1e-6f ? Heading : Vector3.forward);
            Speed = speed * Mathf.Clamp01(keep);
            Bounced = false;
            Travel = 0f;
        }

        // 一幀;回傳 false = 滾出邊緣(改空中,速度用 AirVelocity)
        public bool Step(float resist, float wallRebound, float radius, float stepHeight)
        {
            Bounced = false;
            Speed = S3ThrowModel.StepGroundSpeed(Speed, resist);
            if (Speed < 1e-4f)
            {
                Speed = 0f;
                Travel = 0f;
                return true;
            }
            GroundStep step = GroundWalker.Move(ref Position, ref GroundNormal, ref GroundPoint, Heading, Speed, radius,
                stepHeight);
            Travel = step.Travel;
            if (step.Blocked)
            {
                Vector3 n = new Vector3(step.WallNormal.x, 0f, step.WallNormal.z);
                if (n.sqrMagnitude > 1e-6f)
                {
                    n.Normalize();
                    Heading = Heading - 2f * Vector3.Dot(Heading, n) * n;
                    Heading.y = 0f;
                    Heading = Heading.sqrMagnitude > 1e-6f ? Heading.normalized : -n;
                }
                Speed *= wallRebound;
                Bounced = true;
                WallNormal = step.WallNormal;
            }
            return step.Grounded;
        }

        public Vector3 AirVelocity => Heading * Speed;
    }
}

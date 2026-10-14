using SplatoonC.Core.Combat;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // Curling Bomb 的逐幀運動(含碰撞):CurlingBomb 實飛與 CurlingBombConfig.BuildPreview 預覽共用,預覽 = 實際。
    // 單位:位置公尺、速度 u/F(= 公尺/幀),一次 Step = 一個 60 Hz 幀(S3 原始參數直接可用)。
    //   空中:CurlingBombModel.StepAir(阻力 + 重力);撞地(法線 y ≥ 0.6)→ 落地保留水平速度;撞牆 → 水平鏡射。
    //   地面:速度往巡航收斂(CurlingBombModel),GroundWalker 沿地面平面前進;撞牆(陡面)先試著爬上小台階
    //         (≤ StepHeight),爬不上就水平鏡射(S3:入射角 = 反射角);腳下沒地面 → 滑出邊緣變回空中。
    // 只做地形(CombatMasks.Terrain)查詢;接觸傷害、塗墨、音效由 CurlingBomb 依本步的事件處理。
    public struct CurlingMotion
    {
        private const float FloorNormalY = GroundWalker.FloorNormalY;
        private const float Skin = GroundWalker.Skin;

        public Vector3 Position;
        public Vector3 Velocity;
        public Vector3 Heading;
        public bool Grounded;
        public Vector3 GroundNormal;
        public Vector3 GroundPoint;
        public int Frame;
        public int Fuse;
        public float Charge01;
        public float Radius;
        public float StepHeight;

        // 本步事件
        public bool Landed;
        public bool Bounced;
        public Vector3 BounceNormal;
        public float GroundTravel;

        public int RestFrames => Fuse - Frame;
        public bool Finished => Frame >= Fuse;

        // velocityPerFrame:出手初速(u/F)
        public void Init(Vector3 position, Vector3 velocityPerFrame, float charge01, in CurlingBombParams p,
            float radius, float stepHeight)
        {
            Position = position;
            Velocity = velocityPerFrame;
            Vector3 flat = new Vector3(velocityPerFrame.x, 0f, velocityPerFrame.z);
            Heading = flat.sqrMagnitude > 1e-8f ? flat.normalized : Vector3.forward;
            Grounded = false;
            GroundNormal = Vector3.up;
            GroundPoint = position;
            Frame = 0;
            Charge01 = Mathf.Clamp01(charge01);
            Fuse = CurlingBombModel.FuseFrames(p, Charge01);
            Radius = radius;
            StepHeight = stepHeight;
            Landed = false;
            Bounced = false;
            GroundTravel = 0f;
        }

        // 一幀;回傳 false = 引信到(該爆炸了,空中也爆)
        public bool Step(in CurlingBombParams p)
        {
            Frame++;
            Landed = false;
            Bounced = false;
            GroundTravel = 0f;
            if (Grounded)
            {
                StepGround(p);
            }
            else
            {
                StepAir(p);
            }
            return !Finished;
        }

        private void StepAir(in CurlingBombParams p)
        {
            Vector3 from = Position;
            Vector3 to = from;
            Vector3 velocity = Velocity;
            CurlingBombModel.StepAir(p, ref to, ref velocity);
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance > 1e-6f && Physics.SphereCast(from, Radius, delta / distance, out RaycastHit hit, distance,
                    CombatMasks.Terrain, QueryTriggerInteraction.Ignore))
            {
                Vector3 direction = delta / distance;
                Vector3 normal = hit.normal;
                Position = from + direction * hit.distance + normal * Skin;
                if (normal.y >= FloorNormalY)
                {
                    // 落地:保留水平速度,開始滑行
                    Grounded = true;
                    Landed = true;
                    GroundNormal = normal;
                    GroundPoint = hit.point;
                    Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
                    if (flat.sqrMagnitude > 1e-10f)
                    {
                        Heading = flat.normalized;
                    }
                    Velocity = flat;
                    return;
                }
                if (normal.y <= -FloorNormalY)
                {
                    // 天花板:垂直速度反向變小
                    velocity.y = -Mathf.Abs(velocity.y) * 0.3f;
                    Velocity = velocity;
                    return;
                }
                Velocity = ReflectHorizontal(velocity, normal);
                Vector3 flatAfter = new Vector3(Velocity.x, 0f, Velocity.z);
                if (flatAfter.sqrMagnitude > 1e-10f)
                {
                    Heading = flatAfter.normalized;
                }
                Bounced = true;
                BounceNormal = normal;
                return;
            }
            Position = to;
            Velocity = velocity;
            Vector3 flatVelocity = new Vector3(velocity.x, 0f, velocity.z);
            if (flatVelocity.sqrMagnitude > 1e-10f)
            {
                Heading = flatVelocity.normalized;
            }
        }

        private void StepGround(in CurlingBombParams p)
        {
            float speed = new Vector2(Velocity.x, Velocity.z).magnitude;
            speed = CurlingBombModel.StepGroundSpeed(p, speed,
                CurlingBombModel.TargetSpeed(p, Charge01, RestFrames));
            if (speed < 1e-5f)
            {
                Velocity = Vector3.zero;
                return;
            }
            GroundStep step = GroundWalker.Move(ref Position, ref GroundNormal, ref GroundPoint, Heading, speed,
                Radius, StepHeight);
            if (step.Blocked)
            {
                // S3:牆面鏡射(入射角 = 反射角),速度不減
                Heading = ReflectHorizontal(Heading, step.WallNormal);
                Bounced = true;
                BounceNormal = step.WallNormal;
            }
            Grounded = step.Grounded;
            GroundTravel = step.Travel;
            Velocity = new Vector3(Heading.x, 0f, Heading.z) * speed;
        }

        // 水平鏡射(S3 牆面反射:入射角 = 反射角);垂直分量不變
        public static Vector3 ReflectHorizontal(Vector3 velocity, Vector3 wallNormal)
        {
            Vector3 n = new Vector3(wallNormal.x, 0f, wallNormal.z);
            if (n.sqrMagnitude < 1e-8f)
            {
                return velocity;
            }
            n.Normalize();
            float along = velocity.x * n.x + velocity.z * n.z;
            if (along >= 0f)
            {
                return velocity;
            }
            return velocity - n * (2f * along);
        }
    }
}

using SplatoonC.Core;
using SplatoonC.Core.Combat;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // S3 通用投擲物的逐幀空中飛行(Torpedo / Fizzy Bomb / Point Sensor 的實飛,與各自設定的 BuildPreview 共用 → 預覽 = 實際)。
    // 位置公尺、速度 u/F(一次 Step = 一個 60 Hz 幀,S3 原始參數直接可用):S3ThrowModel.StepAir(阻力 + 重力)後,
    // 這一步以半徑 radius 做 BombRules.CastPath(地形、會引爆炸彈的敵方屏障與 Big Bubbler 球罩、hitTargets 時含敵方/中立目標)。
    public struct S3ThrownFlight
    {
        public Vector3 Position;
        public Vector3 Velocity;

        public void Init(Vector3 position, Vector3 velocityPerFrame)
        {
            Position = position;
            Velocity = velocityPerFrame;
        }

        // 一幀。沒碰到 → 前進、回傳 None;碰到 → Position 停在接觸時的球心、Velocity = 這一幀步進後的速度
        //(呼叫端決定反彈/落地/爆炸),hit / target 為 CastPath 的接觸資訊
        public BombContact Step(in S3ThrowParams p, float radius, Team team, bool hitTargets, out RaycastHit hit,
            out IDamageable target)
        {
            Vector3 from = Position;
            Vector3 to = from;
            Vector3 velocity = Velocity;
            S3ThrowModel.StepAir(p, ref to, ref velocity);
            BombContact contact = BombRules.CastPath(from, to, radius, team, hitTargets, out hit, out target);
            Velocity = velocity;
            if (contact == BombContact.None)
            {
                Position = to;
                return contact;
            }
            Vector3 delta = to - from;
            float length = delta.magnitude;
            Position = length > 1e-6f ? from + delta / length * hit.distance : from;
            return contact;
        }
    }
}

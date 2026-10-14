using System;
using UnityEngine;

namespace SplatoonC.Core.Combat
{
    // Autobomb 的 S3 參數(Leanny v11.3.0 WeaponBombRobot;u = m、F = 1/60 s)。設定資產內嵌一份。
    [Serializable]
    public struct AutobombParams
    {
        [Header("搜尋(落地後)")]
        [Tooltip("水平搜尋半徑(u;S3 9.2.0 起 12.5)")]
        public float SearchRadius;

        [Tooltip("上下搜尋範圍(u;S2 mTargetSearchUp/DownHeight 50 DU = ±5)")]
        public float SearchHeight;

        [Tooltip("落地後持續搜尋的幀數(F;[EST] 搜尋圈展開期間)")]
        public int SearchFrames;

        [Tooltip("找到目標後轉向的幀數(F;S3 FindRotateFrame 10)")]
        public int FindRotateFrames;

        [Header("追蹤(u/F)")]
        [Tooltip("最長追蹤(F;S3 ChaseFrame 150)")]
        public int ChaseFrames;

        [Tooltip("每幀加速(S3 ChaseGroundAddSpeedPerFrame 0.003)")]
        public float ChaseAcceleration;

        [Tooltip("最高速(S3 ChaseGroundMaxSpeed 0.09 = 5.4 m/s,游得過)")]
        public float ChaseMaxSpeed;

        [Tooltip("每幀最大轉向(度;S3 ChaseYawDegree 2.0)")]
        public float YawDegreesPerFrame;

        [Tooltip("目標死亡時走到最後位置多近就開始倒數(u;S3 DeathTargetApprouchDistance 0.65)")]
        public float DeathTargetApproachDistance;

        [Header("引爆倒數(F)")]
        [Tooltip("追到目標(進入近距半徑)後爆炸前的倒數(S3 FindApproachBurstWaitFrame 60)")]
        public int ApproachBurstWaitFrames;

        [Tooltip("追滿 150F 仍沒追到的倒數(S3 ChaseBurstWaitFrame 60)")]
        public int ChaseBurstWaitFrames;

        [Tooltip("沒找到目標的倒數(從落地起算;S3 UnfindBurstWaitFrame 60)")]
        public int UnfindBurstWaitFrames;

        [Tooltip("倒數時每幀速度保留比例([EST] 追到後「幾乎停住、膨脹」)")]
        public float WarningSpeedKeep;

        public static AutobombParams S3 => new AutobombParams
        {
            SearchRadius = 12.5f,
            SearchHeight = 5f,
            SearchFrames = 10,
            FindRotateFrames = 10,
            ChaseFrames = 150,
            ChaseAcceleration = 0.003f,
            ChaseMaxSpeed = 0.09f,
            YawDegreesPerFrame = 2f,
            DeathTargetApproachDistance = 0.65f,
            ApproachBurstWaitFrames = 60,
            ChaseBurstWaitFrames = 60,
            UnfindBurstWaitFrames = 60,
            WarningSpeedKeep = 0.85f,
        };
    }

    // Autobomb 的純邏輯(research 03 §2.8):落地後在水平 12.5 u、上下 ±5 u 內找最近的敵人 → 轉向 10F →
    // 追最多 150F(每幀 +0.003、最高 0.09 u/F、每幀最多轉 2°;不會爬牆)→ 進入近距半徑或追滿 → 60F 後爆炸;
    // 沒找到 → 落地 60F 後爆炸。
    public static class AutobombModel
    {
        // 目標相對炸彈的位移是否在搜尋範圍(水平圓柱)內
        public static bool InSearchWindow(in AutobombParams p, Vector3 offset)
        {
            if (Mathf.Abs(offset.y) > p.SearchHeight)
            {
                return false;
            }
            return offset.x * offset.x + offset.z * offset.z <= p.SearchRadius * p.SearchRadius;
        }

        // 追蹤加速一幀(u/F)
        public static float StepChaseSpeed(in AutobombParams p, float speed)
        {
            return Mathf.Min(speed + p.ChaseAcceleration, p.ChaseMaxSpeed);
        }

        // 轉向一幀:往 desiredYaw 最多轉 YawDegreesPerFrame 度
        public static float StepYaw(in AutobombParams p, float yaw, float desiredYaw)
        {
            return Mathf.MoveTowardsAngle(yaw, desiredYaw, p.YawDegreesPerFrame);
        }

        // 從 0 加速到最高速要幾幀
        public static int FramesToMaxSpeed(in AutobombParams p)
        {
            return p.ChaseAcceleration > 0f ? Mathf.CeilToInt(p.ChaseMaxSpeed / p.ChaseAcceleration - 1e-4f) : 0;
        }

        // 從靜止追 frames 幀走的距離(u,平地直線)
        public static float ChaseDistance(in AutobombParams p, int frames)
        {
            float speed = 0f;
            float distance = 0f;
            for (int i = 0; i < frames; i++)
            {
                speed = StepChaseSpeed(p, speed);
                distance += speed;
            }
            return distance;
        }
    }
}

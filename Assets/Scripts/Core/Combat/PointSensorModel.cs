using System;
using UnityEngine;

namespace SplatoonC.Core.Combat
{
    // Point Sensor 的 S3 參數(Leanny v11.3.0 WeaponPointSensor;u = m、F = 1/60 s)。
    [Serializable]
    public struct PointSensorParams
    {
        [Tooltip("通用投擲彈道(SpawnSpeedZ 1.38 u/F)")]
        public S3ThrowParams Throw;

        [Tooltip("碰撞半徑(u;[EST] S3 檔案未列 GuideRadius)")]
        public float CollisionRadius;

        [Tooltip("感應範圍半徑(u;S3 AreaParam.Distance 6.0,穿牆)")]
        public float Radius;

        [Tooltip("範圍停留幀數(S3 150F = 2.5 s)")]
        public int LingerFrames;

        [Tooltip("標記幀數(S3 MarkingFrameSubSpec.Low 480,從命中起算)")]
        public int MarkingFrames;

        public static PointSensorParams S3 => new PointSensorParams
        {
            Throw = S3ThrowParams.Bomb(1.38f),
            CollisionRadius = 0.18f,
            Radius = 6f,
            LingerFrames = 150,
            MarkingFrames = 480,
        };
    }

    // Point Sensor 純邏輯(research 03 §2.10、DESIGN_REFERENCE §4.3):碰到任何東西(地形、敵人)就爆開,
    // 半徑 6.0 的感應球停留 150F、穿牆;範圍內的敵人被標記 480F(從命中起算:晚進來的標記較短,重複標記取長者)。
    public static class PointSensorModel
    {
        // 命中後第 framesSinceBurst 幀進入範圍:標記秒數(範圍消失後 = 0)
        public static float MarkSecondsAt(in PointSensorParams p, int framesSinceBurst)
        {
            if (framesSinceBurst < 0 || framesSinceBurst >= p.LingerFrames)
            {
                return 0f;
            }
            return Mathf.Max(0, p.MarkingFrames - framesSinceBurst) / 60f;
        }

        public static bool Inside(in PointSensorParams p, Vector3 center, Vector3 point)
        {
            return (point - center).sqrMagnitude <= p.Radius * p.Radius;
        }
    }
}

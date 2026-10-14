using UnityEngine;

namespace SplatoonC.Core.Combat
{
    // S3 三狀態彈道的瞄準補償(純邏輯):TPS 準心射線上的目標點在槍口前 d 公尺時,彈要略往上發,
    // 才會在 d 處剛好穿過準心那條線(直線段沒有重力,進入減速後每幀 −0.07 u/F² 開始下墜)。
    // 以 BulletBallistics 逐幀模擬水平發射,查「水平飛到 d 時比發射線低多少」,呼叫端把目標點抬高這麼多。
    public static class BulletAim
    {
        // 水平發射、水平飛行 horizontalDistance 公尺時的下墜量(公尺,≥ 0)。飛不到(自由落體耗盡水平速度)就回傳最後的下墜。
        public static float DropAtDistance(in BulletMotion motion, float horizontalDistance, int maxFrames = 180)
        {
            if (horizontalDistance <= 0f)
            {
                return 0f;
            }
            BulletState state = BulletBallistics.Launch(Vector3.zero, Vector3.forward, motion);
            for (int i = 0; i < maxFrames; i++)
            {
                Vector3 before = state.Position;
                BulletBallistics.Step(ref state, motion);
                if (state.Position.z >= horizontalDistance)
                {
                    float span = state.Position.z - before.z;
                    float t = span > 1e-6f ? (horizontalDistance - before.z) / span : 1f;
                    return -Mathf.Lerp(before.y, state.Position.y, Mathf.Clamp01(t));
                }
            }
            return -state.Position.y;
        }

        // 從 origin 射向 target(公尺)的發射方向:把目標抬高「飛到該水平距離的下墜量」,迭代兩次讓抬升後的距離收斂
        public static Vector3 CompensatedDirection(in BulletMotion motion, Vector3 origin, Vector3 target)
        {
            Vector3 aimPoint = target;
            for (int i = 0; i < 2; i++)
            {
                Vector3 flat = aimPoint - origin;
                flat.y = 0f;
                float drop = DropAtDistance(motion, flat.magnitude);
                aimPoint = target + Vector3.up * drop;
            }
            Vector3 direction = aimPoint - origin;
            return direction.sqrMagnitude > 1e-8f ? direction.normalized : Vector3.forward;
        }

        // 精準版:用 BulletBallistics 實際模擬「朝瞄準點發射」的這一發,量它水平飛到目標距離時比目標高或低多少,
        // 把瞄準點反向修正,迭代到誤差 < 1 cm。CompensatedDirection 以水平發射近似下墜,對明顯上拋的曲射彈
        //(Crab Tank 砲彈:直線 8F 後自由落體)會差到 1 m 以上;這裡的誤差只剩逐幀插值
        public static Vector3 SolvedDirection(in BulletMotion motion, Vector3 origin, Vector3 target, int iterations = 10)
        {
            Vector3 flat = target - origin;
            flat.y = 0f;
            float distance = flat.magnitude;
            if (distance < 1e-3f)
            {
                Vector3 straight = target - origin;
                return straight.sqrMagnitude > 1e-8f ? straight.normalized : Vector3.forward;
            }
            Vector3 flatDirection = flat / distance;
            Vector3 aimPoint = target;
            for (int i = 0; i < iterations; i++)
            {
                Vector3 direction = (aimPoint - origin).normalized;
                if (!HeightAtDistance(motion, origin, direction, flatDirection, distance, out float height))
                {
                    // 飛不到目標距離:抬高再試(曲射在 45° 前越高越遠)
                    aimPoint.y += Mathf.Max(1f, distance * 0.25f);
                    continue;
                }
                float error = target.y - height;
                if (Mathf.Abs(error) < 0.01f)
                {
                    break;
                }
                aimPoint.y += error;
            }
            return (aimPoint - origin).normalized;
        }

        // 從 origin 沿 direction 發射、水平(沿 flatDirection)飛到 distance 時的高度(世界 y);飛不到回傳 false
        private static bool HeightAtDistance(in BulletMotion motion, Vector3 origin, Vector3 direction, Vector3 flatDirection,
            float distance, out float height, int maxFrames = 300)
        {
            BulletState state = BulletBallistics.Launch(origin, direction, motion);
            for (int i = 0; i < maxFrames; i++)
            {
                Vector3 before = state.Position;
                BulletBallistics.Step(ref state, motion);
                float d0 = Vector3.Dot(before - origin, flatDirection);
                float d1 = Vector3.Dot(state.Position - origin, flatDirection);
                if (d1 >= distance)
                {
                    float t = d1 - d0 > 1e-6f ? (distance - d0) / (d1 - d0) : 1f;
                    height = Mathf.Lerp(before.y, state.Position.y, Mathf.Clamp01(t));
                    return true;
                }
                if (d1 <= d0 + 1e-5f && state.Position.y < origin.y - 50f)
                {
                    break;
                }
            }
            height = 0f;
            return false;
        }
    }
}

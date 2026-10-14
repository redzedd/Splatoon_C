using UnityEngine;

namespace SplatoonC.Core.Animation
{
    // 兩骨 IK(手臂:肩→肘→腕、腿:髖→膝→踝):解析解(餘弦定理),不迭代、零配置。
    // 所有向量在同一個座標空間(角色模型空間),骨長固定——目標太遠時末端停在最遠可及處,不拉長骨頭。
    public static class TwoBoneIk
    {
        // 回傳中間關節位置;reachedEnd = 末端實際到達的位置(目標可及時等於目標)。
        // pole = 中間關節要「凸向」的方向(手肘往後下、膝蓋往前),只取它垂直於 根→目標 的分量。
        public static Vector3 SolveMid(Vector3 root, Vector3 target, Vector3 pole, float upperLength, float lowerLength,
            out Vector3 reachedEnd)
        {
            Vector3 toTarget = target - root;
            float distance = toTarget.magnitude;
            Vector3 direction = distance > 1e-5f ? toTarget / distance : Vector3.down;
            // 留一點點彎度(0.9995):完全打直時彎曲平面不定,肘/膝的滾轉會抖
            float maxReach = (upperLength + lowerLength) * 0.9995f;
            float minReach = Mathf.Abs(upperLength - lowerLength) + 1e-4f;
            float d = Mathf.Clamp(distance, minReach, maxReach);
            reachedEnd = root + direction * d;

            float along = (upperLength * upperLength - lowerLength * lowerLength + d * d) / (2f * d);
            float height = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));

            Vector3 bend = pole - direction * Vector3.Dot(pole, direction);
            if (bend.sqrMagnitude < 1e-8f)
            {
                // pole 與根→目標平行:任取一個垂直方向(先試世界前方,再試上方)
                bend = Vector3.Cross(direction, Vector3.right);
                if (bend.sqrMagnitude < 1e-8f)
                {
                    bend = Vector3.Cross(direction, Vector3.up);
                }
            }
            bend.Normalize();
            return root + direction * along + bend * height;
        }

        // 由三個關節位置組出上/下骨的旋轉(骨的 +Y 沿骨頭、+Z 為骨的「前面」,與 FBX 骨架 roll 慣例相同)。
        // frontTowardsPole:腿 = true(膝蓋那一側就是大腿前面)、手臂 = false(手肘凸向後方,手臂前面在另一側)。
        // 上下骨共用同一條鉸鏈軸(局部 X),蒙皮權重就是照這個軸做的,肘/膝不會扭。
        public static void BoneRotations(Vector3 root, Vector3 mid, Vector3 end, Vector3 pole, bool frontTowardsPole,
            out Quaternion upper, out Quaternion lower)
        {
            Vector3 upperY = mid - root;
            upperY = upperY.sqrMagnitude > 1e-10f ? upperY.normalized : Vector3.down;
            // 中間關節偏離「根→末端」直線的方向 = 肘/膝凸出的方向;由實際位置取得,保證在彎曲平面內
            Vector3 axis = end - root;
            axis = axis.sqrMagnitude > 1e-10f ? axis.normalized : upperY;
            Vector3 bulge = (mid - root) - axis * Vector3.Dot(mid - root, axis);
            if (bulge.sqrMagnitude < 1e-8f)
            {
                // 完全打直:平面不定,改用 pole
                bulge = pole - axis * Vector3.Dot(pole, axis);
            }
            Vector3 front = bulge - upperY * Vector3.Dot(bulge, upperY);
            if (front.sqrMagnitude < 1e-8f)
            {
                front = Vector3.Cross(upperY, Vector3.right);
            }
            front.Normalize();
            if (!frontTowardsPole)
            {
                front = -front;
            }
            upper = Quaternion.LookRotation(front, upperY);

            Vector3 hinge = Vector3.Cross(upperY, front);
            Vector3 lowerY = end - mid;
            lowerY = lowerY.sqrMagnitude > 1e-10f ? lowerY.normalized : upperY;
            Vector3 lowerFront = Vector3.Cross(hinge, lowerY);
            if (lowerFront.sqrMagnitude < 1e-8f)
            {
                lowerFront = front;
            }
            lower = Quaternion.LookRotation(lowerFront.normalized, lowerY);
        }
    }
}

using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 一步貼地移動的結果
    public struct GroundStep
    {
        // 移動後腳下仍有地面(否則滑出/走出邊緣,改成空中)
        public bool Grounded;
        // 前方是爬不上的牆(陡面),停在牆前
        public bool Blocked;
        public Vector3 WallNormal;
        // 爬上了矮台階
        public bool SteppedUp;
        // 實際移動距離(公尺)
        public float Travel;
    }

    // 貼地移動(Curling 滑行、Autobomb 走路共用):球心 position 離地 radius,沿地面平面往 direction 走 distance。
    //   前方陡面(法線 y < 0.6):頂面比腳下高 ≤ stepHeight 就爬上去,否則停在牆前(Blocked,呼叫端決定反射或原地踏步);
    //   移動後往下找地面:找到就貼上去(更新 groundNormal/groundPoint),找不到 → Grounded = false(掉落)。
    // 只查地形(CombatMasks.Terrain);零配置。
    public static class GroundWalker
    {
        public const float FloorNormalY = 0.6f;
        public const float Skin = 0.02f;

        public static GroundStep Move(ref Vector3 position, ref Vector3 groundNormal, ref Vector3 groundPoint,
            Vector3 direction, float distance, float radius, float stepHeight)
        {
            var result = new GroundStep { Grounded = true };
            if (distance <= 1e-6f)
            {
                return result;
            }
            Vector3 along = Vector3.ProjectOnPlane(direction, groundNormal);
            along = along.sqrMagnitude > 1e-8f ? along.normalized : direction;
            Vector3 from = position;
            Vector3 target = from + along * distance;

            // 前方的牆:略縮半徑、略抬高掃,地面起伏不算牆
            Vector3 castFrom = from + groundNormal * 0.03f;
            if (Physics.SphereCast(castFrom, radius * 0.9f, along, out RaycastHit wall, distance + Skin,
                    CombatMasks.Terrain, QueryTriggerInteraction.Ignore) && wall.normal.y < FloorNormalY)
            {
                if (TryStepUp(from, along, wall.distance, radius, stepHeight, out Vector3 stepTarget))
                {
                    target = stepTarget;
                    result.SteppedUp = true;
                }
                else
                {
                    target = from + along * Mathf.Max(0f, wall.distance - Skin);
                    result.Blocked = true;
                    result.WallNormal = wall.normal;
                }
            }

            // 貼地:往下找地面;太陡、太高(不是剛爬上的台階)或找不到 → 掉落
            Vector3 probe = target + Vector3.up * (stepHeight + radius);
            if (Physics.Raycast(probe, Vector3.down, out RaycastHit ground, stepHeight * 2f + radius + 0.6f,
                    CombatMasks.Terrain, QueryTriggerInteraction.Ignore) && ground.normal.y >= FloorNormalY
                && ground.point.y <= from.y - radius + stepHeight + 0.05f)
            {
                groundNormal = ground.normal;
                groundPoint = ground.point;
                target = ground.point + ground.normal * (radius + Skin);
            }
            else
            {
                result.Grounded = false;
            }
            result.Travel = Vector3.Distance(from, target);
            position = target;
            return result;
        }

        // 前方是矮台階(頂面比目前地面高 ≤ stepHeight,上方有空間):回傳爬上去後的球心
        private static bool TryStepUp(Vector3 from, Vector3 along, float wallDistance, float radius, float stepHeight,
            out Vector3 target)
        {
            target = from;
            if (stepHeight <= 0f)
            {
                return false;
            }
            float groundY = from.y - radius - Skin;
            Vector3 flat = new Vector3(along.x, 0f, along.z);
            if (flat.sqrMagnitude < 1e-8f)
            {
                return false;
            }
            flat.Normalize();
            Vector3 top = from + flat * (wallDistance + radius + 0.05f);
            top.y = groundY + stepHeight + radius + 0.1f;
            if (!Physics.Raycast(top, Vector3.down, out RaycastHit step, stepHeight + radius + 0.2f,
                    CombatMasks.Terrain, QueryTriggerInteraction.Ignore) || step.normal.y < FloorNormalY)
            {
                return false;
            }
            if (step.point.y - groundY > stepHeight + 0.01f)
            {
                return false;
            }
            Vector3 raised = new Vector3(from.x, step.point.y + radius + Skin, from.z);
            if (Physics.SphereCast(raised, radius * 0.9f, flat, out _, wallDistance + radius, CombatMasks.Terrain,
                    QueryTriggerInteraction.Ignore))
            {
                return false;
            }
            target = new Vector3(top.x, step.point.y + radius + Skin, top.z);
            return true;
        }
    }
}

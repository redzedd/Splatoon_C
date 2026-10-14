using UnityEngine;

namespace SplatoonC.Core.Combat
{
    // Tacticooler(S3 SpEnergyStand,research/04 §2.15、JP wiki エナジースタンド;v11.3.0 0 AP)的純邏輯。
    //   投擲:13F 前隙,初速 0.4 u/F + 上 0.07 u/F,重力 0.009 u/F²(空氣阻力 0.001 可忽略),不繼承慣性。
    //   著地坡度 ≤ 30°(CanPlaceBoundDegree)才立起來;飲料機 900F(塔上 450F),擋雙方的墨、打不壞。
    //   飲料 4 罐、一人一罐(同一台喝過就不能再喝,死了也一樣);人形的受擊膠囊碰到「飲料機底面中心起、
    //   水平半徑 7、往上 3、往下 0」的圓柱就拿到(隔牆也行)。增益 1020F(SPU 1500F),死亡失效。
    //   能量:飲料機展開後鎖定 600F(v5.0.0 起),飲料機消失 → 立刻歸 0;新的一台在舊的 1.8 u 內立起 → 舊的消失。
    public static class TacticoolerModel
    {
        public const int ThrowLagFrames = 13;
        public const float ThrowSpeed = 0.4f;
        public const float ThrowSpeedY = 0.07f;
        public const float Gravity = 0.009f;
        public const float CanPlaceDegree = 30f;

        public const int StandFrames = 900;
        public const int StandFramesOnTower = 450;
        public const int LockFrames = 600;
        public const int BuffFrames = 1020;
        public const int BuffFramesMax = 1500;
        public const int Drinks = 4;

        public const float ServeRadius = 7f;
        public const float ServeHeightUp = 3f;
        public const float ServeHeightDown = 0f;
        public const float VanishDistance = 1.8f;

        // 展開動畫(罐子打開、四支手臂伸出)[EST:S3 未公開,目視約 0.5 秒;飲料要展開完才拿得到]
        public const int UnfoldFrames = 30;

        // 人形受擊膠囊(腳底 feet、半徑 bodyRadius、高 bodyHeight)有沒有碰到供應圓柱(standBase = 飲料機底面中心)
        public static bool InServeArea(Vector3 standBase, Vector3 feet, float bodyRadius, float bodyHeight)
        {
            float dx = feet.x - standBase.x;
            float dz = feet.z - standBase.z;
            float reach = ServeRadius + bodyRadius;
            if (dx * dx + dz * dz > reach * reach)
            {
                return false;
            }
            float bottom = standBase.y - ServeHeightDown;
            float top = standBase.y + ServeHeightUp;
            return feet.y <= top && feet.y + bodyHeight >= bottom;
        }

        // 坡面法線能不能立起來(≤ 30°)
        public static bool CanPlaceOn(Vector3 normal)
        {
            return normal.y >= Mathf.Cos(CanPlaceDegree * Mathf.Deg2Rad) - 1e-4f;
        }
    }
}

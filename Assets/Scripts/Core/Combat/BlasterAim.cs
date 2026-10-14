using UnityEngine;

namespace SplatoonC.Core.Combat
{
    // 爆破槍的 bot 瞄準幾何(純邏輯;物理檢查在 BlasterWeapon):
    //   空爆距離以內瞄目標腳邊(S3 的「瞄腳邊」):瞄準線穿過小腿 → 直擊 125;預判偏了,彈擦過目標身旁、
    //   在腳邊撞地爆(撞地形爆風 × 0.42,外圈 1.43 m,腳邊仍吃 50~70)。瞄胸口打偏的彈會飛到身後才爆(身後幾公尺,炸不到)。
    //   瞄點不能是地面本身:彈對地形的判定半徑 0.2 m,平射斜向地面時,彈心降到離地 0.2 m 就撞地——瞄地面(高 0)
    //   會在目標前方約 0.7 m 就撞地小爆,永遠碰不到小腿(對人判定 0.285 + 角色半徑 0.4 = 0.685 m 的膨脹膠囊)。
    //   瞄離地 0.3 m(地形判定 0.2 + 0.1 餘裕):撞地點落在目標腳底正後方,彈一定先進入膨脹膠囊 → 直擊優先,打偏才撞地爆。
    //   目標在空爆距離附近時瞄胸口:彈在目標身邊空爆,完整爆風半徑(Blaster 3.385)包住目標。
    //   目標躲到掩體後:瞄掩體上緣之上、讓彈飛過掩體後在目標上方空爆(爆風沒有被牆擋住就炸得到)。
    public static class BlasterAim
    {
        // 腳邊 / 胸口的瞄準高度(公尺,離目標腳底;腳邊 = 小腿,見上方說明)
        public const float FeetAimHeight = 0.3f;
        public const float ChestAimHeight = 0.9f;

        // 從瞄腳邊轉成瞄胸口的距離帶:空爆距離 − 2.5 m 起、到空爆距離 − 0.8 m 全部瞄胸口
        //(Blaster 7.4 → 9.1 m、Luna 4.8 → 6.5 m;再遠彈在目標前方空爆,外圈 3.4 m 仍包得到)
        public const float ChestBlendStart = 2.5f;
        public const float ChestBlendEnd = 0.8f;

        // 依距離決定瞄準高度:空爆距離以內瞄腳邊,接近空爆距離改瞄胸口
        public static float AimHeight(float distance, float burstDistance)
        {
            if (burstDistance <= 1e-3f)
            {
                return ChestAimHeight;
            }
            float t = Mathf.InverseLerp(burstDistance - ChestBlendStart, burstDistance - ChestBlendEnd, distance);
            return Mathf.Lerp(FeetAimHeight, ChestAimHeight, t);
        }

        // 從 origin 朝 aimPoint 射,飛到 burstDistance 時的空爆點(直線近似;武器會補償彈道下墜讓彈穿過瞄準點)
        public static Vector3 BurstPoint(Vector3 origin, Vector3 aimPoint, float burstDistance)
        {
            Vector3 dir = aimPoint - origin;
            float length = dir.magnitude;
            if (length < 1e-4f)
            {
                return origin;
            }
            return origin + dir * (burstDistance / length);
        }

        // 掩體後的空爆能不能炸到:空爆點離目標(胸口)在外圈半徑 × margin 以內
        public static bool BurstReaches(Vector3 burstPoint, Vector3 targetPoint, float farRadius, float margin = 0.85f)
        {
            return (burstPoint - targetPoint).sqrMagnitude <= farRadius * margin * farRadius * margin;
        }

        // 掩體空爆的候選瞄準高度(離目標腳底,公尺;由低到高試,第一個「槍口到空爆點沒被擋」的就用)
        public static float CoverAimHeight(int index)
        {
            return 1.2f + index * 0.7f;
        }

        public const int CoverAimCandidates = 5;
    }
}

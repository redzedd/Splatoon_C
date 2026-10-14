using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 戰鬥查詢共用的圖層遮罩:地形 = 所有預設可射線圖層扣掉角色與目標;目標 = 角色(Inkling)+ 目標(假人、場上裝置)。
    // 場上裝置(DeployableObject)一律放 Target 圖層:子彈用「對人半徑」打它,爆炸視線只在它明確要擋時才擋。
    public static class CombatMasks
    {
        public const int TargetLayerMask = 1 << GameLayers.Target;

        // 可受傷目標所在圖層(對人判定半徑的 sphere cast 用)
        public const int Targets = GameLayers.InklingMask | TargetLayerMask;

        // 地形(對地形判定半徑的 sphere cast、爆炸視線、噴墨射線用)
        public const int Terrain = Physics.DefaultRaycastLayers & ~Targets;
    }
}

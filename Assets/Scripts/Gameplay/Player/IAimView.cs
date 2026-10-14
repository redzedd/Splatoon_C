using UnityEngine;

namespace SplatoonC.Gameplay.Player
{
    // 角色的「視線」:瞄準射線(準心)與移動基準 yaw 的來源。
    // 玩家 = 第三人稱相機(ThirdPersonCameraRig);bot = BotAimView(沒有相機,腦袋直接設角度)。
    // 射擊/投擲/移動只認這個介面,同一套元件因此能被玩家或 bot 驅動。
    public interface IAimView
    {
        // 準心射線起點(玩家 = 相機位置)
        Vector3 AimOrigin { get; }

        // 準心射線方向(單位向量)
        Vector3 AimForward { get; }

        // 移動輸入的參考 yaw(度)
        float Yaw { get; }
    }
}

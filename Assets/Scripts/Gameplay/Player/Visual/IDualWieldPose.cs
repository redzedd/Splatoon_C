using UnityEngine;

namespace SplatoonC.Gameplay.Player
{
    // 雙持武器(雙槍)的姿勢來源:主武器元件實作此介面且 DualWieldActive 時,CharacterVisual 改用
    // 雙手各持一把的姿勢(右手 WeaponSocket、左手 WeaponSocketLeft)、依各手出膛數做後座力、
    // 砲台模式蹲低並雙槍併攏、翻滾時整個身體沿翻滾方向滾一圈。只讀狀態,不改玩法。
    public interface IDualWieldPose
    {
        bool DualWieldActive { get; }

        // 各手實際出膛數(左 / 右;後座力輪流打在出膛的那隻手)
        int LeftShots { get; }
        int RightShots { get; }

        // 砲台模式(翻滾後定身、雙槍併攏)
        bool TurretStance { get; }

        // 翻滾進度:< 0 = 沒在翻滾;0 → 1 = 起手到翻滾位移結束(身體滾一圈的相位)
        float RollProgress { get; }

        // 翻滾方向(世界座標、水平單位向量)
        Vector3 RollDirection { get; }
    }
}

using UnityEngine;

namespace SplatoonC.Gameplay.Player
{
    // 長槍(狙擊槍)的持槍姿勢來源:主武器元件實作此介面且 LongGunActive 時,CharacterVisual 的持槍姿勢
    // 改用它給的握點——瞄準時槍托靠在肩上(右手握把較靠近身體)、左手往前托在護木下,而不是射擊槍那種
    // 短槍的持法;每發後座力依 RecoilScale 放大(滿蓄一發比部分蓄力重)。只讀狀態,不改玩法。
    public interface ILongGunPose
    {
        // 長槍模型掛上且可見(否則沿用預設持槍姿勢)
        bool LongGunActive { get; }

        // 瞄準時右手握把點相對右肩(槍的座標:右、上、前;公尺)
        Vector3 LongGunGripOffset { get; }

        // 瞄準時左手扶槍點相對握把(槍的座標;公尺)
        Vector3 LongGunSupportOffset { get; }

        // 最近一發的後座力倍率(1 = 射擊槍一發)
        float LongGunRecoilScale { get; }
    }
}

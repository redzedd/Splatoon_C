using UnityEngine;

namespace SplatoonC.Gameplay.Player
{
    // 單槍持握姿勢的提示(大型單發槍,例:爆破槍):主武器元件實作此介面且 GunStanceActive 時,CharacterVisual 的
    // 持槍瞄準姿勢改用這組握把 / 扶槍點、每發後座力放大,並在出膛前的預備時間做蓄勢動作(槍往後收、槍口微抬)。
    // 只讀狀態,不改玩法;沒實作的主武器 = 原本的射擊槍姿勢。
    public interface IGunStancePose
    {
        bool GunStanceActive { get; }

        // 握把點相對右肩(公尺;槍的座標:右、上、前)
        Vector3 StanceGripOffset { get; }

        // 左手扶槍點相對握把(公尺;槍的座標)
        Vector3 StanceSupportOffset { get; }

        // 每發後座力倍率(1 = 射擊槍一發)
        float StanceRecoilScale { get; }

        // 出膛前的預備進度 0 → 1(< 0 = 沒在預備)
        float StanceWindup { get; }
    }
}

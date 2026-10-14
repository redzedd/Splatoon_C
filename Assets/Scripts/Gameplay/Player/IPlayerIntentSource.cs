using UnityEngine;

namespace SplatoonC.Gameplay.Player
{
    // 玩家意圖抽象層——AutoTest 用 scripted intent 驅動真實路徑的接縫(見 unity-playmode-testing 鐵律 1)。
    // LookDeltaDeg 一律已換算成角度增量(度),與輸入裝置量綱無關。
    // M4 新增的副武器/大招意圖用預設實作回 false:二十個測試假類別不必跟著改,只有要驅動它們的測試才覆寫。
    public interface IPlayerIntentSource
    {
        Vector2 MoveInput { get; }
        Vector2 LookDeltaDeg { get; }
        bool JumpPressedThisFrame { get; }
        bool AttackHeld { get; }
        bool SquidHeld { get; }
        bool SubWeaponHeld => false;
        // 跳躍鍵是否仍按著(S3 按住跳得較高);預設 true = 永遠跳滿
        bool JumpHeld => true;
        bool SpecialPressedThisFrame => false;

        // A4 超級跳目標選單(玩家用;bot 直接呼叫 SuperJump.TryBegin,不經選單):開關、上下移一格、確定
        bool MapPressedThisFrame => false;
        int MenuStepThisFrame => 0;
        bool MenuConfirmPressedThisFrame => false;
    }
}

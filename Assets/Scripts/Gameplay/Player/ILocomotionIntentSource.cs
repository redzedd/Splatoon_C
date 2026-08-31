using UnityEngine;

namespace SplatoonC.Gameplay.Player
{
    // 移動意圖抽象層——AutoTest 用 scripted intent 驅動真實路徑的接縫(見 unity-playmode-testing 鐵律 1)。
    // LookDeltaDeg 一律已換算成角度增量(度),與輸入裝置量綱無關。
    public interface ILocomotionIntentSource
    {
        Vector2 MoveInput { get; }
        Vector2 LookDeltaDeg { get; }
        bool JumpPressedThisFrame { get; }
    }
}

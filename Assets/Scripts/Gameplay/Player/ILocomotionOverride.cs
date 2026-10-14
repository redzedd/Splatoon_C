using UnityEngine;

namespace SplatoonC.Gameplay.Player
{
    // 主武器接管人形移動的掛鉤(雙槍閃避翻滾;將來的衝刺類動作也可用):主武器元件實作此介面時,
    // PlayerLocomotion 每幀(人形、主武器啟用中)
    //   1. 讀到跳躍鍵按下 → 先問 InterceptJump,回傳 true = 這次按鍵被改成別的動作(翻滾),不起跳;
    //   2. 問 TryGetControlledMotion,回傳 true = 本幀水平速度由它指定(不吃搖桿、也不套重量/開火倍率),
    //      forcedVerticalSpeed 不是 NaN 時連垂直速度一起覆寫(空中翻滾急墜);重力、碰撞、擊退照常。
    // 瞬移/重生(PlayerLocomotion.ResetMotion)時呼叫 OnLocomotionReset,讓它放棄進行中的動作。
    // 沒實作此介面的主武器(射擊槍…)完全走原本的移動路徑,手感不受影響。
    public interface ILocomotionOverride
    {
        bool InterceptJump(Vector2 moveInput, float cameraYawDeg, bool grounded);

        bool TryGetControlledMotion(float time, float deltaTime, bool grounded, out Vector3 horizontalVelocity,
            out float forcedVerticalSpeed);

        void OnLocomotionReset();
    }
}

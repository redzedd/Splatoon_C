namespace SplatoonC.Gameplay.Player
{
    // 大招接管角色面向的掛鉤(Crab Tank 的車身慢慢轉、Ultra Stamp 衝鋒方向):啟動中的大招實作此介面時,
    // PlayerLocomotion 每幀先問它;回傳 true = 視覺根直接轉到 yawDeg(轉速由大招自己限制),不跟準心/移動方向。
    // 沒實作的大招照原本的「瞄準時面向準心、否則面向移動方向」。
    public interface IFacingOverride
    {
        bool TryGetFacingYaw(out float yawDeg);
    }
}

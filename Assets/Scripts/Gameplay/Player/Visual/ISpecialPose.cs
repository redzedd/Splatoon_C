using UnityEngine;

namespace SplatoonC.Gameplay.Player
{
    // 大招的持握姿勢來源(Ultra Stamp 雙手握大槌揮擊、Crab Tank 握操縱桿):
    // 大招把自己設到 CharacterVisual.SpecialPose,每幀 TryGetSpecialPose 回傳 true 時,人形的手臂與上身由它決定。
    // 只讀大招狀態、不改玩法。回傳 false = 本幀用一般姿勢。
    public interface ISpecialPose
    {
        bool TryGetSpecialPose(ref SpecialPoseFrame pose);
    }

    // 一幀的大招姿勢。位置與旋轉都在「胸口座標」:原點 = 兩肩中點、軸 = 胸骨(+Z 前、+Y 上、+X 右手),
    // 所以上身前傾/扭轉時手跟著動。插槽旋轉:+Z = 握把/槍管方向、+Y = 拇指側(與 WeaponSocket 相同慣例)。
    public struct SpecialPoseFrame
    {
        // 右手插槽(握把點)位置與旋轉
        public Vector3 RightGrip;
        public Quaternion RightSocket;

        // 左手插槽(鏡射的左手插槽慣例);LeftWeight 0 = 左手照一般姿勢自然下垂
        public Vector3 LeftGrip;
        public Quaternion LeftSocket;
        public float LeftWeight;

        // 手肘朝向(胸口座標;零向量 = 預設往外下)
        public Vector3 RightElbow;
        public Vector3 LeftElbow;

        // 上身:扭轉(度,+ = 往右)、前傾(度,+ = 往前壓);蹲低(公尺)
        public float TorsoYaw;
        public float TorsoPitch;
        public float Crouch;

        // 整個人往上抬(公尺;Crab Tank:駕駛站在艙口裡,胸口以上露出車殼——腳懸空藏在車殼內)
        public float Lift;

        // 整個人形藏起來(Crab Tank 滾球模式:人縮在球裡)
        public bool HideBody;

        // 目標變化時的平滑半衰期(秒;0 = 預設 0.05,揮擊瞬間用更短的值)
        public float HalfLife;
    }
}

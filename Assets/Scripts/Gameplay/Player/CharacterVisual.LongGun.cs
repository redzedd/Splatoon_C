using SplatoonC.Gameplay.Combat;
using UnityEngine;

namespace SplatoonC.Gameplay.Player
{
    // CharacterVisual 的長槍(狙擊槍,B2)持槍參數:主武器實作 ILongGunPose 時,瞄準姿勢的右手握點 / 左手扶槍點
    // 改用武器給的值(槍托靠肩、左手托護木),每發後座力依武器倍率放大。其餘(低持、空中、丟副武器、大招高舉)沿用持槍姿勢。
    public sealed partial class CharacterVisual
    {
        private ILongGunPose _longGun;

        // 長槍姿勢接手中(測試/除錯)。拿著大招(HeldSpecialActive:Trizooka 發射器…)時不接手 ——
        // 主武器模型只是 forceRenderingOff 藏起來、元件仍啟用,不擋的話大招會被套上長槍的握點
        public bool LongGunStanceActive => !HeldSpecialActive && _longGun != null && _longGun.LongGunActive;

        // UpdateActionState 呼叫:記下目前主武器是不是長槍
        private void TrackLongGun(MainWeapon main)
        {
            _longGun = main != null && main.isActiveAndEnabled ? main as ILongGunPose : null;
        }

        // 瞄準時右手握把點(相對右肩,槍座標)
        private Vector3 AimGripOffset => LongGunStanceActive ? _longGun.LongGunGripOffset : _gripOffset;

        // 瞄準時左手扶槍點(相對握把,槍座標)
        private Vector3 AimSupportOffset => LongGunStanceActive ? _longGun.LongGunSupportOffset : _supportOffset;

        // 放低持槍時槍口朝下的角度(度):射擊槍 26°(跑步 16°);長槍(1.0~1.3 m)壓太低槍口會插進地面 → 14°(跑步 8°)
        private float LowGunPitch(float move)
        {
            return LongGunStanceActive ? 14f - 6f * move : 26f - 10f * move;
        }

        // 放低持槍時握把抬高(公尺;長槍斜抱在腰前,不是垂在大腿旁)
        private float LowGripLift => LongGunStanceActive ? 0.06f : 0f;

        // 這一發的後座力倍率
        private float ShotRecoilScale => LongGunStanceActive ? Mathf.Max(0f, _longGun.LongGunRecoilScale) : 1f;
    }
}

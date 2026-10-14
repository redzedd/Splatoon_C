using SplatoonC.Core.Animation;
using SplatoonC.Gameplay.Combat;
using UnityEngine;

namespace SplatoonC.Gameplay.Player
{
    // CharacterVisual 的單槍持握提示(IGunStancePose;爆破槍 C2):主武器實作介面時,持槍瞄準的握把 / 扶槍點換成武器給的值、
    // 每發後座力乘上武器的倍率,出膛前的預備時間(爆破槍 14F)把槍往後收、槍口微抬(蓄勢),出膛瞬間由後座力彈回。
    // 沒實作的主武器 → 原本的射擊槍姿勢數值(_gripOffset / _supportOffset)。
    public sealed partial class CharacterVisual
    {
        // 蓄勢時槍往後收的距離(公尺)與槍口上抬角(度)
        private const float WindupPullBack = 0.05f;
        private const float WindupPitch = 6f;

        private IGunStancePose _gunStance;
        private MainWeapon _gunStanceMain;
        private float _stanceWindup;

        // 單槍持握提示接手中(測試/除錯)
        public bool GunStanceActive => StanceLive;

        // 目前(平滑後)的蓄勢權重 0 → 1
        public float GunStanceWindup => _stanceWindup;

        // 拿著大招(HeldSpecialActive)時不接手,跟滾筒 / 雙持姿勢一樣讓大招的持握姿勢出現
        private bool StanceLive => !HeldSpecialActive && _gunStance != null && _gunStanceMain != null
                                   && _gunStanceMain.isActiveAndEnabled && _gunStance.GunStanceActive;

        // UpdateActionState 呼叫(換裝時):記下主武器是否提供單槍持握提示
        private void TrackGunStance(MainWeapon main)
        {
            _gunStance = main as IGunStancePose;
            _gunStanceMain = main;
            _stanceWindup = 0f;
        }

        // 每發後座力倍率
        private float GunStanceRecoilScale()
        {
            return StanceLive ? Mathf.Max(0f, _gunStance.StanceRecoilScale) : 1f;
        }

        // 蓄勢權重:預備中跟著預備進度(ease in-out)、出膛後快速歸零
        private void UpdateGunStance(float dt)
        {
            float windup = StanceLive ? _gunStance.StanceWindup : -1f;
            _stanceWindup = windup >= 0f
                ? Mathf.SmoothStep(0f, 1f, windup)
                : SpringMotion.Damp(_stanceWindup, 0f, 0.03f, dt);
        }

        // PoseArms 呼叫:本幀的握把 / 扶槍點(沒有提示 = 長槍或射擊槍數值)
        private void GunStanceOffsets(out Vector3 grip, out Vector3 support)
        {
            if (StanceLive)
            {
                grip = _gunStance.StanceGripOffset;
                support = _gunStance.StanceSupportOffset;
                return;
            }
            // 沒有單槍提示 → 長槍(ILongGunPose,狙擊槍)的握點,再沒有就是射擊槍數值
            grip = AimGripOffset;
            support = AimSupportOffset;
        }

        // PoseArms 呼叫:蓄勢把瞄準姿勢的槍往後收、槍口微抬
        private void ApplyGunStanceWindup(ref Quaternion aimGun, ref Vector3 aimGrip)
        {
            if (_stanceWindup <= 1e-3f)
            {
                return;
            }
            aimGrip -= (aimGun * Vector3.forward) * (WindupPullBack * _stanceWindup);
            aimGun *= Quaternion.AngleAxis(-WindupPitch * _stanceWindup, Vector3.right);
        }
    }
}

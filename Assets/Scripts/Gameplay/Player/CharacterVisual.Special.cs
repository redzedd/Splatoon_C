using SplatoonC.Core.Animation;
using UnityEngine;

namespace SplatoonC.Gameplay.Player
{
    // CharacterVisual 的大招持握(D2;與持槍/滾筒/雙持分開寫):
    //   HeldSpecialActive:大招把東西拿在手上(SpecialHeldModel 顯示中:Trizooka、Wave Breaker、Tacticooler、Ink Vac、
    //     Ultra Stamp、Crab Tank)——滾筒與雙持姿勢不接手(Round 1 合併時發現滾筒/雙槍的手臂壓過大招),左手插槽的槍模也藏起來。
    //   SpecialPose(ISpecialPose):大招自訂的手臂與上身(Ultra Stamp 雙手揮槌、Crab Tank 握操縱桿;Ink Vac 走一般瞄準姿勢 AimOverride);
    //     目標以短半衰期平滑,揮擊瞬間由大招給更短的半衰期;Lift = 整個人抬高(Crab Tank 駕駛站在艙口裡);
    //     HideBody = 整個人形藏起來(Crab Tank 滾球模式)。
    // 只讀大招狀態,不改玩法;零配置。
    public sealed partial class CharacterVisual
    {
        // 大招把東西拿在手上(SpecialHeldModel 設定):壓過滾筒/雙持姿勢
        public bool HeldSpecialActive { get; set; }

        // 大招自訂姿勢來源(null = 一般持握)
        public ISpecialPose SpecialPose { get; set; }

        // 本幀是否用大招姿勢 / 人形是否被大招藏起來(測試讀)
        public bool SpecialPosing => _specialPosing;
        public bool BodyHiddenBySpecial => _bodyHiddenBySpecial;

        // 目前(平滑後)的右手握點(胸口座標)
        public Vector3 SpecialRightGrip => _specialRight;

        // 目前(平滑後)大招把人抬高的量(公尺;Crab Tank 駕駛)
        public float SpecialLift => _specialPosing ? _specialLift : 0f;

        private SpecialPoseFrame _specialFrame;
        private bool _specialPosing;
        private bool _specialPoseValid;
        private bool _bodyHiddenBySpecial;
        private Vector3 _specialRight;
        private Vector3 _specialLeft;
        private Quaternion _specialRightRot = Quaternion.identity;
        private Quaternion _specialLeftRot = Quaternion.identity;
        private float _specialLeftWeight;
        private float _specialTorsoYaw;
        private float _specialTorsoPitch;
        private float _specialCrouch;
        private float _specialLift;

        // 骨盆下壓量(負 = 抬高):蹲低 − 抬高
        private float SpecialCrouch => _specialPosing ? _specialCrouch - _specialLift : 0f;

        // LateUpdate(ApplyForm 之後):問大招本幀姿勢、平滑目標、依 HideBody 藏/顯示人形
        private void SampleSpecialPose(float dt)
        {
            bool posing = false;
            if (!_squidForm && SpecialPose != null)
            {
                SpecialPoseFrame frame = default;
                frame.RightSocket = Quaternion.identity;
                frame.LeftSocket = Quaternion.identity;
                if (SpecialPose.TryGetSpecialPose(ref frame))
                {
                    posing = true;
                    _specialFrame = frame;
                }
            }
            if (posing)
            {
                SpecialPoseFrame f = _specialFrame;
                if (!_specialPoseValid)
                {
                    // 第一幀直接就位(發動的高舉動作蓋過這一跳)
                    _specialPoseValid = true;
                    _specialRight = f.RightGrip;
                    _specialLeft = f.LeftGrip;
                    _specialRightRot = f.RightSocket;
                    _specialLeftRot = f.LeftSocket;
                    _specialLeftWeight = f.LeftWeight;
                    _specialTorsoYaw = f.TorsoYaw;
                    _specialTorsoPitch = f.TorsoPitch;
                    _specialCrouch = f.Crouch;
                    _specialLift = f.Lift;
                }
                else
                {
                    float halfLife = f.HalfLife > 0f ? f.HalfLife : 0.05f;
                    float t = 1f - Mathf.Pow(2f, -dt / Mathf.Max(0.001f, halfLife));
                    _specialRight = SpringMotion.Damp(_specialRight, f.RightGrip, halfLife, dt);
                    _specialLeft = SpringMotion.Damp(_specialLeft, f.LeftGrip, halfLife, dt);
                    _specialRightRot = Quaternion.Slerp(_specialRightRot, f.RightSocket, t);
                    _specialLeftRot = Quaternion.Slerp(_specialLeftRot, f.LeftSocket, t);
                    _specialLeftWeight = SpringMotion.Damp(_specialLeftWeight, f.LeftWeight, halfLife, dt);
                    _specialTorsoYaw = SpringMotion.Damp(_specialTorsoYaw, f.TorsoYaw, halfLife, dt);
                    _specialTorsoPitch = SpringMotion.Damp(_specialTorsoPitch, f.TorsoPitch, halfLife, dt);
                    _specialCrouch = SpringMotion.Damp(_specialCrouch, f.Crouch, halfLife, dt);
                    _specialLift = SpringMotion.Damp(_specialLift, f.Lift, halfLife, dt);
                }
            }
            else
            {
                _specialPoseValid = false;
            }
            _specialPosing = posing;

            // 滾球模式:人形整個藏起來(形態切換只在 ApplyForm 動 SetActive;這裡只在人形態時接手,換回時還原)
            bool hide = posing && _specialFrame.HideBody && !_squidForm;
            if (hide != _bodyHiddenBySpecial)
            {
                _bodyHiddenBySpecial = hide;
                if (!_squidForm && _inklingModel.gameObject.activeSelf == hide)
                {
                    _inklingModel.gameObject.SetActive(!hide);
                }
            }
        }

        // AnimateHuman:大招姿勢接手上身扭轉/前傾
        private void ApplySpecialTorso(ref float torsoYaw, ref float torsoPitch)
        {
            if (!_specialPosing)
            {
                return;
            }
            torsoYaw = _specialTorsoYaw;
            torsoPitch = _specialTorsoPitch - _recoil * 1.5f;
        }

        // PoseArms:大招姿勢的雙手(回傳 true = 已處理)
        private bool PoseSpecialArms()
        {
            if (!_specialPosing)
            {
                return false;
            }
            HumanoidRig rig = _rig;
            Vector3 shoulderL = rig.ModelPosition(HumanoidRig.Bone.UpperArmL);
            Vector3 shoulderR = rig.ModelPosition(HumanoidRig.Bone.UpperArmR);
            Vector3 mid = (shoulderL + shoulderR) * 0.5f;
            Quaternion chest = rig.ModelRotation(HumanoidRig.Bone.Chest);
            SpecialPoseFrame f = _specialFrame;

            // 右手:插槽位姿 → 手腕目標(與持槍同一套換算)
            Vector3 gripR = mid + chest * _specialRight;
            Quaternion socketR = chest * _specialRightRot;
            Quaternion handR = socketR * Quaternion.Inverse(rig.SocketLocalRot);
            Vector3 wristR = gripR - handR * rig.SocketLocalPos;
            Vector3 poleR = f.RightElbow.sqrMagnitude > 1e-6f ? chest * f.RightElbow : chest * new Vector3(0.6f, -0.8f, -0.3f);
            rig.SolveLimb(HumanoidRig.Bone.UpperArmR, HumanoidRig.Bone.LowerArmR, HumanoidRig.Bone.HandR,
                wristR, poleR, handR, false);

            // 左手:握同一支握把(鏡射插槽慣例),權重低時往自然下垂的位置混
            Vector3 hang = shoulderL + chest * new Vector3(-0.08f, -0.36f, 0.05f);
            float w = Mathf.Clamp01(_specialLeftWeight);
            Vector3 gripL = mid + chest * _specialLeft;
            Quaternion socketL = chest * _specialLeftRot;
            Quaternion handL = socketL * Quaternion.Inverse(rig.SocketLocalRotLeft);
            Vector3 wristL = gripL - handL * rig.SocketLocalPosLeft;
            Vector3 poleL = f.LeftElbow.sqrMagnitude > 1e-6f ? chest * f.LeftElbow : chest * new Vector3(-0.6f, -0.8f, -0.3f);
            if (w < 0.999f)
            {
                wristL = Vector3.Lerp(hang, wristL, w);
                poleL = Vector3.Lerp(new Vector3(-0.35f, -0.4f, -1f), poleL, w);
            }
            rig.SolveLimb(HumanoidRig.Bone.UpperArmL, HumanoidRig.Bone.LowerArmL, HumanoidRig.Bone.HandL,
                wristL, poleL, handL, false);
            if (w < 0.999f)
            {
                Quaternion straight = rig.StraightEndRotation(HumanoidRig.Bone.LowerArmL, HumanoidRig.Bone.HandL);
                rig.SetModelRotation(HumanoidRig.Bone.HandL, Quaternion.Slerp(straight, handL, w));
            }
            return true;
        }
    }
}

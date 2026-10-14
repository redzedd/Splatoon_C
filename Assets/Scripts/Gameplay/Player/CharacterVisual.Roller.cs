using SplatoonC.Core.Animation;
using SplatoonC.Gameplay.Combat;
using UnityEngine;

namespace SplatoonC.Gameplay.Player
{
    // CharacterVisual 的滾筒姿勢(B1;與持槍姿勢分開寫,只在裝備 RollerWeapon 且滾筒模型掛上時接手手臂與上身)。
    // 全部在模型空間(+Z 前、+Y 上、+X 右手;原點在腳底):
    //   拿著:右手單手提在身側前方,滾筒輕觸地面(跑步時離地、空中再抬高),左手照常擺動
    //   橫甩(SwingFrame):前 72% 雙手把滾筒高舉過頭(滾筒在頭後上方、上身後仰),最後 28% 往前揮到頭前 = 墨甩出的瞬間;
    //   甩出後 0.3 內砸到身前下方(上身前壓),之後收到推滾(還按著)或拿著姿勢
    //   直甩:滾筒往右後拉到肩高、滾筒軸轉成直立、上身往右扭 → 揮到右前甩出 → 橫掃到左前
    //   推滾:滾筒軸心壓在腳前 RollForward 的地面上(= 接觸判定中心),握把高度約肩下 0.3,上身前傾推
    // 目標姿勢每幀算,握點/握把方向以短半衰期平滑(甩出那一下用更短的半衰期),切換不會跳。
    public sealed partial class CharacterVisual
    {
        private RollerWeapon _roller;
        private bool _rollerPoseValid;
        private Vector3 _rollerGrip;
        private Vector3 _rollerDir = Vector3.forward;
        private float _rollerSide;
        private float _rollerTwoHands;
        private float _rollerTorsoYaw;
        private float _rollerTorsoPitch;
        private float _rollerLean;

        // 滾筒姿勢接手中(測試/除錯)
        // 拿著大招(HeldSpecialActive:Trizooka 發射器、Wave Breaker 裝置…)時不接手,讓大招的持握姿勢出現
        public bool RollerStanceActive => !HeldSpecialActive && _roller != null && _roller.isActiveAndEnabled
                                          && _roller.Model != null;

        // 目前(平滑後)的右手握點與握把方向(模型空間)
        public Vector3 RollerGripModel => _rollerGrip;
        public Vector3 RollerHandleModel => _rollerDir;
        public float RollerTwoHandWeight => _rollerTwoHands;

        // UpdateActionState 呼叫:記下目前主武器是不是滾筒(換裝時姿勢從頭來)
        private void TrackRoller(MainWeapon main)
        {
            var roller = main as RollerWeapon;
            if (roller != _roller)
            {
                _roller = roller;
                _rollerPoseValid = false;
            }
        }

        // AnimateHuman 呼叫(算完持槍的上身角度後):滾筒接手上身扭轉/俯仰/前傾
        private void ApplyRollerTorso(float dt, float yawToAim, float throwW, float throwTwist, ref float torsoYaw,
            ref float torsoPitch, ref float leanPitch)
        {
            if (!RollerStanceActive)
            {
                return;
            }
            RollerTorsoTarget(out float yaw, out float pitch, out float lean);
            bool fast = (_roller.Action == RollerAction.FollowThrough && _roller.FlingProgress < 0.35f)
                        || (_roller.Action == RollerAction.Windup && _roller.WindupProgress > WindupLiftShare);
            float halfLife = fast ? 0.02f : 0.06f;
            _rollerTorsoYaw = SpringMotion.Damp(_rollerTorsoYaw, yaw, halfLife, dt);
            _rollerTorsoPitch = SpringMotion.Damp(_rollerTorsoPitch, pitch, halfLife, dt);
            _rollerLean = SpringMotion.Damp(_rollerLean, lean, 0.08f, dt);
            float attack = _roller.IsAttacking ? 1f : 0f;
            torsoYaw = _rollerTorsoYaw + 0.3f * yawToAim * attack * (1f - throwW) + throwTwist * throwW
                       + 0.35f * yawToAim * throwW;
            torsoPitch = _rollerTorsoPitch - _recoil * 1.5f + ThrowLean() * throwW;
            leanPitch += _rollerLean;
        }

        private void RollerTorsoTarget(out float yaw, out float pitch, out float lean)
        {
            yaw = 0f;
            pitch = 0f;
            lean = 0f;
            RollerWeapon w = _roller;
            bool vertical = w.CurrentSwingVertical;
            switch (w.Action)
            {
                case RollerAction.Windup:
                {
                    float progress = w.WindupProgress;
                    float p = Smooth(progress / WindupLiftShare);
                    float swing = progress > WindupLiftShare ? (progress - WindupLiftShare) / (1f - WindupLiftShare) : 0f;
                    if (vertical)
                    {
                        yaw = Mathf.Lerp(38f * p, 12f, swing);
                    }
                    else
                    {
                        pitch = Mathf.Lerp(-14f * p, 4f, swing);
                    }
                    break;
                }
                case RollerAction.FollowThrough:
                case RollerAction.Rolling:
                {
                    float f = w.FlingProgress;
                    float restLean = w.Action == RollerAction.Rolling || w.RollPending ? 8f + 5f * w.RollSpeedNormalized : 0f;
                    if (f < 0.3f)
                    {
                        float t = f / 0.3f;
                        if (vertical)
                        {
                            yaw = Mathf.Lerp(12f, -32f, t);
                        }
                        else
                        {
                            pitch = Mathf.Lerp(4f, 20f, t);
                        }
                    }
                    else
                    {
                        float t = Smooth((f - 0.3f) / 0.7f);
                        if (vertical)
                        {
                            yaw = Mathf.Lerp(-32f, 0f, t);
                        }
                        else
                        {
                            pitch = Mathf.Lerp(20f, 0f, t);
                        }
                        lean = restLean * t;
                    }
                    break;
                }
            }
        }

        // PoseArms 呼叫:回傳 true = 滾筒接手了兩隻手臂
        private bool PoseRollerArms(float phase, float move, float time)
        {
            if (!RollerStanceActive || !_roller.Model.gameObject.activeInHierarchy)
            {
                return false;
            }
            HumanoidRig rig = _rig;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            Vector3 shoulderL = rig.ModelPosition(HumanoidRig.Bone.UpperArmL);
            Vector3 shoulderR = rig.ModelPosition(HumanoidRig.Bone.UpperArmR);
            Quaternion chest = rig.ModelRotation(HumanoidRig.Bone.Chest);

            RollerTarget(shoulderR, move, out Vector3 grip, out Vector3 dir, out float side, out float twoHands,
                out float fastBlend);
            float flourishW = FlourishWeight;
            if (flourishW > 0.001f)
            {
                // 大招發動:把滾筒高舉過頭
                grip = Vector3.Lerp(grip, shoulderR + new Vector3(-0.06f, 0.36f, 0.06f), flourishW);
                dir = Vector3.Slerp(dir, new Vector3(0f, 0.92f, -0.39f), flourishW).normalized;
                side *= 1f - flourishW;
                twoHands = Mathf.Max(twoHands, flourishW);
            }
            if (!_rollerPoseValid)
            {
                _rollerGrip = grip;
                _rollerDir = dir;
                _rollerSide = side;
                _rollerTwoHands = twoHands;
                _rollerPoseValid = true;
            }
            else
            {
                float halfLife = Mathf.Lerp(0.05f, 0.016f, fastBlend);
                _rollerGrip = SpringMotion.Damp(_rollerGrip, grip, halfLife, dt);
                float k = 1f - Mathf.Pow(0.5f, dt / halfLife);
                _rollerDir = Vector3.Slerp(_rollerDir, dir, k).normalized;
                _rollerSide = SpringMotion.Damp(_rollerSide, side, halfLife, dt);
                _rollerTwoHands = SpringMotion.Damp(_rollerTwoHands, twoHands, 0.04f, dt);
            }

            // 握把座標系:+Z 沿握把往滾筒;橫甩/推滾時滾筒軸 = 模型右方,直甩時滾筒軸轉成直立
            Vector3 upSagittal = Vector3.Cross(_rollerDir, Vector3.right);
            Vector3 upSide = Vector3.Cross(_rollerDir, Vector3.up);
            Vector3 up = Vector3.Lerp(upSagittal, upSide, _rollerSide);
            if (up.sqrMagnitude < 1e-4f)
            {
                up = Vector3.up;
            }
            Quaternion handle = Quaternion.LookRotation(_rollerDir, up.normalized);

            // ── 右手:握把末端 ──
            float overhead = Mathf.Clamp01(_rollerDir.y * 1.4f);
            Vector3 poleR = Vector3.Lerp(new Vector3(0.6f, -0.55f, -0.6f), new Vector3(0.85f, 0.25f, -0.35f), overhead);
            poleR = Vector3.Lerp(poleR, new Vector3(0.35f, -0.8f, -0.4f), _rollerSide);
            Quaternion handR = handle * Quaternion.Inverse(rig.SocketLocalRot);
            Vector3 wristR = _rollerGrip - handR * rig.SocketLocalPos;
            rig.SolveLimb(HumanoidRig.Bone.UpperArmR, HumanoidRig.Bone.LowerArmR, HumanoidRig.Bone.HandR,
                wristR, poleR, handR, false);

            // ── 左手:空手擺動 → 握把下段(攻擊中)→ 丟副武器 ──
            float swingL = GaitCycle.ArmSwing(phase) * move;
            float air = _airWeight;
            float idle = Mathf.Sin(time * 1.1f) * 0.006f * (1f - move);
            Vector3 lowL = shoulderL + chest * new Vector3(-0.08f + 0.02f * move,
                -0.365f + 0.13f * move + 0.06f * Mathf.Max(0f, swingL) + idle,
                0.03f + 0.02f * move + 0.18f * swingL);
            Vector3 airL = shoulderL + chest * new Vector3(-0.27f, -0.02f, 0.06f);
            Vector3 handPosL = Vector3.Lerp(lowL, airL, air * (1f - _rollerTwoHands));
            Quaternion handRotL = Quaternion.identity;
            float straightL = 1f;
            Vector3 poleL = new Vector3(-0.35f, -0.4f, -1f);
            float support = _rollerTwoHands;
            if (support > 0.001f)
            {
                Vector3 target = _rollerGrip + _rollerDir * _roller.Model.SupportDistance;
                Vector3 fingers = handle * new Vector3(0.85f, 0.2f, 0.45f).normalized;
                Vector3 thumb = handle * new Vector3(0f, 0.7f, 0.7f);
                handPosL = Vector3.Lerp(handPosL, target, support);
                handRotL = LookAlong(fingers, thumb);
                straightL = 1f - support;
                poleL = Vector3.Lerp(poleL, new Vector3(-0.7f, -0.45f, -0.35f), support);
            }
            float throwW = ThrowWeight;
            if (throwW > 0.001f)
            {
                Vector3 throwTarget = shoulderL + chest * ThrowHandOffset();
                handPosL = Vector3.Lerp(handPosL, throwTarget, throwW);
                Quaternion throwRot = LookAlong(chest * new Vector3(-0.1f, 0.9f, 0.35f), chest * new Vector3(0.3f, 0f, 1f));
                handRotL = straightL >= 1f ? throwRot : Quaternion.Slerp(handRotL, throwRot, throwW);
                straightL *= 1f - throwW;
                poleL = Vector3.Lerp(poleL, new Vector3(-1f, 0.05f, -0.3f), throwW);
            }
            if (straightL >= 0.999f)
            {
                rig.SolveLimb(HumanoidRig.Bone.UpperArmL, HumanoidRig.Bone.LowerArmL, HumanoidRig.Bone.HandL,
                    handPosL, poleL, Quaternion.identity, false);
                rig.SetModelRotation(HumanoidRig.Bone.HandL,
                    rig.StraightEndRotation(HumanoidRig.Bone.LowerArmL, HumanoidRig.Bone.HandL));
            }
            else
            {
                rig.SolveLimb(HumanoidRig.Bone.UpperArmL, HumanoidRig.Bone.LowerArmL, HumanoidRig.Bone.HandL,
                    handPosL, poleL, handRotL, false);
                if (straightL > 0.001f)
                {
                    Quaternion straight = rig.StraightEndRotation(HumanoidRig.Bone.LowerArmL, HumanoidRig.Bone.HandL);
                    rig.SetModelRotation(HumanoidRig.Bone.HandL, Quaternion.Slerp(handRotL, straight, straightL));
                }
            }
            return true;
        }

        // 目前動作的目標握點/握把方向(模型空間)。side = 直甩座標系權重;fastBlend = 甩出那一下(平滑用短半衰期)
        private void RollerTarget(Vector3 shoulderR, float move, out Vector3 grip, out Vector3 dir, out float side,
            out float twoHands, out float fastBlend)
        {
            RollerWeapon w = _roller;
            RollerModel model = w.Model;
            float length = model.HandleLength;
            float radius = model.DrumRadius;
            float rollForward = w.Config != null ? w.Config.RollForward : 1.05f;

            // 拿著:右手在身側前方低處,滾筒朝右前方、軸心離地 = 半徑 + 一點(跑步離地多一點,空中再抬高)
            Vector3 carryGrip = shoulderR + new Vector3(0.05f, -0.4f + 0.05f * move, 0.12f + 0.06f * move);
            float carryDrumY = radius + 0.03f + 0.16f * move + 0.35f * _airWeight;
            Vector3 carryDir = HandleToward(carryGrip, new Vector2(0.34f, 0.6f + 0.22f * move), carryDrumY, length);

            // 推滾:滾筒軸心在腳前 RollForward 的地面上;握把高度 ≈ 肩下 0.3
            float gripHeight = shoulderR.y - 0.3f;
            float sin = Mathf.Clamp((gripHeight - radius) / Mathf.Max(0.1f, length), 0.2f, 0.95f);
            Vector3 rollDir = new Vector3(0f, -sin, Mathf.Sqrt(1f - sin * sin));
            Vector3 rollGrip = new Vector3(0f, radius, rollForward) - rollDir * length;

            // 橫甩:舉起過頭(滾筒在頭後上方)→ 往前揮到頭前(墨在這時甩出)→ 砸到身前下方
            Vector3 overGrip = shoulderR + new Vector3(-0.14f, 0.4f, 0.02f);
            Vector3 overDir = new Vector3(0f, 0.8f, -0.6f);
            Vector3 releaseGrip = shoulderR + new Vector3(-0.12f, 0.12f, 0.3f);
            Vector3 releaseDir = new Vector3(0f, 0.3f, 0.95f).normalized;
            Vector3 slamGrip = shoulderR + new Vector3(-0.1f, -0.22f, 0.42f);
            Vector3 slamDir = new Vector3(0f, -0.45f, 0.89f).normalized;

            // 直甩:往右後拉到肩高 → 揮到右前(甩出)→ 橫掃到左前
            Vector3 sideGrip = shoulderR + new Vector3(0.16f, -0.12f, -0.02f);
            Vector3 sideDir = new Vector3(0.88f, 0.05f, -0.47f).normalized;
            Vector3 sideReleaseGrip = shoulderR + new Vector3(0.05f, -0.12f, 0.22f);
            Vector3 sideReleaseDir = new Vector3(0.6f, 0.03f, 0.8f).normalized;
            Vector3 sweepGrip = shoulderR + new Vector3(-0.3f, -0.12f, 0.3f);
            Vector3 sweepDir = new Vector3(-0.8f, 0.02f, 0.6f).normalized;

            bool vertical = w.CurrentSwingVertical;
            bool toRoll = w.Action == RollerAction.Rolling || w.RollPending;
            Vector3 restGrip = toRoll ? rollGrip : carryGrip;
            Vector3 restDir = toRoll ? rollDir : carryDir;
            fastBlend = 0f;
            side = 0f;
            twoHands = 1f;
            switch (w.Action)
            {
                case RollerAction.Windup:
                {
                    // 前 72% 舉起蓄勢,最後 28% 往前揮;SwingFrame 結束(甩出墨)時滾筒剛好揮到身前
                    float progress = w.WindupProgress;
                    Vector3 holdGrip = vertical ? sideGrip : overGrip;
                    Vector3 holdDir = vertical ? sideDir : overDir.normalized;
                    if (progress < WindupLiftShare)
                    {
                        float p = Smooth(progress / WindupLiftShare);
                        grip = Vector3.Lerp(carryGrip, holdGrip, p);
                        dir = Vector3.Slerp(carryDir, holdDir, p).normalized;
                        side = vertical ? p : 0f;
                    }
                    else
                    {
                        float t = (progress - WindupLiftShare) / (1f - WindupLiftShare);
                        t *= t;
                        grip = Vector3.Lerp(holdGrip, vertical ? sideReleaseGrip : releaseGrip, t);
                        dir = Vector3.Slerp(holdDir, vertical ? sideReleaseDir : releaseDir, t).normalized;
                        side = vertical ? 1f : 0f;
                        fastBlend = 1f;
                    }
                    break;
                }
                case RollerAction.FollowThrough:
                case RollerAction.Rolling:
                {
                    float f = w.FlingProgress;
                    Vector3 fromGrip = vertical ? sideReleaseGrip : releaseGrip;
                    Vector3 fromDir = vertical ? sideReleaseDir : releaseDir;
                    Vector3 strikeGrip = vertical ? sweepGrip : slamGrip;
                    Vector3 strikeDir = vertical ? sweepDir : slamDir;
                    if (f < 0.3f)
                    {
                        float t = f / 0.3f;
                        grip = Vector3.Lerp(fromGrip, strikeGrip, t);
                        dir = Vector3.Slerp(fromDir, strikeDir, t).normalized;
                        side = vertical ? 1f : 0f;
                        fastBlend = 1f;
                    }
                    else
                    {
                        float t = Smooth((f - 0.3f) / 0.7f);
                        grip = Vector3.Lerp(strikeGrip, restGrip, t);
                        dir = Vector3.Slerp(strikeDir, restDir, t).normalized;
                        side = vertical ? 1f - t : 0f;
                        fastBlend = 1f - t;
                        // 沒有要推滾:收招結束放回單手拿著
                        twoHands = toRoll ? 1f : 1f - t;
                    }
                    break;
                }
                default:
                    grip = carryGrip;
                    dir = carryDir;
                    twoHands = 0f;
                    break;
            }
        }

        // 舉起蓄勢佔 SwingFrame 的比例(其餘是往前揮到甩出點)
        private const float WindupLiftShare = 0.72f;

        // 握點 grip 朝水平方向 towardsXZ(模型 xz)、滾筒軸心落在高度 drumY 的握把方向(握把長 length 固定)
        private static Vector3 HandleToward(Vector3 grip, Vector2 towardsXZ, float drumY, float length)
        {
            Vector2 horizontal = new Vector2(towardsXZ.x - grip.x, towardsXZ.y - grip.z);
            horizontal = horizontal.sqrMagnitude > 1e-6f ? horizontal.normalized : Vector2.up;
            float sin = Mathf.Clamp((grip.y - drumY) / Mathf.Max(0.1f, length), -0.3f, 0.97f);
            float cos = Mathf.Sqrt(1f - sin * sin);
            return new Vector3(horizontal.x * cos, -sin, horizontal.y * cos);
        }

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }
    }
}

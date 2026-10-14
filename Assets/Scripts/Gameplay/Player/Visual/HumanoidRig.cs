using SplatoonC.Core.Animation;
using UnityEngine;

namespace SplatoonC.Gameplay.Player
{
    // Inkling.fbx 骨架的程序動畫容器:骨頭表、靜止姿勢、每幀的局部旋轉緩衝、模型空間正向運動學與兩骨 IK。
    // 全部運算在「模型空間」(InklingModel 的局部座標:+Y 上、+Z 角色正面、+X 角色右手)——
    // Visual 被 SquidController 非等比壓扁時也不受影響;每幀最後一次寫回 Transform.localRotation。
    // 只轉骨頭的 localRotation,絕不動 Rig 物件本身(README:Rig 帶 -90° 由 Root 骨抵銷)。
    public sealed class HumanoidRig
    {
        public enum Bone
        {
            Root, Hips, Spine, Chest, Neck, Head,
            UpperArmL, LowerArmL, HandL, UpperArmR, LowerArmR, HandR,
            UpperLegL, LowerLegL, FootL, UpperLegR, LowerLegR, FootR,
            Tentacle1A, Tentacle1B, Tentacle2A, Tentacle2B, Tentacle3A, Tentacle3B,
            Tentacle4A, Tentacle4B, Tentacle5A, Tentacle5B, Tentacle6A, Tentacle6B,
            Count,
        }

        // 與 Bone 列舉同序;父骨一定排在子骨前面(正向運動學依序計算)
        private static readonly string[] BoneNames =
        {
            "Root", "Hips", "Spine", "Chest", "Neck", "Head",
            "UpperArm.L", "LowerArm.L", "Hand.L", "UpperArm.R", "LowerArm.R", "Hand.R",
            "UpperLeg.L", "LowerLeg.L", "Foot.L", "UpperLeg.R", "LowerLeg.R", "Foot.R",
            "Tentacle_1_A", "Tentacle_1_B", "Tentacle_2_A", "Tentacle_2_B", "Tentacle_3_A", "Tentacle_3_B",
            "Tentacle_4_A", "Tentacle_4_B", "Tentacle_5_A", "Tentacle_5_B", "Tentacle_6_A", "Tentacle_6_B",
        };

        private const int Count = (int)Bone.Count;

        private readonly Transform[] _bones = new Transform[Count];
        private readonly int[] _parent = new int[Count];
        private readonly Vector3[] _restLocalPos = new Vector3[Count];
        private readonly Quaternion[] _restLocalRot = new Quaternion[Count];
        private readonly Quaternion[] _restModelRot = new Quaternion[Count];
        private readonly Vector3[] _restModelPos = new Vector3[Count];
        private readonly Quaternion[] _local = new Quaternion[Count];
        private readonly Quaternion[] _model = new Quaternion[Count];
        private readonly Vector3[] _modelPos = new Vector3[Count];
        private Quaternion _rigRot;
        private Vector3 _rigPos;
        private Vector3 _hipsOffset;

        public bool IsValid { get; private set; }
        public Transform ModelRoot { get; private set; }
        public Transform WeaponSocket { get; private set; }
        public Transform InkTankFill { get; private set; }

        // WeaponSocket 相對 Hand.R 的靜止位姿(握把點;socket +Z = 槍管方向、+Y = 拇指側)
        public Vector3 SocketLocalPos { get; private set; }
        public Quaternion SocketLocalRot { get; private set; }

        // 左手武器插槽(雙槍的左手那把):模型沒有內建,位姿 = 右手插槽對模型 YZ 平面的鏡射(相對 Hand.L);
        // 插槽物件第一次要掛東西時才建立(EnsureWeaponSocketLeft),沒有雙持武器的角色不多一個節點
        public Transform WeaponSocketLeft { get; private set; }
        public Vector3 SocketLocalPosLeft { get; private set; }
        public Quaternion SocketLocalRotLeft { get; private set; }

        public const string LeftSocketName = "WeaponSocketLeft";

        public float UpperArmLength { get; private set; }
        public float LowerArmLength { get; private set; }
        public float UpperLegLength { get; private set; }
        public float LowerLegLength { get; private set; }

        public Transform BoneTransform(Bone bone)
        {
            return _bones[(int)bone];
        }

        public Vector3 RestModelPosition(Bone bone)
        {
            return _restModelPos[(int)bone];
        }

        public Quaternion RestModelRotation(Bone bone)
        {
            return _restModelRot[(int)bone];
        }

        public Vector3 ModelPosition(Bone bone)
        {
            return _modelPos[(int)bone];
        }

        public Quaternion ModelRotation(Bone bone)
        {
            return _model[(int)bone];
        }

        // 綁定:依名稱找骨頭並記下靜止姿勢。找不到任何一根就整個停用(寧可 A-pose 也不要扭曲)。
        public bool Bind(Transform modelRoot)
        {
            IsValid = false;
            ModelRoot = modelRoot;
            if (modelRoot == null)
            {
                return false;
            }
            Transform rig = modelRoot.Find("Rig");
            if (rig == null)
            {
                Debug.LogError("HumanoidRig:模型底下找不到 Rig", modelRoot);
                return false;
            }
            for (int i = 0; i < Count; i++)
            {
                _bones[i] = FindDeep(rig, BoneNames[i]);
                if (_bones[i] == null)
                {
                    Debug.LogError($"HumanoidRig:找不到骨頭 {BoneNames[i]}", modelRoot);
                    return false;
                }
            }
            for (int i = 0; i < Count; i++)
            {
                _parent[i] = -1;
                Transform p = _bones[i].parent;
                for (int j = 0; j < i; j++)
                {
                    if (_bones[j] == p)
                    {
                        _parent[i] = j;
                        break;
                    }
                }
                _restLocalPos[i] = _bones[i].localPosition;
                _restLocalRot[i] = _bones[i].localRotation;
            }
            _rigRot = rig.localRotation;
            _rigPos = rig.localPosition;
            BeginPose();
            SolveFk();
            for (int i = 0; i < Count; i++)
            {
                _restModelRot[i] = _model[i];
                _restModelPos[i] = _modelPos[i];
            }

            UpperArmLength = _restLocalPos[(int)Bone.LowerArmR].magnitude;
            LowerArmLength = _restLocalPos[(int)Bone.HandR].magnitude;
            UpperLegLength = _restLocalPos[(int)Bone.LowerLegR].magnitude;
            LowerLegLength = _restLocalPos[(int)Bone.FootR].magnitude;

            WeaponSocket = FindDeep(_bones[(int)Bone.HandR], "WeaponSocket");
            if (WeaponSocket != null)
            {
                SocketLocalPos = WeaponSocket.localPosition;
                SocketLocalRot = WeaponSocket.localRotation;
            }
            else
            {
                SocketLocalPos = new Vector3(0f, 0.044f, 0f);
                SocketLocalRot = Quaternion.identity;
            }
            InkTankFill = FindDeep(_bones[(int)Bone.Chest], "InkTankFill");
            ComputeLeftSocketPose();
            WeaponSocketLeft = FindDeep(_bones[(int)Bone.HandL], LeftSocketName);
            IsValid = true;
            return true;
        }

        // 右手插槽的靜止模型位姿鏡射到左邊(x → −x;旋轉的鏡射 = (x, −y, −z, w)),再換成相對 Hand.L 的局部位姿
        private void ComputeLeftSocketPose()
        {
            int handR = (int)Bone.HandR;
            int handL = (int)Bone.HandL;
            Quaternion rightRot = _restModelRot[handR] * SocketLocalRot;
            Vector3 rightPos = _restModelPos[handR] + _restModelRot[handR] * SocketLocalPos;
            var mirroredRot = new Quaternion(rightRot.x, -rightRot.y, -rightRot.z, rightRot.w);
            var mirroredPos = new Vector3(-rightPos.x, rightPos.y, rightPos.z);
            Quaternion inverseHandL = Quaternion.Inverse(_restModelRot[handL]);
            SocketLocalRotLeft = inverseHandL * mirroredRot;
            SocketLocalPosLeft = inverseHandL * (mirroredPos - _restModelPos[handL]);
        }

        // 左手插槽(沒有就在 Hand.L 底下建立;只在換裝時呼叫)
        public Transform EnsureWeaponSocketLeft()
        {
            if (!IsValid)
            {
                return null;
            }
            if (WeaponSocketLeft == null)
            {
                Transform hand = _bones[(int)Bone.HandL];
                var socket = new GameObject(LeftSocketName).transform;
                socket.SetParent(hand, false);
                socket.gameObject.layer = hand.gameObject.layer;
                WeaponSocketLeft = socket;
            }
            WeaponSocketLeft.localPosition = SocketLocalPosLeft;
            WeaponSocketLeft.localRotation = SocketLocalRotLeft;
            return WeaponSocketLeft;
        }

        // 每幀開始:回到靜止姿勢(局部旋轉 = 靜止值、骨盆位移歸零)
        public void BeginPose()
        {
            for (int i = 0; i < Count; i++)
            {
                _local[i] = _restLocalRot[i];
            }
            _hipsOffset = Vector3.zero;
        }

        // 骨盆位移(模型空間,公尺):上下擺動、蹲低
        public void SetHipsOffset(Vector3 offset)
        {
            _hipsOffset = offset;
        }

        // 在骨頭自己的局部軸上疊加旋轉(觸手:局部 X = 往外甩、局部 Z = 左右擺)
        public void RotateLocal(Bone bone, Quaternion delta)
        {
            int i = (int)bone;
            _local[i] = _local[i] * delta;
        }

        // 在「模型空間的靜止軸」上疊加旋轉:delta 以模型軸表示(繞 +Y = 往右轉、繞 +X = 往前低頭),
        // 換算成骨頭局部旋轉後疊上去——軀幹骨的前傾、扭轉都用這個寫,不必記每根骨頭的 roll。
        public void RotateModel(Bone bone, Quaternion modelDelta)
        {
            int i = (int)bone;
            Quaternion rest = _restModelRot[i];
            _local[i] = _local[i] * (Quaternion.Inverse(rest) * modelDelta * rest);
        }

        // 直接指定骨頭在模型空間的旋轉(IK 結果);父骨的模型旋轉必須已是本幀最新值
        public void SetModelRotation(Bone bone, Quaternion modelRotation)
        {
            int i = (int)bone;
            int p = _parent[i];
            Quaternion parentModel = p >= 0 ? _model[p] : _rigRot;
            _local[i] = Quaternion.Inverse(parentModel) * modelRotation;
            _model[i] = modelRotation;
        }

        // 正向運動學:由局部旋轉算出每根骨頭的模型空間旋轉與位置(父在前、子在後)
        public void SolveFk()
        {
            for (int i = 0; i < Count; i++)
            {
                int p = _parent[i];
                Vector3 localPos = _restLocalPos[i];
                if (i == (int)Bone.Hips)
                {
                    // Root 的模型旋轉是單位旋轉(Rig -90° 與 Root +90° 抵銷),骨盆位移直接用模型軸
                    localPos += _hipsOffset;
                }
                if (p >= 0)
                {
                    _model[i] = _model[p] * _local[i];
                    _modelPos[i] = _modelPos[p] + _model[p] * localPos;
                }
                else
                {
                    _model[i] = _rigRot * _local[i];
                    _modelPos[i] = _rigPos + _rigRot * localPos;
                }
            }
        }

        // 兩骨 IK:上骨/下骨/末端骨(手或腳)。target = 末端骨原點(手腕、腳踝)的模型空間位置;
        // endRotation = 末端骨的模型空間旋轉。frontTowardsPole:腿 true、手臂 false。
        public void SolveLimb(Bone upper, Bone lower, Bone end, Vector3 target, Vector3 pole, Quaternion endRotation,
            bool frontTowardsPole)
        {
            int u = (int)upper;
            float upperLength = _restLocalPos[(int)lower].magnitude;
            float lowerLength = _restLocalPos[(int)end].magnitude;
            Vector3 root = _modelPos[u];
            Vector3 mid = TwoBoneIk.SolveMid(root, target, pole, upperLength, lowerLength, out Vector3 reached);
            TwoBoneIk.BoneRotations(root, mid, reached, pole, frontTowardsPole,
                out Quaternion upperRot, out Quaternion lowerRot);
            // 骨架的 roll 慣例(+Y 沿骨、+Z 朝前)與 LookRotation(前, 沿骨) 相同,直接當模型旋轉用
            SetModelRotation(upper, upperRot);
            SetModelRotation(lower, lowerRot);
            SetModelRotation(end, endRotation);
            _modelPos[(int)lower] = mid;
            _modelPos[(int)end] = reached;
        }

        // 下骨目前的模型旋轉(手腕打直時手的旋轉 = 前臂旋轉 × 手的靜止局部旋轉)
        public Quaternion StraightEndRotation(Bone lower, Bone end)
        {
            return _model[(int)lower] * _restLocalRot[(int)end];
        }

        // 寫回 Transform(每根骨頭一次)
        public void Apply()
        {
            for (int i = 0; i < Count; i++)
            {
                _bones[i].localRotation = _local[i];
            }
            _bones[(int)Bone.Hips].localPosition = _restLocalPos[(int)Bone.Hips] + _hipsOffset;
        }

        // 回到綁定時的靜止姿勢(停用時)
        public void ApplyRest()
        {
            if (!IsValid)
            {
                return;
            }
            for (int i = 0; i < Count; i++)
            {
                _bones[i].localRotation = _restLocalRot[i];
            }
            _bones[(int)Bone.Hips].localPosition = _restLocalPos[(int)Bone.Hips];
        }

        public static Transform FindDeep(Transform parent, string childName)
        {
            if (parent == null)
            {
                return null;
            }
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name == childName)
                {
                    return child;
                }
                Transform found = FindDeep(child, childName);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }
    }
}

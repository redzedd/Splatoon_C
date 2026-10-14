using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // Ultra Stamp 的槌頭擋彈判定(JP wiki ウルトラハンコ「振り下ろされたハンコ本体は強固な盾判定」):
    // 揮擊/連敲時跟著槌頭(Target 圖層的方塊,與使用者的 CharacterController 互相忽略),
    //   敵方子彈打到就停(InkBullet / InkProjectile 直擊判定,傷害不算——打不壞)、
    //   敵方爆炸的視線被擋(BlocksAttacksFrom)、敵方炸彈碰到就爆(BombRules.Detonates;S3 v1.2.0 起連炸彈爆風都擋)。
    // 槌舉起來的時候在頭上後方,正面自然擋不到(S3「振り上げのタイミングでは普通にダメージを受ける」)。
    // 只在揮擊中啟用;平常拿著、丟出後關閉。
    public sealed class UltraStampGuard : DeployableObject
    {
        [SerializeField, Tooltip("擋彈的碰撞體(槌頭大小)")]
        private Collider _shield;

        private CharacterController _ownerController;
        private bool _raised;

        public override float MaxHp => 1f;
        public override TargetClass TargetClass => TargetClass.Device;
        protected override bool Indestructible => true;
        protected override string BrokenSound => null;

        public bool Raised => _raised;

        public override bool BlocksAttacksFrom(Team attackerTeam)
        {
            return _raised && IsDeployed && attackerTeam != Team;
        }

        public override void Launch(Vector3 position, Vector3 velocity, SubWeaponConfig config, Inkling owner)
        {
            Debug.LogError("UltraStampGuard:請用 Attach(由 UltraStampSpecial 建立)", this);
            Expire();
        }

        public void Attach(Inkling owner)
        {
            Owner = owner;
            Deploy(owner, owner != null ? owner.Team : Team.Alpha);
            _ownerController = owner != null ? owner.GetComponent<CharacterController>() : null;
            if (_ownerController != null && _shield != null)
            {
                Physics.IgnoreCollision(_ownerController, _shield, true);
            }
            SetRaised(false);
        }

        // 每幀跟著槌頭;raised = 這一刻擋不擋
        public void Follow(Vector3 position, Quaternion rotation, bool raised)
        {
            transform.SetPositionAndRotation(position, rotation);
            SetRaised(raised);
        }

        private void SetRaised(bool raised)
        {
            _raised = raised;
            if (_shield != null && _shield.enabled != raised)
            {
                _shield.enabled = raised;
            }
        }

        public void Detach()
        {
            if (_ownerController != null && _shield != null)
            {
                Physics.IgnoreCollision(_ownerController, _shield, false);
            }
            _ownerController = null;
            SetRaised(false);
            if (gameObject.activeSelf)
            {
                Expire();
            }
        }

        protected override void OnDisable()
        {
            if (_ownerController != null && _shield != null)
            {
                Physics.IgnoreCollision(_ownerController, _shield, false);
            }
            _ownerController = null;
            base.OnDisable();
        }
    }
}

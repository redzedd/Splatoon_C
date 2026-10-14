using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 可設定的意圖來源:除錯指令與 AutoTest 以「真實輸入路徑」驅動角色(PlayerInputRouter.SetOverrideSource)。
    // 一次性按鍵(Jump/Special)用 Pulse:讀一次就自動清掉,等同「這一幀按下」。
    public sealed class ScriptedIntent : IPlayerIntentSource
    {
        public Vector2 Move;
        public Vector2 Look;
        public bool Attack;
        public bool Squid;
        public bool Sub;
        // 跳躍鍵按住(S3 按住跳得較高);預設按住
        public bool JumpHold = true;
        private bool _jumpPulse;
        private bool _specialPulse;
        private bool _mapPulse;
        private int _menuStepPulse;
        private bool _menuConfirmPulse;

        public Vector2 MoveInput => Move;
        public Vector2 LookDeltaDeg => Look;
        public bool AttackHeld => Attack;
        public bool SquidHeld => Squid;
        public bool SubWeaponHeld => Sub;
        public bool JumpHeld => JumpHold;

        public bool JumpPressedThisFrame
        {
            get
            {
                bool pressed = _jumpPulse;
                _jumpPulse = false;
                return pressed;
            }
        }

        public bool SpecialPressedThisFrame
        {
            get
            {
                bool pressed = _specialPulse;
                _specialPulse = false;
                return pressed;
            }
        }

        public void PressJump()
        {
            _jumpPulse = true;
        }

        // A4 超級跳目標選單:開關 / 上下移一格 / 確定(一次性,讀一次就清)
        public bool MapPressedThisFrame
        {
            get
            {
                bool pressed = _mapPulse;
                _mapPulse = false;
                return pressed;
            }
        }

        public int MenuStepThisFrame
        {
            get
            {
                int step = _menuStepPulse;
                _menuStepPulse = 0;
                return step;
            }
        }

        public bool MenuConfirmPressedThisFrame
        {
            get
            {
                bool pressed = _menuConfirmPulse;
                _menuConfirmPulse = false;
                return pressed;
            }
        }

        public void PressMap()
        {
            _mapPulse = true;
        }

        public void PressMenuStep(int step)
        {
            _menuStepPulse = step;
        }

        public void PressMenuConfirm()
        {
            _menuConfirmPulse = true;
        }

        public void PressSpecial()
        {
            _specialPulse = true;
        }

        // 除錯捷徑:接管某角色的輸入並回傳意圖物件
        public static ScriptedIntent Drive(Component character)
        {
            var router = character.GetComponent<PlayerInputRouter>();
            var intent = new ScriptedIntent();
            if (router != null)
            {
                router.SetOverrideSource(intent);
            }
            return intent;
        }
    }
}

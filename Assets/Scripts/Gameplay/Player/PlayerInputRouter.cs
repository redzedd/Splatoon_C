using UnityEngine;
using UnityEngine.InputSystem;

namespace SplatoonC.Gameplay.Player
{
    // 全專案唯一碰 Input System 的類別。引用 Project-wide Actions 的共用實例,
    // 絕不 new InputSystem_Actions()(會複製出第二份 action 導致鎖輸入失效)。
    // 測試可用 SetOverrideSource 換成 scripted intent(真實路徑驗收,不用合成輸入)。
    public sealed class PlayerInputRouter : MonoBehaviour, IPlayerIntentSource
    {
        private IPlayerIntentSource _overrideSource;

        [SerializeField, Tooltip("移動動作(Player/Move)")]
        private InputActionReference _move;

        [SerializeField, Tooltip("視角動作(Player/Look)")]
        private InputActionReference _look;

        [SerializeField, Tooltip("跳躍動作(Player/Jump)")]
        private InputActionReference _jump;

        [SerializeField, Tooltip("攻擊動作(Player/Attack,按住連射)")]
        private InputActionReference _attack;

        [SerializeField, Tooltip("烏賊態動作(Player/Crouch,按住變形)")]
        private InputActionReference _squid;

        [SerializeField, Tooltip("滑鼠靈敏度(度/像素);Pointer delta 已是每幀量,不乘 deltaTime")]
        private float _mouseSensitivity = 0.12f;

        [SerializeField, Tooltip("手把視角速度(度/秒);搖桿是軸值,須乘 deltaTime")]
        private float _gamepadLookSpeed = 220f;

        public Vector2 MoveInput
        {
            get
            {
                if (_overrideSource != null)
                {
                    return _overrideSource.MoveInput;
                }
                return _move == null ? Vector2.zero : _move.action.ReadValue<Vector2>();
            }
        }

        public bool JumpPressedThisFrame
        {
            get
            {
                if (_overrideSource != null)
                {
                    return _overrideSource.JumpPressedThisFrame;
                }
                return _jump != null && _jump.action.WasPressedThisFrame();
            }
        }

        public bool AttackHeld
        {
            get
            {
                if (_overrideSource != null)
                {
                    return _overrideSource.AttackHeld;
                }
                return _attack != null && _attack.action.IsPressed();
            }
        }

        public bool SquidHeld
        {
            get
            {
                if (_overrideSource != null)
                {
                    return _overrideSource.SquidHeld;
                }
                return _squid != null && _squid.action.IsPressed();
            }
        }

        // 回傳「已換算成角度增量」的視角輸入,呼叫端不需再管裝置量綱差異。
        public Vector2 LookDeltaDeg
        {
            get
            {
                if (_overrideSource != null)
                {
                    return _overrideSource.LookDeltaDeg;
                }
                if (_look == null)
                {
                    return Vector2.zero;
                }
                InputAction action = _look.action;
                Vector2 raw = action.ReadValue<Vector2>();
                bool fromGamepad = action.activeControl != null && action.activeControl.device is Gamepad;
                return fromGamepad
                    ? raw * (_gamepadLookSpeed * Time.deltaTime)
                    : raw * _mouseSensitivity;
            }
        }

        public void SetOverrideSource(IPlayerIntentSource source)
        {
            _overrideSource = source;
        }

        public void ClearOverrideSource()
        {
            _overrideSource = null;
        }

        private void Awake()
        {
            if (_move == null) Debug.LogError("PlayerInputRouter:缺少 Move 動作引用(Player/Move)", this);
            if (_look == null) Debug.LogError("PlayerInputRouter:缺少 Look 動作引用(Player/Look)", this);
            if (_jump == null) Debug.LogError("PlayerInputRouter:缺少 Jump 動作引用(Player/Jump)", this);
            if (_attack == null) Debug.LogError("PlayerInputRouter:缺少 Attack 動作引用(Player/Attack)", this);
            if (_squid == null) Debug.LogError("PlayerInputRouter:缺少烏賊態動作引用(Player/Crouch)", this);
        }

        private void OnEnable()
        {
            // 專案級動作理論上已自動啟用;重複 Enable 冪等,保險起見仍呼叫。
            if (_move != null) _move.action.Enable();
            if (_look != null) _look.action.Enable();
            if (_jump != null) _jump.action.Enable();
            if (_attack != null) _attack.action.Enable();
            if (_squid != null) _squid.action.Enable();
        }

        private void OnDisable()
        {
            if (_move != null) _move.action.Disable();
            if (_look != null) _look.action.Disable();
            if (_jump != null) _jump.action.Disable();
            if (_attack != null) _attack.action.Disable();
            if (_squid != null) _squid.action.Disable();
        }
    }
}

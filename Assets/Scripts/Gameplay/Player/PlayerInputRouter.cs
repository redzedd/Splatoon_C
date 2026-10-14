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

        // 比賽流程鎖(開場倒數、GAME! 之後、被擊倒):除了視角外全部意圖歸零。
        // 視角保留——倒數時可以先轉鏡頭看場地。
        public bool InputLocked { get; set; }

        // 動作鎖(A4 超級跳蓄力/飛行):被鎖的意圖歸零;一次性按鍵照樣從意圖來源讀掉(不留到解鎖後才觸發)。視角不受影響。
        public InputBlock ActionBlock { get; set; }

        // 超級跳目標選單開著(A4 SuperJumpHud):移動/跳/攻擊/副武器/大招歸零,選單自己讀 Menu* 輸入;烏賊與視角照常
        public bool MenuOpen { get; set; }

        private InputBlock Blocked => ActionBlock | (MenuOpen ? InputBlock.Actions : InputBlock.None);

        private bool IsBlocked(InputBlock channel)
        {
            return (Blocked & channel) != 0;
        }

        // 暫停選單開啟中(Time.timeScale 0):所有角色的全部意圖(含視角)歸零。靜態:暫停是全場的事。
        public static bool Paused { get; set; }

        // 視角設定(設定頁;SplatoonC.Core.Menu.LookSensitivity 換算):實體裝置輸入的靈敏度倍率與反轉。
        // 只套在滑鼠/手把,測試與 bot 的 override 來源不受影響。
        public static Vector2 LookScale { get; set; } = Vector2.one;
        public static bool InvertLookX { get; set; }
        public static bool InvertLookY { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Paused = false;
            LookScale = Vector2.one;
            InvertLookX = false;
            InvertLookY = false;
        }

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

        [SerializeField, Tooltip("副武器動作(Player/SubWeapon,按住顯示拋物線、放開投擲)")]
        private InputActionReference _subWeapon;

        [SerializeField, Tooltip("大招動作(Player/Special,按下啟動)")]
        private InputActionReference _special;

        [SerializeField, Tooltip("滑鼠靈敏度(度/像素);Pointer delta 已是每幀量,不乘 deltaTime")]
        private float _mouseSensitivity = 0.12f;

        [SerializeField, Tooltip("手把視角速度(度/秒);搖桿是軸值,須乘 deltaTime")]
        private float _gamepadLookSpeed = 220f;

        public Vector2 MoveInput
        {
            get
            {
                if (InputLocked || Paused || IsBlocked(InputBlock.Move))
                {
                    return Vector2.zero;
                }
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
                if (InputLocked || Paused)
                {
                    return false;
                }
                if (IsBlocked(InputBlock.Jump))
                {
                    // 被鎖:脈衝照樣讀掉丟棄,解鎖後不會補跳
                    if (_overrideSource != null)
                    {
                        _ = _overrideSource.JumpPressedThisFrame;
                    }
                    return false;
                }
                if (_overrideSource != null)
                {
                    return _overrideSource.JumpPressedThisFrame;
                }
                return _jump != null && _jump.action.WasPressedThisFrame();
            }
        }

        public bool JumpHeld
        {
            get
            {
                if (InputLocked || Paused || IsBlocked(InputBlock.Jump))
                {
                    return false;
                }
                if (_overrideSource != null)
                {
                    return _overrideSource.JumpHeld;
                }
                return _jump != null && _jump.action.IsPressed();
            }
        }

        public bool AttackHeld
        {
            get
            {
                if (InputLocked || Paused || IsBlocked(InputBlock.Attack))
                {
                    return false;
                }
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
                if (InputLocked || Paused)
                {
                    return false;
                }
                if (_overrideSource != null)
                {
                    return _overrideSource.SquidHeld;
                }
                return _squid != null && _squid.action.IsPressed();
            }
        }

        public bool SubWeaponHeld
        {
            get
            {
                if (InputLocked || Paused || IsBlocked(InputBlock.Sub))
                {
                    return false;
                }
                if (_overrideSource != null)
                {
                    return _overrideSource.SubWeaponHeld;
                }
                return _subWeapon != null && _subWeapon.action.IsPressed();
            }
        }

        public bool SpecialPressedThisFrame
        {
            get
            {
                if (InputLocked || Paused)
                {
                    return false;
                }
                if (IsBlocked(InputBlock.Special))
                {
                    if (_overrideSource != null)
                    {
                        _ = _overrideSource.SpecialPressedThisFrame;
                    }
                    return false;
                }
                if (_overrideSource != null)
                {
                    return _overrideSource.SpecialPressedThisFrame;
                }
                return _special != null && _special.action.WasPressedThisFrame();
            }
        }

        // ── 超級跳目標選單(A4;不受 ActionBlock / MenuOpen 影響——選單開著時就是靠這些操作)──
        // 裝置綁定沿用專案級動作裡原本沒用到的 Player/Interact(E、手把北鍵)與 Previous / Next(1 / 2、十字鍵左右),
        // 以 InputSystem.actions 查找(不改 Player prefab 的欄位接線)。

        // 開關選單(S3 按 X 開地圖)
        public bool MapPressedThisFrame
        {
            get
            {
                if (InputLocked || Paused)
                {
                    return false;
                }
                if (_overrideSource != null)
                {
                    return _overrideSource.MapPressedThisFrame;
                }
                InputAction action = MenuAction(ref _mapAction, "Player/Interact");
                return action != null && action.WasPressedThisFrame();
            }
        }

        // 選單上下移一格(−1 上一個 / +1 下一個;裝置:1/2、十字鍵左右;搖桿/WASD 上下由選單讀 MenuNavigate 自己判斷)
        public int MenuStepThisFrame
        {
            get
            {
                if (InputLocked || Paused)
                {
                    return 0;
                }
                if (_overrideSource != null)
                {
                    return _overrideSource.MenuStepThisFrame;
                }
                InputAction previous = MenuAction(ref _previousAction, "Player/Previous");
                InputAction next = MenuAction(ref _nextAction, "Player/Next");
                int step = 0;
                if (previous != null && previous.WasPressedThisFrame())
                {
                    step--;
                }
                if (next != null && next.WasPressedThisFrame())
                {
                    step++;
                }
                return step;
            }
        }

        // 確定跳躍(S3 按 A;這裡是跳躍鍵或攻擊鍵)
        public bool MenuConfirmPressedThisFrame
        {
            get
            {
                if (InputLocked || Paused)
                {
                    return false;
                }
                if (_overrideSource != null)
                {
                    return _overrideSource.MenuConfirmPressedThisFrame;
                }
                return (_jump != null && _jump.action.WasPressedThisFrame())
                       || (_attack != null && _attack.action.WasPressedThisFrame());
            }
        }

        // 選單游標用的移動輸入(不受 MenuOpen 歸零)
        public Vector2 MenuNavigate
        {
            get
            {
                if (InputLocked || Paused)
                {
                    return Vector2.zero;
                }
                if (_overrideSource != null)
                {
                    return _overrideSource.MoveInput;
                }
                return _move == null ? Vector2.zero : _move.action.ReadValue<Vector2>();
            }
        }

        private InputAction _mapAction;
        private InputAction _previousAction;
        private InputAction _nextAction;
        private bool _menuActionsResolved;

        private InputAction MenuAction(ref InputAction cached, string path)
        {
            if (!_menuActionsResolved)
            {
                _menuActionsResolved = true;
                InputActionAsset actions = InputSystem.actions;
                if (actions != null)
                {
                    _mapAction = actions.FindAction("Player/Interact");
                    _previousAction = actions.FindAction("Player/Previous");
                    _nextAction = actions.FindAction("Player/Next");
                    _mapAction?.Enable();
                    _previousAction?.Enable();
                    _nextAction?.Enable();
                }
            }
            return cached;
        }

        // 回傳「已換算成角度增量」的視角輸入,呼叫端不需再管裝置量綱差異。
        public Vector2 LookDeltaDeg
        {
            get
            {
                if (Paused)
                {
                    return Vector2.zero;
                }
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
                Vector2 degrees = fromGamepad
                    ? raw * (_gamepadLookSpeed * Time.deltaTime)
                    : raw * _mouseSensitivity;
                Vector2 scale = LookScale;
                return new Vector2(degrees.x * scale.x * (InvertLookX ? -1f : 1f),
                    degrees.y * scale.y * (InvertLookY ? -1f : 1f));
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
            if (_subWeapon == null) Debug.LogError("PlayerInputRouter:缺少副武器動作引用(Player/SubWeapon)", this);
            if (_special == null) Debug.LogError("PlayerInputRouter:缺少大招動作引用(Player/Special)", this);
        }

        private void OnEnable()
        {
            // 專案級動作理論上已自動啟用;重複 Enable 冪等,保險起見仍呼叫。
            if (_move != null) _move.action.Enable();
            if (_look != null) _look.action.Enable();
            if (_jump != null) _jump.action.Enable();
            if (_attack != null) _attack.action.Enable();
            if (_squid != null) _squid.action.Enable();
            if (_subWeapon != null) _subWeapon.action.Enable();
            if (_special != null) _special.action.Enable();
        }

        // 刻意不在 OnDisable 關閉動作:專案級動作是全場共用的單一實例,
        // 場上每個角色(含 bot)都掛著本元件——任一 bot 被銷毀時關掉動作,玩家的輸入就一起斷了。
    }
}

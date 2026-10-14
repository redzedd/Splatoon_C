using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace SplatoonC.Gameplay.UI.Menu
{
    // 選單的輸入小工具:確保場景有 EventSystem(Input System 的 UI 模組,鍵盤/滑鼠/手把導覽都走它),
    // 以及幾個直接輪詢的按鍵(「按任意鍵」、暫停鍵、切換類別)。
    public static class MenuInput
    {
        public static EventSystem EnsureEventSystem()
        {
            EventSystem system = EventSystem.current;
            if (system == null)
            {
                system = Object.FindAnyObjectByType<EventSystem>();
            }
            if (system == null)
            {
                var go = new GameObject("EventSystem");
                system = go.AddComponent<EventSystem>();
                go.AddComponent<InputSystemUIInputModule>();
            }
            var module = system.GetComponent<InputSystemUIInputModule>();
            if (module != null)
            {
                // 點到空白處不要清掉選取(手把/鍵盤接著操作才不會失焦)
                module.deselectOnBackgroundClick = false;
            }
            return system;
        }

        // 標題畫面「按任意鍵」:鍵盤任意鍵、滑鼠左鍵、手把常用按鍵
        public static bool AnyKeyPressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.anyKey.wasPressedThisFrame)
            {
                return true;
            }
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                return true;
            }
            Gamepad gamepad = Gamepad.current;
            return gamepad != null && (gamepad.buttonSouth.wasPressedThisFrame || gamepad.buttonEast.wasPressedThisFrame
                                       || gamepad.buttonNorth.wasPressedThisFrame || gamepad.buttonWest.wasPressedThisFrame
                                       || gamepad.startButton.wasPressedThisFrame);
        }

        // 暫停:Esc / 手把 Start(+)
        public static bool PausePressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                return true;
            }
            Gamepad gamepad = Gamepad.current;
            return gamepad != null && gamepad.startButton.wasPressedThisFrame;
        }

        public static bool StartButtonPressed()
        {
            Gamepad gamepad = Gamepad.current;
            return gamepad != null && gamepad.startButton.wasPressedThisFrame;
        }

        // 切換類別分頁:Q / E、手把 LB / RB;回傳 -1 / 0 / +1
        public static int TabDelta()
        {
            int delta = 0;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.qKey.wasPressedThisFrame)
                {
                    delta--;
                }
                if (keyboard.eKey.wasPressedThisFrame)
                {
                    delta++;
                }
            }
            Gamepad gamepad = Gamepad.current;
            if (gamepad != null)
            {
                if (gamepad.leftShoulder.wasPressedThisFrame)
                {
                    delta--;
                }
                if (gamepad.rightShoulder.wasPressedThisFrame)
                {
                    delta++;
                }
            }
            return delta;
        }

        // 關閉暫停時,遊戲操作鍵是否還按著(按著就先不解鎖角色輸入,避免「點繼續 = 開一槍 / 跳一下」)
        public static bool AnyGameplayButtonHeld()
        {
            Mouse mouse = Mouse.current;
            if (mouse != null && (mouse.leftButton.isPressed || mouse.rightButton.isPressed))
            {
                return true;
            }
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && (keyboard.enterKey.isPressed || keyboard.spaceKey.isPressed
                                     || keyboard.escapeKey.isPressed))
            {
                return true;
            }
            Gamepad gamepad = Gamepad.current;
            return gamepad != null && (gamepad.buttonSouth.isPressed || gamepad.buttonEast.isPressed
                                       || gamepad.startButton.isPressed || gamepad.rightTrigger.isPressed);
        }
    }
}

using System;
using System.Collections.Generic;
using SplatoonC.Gameplay.Audio;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SplatoonC.Gameplay.UI.Menu
{
    // 選單頁面堆疊 + 共用的操作規則(主選單與暫停選單共用):
    //   - 進頁淡入滑入;返回(Esc / Backspace / 手把 B / 滑鼠右鍵)彈出一頁並播 ui_back,堆疊底再返回交給 RootBack。
    //   - 永遠保持一個選取項目(滑鼠點到空白處、頁面切換後都會補回),手把/鍵盤才不會「失焦」。
    //   - 選取改變播 ui_move(程式指定的選取不播)。
    // 同一幀內剛換頁就忽略返回鍵:開頁的那一下(例如按 Esc 開暫停)不會立刻把頁關掉。
    public sealed class MenuNavigator
    {
        private const float ShowSeconds = 0.18f;
        private const float SlideDistance = 48f;

        private readonly List<MenuPage> _stack = new List<MenuPage>();
        private int _lastTransitionFrame = -1;
        private GameObject _lastSelected;
        private MenuPage _animating;
        private float _animStart;
        private float _animFromX;

        public MenuPage Current => _stack.Count > 0 ? _stack[_stack.Count - 1] : null;
        public int Depth => _stack.Count;

        // 堆疊底再按返回
        public event Action RootBack;

        // 目前頁面改變(提示列更新)
        public event Action<MenuPage> PageChanged;

        // 停用返回鍵輪詢(轉場中)
        public bool InputEnabled { get; set; } = true;

        public bool Contains(MenuPage page)
        {
            return _stack.Contains(page);
        }

        public void Push(MenuPage page)
        {
            if (page == null)
            {
                return;
            }
            MenuPage previous = Current;
            if (previous != null)
            {
                RememberSelection(previous);
                if (page.IsOverlay)
                {
                    previous.Group.interactable = false;
                }
                else
                {
                    Hide(previous);
                }
            }
            _stack.Remove(page);
            _stack.Add(page);
            Show(page, true);
        }

        public void Pop()
        {
            if (_stack.Count == 0)
            {
                return;
            }
            MenuPage top = Current;
            _stack.RemoveAt(_stack.Count - 1);
            Hide(top);
            MenuPage below = Current;
            if (below != null)
            {
                below.Group.interactable = true;
                if (!below.Root.gameObject.activeSelf)
                {
                    Show(below, false);
                }
                else
                {
                    Restore(below);
                    PageChanged?.Invoke(below);
                    _lastTransitionFrame = Time.frameCount;
                }
            }
        }

        // 彈到 page 為止(page 在堆疊內);不在堆疊內就整個換成 chain
        public void JumpTo(MenuPage page, params MenuPage[] chain)
        {
            if (_stack.Contains(page))
            {
                if (Current == page)
                {
                    return;
                }
                // 中間的頁面直接收掉,不逐頁顯示(避免中途頁面白白重建)
                while (Current != page)
                {
                    MenuPage top = Current;
                    _stack.RemoveAt(_stack.Count - 1);
                    Hide(top);
                }
                page.Group.interactable = true;
                Show(page, false);
                return;
            }
            ResetTo(chain);
        }

        // 整個換成指定堆疊(最後一頁顯示)
        public void ResetTo(params MenuPage[] pages)
        {
            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                Hide(_stack[i]);
            }
            _stack.Clear();
            for (int i = 0; i < pages.Length; i++)
            {
                if (pages[i] != null)
                {
                    _stack.Add(pages[i]);
                    pages[i].Group.interactable = true;
                }
            }
            if (Current != null)
            {
                Show(Current, true);
            }
        }

        public void Clear()
        {
            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                Hide(_stack[i]);
            }
            _stack.Clear();
        }

        public void Back()
        {
            MenuPage current = Current;
            if (current == null || Time.frameCount == _lastTransitionFrame)
            {
                return;
            }
            if (current.OnBack())
            {
                return;
            }
            if (_stack.Count > 1)
            {
                GameAudio.Play2D("ui_back");
                Pop();
                return;
            }
            RootBack?.Invoke();
        }

        // 程式指定選取(不播移動音)
        public void Select(Selectable selectable)
        {
            EventSystem system = EventSystem.current;
            if (selectable == null || system == null)
            {
                return;
            }
            system.SetSelectedGameObject(selectable.gameObject);
            _lastSelected = selectable.gameObject;
        }

        public void Update()
        {
            MenuPage current = Current;
            if (current == null)
            {
                return;
            }
            AnimateShow();
            KeepSelection(current);
            if (InputEnabled && BackPressed())
            {
                Back();
            }
            // Back 可能已換頁;換頁的那一幀不跑頁面輸入(同一下按鍵不會連觸發兩頁)
            if (Time.frameCount != _lastTransitionFrame)
            {
                Current?.Tick();
            }
        }

        public static bool BackPressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && (keyboard.escapeKey.wasPressedThisFrame || keyboard.backspaceKey.wasPressedThisFrame))
            {
                return true;
            }
            Gamepad gamepad = Gamepad.current;
            if (gamepad != null && gamepad.buttonEast.wasPressedThisFrame)
            {
                return true;
            }
            Mouse mouse = Mouse.current;
            return mouse != null && mouse.rightButton.wasPressedThisFrame;
        }

        private void Show(MenuPage page, bool freshEntry)
        {
            // 上一頁的進場動畫還沒播完(例如立刻疊上確認框):直接收尾,不要停在半透明
            FinishAnimation();
            page.Root.gameObject.SetActive(true);
            page.Group.interactable = true;
            page.Group.alpha = 0f;
            _animating = page;
            _animStart = Time.unscaledTime;
            _animFromX = freshEntry ? SlideDistance : -SlideDistance;
            page.Root.anchoredPosition = new Vector2(_animFromX, 0f);
            page.OnShow();
            Restore(page);
            _lastTransitionFrame = Time.frameCount;
            PageChanged?.Invoke(page);
        }

        private void Hide(MenuPage page)
        {
            if (page.Root.gameObject.activeSelf)
            {
                page.OnHide();
                page.Root.gameObject.SetActive(false);
            }
            if (_animating == page)
            {
                _animating = null;
            }
        }

        private void FinishAnimation()
        {
            if (_animating != null)
            {
                _animating.Group.alpha = 1f;
                _animating.Root.anchoredPosition = Vector2.zero;
                _animating = null;
            }
        }

        private void AnimateShow()
        {
            if (_animating == null)
            {
                return;
            }
            float t = Mathf.Clamp01((Time.unscaledTime - _animStart) / ShowSeconds);
            float eased = 1f - (1f - t) * (1f - t);
            _animating.Group.alpha = eased;
            _animating.Root.anchoredPosition = new Vector2(Mathf.Lerp(_animFromX, 0f, eased), 0f);
            if (t >= 1f)
            {
                _animating.Root.anchoredPosition = Vector2.zero;
                _animating.Group.alpha = 1f;
                _animating = null;
            }
        }

        private void Restore(MenuPage page)
        {
            Selectable target = page.LastSelected != null && IsUsable(page.LastSelected, page)
                ? page.LastSelected
                : page.DefaultSelection;
            Select(target);
        }

        private void RememberSelection(MenuPage page)
        {
            EventSystem system = EventSystem.current;
            GameObject selected = system != null ? system.currentSelectedGameObject : null;
            if (selected != null && selected.transform.IsChildOf(page.Root))
            {
                page.LastSelected = selected.GetComponent<Selectable>();
            }
        }

        private void KeepSelection(MenuPage page)
        {
            EventSystem system = EventSystem.current;
            if (system == null)
            {
                return;
            }
            GameObject selected = system.currentSelectedGameObject;
            Selectable selectable = selected != null ? selected.GetComponent<Selectable>() : null;
            if (selectable == null || !IsUsable(selectable, page))
            {
                Restore(page);
                return;
            }
            if (selected != _lastSelected)
            {
                // 使用者移動了選取(方向鍵、滑鼠移入)
                if (_lastSelected != null)
                {
                    GameAudio.Play2D("ui_move", 0.8f);
                }
                _lastSelected = selected;
                page.LastSelected = selectable;
            }
        }

        private static bool IsUsable(Selectable selectable, MenuPage page)
        {
            return selectable != null && selectable.isActiveAndEnabled && selectable.IsInteractable()
                   && selectable.transform.IsChildOf(page.Root);
        }
    }
}

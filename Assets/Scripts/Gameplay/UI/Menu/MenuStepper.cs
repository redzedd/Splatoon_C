using System;
using SplatoonC.Core.Menu;
using SplatoonC.Gameplay.Audio;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SplatoonC.Gameplay.UI.Menu
{
    // 選項列(設定頁、對戰準備):左邊名稱、右邊「◀ 值 ▶」;條狀模式(音量、靈敏度)多一條可拖曳的刻度條。
    // 左右鍵/搖桿調整,上下交給一般導覽;確定鍵在非條狀選項上循環切換(開/關、難度)。
    [DisallowMultipleComponent]
    public sealed class MenuStepper : Selectable, ISubmitHandler, IPointerClickHandler, IDragHandler,
        IInitializePotentialDragHandler
    {
        public Image Background;
        public Image Frame;
        public Text Label;
        public Text ValueText;
        public Image LeftArrow;
        public Image RightArrow;
        // 條狀模式
        public RectTransform Bar;
        public Image BarFill;
        public Image BarKnob;

        public Color NormalColor = MenuTheme.Item;
        public Color SelectedColor = MenuTheme.Accent;

        // 選取時顯示在頁面說明列的文字
        public string Description = "";

        public int Count { get; private set; } = 2;
        public int Index { get; private set; }
        public bool IsBar => Bar != null;

        public event Action<int> Changed;
        public event Action Focused;

        private Func<int, string> _format;
        private bool _draggingBar;
        private float _selected;
        private float _bump;
        private bool _wasFocused;

        public bool IsFocused => EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject;

        protected override void Awake()
        {
            base.Awake();
            transition = Transition.None;
        }

        public void Setup(int count, int index, Func<int, string> format)
        {
            Count = Mathf.Max(1, count);
            _format = format;
            SetIndex(index, false);
        }

        public void SetIndex(int index, bool notify)
        {
            int clamped = Mathf.Clamp(index, 0, Count - 1);
            bool changed = clamped != Index;
            Index = clamped;
            RefreshValue();
            if (changed && notify)
            {
                _bump = 1f;
                GameAudio.Play2D("ui_move");
                Changed?.Invoke(Index);
            }
        }

        // delta:±1;wrap:到底是否回頭
        public void Step(int delta, bool wrap)
        {
            if (!IsInteractable())
            {
                return;
            }
            int next = MenuMath.Cycle(Index, delta, Count, wrap);
            if (next == Index)
            {
                // 已到底:輕微回彈提示
                _bump = 0.4f;
                return;
            }
            SetIndex(next, true);
        }

        private void RefreshValue()
        {
            if (ValueText != null)
            {
                ValueText.text = _format != null ? _format(Index) : Index.ToString();
            }
            if (BarFill != null)
            {
                // 用錨點拉寬(圓頭九宮格),不用 Filled
                BarFill.rectTransform.anchorMax = new Vector2(Fraction, 1f);
                BarFill.enabled = Fraction > 0.001f;
            }
            if (BarKnob != null && Bar != null)
            {
                BarKnob.rectTransform.anchoredPosition = new Vector2(Fraction * Bar.rect.width, 0f);
            }
        }

        private float Fraction => Count > 1 ? (float)Index / (Count - 1) : 1f;

        // 走到底的方向箭頭變淡(開關類左右循環,不淡)
        private bool CanStep(int delta)
        {
            if (!IsBar && Count == 2)
            {
                return true;
            }
            return delta < 0 ? Index > 0 : Index < Count - 1;
        }

        public override void OnMove(AxisEventData eventData)
        {
            if (eventData.moveDir == MoveDirection.Left || eventData.moveDir == MoveDirection.Right)
            {
                // 條狀/多段選項停在兩端;兩個值的開關左右都能切
                Step(eventData.moveDir == MoveDirection.Right ? 1 : -1, !IsBar && Count == 2);
                eventData.Use();
                return;
            }
            base.OnMove(eventData);
        }

        public void OnSubmit(BaseEventData eventData)
        {
            if (!IsBar)
            {
                Step(1, true);
            }
        }

        public override void OnPointerEnter(PointerEventData eventData)
        {
            base.OnPointerEnter(eventData);
            if (IsInteractable() && EventSystem.current != null && !IsFocused)
            {
                EventSystem.current.SetSelectedGameObject(gameObject, eventData);
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !IsInteractable())
            {
                return;
            }
            if (HitArrow(LeftArrow, eventData))
            {
                Step(-1, !IsBar);
                return;
            }
            if (HitArrow(RightArrow, eventData))
            {
                Step(1, !IsBar);
                return;
            }
            // 條狀選項:按在條上已由 OnPointerDown 設值;按在名稱等其他地方只選取不改值
            if (!IsBar)
            {
                Step(1, true);
            }
        }

        public override void OnPointerDown(PointerEventData eventData)
        {
            base.OnPointerDown(eventData);
            _draggingBar = IsBar && eventData.button == PointerEventData.InputButton.Left && IsInteractable()
                           && PointerOnBar(eventData);
            if (_draggingBar)
            {
                SetFromPointer(eventData);
            }
        }

        public override void OnPointerUp(PointerEventData eventData)
        {
            base.OnPointerUp(eventData);
            _draggingBar = false;
        }

        public void OnInitializePotentialDrag(PointerEventData eventData)
        {
            eventData.useDragThreshold = false;
        }

        public void OnDrag(PointerEventData eventData)
        {
            // 只有從刻度條上開始的拖曳才調值
            if (_draggingBar && IsInteractable())
            {
                SetFromPointer(eventData);
            }
        }

        private static bool HitArrow(Image arrow, PointerEventData eventData)
        {
            if (arrow == null || !arrow.enabled)
            {
                return false;
            }
            RectTransform rt = arrow.rectTransform;
            // 箭頭命中範圍放大一倍(小目標難點)
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, eventData.position,
                    eventData.pressEventCamera, out Vector2 local))
            {
                return false;
            }
            Rect r = rt.rect;
            r.xMin -= r.width * 0.5f;
            r.xMax += r.width * 0.5f;
            r.yMin -= r.height * 0.5f;
            r.yMax += r.height * 0.5f;
            return r.Contains(local);
        }

        // 指標是否在刻度條上(上下左右各放寬一點,細條好點)
        private bool PointerOnBar(PointerEventData eventData)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(Bar, eventData.position, eventData.pressEventCamera,
                    out Vector2 local))
            {
                return false;
            }
            Rect r = Bar.rect;
            r.xMin -= 16f;
            r.xMax += 16f;
            r.yMin -= 20f;
            r.yMax += 20f;
            return r.Contains(local);
        }

        private void SetFromPointer(PointerEventData eventData)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(Bar, eventData.position, eventData.pressEventCamera,
                    out Vector2 local))
            {
                return;
            }
            Rect r = Bar.rect;
            float fraction = (local.x - r.xMin) / Mathf.Max(1f, r.width);
            SetIndex(MenuMath.StepFromFraction(fraction, Count), true);
        }

        private void Update()
        {
            bool focused = IsFocused;
            if (focused && !_wasFocused)
            {
                Focused?.Invoke();
            }
            _wasFocused = focused;
            float dt = Time.unscaledDeltaTime;
            _selected = Mathf.MoveTowards(_selected, focused ? 1f : 0f, dt * 9f);
            _bump = Mathf.MoveTowards(_bump, 0f, dt * 5f);
            float s = Mathf.SmoothStep(0f, 1f, _selected);
            Color dark = MenuTheme.TextOnAccent;
            if (Background != null)
            {
                Background.color = Color.Lerp(NormalColor, SelectedColor, s);
            }
            if (Frame != null)
            {
                Frame.color = Ui.WithAlpha(Color.white, s);
            }
            if (Label != null)
            {
                Label.color = Color.Lerp(Color.white, dark, s);
            }
            if (ValueText != null)
            {
                ValueText.color = Color.Lerp(Color.white, dark, s);
                float pop = 1f + 0.18f * Mathf.Sin(_bump * Mathf.PI);
                ValueText.rectTransform.localScale = new Vector3(pop, pop, 1f);
            }
            Color arrowColor = Color.Lerp(new Color(1f, 1f, 1f, 0.7f), dark, s);
            if (LeftArrow != null)
            {
                LeftArrow.color = Ui.WithAlpha(arrowColor, CanStep(-1) ? arrowColor.a : 0.2f);
            }
            if (RightArrow != null)
            {
                RightArrow.color = Ui.WithAlpha(arrowColor, CanStep(1) ? arrowColor.a : 0.2f);
            }
            if (BarFill != null)
            {
                BarFill.color = Color.Lerp(MenuTheme.Accent, MenuTheme.Secondary, s);
            }
            if (BarKnob != null)
            {
                BarKnob.color = Color.Lerp(Color.white, dark, s);
                // 版面在第一幀才算好寬度:持續對齊刻度位置(便宜)
                BarKnob.rectTransform.anchoredPosition = new Vector2(Fraction * Bar.rect.width, 0f);
            }
        }
    }
}

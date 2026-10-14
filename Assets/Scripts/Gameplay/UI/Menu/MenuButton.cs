using System;
using SplatoonC.Gameplay.Audio;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SplatoonC.Gameplay.UI.Menu
{
    // 選單按鈕/卡片(S3 風格):選取中 = 螢光黃綠底 + 黑字 + 放大 + 左側烏賊游標;滑鼠移入即選取(與手把/鍵盤同一套視覺)。
    // 鎖住(即將推出)的項目仍可選取瀏覽,按下只會搖一下並播返回音。動畫一律用 unscaled 時間(暫停中也要動)。
    [DisallowMultipleComponent]
    public sealed class MenuButton : Button
    {
        [Tooltip("底板")]
        public Image Background;

        [Tooltip("選取外框(選取時淡入)")]
        public Image Frame;

        [Tooltip("主文字")]
        public Text Label;

        [Tooltip("副文字")]
        public Text Detail;

        [Tooltip("圖示(選取時轉黑)")]
        public Image Icon;

        [Tooltip("縮放/搖晃的內容根(預設 = 自己)")]
        public RectTransform Content;

        [Tooltip("選取游標(烏賊頭)")]
        public Image CursorIcon;

        public Color NormalColor = MenuTheme.Item;
        public Color SelectedColor = MenuTheme.Accent;
        public Color LabelColor = Color.white;
        public Color DetailColor = MenuTheme.TextDim;
        public Color IconColor = Color.white;

        // 即將推出:可選取、不可執行
        public bool Locked;

        // 按下(已過鎖定檢查)
        public event Action Activated;

        // 成為選取項目(詳細面板跟著更新)
        public event Action Focused;

        private float _selected;
        private float _press;
        private float _shake;
        private bool _wasSelected;
        private Vector2 _contentBase;
        private bool _baseCaptured;

        public bool IsFocused => EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject;

        protected override void Awake()
        {
            base.Awake();
            transition = Transition.None;
            onClick.AddListener(OnClicked);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            // 重新顯示時從目前狀態直接開始,不要從上次殘留的動畫值補間
            _selected = IsFocused ? 1f : 0f;
            _press = 0f;
            _shake = 0f;
            Apply();
        }

        public override void OnPointerEnter(PointerEventData eventData)
        {
            base.OnPointerEnter(eventData);
            if (IsInteractable() && EventSystem.current != null && !IsFocused)
            {
                EventSystem.current.SetSelectedGameObject(gameObject, eventData);
            }
        }

        private void OnClicked()
        {
            if (Locked)
            {
                GameAudio.Play2D("ui_back");
                _shake = 1f;
                return;
            }
            GameAudio.Play2D("ui_select");
            _press = 1f;
            Activated?.Invoke();
        }

        private void Update()
        {
            bool focused = IsFocused;
            if (focused && !_wasSelected)
            {
                Focused?.Invoke();
            }
            _wasSelected = focused;
            float dt = Time.unscaledDeltaTime;
            _selected = Mathf.MoveTowards(_selected, focused ? 1f : 0f, dt * 9f);
            _press = Mathf.MoveTowards(_press, 0f, dt * 6f);
            _shake = Mathf.MoveTowards(_shake, 0f, dt * 3.5f);
            Apply();
        }

        private void Apply()
        {
            float s = Mathf.SmoothStep(0f, 1f, _selected);
            Color selectedColor = Locked ? new Color(0.62f, 0.62f, 0.68f) : SelectedColor;
            Color normalColor = Locked ? MenuTheme.ItemLocked : NormalColor;
            if (Background != null)
            {
                Background.color = Color.Lerp(normalColor, selectedColor, s);
            }
            if (Frame != null)
            {
                Frame.color = Ui.WithAlpha(Color.white, s);
            }
            Color dark = MenuTheme.TextOnAccent;
            if (Label != null)
            {
                Label.color = Color.Lerp(Locked ? MenuTheme.TextDim : LabelColor, dark, s);
            }
            if (Detail != null)
            {
                Detail.color = Color.Lerp(DetailColor, dark, s);
            }
            if (Icon != null)
            {
                Icon.color = Color.Lerp(Locked ? Ui.WithAlpha(IconColor, 0.4f) : IconColor, dark, s);
            }
            if (CursorIcon != null)
            {
                CursorIcon.enabled = s > 0.02f;
                CursorIcon.color = Ui.WithAlpha(Locked ? Color.white : MenuTheme.Secondary, s);
                float bob = Mathf.Sin(Time.unscaledTime * 6f) * 5f;
                CursorIcon.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -90f + bob);
            }
            float scale = 1f + 0.045f * s - 0.06f * Mathf.Sin(_press * Mathf.PI);
            if (Content == null)
            {
                // 沒有內容根:只縮放自己(不搖,避免改到版面位置)
                transform.localScale = new Vector3(scale, scale, 1f);
                return;
            }
            if (!_baseCaptured)
            {
                _contentBase = Content.anchoredPosition;
                _baseCaptured = true;
            }
            Content.localScale = new Vector3(scale, scale, 1f);
            float shakeX = Mathf.Sin(_shake * 40f) * 10f * _shake;
            Content.anchoredPosition = _contentBase + new Vector2(shakeX, 0f);
        }

        // 版面移動後(捲動區重排)重新記錄基準位置
        public void RebaseContent()
        {
            _baseCaptured = false;
        }
    }
}

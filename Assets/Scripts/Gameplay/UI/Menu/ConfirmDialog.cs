using System;
using UnityEngine;
using UnityEngine.UI;

namespace SplatoonC.Gameplay.UI.Menu
{
    // 確認對話框(疊在目前頁面上):訊息 + 「確定 / 取消」,預設選「取消」(誤按不會出事)。
    public sealed class ConfirmDialog : MenuPage
    {
        private MenuButton _confirm;
        private MenuButton _cancel;
        private Text _message;
        private RectTransform _panel;
        private Action _onConfirm;
        private MenuNavigator _owner;
        private float _shownAt;

        public MenuButton ConfirmButton => _confirm;
        public MenuButton CancelButton => _cancel;
        public string Message => _message.text;

        public override bool IsOverlay => true;
        public override string Hints => "← →:選擇|Enter / A:決定|Esc / B:取消";
        public override Selectable DefaultSelection => _cancel;

        protected override void Build(RectTransform root)
        {
            Image shade = Ui.Image("Shade", root, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, null,
                new Color(0f, 0f, 0f, 0.62f));
            shade.rectTransform.anchorMin = Vector2.zero;
            shade.rectTransform.anchorMax = Vector2.one;
            shade.rectTransform.sizeDelta = Vector2.zero;
            // 擋住下面那頁的滑鼠
            shade.raycastTarget = true;

            _panel = Ui.Rect("Panel", root, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(940f, 400f));
            _panel.pivot = new Vector2(0.5f, 0.5f);
            Image splat = Ui.Image("Splat", _panel, new Vector2(0f, 1f), new Vector2(40f, -10f), new Vector2(300f, 300f),
                UiSprites.Splat(256, 61), Ui.WithAlpha(MenuTheme.Secondary, 0.95f));
            splat.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            MenuTheme.Panel9("Back", _panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(940f, 400f),
                new Color(0.05f, 0.04f, 0.08f, 0.97f), 28).rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _message = MenuTheme.Paragraph("Message", _panel, new Vector2(0.5f, 1f), new Vector2(0f, -56f),
                new Vector2(820f, 150f), 40, Color.white);
            _message.rectTransform.pivot = new Vector2(0.5f, 1f);
            _message.alignment = TextAnchor.MiddleCenter;
            _message.fontStyle = FontStyle.Bold;

            _confirm = MenuWidgets.ListButton("Confirm", _panel, 90f, 250f, 360f, 104f, "確定", "", null, 40);
            _cancel = MenuWidgets.ListButton("Cancel", _panel, 490f, 250f, 360f, 104f, "取消", "", null, 40);
            _confirm.navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit, selectOnRight = _cancel, selectOnLeft = _cancel,
            };
            _cancel.navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit, selectOnLeft = _confirm, selectOnRight = _confirm,
            };
            _confirm.Activated += () =>
            {
                Action action = _onConfirm;
                _onConfirm = null;
                Close();
                action?.Invoke();
            };
            _cancel.Activated += Close;
        }

        public void Show(MenuNavigator navigator, string message, string confirmLabel, Action onConfirm)
        {
            _owner = navigator;
            _message.text = message;
            _confirm.Label.text = string.IsNullOrEmpty(confirmLabel) ? "確定" : confirmLabel;
            _onConfirm = onConfirm;
            navigator.Push(this);
        }

        private void Close()
        {
            if (_owner != null && _owner.Current == this)
            {
                _owner.Pop();
            }
        }

        public override void OnShow()
        {
            _shownAt = Time.unscaledTime;
        }

        public override void Tick()
        {
            float pop = Ui.PopScale(Mathf.Clamp01((Time.unscaledTime - _shownAt) * 5f));
            _panel.localScale = Vector3.one * pop;
        }
    }
}

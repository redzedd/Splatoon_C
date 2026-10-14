using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SplatoonC.Gameplay.UI.Menu
{
    // 畫面底部的按鍵提示列(S3 右下角的按鈕說明):「按鍵」膠囊 + 說明文字,由右往左排。
    // 格式:「Enter / A:決定|Esc / B:返回」;只在換頁時重建(不是每幀)。
    public sealed class MenuHintBar
    {
        private readonly RectTransform _root;
        private readonly List<GameObject> _chips = new List<GameObject>();
        private string _current;

        public MenuHintBar(Transform parent)
        {
            _root = Ui.Rect("HintBar", parent, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, 66f));
            _root.anchorMin = new Vector2(0f, 0f);
            _root.anchorMax = new Vector2(1f, 0f);
            _root.pivot = new Vector2(0.5f, 0f);
            _root.sizeDelta = new Vector2(0f, 66f);
            var band = _root.gameObject.AddComponent<Image>();
            band.color = new Color(0f, 0f, 0f, 0.62f);
            band.raycastTarget = false;
        }

        public RectTransform Root => _root;

        public void Set(string hints)
        {
            if (hints == _current)
            {
                return;
            }
            _current = hints;
            foreach (GameObject chip in _chips)
            {
                Object.Destroy(chip);
            }
            _chips.Clear();
            if (string.IsNullOrEmpty(hints))
            {
                return;
            }
            string[] entries = hints.Split('|');
            float right = -40f;
            for (int i = entries.Length - 1; i >= 0; i--)
            {
                string entry = entries[i];
                int colon = entry.IndexOf(':');
                string key = colon >= 0 ? entry.Substring(0, colon) : entry;
                string label = colon >= 0 ? entry.Substring(colon + 1) : "";
                right = AddChip(key, label, right) - 36f;
            }
        }

        // 回傳這組提示的左緣 x
        private float AddChip(string key, string label, float right)
        {
            Text labelText = MenuTheme.Label("HintLabel", _root, new Vector2(1f, 0.5f), new Vector2(right, 0f),
                new Vector2(400f, 50f), 26, TextAnchor.MiddleRight, Color.white, 0f);
            labelText.text = label;
            // 字寬依畫布縮放(字形點陣大小)略有出入:框多留一點
            float labelWidth = labelText.preferredWidth + 12f;
            labelText.rectTransform.sizeDelta = new Vector2(labelWidth, 50f);
            _chips.Add(labelText.gameObject);

            float keyRight = right - labelWidth - 14f;
            Text keyText = MenuTheme.Label("HintKey", _root, new Vector2(1f, 0.5f), new Vector2(keyRight - 14f, 0f),
                new Vector2(300f, 40f), 22, TextAnchor.MiddleRight, MenuTheme.TextOnAccent, 0f);
            keyText.text = key;
            float keyWidth = keyText.preferredWidth + 10f;
            keyText.rectTransform.sizeDelta = new Vector2(keyWidth, 40f);
            Image cap = Ui.Image("HintCap", _root, new Vector2(1f, 0.5f), new Vector2(keyRight, 0f),
                new Vector2(keyWidth + 18f, 40f), UiSprites.RoundedRect(12), MenuTheme.Accent);
            cap.transform.SetSiblingIndex(keyText.transform.GetSiblingIndex());
            _chips.Add(cap.gameObject);
            _chips.Add(keyText.gameObject);
            return keyRight - keyWidth - 18f;
        }
    }
}

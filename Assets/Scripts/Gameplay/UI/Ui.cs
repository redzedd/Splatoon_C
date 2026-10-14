using SplatoonC.Gameplay.Scoring;
using UnityEngine;
using UnityEngine.UI;

namespace SplatoonC.Gameplay.UI
{
    // 程式碼建 uGUI 的小工具:錨點/尺寸一行設好,文字統一用 OS 繁中字型 + 粗描邊(Splatoon 的白字黑邊風格)。
    public static class Ui
    {
        public const string FontName = "Microsoft JhengHei";

        public static Canvas CreateCanvas(string name, int sortingOrder)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        // anchor:錨點(0~1),pivot 與錨點相同;pos:相對錨點的位移(參考解析度像素)
        public static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        // 撐滿父物件
        public static RectTransform Stretch(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        public static Image Image(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Sprite sprite,
            Color color)
        {
            RectTransform rt = Rect(name, parent, anchor, pos, size);
            var image = rt.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            if (sprite != null && sprite.border != Vector4.zero)
            {
                image.type = UnityEngine.UI.Image.Type.Sliced;
            }
            return image;
        }

        public static Text Text(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, int fontSize,
            TextAnchor alignment, Color color, bool bold = true, float outline = 2f)
        {
            RectTransform rt = Rect(name, parent, anchor, pos, size);
            var text = rt.gameObject.AddComponent<Text>();
            text.font = HudFont.Get(FontName, Mathf.Clamp(fontSize, 16, 128));
            text.fontSize = fontSize;
            text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            if (outline > 0f)
            {
                var o = rt.gameObject.AddComponent<Outline>();
                o.effectColor = new Color(0f, 0f, 0f, 0.85f);
                o.effectDistance = new Vector2(outline, -outline);
            }
            return text;
        }

        public static void SetAlpha(Graphic graphic, float alpha)
        {
            Color c = graphic.color;
            c.a = alpha;
            graphic.color = c;
        }

        // 彈跳縮放(標題字 pop-in):t 0→1
        public static float PopScale(float t)
        {
            if (t <= 0f)
            {
                return 0f;
            }
            if (t >= 1f)
            {
                return 1f;
            }
            // 過衝後回穩
            return 1f + Mathf.Sin(t * Mathf.PI * 1.5f) * (1f - t) * 0.35f - (1f - t) * (1f - t) * 0.6f;
        }

        public static Color WithAlpha(Color c, float a)
        {
            c.a = a;
            return c;
        }
    }
}

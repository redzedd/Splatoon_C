using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Scoring;
using UnityEngine;
using UnityEngine.UI;

namespace SplatoonC.Gameplay.UI.Menu
{
    // 主選單/暫停選單的視覺語言(S3 介面):近黑底板、螢光黃綠選取色、紫色副色、白色粗字黑描邊、
    // 微傾斜的黑色膠帶標題、墨漬底圖。字型:繁中用 OS 的 Microsoft JhengHei(與 HUD 相同),
    // 拉丁大標題用 OS 的粗黑體(Arial Black 等;S3 的 Splatfont 是任天堂字型,不可用)。
    public static class MenuTheme
    {
        // S3 大廳/選單的螢光黃綠(選取中的按鈕底色)
        public static readonly Color Accent = new Color(0.91f, 1f, 0.2f);
        // 紫(S3 主視覺的另一色)
        public static readonly Color Secondary = new Color(0.45f, 0.24f, 1f);
        public static readonly Color Background = new Color(0.07f, 0.05f, 0.11f);
        // 資訊底板不透明:背景墨漬是動的,透出來會干擾閱讀
        public static readonly Color Panel = new Color(0.035f, 0.03f, 0.055f, 1f);
        public static readonly Color PanelSoft = new Color(1f, 1f, 1f, 0.07f);
        public static readonly Color Item = new Color(0.1f, 0.09f, 0.15f, 0.97f);
        public static readonly Color ItemLocked = new Color(0.16f, 0.16f, 0.19f, 0.9f);
        public static readonly Color TextMain = Color.white;
        public static readonly Color TextDim = new Color(0.72f, 0.72f, 0.8f);
        public static readonly Color TextOnAccent = new Color(0.05f, 0.05f, 0.08f);
        public static readonly Color Warning = new Color(1f, 0.36f, 0.32f);

        public const string BodyFont = Ui.FontName;
        private static readonly string[] DisplayFonts = { "Arial Black", "Segoe UI Black", "Impact", Ui.FontName };
        private static Font _display;

        // 拉丁粗黑體(標誌、VS、數字);OS 沒有就退回正黑體
        public static Font Display
        {
            get
            {
                if (_display == null)
                {
                    _display = Font.CreateDynamicFontFromOSFont(DisplayFonts, 64);
                }
                return _display;
            }
        }

        public static Font Body => HudFont.Get(BodyFont, 32);

        // 文字(Ui.Text 的選單版):display = 拉丁粗黑體
        public static Text Label(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, int fontSize,
            TextAnchor alignment, Color color, float outline = 2f, bool display = false)
        {
            Text text = Ui.Text(name, parent, anchor, pos, size, fontSize, alignment, color, true, outline);
            if (display)
            {
                text.font = Display;
            }
            return text;
        }

        // 會換行的段落(說明文字)
        public static Text Paragraph(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, int fontSize,
            Color color)
        {
            Text text = Ui.Text(name, parent, anchor, pos, size, fontSize, TextAnchor.UpperLeft, color, false, 0f);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.lineSpacing = 1.1f;
            return text;
        }

        // 單行文字縮到放得進框(武器名稱長短不一:「斯普拉射擊槍 聯名」在小卡片上要縮字);只在內容改變時呼叫
        public static void Fit(Text text, int maxSize, int minSize)
        {
            text.fontSize = maxSize;
            // 留 8 的餘裕:字寬依畫布縮放(字形點陣大小)略有出入
            float width = text.rectTransform.rect.width - 8f;
            while (text.fontSize > minSize && text.preferredWidth > width)
            {
                text.fontSize -= 1;
            }
        }

        // S3 的傾斜黑膠帶標題(頁面標題、類別標籤):黑帶 + 白字,底下墊一塊主色墨漬
        public static RectTransform TapeTitle(string name, Transform parent, Vector2 anchor, Vector2 pos, string title,
            int fontSize, Color splatColor, float tiltDeg = -2.5f)
        {
            RectTransform root = Ui.Rect(name, parent, anchor, pos, new Vector2(title.Length * fontSize * 1.05f + 90f, fontSize * 1.7f));
            Image splat = Ui.Image("Splat", root, new Vector2(0f, 0.5f), new Vector2(-30f, 0f),
                new Vector2(fontSize * 3.2f, fontSize * 3.2f), UiSprites.Splat(256, 11), Ui.WithAlpha(splatColor, 0.95f));
            splat.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            splat.rectTransform.anchoredPosition = new Vector2(fontSize * 0.6f, 0f);
            Image band = Ui.Image("Band", root, new Vector2(0f, 0.5f), Vector2.zero, root.sizeDelta, null,
                new Color(0.02f, 0.02f, 0.03f, 0.96f));
            band.rectTransform.pivot = new Vector2(0f, 0.5f);
            Text text = Label("Text", band.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, 2f), root.sizeDelta, fontSize,
                TextAnchor.MiddleCenter, Color.white, 0f);
            text.text = title;
            // 帶子寬度跟著實際字寬(中英混排時估算不準)
            float width = text.preferredWidth + 70f;
            root.sizeDelta = new Vector2(width, root.sizeDelta.y);
            band.rectTransform.sizeDelta = root.sizeDelta;
            text.rectTransform.sizeDelta = root.sizeDelta;
            root.localRotation = Quaternion.Euler(0f, 0f, tiltDeg);
            return root;
        }

        // 圓角底板
        public static Image Panel9(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Color color,
            int radius = 22)
        {
            Image image = Ui.Image(name, parent, anchor, pos, size, UiSprites.RoundedRect(radius), color);
            return image;
        }

        // 能力值橫條(S3 武器資訊的三條):底條 + 填滿;回傳填滿的 Image,用 SetBar 設 0~1
        //(用錨點拉寬而不是 Filled:圓角九宮格在任何長度都保持圓頭)
        public static Image StatBar(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Color fill)
        {
            Image back = Ui.Image(name, parent, anchor, pos, size, UiSprites.RoundedRect(10), new Color(1f, 1f, 1f, 0.12f));
            Image bar = Ui.Image("Fill", back.transform, new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero,
                UiSprites.RoundedRect(8), fill);
            RectTransform rt = bar.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(3f, 0f);
            rt.sizeDelta = new Vector2(0f, -6f);
            SetBar(bar, 0f);
            return bar;
        }

        public static void SetBar(Image fill, float t)
        {
            t = Mathf.Clamp01(t);
            RectTransform rt = fill.rectTransform;
            var parent = (RectTransform)rt.parent;
            float inner = parent.rect.width - 6f;
            float height = parent.rect.height - 6f;
            fill.enabled = t > 0.005f;
            // 太短時圓頭會擠扁:至少一個圓的寬度
            rt.sizeDelta = new Vector2(Mathf.Max(height, inner * t), -6f);
        }

        public static string ClassName(WeaponClass weaponClass)
        {
            // S3 繁中版的武器種類名稱
            switch (weaponClass)
            {
                case WeaponClass.Shooter: return "射擊槍";
                case WeaponClass.Roller: return "滾筒";
                case WeaponClass.Charger: return "狙擊槍";
                case WeaponClass.Slosher: return "潑桶";
                case WeaponClass.Splatling: return "旋轉槍";
                case WeaponClass.Dualies: return "雙槍";
                case WeaponClass.Brella: return "傘";
                case WeaponClass.Blaster: return "爆破槍";
                case WeaponClass.Brush: return "筆刷";
                case WeaponClass.Stringer: return "弓";
                case WeaponClass.Splatana: return "刮水刀";
                default: return weaponClass.ToString();
            }
        }

        public static string ClassEnglish(WeaponClass weaponClass)
        {
            return weaponClass.ToString().ToUpperInvariant();
        }

        // 重量級別與實際跑速/游速倍率(WeightClassUtil 的 S3 值)
        public static string WeightName(WeightClass weight)
        {
            switch (weight)
            {
                case WeightClass.Light: return "輕量級";
                case WeightClass.Heavy: return "重量級";
                default: return "中量級";
            }
        }

        public static string WeightDetail(WeightClass weight)
        {
            return $"跑速 ×{WeightClassUtil.RunMultiplier(weight):0.00} · 游速 ×{WeightClassUtil.SwimMultiplier(weight):0.00}";
        }
    }
}

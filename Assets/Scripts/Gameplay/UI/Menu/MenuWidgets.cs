using UnityEngine;
using UnityEngine.UI;

namespace SplatoonC.Gameplay.UI.Menu
{
    // 選單元件工廠(程式碼建 uGUI):清單按鈕、卡片、選項列、條狀選項、提示列。
    // 所有位置用「父物件左上角為原點、y 往下」的座標(TopLeft),與 MenuGridLayout 一致,版面好算。
    public static class MenuWidgets
    {
        private static readonly Vector2 TopLeftAnchor = new Vector2(0f, 1f);

        // 父物件左上角為原點的矩形(y 往下為正)
        public static RectTransform TopLeft(string name, Transform parent, float x, float y, float width, float height)
        {
            return Ui.Rect(name, parent, TopLeftAnchor, new Vector2(x, -y), new Vector2(width, height));
        }

        public static void Place(RectTransform rt, float x, float y, float width, float height)
        {
            rt.anchorMin = TopLeftAnchor;
            rt.anchorMax = TopLeftAnchor;
            rt.pivot = TopLeftAnchor;
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(width, height);
        }

        private static MenuButton CreateButtonShell(string name, RectTransform rt, int radius)
        {
            var button = rt.gameObject.AddComponent<MenuButton>();
            RectTransform content = Ui.Stretch("Content", rt);
            content.pivot = new Vector2(0.5f, 0.5f);
            Image frame = Ui.Image("Frame", content, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero,
                UiSprites.RoundedRect(radius + 4), new Color(1f, 1f, 1f, 0f));
            Stretch(frame.rectTransform, -5f);
            Image background = Ui.Image("Background", content, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero,
                UiSprites.RoundedRect(radius), MenuTheme.Item);
            Stretch(background.rectTransform, 0f);
            background.raycastTarget = true;
            button.targetGraphic = background;
            button.Background = background;
            button.Frame = frame;
            button.Content = content;
            return button;
        }

        private static void Stretch(RectTransform rt, float inset)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(-inset, -inset);
            rt.offsetMax = new Vector2(inset, inset);
        }

        // 橫向清單按鈕:左邊圖示、主標題 + 副標題;選取時左側出現烏賊游標
        public static MenuButton ListButton(string name, Transform parent, float x, float y, float width, float height,
            string title, string detail, Sprite icon, int titleSize = 44)
        {
            RectTransform rt = TopLeft(name, parent, x, y, width, height);
            MenuButton button = CreateButtonShell(name, rt, 20);
            float textLeft = 34f;
            if (icon != null)
            {
                float iconSize = height * 0.66f;
                Image image = Ui.Image("Icon", button.Content, new Vector2(0f, 0.5f), new Vector2(26f, 0f),
                    new Vector2(iconSize, iconSize), icon, Color.white);
                button.Icon = image;
                textLeft = 26f + iconSize + 22f;
            }
            bool hasDetail = !string.IsNullOrEmpty(detail);
            Text label = MenuTheme.Label("Label", button.Content, new Vector2(0f, 0.5f),
                new Vector2(textLeft, hasDetail ? height * 0.14f : 0f), new Vector2(width - textLeft - 20f, titleSize * 1.3f),
                titleSize, TextAnchor.MiddleLeft, Color.white, 0f);
            label.text = title;
            MenuTheme.Fit(label, titleSize, titleSize * 3 / 5);
            button.Label = label;
            if (hasDetail)
            {
                Text sub = MenuTheme.Label("Detail", button.Content, new Vector2(0f, 0.5f),
                    new Vector2(textLeft + 2f, -height * 0.24f), new Vector2(width - textLeft - 20f, 32f), 22,
                    TextAnchor.MiddleLeft, MenuTheme.TextDim, 0f);
                sub.fontStyle = FontStyle.Normal;
                sub.text = detail;
                button.Detail = sub;
            }
            Image cursor = Ui.Image("Cursor", rt, new Vector2(0f, 0.5f), new Vector2(-58f, 0f), new Vector2(54f, 54f),
                UiSprites.SquidHead(), new Color(1f, 1f, 1f, 0f));
            cursor.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            cursor.rectTransform.anchoredPosition = new Vector2(-34f, 0f);
            button.CursorIcon = cursor;
            return button;
        }

        // 直向卡片(武器、場地、模式):上方大圖示、下方名稱;tag = 左上角小標籤
        public static MenuButton Card(string name, Transform parent, float x, float y, float width, float height,
            string title, string tag, Sprite icon, int titleSize)
        {
            RectTransform rt = TopLeft(name, parent, x, y, width, height);
            MenuButton button = CreateButtonShell(name, rt, 18);
            float titleBand = titleSize * 1.6f;
            if (icon != null)
            {
                float iconSize = Mathf.Min(width * 0.7f, (height - titleBand) * 0.78f);
                Image image = Ui.Image("Icon", button.Content, new Vector2(0.5f, 1f),
                    new Vector2(0f, -((height - titleBand) - iconSize) * 0.5f - 6f), new Vector2(iconSize, iconSize), icon,
                    Color.white);
                button.Icon = image;
            }
            Text label = MenuTheme.Label("Label", button.Content, new Vector2(0.5f, 0f), new Vector2(0f, titleBand * 0.18f),
                new Vector2(width - 16f, titleBand * 0.7f), titleSize, TextAnchor.MiddleCenter, Color.white, 0f);
            label.text = title;
            MenuTheme.Fit(label, titleSize, Mathf.Max(14, titleSize * 3 / 5));
            button.Label = label;
            if (!string.IsNullOrEmpty(tag))
            {
                Text sub = MenuTheme.Label("Tag", button.Content, new Vector2(0f, 1f), new Vector2(14f, -8f),
                    new Vector2(width - 28f, 28f), Mathf.Clamp(titleSize - 8, 16, 22), TextAnchor.UpperLeft,
                    MenuTheme.TextDim, 0f);
                sub.text = tag;
                button.Detail = sub;
            }
            return button;
        }

        // 選項列(開關、難度、人數):名稱 + 「◀ 值 ▶」
        public static MenuStepper Option(string name, Transform parent, float x, float y, float width, float height,
            string label, float valueWidth = 300f)
        {
            RectTransform rt = TopLeft(name, parent, x, y, width, height);
            MenuStepper stepper = CreateStepperShell(rt, label, height);
            BuildArrows(stepper, rt, height, valueWidth);
            return stepper;
        }

        // 條狀選項(音量、靈敏度):名稱 + 刻度條(可拖曳)+ 數值
        public static MenuStepper BarOption(string name, Transform parent, float x, float y, float width, float height,
            string label, float barWidth = 380f)
        {
            RectTransform rt = TopLeft(name, parent, x, y, width, height);
            MenuStepper stepper = CreateStepperShell(rt, label, height);
            float arrow = BuildArrows(stepper, rt, height, 150f);
            float barRight = 22f + arrow * 2f + 150f + 28f;
            RectTransform bar = Ui.Rect("Bar", rt, new Vector2(1f, 0.5f), new Vector2(-barRight, 0f), new Vector2(barWidth, 14f));
            Image back = Ui.Image("BarBack", bar, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero,
                UiSprites.RoundedRect(7), new Color(0f, 0f, 0f, 0.45f));
            Stretch(back.rectTransform, 0f);
            back.raycastTarget = true;
            Image fill = Ui.Image("BarFill", bar, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero,
                UiSprites.RoundedRect(7), MenuTheme.Accent);
            Stretch(fill.rectTransform, 0f);
            // 寬度由錨點決定(anchorMax.x = 比例),圓頭九宮格不變形
            Image knob = Ui.Image("Knob", bar, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(30f, 30f), UiSprites.Circle(64),
                Color.white);
            knob.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            stepper.Bar = bar;
            stepper.BarFill = fill;
            stepper.BarKnob = knob;
            return stepper;
        }

        private static MenuStepper CreateStepperShell(RectTransform rt, string label, float height)
        {
            var stepper = rt.gameObject.AddComponent<MenuStepper>();
            Image frame = Ui.Image("Frame", rt, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero,
                UiSprites.RoundedRect(20), new Color(1f, 1f, 1f, 0f));
            Stretch(frame.rectTransform, -4f);
            Image background = Ui.Image("Background", rt, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero,
                UiSprites.RoundedRect(16), MenuTheme.Item);
            Stretch(background.rectTransform, 0f);
            background.raycastTarget = true;
            stepper.targetGraphic = background;
            stepper.Background = background;
            stepper.Frame = frame;
            Text text = MenuTheme.Label("Label", rt, new Vector2(0f, 0.5f), new Vector2(30f, 0f), new Vector2(360f, height),
                Mathf.RoundToInt(Mathf.Min(34f, height * 0.44f)), TextAnchor.MiddleLeft, Color.white, 0f);
            text.text = label;
            stepper.Label = text;
            return stepper;
        }

        // 回傳箭頭邊長
        private static float BuildArrows(MenuStepper stepper, RectTransform rt, float height, float valueWidth)
        {
            float arrow = Mathf.Min(34f, height * 0.42f);
            Image right = Ui.Image("Right", rt, new Vector2(1f, 0.5f), Vector2.zero, new Vector2(arrow, arrow),
                MenuSprites.Chevron(), Color.white);
            right.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            right.rectTransform.anchoredPosition = new Vector2(-22f - arrow * 0.5f, 0f);
            Image left = Ui.Image("Left", rt, new Vector2(1f, 0.5f), Vector2.zero, new Vector2(arrow, arrow),
                MenuSprites.Chevron(), Color.white);
            left.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            left.rectTransform.anchoredPosition = new Vector2(-22f - arrow - valueWidth - arrow * 0.5f, 0f);
            // 左箭頭 = 右箭頭水平翻轉(以中心翻,位置不變)
            left.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
            Text value = MenuTheme.Label("Value", rt, new Vector2(1f, 0.5f), new Vector2(-22f - arrow, 0f),
                new Vector2(valueWidth, height), Mathf.RoundToInt(Mathf.Min(34f, height * 0.44f)), TextAnchor.MiddleCenter,
                Color.white, 0f);
            value.rectTransform.pivot = new Vector2(1f, 0.5f);
            stepper.LeftArrow = left;
            stepper.RightArrow = right;
            stepper.ValueText = value;
            return arrow;
        }

        // 直向串接導覽(上下),左右交給元件自己(選項列)或不動
        public static void LinkVertical(Selectable[] items, bool wrap)
        {
            for (int i = 0; i < items.Length; i++)
            {
                Navigation nav = new Navigation { mode = Navigation.Mode.Explicit };
                int up = i - 1;
                int down = i + 1;
                if (wrap)
                {
                    up = (up + items.Length) % items.Length;
                    down %= items.Length;
                }
                nav.selectOnUp = up >= 0 && up < items.Length && up != i ? items[up] : null;
                nav.selectOnDown = down >= 0 && down < items.Length && down != i ? items[down] : null;
                items[i].navigation = nav;
            }
        }
    }
}

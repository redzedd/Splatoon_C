using SplatoonC.Gameplay.Combat;
using UnityEngine;
using UnityEngine.UI;

namespace SplatoonC.Gameplay.UI.Menu
{
    // 主選單:對戰 / 訓練場 / 設定 / 離開(左側清單),右側展示目前武器與說明。
    public sealed class HomePage : MenuPage
    {
        private readonly MainMenuController _menu;
        private MenuButton _battle;
        private MenuButton _training;
        private MenuButton _settings;
        private MenuButton _quit;
        private Text _info;
        private Text _kitName;
        private Text _kitDetail;
        private Image _kitIcon;
        private RectTransform _showcase;

        public HomePage(MainMenuController menu)
        {
            _menu = menu;
        }

        public MenuButton BattleButton => _battle;
        public MenuButton TrainingButton => _training;
        public MenuButton SettingsButton => _settings;
        public MenuButton QuitButton => _quit;

        public override Selectable DefaultSelection => _battle;

        protected override void Build(RectTransform root)
        {
            MenuTheme.TapeTitle("Title", root, new Vector2(0f, 1f), new Vector2(90f, -60f), "主選單", 56, MenuTheme.Accent);

            const float x = 150f;
            const float w = 640f;
            const float h = 124f;
            const float gap = 26f;
            float y = 250f;
            _battle = MenuWidgets.ListButton("Battle", root, x, y, w, h, "對戰", "塗地對戰 ・ 與電腦 4 對 4",
                UiSprites.Splat(256, 3));
            y += h + gap;
            _training = MenuWidgets.ListButton("Training", root, x, y, w, h, "訓練場", "試射場:自由練習武器與移動",
                MenuSprites.WeaponClassIcon(WeaponClass.Shooter));
            y += h + gap;
            _settings = MenuWidgets.ListButton("Settings", root, x, y, w, h, "設定", "音量、視角靈敏度、色彩鎖定",
                MenuSprites.Star("gear", 128, 8, 0.46f, 0.34f));
            y += h + gap;
            _quit = MenuWidgets.ListButton("Quit", root, x, y, w, h, "離開", "結束遊戲", MenuSprites.Chevron());
            MenuWidgets.LinkVertical(new Selectable[] { _battle, _training, _settings, _quit }, true);

            _battle.Activated += () => _menu.Navigator.Push(_menu.Modes);
            _training.Activated += () =>
            {
                _menu.TrainingFlow = true;
                _menu.Navigator.Push(_menu.Weapons);
            };
            _settings.Activated += () => _menu.Navigator.Push(_menu.Settings);
            _quit.Activated += () => _menu.Confirm.Show(_menu.Navigator, "要結束遊戲嗎?", "結束", _menu.QuitGame);

            // 中文段落不放半形空白:舊版 uGUI 只在空白處換行,中文靠逐字斷行
            _battle.Focused += () => SetInfo("和7個電腦一起打3分鐘的塗地對戰。選好模式、場地與武器就能出發!");
            _training.Focused += () => SetInfo("在試射場自由練習:假人靶、攀爬牆、斜坡,墨水與大招都能盡情試。");
            _settings.Focused += () => SetInfo("調整主音量/音樂/音效、視角靈敏度(-5~+5)與反轉、色彩鎖定。");
            _quit.Focused += () => SetInfo("結束遊戲。");

            BuildShowcase(root);
            _info = MenuTheme.Paragraph("Info", root, new Vector2(0f, 0f), new Vector2(150f, 100f), new Vector2(880f, 90f), 26,
                MenuTheme.TextDim);
        }

        private void BuildShowcase(RectTransform root)
        {
            // 右側:目前武器卡(傾斜的黑板 + 主色墨漬 + 大剪影)
            _showcase = Ui.Rect("Showcase", root, new Vector2(1f, 0.5f), new Vector2(-150f, 10f), new Vector2(720f, 560f));
            _showcase.localRotation = Quaternion.Euler(0f, 0f, 3f);
            Image splat = Ui.Image("Splat", _showcase, new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(760f, 760f),
                UiSprites.Splat(256, 17), Ui.WithAlpha(MenuTheme.Accent, 0.92f));
            splat.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            MenuTheme.Panel9("Board", _showcase, new Vector2(0.5f, 0f), new Vector2(0f, 0f), new Vector2(620f, 190f),
                MenuTheme.Panel, 24);
            _kitIcon = Ui.Image("KitIcon", _showcase, new Vector2(0.5f, 0.5f), new Vector2(0f, 90f), new Vector2(340f, 340f),
                MenuSprites.WeaponClassIcon(WeaponClass.Shooter), MenuTheme.TextOnAccent);
            _kitIcon.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            Text caption = MenuTheme.Label("Caption", _showcase, new Vector2(0.5f, 0f), new Vector2(0f, 140f),
                new Vector2(600f, 36f), 24, TextAnchor.MiddleCenter, MenuTheme.Accent, 0f);
            caption.text = "目前武器";
            _kitName = MenuTheme.Label("KitName", _showcase, new Vector2(0.5f, 0f), new Vector2(0f, 76f), new Vector2(600f, 64f),
                46, TextAnchor.MiddleCenter, Color.white, 0f);
            _kitDetail = MenuTheme.Label("KitDetail", _showcase, new Vector2(0.5f, 0f), new Vector2(0f, 30f),
                new Vector2(600f, 40f), 24, TextAnchor.MiddleCenter, MenuTheme.TextDim, 0f);
        }

        public override void OnShow()
        {
            _menu.TrainingFlow = false;
            WeaponKit kit = _menu.SelectedKit;
            if (kit != null)
            {
                _kitName.text = kit.DisplayName;
                MenuTheme.Fit(_kitName, 46, 28);
                _kitDetail.text = $"{(kit.Sub != null ? kit.Sub.DisplayName : "-")} ・ {(kit.Special != null ? kit.Special.SpecialName : "-")}";
                _kitIcon.sprite = MenuSprites.WeaponClassIcon(kit.Main != null ? kit.Main.Class : WeaponClass.Shooter);
            }
            else
            {
                _kitName.text = "(沒有武器)";
                _kitDetail.text = "";
            }
        }

        public override void Tick()
        {
            float t = Time.unscaledTime;
            _showcase.localRotation = Quaternion.Euler(0f, 0f, 3f + 1.2f * Mathf.Sin(t * 1.3f));
            _kitIcon.rectTransform.anchoredPosition = new Vector2(0f, 90f + 8f * Mathf.Sin(t * 2.1f));
        }

        private void SetInfo(string text)
        {
            _info.text = text;
        }
    }
}

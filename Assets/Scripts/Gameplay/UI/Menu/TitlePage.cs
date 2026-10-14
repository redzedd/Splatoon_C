using SplatoonC.Gameplay.Audio;
using UnityEngine;
using UnityEngine.UI;

namespace SplatoonC.Gameplay.UI.Menu
{
    // 標題畫面:「SPLATOON」大字 + 墨漬裡的「C」(仿 S3 標誌的塗鴉「3」)、副標膠帶,「按任意鍵開始」閃爍。
    public sealed class TitlePage : MenuPage
    {
        // 剛顯示的一小段時間不收「任意鍵」:從主選單按 Esc 回來的那一下不會又直接進去
        private const float InputDelay = 0.3f;

        private readonly MainMenuController _menu;
        private MenuButton _start;
        private Text _prompt;
        private RectTransform _logo;
        private RectTransform _letterC;
        private float _shownAt;

        public TitlePage(MainMenuController menu)
        {
            _menu = menu;
        }

        public override string Hints => "任意鍵:開始";
        public override Selectable DefaultSelection => _start;

        protected override void Build(RectTransform root)
        {
            // 整頁透明按鈕:滑鼠點任何地方、Enter / A 都算「開始」
            RectTransform hit = Ui.Stretch("PressStart", root);
            _start = hit.gameObject.AddComponent<MenuButton>();
            Image hitImage = hit.gameObject.AddComponent<Image>();
            hitImage.color = new Color(0f, 0f, 0f, 0f);
            _start.targetGraphic = hitImage;
            _start.NormalColor = new Color(0f, 0f, 0f, 0f);
            _start.SelectedColor = new Color(0f, 0f, 0f, 0f);
            _start.Background = hitImage;
            _start.navigation = new Navigation { mode = Navigation.Mode.None };
            _start.Activated += Proceed;

            _logo = Ui.Rect("Logo", root, new Vector2(0.5f, 0.6f), Vector2.zero, new Vector2(1500f, 460f));
            _logo.pivot = new Vector2(0.5f, 0.5f);
            // 背後的大墨漬
            Image splatA = Ui.Image("SplatA", _logo, new Vector2(0.5f, 0.5f), new Vector2(-120f, 10f), new Vector2(900f, 900f),
                UiSprites.Splat(256, 31), Ui.WithAlpha(MenuTheme.Secondary, 0.9f));
            splatA.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            splatA.rectTransform.localScale = new Vector3(1.35f, 0.62f, 1f);
            Text word = MenuTheme.Label("Splatoon", _logo, new Vector2(0.5f, 0.5f), new Vector2(-170f, 20f),
                new Vector2(1200f, 240f), 176, TextAnchor.MiddleCenter, Color.white, 7f, true);
            word.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            word.text = "SPLATOON";
            word.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 4f);
            var shadow = word.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
            shadow.effectDistance = new Vector2(10f, -12f);

            // 「C」:黃綠墨漬 + 黑描邊白字,斜放在右側(S3 標誌右下的塗鴉 3)
            _letterC = Ui.Rect("C", _logo, new Vector2(0.5f, 0.5f), new Vector2(560f, -40f), new Vector2(400f, 400f));
            _letterC.pivot = new Vector2(0.5f, 0.5f);
            Image splatC = Ui.Image("SplatC", _letterC, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(470f, 470f),
                UiSprites.Splat(256, 42), MenuTheme.Accent);
            splatC.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            Text c = MenuTheme.Label("Letter", _letterC, new Vector2(0.5f, 0.5f), new Vector2(0f, 6f), new Vector2(400f, 400f),
                270, TextAnchor.MiddleCenter, MenuTheme.TextOnAccent, 0f, true);
            c.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            c.text = "C";
            var cOutline = c.gameObject.AddComponent<Outline>();
            cOutline.effectColor = Color.white;
            cOutline.effectDistance = new Vector2(5f, -5f);
            _letterC.localRotation = Quaternion.Euler(0f, 0f, -12f);

            RectTransform tape = MenuTheme.TapeTitle("Tagline", _logo, new Vector2(0.5f, 0.5f), new Vector2(-470f, -170f),
                "塗地對戰 ・ 4 對 4 vs 電腦", 40, MenuTheme.Accent, -3f);
            tape.pivot = new Vector2(0.5f, 0.5f);

            _prompt = MenuTheme.Label("Prompt", root, new Vector2(0.5f, 0.2f), Vector2.zero, new Vector2(900f, 80f), 48,
                TextAnchor.MiddleCenter, Color.white, 3f);
            _prompt.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _prompt.text = "按任意鍵開始";

            Text credit = MenuTheme.Label("Credit", root, new Vector2(0f, 0f), new Vector2(40f, 84f), new Vector2(900f, 36f), 22,
                TextAnchor.MiddleLeft, MenuTheme.TextDim, 0f);
            credit.fontStyle = FontStyle.Normal;
            credit.text = "Splatoon 3 同人重製(非官方) ・ 單機 vs 電腦";
        }

        public override void OnShow()
        {
            _shownAt = Time.unscaledTime;
        }

        // 標題是堆疊底:返回鍵不做事
        public override bool OnBack()
        {
            return true;
        }

        public override void Tick()
        {
            float t = Time.unscaledTime;
            float since = t - _shownAt;
            // 標誌:進場彈跳 + 呼吸
            float pop = Ui.PopScale(Mathf.Clamp01(since * 2.2f));
            float breathe = 1f + 0.012f * Mathf.Sin(t * 2.4f);
            _logo.localScale = Vector3.one * (pop * breathe);
            _letterC.localRotation = Quaternion.Euler(0f, 0f, -12f + 3f * Mathf.Sin(t * 1.7f));
            Ui.SetAlpha(_prompt, 0.35f + 0.65f * (0.5f + 0.5f * Mathf.Cos(t * 4f)));

            if (since > InputDelay && MenuInput.AnyKeyPressed())
            {
                GameAudio.Play2D("ui_select");
                Proceed();
            }
        }

        public void Proceed()
        {
            if (Navigator.Current != this || Time.unscaledTime - _shownAt < InputDelay)
            {
                return;
            }
            Navigator.Push(_menu.Home);
        }
    }
}

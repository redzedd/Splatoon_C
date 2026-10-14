using SplatoonC.Core;
using SplatoonC.Core.Match;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Match;
using SplatoonC.Gameplay.Painting;
using UnityEngine;
using UnityEngine.UI;

namespace SplatoonC.Gameplay.UI.Menu
{
    // 暫停選單首頁:繼續 / 設定 / 回到主選單,右側顯示本場資訊(模式、場地、武器、剩餘時間)。
    public sealed class PauseRootPage : MenuPage
    {
        private readonly PauseMenu _pause;
        private MenuButton _resume;
        private MenuButton _settings;
        private MenuButton _quit;
        private Text _where;
        private Text _kitName;
        private Text _kitDetail;
        private Image _kitIcon;
        private Text _time;
        private Image _allyChip;
        private Image _enemyChip;

        public PauseRootPage(PauseMenu pause)
        {
            _pause = pause;
        }

        public MenuButton ResumeButton => _resume;
        public MenuButton SettingsButton => _settings;
        public MenuButton QuitButton => _quit;

        public override string Hints => "Enter / A:決定|Esc / Start:繼續遊戲";
        public override Selectable DefaultSelection => _resume;

        protected override void Build(RectTransform root)
        {
            Image shade = Ui.Image("Shade", root, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, null,
                new Color(0.02f, 0.01f, 0.05f, 0.6f));
            shade.rectTransform.anchorMin = Vector2.zero;
            shade.rectTransform.anchorMax = Vector2.one;
            shade.rectTransform.sizeDelta = Vector2.zero;
            shade.raycastTarget = true;

            Image side = Ui.Image("Side", root, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(780f, 1400f), null,
                new Color(0.03f, 0.02f, 0.06f, 0.96f));
            side.rectTransform.anchorMin = new Vector2(0f, 0f);
            side.rectTransform.anchorMax = new Vector2(0f, 1f);
            side.rectTransform.sizeDelta = new Vector2(780f, 0f);
            Image edge = Ui.Image("Edge", side.transform, new Vector2(1f, 0.5f), Vector2.zero, new Vector2(10f, 0f), null,
                MenuTheme.Accent);
            edge.rectTransform.anchorMin = new Vector2(1f, 0f);
            edge.rectTransform.anchorMax = new Vector2(1f, 1f);
            edge.rectTransform.sizeDelta = new Vector2(10f, 0f);

            MenuTheme.TapeTitle("Title", root, new Vector2(0f, 1f), new Vector2(90f, -70f), "暫停中", 60, MenuTheme.Accent);

            const float x = 130f;
            const float w = 560f;
            const float h = 118f;
            _resume = MenuWidgets.ListButton("Resume", root, x, 280f, w, h, "繼續", "回到遊戲", MenuSprites.Chevron());
            _settings = MenuWidgets.ListButton("Settings", root, x, 280f + h + 26f, w, h, "設定", "音量、視角靈敏度",
                MenuSprites.Star("gear", 128, 8, 0.46f, 0.34f));
            _quit = MenuWidgets.ListButton("Quit", root, x, 280f + 2f * (h + 26f), w, h, "回到主選單",
                _pause.InMatch ? "放棄這場對戰" : "離開訓練場", UiSprites.SquidHead());
            MenuWidgets.LinkVertical(new Selectable[] { _resume, _settings, _quit }, true);
            _resume.Activated += _pause.Resume;
            _settings.Activated += () => Navigator.Push(_pause.Settings);
            _quit.Activated += _pause.RequestReturnToMenu;

            Text note = MenuTheme.Paragraph("Note", root, new Vector2(0f, 0f), new Vector2(130f, 100f), new Vector2(580f, 90f), 22,
                MenuTheme.TextDim);
            note.text = "離線模式限定:暫停時計時、電腦與所有動作都會停止。";

            BuildInfo(root);
        }

        private void BuildInfo(RectTransform root)
        {
            RectTransform card = Ui.Rect("Info", root, new Vector2(1f, 0.5f), new Vector2(-140f, 30f), new Vector2(700f, 520f));
            card.localRotation = Quaternion.Euler(0f, 0f, -2f);
            // 右上角探出來的墨漬(不要壓到文字)
            Image splat = Ui.Image("Splat", card, new Vector2(1f, 1f), new Vector2(-30f, -20f), new Vector2(300f, 300f),
                UiSprites.Splat(256, 71), Ui.WithAlpha(MenuTheme.Accent, 0.95f));
            splat.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            MenuTheme.Panel9("Back", card, new Vector2(0f, 1f), new Vector2(0f, -40f), new Vector2(700f, 480f), MenuTheme.Panel, 26);
            _where = MenuTheme.Label("Where", card, new Vector2(0f, 1f), new Vector2(36f, -70f), new Vector2(640f, 56f), 40,
                TextAnchor.MiddleLeft, Color.white, 2f);
            _time = MenuTheme.Label("Time", card, new Vector2(0f, 1f), new Vector2(38f, -130f), new Vector2(640f, 40f), 28,
                TextAnchor.MiddleLeft, MenuTheme.Accent, 0f);
            _kitIcon = Ui.Image("KitIcon", card, new Vector2(0f, 1f), new Vector2(36f, -200f), new Vector2(180f, 180f),
                MenuSprites.WeaponClassIcon(WeaponClass.Shooter), Color.white);
            _kitName = MenuTheme.Label("KitName", card, new Vector2(0f, 1f), new Vector2(240f, -230f), new Vector2(440f, 56f), 40,
                TextAnchor.MiddleLeft, Color.white, 0f);
            _kitDetail = MenuTheme.Paragraph("KitDetail", card, new Vector2(0f, 1f), new Vector2(242f, -292f),
                new Vector2(430f, 90f), 24, MenuTheme.TextDim);
            Text colors = MenuTheme.Label("ColorsCaption", card, new Vector2(0f, 1f), new Vector2(38f, -420f),
                new Vector2(200f, 40f), 24, TextAnchor.MiddleLeft, MenuTheme.TextDim, 0f);
            colors.text = "隊伍顏色";
            _allyChip = Ui.Image("Ally", card, new Vector2(0f, 1f), new Vector2(200f, -420f), new Vector2(120f, 40f),
                UiSprites.RoundedRect(12), Color.white);
            _enemyChip = Ui.Image("Enemy", card, new Vector2(0f, 1f), new Vector2(336f, -420f), new Vector2(120f, 40f),
                UiSprites.RoundedRect(12), Color.white);
        }

        public override void OnShow()
        {
            MatchController match = MatchController.Instance;
            _where.text = match != null ? $"塗地對戰 ・ {_pause.StageName}" : "訓練場";
            if (match != null && match.Timeline != null)
            {
                MatchTimeline timeline = match.Timeline;
                float remaining = timeline.Phase == MatchPhase.Battle ? timeline.BattleRemaining
                    : timeline.Phase < MatchPhase.Battle ? timeline.Settings.BattleSeconds : 0f;
                MatchTimeline.SplitClock(remaining, out int minutes, out int seconds);
                _time.text = $"剩餘時間 {minutes}:{seconds:00} ・ {match.TeamSize} 對 {match.TeamSize}";
            }
            else
            {
                _time.text = "自由練習";
            }
            WeaponKit kit = _pause.LocalKit;
            if (kit != null)
            {
                _kitName.text = kit.DisplayName;
                MenuTheme.Fit(_kitName, 40, 24);
                _kitDetail.text = $"副武器 {(kit.Sub != null ? kit.Sub.DisplayName : "-")}\n大招 {(kit.Special != null ? kit.Special.SpecialName : "-")}({Mathf.RoundToInt(kit.SpecialPoints)}p)";
                _kitIcon.sprite = MenuSprites.WeaponClassIcon(kit.Main != null ? kit.Main.Class : WeaponClass.Shooter);
            }
            else
            {
                _kitName.text = "-";
                _kitDetail.text = "";
            }
            Team ally = _pause.LocalTeam;
            _allyChip.color = TeamPalette.ColorOf(ally);
            _enemyChip.color = TeamPalette.ColorOf(TeamUtil.Opponent(ally));
        }

        // 首頁返回 = 繼續遊戲
        public override bool OnBack()
        {
            _pause.Resume();
            return true;
        }
    }
}

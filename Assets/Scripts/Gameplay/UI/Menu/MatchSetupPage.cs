using System.Collections.Generic;
using SplatoonC.Core.Match;
using SplatoonC.Core.Match.Ranked;
using SplatoonC.Gameplay.AI;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Match;
using UnityEngine;
using UnityEngine.UI;

namespace SplatoonC.Gameplay.UI.Menu
{
    // 對戰準備:左側摘要(模式 / 場地 / 武器,點了回去改)、右側規則(電腦難度、隊伍人數)與隊伍預覽,
    // 最後「開始對戰!」。對戰結束回主選單時直接落在這頁(預設選取開始鈕,一鍵再來一場)。
    public sealed class MatchSetupPage : MenuPage
    {
        private static readonly string[] DifficultyNames = { "簡單", "普通", "困難" };

        private static readonly string[] DifficultyNotes =
        {
            "反應慢(0.65秒)、瞄準誤差大,不太跳躍——適合熟悉操作。",
            "反應0.35秒、瞄準誤差中等,會跳躍閃避與撤退回墨。",
            "反應0.18秒、轉身快、瞄得準——認真對打。",
        };

        private readonly MainMenuController _menu;
        private MenuButton _modeCard;
        private MenuButton _stageCard;
        private MenuButton _weaponCard;
        private MenuStepper _difficulty;
        private MenuStepper _teamSize;
        private MenuButton _start;
        private Text _difficultyNote;
        private RectTransform _preview;
        private readonly List<Image> _allyIcons = new List<Image>();
        private readonly List<Image> _enemyIcons = new List<Image>();

        public MatchSetupPage(MainMenuController menu)
        {
            _menu = menu;
        }

        public MenuStepper DifficultyStepper => _difficulty;
        public MenuStepper TeamSizeStepper => _teamSize;
        public MenuButton StartButton => _start;
        public MenuButton WeaponCard => _weaponCard;

        public override Selectable DefaultSelection => _start;

        protected override void Build(RectTransform root)
        {
            MenuTheme.TapeTitle("Title", root, new Vector2(0f, 1f), new Vector2(90f, -60f), "對戰準備", 56, MenuTheme.Accent);

            const float leftX = 130f;
            const float leftW = 740f;
            _modeCard = MenuWidgets.ListButton("Mode", root, leftX, 240f, leftW, 138f, "塗地對戰", "模式", UiSprites.Splat(256, 9),
                46);
            _stageCard = MenuWidgets.ListButton("Stage", root, leftX, 402f, leftW, 138f, "-", "場地", MenuSprites.ZoneIcon(), 46);
            _weaponCard = MenuWidgets.ListButton("Weapon", root, leftX, 564f, leftW, 138f, "-", "武器",
                MenuSprites.WeaponClassIcon(WeaponClass.Shooter), 46);
            _modeCard.Activated += () => _menu.Navigator.JumpTo(_menu.Modes, _menu.Home, _menu.Modes);
            _stageCard.Activated += () => _menu.Navigator.JumpTo(_menu.Stages, _menu.Home, _menu.Modes, _menu.Stages);
            _weaponCard.Activated += () =>
                _menu.Navigator.JumpTo(_menu.Weapons, _menu.Home, _menu.Modes, _menu.Stages, _menu.Weapons);
            Text change = MenuTheme.Label("ChangeHint", root, new Vector2(0f, 1f), new Vector2(leftX, -722f),
                new Vector2(leftW, 36f), 22, TextAnchor.MiddleLeft, MenuTheme.TextDim, 0f);
            change.fontStyle = FontStyle.Normal;
            change.text = "選擇上面的項目可以回去更改";

            // 右側:規則
            const float rightX = 950f;
            const float rightW = 840f;
            Image panel = MenuTheme.Panel9("RulesPanel", root, new Vector2(0f, 1f), new Vector2(rightX, -210f),
                new Vector2(rightW, 560f), MenuTheme.Panel, 26);
            Text heading = MenuTheme.Label("Heading", panel.transform, new Vector2(0f, 1f), new Vector2(34f, -22f),
                new Vector2(500f, 50f), 32, TextAnchor.MiddleLeft, MenuTheme.Accent, 0f);
            heading.text = "對戰規則";
            _difficulty = MenuWidgets.Option("Difficulty", panel.transform, 30f, 88f, rightW - 60f, 86f, "電腦難度", 220f);
            _difficulty.Setup(DifficultyNames.Length, (int)_menu.Difficulty, i => DifficultyNames[i]);
            _difficulty.Changed += i =>
            {
                _menu.Difficulty = (BotBrain.Difficulty)i;
                _difficultyNote.text = DifficultyNotes[i];
            };
            _difficultyNote = MenuTheme.Paragraph("DifficultyNote", panel.transform, new Vector2(0f, 1f), new Vector2(40f, -182f),
                new Vector2(rightW - 80f, 40f), 22, MenuTheme.TextDim);
            _teamSize = MenuWidgets.Option("TeamSize", panel.transform, 30f, 236f, rightW - 60f, 86f, "隊伍人數", 220f);
            _teamSize.Setup(MatchSettings.MaxTeamSize, _menu.TeamSize - 1, i => $"{i + 1} 對 {i + 1}");
            _teamSize.Changed += i =>
            {
                _menu.TeamSize = i + 1;
                RefreshPreview();
            };
            BuildPreview(panel.transform, rightW);

            _start = MenuWidgets.ListButton("Start", root, rightX, 800f, rightW, 130f, "開始對戰!", "", MenuSprites.Chevron(), 56);
            _start.NormalColor = MenuTheme.Secondary;
            _start.Activated += () => _menu.StartBattle();

            // 導覽:左欄上下;右欄 難度 ↕ 人數 ↕ 開始;開始 ← 武器摘要、摘要 → 難度
            SetNav(_modeCard, null, _stageCard, null, _difficulty);
            SetNav(_stageCard, _modeCard, _weaponCard, null, _difficulty);
            SetNav(_weaponCard, _stageCard, null, null, _start);
            SetNav(_difficulty, null, _teamSize, null, null);
            SetNav(_teamSize, _difficulty, _start, null, null);
            SetNav(_start, _teamSize, null, _weaponCard, null);
        }

        private static void SetNav(Selectable item, Selectable up, Selectable down, Selectable left, Selectable right)
        {
            item.navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnUp = up,
                selectOnDown = down,
                selectOnLeft = left,
                selectOnRight = right,
            };
        }

        // 隊伍預覽:我方(左、第一個是「你」)vs 對手(右)的烏賊頭,人數跟著選項變
        private void BuildPreview(Transform panel, float width)
        {
            _preview = Ui.Rect("Preview", panel, new Vector2(0f, 1f), new Vector2(30f, -350f), new Vector2(width - 60f, 180f));
            MenuTheme.Panel9("Back", _preview, new Vector2(0f, 1f), Vector2.zero, new Vector2(width - 60f, 180f),
                MenuTheme.PanelSoft, 18);
            Text vs = MenuTheme.Label("VS", _preview, new Vector2(0.5f, 0.5f), new Vector2(0f, 6f), new Vector2(120f, 80f), 56,
                TextAnchor.MiddleCenter, Color.white, 3f, true);
            vs.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            vs.text = "VS";
            for (int i = 0; i < MatchSettings.MaxTeamSize; i++)
            {
                _allyIcons.Add(SquidIcon(_preview, "Ally" + i, i == 0));
                _enemyIcons.Add(SquidIcon(_preview, "Enemy" + i, false));
            }
            Text allyLabel = MenuTheme.Label("AllyLabel", _preview, new Vector2(0.25f, 0f), new Vector2(0f, 12f),
                new Vector2(300f, 30f), 22, TextAnchor.MiddleCenter, MenuTheme.TextDim, 0f);
            allyLabel.rectTransform.pivot = new Vector2(0.5f, 0f);
            allyLabel.text = "我方";
            Text enemyLabel = MenuTheme.Label("EnemyLabel", _preview, new Vector2(0.75f, 0f), new Vector2(0f, 12f),
                new Vector2(300f, 30f), 22, TextAnchor.MiddleCenter, MenuTheme.TextDim, 0f);
            enemyLabel.rectTransform.pivot = new Vector2(0.5f, 0f);
            enemyLabel.text = "對手(電腦)";
        }

        private static Image SquidIcon(Transform parent, string name, bool you)
        {
            Image body = Ui.Image(name, parent, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(72f, 72f), UiSprites.SquidHead(),
                Color.white);
            body.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            Ui.Image("Eyes", body.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(72f, 72f), UiSprites.SquidEyes(),
                Color.white).rectTransform.pivot = new Vector2(0.5f, 0.5f);
            if (you)
            {
                Text tag = MenuTheme.Label("You", body.transform, new Vector2(0.5f, 1f), new Vector2(0f, 26f), new Vector2(80f, 30f),
                    22, TextAnchor.MiddleCenter, Color.white, 2f);
                tag.rectTransform.pivot = new Vector2(0.5f, 1f);
                tag.text = "你";
            }
            return body;
        }

        private void RefreshPreview()
        {
            int n = _menu.TeamSize;
            // 色彩鎖定時用固定的黃 / 藍紫預覽,否則用選單主色(實際配色開場才隨機決定)
            Color ally = MatchSettings.ColorLock ? InkColorSets.ColorLock.Alpha : MenuTheme.Accent;
            Color enemy = MatchSettings.ColorLock ? InkColorSets.ColorLock.Bravo : MenuTheme.Secondary;
            for (int i = 0; i < _allyIcons.Count; i++)
            {
                bool on = i < n;
                _allyIcons[i].gameObject.SetActive(on);
                _enemyIcons[i].gameObject.SetActive(on);
                // 我方由中線往左排、對手往右排(與對戰 HUD 一致)
                _allyIcons[i].rectTransform.anchoredPosition = new Vector2(-104f - i * 80f, 10f);
                _enemyIcons[i].rectTransform.anchoredPosition = new Vector2(104f + i * 80f, 10f);
                _allyIcons[i].color = ally;
                _enemyIcons[i].color = enemy;
            }
        }

        public override void OnShow()
        {
            // 所選模式(蠻頹模式用模式圖示)
            MatchMode mode = MatchModes.FromId(_menu.ModeId);
            _modeCard.Label.text = ObjectiveModes.DisplayName(mode);
            _modeCard.Icon.sprite = ModeIcon(mode);
            StageCatalog.Stage stage = _menu.SelectedStage;
            // 場地沒有這個模式的目標配置 → 開賽會退回塗地,這裡先說清楚
            bool supported = string.IsNullOrEmpty(stage.SceneName) || ModeLayout.StageSupports(stage.SceneName, mode);
            _modeCard.Detail.text = mode == MatchMode.TurfWar ? "模式"
                : supported ? $"模式 ・ {ObjectiveModes.EnglishName(mode)}" : "模式 ・ 此場地尚無配置,將進行塗地對戰";
            _stageCard.Label.text = string.IsNullOrEmpty(stage.DisplayName) ? "(沒有場地)" : stage.DisplayName;
            MenuTheme.Fit(_stageCard.Label, 46, 28);
            _stageCard.Detail.text = string.IsNullOrEmpty(stage.EnglishName) ? "場地" : $"場地 ・ {stage.EnglishName}";
            WeaponKit kit = _menu.SelectedKit;
            _weaponCard.Label.text = kit != null ? kit.DisplayName : "(沒有武器)";
            MenuTheme.Fit(_weaponCard.Label, 46, 28);
            _weaponCard.Detail.text = kit != null
                ? $"武器 ・ {(kit.Sub != null ? kit.Sub.DisplayName : "-")} / {(kit.Special != null ? kit.Special.SpecialName : "-")}"
                : "武器";
            _weaponCard.Icon.sprite = MenuSprites.WeaponClassIcon(kit != null && kit.Main != null ? kit.Main.Class : WeaponClass.Shooter);
            _difficulty.SetIndex((int)_menu.Difficulty, false);
            _difficultyNote.text = DifficultyNotes[(int)_menu.Difficulty];
            _teamSize.SetIndex(_menu.TeamSize - 1, false);
            RefreshPreview();
        }

        private static Sprite ModeIcon(MatchMode mode)
        {
            switch (mode)
            {
                case MatchMode.SplatZones: return MenuSprites.ZoneIcon();
                case MatchMode.TowerControl: return MenuSprites.TowerIcon();
                case MatchMode.Rainmaker: return MenuSprites.RainmakerIcon();
                case MatchMode.ClamBlitz: return MenuSprites.ClamIcon();
                default: return UiSprites.Splat(256, 9);
            }
        }

        public override void Tick()
        {
            float t = Time.unscaledTime;
            for (int i = 0; i < _allyIcons.Count; i++)
            {
                float bob = Mathf.Sin(t * 3f + i * 0.9f) * 4f;
                _allyIcons[i].rectTransform.localRotation = Quaternion.Euler(0f, 0f, bob);
                _enemyIcons[i].rectTransform.localRotation = Quaternion.Euler(0f, 0f, -bob);
            }
        }
    }
}

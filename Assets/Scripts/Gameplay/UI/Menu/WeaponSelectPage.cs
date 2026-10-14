using System.Collections.Generic;
using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.Combat;
using UnityEngine;
using UnityEngine.UI;

namespace SplatoonC.Gameplay.UI.Menu
{
    // 武器選擇(資料驅動:Resources/WeaponCatalog 有幾組就列幾張卡,1~30 組都排得好):
    //   上方種類分頁(全部 + 清單裡有的種類,照清單順序;Q/E、LB/RB 切換,S3 依種類分組)
    //   左側卡片格(種類剪影 + 名稱;少量放大置中、多量縮小捲動)
    //   右側詳細:種類、名稱、副武器、大招 + 所需點數、重量級別(跑速/游速倍率)、三條能力值、說明
    public sealed class WeaponSelectPage : MenuPage
    {
        private const float GridX = 100f;
        private const float GridY = 272f;
        private const float GridWidth = 980f;
        private const float GridHeight = 650f;

        private readonly MainMenuController _menu;
        private readonly List<WeaponKit> _visible = new List<WeaponKit>();
        private readonly List<int> _tabClasses = new List<int>();
        private readonly List<Image> _tabBacks = new List<Image>();
        private readonly List<Image> _tabIcons = new List<Image>();
        private readonly List<Text> _tabTexts = new List<Text>();
        private readonly List<GameObject> _tabObjects = new List<GameObject>();
        private RectTransform _keyE;
        private MenuCardGrid _grid;
        private RectTransform _tabRow;
        private int _filter = -1;
        private int _builtSignature;
        private Text _emptyText;

        // 詳細面板
        private RectTransform _detail;
        private Image _tape;
        private Text _classTape;
        private Image _detailIcon;
        private Image _detailSplat;
        private Text _name;
        private Text _english;
        private Text _sub;
        private Text _special;
        private Text _points;
        private Text _weight;
        private Text _weightDetail;
        private readonly Text[] _statLabels = new Text[3];
        private readonly Text[] _statValues = new Text[3];
        private readonly Image[] _statBars = new Image[3];
        private readonly float[] _statTargets = new float[3];
        private readonly float[] _statShown = new float[3];
        private Text _description;

        public WeaponSelectPage(MainMenuController menu)
        {
            _menu = menu;
        }

        public MenuCardGrid Grid => _grid;
        public IReadOnlyList<WeaponKit> VisibleKits => _visible;
        public int IndexOfVisible(WeaponKit kit) => _visible.IndexOf(kit);
        public Text DetailName => _name;
        public Text DetailSub => _sub;
        public Text DetailSpecial => _special;
        public Text DetailPoints => _points;
        public Text DetailWeight => _weight;
        public int TabCount => _tabClasses.Count;

        public override string Hints => _menu.TrainingFlow
            ? "Enter / A:前往訓練場|Q / E:切換種類|Esc / B:返回"
            : "Enter / A:決定|Q / E:切換種類|Esc / B:返回";

        public override Selectable DefaultSelection
        {
            get
            {
                if (_grid.Cards.Count == 0)
                {
                    return null;
                }
                int index = _visible.IndexOf(_menu.SelectedKit);
                return _grid.Cards[Mathf.Max(0, index)];
            }
        }

        protected override void Build(RectTransform root)
        {
            MenuTheme.TapeTitle("Title", root, new Vector2(0f, 1f), new Vector2(90f, -60f), "選擇武器", 56, MenuTheme.Accent);
            _tabRow = MenuWidgets.TopLeft("Tabs", root, GridX, 196f, GridWidth, 56f);
            _grid = new MenuCardGrid(root, GridX, GridY, GridWidth, GridHeight);
            _emptyText = MenuTheme.Label("Empty", root, new Vector2(0f, 1f), new Vector2(GridX, -GridY - 200f),
                new Vector2(GridWidth, 80f), 34, TextAnchor.MiddleCenter, MenuTheme.TextDim, 0f);
            _emptyText.text = "武器清單是空的(先跑 Tools/SplatoonC/M7 Setup)";
            _emptyText.enabled = false;
            BuildDetail(root);
        }

        private void BuildDetail(RectTransform root)
        {
            const float w = 700f;
            const float h = 820f;
            _detail = Ui.Rect("Detail", root, new Vector2(1f, 1f), new Vector2(-80f, -178f), new Vector2(w, h));
            MenuTheme.Panel9("Back", _detail, new Vector2(0f, 1f), Vector2.zero, new Vector2(w, h), MenuTheme.Panel, 26);

            // 種類膠帶(左上,傾斜;寬度跟著文字)
            _tape = Ui.Image("ClassTape", _detail, new Vector2(0f, 1f), new Vector2(26f, -26f), new Vector2(300f, 54f), null,
                MenuTheme.Accent);
            _tape.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 2.5f);
            _classTape = MenuTheme.Label("ClassText", _tape.transform, new Vector2(0f, 0.5f), new Vector2(18f, 0f),
                new Vector2(360f, 54f), 30, TextAnchor.MiddleLeft, MenuTheme.TextOnAccent, 0f);

            // 右上:種類剪影壓在紫色墨漬上
            _detailSplat = Ui.Image("Splat", _detail, new Vector2(0f, 1f), Vector2.zero, new Vector2(270f, 270f),
                UiSprites.Splat(256, 51), Ui.WithAlpha(MenuTheme.Secondary, 0.95f));
            _detailSplat.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _detailSplat.rectTransform.anchoredPosition = new Vector2(548f, -150f);
            _detailIcon = Ui.Image("Icon", _detail, new Vector2(0f, 1f), Vector2.zero, new Vector2(190f, 190f),
                MenuSprites.WeaponClassIcon(WeaponClass.Shooter), Color.white);
            _detailIcon.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _detailIcon.rectTransform.anchoredPosition = new Vector2(548f, -150f);

            _name = MenuTheme.Label("Name", _detail, new Vector2(0f, 1f), new Vector2(34f, -104f), new Vector2(400f, 64f), 50,
                TextAnchor.MiddleLeft, Color.white, 2f);
            _english = MenuTheme.Label("English", _detail, new Vector2(0f, 1f), new Vector2(36f, -170f), new Vector2(400f, 34f), 24,
                TextAnchor.MiddleLeft, MenuTheme.Accent, 0f, true);

            Separator(296f);
            _sub = InfoRow("Sub", 314f, "副武器", MenuSprites.SubIcon(), out _);
            _special = InfoRow("Special", 362f, "大招", MenuSprites.SpecialIcon(), out _);
            _points = MenuTheme.Label("Points", _detail, new Vector2(1f, 1f), new Vector2(-34f, -362f), new Vector2(160f, 44f), 30,
                TextAnchor.MiddleRight, MenuTheme.Accent, 0f, true);
            _weight = InfoRow("Weight", 410f, "重量", MenuSprites.WeightIcon(), out _);
            _weightDetail = MenuTheme.Label("WeightDetail", _detail, new Vector2(1f, 1f), new Vector2(-34f, -410f),
                new Vector2(300f, 44f), 22, TextAnchor.MiddleRight, MenuTheme.TextDim, 0f);
            Separator(468f);

            for (int i = 0; i < 3; i++)
            {
                float y = 486f + i * 50f;
                _statLabels[i] = MenuTheme.Label("StatLabel" + i, _detail, new Vector2(0f, 1f), new Vector2(34f, -y),
                    new Vector2(120f, 42f), 28, TextAnchor.MiddleLeft, Color.white, 0f);
                _statBars[i] = MenuTheme.StatBar("StatBar" + i, _detail, new Vector2(0f, 1f), new Vector2(160f, -y - 8f),
                    new Vector2(420f, 26f), MenuTheme.Accent);
                _statValues[i] = MenuTheme.Label("StatValue" + i, _detail, new Vector2(1f, 1f), new Vector2(-34f, -y),
                    new Vector2(90f, 42f), 28, TextAnchor.MiddleRight, Color.white, 0f, true);
            }
            _description = MenuTheme.Paragraph("Description", _detail, new Vector2(0f, 1f), new Vector2(34f, -648f),
                new Vector2(632f, 150f), 24, MenuTheme.TextDim);
        }

        private void Separator(float y)
        {
            Ui.Image("Separator", _detail, new Vector2(0f, 1f), new Vector2(34f, -y), new Vector2(632f, 2f), null,
                new Color(1f, 1f, 1f, 0.12f));
        }

        // 圖示 + 欄名 + 值;回傳值文字
        private Text InfoRow(string name, float y, string caption, Sprite icon, out Image iconImage)
        {
            iconImage = null;
            float x = 34f;
            if (icon != null)
            {
                iconImage = Ui.Image(name + "Icon", _detail, new Vector2(0f, 1f), new Vector2(x, -y - 4f), new Vector2(36f, 36f),
                    icon, MenuTheme.Accent);
            }
            Text label = MenuTheme.Label(name + "Caption", _detail, new Vector2(0f, 1f), new Vector2(x + 46f, -y),
                new Vector2(120f, 44f), 24, TextAnchor.MiddleLeft, MenuTheme.TextDim, 0f);
            label.text = caption;
            Text value = MenuTheme.Label(name + "Value", _detail, new Vector2(0f, 1f), new Vector2(x + 160f, -y),
                new Vector2(330f, 44f), 30, TextAnchor.MiddleLeft, Color.white, 0f);
            return value;
        }

        public override void OnShow()
        {
            // 目前裝備的武器不在上次的種類分頁裡:回到「全部」,一進來就看得到它
            if (_filter >= 0 && _menu.SelectedKit != null && _menu.SelectedKit.Main != null
                && (int)_menu.SelectedKit.Main.Class != _filter)
            {
                _filter = -1;
            }
            RebuildTabs();
            int signature = Signature();
            if (signature != _builtSignature)
            {
                RebuildGrid();
            }
            WeaponKit kit = _menu.SelectedKit != null && _visible.Contains(_menu.SelectedKit) ? _menu.SelectedKit
                : _visible.Count > 0 ? _visible[0] : null;
            if (kit != null)
            {
                ShowDetail(kit, true);
                _grid.RevealImmediate(_visible.IndexOf(kit));
            }
        }

        // 清單內容 + 篩選 + 目前裝備 的指紋:沒變就不重建卡片
        private int Signature()
        {
            IReadOnlyList<WeaponKit> kits = _menu.Kits;
            int hash = 17 + _filter * 31;
            for (int i = 0; i < kits.Count; i++)
            {
                hash = hash * 31 + (kits[i] != null ? kits[i].GetHashCode() : 0);
            }
            return hash;
        }

        private void RebuildTabs()
        {
            IReadOnlyList<WeaponKit> kits = _menu.Kits;
            var classes = new List<int>();
            for (int i = 0; i < kits.Count; i++)
            {
                if (kits[i] == null || kits[i].Main == null)
                {
                    continue;
                }
                int c = (int)kits[i].Main.Class;
                if (!classes.Contains(c))
                {
                    classes.Add(c);
                }
            }
            // 依清單中第一次出現的順序(清單本身照 S3 遊戲內順序:射擊槍、爆破槍、滾筒、狙擊槍、雙槍…)
            classes.Insert(0, -1);
            bool same = classes.Count == _tabClasses.Count;
            for (int i = 0; same && i < classes.Count; i++)
            {
                same = classes[i] == _tabClasses[i];
            }
            if (!same)
            {
                foreach (GameObject go in _tabObjects)
                {
                    Object.Destroy(go);
                }
                _tabObjects.Clear();
                _tabBacks.Clear();
                _tabIcons.Clear();
                _tabTexts.Clear();
                _tabClasses.Clear();
                _tabClasses.AddRange(classes);
                HintKey("Q", 0f);
                for (int i = 0; i < _tabClasses.Count; i++)
                {
                    AddTab(i);
                }
                _keyE = HintKey("E", 0f);
            }
            if (!_tabClasses.Contains(_filter))
            {
                _filter = -1;
            }
            RefreshTabs();
        }

        private RectTransform HintKey(string key, float x)
        {
            Image cap = Ui.Image("Key" + key, _tabRow, new Vector2(0f, 0.5f), new Vector2(x, 0f), new Vector2(44f, 44f),
                UiSprites.RoundedRect(12), new Color(1f, 1f, 1f, 0.16f));
            Text text = MenuTheme.Label("Text", cap.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(44f, 44f), 24,
                TextAnchor.MiddleCenter, Color.white, 0f);
            text.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            text.text = key;
            _tabObjects.Add(cap.gameObject);
            return cap.rectTransform;
        }

        // S3 的種類分頁是圖示:未選中只顯示剪影,選中的那一頁展開顯示名稱(11 種全出現也排得下)
        private void AddTab(int index)
        {
            int c = _tabClasses[index];
            string label = c < 0 ? "全部" : MenuTheme.ClassName((WeaponClass)c);
            Image back = Ui.Image("Tab_" + label, _tabRow, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(54f, 50f),
                UiSprites.RoundedRect(14), MenuTheme.Item);
            back.raycastTarget = true;
            var button = back.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            // 分頁只給滑鼠點與 Q/E 切換,不進方向鍵導覽(不會搶走卡片的選取)
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() => SetFilter(c));
            Image icon = null;
            if (c >= 0)
            {
                icon = Ui.Image("Icon", back.transform, new Vector2(0f, 0.5f), new Vector2(7f, 0f), new Vector2(40f, 40f),
                    MenuSprites.WeaponClassIcon((WeaponClass)c), Color.white);
            }
            Text text = MenuTheme.Label("Text", back.transform, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(120f, 50f), 24,
                TextAnchor.MiddleLeft, Color.white, 0f);
            text.text = label;
            _tabBacks.Add(back);
            _tabIcons.Add(icon);
            _tabTexts.Add(text);
            _tabObjects.Add(back.gameObject);
        }

        // 依選中的分頁重新排列(選中者展開顯示名稱)
        private void RefreshTabs()
        {
            float x = 58f;
            for (int i = 0; i < _tabClasses.Count; i++)
            {
                bool active = _tabClasses[i] == _filter;
                bool all = _tabClasses[i] < 0;
                Text text = _tabTexts[i];
                Image icon = _tabIcons[i];
                float textWidth = text.preferredWidth + 8f;
                bool showText = all || active;
                float left = icon != null ? 52f : 18f;
                float width = showText ? left + textWidth + 16f : 54f;
                RectTransform back = _tabBacks[i].rectTransform;
                back.anchoredPosition = new Vector2(x, 0f);
                back.sizeDelta = new Vector2(width, 50f);
                _tabBacks[i].color = active ? MenuTheme.Accent : MenuTheme.Item;
                text.enabled = showText;
                text.rectTransform.anchoredPosition = new Vector2(left, 0f);
                text.rectTransform.sizeDelta = new Vector2(textWidth + 4f, 50f);
                text.color = active ? MenuTheme.TextOnAccent : Color.white;
                if (icon != null)
                {
                    icon.color = active ? MenuTheme.TextOnAccent : Color.white;
                }
                x += width + 6f;
            }
            if (_keyE != null)
            {
                _keyE.anchoredPosition = new Vector2(x + 4f, 0f);
            }
        }

        public void SetFilter(int weaponClass)
        {
            if (weaponClass == _filter)
            {
                return;
            }
            GameAudio.Play2D("ui_move");
            _filter = weaponClass;
            RefreshTabs();
            RebuildGrid();
            if (_grid.Cards.Count > 0)
            {
                Navigator.Select(_grid.Cards[0]);
                ShowDetail(_visible[0], true);
            }
        }

        private void RebuildGrid()
        {
            _builtSignature = Signature();
            _visible.Clear();
            IReadOnlyList<WeaponKit> kits = _menu.Kits;
            for (int i = 0; i < kits.Count; i++)
            {
                WeaponKit kit = kits[i];
                if (kit == null || !kit.IsValid)
                {
                    continue;
                }
                if (_filter >= 0 && (int)kit.Main.Class != _filter)
                {
                    continue;
                }
                _visible.Add(kit);
            }
            _emptyText.enabled = _visible.Count == 0;
            _grid.Begin(_visible.Count, 18f, 0.86f, 180f, 380f);
            for (int i = 0; i < _visible.Count; i++)
            {
                WeaponKit kit = _visible[i];
                Rect cell = _grid.CellRect(i);
                int titleSize = Mathf.RoundToInt(Mathf.Clamp(cell.width * 0.105f, 21f, 34f));
                MenuButton card = MenuWidgets.Card("Kit_" + kit.Id, _grid.Content, cell.x, cell.y, cell.width, cell.height,
                    kit.DisplayName, MenuTheme.ClassName(kit.Main.Class), MenuSprites.WeaponClassIcon(kit.Main.Class),
                    titleSize);
                card.Focused += () => ShowDetail(kit, false);
                card.Activated += () => Choose(kit);
                if (kit == _menu.SelectedKit)
                {
                    // 目前裝備中的標記
                    Image badge = MenuTheme.Panel9("Equipped", card.Content, new Vector2(1f, 1f), new Vector2(-8f, -8f),
                        new Vector2(86f, 30f), MenuTheme.Secondary, 10);
                    Text text = MenuTheme.Label("Text", badge.transform, new Vector2(0.5f, 0.5f), Vector2.zero,
                        new Vector2(86f, 30f), 18, TextAnchor.MiddleCenter, Color.white, 0f);
                    text.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                    text.text = "使用中";
                }
                _grid.Add(card);
            }
            _grid.LinkNavigation(null, null, null, null);
        }

        private void Choose(WeaponKit kit)
        {
            _menu.SelectedKit = kit;
            // 重畫「使用中」標記
            _builtSignature = 0;
            if (_menu.TrainingFlow)
            {
                _menu.StartTraining();
            }
            else
            {
                _menu.Navigator.Push(_menu.Setup);
            }
        }

        private void ShowDetail(WeaponKit kit, bool instant)
        {
            WeaponClass weaponClass = kit.Main != null ? kit.Main.Class : WeaponClass.Shooter;
            _classTape.text = $"{MenuTheme.ClassName(weaponClass)}  {MenuTheme.ClassEnglish(weaponClass)}";
            float tapeWidth = _classTape.preferredWidth + 40f;
            _tape.rectTransform.sizeDelta = new Vector2(tapeWidth, 54f);
            _classTape.rectTransform.sizeDelta = new Vector2(tapeWidth, 54f);
            _detailIcon.sprite = MenuSprites.WeaponClassIcon(weaponClass);
            _name.text = kit.DisplayName;
            MenuTheme.Fit(_name, 50, 30);
            _english.text = kit.EnglishName;
            _sub.text = kit.Sub != null ? kit.Sub.DisplayName : "-";
            _special.text = kit.Special != null ? kit.Special.SpecialName : "-";
            _points.text = $"{Mathf.RoundToInt(kit.SpecialPoints)}p";
            _weight.text = MenuTheme.WeightName(kit.Weight);
            _weightDetail.text = MenuTheme.WeightDetail(kit.Weight);
            SetStat(0, kit.Stat1Label, kit.Stat1, instant);
            SetStat(1, kit.Stat2Label, kit.Stat2, instant);
            SetStat(2, kit.Stat3Label, kit.Stat3, instant);
            _description.text = kit.Description;
            if (!instant)
            {
                _detailIcon.rectTransform.localScale = Vector3.one * 0.85f;
            }
        }

        private void SetStat(int i, string label, int value, bool instant)
        {
            _statLabels[i].text = label;
            _statValues[i].text = value.ToString();
            _statTargets[i] = Mathf.Clamp01(value / 100f);
            if (instant)
            {
                _statShown[i] = _statTargets[i];
                MenuTheme.SetBar(_statBars[i], _statShown[i]);
            }
        }

        public override void Tick()
        {
            _grid.Tick();
            int delta = MenuInput.TabDelta();
            if (delta != 0 && _tabClasses.Count > 1)
            {
                int index = Mathf.Max(0, _tabClasses.IndexOf(_filter));
                index = (index + delta + _tabClasses.Count) % _tabClasses.Count;
                SetFilter(_tabClasses[index]);
            }
            float k = 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime);
            for (int i = 0; i < 3; i++)
            {
                if (Mathf.Abs(_statShown[i] - _statTargets[i]) > 0.001f)
                {
                    _statShown[i] = Mathf.Lerp(_statShown[i], _statTargets[i], k);
                    MenuTheme.SetBar(_statBars[i], _statShown[i]);
                }
            }
            Transform icon = _detailIcon.rectTransform;
            icon.localScale = Vector3.Lerp(icon.localScale, Vector3.one, k);
            _detailSplat.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Time.unscaledTime * -8f);
        }
    }
}

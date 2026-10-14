using SplatoonC.Gameplay.Match;
using UnityEngine;
using UnityEngine.UI;

namespace SplatoonC.Gameplay.UI.Menu
{
    // 場地選擇:Resources/StageCatalog 的場地卡片(預覽色 + 名稱),右側場地資訊。卡片格隨場地數量縮放/捲動。
    public sealed class StageSelectPage : MenuPage
    {
        private const float GridX = 100f;
        private const float GridY = 240f;
        private const float GridWidth = 960f;
        private const float GridHeight = 660f;

        private readonly MainMenuController _menu;
        private MenuCardGrid _grid;
        private RectTransform _info;
        private RectTransform _infoPreview;
        private Text _name;
        private Text _english;
        private Text _description;
        private int _builtCount = -1;

        public StageSelectPage(MainMenuController menu)
        {
            _menu = menu;
        }

        public MenuCardGrid Grid => _grid;

        public override Selectable DefaultSelection
        {
            get
            {
                if (_grid.Cards.Count == 0)
                {
                    return null;
                }
                return _grid.Cards[Mathf.Clamp(_menu.StageIndex, 0, _grid.Cards.Count - 1)];
            }
        }

        protected override void Build(RectTransform root)
        {
            MenuTheme.TapeTitle("Title", root, new Vector2(0f, 1f), new Vector2(90f, -60f), "選擇場地", 56, MenuTheme.Accent);
            _grid = new MenuCardGrid(root, GridX, GridY, GridWidth, GridHeight);

            // 右側資訊
            RectTransform info = Ui.Rect("Info", root, new Vector2(1f, 1f), new Vector2(-90f, -200f), new Vector2(660f, 740f));
            _info = info;
            MenuTheme.Panel9("Back", info, new Vector2(0f, 1f), Vector2.zero, new Vector2(660f, 740f), MenuTheme.Panel, 26);
            _name = MenuTheme.Label("Name", info, new Vector2(0f, 1f), new Vector2(30f, -380f), new Vector2(600f, 70f), 54,
                TextAnchor.MiddleLeft, Color.white, 2f);
            _english = MenuTheme.Label("English", info, new Vector2(0f, 1f), new Vector2(32f, -446f), new Vector2(600f, 40f), 26,
                TextAnchor.MiddleLeft, MenuTheme.Accent, 0f, true);
            _description = MenuTheme.Paragraph("Description", info, new Vector2(0f, 1f), new Vector2(32f, -500f),
                new Vector2(596f, 150f), 26, MenuTheme.TextDim);
            Text modes = MenuTheme.Label("Modes", info, new Vector2(0f, 0f), new Vector2(32f, 28f), new Vector2(596f, 40f), 24,
                TextAnchor.MiddleLeft, Color.white, 0f);
            modes.text = "對應模式:塗地對戰";
        }

        public override void OnShow()
        {
            StageCatalog stages = _menu.StageList;
            int count = stages != null ? stages.Stages.Count : 0;
            if (count != _builtCount)
            {
                Rebuild(stages, count);
            }
            if (count > 0)
            {
                ShowInfo(_menu.SelectedStage);
                _grid.RevealImmediate(Mathf.Clamp(_menu.StageIndex, 0, count - 1));
            }
        }

        private void Rebuild(StageCatalog stages, int count)
        {
            _builtCount = count;
            _grid.Begin(count, 22f, 0.62f, 300f, 760f);
            for (int i = 0; i < count; i++)
            {
                int index = i;
                StageCatalog.Stage stage = stages.Stages[i];
                Rect cell = _grid.CellRect(i);
                int titleSize = Mathf.RoundToInt(Mathf.Clamp(cell.width * 0.075f, 26f, 48f));
                MenuButton card = MenuWidgets.Card("Stage_" + stage.SceneName, _grid.Content, cell.x, cell.y, cell.width,
                    cell.height, stage.DisplayName, stage.EnglishName, null, titleSize);
                float band = titleSize * 1.6f;
                // 預覽(放在底板之上、文字之下)
                RectTransform preview = BuildPreview(card.Content, new Vector2(0.5f, 1f), new Vector2(0f, -10f),
                    new Vector2(cell.width - 20f, cell.height - band - 14f), stage, i);
                preview.SetSiblingIndex(2);
                // 英文名移到預覽左上(白字)
                if (card.Detail != null)
                {
                    card.Detail.transform.SetAsLastSibling();
                    card.DetailColor = Color.white;
                    // 淺色天空上的白字:加深色描邊
                    var outline = card.Detail.gameObject.AddComponent<Outline>();
                    outline.effectColor = new Color(0f, 0f, 0f, 0.6f);
                    outline.effectDistance = new Vector2(1.5f, -1.5f);
                }
                card.Focused += () =>
                {
                    _menu.StageIndex = index;
                    ShowInfo(stage);
                };
                card.Activated += () =>
                {
                    _menu.StageIndex = index;
                    _menu.TrainingFlow = false;
                    _menu.Navigator.Push(_menu.Weapons);
                };
                _grid.Add(card);
            }
            _grid.LinkNavigation(null, null, null, null);
        }

        // 程序化場地預覽:天空 + 峽谷岩壁剪影 + 谷底地面 + 兩隊的墨漬(S3 選場地時看的是場地照片;這裡用場地配色畫示意)
        private static RectTransform BuildPreview(Transform parent, Vector2 anchor, Vector2 pos, Vector2 size,
            StageCatalog.Stage stage, int seed)
        {
            Color skyColor = Color.Lerp(stage.PreviewColor, new Color(0.55f, 0.78f, 1f), 0.72f);
            Image sky = MenuTheme.Panel9("Preview", parent, anchor, pos, size, skyColor, 14);
            sky.gameObject.AddComponent<RectMask2D>();
            Transform root = sky.transform;
            Image gorge = Ui.Image("Gorge", root, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(size.x, size.y * 0.8f),
                MenuSprites.GorgeSilhouette(), stage.PreviewAccent);
            gorge.rectTransform.pivot = new Vector2(0.5f, 0f);
            Ui.Image("Floor", root, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(size.x, size.y * 0.22f), null,
                stage.PreviewColor).rectTransform.pivot = new Vector2(0.5f, 0f);
            float ink = size.y * 0.42f;
            Image inkA = Ui.Image("InkA", root, new Vector2(0.28f, 0.2f), Vector2.zero, new Vector2(ink, ink),
                UiSprites.Splat(256, 40 + seed), Ui.WithAlpha(MenuTheme.Accent, 0.92f));
            inkA.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            Image inkB = Ui.Image("InkB", root, new Vector2(0.74f, 0.26f), Vector2.zero, new Vector2(ink * 1.15f, ink * 1.15f),
                UiSprites.Splat(256, 60 + seed), Ui.WithAlpha(MenuTheme.Secondary, 0.92f));
            inkB.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            Image stripes = Ui.Image("Stripes", root, new Vector2(0.5f, 0.5f), Vector2.zero, size, MenuSprites.Stripes(),
                new Color(1f, 1f, 1f, 0.07f));
            stripes.type = Image.Type.Tiled;
            return sky.rectTransform;
        }

        private void ShowInfo(StageCatalog.Stage stage)
        {
            if (_infoPreview != null)
            {
                Object.Destroy(_infoPreview.gameObject);
            }
            _infoPreview = BuildPreview(_info, new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(604f, 330f), stage,
                _menu.StageIndex);
            _name.text = stage.DisplayName;
            MenuTheme.Fit(_name, 54, 32);
            _english.text = stage.EnglishName;
            _description.text = stage.Description;
        }

        public override void Tick()
        {
            _grid.Tick();
        }
    }
}

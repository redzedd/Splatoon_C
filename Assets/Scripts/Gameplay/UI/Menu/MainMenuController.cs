using System.Collections.Generic;
using SplatoonC.Gameplay.AI;
using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Match;
using SplatoonC.Gameplay.Player;
using UnityEngine;
using UnityEngine.UI;

namespace SplatoonC.Gameplay.UI.Menu
{
    // 主選單場景(Assets/Scenes/MainMenu.unity,MenuBuilder 產生)的總管:執行期用程式碼建 uGUI。
    //   標題(按任意鍵)→ 主選單:對戰 / 訓練場 / 設定 / 離開
    //   對戰:模式 → 場地 → 武器 → 對戰準備(bot 難度、人數)→ 開始(MatchSettings → 載入場地)
    //   訓練場:武器 → 載入試射場(同一把武器)
    // 對戰結束回到主選單時直接落在「對戰準備」(馬上再來一場);訓練場回來落在主選單。
    [DisallowMultipleComponent]
    public sealed class MainMenuController : MonoBehaviour
    {
        // 場景內容版本:MenuBuilder 比對這個值決定是否重建 MainMenu.unity
        public const int CurrentSceneVersion = 1;
        public const string TrainingScene = "SampleScene";

        [SerializeField, Tooltip("場景內容版本(MenuBuilder 寫入;與程式版本不同時重建場景)")]
        private int _sceneVersion = CurrentSceneVersion;

        public static MainMenuController Instance { get; private set; }

        // 測試/除錯:替換武器清單(null = Resources/WeaponCatalog)
        public static WeaponCatalog CatalogOverride { get; set; }

        public MenuNavigator Navigator { get; private set; }
        public Canvas Canvas { get; private set; }
        public TitlePage Title { get; private set; }
        public HomePage Home { get; private set; }
        public ModeSelectPage Modes { get; private set; }
        public StageSelectPage Stages { get; private set; }
        public WeaponSelectPage Weapons { get; private set; }
        public MatchSetupPage Setup { get; private set; }
        public SettingsPage Settings { get; private set; }
        public ConfirmDialog Confirm { get; private set; }

        // ── 目前的選擇(開始時寫進 MatchSettings)──
        public string ModeId { get; set; } = MatchSettings.TurfWarModeId;
        public int StageIndex { get; set; }
        public WeaponKit SelectedKit { get; set; }
        public BotBrain.Difficulty Difficulty { get; set; } = BotBrain.Difficulty.Normal;
        public int TeamSize { get; set; } = MatchSettings.MaxTeamSize;
        // 武器頁是從「訓練場」進來的(選完直接進試射場)
        public bool TrainingFlow { get; set; }

        public int SceneVersion => _sceneVersion;

        private MenuHintBar _hints;
        private bool _built;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
            CatalogOverride = null;
        }

        public IReadOnlyList<WeaponKit> Kits
        {
            get
            {
                WeaponCatalog catalog = CatalogOverride != null ? CatalogOverride : WeaponCatalog.Load();
                return catalog != null ? catalog.Kits : (IReadOnlyList<WeaponKit>)System.Array.Empty<WeaponKit>();
            }
        }

        public StageCatalog StageList => StageCatalog.Load();

        public bool HasStage => StageList != null && StageList.Stages.Count > 0;

        public StageCatalog.Stage SelectedStage
        {
            get
            {
                StageCatalog list = StageList;
                if (list == null || list.Stages.Count == 0)
                {
                    return default;
                }
                return list.Stages[Mathf.Clamp(StageIndex, 0, list.Stages.Count - 1)];
            }
        }

        private void Awake()
        {
            Instance = this;
            // 從暫停選單/對戰回來:清掉任何殘留的暫停狀態
            Time.timeScale = 1f;
            AudioListener.pause = false;
            PlayerInputRouter.Paused = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Start()
        {
            MenuInput.EnsureEventSystem();
            RestoreSelections();
            Build();
            GameAudio.PlayMusic("menu_theme", 1.2f);
            switch (MatchSettings.LastLaunch)
            {
                case LaunchKind.Battle:
                    Navigator.ResetTo(Home, Setup);
                    break;
                case LaunchKind.Training:
                    Navigator.ResetTo(Home);
                    break;
                default:
                    Navigator.ResetTo(Title);
                    break;
            }
        }

        // 上一場的選擇(同一次執行內記住)
        private void RestoreSelections()
        {
            IReadOnlyList<WeaponKit> kits = Kits;
            SelectedKit = MatchSettings.LocalKit != null ? MatchSettings.LocalKit : kits.Count > 0 ? kits[0] : null;
            // 上一場的模式(真格區域打完回來,「再來一場」仍是真格區域)
            if (!string.IsNullOrEmpty(MatchSettings.ModeId))
            {
                ModeId = MatchSettings.ModeId;
            }
            StageCatalog stages = StageList;
            if (stages != null && !string.IsNullOrEmpty(MatchSettings.StageScene))
            {
                StageIndex = Mathf.Max(0, stages.IndexOf(MatchSettings.StageScene));
            }
            if (MatchSettings.BotDifficulty.HasValue)
            {
                Difficulty = MatchSettings.BotDifficulty.Value;
            }
            if (MatchSettings.TeamSize > 0)
            {
                TeamSize = Mathf.Clamp(MatchSettings.TeamSize, MatchSettings.MinTeamSize, MatchSettings.MaxTeamSize);
            }
        }

        private void Build()
        {
            if (_built)
            {
                return;
            }
            _built = true;
            Canvas = Ui.CreateCanvas("MainMenuCanvas", 20);
            Canvas.transform.SetParent(transform, false);
            Canvas.gameObject.AddComponent<GraphicRaycaster>();
            Transform root = Canvas.transform;
            MenuBackground.Create(root);
            RectTransform pages = Ui.Stretch("Pages", root);
            _hints = new MenuHintBar(root);

            Navigator = new MenuNavigator();
            Navigator.PageChanged += page => _hints.Set(page.Hints);
            // 堆疊底(對戰回來時是主選單)再按返回:回標題畫面
            Navigator.RootBack += () =>
            {
                if (Navigator.Current == Home)
                {
                    GameAudio.Play2D("ui_back");
                    Navigator.ResetTo(Title);
                }
            };
            Title = Create(new TitlePage(this), pages);
            Home = Create(new HomePage(this), pages);
            Modes = Create(new ModeSelectPage(this), pages);
            Stages = Create(new StageSelectPage(this), pages);
            Weapons = Create(new WeaponSelectPage(this), pages);
            Setup = Create(new MatchSetupPage(this), pages);
            Settings = Create(new SettingsPage(false), pages);
            Confirm = Create(new ConfirmDialog(), pages);
        }

        private T Create<T>(T page, RectTransform parent) where T : MenuPage
        {
            page.Create(parent, Navigator);
            return page;
        }

        private void Update()
        {
            if (Navigator == null)
            {
                return;
            }
            // 轉場中不接受輸入
            Navigator.InputEnabled = !SceneTransition.Busy;
            if (!SceneTransition.Busy)
            {
                Navigator.Update();
            }
        }

        // ── 流程 ──

        public void StartBattle()
        {
            if (SceneTransition.Busy || !HasStage)
            {
                return;
            }
            StageCatalog.Stage stage = SelectedStage;
            MatchSettings.LocalKit = SelectedKit;
            MatchSettings.StageScene = stage.SceneName;
            MatchSettings.ModeId = ModeId;
            MatchSettings.TeamSize = TeamSize;
            MatchSettings.BotDifficulty = Difficulty;
            MatchSettings.RandomBotKits = true;
            MatchSettings.LastLaunch = LaunchKind.Battle;
            // 對戰場景有自己的開場與對戰曲:選單曲在墨水蓋住畫面時淡出
            GameAudio.StopMusic(0.5f);
            SceneTransition.Load(stage.SceneName, stage.DisplayName, MenuTheme.Secondary);
        }

        public void StartTraining()
        {
            if (SceneTransition.Busy)
            {
                return;
            }
            MatchSettings.LocalKit = SelectedKit;
            MatchSettings.StageScene = TrainingScene;
            MatchSettings.LastLaunch = LaunchKind.Training;
            SceneTransition.Load(TrainingScene, "訓練場", MenuTheme.Accent);
        }

        public void QuitGame()
        {
            GameSettings.Save();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.ExitPlaymode();
#else
            Application.Quit();
#endif
        }
    }
}

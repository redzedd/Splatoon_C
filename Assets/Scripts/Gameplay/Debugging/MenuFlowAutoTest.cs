using System;
using System.Collections;
using System.Collections.Generic;
using SplatoonC.Core.Match;
using SplatoonC.Core.Menu;
using SplatoonC.Gameplay.AI;
using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Match;
using SplatoonC.Gameplay.Player;
using SplatoonC.Gameplay.UI.Menu;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace SplatoonC.Gameplay.Debugging
{
    // 選單流程驗收(主選單場景):全程用 uGUI 的真實事件路徑(submit / move 事件送到目前選取的元件,
    // 與 InputSystemUIInputModule 收到 Enter / 方向鍵時完全相同)操作:
    //   標題 → 主選單 → 模式(真格區域開放、未實作的蠻頹模式按不下去)→ 場地 → 武器(選非預設組合)→ 對戰準備(困難、3 對 3)→ 開始
    //   → 對戰場景:本機武器 = 所選、bot 難度 = 困難、名單 6 人
    //   → 暫停:timeScale 0、AudioListener.pause、角色輸入全鎖、計時凍結、音樂照播;暫停中改設定即時生效;繼續後輸入恢復
    //   → 暫停 → 回到主選單(確認)→ 落在「對戰準備」、狀態全部還原
    //   → 訓練場(同一把武器)→ 暫停可開關。
    // 武器清單只有 1 組時,執行期多建一組「重量級、180p」的測試組合,確保走的是「非預設組合」路徑。
    [AutoTest(Scene = MenuScene, Timeout = 180f, Order = 300)]
    public sealed class MenuFlowAutoTest : AutoTestBase
    {
        public const string MenuScene = "Assets/Scenes/MainMenu.unity";

        private readonly List<string> _heard = new List<string>();

        public static void Run()
        {
            Launch<MenuFlowAutoTest>();
        }

        private void Awake()
        {
            // 跨場景(主選單 → 對戰 → 主選單 → 訓練場)
            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            GameAudio.Played += OnPlayed;
        }

        private void OnDisable()
        {
            GameAudio.Played -= OnPlayed;
        }

        private void OnPlayed(string id)
        {
            _heard.Add(id);
        }

        private int Heard(string id)
        {
            int n = 0;
            foreach (string h in _heard)
            {
                if (h == id)
                {
                    n++;
                }
            }
            return n;
        }

        private static GameObject Selected => EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;

        private static string SelectedName => Selected != null ? Selected.name : "(無)";

        // Enter / A:submit 事件送到目前選取的元件
        private static void Submit()
        {
            GameObject go = Selected;
            if (go != null)
            {
                ExecuteEvents.Execute(go, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
            }
        }

        // 方向鍵 / 搖桿:move 事件
        private static void Move(MoveDirection direction)
        {
            GameObject go = Selected;
            if (go != null)
            {
                var data = new AxisEventData(EventSystem.current) { moveDir = direction };
                ExecuteEvents.Execute(go, data, ExecuteEvents.moveHandler);
            }
        }

        // 暫停時 Time.time 不走:一律用真實時間等待
        private static IEnumerator WaitReal(Func<bool> condition, float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < end)
            {
                yield return null;
            }
        }

        private static IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return null;
            }
        }

        private IEnumerator Start()
        {
            GameSettings.Snapshot settings = GameSettings.Capture();
            yield return WaitReal(() => MainMenuController.Instance != null && MainMenuController.Instance.Navigator != null
                                        && MainMenuController.Instance.Navigator.Current != null, 8f);
            MainMenuController menu = MainMenuController.Instance;
            if (menu == null || menu.Navigator == null)
            {
                FailSetup("沒有 MainMenuController(需在 MainMenu 場景)");
                yield break;
            }

            // 武器清單:至少兩組才能驗「非預設組合」
            WeaponCatalog catalog = WeaponCatalog.Load();
            if (catalog == null || catalog.Default == null || !catalog.Default.IsValid)
            {
                FailSetup("Resources/WeaponCatalog 沒有有效的武器組合");
                yield break;
            }
            WeaponKit target;
            if (catalog.Kits.Count >= 2 && catalog.Kits[1] != null && catalog.Kits[1].IsValid)
            {
                target = catalog.Kits[1];
            }
            else
            {
                target = CreateTestKit(catalog.Default);
                var runtimeCatalog = ScriptableObject.CreateInstance<WeaponCatalog>();
                runtimeCatalog.hideFlags = HideFlags.DontUnloadUnusedAsset;
                runtimeCatalog.SetKits(new List<WeaponKit> { catalog.Default, target });
                MainMenuController.CatalogOverride = runtimeCatalog;
            }

            // ── 1 標題 ──
            yield return WaitReal(() => GameAudio.CurrentMusicId == "menu_theme", 3f);
            Check("主選單場景是 Build Settings 第一個", SceneUtility.GetBuildIndexByScenePath(MenuScene) == 0,
                $"buildIndex={SceneUtility.GetBuildIndexByScenePath(MenuScene)}");
            Check("開場是標題畫面、播選單曲", menu.Navigator.Current == menu.Title && GameAudio.CurrentMusicId == "menu_theme",
                $"page={menu.Navigator.Current?.GetType().Name} music={GameAudio.CurrentMusicId ?? "(無)"}");
            yield return new WaitForSecondsRealtime(0.4f);
            Submit();
            yield return Frames(3);
            Check("按任意鍵 → 主選單,預設選「對戰」", menu.Navigator.Current == menu.Home && Selected == menu.Home.BattleButton.gameObject,
                $"page={menu.Navigator.Current?.GetType().Name} selected={SelectedName}");

            // ── 2 模式 ──
            Submit();
            yield return Frames(3);
            bool onModes = menu.Navigator.Current == menu.Modes && Selected == menu.Modes.Cards[0].gameObject;
            // 塗地 → 右:真格區域(M1 起開放)→ 右:真格塔樓(尚未實作,鎖住)
            Move(MoveDirection.Right);
            yield return Frames(3);
            GameObject zonesCard = Selected;
            bool zonesOpen = zonesCard != null && zonesCard == menu.Modes.Cards[1].gameObject && !menu.Modes.Cards[1].Locked;
            Move(MoveDirection.Right);
            yield return Frames(3);
            GameObject lockedCard = Selected;
            bool lockedSelected = lockedCard != null && lockedCard.GetComponent<MenuButton>() != null
                                                     && lockedCard.GetComponent<MenuButton>().Locked;
            int backBefore = Heard("ui_back");
            Submit();
            yield return Frames(3);
            bool stayed = menu.Navigator.Current == menu.Modes && Heard("ui_back") > backBefore;
            Check("模式:塗地、真格區域可選,未實作的蠻頹模式鎖住(按下只搖一下)", onModes && zonesOpen && lockedSelected && stayed,
                $"onModes={onModes} zonesOpen={zonesOpen}({(zonesCard != null ? zonesCard.name : "-")}) " +
                $"lockedSelected={lockedSelected}({(lockedCard != null ? lockedCard.name : "-")}) stayed={stayed}");
            Move(MoveDirection.Left);
            yield return Frames(3);
            Move(MoveDirection.Left);
            yield return Frames(3);
            Submit();
            yield return Frames(3);
            StageCatalog stages = StageCatalog.Load();
            Check("場地頁:卡片數 = StageCatalog", menu.Navigator.Current == menu.Stages && stages != null
                                                  && menu.Stages.Grid.Cards.Count == stages.Stages.Count && stages.Stages.Count > 0,
                $"page={menu.Navigator.Current?.GetType().Name} cards={menu.Stages.Grid.Cards.Count} catalog={stages?.Stages.Count}");
            string stageScene = menu.SelectedStage.SceneName;

            // ── 3 武器 ──
            Submit();
            yield return Frames(3);
            IReadOnlyList<WeaponKit> kits = menu.Kits;
            Check("武器頁:卡片數 = 武器清單", menu.Navigator.Current == menu.Weapons
                                              && menu.Weapons.Grid.Cards.Count == kits.Count && kits.Count >= 2,
                $"page={menu.Navigator.Current?.GetType().Name} cards={menu.Weapons.Grid.Cards.Count} kits={kits.Count}");
            int targetIndex = menu.Weapons.IndexOfVisible(target);
            for (int i = 0; i < targetIndex; i++)
            {
                Move(MoveDirection.Right);
                yield return Frames(2);
            }
            yield return Frames(2);
            string expectedPoints = $"{Mathf.RoundToInt(target.SpecialPoints)}p";
            Check("武器詳細面板跟著選取更新(名稱 / 副武器 / 大招 / 點數 / 重量)",
                Selected == menu.Weapons.Grid.Cards[targetIndex].gameObject
                && menu.Weapons.DetailName.text == target.DisplayName
                && menu.Weapons.DetailSub.text == target.Sub.DisplayName
                && menu.Weapons.DetailSpecial.text == target.Special.SpecialName
                && menu.Weapons.DetailPoints.text == expectedPoints
                && menu.Weapons.DetailWeight.text == MenuTheme.WeightName(target.Weight),
                $"selected={SelectedName} name={menu.Weapons.DetailName.text} sub={menu.Weapons.DetailSub.text} " +
                $"special={menu.Weapons.DetailSpecial.text} points={menu.Weapons.DetailPoints.text} weight={menu.Weapons.DetailWeight.text}");
            Submit();
            yield return Frames(3);

            // ── 4 對戰準備 ──
            MatchSetupPage setup = menu.Setup;
            bool onSetup = menu.Navigator.Current == setup && Selected == setup.StartButton.gameObject;
            Move(MoveDirection.Up);
            yield return Frames(2);
            Move(MoveDirection.Up);
            yield return Frames(2);
            bool onDifficulty = Selected == setup.DifficultyStepper.gameObject;
            int difficultyFrom = setup.DifficultyStepper.Index;
            // 從目前難度往右推到底 = 困難
            for (int i = 0; i < 3; i++)
            {
                Move(MoveDirection.Right);
                yield return Frames(1);
            }
            Move(MoveDirection.Down);
            yield return Frames(2);
            Move(MoveDirection.Left);
            yield return Frames(2);
            int teamSize = menu.TeamSize;
            Move(MoveDirection.Down);
            yield return Frames(2);
            Check("對戰準備:方向鍵調難度(困難)與人數(少一人)",
                onSetup && onDifficulty && menu.Difficulty == BotBrain.Difficulty.Hard && teamSize == 3
                && Selected == setup.StartButton.gameObject,
                $"onSetup={onSetup} onDifficulty={onDifficulty} 難度 {difficultyFrom}→{menu.Difficulty} 人數={teamSize} selected={SelectedName}");
            Submit();
            yield return Frames(1);
            Check("開始 → MatchSettings 寫入、墨水轉場", SceneTransition.Busy && MatchSettings.LocalKit == target
                                                     && MatchSettings.TeamSize == 3
                                                     && MatchSettings.BotDifficulty == BotBrain.Difficulty.Hard
                                                     && MatchSettings.StageScene == stageScene,
                $"busy={SceneTransition.Busy} kit={MatchSettings.LocalKit?.Id} team={MatchSettings.TeamSize} " +
                $"bot={MatchSettings.BotDifficulty} stage={MatchSettings.StageScene}");

            // ── 5 對戰場景 ──
            yield return WaitReal(() => !SceneTransition.Busy && MatchController.Instance != null
                                        && MatchController.Instance.LocalPlayer != null, 40f);
            MatchController match = MatchController.Instance;
            if (match == null || match.LocalPlayer == null)
            {
                FailSetup($"對戰場景沒有載入(目前 {SceneManager.GetActiveScene().name})");
                Cleanup(settings);
                yield break;
            }
            Inkling local = match.LocalPlayer;
            var loadout = local.GetComponent<WeaponLoadout>();
            var gauge = local.GetComponent<PlayerSpecialGauge>();
            Check("載入所選場地", SceneManager.GetActiveScene().name == stageScene, SceneManager.GetActiveScene().name);
            Check("本機玩家的武器 = 選單所選(非預設組合,大招點數跟著換)",
                loadout != null && loadout.Kit == target && gauge != null && Mathf.Abs(gauge.CostPoints - target.SpecialPoints) < 0.01f
                && target != catalog.Default,
                $"kit={loadout?.Kit?.Id} 預期={target.Id} 大招點數={gauge?.CostPoints} 預期={target.SpecialPoints}");
            yield return WaitReal(() => BotDirector.Instance != null && BotDirector.Instance.Bots.Count > 0, 5f);
            bool allHard = BotDirector.Instance != null && BotDirector.Instance.Bots.Count == 5;
            if (BotDirector.Instance != null)
            {
                foreach (BotBrain bot in BotDirector.Instance.Bots)
                {
                    allHard &= bot != null && bot.Level == BotBrain.Difficulty.Hard;
                }
            }
            Check("bot 難度 = 困難、人數 3 對 3", allHard && match.Roster.Count == 6 && match.TeamSize == 3,
                $"bots={BotDirector.Instance?.Bots.Count} allHard={allHard} roster={match.Roster.Count} teamSize={match.TeamSize}");

            // ── 6 暫停 ──
            yield return WaitReal(() => match.Phase == MatchPhase.Battle, 20f);
            PauseMenu pause = PauseMenu.Instance;
            var router = local.GetComponent<PlayerInputRouter>();
            ScriptedIntent intent = ScriptedIntent.Drive(local);
            intent.Attack = true;
            intent.Move = new Vector2(0f, 1f);
            intent.Look = new Vector2(1f, 0f);
            yield return Frames(3);
            bool actingBefore = router.AttackHeld && router.MoveInput.sqrMagnitude > 0.5f;
            if (pause == null)
            {
                FailSetup("對戰場景沒有暫停選單(SessionFlow 沒掛上)");
                Cleanup(settings);
                yield break;
            }
            pause.Open();
            yield return null;
            float frozenRemaining = match.Timeline.BattleRemaining;
            Vector3 frozenPosition = local.transform.position;
            yield return new WaitForSecondsRealtime(0.8f);
            bool locked = !router.AttackHeld && router.MoveInput == Vector2.zero && router.LookDeltaDeg == Vector2.zero;
            Check("暫停:timeScale 0、AudioListener.pause、角色輸入(含視角)全鎖",
                pause.IsOpen && Time.timeScale == 0f && AudioListener.pause && PlayerInputRouter.Paused && locked && actingBefore,
                $"open={pause.IsOpen} timeScale={Time.timeScale} listener={AudioListener.pause} paused={PlayerInputRouter.Paused} " +
                $"locked={locked} 暫停前在動={actingBefore}");
            Check("暫停:計時與角色凍結、音樂照播",
                Mathf.Approximately(match.Timeline.BattleRemaining, frozenRemaining)
                && (local.transform.position - frozenPosition).sqrMagnitude < 1e-6f
                && match.Phase == MatchPhase.Battle && GameAudio.CurrentMusicId == "battle_theme",
                $"剩餘 {frozenRemaining:0.00}→{match.Timeline.BattleRemaining:0.00} 位移 {(local.transform.position - frozenPosition).magnitude:0.0000} " +
                $"music={GameAudio.CurrentMusicId ?? "(無)"}");
            Check("暫停選單首頁,預設選「繼續」", pause.Navigator.Current == pause.Root && Selected == pause.Root.ResumeButton.gameObject,
                $"page={pause.Navigator.Current?.GetType().Name} selected={SelectedName}");

            // 暫停中開設定,改水平靈敏度 → 立即生效
            Move(MoveDirection.Down);
            yield return Frames(2);
            Submit();
            yield return Frames(3);
            bool onSettings = pause.Navigator.Current == pause.Settings && Selected == pause.Settings.MasterVolume.gameObject;
            for (int i = 0; i < 3; i++)
            {
                Move(MoveDirection.Down);
                yield return Frames(2);
            }
            bool onLookX = Selected == pause.Settings.LookX.gameObject;
            float lookBefore = GameSettings.LookSensitivityX;
            Move(MoveDirection.Right);
            yield return Frames(2);
            float expectedLevel = LookSensitivity.Clamp(lookBefore + LookSensitivity.Step);
            if (Mathf.Approximately(lookBefore, LookSensitivity.Max))
            {
                Move(MoveDirection.Left);
                yield return Frames(2);
                expectedLevel = LookSensitivity.Max - LookSensitivity.Step;
            }
            float expectedScale = LookSensitivity.Multiplier(expectedLevel);
            Check("暫停中的設定頁:水平靈敏度 +0.5 格立即套到視角輸入",
                onSettings && onLookX && Mathf.Approximately(GameSettings.LookSensitivityX, expectedLevel)
                && Mathf.Abs(PlayerInputRouter.LookScale.x - expectedScale) < 1e-4f,
                $"onSettings={onSettings} onLookX={onLookX} 級數 {lookBefore}→{GameSettings.LookSensitivityX} " +
                $"倍率={PlayerInputRouter.LookScale.x:0.000} 預期={expectedScale:0.000}");
            pause.Navigator.Back();
            yield return Frames(3);
            bool backToRoot = pause.Navigator.Current == pause.Root;
            Move(MoveDirection.Up);
            yield return Frames(2);
            bool onResume = Selected == pause.Root.ResumeButton.gameObject;
            Submit();
            yield return Frames(3);
            Check("繼續:時間/聲音恢復、角色輸入解鎖",
                backToRoot && onResume && !pause.IsOpen && Time.timeScale == 1f && !AudioListener.pause && !PlayerInputRouter.Paused
                && router.AttackHeld,
                $"backToRoot={backToRoot} onResume={onResume} open={pause.IsOpen} timeScale={Time.timeScale} " +
                $"listener={AudioListener.pause} paused={PlayerInputRouter.Paused} attack={router.AttackHeld}");
            yield return new WaitForSecondsRealtime(0.3f);
            Check("繼續後計時照走", match.Timeline.BattleRemaining < frozenRemaining - 0.1f,
                $"剩餘 {frozenRemaining:0.00}→{match.Timeline.BattleRemaining:0.00}");
            intent.Attack = false;
            intent.Move = Vector2.zero;
            intent.Look = Vector2.zero;

            // ── 7 暫停 → 回到主選單 ──
            pause.Open();
            yield return Frames(2);
            Move(MoveDirection.Down);
            yield return Frames(2);
            Move(MoveDirection.Down);
            yield return Frames(2);
            bool onQuit = Selected == pause.Root.QuitButton.gameObject;
            Submit();
            yield return Frames(3);
            bool confirmShown = pause.Navigator.Current == pause.Confirm && Selected == pause.Confirm.CancelButton.gameObject;
            Move(MoveDirection.Left);
            yield return Frames(2);
            Submit();
            yield return Frames(1);
            Check("回到主選單要確認(預設選「取消」)", onQuit && confirmShown && SceneTransition.Busy,
                $"onQuit={onQuit} confirm={confirmShown} busy={SceneTransition.Busy}");
            yield return WaitReal(() => !SceneTransition.Busy && MainMenuController.Instance != null
                                        && MainMenuController.Instance.Navigator != null
                                        && MainMenuController.Instance.Navigator.Current != null, 40f);
            menu = MainMenuController.Instance;
            if (menu == null)
            {
                FailSetup($"沒有回到主選單(目前 {SceneManager.GetActiveScene().name})");
                Cleanup(settings);
                yield break;
            }
            yield return WaitReal(() => GameAudio.CurrentMusicId == "menu_theme", 3f);
            Check("回到主選單:暫停狀態全還原、落在「對戰準備」且記得選擇",
                Time.timeScale == 1f && !AudioListener.pause && !PlayerInputRouter.Paused
                && menu.Navigator.Current == menu.Setup && menu.SelectedKit == target
                && menu.Difficulty == BotBrain.Difficulty.Hard && menu.TeamSize == 3 && GameAudio.CurrentMusicId == "menu_theme",
                $"timeScale={Time.timeScale} listener={AudioListener.pause} paused={PlayerInputRouter.Paused} " +
                $"page={menu.Navigator.Current?.GetType().Name} kit={menu.SelectedKit?.Id} 難度={menu.Difficulty} 人數={menu.TeamSize} " +
                $"music={GameAudio.CurrentMusicId ?? "(無)"}");

            // ── 8 再來一場 → 打到結算 → 結算確認(MatchHud 的 Enter/A 路徑)回主選單 ──
            Submit();
            yield return WaitReal(() => !SceneTransition.Busy && MatchController.Instance != null
                                        && MatchController.Instance.LocalPlayer != null, 40f);
            match = MatchController.Instance;
            if (match == null)
            {
                FailSetup("「對戰準備」預設的開始鈕沒有載入對戰");
                Cleanup(settings);
                yield break;
            }
            match.DebugSetRemaining(0.3f);
            yield return WaitReal(() => match.Phase >= MatchPhase.Judging, 10f);
            pause = PauseMenu.Instance;
            if (pause != null)
            {
                pause.Open();
            }
            bool blocked = pause != null && !pause.IsOpen && Time.timeScale == 1f;
            yield return WaitReal(() => match.Phase >= MatchPhase.Results, 15f);
            bool results = match.Phase == MatchPhase.Results;
            // MatchHud 在結算畫面按 Enter / A 做的事
            match.ConfirmResults();
            UI.SceneFlow.AfterMatch();
            yield return WaitReal(() => MainMenuController.Instance != null && MainMenuController.Instance.Navigator != null
                                        && MainMenuController.Instance.Navigator.Current != null, 30f);
            // 換頁的那一幀不收返回鍵(MenuNavigator 的防連按):等幾幀再操作
            yield return Frames(3);
            menu = MainMenuController.Instance;
            Check("再來一場 → 判定/結算不能暫停 → 結算確認回主選單的「對戰準備」",
                blocked && results && menu != null && menu.Navigator.Current == menu.Setup && menu.SelectedKit == target,
                $"判定中暫停被擋={blocked} 到結算={results} page={menu?.Navigator.Current?.GetType().Name} kit={menu?.SelectedKit?.Id}");
            if (menu == null)
            {
                Cleanup(settings);
                Finish();
                yield break;
            }

            // ── 9 訓練場 ──
            menu.Navigator.Back();
            yield return Frames(3);
            bool home = menu.Navigator.Current == menu.Home;
            Move(MoveDirection.Down);
            yield return Frames(2);
            bool onTraining = Selected == menu.Home.TrainingButton.gameObject;
            Submit();
            yield return Frames(3);
            bool weaponsForTraining = menu.Navigator.Current == menu.Weapons && menu.TrainingFlow
                                      && Selected == menu.Weapons.Grid.Cards[targetIndex].gameObject;
            Submit();
            yield return Frames(1);
            Check("訓練場:主選單 → 武器 → 直接進試射場", home && onTraining && weaponsForTraining && SceneTransition.Busy,
                $"home={home} onTraining={onTraining} weapons={weaponsForTraining} busy={SceneTransition.Busy}");
            yield return WaitReal(() => !SceneTransition.Busy && SceneManager.GetActiveScene().name == MainMenuController.TrainingScene
                                        && PauseMenu.Instance != null, 40f);
            GameObject trainee = GameObject.Find("Player");
            var trainingLoadout = trainee != null ? trainee.GetComponent<WeaponLoadout>() : null;
            Check("訓練場的玩家武器 = 選單所選", trainingLoadout != null && trainingLoadout.Kit == target,
                $"scene={SceneManager.GetActiveScene().name} kit={trainingLoadout?.Kit?.Id}");
            pause = PauseMenu.Instance;
            bool pausedTraining = false;
            if (pause != null)
            {
                pause.Open();
                yield return Frames(2);
                pausedTraining = pause.IsOpen && Time.timeScale == 0f && PlayerInputRouter.Paused;
                pause.Navigator.Back();
                yield return Frames(3);
            }
            Check("訓練場也能暫停(返回鍵 = 繼續)", pausedTraining && pause != null && !pause.IsOpen && Time.timeScale == 1f
                                                   && !PlayerInputRouter.Paused,
                $"paused={pausedTraining} open={pause?.IsOpen} timeScale={Time.timeScale} lock={PlayerInputRouter.Paused}");

            Check("介面音效:移動 / 確定 / 返回", Heard("ui_move") >= 5 && Heard("ui_select") >= 6 && Heard("ui_back") >= 2,
                $"ui_move={Heard("ui_move")} ui_select={Heard("ui_select")} ui_back={Heard("ui_back")}");
            Cleanup(settings);
            Finish();
        }

        private static WeaponKit CreateTestKit(WeaponKit basis)
        {
            var kit = ScriptableObject.CreateInstance<WeaponKit>();
            kit.hideFlags = HideFlags.DontUnloadUnusedAsset;
            kit.name = "Kit_menu_test_heavy";
            kit.Setup("menu_test_heavy", "測試用重型射擊槍", "Menu Test Heavy", "選單流程測試用(執行期建立,不在正式清單內)。",
                basis.Main, basis.Sub, basis.Special, 180f, WeightClass.Heavy, "射程", 30, "威力", 90, "射速", 20);
            return kit;
        }

        private static void Cleanup(GameSettings.Snapshot settings)
        {
            // PlayerPrefs 由所有工作樹共用:測試改過的設定放回去
            GameSettings.Restore(settings);
            MainMenuController.CatalogOverride = null;
        }
    }
}

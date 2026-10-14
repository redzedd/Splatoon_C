using SplatoonC.Core;
using SplatoonC.Core.Match;
using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Match;
using SplatoonC.Gameplay.Player;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SplatoonC.Gameplay.UI.Menu
{
    // 對戰與訓練場的暫停選單(Esc / 手把 +):繼續、設定、回到主選單。
    // S3 線上對戰不能暫停;這是離線 vs 電腦版,暫停 = Time.timeScale 0(計時、bot、物理全停,時間軸不會錯亂)
    // + AudioListener.pause(音效暫停;音樂音源 ignoreListenerPause,照常播放)+ 鎖住所有角色輸入(含視角)。
    // 關閉時若攻擊/確定鍵還按著,等放開才解鎖角色輸入(點「繼續」不會順便開一槍)。
    // 由 SessionFlow 在對戰/試射場景載入時自動掛上,不需要改場景。
    public sealed class PauseMenu : MonoBehaviour
    {
        // 放開按鍵才解鎖的最長等待(秒,unscaled)
        private const float ReleaseGuardSeconds = 0.6f;

        public static PauseMenu Instance { get; private set; }

        public bool IsOpen { get; private set; }
        public bool InMatch => MatchController.Instance != null;
        public MenuNavigator Navigator { get; private set; }
        public PauseRootPage Root { get; private set; }
        public SettingsPage Settings { get; private set; }
        public ConfirmDialog Confirm { get; private set; }

        private Canvas _canvas;
        private MenuHintBar _hints;
        private float _savedTimeScale = 1f;
        private CursorLockMode _savedLock;
        private bool _savedCursorVisible;
        private bool _releaseGuard;
        private float _guardUntil;
        private bool _leaving;

        public string StageName
        {
            get
            {
                string scene = SceneManager.GetActiveScene().name;
                StageCatalog stages = StageCatalog.Load();
                return stages != null ? stages.DisplayNameOf(scene) : scene;
            }
        }

        public Inkling LocalPlayer
        {
            get
            {
                if (MatchController.Instance != null)
                {
                    return MatchController.Instance.LocalPlayer;
                }
                for (int i = 0; i < Inkling.All.Count; i++)
                {
                    if (Inkling.All[i] != null && Inkling.All[i].IsLocalPlayer)
                    {
                        return Inkling.All[i];
                    }
                }
                return null;
            }
        }

        public WeaponKit LocalKit
        {
            get
            {
                Inkling player = LocalPlayer;
                var loadout = player != null ? player.GetComponent<WeaponLoadout>() : null;
                return loadout != null ? loadout.Kit : null;
            }
        }

        public Team LocalTeam
        {
            get
            {
                Inkling player = LocalPlayer;
                return player != null ? player.Team : Team.Alpha;
            }
        }

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            // 場景卸載時還開著:把全域狀態還原(不然主選單會一直是 timeScale 0)
            if (IsOpen || _releaseGuard)
            {
                Time.timeScale = _savedTimeScale > 0f ? _savedTimeScale : 1f;
                AudioListener.pause = false;
                PlayerInputRouter.Paused = false;
            }
            if (Instance == this)
            {
                Instance = null;
            }
        }

        // 現在能不能暫停:轉場中、判定/結算之後(結算畫面有自己的確認鍵)不行
        public bool CanPause
        {
            get
            {
                if (SceneTransition.Busy || _leaving || LocalPlayer == null)
                {
                    return false;
                }
                MatchController match = MatchController.Instance;
                return match == null || match.Phase < MatchPhase.Judging;
            }
        }

        private void Update()
        {
            if (_releaseGuard && (Time.unscaledTime > _guardUntil || !MenuInput.AnyGameplayButtonHeld()))
            {
                _releaseGuard = false;
                PlayerInputRouter.Paused = false;
            }
            if (!IsOpen)
            {
                if (MenuInput.PausePressed() && CanPause)
                {
                    Open();
                }
                return;
            }
            if (_leaving)
            {
                return;
            }
            // 手把 + 在任何一頁都直接回到遊戲;Esc 走返回(設定頁 → 暫停首頁 → 繼續)
            if (MenuInput.StartButtonPressed())
            {
                Resume();
                return;
            }
            Navigator.Update();
        }

        public void Open()
        {
            if (IsOpen || !CanPause)
            {
                return;
            }
            EnsureBuilt();
            MenuInput.EnsureEventSystem();
            IsOpen = true;
            _releaseGuard = false;
            _savedTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
            Time.timeScale = 0f;
            AudioListener.pause = true;
            PlayerInputRouter.Paused = true;
            _savedLock = Cursor.lockState;
            _savedCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _canvas.gameObject.SetActive(true);
            Navigator.ResetTo(Root);
            GameAudio.Play2D("ui_select");
        }

        public void Resume()
        {
            if (!IsOpen || _leaving)
            {
                return;
            }
            IsOpen = false;
            Navigator.Clear();
            _canvas.gameObject.SetActive(false);
            Time.timeScale = _savedTimeScale;
            AudioListener.pause = false;
            Cursor.lockState = _savedLock;
            Cursor.visible = _savedCursorVisible;
            GameSettings.Save();
            GameAudio.Play2D("ui_back");
            // 角色輸入等按鍵放開才解鎖
            _releaseGuard = true;
            _guardUntil = Time.unscaledTime + ReleaseGuardSeconds;
        }

        public void RequestReturnToMenu()
        {
            if (InMatch)
            {
                Confirm.Show(Navigator, "放棄這場對戰,回到主選單?", "回主選單", ReturnToMenu);
            }
            else
            {
                ReturnToMenu();
            }
        }

        public void ReturnToMenu()
        {
            if (_leaving)
            {
                return;
            }
            // 轉場蓋住畫面前維持暫停;SceneTransition 載入前會把 timeScale/聲音/輸入鎖還原
            if (SceneTransition.Load(SceneFlow.MenuScene, "主選單", MenuTheme.Accent))
            {
                _leaving = true;
                GameSettings.Save();
                return;
            }
            Debug.LogError("PauseMenu:主選單場景不在 Build Settings(先跑 Tools/SplatoonC/Build Main Menu)");
        }

        private void EnsureBuilt()
        {
            if (_canvas != null)
            {
                return;
            }
            _canvas = Ui.CreateCanvas("PauseCanvas", 60);
            _canvas.transform.SetParent(transform, false);
            _canvas.gameObject.AddComponent<GraphicRaycaster>();
            RectTransform pages = Ui.Stretch("Pages", _canvas.transform);
            _hints = new MenuHintBar(_canvas.transform);
            Navigator = new MenuNavigator();
            Navigator.PageChanged += page => _hints.Set(page.Hints);
            Root = new PauseRootPage(this);
            Root.Create(pages, Navigator);
            Settings = new SettingsPage(true);
            Settings.Create(pages, Navigator);
            Confirm = new ConfirmDialog();
            Confirm.Create(pages, Navigator);
            _canvas.gameObject.SetActive(false);
        }
    }
}

using SplatoonC.Core.Menu;
using UnityEngine;
using UnityEngine.UI;

namespace SplatoonC.Gameplay.UI.Menu
{
    // 設定(主選單與暫停選單共用):音量三條、視角靈敏度水平/垂直(S3 -5 ~ +5,每格 0.5)、反轉上下/左右、色彩鎖定、恢復預設。
    // 每一格改動立即生效(音量即時、視角下一幀、色彩鎖定下一場);離開本頁時寫入 PlayerPrefs。
    public sealed class SettingsPage : MenuPage
    {
        private const int VolumeSteps = 10;
        private static readonly string[] OnOff = { "關", "開" };

        private readonly bool _inPause;
        private MenuStepper _master;
        private MenuStepper _music;
        private MenuStepper _sfx;
        private MenuStepper _lookX;
        private MenuStepper _lookY;
        private MenuStepper _invertY;
        private MenuStepper _invertX;
        private MenuStepper _colorLock;
        private MenuButton _reset;
        private Text _description;

        public SettingsPage(bool inPause)
        {
            _inPause = inPause;
        }

        public MenuStepper MasterVolume => _master;
        public MenuStepper LookX => _lookX;
        public MenuStepper LookY => _lookY;
        public MenuStepper InvertY => _invertY;

        public override string Hints => "↑↓:選擇|← →:調整|Esc / B:返回";
        public override Selectable DefaultSelection => _master;

        protected override void Build(RectTransform root)
        {
            if (_inPause)
            {
                // 暫停中疊在遊戲畫面上:整頁加一層深色底
                Image shade = Ui.Image("Shade", root, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, null,
                    new Color(0.03f, 0.02f, 0.06f, 0.92f));
                shade.rectTransform.anchorMin = Vector2.zero;
                shade.rectTransform.anchorMax = Vector2.one;
                shade.rectTransform.sizeDelta = Vector2.zero;
            }
            MenuTheme.TapeTitle("Title", root, new Vector2(0f, 1f), new Vector2(90f, -60f), "設定", 56, MenuTheme.Accent);

            RectTransform list = Ui.Rect("List", root, new Vector2(0.5f, 1f), new Vector2(0f, -180f), new Vector2(1120f, 760f));
            const float w = 1120f;
            const float h = 70f;
            const float step = 78f;
            float y = 0f;
            _master = Bar(list, "Master", "主音量", y, w, h,
                "整體音量(音樂 × 音效)。");
            y += step;
            _music = Bar(list, "Music", "音樂音量", y, w, h, "主選單、對戰曲與最後一分鐘曲的音量。");
            y += step;
            _sfx = Bar(list, "Sfx", "音效音量", y, w, h, "射擊、爆炸、腳步、介面音效的音量。");
            y += step + 8f;
            _lookX = LookBar(list, "LookX", "水平靈敏度", y, w, h,
                "左右轉動視角的速度(S3:-5 ~ +5)。每 +2.5 級約快一倍,滑鼠與手把都適用。");
            y += step;
            _lookY = LookBar(list, "LookY", "垂直靈敏度", y, w, h, "上下轉動視角的速度(S3:-5 ~ +5)。");
            y += step + 8f;
            _invertY = Toggle(list, "InvertY", "反轉上下", y, w, h, "開:推上 = 往下看(飛行模擬式)。");
            y += step;
            _invertX = Toggle(list, "InvertX", "反轉左右", y, w, h, "開:往右推 = 視角往左轉。");
            y += step;
            _colorLock = Toggle(list, "ColorLock", "色彩鎖定", y, w, h,
                "S3 的色覺友善選項:每場固定使用黃 vs 藍紫配色,不再隨機。下一場開始生效。");
            y += step + 8f;
            _reset = MenuWidgets.ListButton("Reset", list, 0f, y, 360f, 64f, "恢復預設", "", null, 30);

            _description = MenuTheme.Paragraph("Description", root, new Vector2(0.5f, 0f), new Vector2(0f, 88f),
                new Vector2(1120f, 70f), 26, MenuTheme.TextDim);
            _description.rectTransform.pivot = new Vector2(0.5f, 0f);
            _description.alignment = TextAnchor.MiddleCenter;
            _reset.Focused += () => _description.text = "音量、靈敏度、反轉與色彩鎖定全部恢復成預設值。";

            _master.Changed += i => GameSettings.MasterVolume = MenuMath.StepToVolume(i, VolumeSteps);
            _music.Changed += i => GameSettings.MusicVolume = MenuMath.StepToVolume(i, VolumeSteps);
            _sfx.Changed += i => GameSettings.SfxVolume = MenuMath.StepToVolume(i, VolumeSteps);
            _lookX.Changed += i => GameSettings.LookSensitivityX = LookSensitivity.LevelOfIndex(i);
            _lookY.Changed += i => GameSettings.LookSensitivityY = LookSensitivity.LevelOfIndex(i);
            _invertY.Changed += i => GameSettings.InvertY = i == 1;
            _invertX.Changed += i => GameSettings.InvertX = i == 1;
            _colorLock.Changed += i => GameSettings.ColorLock = i == 1;
            _reset.Activated += () =>
            {
                GameSettings.ResetToDefaults();
                Sync();
            };

            MenuWidgets.LinkVertical(new Selectable[]
            {
                _master, _music, _sfx, _lookX, _lookY, _invertY, _invertX, _colorLock, _reset,
            }, true);
        }

        private MenuStepper Bar(Transform list, string name, string label, float y, float w, float h, string description)
        {
            MenuStepper stepper = MenuWidgets.BarOption(name, list, 0f, y, w, h, label, 420f);
            stepper.Setup(VolumeSteps + 1, 0, i => $"{i * 100 / VolumeSteps}%");
            Describe(stepper, description);
            return stepper;
        }

        private MenuStepper LookBar(Transform list, string name, string label, float y, float w, float h, string description)
        {
            MenuStepper stepper = MenuWidgets.BarOption(name, list, 0f, y, w, h, label, 420f);
            stepper.Setup(LookSensitivity.StepCount, LookSensitivity.IndexOfLevel(0f),
                i => LookSensitivity.Format(LookSensitivity.LevelOfIndex(i)));
            Describe(stepper, description);
            return stepper;
        }

        private MenuStepper Toggle(Transform list, string name, string label, float y, float w, float h, string description)
        {
            MenuStepper stepper = MenuWidgets.Option(name, list, 0f, y, w, h, label, 160f);
            stepper.Setup(2, 0, i => OnOff[i]);
            Describe(stepper, description);
            return stepper;
        }

        private void Describe(MenuStepper stepper, string description)
        {
            stepper.Description = description;
            stepper.Focused += () => _description.text = stepper.Description;
        }

        public override void OnShow()
        {
            Sync();
            _description.text = _master.Description;
        }

        // 從目前設定同步顯示(不觸發 Changed)
        private void Sync()
        {
            _master.SetIndex(MenuMath.VolumeToStep(GameSettings.MasterVolume, VolumeSteps), false);
            _music.SetIndex(MenuMath.VolumeToStep(GameSettings.MusicVolume, VolumeSteps), false);
            _sfx.SetIndex(MenuMath.VolumeToStep(GameSettings.SfxVolume, VolumeSteps), false);
            _lookX.SetIndex(LookSensitivity.IndexOfLevel(GameSettings.LookSensitivityX), false);
            _lookY.SetIndex(LookSensitivity.IndexOfLevel(GameSettings.LookSensitivityY), false);
            _invertY.SetIndex(GameSettings.InvertY ? 1 : 0, false);
            _invertX.SetIndex(GameSettings.InvertX ? 1 : 0, false);
            _colorLock.SetIndex(GameSettings.ColorLock ? 1 : 0, false);
        }

        public override void OnHide()
        {
            GameSettings.Save();
        }
    }
}

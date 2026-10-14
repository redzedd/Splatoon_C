using SplatoonC.Gameplay.Player;
using UnityEngine;
using UnityEngine.UI;

namespace SplatoonC.Gameplay.Scoring
{
    // 大招能量條 HUD(仿 InkHud:fill 程式控寬)+ 狀態文字:滿格閃「大招 READY(R)」、啟動中「龍捲風 剩餘 N」。
    // 字串只在狀態/剩餘數變動時重建;閃爍只改顏色 alpha。
    public sealed class SpecialHud : MonoBehaviour
    {
        [SerializeField, Tooltip("大招能量(Player 上的 PlayerSpecialGauge)")]
        private PlayerSpecialGauge _gauge;

        [SerializeField, Tooltip("能量條 fill(HudCanvas/SpecialBarBG/SpecialBarFill)")]
        private RectTransform _fill;

        [SerializeField, Tooltip("狀態文字(HudCanvas/SpecialReadyText)")]
        private Text _readyText;

        [SerializeField, Tooltip("滿格寬度(像素)")]
        private float _fullWidth = 240f;

        [SerializeField, Tooltip("字型名稱(OS 動態字型)")]
        private string _fontName = "Microsoft JhengHei";

        [SerializeField, Tooltip("字級")]
        private int _fontSize = 20;

        [SerializeField, Tooltip("READY 閃爍週期(秒)")]
        private float _blinkPeriod = 0.8f;

        private const int StateCharging = 0;
        private const int StateReady = 1;
        private const int StateActive = 2;

        private float _shownNormalized = -1f;
        private int _shownState = -1;
        private int _shownRemaining = -1;

        public int State { get; private set; } = -1;

        private void Awake()
        {
            if (_readyText != null)
            {
                _readyText.font = HudFont.Get(_fontName, _fontSize);
                _readyText.fontSize = _fontSize;
            }
        }

        private void Update()
        {
            if (_gauge == null)
            {
                return;
            }

            float normalized = _gauge.Normalized;
            if (_fill != null && Mathf.Abs(normalized - _shownNormalized) >= 0.002f)
            {
                _shownNormalized = normalized;
                Vector2 size = _fill.sizeDelta;
                size.x = _fullWidth * normalized;
                _fill.sizeDelta = size;
            }

            int state = _gauge.IsActive ? StateActive : (_gauge.IsFull ? StateReady : StateCharging);
            int remaining = _gauge.DevicesRemaining;
            State = state;
            if (_readyText == null)
            {
                return;
            }
            if (state != _shownState || (state == StateActive && remaining != _shownRemaining))
            {
                _shownState = state;
                _shownRemaining = remaining;
                switch (state)
                {
                    case StateReady:
                        _readyText.text = "大招 READY(R)";
                        break;
                    case StateActive:
                        // 三重龍捲風顯示剩餘信標;其他大招顯示名稱
                        _readyText.text = _gauge.Special is SplatoonC.Gameplay.Combat.TripleInkstrikeSpecial || _gauge.SpecialConfig == null
                            ? $"龍捲風 剩餘 {remaining}"
                            : $"{_gauge.SpecialConfig.SpecialName} 發動中";
                        break;
                    default:
                        _readyText.text = string.Empty;
                        break;
                }
                _readyText.enabled = state != StateCharging;
            }
            if (state == StateReady && _blinkPeriod > 0f)
            {
                Color color = _readyText.color;
                color.a = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.time * Mathf.PI / _blinkPeriod));
                _readyText.color = color;
            }
            else if (state == StateActive)
            {
                Color color = _readyText.color;
                color.a = 1f;
                _readyText.color = color;
            }
        }
    }
}

using UnityEngine;
using UnityEngine.UI;

namespace SplatoonC.Gameplay.Scoring
{
    // 佔地率 HUD(M1 決策:uGUI + 動態 OS 字型;UI Toolkit 評估留到 M1 之後)。
    // 只在計分版本變動時更新文字(0.5 秒一次的字串配置,非每幀)。
    public sealed class CoverageHud : MonoBehaviour
    {
        [SerializeField, Tooltip("計分來源")]
        private CoverageScorer _scorer;

        [SerializeField, Tooltip("顯示文字元件")]
        private Text _text;

        [SerializeField, Tooltip("動態字型名稱(OS 字型,需支援繁中)")]
        private string _fontName = "Microsoft JhengHei";

        [SerializeField, Tooltip("字型大小")]
        private int _fontSize = 28;

        private int _shownVersion = -1;

        private void Awake()
        {
            // 一律用 OS 動態字型:內建 LegacyRuntime 字型無繁中字元,會顯示方框。
            if (_text != null)
            {
                _text.font = Font.CreateDynamicFontFromOSFont(_fontName, _fontSize);
                _text.fontSize = _fontSize;
            }
        }

        private void Update()
        {
            if (_scorer == null || _text == null || _scorer.Version == _shownVersion)
            {
                return;
            }
            _shownVersion = _scorer.Version;
            _text.text = $"佔地 {_scorer.Latest.PaintedRatio * 100f:0.0}%";
        }
    }
}

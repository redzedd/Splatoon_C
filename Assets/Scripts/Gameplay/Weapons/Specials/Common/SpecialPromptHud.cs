using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.UI;
using UnityEngine;
using UnityEngine.UI;

namespace SplatoonC.Gameplay.Combat
{
    // 大招的畫面中央短提示(S3「Can't Use!」:Big Bubbler 在空中、懸崖邊、低天花板下按下時)。
    // 第一次用時自建 overlay canvas(不需要場景接線);只給本機玩家看。彈出 → 停留 → 淡出,零配置。
    public sealed class SpecialPromptHud : MonoBehaviour
    {
        private const float PopSeconds = 0.18f;

        private static SpecialPromptHud _instance;

        private Text _text;
        private float _shownAt = float.NegativeInfinity;
        private float _hideAt = float.NegativeInfinity;

        // 測試用:最近一次顯示的文字與次數
        public static string LastMessage { get; private set; }
        public static int ShowCount { get; private set; }
        public static bool IsVisible => _instance != null && Time.time < _instance._hideAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetOnLoad()
        {
            LastMessage = null;
            ShowCount = 0;
        }

        public static void Show(string message, float seconds = 1.1f, string sound = "special_cant_use")
        {
            if (_instance == null)
            {
                _instance = new GameObject("SpecialPromptHud").AddComponent<SpecialPromptHud>();
            }
            LastMessage = message;
            ShowCount++;
            _instance._text.text = message;
            _instance._shownAt = Time.time;
            _instance._hideAt = Time.time + seconds;
            _instance._text.enabled = true;
            if (!string.IsNullOrEmpty(sound))
            {
                GameAudio.Play2D(GameAudio.Has(sound) ? sound : "ui_back", 0.8f);
            }
        }

        private void Awake()
        {
            Canvas canvas = Ui.CreateCanvas("SpecialPromptCanvas", 40);
            canvas.transform.SetParent(transform, false);
            _text = Ui.Text("Prompt", canvas.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, -120f),
                new Vector2(640f, 90f), 54, TextAnchor.MiddleCenter, new Color(1f, 0.95f, 0.35f), true, 3f);
            _text.enabled = false;
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void Update()
        {
            if (!_text.enabled)
            {
                return;
            }
            float now = Time.time;
            if (now >= _hideAt)
            {
                _text.enabled = false;
                return;
            }
            float age = now - _shownAt;
            float scale = age < PopSeconds ? Ui.PopScale(age / PopSeconds) : 1f;
            _text.rectTransform.localScale = Vector3.one * scale;
            Ui.SetAlpha(_text, Mathf.Clamp01((_hideAt - now) / 0.25f));
        }
    }
}

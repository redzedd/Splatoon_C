using System.Collections;
using SplatoonC.Gameplay.Player;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SplatoonC.Gameplay.UI.Menu
{
    // 換場景的墨水轉場(跨場景常駐):墨漬從中央炸開蓋滿畫面 → 非同步載入 → 新場景跑兩幀後墨漬縮回。
    // 蓋住期間顯示轉圈的烏賊與目的地名稱(S3 的載入畫面)。全部用 unscaled 時間;開始前把暫停狀態清掉。
    public sealed class SceneTransition : MonoBehaviour
    {
        private const float CoverSeconds = 0.38f;
        private const float RevealSeconds = 0.42f;

        public static bool Busy { get; private set; }

        // 最近一次載入的場景名(測試用)
        public static string LastTarget { get; private set; }

        private string _scene;
        private string _label;
        private Color _color;
        private RectTransform _splat;
        private Image _shade;
        private RectTransform _loading;
        private RectTransform _spinner;
        private Text _labelText;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Busy = false;
            LastTarget = null;
        }

        // 載入 scene(必須在 Build Settings 內);label = 載入畫面上的目的地名稱
        public static bool Load(string scene, string label, Color inkColor)
        {
            if (Busy)
            {
                return false;
            }
            if (!Application.CanStreamedLevelBeLoaded(scene))
            {
                Debug.LogError($"SceneTransition:場景 {scene} 不在 Build Settings(先跑 Tools/SplatoonC/Build Main Menu)");
                return false;
            }
            Busy = true;
            LastTarget = scene;
            var go = new GameObject("SceneTransition");
            DontDestroyOnLoad(go);
            var transition = go.AddComponent<SceneTransition>();
            transition._scene = scene;
            transition._label = label;
            transition._color = inkColor;
            return true;
        }

        private IEnumerator Start()
        {
            Build();
            yield return Animate(true);
            // 載入前恢復正常時間與聲音(從暫停選單回主選單時 timeScale 是 0)
            Time.timeScale = 1f;
            AudioListener.pause = false;
            PlayerInputRouter.Paused = false;
            AsyncOperation load = SceneManager.LoadSceneAsync(_scene);
            while (load != null && !load.isDone)
            {
                _spinner.localRotation = Quaternion.Euler(0f, 0f, -Time.unscaledTime * 360f);
                yield return null;
            }
            // 新場景的 Awake/Start 跑完再掀開
            yield return null;
            yield return null;
            yield return Animate(false);
            Busy = false;
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            Busy = false;
        }

        private void Build()
        {
            Canvas canvas = Ui.CreateCanvas("TransitionCanvas", 1000);
            canvas.transform.SetParent(transform, false);
            // 轉場中擋住所有點擊
            canvas.gameObject.AddComponent<GraphicRaycaster>();
            Transform root = canvas.transform;
            _shade = Ui.Image("Shade", root, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, null,
                new Color(0.04f, 0.03f, 0.07f, 0f));
            _shade.rectTransform.anchorMin = Vector2.zero;
            _shade.rectTransform.anchorMax = Vector2.one;
            _shade.rectTransform.sizeDelta = Vector2.zero;
            _shade.raycastTarget = true;
            Image splat = Ui.Image("Splat", root, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1400f, 1400f),
                UiSprites.Splat(256, 5), _color);
            _splat = splat.rectTransform;
            _splat.localScale = Vector3.zero;

            _loading = Ui.Rect("Loading", root, new Vector2(1f, 0f), new Vector2(-80f, 70f), new Vector2(560f, 120f));
            Image squid = Ui.Image("Spinner", _loading, new Vector2(1f, 0.5f), Vector2.zero, new Vector2(92f, 92f),
                UiSprites.SquidHead(), Color.white);
            squid.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            squid.rectTransform.anchoredPosition = new Vector2(-46f, 0f);
            _spinner = squid.rectTransform;
            _labelText = MenuTheme.Label("Label", _loading, new Vector2(1f, 0.5f), new Vector2(-116f, 14f),
                new Vector2(440f, 56f), 40, TextAnchor.MiddleRight, Color.white, 3f);
            _labelText.text = _label;
            Text sub = MenuTheme.Label("Sub", _loading, new Vector2(1f, 0.5f), new Vector2(-118f, -30f), new Vector2(440f, 36f),
                24, TextAnchor.MiddleRight, MenuTheme.TextDim, 2f);
            sub.text = "載入中…";
            _loading.gameObject.SetActive(false);
        }

        // cover = true:墨漬炸開蓋住;false:縮回露出新場景
        private IEnumerator Animate(bool cover)
        {
            float duration = cover ? CoverSeconds : RevealSeconds;
            float start = Time.unscaledTime;
            _loading.gameObject.SetActive(!cover);
            while (true)
            {
                float t = Mathf.Clamp01((Time.unscaledTime - start) / duration);
                float k = cover ? t : 1f - t;
                float eased = k * k * (3f - 2f * k);
                // 墨漬從中央放大到蓋過整個畫面(對角線 ≈ 2200 參考像素)
                _splat.localScale = Vector3.one * (eased * 2.4f);
                _splat.localRotation = Quaternion.Euler(0f, 0f, eased * 40f);
                _shade.color = new Color(0.04f, 0.03f, 0.07f, Mathf.Clamp01((eased - 0.55f) / 0.45f));
                if (!cover)
                {
                    Ui.SetAlpha(_labelText, 1f - t);
                    _loading.localScale = Vector3.one * (1f - t * 0.3f);
                }
                if (t >= 1f)
                {
                    break;
                }
                yield return null;
            }
            if (cover)
            {
                _loading.gameObject.SetActive(true);
                _loading.localScale = Vector3.one;
            }
        }
    }
}

using System.Globalization;
using SplatoonC.Gameplay.Combat;
using UnityEngine;
using UnityEngine.UI;

namespace SplatoonC.Gameplay.Scoring
{
    // 假人頭頂的累計傷害數字:螢幕空間 uGUI Text,每幀跟著假人的 PopupAnchor 投影位置走。
    // 文字只在假人的 PopupVersion 變動時重建(一次擊中一次字串配置,不是每幀);
    // 擊殺:字級放大、變紅;每次變動做一次縮放 punch。掛在 HudCanvas 上,slot 於 Awake 一次建好。
    public sealed class DamagePopupHud : MonoBehaviour
    {
        [SerializeField, Tooltip("同時可顯示的數字上限(每個假人一個)")]
        private int _poolSize = 8;

        [SerializeField, Tooltip("一般傷害字級")]
        private int _fontSize = 26;

        [SerializeField, Tooltip("擊殺字級(放大)")]
        private int _killFontSize = 40;

        [SerializeField, Tooltip("一般傷害顏色")]
        private Color _color = Color.white;

        [SerializeField, Tooltip("擊殺顏色")]
        private Color _killColor = new Color(1f, 0.2f, 0.15f, 1f);

        [SerializeField, Tooltip("數字變動時的縮放 punch 起始倍率")]
        private float _punchScale = 1.35f;

        [SerializeField, Tooltip("punch 回到 1 的秒數")]
        private float _punchDuration = 0.12f;

        [SerializeField, Tooltip("數字相對錨點的螢幕位移(像素)")]
        private Vector2 _screenOffset = new Vector2(0f, 8f);

        [SerializeField, Tooltip("字型名稱(OS 動態字型;內建字型無繁中)")]
        private string _fontName = "Microsoft JhengHei";

        private sealed class Slot
        {
            public TargetDummy Dummy;
            public Text Text;
            public RectTransform Rect;
            public int ShownVersion = -1;
            public float PunchStart = -10f;
        }

        private Slot[] _slots;
        private RectTransform _canvasRect;
        private Camera _camera;

        public static DamagePopupHud Instance { get; private set; }

        public int KillFontSize => _killFontSize;
        public int FontSize => _fontSize;
        public Color KillColor => _killColor;
        public Color NormalColor => _color;

        private void Awake()
        {
            Instance = this;
            _canvasRect = GetComponent<RectTransform>();
            Font font = HudFont.Get(_fontName, _killFontSize);
            _slots = new Slot[Mathf.Max(1, _poolSize)];
            for (int i = 0; i < _slots.Length; i++)
            {
                var go = new GameObject($"DamagePopup{i}", typeof(RectTransform), typeof(Text));
                go.layer = gameObject.layer;
                var rect = go.GetComponent<RectTransform>();
                rect.SetParent(transform, false);
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0f);
                rect.sizeDelta = new Vector2(240f, 60f);
                var text = go.GetComponent<Text>();
                text.font = font;
                text.fontSize = _fontSize;
                text.alignment = TextAnchor.LowerCenter;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                text.verticalOverflow = VerticalWrapMode.Overflow;
                text.raycastTarget = false;
                text.color = _color;
                go.SetActive(false);
                _slots[i] = new Slot { Text = text, Rect = rect };
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void LateUpdate()
        {
            if (_camera == null)
            {
                _camera = Camera.main;
                if (_camera == null)
                {
                    return;
                }
            }

            // 1) 該顯示的假人都要有 slot
            var active = DamageableRegistry.Active;
            for (int i = 0; i < active.Count; i++)
            {
                var dummy = active[i] as TargetDummy;
                if (dummy == null || !dummy.PopupVisible)
                {
                    continue;
                }
                if (FindSlot(dummy) == null)
                {
                    Slot free = FindSlot(null);
                    if (free != null)
                    {
                        free.Dummy = dummy;
                        free.ShownVersion = -1;
                    }
                }
            }

            // 2) 更新每個 slot:內容(版本變才重建)、位置(每幀)、punch
            for (int i = 0; i < _slots.Length; i++)
            {
                Slot slot = _slots[i];
                if (slot.Dummy == null)
                {
                    continue;
                }
                if (!slot.Dummy.isActiveAndEnabled || !slot.Dummy.PopupVisible)
                {
                    slot.Dummy = null;
                    slot.Text.gameObject.SetActive(false);
                    continue;
                }
                if (slot.ShownVersion != slot.Dummy.PopupVersion)
                {
                    slot.ShownVersion = slot.Dummy.PopupVersion;
                    bool kill = slot.Dummy.PopupIsKill;
                    slot.Text.text = slot.Dummy.PopupTotal.ToString("0.0", CultureInfo.InvariantCulture);
                    slot.Text.fontSize = kill ? _killFontSize : _fontSize;
                    slot.Text.color = kill ? _killColor : _color;
                    slot.PunchStart = Time.time;
                }

                Vector3 screen = _camera.WorldToScreenPoint(slot.Dummy.PopupAnchor.position);
                if (screen.z <= 0f)
                {
                    slot.Text.gameObject.SetActive(false);
                    continue;
                }
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        _canvasRect, new Vector2(screen.x, screen.y), null, out Vector2 local))
                {
                    slot.Rect.anchoredPosition = local + _screenOffset;
                }
                float punchT = _punchDuration > 0f ? Mathf.Clamp01((Time.time - slot.PunchStart) / _punchDuration) : 1f;
                float scale = Mathf.Lerp(_punchScale, 1f, punchT);
                slot.Rect.localScale = new Vector3(scale, scale, 1f);
                if (!slot.Text.gameObject.activeSelf)
                {
                    slot.Text.gameObject.SetActive(true);
                }
            }
        }

        private Slot FindSlot(TargetDummy dummy)
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i].Dummy == dummy)
                {
                    return _slots[i];
                }
            }
            return null;
        }

        // 測試/除錯:讀某假人目前顯示的文字與樣式;沒在顯示回 false。
        public bool TryGetShown(TargetDummy dummy, out string text, out Color color, out int fontSize)
        {
            text = null;
            color = default;
            fontSize = 0;
            Slot slot = dummy != null ? FindSlot(dummy) : null;
            if (slot == null || !slot.Text.gameObject.activeSelf)
            {
                return false;
            }
            text = slot.Text.text;
            color = slot.Text.color;
            fontSize = slot.Text.fontSize;
            return true;
        }
    }
}

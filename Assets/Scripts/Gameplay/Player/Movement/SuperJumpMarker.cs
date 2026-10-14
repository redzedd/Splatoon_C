using SplatoonC.Core;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.UI;
using UnityEngine;
using UnityEngine.UI;

namespace SplatoonC.Gameplay.Player
{
    // 超級跳落點標記(S3:所有人都看得到 —— 敵人也會來守落點):地面上的隊伍色圈 + 懸在上方往下指的箭頭
    // (箭頭裡的墨水隨飛行時間流乾)+ 跳躍者名字。每個 SuperJump 一個實例(不掛在角色底下,角色隱形/陣亡不影響),
    // 由 MovementSetup 建的 prefab 產生;名字用世界空間 uGUI(OS 繁中字型,與 HUD 同一套)。
    // 子物件命名:Ink* = 隊伍色(MaterialPropertyBlock)。
    public sealed class SuperJumpMarker : MonoBehaviour
    {
        [SerializeField, Tooltip("地面圈(緩慢旋轉、脈動)")]
        private Transform _ring;

        [SerializeField, Tooltip("箭頭根(面向鏡頭、上下浮動)")]
        private Transform _arrow;

        [SerializeField, Tooltip("箭頭墨水的縮放樞紐(在箭頭尖端;y 縮放 = 剩餘墨水)")]
        private Transform _fillPivot;

        [SerializeField, Tooltip("名字的位置")]
        private Transform _labelAnchor;

        [SerializeField, Tooltip("箭頭浮在落點上方的高度(公尺)")]
        private float _arrowHeight = 3.2f;

        [SerializeField, Tooltip("浮動幅度(公尺)")]
        private float _bobAmplitude = 0.18f;

        private Renderer[] _inkRenderers;
        private Canvas _canvas;
        private Text _label;
        private Team _team = Team.None;
        private Color _appliedColor = Color.clear;
        private float _fill = 1f;
        private float _shownAt;

        public bool Showing => gameObject.activeSelf;
        public float Fill => _fill;
        public string LabelText => _label != null ? _label.text : null;
        public Team Team => _team;

        private void Awake()
        {
            var inks = new System.Collections.Generic.List<Renderer>();
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                if (r.name.StartsWith("Ink", System.StringComparison.Ordinal))
                {
                    inks.Add(r);
                }
            }
            _inkRenderers = inks.ToArray();
            BuildLabel();
        }

        private void BuildLabel()
        {
            Transform parent = _labelAnchor != null ? _labelAnchor : transform;
            var go = new GameObject("Label", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.sizeDelta = new Vector2(420f, 90f);
            rect.localScale = Vector3.one * 0.01f;
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.sortingOrder = 5;
            _label = Ui.Text("Name", rect, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(420f, 90f), 44,
                TextAnchor.MiddleCenter, Color.white, true, 3f);
        }

        // 開始顯示(蓄力開始時):名字、隊伍色、墨水滿
        public void Show(Inkling jumper, Vector3 point)
        {
            _team = jumper != null ? jumper.Team : Team.None;
            if (_label != null)
            {
                _label.text = jumper != null ? jumper.DisplayName : "";
            }
            _shownAt = Time.time;
            _fill = 1f;
            _appliedColor = Color.clear;
            transform.position = point;
            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
            }
            ApplyTint();
            ApplyFill();
        }

        public void MoveTo(Vector3 point)
        {
            transform.position = point;
        }

        // 墨水剩餘比例(1 → 0)
        public void SetFill(float fill01)
        {
            _fill = Mathf.Clamp01(fill01);
            ApplyFill();
        }

        public void Hide()
        {
            if (gameObject.activeSelf)
            {
                gameObject.SetActive(false);
            }
        }

        private void ApplyFill()
        {
            if (_fillPivot != null)
            {
                _fillPivot.localScale = new Vector3(1f, Mathf.Max(0.001f, _fill), 1f);
            }
        }

        private void ApplyTint()
        {
            Color color = TeamPalette.ColorOf(_team);
            if (color == _appliedColor || _inkRenderers == null)
            {
                return;
            }
            _appliedColor = color;
            for (int i = 0; i < _inkRenderers.Length; i++)
            {
                TeamTint.Apply(_inkRenderers[i], color, _inkRenderers[i].name.StartsWith("InkDisc", System.StringComparison.Ordinal)
                    ? 0.35f
                    : 1f);
            }
            if (_label != null)
            {
                _label.color = Color.Lerp(color, Color.white, 0.55f);
            }
        }

        private void LateUpdate()
        {
            ApplyTint();
            float age = Time.time - _shownAt;
            if (_ring != null)
            {
                float pulse = 1f + 0.06f * Mathf.Sin(age * 7f);
                _ring.localScale = new Vector3(pulse, 1f, pulse);
                _ring.localRotation = Quaternion.Euler(0f, age * 40f, 0f);
            }
            Camera cam = Camera.main;
            Vector3 toCamera = cam != null ? cam.transform.position - transform.position : Vector3.back;
            toCamera.y = 0f;
            Quaternion face = toCamera.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(-toCamera.normalized, Vector3.up)
                : Quaternion.identity;
            if (_arrow != null)
            {
                _arrow.SetPositionAndRotation(
                    transform.position + Vector3.up * (_arrowHeight + _bobAmplitude * Mathf.Sin(age * 4.2f)), face);
            }
            if (_labelAnchor != null)
            {
                _labelAnchor.rotation = face;
            }
        }
    }
}

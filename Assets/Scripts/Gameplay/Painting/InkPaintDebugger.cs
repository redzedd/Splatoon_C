using UnityEngine;
using UnityEngine.InputSystem;

namespace SplatoonC.Gameplay.Painting
{
    // 步驟 3 的除錯塗色路徑:按住 Attack,從畫面中心射線「點哪塗哪」。
    // 步驟 4 會被真正的墨彈武器取代——本類別到時整個刪除。
    public sealed class InkPaintDebugger : MonoBehaviour
    {
        [SerializeField, Tooltip("塗色動作(Player/Attack,按住連塗)")]
        private InputActionReference _paint;

        [SerializeField, Tooltip("墨水顏色")]
        private Color _inkColor = new Color(1f, 0.5f, 0f, 1f);

        [SerializeField, Tooltip("splat 半徑(公尺)")]
        private float _radius = 0.75f;

        [SerializeField, Range(0f, 1f), Tooltip("筆刷硬度(內圈實心比例)")]
        private float _hardness = 0.6f;

        [SerializeField, Tooltip("連塗間隔(秒)")]
        private float _paintInterval = 0.05f;

        [SerializeField, Tooltip("瞄準射線最大距離(公尺)")]
        private float _maxDistance = 60f;

        [SerializeField, Tooltip("瞄準射線圖層(場景接線時排除 Player)")]
        private LayerMask _mask = ~0;

        private Camera _camera;
        private float _nextPaintTime;

        private void Awake()
        {
            _camera = Camera.main;
            if (_paint == null)
            {
                Debug.LogError("InkPaintDebugger:缺少塗色動作引用(Player/Attack)", this);
            }
        }

        private void OnEnable()
        {
            if (_paint != null)
            {
                _paint.action.Enable();
            }
        }

        private void OnDisable()
        {
            if (_paint != null)
            {
                _paint.action.Disable();
            }
        }

        private void Update()
        {
            if (_paint == null || !_paint.action.IsPressed())
            {
                return;
            }
            if (Time.time < _nextPaintTime)
            {
                return;
            }
            _nextPaintTime = Time.time + _paintInterval;
            PaintAtAim();
        }

        // 供 AutoTest 直呼:走真實 raycast → PaintableSurface 路徑,只略過輸入讀取層。
        public bool PaintAtAim()
        {
            if (_camera == null)
            {
                _camera = Camera.main;
                if (_camera == null)
                {
                    return false;
                }
            }

            var ray = new Ray(_camera.transform.position, _camera.transform.forward);
            if (!Physics.Raycast(ray, out RaycastHit hit, _maxDistance, _mask, QueryTriggerInteraction.Ignore))
            {
                return false;
            }
            var surface = hit.collider.GetComponent<PaintableSurface>();
            if (surface == null)
            {
                return false;
            }
            surface.Paint(hit.point, _radius, _inkColor, _hardness);
            return true;
        }
    }
}

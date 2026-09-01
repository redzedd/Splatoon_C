using System;
using SplatoonC.Core;
using SplatoonC.Gameplay.Painting;
using UnityEngine;
using UnityEngine.Rendering;

namespace SplatoonC.Gameplay.Scoring
{
    // 佔地率計分:定期 AsyncGPUReadback 讀 ink map 統計像素(專案紅線:禁同步 readback)。
    // callback 委派在 Awake 快取,計分路徑無每幀配置。
    public sealed class CoverageScorer : MonoBehaviour
    {
        [SerializeField, Tooltip("要計分的可塗表面(Ground)")]
        private PaintableSurface _surface;

        [SerializeField, Tooltip("計分間隔(秒);readback 非同步,不會卡幀")]
        private float _interval = 0.5f;

        [SerializeField, Range(1, 254), Tooltip("alpha 門檻:超過視為已塗")]
        private int _alphaThreshold = 32;

        public CoverageCalculator.Result Latest { get; private set; }

        // 每次計分完成 +1,測試/HUD 用來偵測更新。
        public int Version { get; private set; }

        private Action<AsyncGPUReadbackRequest> _onReadback;
        private float _nextRequestTime;
        private bool _pending;

        private void Awake()
        {
            _onReadback = OnReadback;
            if (_surface == null)
            {
                Debug.LogError("CoverageScorer:缺少計分表面,佔地率不會更新", this);
            }
        }

        private void Update()
        {
            if (_surface == null || _pending || Time.time < _nextRequestTime)
            {
                return;
            }
            if (_surface.InkMap == null)
            {
                // shader 缺失等初始化失敗:大聲報錯後停用,不進例外風暴。
                Debug.LogError("CoverageScorer:表面的 InkMap 不存在(塗色系統初始化失敗?),計分停用", this);
                enabled = false;
                return;
            }
            _nextRequestTime = Time.time + _interval;
            _pending = true;
            AsyncGPUReadback.Request(_surface.InkMap, 0, TextureFormat.RGBA32, _onReadback);
        }

        private void OnReadback(AsyncGPUReadbackRequest request)
        {
            _pending = false;
            if (request.hasError)
            {
                return;
            }
            var data = request.GetData<Color32>();
            int painted = 0;
            byte threshold = (byte)_alphaThreshold;
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i].a > threshold)
                {
                    painted++;
                }
            }
            Latest = CoverageCalculator.Compute(painted, 0, data.Length);
            Version++;
        }
    }
}

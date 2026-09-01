using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SplatoonC.Gameplay.Painting
{
    // 可塗表面:持有本表面的 ink RenderTexture,Paint() 用 CommandBuffer 以 UV 空間 splat 注入。
    // 鐵律:本表面 UV 必須唯一且不重疊(CLAUDE.md §2);計分 readback 一律走 AsyncGPUReadback。
    [RequireComponent(typeof(Renderer))]
    public sealed class PaintableSurface : MonoBehaviour
    {
        // OnEnable/OnDisable 自維護,不依賴 domain reload 重置
        public static readonly List<PaintableSurface> Active = new List<PaintableSurface>();

        [SerializeField, Tooltip("墨水圖解析度(每邊像素)")]
        private int _resolution = 512;

        [SerializeField, Tooltip("splat 注入 shader(SplatoonC/InkSplat);留空自動尋找")]
        private Shader _splatShader;

        private static readonly int InkMapId = Shader.PropertyToID("_InkMap");
        private static readonly int SplatCenterId = Shader.PropertyToID("_SplatCenter");
        private static readonly int SplatColorId = Shader.PropertyToID("_SplatColor");
        private static readonly int SplatHardnessId = Shader.PropertyToID("_SplatHardness");

        private Renderer _renderer;
        private RenderTexture _inkMap;
        private Material _splatMaterial;
        private CommandBuffer _commandBuffer;
        private MaterialPropertyBlock _propertyBlock;
        private bool _initialized;

        public RenderTexture InkMap
        {
            get
            {
                EnsureInitialized();
                return _inkMap;
            }
        }

        private void Awake()
        {
            EnsureInitialized();
        }

        private void OnEnable()
        {
            Active.Add(this);
        }

        private void OnDisable()
        {
            Active.Remove(this);
        }

        private void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }
            _initialized = true;

            _renderer = GetComponent<Renderer>();
            if (_splatShader == null)
            {
                _splatShader = Shader.Find("SplatoonC/InkSplat");
            }
            if (_splatShader == null)
            {
                Debug.LogError("PaintableSurface:找不到 SplatoonC/InkSplat shader,無法塗色", this);
                return;
            }

            _inkMap = new RenderTexture(_resolution, _resolution, 0, RenderTextureFormat.ARGB32)
            {
                name = name + "_InkMap",
            };
            _inkMap.Create();
            var previous = RenderTexture.active;
            RenderTexture.active = _inkMap;
            GL.Clear(false, true, Color.clear);
            RenderTexture.active = previous;

            _splatMaterial = new Material(_splatShader);
            _commandBuffer = new CommandBuffer { name = "InkSplat" };
            _propertyBlock = new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetTexture(InkMapId, _inkMap);
            _renderer.SetPropertyBlock(_propertyBlock);
        }

        public void Paint(Vector3 worldPosition, float radius, Color color, float hardness)
        {
            EnsureInitialized();
            if (_inkMap == null || _splatMaterial == null)
            {
                return;
            }

            _commandBuffer.Clear();
            _commandBuffer.SetRenderTarget(_inkMap);
            _commandBuffer.SetGlobalVector(SplatCenterId,
                new Vector4(worldPosition.x, worldPosition.y, worldPosition.z, radius));
            // Linear 色彩空間:檢視器挑的 sRGB 色先轉 linear,否則墨色顯示偏亮(橘變黃)。
            _commandBuffer.SetGlobalColor(SplatColorId, color.linear);
            _commandBuffer.SetGlobalFloat(SplatHardnessId, hardness);
            _commandBuffer.DrawRenderer(_renderer, _splatMaterial, 0, 0);
            Graphics.ExecuteCommandBuffer(_commandBuffer);

            // 同步登記到腳下墨網格(M1 水平地面假設;之後牆面塗色要改按表面法線過濾)。
            if (InkWorld.Instance != null)
            {
                InkWorld.Instance.RegisterSplat(worldPosition, radius);
            }
        }

        private void OnDestroy()
        {
            if (_inkMap != null)
            {
                _inkMap.Release();
                Destroy(_inkMap);
            }
            if (_splatMaterial != null)
            {
                Destroy(_splatMaterial);
            }
            if (_commandBuffer != null)
            {
                _commandBuffer.Dispose();
            }
        }
    }
}

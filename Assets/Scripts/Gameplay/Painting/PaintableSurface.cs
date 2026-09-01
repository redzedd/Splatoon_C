using System.Collections.Generic;
using SplatoonC.Core.Painting;
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

        [SerializeField, Tooltip("splat 注入 shader(SplatoonC/InkSplat);留空自動尋找。注意:standalone build 需要此欄位建立資產引用,否則 shader 被剔除")]
        private Shader _splatShader;

        [SerializeField, Tooltip("歸屬網格 cell 大小(公尺,世界單位);烏賊腳下/牆面墨查詢的解析度")]
        private float _ownershipCellSize = 0.25f;

        [SerializeField, Range(0f, 0.6f), Tooltip("墨漬邊緣噪聲振幅(0 = 正圓;只影響視覺,不影響歸屬網格)")]
        private float _splatNoiseAmplitude = 0.3f;

        private static readonly int InkMapId = Shader.PropertyToID("_InkMap");
        private static readonly int SplatCenterId = Shader.PropertyToID("_SplatCenter");
        private static readonly int SplatColorId = Shader.PropertyToID("_SplatColor");
        private static readonly int SplatHardnessId = Shader.PropertyToID("_SplatHardness");
        private static readonly int SplatNoiseId = Shader.PropertyToID("_SplatNoise");

        private Renderer _renderer;
        private RenderTexture _inkMap;
        private Material _splatMaterial;
        private CommandBuffer _commandBuffer;
        private MaterialPropertyBlock _propertyBlock;
        private bool _initialized;
        private InkOwnershipGrid _ownershipGrid;
        private PlanarSurfaceMap _surfaceMap;
        private float _planarScale = 1f;

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

            InitializeOwnership();
        }

        // 每表面局部平面歸屬網格(M2 重構:取代世界水平網格,牆面也適用)。
        private void InitializeOwnership()
        {
            var meshFilter = GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                Debug.LogError("PaintableSurface:找不到 mesh,墨歸屬查詢停用", this);
                return;
            }
            Bounds bounds = meshFilter.sharedMesh.bounds;
            _surfaceMap = PlanarSurfaceMap.FromBounds(bounds.min, bounds.size);

            Vector3 lossy = transform.lossyScale;
            float scaleA;
            float scaleB;
            switch (_surfaceMap.NormalAxis)
            {
                case 0: scaleA = Mathf.Abs(lossy.z); scaleB = Mathf.Abs(lossy.y); break;
                case 1: scaleA = Mathf.Abs(lossy.x); scaleB = Mathf.Abs(lossy.z); break;
                default: scaleA = Mathf.Abs(lossy.x); scaleB = Mathf.Abs(lossy.y); break;
            }
            _planarScale = Mathf.Max((scaleA + scaleB) * 0.5f, 0.0001f);

            float localCellSize = _ownershipCellSize / _planarScale;
            _ownershipGrid = new InkOwnershipGrid(
                _surfaceMap.PlaneMin.x, _surfaceMap.PlaneMin.y,
                _surfaceMap.PlaneSize.x, _surfaceMap.PlaneSize.y,
                localCellSize);
        }

        // 查詢表面上某世界點的墨歸屬(0=無墨,1=自家)。
        public byte SampleOwnership(Vector3 worldPosition)
        {
            EnsureInitialized();
            if (_ownershipGrid == null)
            {
                return 0;
            }
            Vector2 planePoint = _surfaceMap.ToPlane(transform.InverseTransformPoint(worldPosition));
            return _ownershipGrid.Sample(planePoint.x, planePoint.y);
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
            // 每發隨機波瓣:頻率取整數(shader 的 ±π 接縫要求),相位全隨機。
            // 低頻大瓣(2~4)+中頻小瓣(5~8):高頻會變齒輪感(2026-09-02 截圖迭代)。
            _commandBuffer.SetGlobalVector(SplatNoiseId, new Vector4(
                _splatNoiseAmplitude,
                Mathf.Round(Random.Range(2f, 5f)),
                Mathf.Round(Random.Range(5f, 9f)),
                Random.Range(0f, Mathf.PI * 2f)));
            _commandBuffer.DrawRenderer(_renderer, _splatMaterial, 0, 0);
            Graphics.ExecuteCommandBuffer(_commandBuffer);

            // 同步標記本表面的局部歸屬網格(牆面也適用;取代 M1 的世界水平網格)。
            if (_ownershipGrid != null)
            {
                Vector2 planePoint = _surfaceMap.ToPlane(transform.InverseTransformPoint(worldPosition));
                _ownershipGrid.MarkCircle(planePoint.x, planePoint.y, radius / _planarScale, 1);
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

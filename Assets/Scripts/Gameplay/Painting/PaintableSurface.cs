using System.Collections.Generic;
using SplatoonC.Core;
using SplatoonC.Core.Painting;
using UnityEngine;
using UnityEngine.Rendering;

namespace SplatoonC.Gameplay.Painting
{
    // 可塗表面:持有本表面的 ink RenderTexture 與每個平面區塊(面)的歸屬網格。
    // ink map 通道約定:R = Alpha 隊佔比、G = Bravo 隊佔比、A = 總覆蓋(R + G ≤ 1 由混合保證);
    // 顏色在 PaintableSurface.shader 依 TeamPalette 合成,所以 ink map 本身與配色無關。
    // 幾何:同物件有 IPaintableGeometry(PaintableBlock)就用它的多面與擴邊塗色網格;
    // 否則退回「單一平面 mesh」模式(Unity Plane/Quad),由 mesh bounds 推出唯一的面。
    // 鐵律:渲染 mesh 的 UV 必須唯一不重疊;計分 readback 一律走 AsyncGPUReadback。
    [RequireComponent(typeof(Renderer))]
    public sealed class PaintableSurface : MonoBehaviour
    {
        // OnEnable/OnDisable 自維護,不依賴 domain reload 重置
        public static readonly List<PaintableSurface> Active = new List<PaintableSurface>();

        // 背面容差(公尺):球心在面後方超過此距離就不塗該面(薄牆另一側不透墨)
        public const float BackfaceTolerance = 0.1f;

        // 法線 y 分量超過此值的面算「地面」——佔地判定只算地面(Splatoon 規則:牆不計)
        public const float FloorNormalY = 0.7f;

        [SerializeField, Tooltip("單一平面模式的墨水圖解析度(每邊像素);PaintableBlock 模式由 texel 密度決定")]
        private int _resolution = 512;

        [SerializeField, Tooltip("splat 注入 shader(SplatoonC/InkSplat);留空自動尋找。注意:standalone build 需要此欄位建立資產引用,否則 shader 被剔除")]
        private Shader _splatShader;

        [SerializeField, Tooltip("歸屬網格 cell 大小(公尺,世界單位);烏賊腳下/牆面墨查詢與佔地判定的解析度")]
        private float _ownershipCellSize = 0.25f;

        [SerializeField, Range(0f, 0.6f), Tooltip("墨漬邊緣噪聲振幅(0 = 正圓;只影響視覺,不影響歸屬網格)")]
        private float _splatNoiseAmplitude = 0.3f;

        [SerializeField, Tooltip("是否列入佔地判定(裝飾物/場外牆可關)")]
        private bool _countsForTurf = true;

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
        private Mesh _paintMesh;
        private SurfaceFace[] _worldFaces;
        private InkOwnershipGrid[] _grids;
        private Bounds _worldBounds;

        public RenderTexture InkMap
        {
            get
            {
                EnsureInitialized();
                return _inkMap;
            }
        }

        // 塗墨範圍粗篩用(靜態表面;會移動的可塗物要呼叫 RefreshWorldFrames)
        public Bounds WorldBounds
        {
            get
            {
                EnsureInitialized();
                return _worldBounds;
            }
        }

        public bool CountsForTurf => _countsForTurf;

        public int FaceCount
        {
            get
            {
                EnsureInitialized();
                return _worldFaces != null ? _worldFaces.Length : 0;
            }
        }

        public float OwnershipCellSize => _ownershipCellSize;

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

            var geometry = GetComponent<IPaintableGeometry>();
            Vector2Int resolution = new Vector2Int(_resolution, _resolution);
            SurfaceFace[] localFaces;
            if (geometry != null)
            {
                geometry.EnsureBuilt();
                localFaces = geometry.LocalFaces;
                _paintMesh = geometry.PaintMesh;
                resolution = geometry.InkMapResolution;
            }
            else
            {
                localFaces = BuildSinglePlaneFace();
            }

            // 資料貼圖:linear,R/G 是隊伍佔比而不是顏色,不能被 sRGB 編碼扭曲
            _inkMap = new RenderTexture(Mathf.Max(1, resolution.x), Mathf.Max(1, resolution.y), 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            {
                name = name + "_InkMap",
            };
            _inkMap.Create();
            ClearRenderTexture();

            _splatMaterial = new Material(_splatShader);
            _commandBuffer = new CommandBuffer { name = "InkSplat" };
            _propertyBlock = new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetTexture(InkMapId, _inkMap);
            _renderer.SetPropertyBlock(_propertyBlock);

            InitializeFaces(localFaces);
        }

        private SurfaceFace[] BuildSinglePlaneFace()
        {
            var meshFilter = GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                Debug.LogError("PaintableSurface:找不到 mesh,墨歸屬查詢停用", this);
                return new SurfaceFace[0];
            }
            Mesh mesh = meshFilter.sharedMesh;
            Bounds bounds = mesh.bounds;
            Vector3 normalHint = Vector3.zero;
            Vector3[] normals = mesh.normals;
            if (normals != null && normals.Length > 0)
            {
                normalHint = normals[0];
            }
            return new[] { SurfaceFace.FromBounds(bounds.min, bounds.size, normalHint) };
        }

        private void InitializeFaces(SurfaceFace[] localFaces)
        {
            Matrix4x4 localToWorld = transform.localToWorldMatrix;
            _worldFaces = new SurfaceFace[localFaces.Length];
            _grids = new InkOwnershipGrid[localFaces.Length];
            for (int i = 0; i < localFaces.Length; i++)
            {
                SurfaceFace world = localFaces[i].Transform(localToWorld);
                _worldFaces[i] = world;
                _grids[i] = new InkOwnershipGrid(0f, 0f, world.Width, world.Height, _ownershipCellSize);
            }
            RecomputeBounds();
        }

        private void RecomputeBounds()
        {
            bool any = false;
            var bounds = new Bounds();
            for (int i = 0; i < _worldFaces.Length; i++)
            {
                SurfaceFace f = _worldFaces[i];
                Vector3 a = f.Origin;
                Vector3 b = f.FromPlane(new Vector2(f.Width, 0f));
                Vector3 c = f.FromPlane(new Vector2(f.Width, f.Height));
                Vector3 d = f.FromPlane(new Vector2(0f, f.Height));
                if (!any)
                {
                    bounds = new Bounds(a, Vector3.zero);
                    any = true;
                }
                bounds.Encapsulate(a);
                bounds.Encapsulate(b);
                bounds.Encapsulate(c);
                bounds.Encapsulate(d);
            }
            _worldBounds = any ? bounds : _renderer.bounds;
        }

        // 移動過的可塗物(極少)重算世界座標框架;歸屬網格保留(面在物件上的位置沒變)。
        public void RefreshWorldFrames()
        {
            EnsureInitialized();
            var geometry = GetComponent<IPaintableGeometry>();
            SurfaceFace[] localFaces = geometry != null ? geometry.LocalFaces : BuildSinglePlaneFace();
            Matrix4x4 localToWorld = transform.localToWorldMatrix;
            for (int i = 0; i < _worldFaces.Length && i < localFaces.Length; i++)
            {
                _worldFaces[i] = localFaces[i].Transform(localToWorld);
            }
            RecomputeBounds();
        }

        // 第 face 個面的世界座標框架與歸屬網格(模式目標區域逐格統計 / 重塗用;網格格座標 = 面的平面座標)
        public SurfaceFace GetWorldFace(int face)
        {
            EnsureInitialized();
            return _worldFaces[face];
        }

        public InkOwnershipGrid GetFaceGrid(int face)
        {
            EnsureInitialized();
            return _grids[face];
        }

        // 以自訂材質(pass 0)把本表面的塗色網格整張畫進 ink map 一次(例:真格區域佔領時整片重塗)。
        // 遮罩完全由材質的 fragment 決定(參數請先用 Shader.SetGlobal* 設好);歸屬網格不在此更新,呼叫端自己改格。
        public void DrawIntoInkMap(Material material)
        {
            EnsureInitialized();
            if (_inkMap == null || material == null)
            {
                return;
            }
            _commandBuffer.Clear();
            _commandBuffer.SetRenderTarget(_inkMap);
            if (_paintMesh != null)
            {
                _commandBuffer.DrawMesh(_paintMesh, transform.localToWorldMatrix, material, 0, 0);
            }
            else
            {
                _commandBuffer.DrawRenderer(_renderer, material, 0, 0);
            }
            Graphics.ExecuteCommandBuffer(_commandBuffer);
        }

        // 查詢某世界點的墨歸屬(0 = 無墨)。normal 可給 Vector3.zero(不看朝向)。
        public byte SampleOwnership(Vector3 worldPosition, Vector3 normal)
        {
            int face = FindFace(worldPosition, normal);
            if (face < 0)
            {
                return 0;
            }
            Vector2 q = _worldFaces[face].ToPlane(worldPosition);
            return _grids[face].Sample(q.x, q.y);
        }

        public byte SampleOwnership(Vector3 worldPosition)
        {
            return SampleOwnership(worldPosition, Vector3.zero);
        }

        // 挑包含此點、離平面最近的面;給了法線就排除朝向不符的面(方塊邊角同時貼近兩面時分得開)。
        private int FindFace(Vector3 worldPosition, Vector3 normal)
        {
            EnsureInitialized();
            if (_worldFaces == null)
            {
                return -1;
            }
            bool useNormal = normal.sqrMagnitude > 0.01f;
            int best = -1;
            float bestDistance = 0.35f;
            for (int i = 0; i < _worldFaces.Length; i++)
            {
                SurfaceFace f = _worldFaces[i];
                if (useNormal && Vector3.Dot(f.Normal, normal) < 0.5f)
                {
                    continue;
                }
                float distance = Mathf.Abs(f.SignedDistance(worldPosition));
                if (distance >= bestDistance)
                {
                    continue;
                }
                if (!f.Contains(f.ToPlane(worldPosition), 0.05f))
                {
                    continue;
                }
                best = i;
                bestDistance = distance;
            }
            return best;
        }

        // 球形塗墨(世界座標球心 + 半徑)。回傳新轉為 team 的地面投影面積(m²;牆面為 0)。
        // 視覺:整個 ink map 一次 pass(shader 以 3D 距離 + 背面測試決定遮罩);
        // 歸屬:逐面以「球與平面的交圓」標記網格,半徑 = √(r² − d²)。
        public float Paint(Vector3 worldPosition, float radius, Team team, float hardness)
        {
            EnsureInitialized();
            if (_inkMap == null || _splatMaterial == null || team == Team.None)
            {
                return 0f;
            }

            _commandBuffer.Clear();
            _commandBuffer.SetRenderTarget(_inkMap);
            _commandBuffer.SetGlobalVector(SplatCenterId,
                new Vector4(worldPosition.x, worldPosition.y, worldPosition.z, radius));
            _commandBuffer.SetGlobalVector(SplatColorId, TeamPalette.ChannelOf(team));
            _commandBuffer.SetGlobalFloat(SplatHardnessId, hardness);
            // 每發隨機波瓣:頻率取整數(shader 的 ±π 接縫要求),相位全隨機。
            // 低頻大瓣(2~4)+中頻小瓣(5~8):高頻會變齒輪感(2026-09-02 截圖迭代)。
            _commandBuffer.SetGlobalVector(SplatNoiseId, new Vector4(
                _splatNoiseAmplitude,
                Mathf.Round(Random.Range(2f, 5f)),
                Mathf.Round(Random.Range(5f, 9f)),
                Random.Range(0f, Mathf.PI * 2f)));
            if (_paintMesh != null)
            {
                _commandBuffer.DrawMesh(_paintMesh, transform.localToWorldMatrix, _splatMaterial, 0, 0);
            }
            else
            {
                _commandBuffer.DrawRenderer(_renderer, _splatMaterial, 0, 0);
            }
            Graphics.ExecuteCommandBuffer(_commandBuffer);

            // 回傳值 = 新增的「地面投影」面積:牆面照樣標記歸屬(可游、可爬),但不計分也不充大招(S3 規則)
            float cellArea = _ownershipCellSize * _ownershipCellSize;
            float turfArea = 0f;
            for (int i = 0; i < _worldFaces.Length; i++)
            {
                SurfaceFace f = _worldFaces[i];
                float d = f.SignedDistance(worldPosition);
                if (d < -BackfaceTolerance || Mathf.Abs(d) >= radius)
                {
                    continue;
                }
                float circleRadius = Mathf.Sqrt(radius * radius - d * d);
                Vector2 q = f.ToPlane(worldPosition);
                if (q.x + circleRadius < 0f || q.y + circleRadius < 0f
                    || q.x - circleRadius > f.Width || q.y - circleRadius > f.Height)
                {
                    continue;
                }
                int newCells = _grids[i].MarkCircle(q.x, q.y, circleRadius, (byte)team);
                if (_countsForTurf && f.Normal.y >= FloorNormalY)
                {
                    turfArea += newCells * cellArea * f.Normal.y;
                }
            }
            return turfArea;
        }

        // 佔地判定:累加本表面「地面」面上各隊的俯視投影面積(m²)。
        // Splatoon 規則:牆 0 分;斜坡只算投影面積(= 面積 × 法線 y),同一發墨在平地或坡上得分相同。
        public void AccumulateTurf(ref float alpha, ref float bravo, ref float total)
        {
            EnsureInitialized();
            if (!_countsForTurf || _worldFaces == null)
            {
                return;
            }
            float cellArea = _ownershipCellSize * _ownershipCellSize;
            for (int i = 0; i < _worldFaces.Length; i++)
            {
                float normalY = _worldFaces[i].Normal.y;
                if (normalY < FloorNormalY)
                {
                    continue;
                }
                float weight = cellArea * normalY;
                InkOwnershipGrid grid = _grids[i];
                alpha += grid.CountOf((byte)Team.Alpha) * weight;
                bravo += grid.CountOf((byte)Team.Bravo) * weight;
                total += grid.TotalCells * weight;
            }
        }

        // 佔地分母修正:地面上被其他物件壓住的格(方塊放在地板上、塔立在台地上)永遠塗不到,
        // 從總格數排除。只需做一次(靜態場地);以格中心沿法線往上短射線判定。
        private bool _occlusionResolved;

        public int ExcludeOccludedTurf(float probeHeight = 0.6f)
        {
            EnsureInitialized();
            if (_occlusionResolved || _worldFaces == null)
            {
                return 0;
            }
            _occlusionResolved = true;
            int blocked = 0;
            int mask = ~GameLayers.InklingMask;
            for (int i = 0; i < _worldFaces.Length; i++)
            {
                SurfaceFace f = _worldFaces[i];
                if (f.Normal.y < FloorNormalY)
                {
                    continue;
                }
                InkOwnershipGrid grid = _grids[i];
                for (int cz = 0; cz < grid.Height; cz++)
                {
                    for (int cx = 0; cx < grid.Width; cx++)
                    {
                        Vector3 p = f.FromPlane(new Vector2(grid.CellCenterX(cx), grid.CellCenterZ(cz)));
                        // 超出面範圍的邊緣格(寬度不是格距整數倍)也不該計分
                        bool outside = grid.CellCenterX(cx) > f.Width || grid.CellCenterZ(cz) > f.Height;
                        // 重疊球:抓「直接立在地面上」的方塊(射線起點落在方塊內部時不會命中它);
                        // 短射線:抓離地一點點的懸空物
                        if (outside
                            || Physics.CheckSphere(p + f.Normal * 0.15f, 0.1f, mask, QueryTriggerInteraction.Ignore)
                            || Physics.Raycast(p + f.Normal * 0.3f, f.Normal, probeHeight, mask,
                                QueryTriggerInteraction.Ignore))
                        {
                            grid.BlockCell(cx, cz);
                            blocked++;
                        }
                    }
                }
            }
            return blocked;
        }

        // 比賽重置:清掉視覺與歸屬
        public void ClearInk()
        {
            EnsureInitialized();
            ClearRenderTexture();
            if (_grids != null)
            {
                for (int i = 0; i < _grids.Length; i++)
                {
                    _grids[i].Clear();
                }
            }
        }

        private void ClearRenderTexture()
        {
            if (_inkMap == null)
            {
                return;
            }
            var previous = RenderTexture.active;
            RenderTexture.active = _inkMap;
            GL.Clear(false, true, Color.clear);
            RenderTexture.active = previous;
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

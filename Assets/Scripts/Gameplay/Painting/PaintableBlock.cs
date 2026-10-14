using SplatoonC.Core.Painting;
using UnityEngine;

namespace SplatoonC.Gameplay.Painting
{
    // 可塗表面的幾何提供者:給出局部空間的面清單、塗色網格與 ink map 解析度。
    public interface IPaintableGeometry
    {
        void EnsureBuilt();
        SurfaceFace[] LocalFaces { get; }
        Mesh PaintMesh { get; }
        Vector2Int InkMapResolution { get; }
    }

    // 灰盒關卡方塊/斜坡:依參數在 Edit 與 Play 都即時產生網格(不存成資產,場景只記參數)。
    // 網格 DontSave:場景存檔時 MeshFilter/MeshCollider 的引用存成空,載入時 OnEnable 重建。
    // collider:方塊用 BoxCollider、斜坡用 MeshCollider(NavMesh 烘焙與角色碰撞都吃得到)。
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class PaintableBlock : MonoBehaviour, IPaintableGeometry
    {
        public enum BlockShape
        {
            Box = 0,
            Wedge = 1,
        }

        [SerializeField, Tooltip("形狀:方塊(中心樞紐)或斜坡(底面中心樞紐,沿 +Z 升高)")]
        private BlockShape _shape = BlockShape.Box;

        [SerializeField, Tooltip("尺寸(公尺);斜坡為 寬 × 高 × 長")]
        private Vector3 _size = new Vector3(4f, 1f, 4f);

        [SerializeField, Tooltip("ink map 每像素對應公尺數;越小越細緻、越吃記憶體")]
        private float _texelSize = 0.08f;

        [SerializeField, Tooltip("方塊要產生哪些面(貼地的底面可省)")]
        private PaintableMeshBuilder.BoxFaces _boxFaces = PaintableMeshBuilder.BoxFaces.All;

        private Mesh _renderMesh;
        private Mesh _paintMesh;
        private SurfaceFace[] _faces;
        private Vector2Int _resolution;
        private bool _built;

        public BlockShape Shape => _shape;
        public Vector3 Size => _size;

        public SurfaceFace[] LocalFaces
        {
            get
            {
                EnsureBuilt();
                return _faces;
            }
        }

        public Mesh PaintMesh
        {
            get
            {
                EnsureBuilt();
                return _paintMesh;
            }
        }

        public Vector2Int InkMapResolution
        {
            get
            {
                EnsureBuilt();
                return _resolution;
            }
        }

        public void Configure(BlockShape shape, Vector3 size, float texelSize, PaintableMeshBuilder.BoxFaces faces)
        {
            _shape = shape;
            _size = size;
            _texelSize = texelSize;
            _boxFaces = faces;
            Rebuild();
        }

        private void OnEnable()
        {
            EnsureBuilt();
        }

        private void OnValidate()
        {
            // OnValidate 內不可建/毀物件(Unity 會警告):標髒,下次存取時重建
            _built = false;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null)
                {
                    EnsureBuilt();
                }
            };
#endif
        }

        public void EnsureBuilt()
        {
            if (!_built || _renderMesh == null)
            {
                Rebuild();
            }
        }

        public void Rebuild()
        {
            _built = true;
            Vector3 size = new Vector3(Mathf.Max(0.05f, _size.x), Mathf.Max(0.05f, _size.y), Mathf.Max(0.05f, _size.z));
            PaintableMeshData data = _shape == BlockShape.Wedge
                ? PaintableMeshBuilder.BuildWedge(size, _texelSize)
                : PaintableMeshBuilder.BuildBox(size, _texelSize, _boxFaces);

            DestroyMeshes();
            _renderMesh = PaintableMeshBuilder.ToRenderMesh(data, name + "_Render");
            _renderMesh.hideFlags = HideFlags.DontSave;
            _paintMesh = PaintableMeshBuilder.ToPaintMesh(data, name + "_Paint");
            _paintMesh.hideFlags = HideFlags.DontSave;
            _faces = data.Faces;
            _resolution = new Vector2Int(data.AtlasWidthPx, data.AtlasHeightPx);

            GetComponent<MeshFilter>().sharedMesh = _renderMesh;
            if (_shape == BlockShape.Box)
            {
                var meshCollider = GetComponent<MeshCollider>();
                if (meshCollider != null)
                {
                    DestroySafe(meshCollider);
                }
                var box = GetComponent<BoxCollider>();
                if (box == null)
                {
                    box = gameObject.AddComponent<BoxCollider>();
                }
                box.center = Vector3.zero;
                box.size = size;
            }
            else
            {
                var box = GetComponent<BoxCollider>();
                if (box != null)
                {
                    DestroySafe(box);
                }
                var meshCollider = GetComponent<MeshCollider>();
                if (meshCollider == null)
                {
                    meshCollider = gameObject.AddComponent<MeshCollider>();
                }
                meshCollider.sharedMesh = null;
                meshCollider.sharedMesh = _renderMesh;
            }
        }

        private void OnDestroy()
        {
            DestroyMeshes();
        }

        private void DestroyMeshes()
        {
            if (_renderMesh != null)
            {
                DestroySafe(_renderMesh);
                _renderMesh = null;
            }
            if (_paintMesh != null)
            {
                DestroySafe(_paintMesh);
                _paintMesh = null;
            }
        }

        private static void DestroySafe(Object obj)
        {
            if (Application.isPlaying)
            {
                Destroy(obj);
            }
            else
            {
                DestroyImmediate(obj);
            }
        }
    }
}

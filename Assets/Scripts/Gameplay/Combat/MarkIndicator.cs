using SplatoonC.Core;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 被標記角色的世界空間提示(只給「本機玩家所屬隊伍 = 標記方」看):
    //   頭上菱形標記(白框 + 目標隊伍色,面向鏡頭、隨距離放大維持可讀)
    //   身形輪廓(穿牆可見的半透明膠囊 + 邊光;潛伏中也看得到,S3 被標記者無處可躲)
    //   本機玩家 → 目標的虛線指向線(S3「每位隊友各有一條指向目標的線」,每個畫面只畫自己的那條)
    // 全部用 SplatoonC/MarkOverlay(ZTest Always)。第一次被標記時由 Marking.Ensure 掛上;
    // 視覺物件不掛在角色底下(免得被角色的 Renderer 掃描/染色誤傷),隨本元件銷毀。
    [DisallowMultipleComponent]
    public sealed class MarkIndicator : MonoBehaviour
    {
        private const string MaterialResource = "Effects/MarkOverlay";
        private const string ShaderName = "SplatoonC/MarkOverlay";

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static Material _outlineMaterial;
        private static Material _fillMaterial;
        private static Material _ghostMaterial;
        private static Material _lineMaterial;
        private static Mesh _diamondFillMesh;
        private static Mesh _diamondOutlineMesh;
        private static Mesh _capsuleMesh;
        private static Texture2D _dashTexture;

        [SerializeField, Tooltip("菱形標記離頭頂的高度(公尺)")]
        private float _markerLift = 0.55f;

        [SerializeField, Tooltip("指向線寬度(公尺)")]
        private float _lineWidth = 0.07f;

        private Inkling _target;
        private InklingLifecycle _life;
        private SquidController _squid;
        private CharacterController _controller;
        private GameObject _root;
        private Transform _marker;
        private Transform _ghostTransform;
        private Renderer _outline;
        private Renderer _fill;
        private Renderer _ghost;
        private LineRenderer _line;
        private MaterialPropertyBlock _block;
        private Inkling _viewer;
        private InklingLifecycle _viewerLife;
        private Color _appliedColor = Color.clear;
        private bool _visible;
        private readonly Vector3[] _linePoints = new Vector3[2];

        // 本機畫面上目前有沒有顯示(測試/除錯)
        public bool Showing => _visible;
        public bool LineShowing => _visible && _line != null && _line.enabled;
        public Renderer GhostRenderer => _ghost;
        public Transform Marker => _marker;

        public static MarkIndicator Ensure(Inkling target)
        {
            if (target == null)
            {
                return null;
            }
            var indicator = target.GetComponent<MarkIndicator>();
            return indicator != null ? indicator : target.gameObject.AddComponent<MarkIndicator>();
        }

        private void Awake()
        {
            _target = GetComponent<Inkling>();
            _life = GetComponent<InklingLifecycle>();
            _squid = GetComponent<SquidController>();
            _controller = GetComponent<CharacterController>();
            _block = new MaterialPropertyBlock();
            Build();
            SetVisible(false);
        }

        private void OnDisable()
        {
            SetVisible(false);
        }

        private void OnDestroy()
        {
            if (_root != null)
            {
                Destroy(_root);
            }
        }

        private void Build()
        {
            EnsureSharedAssets();
            _root = new GameObject("MarkIndicator_" + name);

            _marker = new GameObject("Marker").transform;
            _marker.SetParent(_root.transform, false);
            _outline = CreateMeshRenderer("Outline", _marker, _diamondOutlineMesh, _outlineMaterial);
            _fill = CreateMeshRenderer("Fill", _marker, _diamondFillMesh, _fillMaterial);

            _ghostTransform = new GameObject("Silhouette").transform;
            _ghostTransform.SetParent(_root.transform, false);
            _ghost = CreateMeshRenderer("Mesh", _ghostTransform, _capsuleMesh, _ghostMaterial);

            var lineGo = new GameObject("TrackLine");
            lineGo.transform.SetParent(_root.transform, false);
            _line = lineGo.AddComponent<LineRenderer>();
            _line.sharedMaterial = _lineMaterial;
            _line.useWorldSpace = true;
            _line.positionCount = 2;
            _line.widthMultiplier = _lineWidth;
            _line.textureMode = LineTextureMode.Tile;
            _line.numCapVertices = 2;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
            _line.alignment = LineAlignment.View;

            _outline.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, Color.white);
            _outline.SetPropertyBlock(_block);
        }

        private static Renderer CreateMeshRenderer(string objectName, Transform parent, Mesh mesh, Material material)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return renderer;
        }

        private void LateUpdate()
        {
            Inkling viewer = Inkling.Local;
            Team viewTeam = viewer != null ? viewer.Team : Team.None;
            bool dead = _life != null && _life.IsDead;
            bool show = !dead && _target != null && viewTeam != Team.None && Marking.IsMarked(_target, viewTeam);
            SetVisible(show);
            if (!show)
            {
                return;
            }

            ApplyColor(TeamPalette.ColorOf(_target.Team));
            Vector3 feet = transform.position;
            bool squid = _squid != null && _squid.IsSquid;
            float height = _controller != null ? _controller.height : 2f;
            float bodyHeight = squid ? height * 0.35f : height;
            float radius = _controller != null ? _controller.radius : 0.4f;

            // 身形輪廓:與角色碰撞膠囊同尺寸(Unity 內建膠囊高 2、半徑 0.5)
            _ghostTransform.position = feet + Vector3.up * (bodyHeight * 0.5f);
            _ghostTransform.rotation = Quaternion.identity;
            _ghostTransform.localScale = new Vector3(radius * 2f, bodyHeight * 0.5f, radius * 2f);

            // 菱形標記:頭頂上方、輕微上下浮動、面向鏡頭;遠處放大維持可讀
            Camera cam = Camera.main;
            float bob = Mathf.Sin(Time.time * 4f) * 0.06f;
            Vector3 markerPos = feet + Vector3.up * (bodyHeight + _markerLift + bob);
            float scale = 1f;
            if (cam != null)
            {
                Vector3 toCamera = cam.transform.position - markerPos;
                scale = Mathf.Clamp(toCamera.magnitude / 10f, 0.9f, 3f);
                _marker.rotation = Quaternion.LookRotation(-toCamera.normalized, cam.transform.up);
            }
            _marker.position = markerPos + Vector3.up * (scale - 1f) * 0.25f;
            _marker.localScale = Vector3.one * scale;

            // 指向線:本機玩家胸口 → 目標胸口(本機玩家陣亡時不畫)
            if (viewer != _viewer)
            {
                _viewer = viewer;
                _viewerLife = viewer.GetComponent<InklingLifecycle>();
            }
            bool viewerAlive = _viewerLife == null || !_viewerLife.IsDead;
            _line.enabled = viewerAlive;
            if (viewerAlive)
            {
                _linePoints[0] = viewer.transform.position + Vector3.up * 1.1f;
                _linePoints[1] = feet + Vector3.up * (bodyHeight * 0.55f);
                _line.SetPositions(_linePoints);
            }
        }

        private void SetVisible(bool visible)
        {
            if (_root == null)
            {
                _visible = false;
                return;
            }
            if (_visible == visible && _root.activeSelf == visible)
            {
                return;
            }
            _visible = visible;
            _root.SetActive(visible);
        }

        private void ApplyColor(Color color)
        {
            if (color == _appliedColor)
            {
                return;
            }
            _appliedColor = color;
            SetColor(_fill, color, 0.95f);
            SetColor(_ghost, color, 1f);
            SetColor(_line, color, 0.75f);
        }

        private void SetColor(Renderer renderer, Color color, float alpha)
        {
            color.a = alpha;
            renderer.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, color);
            renderer.SetPropertyBlock(_block);
        }

        // ── 共用資產(整場一份)──

        private static void EnsureSharedAssets()
        {
            if (_fillMaterial != null)
            {
                return;
            }
            Material template = Resources.Load<Material>(MaterialResource);
            Shader shader = template != null ? template.shader : Shader.Find(ShaderName);
            if (shader == null)
            {
                // 保底(看不穿牆,但至少不洋紅):正式流程由 EffectsInfraSetup 在 Resources 建材質引用 shader
                Debug.LogWarning("MarkIndicator:找不到 " + ShaderName + ",標記改用 URP Unlit(無穿牆)");
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }
            _outlineMaterial = CreateMaterial(template, shader, "MarkOutline", 3100, 1f, 0f, 0f, 0f, 0f);
            _fillMaterial = CreateMaterial(template, shader, "MarkFill", 3101, 1f, 0f, 0f, 0f, 0.15f);
            // 身形:內部淡、邊緣亮(看起來像發光輪廓),慢速呼吸
            _ghostMaterial = CreateMaterial(template, shader, "MarkSilhouette", 3099, 0.22f, 2.2f, 1.1f, 0f, 0.3f);
            _ghostMaterial.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Back);
            _lineMaterial = CreateMaterial(template, shader, "MarkLine", 3098, 1f, 0f, 0f, -1.6f, 0f);
            _dashTexture = CreateDashTexture();
            _lineMaterial.SetTexture("_BaseMap", _dashTexture);
            _lineMaterial.SetTextureScale("_BaseMap", new Vector2(1.6f, 1f));
            _diamondFillMesh = CreateDiamondMesh(false);
            _diamondOutlineMesh = CreateDiamondMesh(true);
            _capsuleMesh = LoadCapsuleMesh();
        }

        private static Material CreateMaterial(Material template, Shader shader, string materialName, int queue,
            float fillAlpha, float rimPower, float rimBoost, float scroll, float pulse)
        {
            Material material = template != null ? new Material(template) : new Material(shader);
            material.name = materialName;
            material.hideFlags = HideFlags.DontSave;
            material.renderQueue = queue;
            material.SetFloat("_FillAlpha", fillAlpha);
            material.SetFloat("_RimPower", rimPower);
            material.SetFloat("_RimBoost", rimBoost);
            material.SetFloat("_ScrollSpeed", scroll);
            material.SetFloat("_Pulse", pulse);
            material.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
            material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            return material;
        }

        // 虛線:前 55% 實、後段透明,沿線平鋪
        private static Texture2D CreateDashTexture()
        {
            const int width = 32;
            var texture = new Texture2D(width, 1, TextureFormat.RGBA32, false)
            {
                name = "MarkDash",
                hideFlags = HideFlags.DontSave,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[width];
            for (int x = 0; x < width; x++)
            {
                byte a = x < width * 0.55f ? (byte)255 : (byte)0;
                pixels[x] = new Color32(255, 255, 255, a);
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        // 菱形(寬 0.52 × 高 0.72)+ 下方小三角指向目標;outline = 放大 1.3 倍的外框環
        private static Mesh CreateDiamondMesh(bool outline)
        {
            const float halfWidth = 0.26f;
            const float halfHeight = 0.36f;
            var mesh = new Mesh { name = outline ? "MarkDiamondOutline" : "MarkDiamond", hideFlags = HideFlags.DontSave };
            if (outline)
            {
                const float grow = 1.3f;
                var vertices = new Vector3[8];
                for (int i = 0; i < 4; i++)
                {
                    Vector3 corner = DiamondCorner(i, halfWidth, halfHeight);
                    vertices[i] = corner * grow;
                    vertices[i + 4] = corner;
                }
                var triangles = new int[24];
                for (int i = 0; i < 4; i++)
                {
                    int next = (i + 1) % 4;
                    int t = i * 6;
                    triangles[t] = i;
                    triangles[t + 1] = next;
                    triangles[t + 2] = i + 4;
                    triangles[t + 3] = next;
                    triangles[t + 4] = next + 4;
                    triangles[t + 5] = i + 4;
                }
                mesh.vertices = vertices;
                mesh.triangles = triangles;
            }
            else
            {
                var vertices = new Vector3[8];
                vertices[0] = Vector3.zero;
                for (int i = 0; i < 4; i++)
                {
                    vertices[i + 1] = DiamondCorner(i, halfWidth, halfHeight);
                }
                // 下方指向三角
                vertices[5] = new Vector3(-0.12f, -halfHeight * 1.45f, 0f);
                vertices[6] = new Vector3(0.12f, -halfHeight * 1.45f, 0f);
                vertices[7] = new Vector3(0f, -halfHeight * 1.85f, 0f);
                mesh.vertices = vertices;
                mesh.triangles = new[] { 0, 1, 2, 0, 2, 3, 0, 3, 4, 0, 4, 1, 5, 7, 6 };
            }
            var uvs = new Vector2[mesh.vertexCount];
            mesh.uv = uvs;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Vector3 DiamondCorner(int index, float halfWidth, float halfHeight)
        {
            switch (index)
            {
                case 0: return new Vector3(0f, halfHeight, 0f);
                case 1: return new Vector3(halfWidth, 0f, 0f);
                case 2: return new Vector3(0f, -halfHeight, 0f);
                default: return new Vector3(-halfWidth, 0f, 0f);
            }
        }

        private static Mesh LoadCapsuleMesh()
        {
            var temp = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Mesh mesh = temp.GetComponent<MeshFilter>().sharedMesh;
            DestroyImmediate(temp);
            return mesh;
        }
    }
}

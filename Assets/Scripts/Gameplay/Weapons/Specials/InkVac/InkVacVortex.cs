using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Painting;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // Ink Vac 吸引漩渦的外觀(大家都看得到):從噴嘴往前的半透明錐(程序網格,長度/遠端半徑 = 實際判定)+
    // 三圈往噴嘴捲進去的環(LineRenderer,本地半徑 1 的圓,依所在位置的錐半徑縮放)。
    // 只有外觀;判定在 InkVacuum / InkVacModel。錐長變化時才重寫頂點(零配置)。
    public sealed class InkVacVortex : MonoBehaviour
    {
        private const int Segments = 24;
        private const float ConeAlpha = 0.16f;
        private const float RingAlpha = 0.55f;

        [SerializeField, Tooltip("錐的網格(執行期生成)")]
        private MeshFilter _cone;

        [SerializeField, Tooltip("往噴嘴捲的環(本地 XY 平面、半徑 1 的圓)")]
        private LineRenderer[] _rings;

        private Mesh _mesh;
        private Vector3[] _vertices;
        private Renderer _coneRenderer;
        private float _builtLength = -1f;
        private float _phase;
        private Team _team = Team.None;

        public float Length => _builtLength;

        private void Awake()
        {
            _coneRenderer = _cone != null ? _cone.GetComponent<Renderer>() : null;
            BuildMesh();
        }

        private void BuildMesh()
        {
            if (_cone == null)
            {
                return;
            }
            _mesh = new Mesh { name = "InkVacCone" };
            _mesh.MarkDynamic();
            int ring = Segments + 1;
            _vertices = new Vector3[ring * 2];
            var uv = new Vector2[ring * 2];
            var triangles = new int[Segments * 6];
            for (int i = 0; i < ring; i++)
            {
                uv[i] = new Vector2(i / (float)Segments, 0f);
                uv[ring + i] = new Vector2(i / (float)Segments, 1f);
            }
            for (int i = 0; i < Segments; i++)
            {
                int t = i * 6;
                triangles[t] = i;
                triangles[t + 1] = ring + i;
                triangles[t + 2] = i + 1;
                triangles[t + 3] = i + 1;
                triangles[t + 4] = ring + i;
                triangles[t + 5] = ring + i + 1;
            }
            WriteVertices(1f);
            _mesh.vertices = _vertices;
            _mesh.uv = uv;
            _mesh.triangles = triangles;
            _mesh.RecalculateBounds();
            _cone.sharedMesh = _mesh;
        }

        private void WriteVertices(float length)
        {
            int ring = Segments + 1;
            float near = InkVacModel.RadiusNear;
            float far = InkVacModel.RadiusAt(length);
            for (int i = 0; i < ring; i++)
            {
                float a = i * Mathf.PI * 2f / Segments;
                float c = Mathf.Cos(a);
                float s = Mathf.Sin(a);
                _vertices[i] = new Vector3(c * near, s * near, 0f);
                _vertices[ring + i] = new Vector3(c * far, s * far, length);
            }
        }

        // 每幀由 Ink Vac 更新:apex = 噴嘴口、axis = 單位方向、length = 目前錐長
        public void Show(Vector3 apex, Vector3 axis, float length, Team team)
        {
            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
            }
            transform.SetPositionAndRotation(apex, Quaternion.LookRotation(axis.sqrMagnitude > 1e-6f ? axis : Vector3.forward));
            if (team != _team)
            {
                _team = team;
                Color color = TeamPalette.ColorOf(team);
                if (_coneRenderer != null)
                {
                    TeamTint.Apply(_coneRenderer, color, ConeAlpha);
                }
                if (_rings != null)
                {
                    for (int i = 0; i < _rings.Length; i++)
                    {
                        if (_rings[i] != null)
                        {
                            TeamTint.Apply(_rings[i], Color.Lerp(color, Color.white, 0.25f), RingAlpha);
                        }
                    }
                }
            }
            length = Mathf.Max(0.05f, length);
            if (_mesh != null && Mathf.Abs(length - _builtLength) > 0.01f)
            {
                _builtLength = length;
                WriteVertices(length);
                _mesh.vertices = _vertices;
                _mesh.RecalculateBounds();
            }
            // 環從錐尖往噴嘴捲(每圈 0.45 秒走完全長,邊走邊轉)
            _phase += Time.deltaTime / 0.45f;
            if (_rings == null)
            {
                return;
            }
            for (int i = 0; i < _rings.Length; i++)
            {
                LineRenderer ringLine = _rings[i];
                if (ringLine == null)
                {
                    continue;
                }
                float u = 1f - Mathf.Repeat(_phase + i / (float)_rings.Length, 1f);
                float s = u * length;
                float r = InkVacModel.RadiusAt(s);
                Transform t = ringLine.transform;
                t.localPosition = new Vector3(0f, 0f, s);
                t.localRotation = Quaternion.Euler(0f, 0f, _phase * 220f + i * 40f);
                t.localScale = new Vector3(r, r, 1f);
            }
        }

        public void Hide()
        {
            if (gameObject.activeSelf)
            {
                gameObject.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (_mesh != null)
            {
                Destroy(_mesh);
            }
        }
    }
}

using System.Collections.Generic;
using SplatoonC.Core;
using SplatoonC.Core.Match.Ranked;
using SplatoonC.Gameplay.Painting;
using UnityEngine;

namespace SplatoonC.Gameplay.Match
{
    // 真格區域外觀(S3:區域以發光邊線圍起,顏色 = 佔領方或中立色;遠看邊線往上放光;被牆擋住時顯示虛線):
    //   Border  地面邊框帶(0.4m 寬,貼著區域地面內緣)
    //   Curtain 邊框往上的漸層光幕(2.4m,離相機越遠越明顯)
    //   XRay    被擋住的邊框以流動虛線穿牆顯示(ZTest Greater)
    // 全部用 MarkOverlay shader(無光照半透明);隊伍色以 MaterialPropertyBlock 染,不產生材質實例。
    // 程序化網格與貼圖在 Build 時建一次;每幀只更新顏色/透明度(不配置記憶體)。
    public sealed class SplatZoneVisual : MonoBehaviour
    {
        private const float SampleSpacing = 0.5f;
        private const float BorderWidth = 0.4f;
        private const float CurtainHeight = 2.4f;
        private const float DashLength = 1.2f;
        // 佔領時光幕閃一下
        private const float FlashSeconds = 0.9f;

        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int FillAlphaId = Shader.PropertyToID("_FillAlpha");
        private static readonly int PulseId = Shader.PropertyToID("_Pulse");
        private static readonly int ScrollId = Shader.PropertyToID("_ScrollSpeed");

        private static Texture2D _borderTexture;
        private static Texture2D _dashTexture;

        private SplatZone _zone;
        private MeshRenderer _border;
        private MeshRenderer _curtain;
        private MeshRenderer _xray;
        private MaterialPropertyBlock _block;
        private Color _color = Color.white;
        private float _flashUntil;
        private Camera _camera;

        public Color CurrentColor => _color;
        public Renderer BorderRenderer => _border;
        public Renderer CurtainRenderer => _curtain;

        public void Build(SplatZone zone, ObjectiveAssets assets)
        {
            _zone = zone;
            _block = new MaterialPropertyBlock();
            List<Vector3> ring = SamplePerimeter(zone);
            if (ring.Count < 3 || assets == null)
            {
                return;
            }
            bool ccw = ZonePolygon.SignedArea(ToArray(zone.Polygon)) > 0f;
            _border = CreatePart("Border", BuildRibbon(ring, ccw, BorderWidth, 0.03f, 1f, "ZoneBorder"),
                assets.ZoneBorderMaterial);
            _curtain = CreatePart("Curtain", BuildCurtain(ring, "ZoneCurtain"), assets.ZoneCurtainMaterial);
            _xray = CreatePart("XRay", BuildRibbon(ring, ccw, BorderWidth * 0.6f, 0.05f, 1f / DashLength, "ZoneXRay"),
                assets.ZoneXRayMaterial);
            SetColor(Color.white);
        }

        private static Vector2[] ToArray(IReadOnlyList<Vector2> list)
        {
            var result = new Vector2[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                result[i] = list[i];
            }
            return result;
        }

        private MeshRenderer CreatePart(string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }

        // 沿多邊形邊界每 0.5m 取樣,貼到區域地面高度(射線往下找區域範圍內的地面)
        private static List<Vector3> SamplePerimeter(SplatZone zone)
        {
            var points = new List<Vector3>();
            IReadOnlyList<Vector2> poly = zone.Polygon;
            int mask = ~GameLayers.InklingMask;
            float lastY = zone.Center.y;
            for (int i = 0; i < poly.Count; i++)
            {
                Vector2 a = poly[i];
                Vector2 b = poly[(i + 1) % poly.Count];
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / SampleSpacing));
                for (int k = 0; k < steps; k++)
                {
                    Vector2 p = Vector2.Lerp(a, b, k / (float)steps);
                    var from = new Vector3(p.x, zone.MaxY + 0.3f, p.y);
                    float y = lastY;
                    if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, zone.MaxY - zone.MinY + 1.2f, mask,
                            QueryTriggerInteraction.Ignore))
                    {
                        y = hit.point.y;
                    }
                    lastY = y;
                    points.Add(new Vector3(p.x, y, p.y));
                }
            }
            return points;
        }

        // 地面帶:每條邊各自一段四邊形條(外緣 = 邊界、往區域內側縮 width);u = 周長距離 × uScale、v = 外 0 → 內 1
        private static Mesh BuildRibbon(List<Vector3> ring, bool ccw, float width, float lift, float uScale, string name)
        {
            int n = ring.Count;
            var vertices = new List<Vector3>(n * 4);
            var uvs = new List<Vector2>(n * 4);
            var normals = new List<Vector3>(n * 4);
            var triangles = new List<int>(n * 6);
            float distance = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector3 a = ring[i];
                Vector3 b = ring[(i + 1) % n];
                Vector3 d = b - a;
                d.y = 0f;
                float length = d.magnitude;
                if (length < 1e-4f)
                {
                    continue;
                }
                d /= length;
                // 逆時針(俯視 x-z)時內側在行進方向左邊
                Vector3 inward = ccw ? new Vector3(-d.z, 0f, d.x) : new Vector3(d.z, 0f, -d.x);
                int start = vertices.Count;
                vertices.Add(a + Vector3.up * lift);
                vertices.Add(a + inward * width + Vector3.up * lift);
                vertices.Add(b + Vector3.up * lift);
                vertices.Add(b + inward * width + Vector3.up * lift);
                uvs.Add(new Vector2(distance * uScale, 0f));
                uvs.Add(new Vector2(distance * uScale, 1f));
                uvs.Add(new Vector2((distance + length) * uScale, 0f));
                uvs.Add(new Vector2((distance + length) * uScale, 1f));
                for (int k = 0; k < 4; k++)
                {
                    normals.Add(Vector3.up);
                }
                triangles.Add(start);
                triangles.Add(start + 1);
                triangles.Add(start + 2);
                triangles.Add(start + 2);
                triangles.Add(start + 1);
                triangles.Add(start + 3);
                distance += length;
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // 光幕:沿邊界往上的直立條,頂點色 alpha 下 1 → 上 0(MarkOverlay 乘上頂點色)
        private static Mesh BuildCurtain(List<Vector3> ring, string name)
        {
            int n = ring.Count;
            var vertices = new List<Vector3>(n * 4);
            var colors = new List<Color>(n * 4);
            var uvs = new List<Vector2>(n * 4);
            var normals = new List<Vector3>(n * 4);
            var triangles = new List<int>(n * 6);
            var bottom = new Color(1f, 1f, 1f, 1f);
            var top = new Color(1f, 1f, 1f, 0f);
            for (int i = 0; i < n; i++)
            {
                Vector3 a = ring[i];
                Vector3 b = ring[(i + 1) % n];
                Vector3 side = b - a;
                side.y = 0f;
                if (side.sqrMagnitude < 1e-8f)
                {
                    continue;
                }
                Vector3 normal = Vector3.Cross(Vector3.up, side.normalized);
                int start = vertices.Count;
                vertices.Add(a);
                vertices.Add(a + Vector3.up * CurtainHeight);
                vertices.Add(b);
                vertices.Add(b + Vector3.up * CurtainHeight);
                colors.Add(bottom);
                colors.Add(top);
                colors.Add(bottom);
                colors.Add(top);
                uvs.Add(new Vector2(0.5f, 0f));
                uvs.Add(new Vector2(0.5f, 1f));
                uvs.Add(new Vector2(0.5f, 0f));
                uvs.Add(new Vector2(0.5f, 1f));
                for (int k = 0; k < 4; k++)
                {
                    normals.Add(normal);
                }
                triangles.Add(start);
                triangles.Add(start + 1);
                triangles.Add(start + 2);
                triangles.Add(start + 2);
                triangles.Add(start + 1);
                triangles.Add(start + 3);
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // 邊框帶貼圖:v 方向中段實心、兩側柔邊,再往內一條細亮線(S3 區域邊線的雙線感)
        private static Texture2D BorderTexture()
        {
            if (_borderTexture != null)
            {
                return _borderTexture;
            }
            const int w = 4;
            const int h = 64;
            _borderTexture = new Texture2D(w, h, TextureFormat.RGBA32, false, true)
            {
                name = "ZoneBorderTex",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                float v = (y + 0.5f) / h;
                // 外緣實心帶 0~0.55、內側細線 0.72~0.8、其餘透明(柔邊)
                float band = Mathf.Clamp01((0.55f - v) / 0.06f) * Mathf.Clamp01((v + 0.02f) / 0.06f);
                float line = Mathf.Clamp01(1f - Mathf.Abs(v - 0.76f) / 0.05f);
                byte a = (byte)(Mathf.Clamp01(Mathf.Max(band, line * 0.85f)) * 255f);
                for (int x = 0; x < w; x++)
                {
                    pixels[y * w + x] = new Color32(255, 255, 255, a);
                }
            }
            _borderTexture.SetPixels32(pixels);
            _borderTexture.Apply(false, true);
            return _borderTexture;
        }

        // 虛線貼圖:u 方向 55% 實、45% 空
        private static Texture2D DashTexture()
        {
            if (_dashTexture != null)
            {
                return _dashTexture;
            }
            const int w = 64;
            const int h = 4;
            _dashTexture = new Texture2D(w, h, TextureFormat.RGBA32, false, true)
            {
                name = "ZoneDashTex",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[w * h];
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w;
                float on = Mathf.Clamp01((0.55f - u) / 0.04f) * Mathf.Clamp01(u / 0.04f);
                byte a = (byte)(on * 255f);
                for (int y = 0; y < h; y++)
                {
                    pixels[y * w + x] = new Color32(255, 255, 255, a);
                }
            }
            _dashTexture.SetPixels32(pixels);
            _dashTexture.Apply(false, true);
            return _dashTexture;
        }

        // 顏色(佔領方 / 中立色);只有變了才重設 MPB
        public void SetColor(Color color)
        {
            _color = color;
            if (_border != null)
            {
                _block.Clear();
                _block.SetTexture(BaseMapId, BorderTexture());
                // 邊框偏白(S3 區域邊線發亮):腳下已是同色墨時也看得出界線
                _block.SetColor(BaseColorId, Color.Lerp(color, Color.white, 0.5f));
                _block.SetFloat(FillAlphaId, 0.95f);
                _border.SetPropertyBlock(_block);
            }
            if (_xray != null)
            {
                _block.Clear();
                _block.SetTexture(BaseMapId, DashTexture());
                _block.SetColor(BaseColorId, color);
                _block.SetFloat(FillAlphaId, 0.55f);
                _block.SetFloat(ScrollId, 0.35f);
                _xray.SetPropertyBlock(_block);
            }
            UpdateCurtain();
        }

        public void Flash()
        {
            _flashUntil = Time.time + FlashSeconds;
        }

        private void LateUpdate()
        {
            if (_curtain != null)
            {
                UpdateCurtain();
            }
        }

        // 光幕:近看淡(不擋視線)、遠看明顯(S3「遠看往上放光」);佔領瞬間閃亮後退回
        private void UpdateCurtain()
        {
            if (_curtain == null)
            {
                return;
            }
            if (_camera == null)
            {
                _camera = Camera.main;
            }
            float distance = _camera != null && _zone != null ? Vector3.Distance(_camera.transform.position, _zone.Center) : 30f;
            float alpha = Mathf.Lerp(0.22f, 0.7f, Mathf.InverseLerp(12f, 55f, distance));
            float flash = Mathf.Clamp01((_flashUntil - Time.time) / FlashSeconds);
            alpha = Mathf.Lerp(alpha, 1f, flash);
            _block.Clear();
            _block.SetColor(BaseColorId, Color.Lerp(_color, Color.white, 0.2f + flash * 0.5f));
            _block.SetFloat(FillAlphaId, alpha);
            _curtain.SetPropertyBlock(_block);
        }
    }
}

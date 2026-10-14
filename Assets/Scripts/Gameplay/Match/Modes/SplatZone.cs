using System.Collections.Generic;
using SplatoonC.Core;
using SplatoonC.Core.Match.Ranked;
using SplatoonC.Core.Painting;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Painting;
using UnityEngine;

namespace SplatoonC.Gameplay.Match
{
    // 真格區域的一個區域(執行期由 ObjectiveModeController 依 ModeLayout 生成,場景裡沒有):
    //   - 開賽時收集「俯視在多邊形內、高度在範圍內、從上方看得到、沒被壓住」的地面歸屬格(可塗表面的 CPU 網格)
    //   - 每幀加總各隊的地面投影面積佔比(與塗地判定同一套網格,牆不算、斜坡算投影)
    //   - 佔領時瞬間重塗:歸屬格直接改成佔領方 + InkZoneFill shader 把多邊形內的地面整片寫進 ink map
    //     (多邊形在開賽時光柵化成 0.1m/texel 的俯視遮罩貼圖;每個區域一個材質實例,參數全是材質屬性)
    // 外觀(邊框、光幕、穿牆虛線)在 SplatZoneVisual。
    public sealed class SplatZone : MonoBehaviour
    {
        private struct Cell
        {
            public InkOwnershipGrid Grid;
            public int X;
            public int Z;
            public float Weight;
            public Vector3 Position;
        }

        // 「從上方看得到」的射線起點高度(區域最高點之上)
        private const float SkyProbeHeight = 12f;
        // 重塗遮罩解析度(公尺/texel,與可塗方塊的 ink map 同密度)
        private const float MaskTexel = 0.1f;
        // 遮罩四周留空的 texel 數(雙線性取樣不會從邊界外漏進來)
        private const int MaskPadding = 2;

        private static readonly int ZoneMaskId = Shader.PropertyToID("_ZoneMask");
        private static readonly int ZoneBoundsId = Shader.PropertyToID("_ZoneBounds");
        private static readonly int ZoneRangeId = Shader.PropertyToID("_ZoneRange");
        private static readonly int ZoneChannelId = Shader.PropertyToID("_ZoneChannel");

        private readonly List<Cell> _cells = new List<Cell>();
        private readonly List<PaintableSurface> _surfaces = new List<PaintableSurface>();
        private Vector2[] _polygon;
        private Texture2D _mask;
        private float _minY;
        private float _maxY;
        private float _totalWeight;
        private Material _fillMaterial;

        public int Index { get; private set; }
        public string ZoneName { get; private set; }
        public IReadOnlyList<Vector2> Polygon => _polygon;
        public float MinY => _minY;
        public float MaxY => _maxY;

        // 區域地面投影面積(m²)
        public float Area => _totalWeight;
        public int CellCount => _cells.Count;

        // 區域中心(多邊形重心,高度 = 區域地面平均高度)
        public Vector3 Center { get; private set; }

        // 最近一次取樣的各隊佔比(0~1)
        public float AlphaRatio { get; private set; }
        public float BravoRatio { get; private set; }

        // 目前佔領方(規則回報;外觀與 HUD 讀)
        public Team Owner { get; set; }

        public SplatZoneVisual Visual { get; private set; }

        // 佔領重塗的 ink map 材質已就緒
        public bool FillReady => _fillMaterial != null;

        public float RatioOf(Team team)
        {
            return team == Team.Alpha ? AlphaRatio : team == Team.Bravo ? BravoRatio : 0f;
        }

        // 生成:收集歸屬格並建立外觀。須在 TurfJudge.ClearAll(排除被壓住的格)之後呼叫。
        public void Build(int index, ModeLayout.ZoneArea area, Shader fillShader, ObjectiveAssets assets)
        {
            Index = index;
            ZoneName = string.IsNullOrEmpty(area.Name) ? "Zone" + index : area.Name;
            _minY = area.MinY;
            _maxY = area.MaxY;
            _polygon = area.Polygon ?? new Vector2[0];
            CollectCells();
            if (fillShader != null && _polygon.Length >= 3)
            {
                BuildFillMaterial(fillShader);
            }
            Visual = gameObject.AddComponent<SplatZoneVisual>();
            Visual.Build(this, assets);
        }

        private void OnDestroy()
        {
            if (_fillMaterial != null)
            {
                Destroy(_fillMaterial);
            }
            if (_mask != null)
            {
                Destroy(_mask);
            }
        }

        // 多邊形 → 俯視遮罩貼圖(texel 中心在多邊形內 = 1)+ 本區域專用的重塗材質
        private void BuildFillMaterial(Shader shader)
        {
            // shader 不支援時(錯誤 shader 會把整張 ink map 寫成洋紅 = R 通道)寧可不畫,只改歸屬格
            if (!shader.isSupported)
            {
                Debug.LogError($"SplatZone:{shader.name} 在此平台不支援,佔領時不重畫墨", this);
                return;
            }
            Vector2 min = _polygon[0];
            Vector2 max = _polygon[0];
            for (int i = 1; i < _polygon.Length; i++)
            {
                min = Vector2.Min(min, _polygon[i]);
                max = Vector2.Max(max, _polygon[i]);
            }
            min -= Vector2.one * (MaskPadding * MaskTexel);
            max += Vector2.one * (MaskPadding * MaskTexel);
            int width = Mathf.Clamp(Mathf.CeilToInt((max.x - min.x) / MaskTexel), 4, 2048);
            int height = Mathf.Clamp(Mathf.CeilToInt((max.y - min.y) / MaskTexel), 4, 2048);
            Vector2 size = new Vector2(width * MaskTexel, height * MaskTexel);
            _mask = new Texture2D(width, height, TextureFormat.R8, false, true)
            {
                name = ZoneName + "_Mask",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new byte[width * height];
            for (int y = 0; y < height; y++)
            {
                float z = min.y + (y + 0.5f) * MaskTexel;
                for (int x = 0; x < width; x++)
                {
                    float wx = min.x + (x + 0.5f) * MaskTexel;
                    pixels[y * width + x] = ZonePolygon.Contains(_polygon, wx, z) ? (byte)255 : (byte)0;
                }
            }
            _mask.SetPixelData(pixels, 0);
            _mask.Apply(false, true);
            _fillMaterial = new Material(shader) { name = ZoneName + "_Fill" };
            _fillMaterial.SetTexture(ZoneMaskId, _mask);
            _fillMaterial.SetVector(ZoneBoundsId, new Vector4(min.x, min.y, 1f / size.x, 1f / size.y));
            _fillMaterial.SetVector(ZoneRangeId, new Vector4(_minY, _maxY, PaintableSurface.FloorNormalY, 0f));
        }

        private void CollectCells()
        {
            _cells.Clear();
            _surfaces.Clear();
            _totalWeight = 0f;
            if (_polygon.Length < 3)
            {
                return;
            }
            Vector2 min = _polygon[0];
            Vector2 max = _polygon[0];
            for (int i = 1; i < _polygon.Length; i++)
            {
                min = Vector2.Min(min, _polygon[i]);
                max = Vector2.Max(max, _polygon[i]);
            }
            int mask = ~GameLayers.InklingMask;
            float heightSum = 0f;
            var surfaces = PaintableSurface.Active;
            for (int s = 0; s < surfaces.Count; s++)
            {
                PaintableSurface surface = surfaces[s];
                Bounds b = surface.WorldBounds;
                if (b.max.x < min.x || b.min.x > max.x || b.max.z < min.y || b.min.z > max.y
                    || b.max.y < _minY - 0.1f || b.min.y > _maxY + 0.1f)
                {
                    continue;
                }
                int before = _cells.Count;
                for (int face = 0; face < surface.FaceCount; face++)
                {
                    SurfaceFace f = surface.GetWorldFace(face);
                    if (f.Normal.y < PaintableSurface.FloorNormalY)
                    {
                        continue;
                    }
                    InkOwnershipGrid grid = surface.GetFaceGrid(face);
                    float weight = grid.CellSize * grid.CellSize * f.Normal.y;
                    for (int cz = 0; cz < grid.Height; cz++)
                    {
                        for (int cx = 0; cx < grid.Width; cx++)
                        {
                            if (grid.CellAt(cx, cz) == InkOwnershipGrid.Blocked)
                            {
                                continue;
                            }
                            var plane = new Vector2(grid.CellCenterX(cx), grid.CellCenterZ(cz));
                            if (!f.Contains(plane, 0f))
                            {
                                continue;
                            }
                            Vector3 p = f.FromPlane(plane);
                            if (p.y < _minY || p.y > _maxY || !ZonePolygon.Contains(_polygon, p.x, p.z))
                            {
                                continue;
                            }
                            // 從上方看得到(塔、斜坡上段蓋住的格不算:塗不到)
                            var from = new Vector3(p.x, _maxY + SkyProbeHeight, p.z);
                            if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, from.y - p.y + 0.5f, mask,
                                    QueryTriggerInteraction.Ignore) && hit.point.y > p.y + 0.3f)
                            {
                                continue;
                            }
                            _cells.Add(new Cell { Grid = grid, X = cx, Z = cz, Weight = weight, Position = p });
                            _totalWeight += weight;
                            heightSum += p.y * weight;
                        }
                    }
                }
                if (_cells.Count > before)
                {
                    _surfaces.Add(surface);
                }
            }
            Vector2 centroid = ZonePolygon.Centroid(_polygon);
            float y = _totalWeight > 0f ? heightSum / _totalWeight : _minY;
            Center = new Vector3(centroid.x, y, centroid.y);
        }

        // 取樣:各隊地面投影面積佔比
        public void Sample()
        {
            if (_totalWeight <= 0f)
            {
                AlphaRatio = 0f;
                BravoRatio = 0f;
                return;
            }
            float alpha = 0f;
            float bravo = 0f;
            for (int i = 0; i < _cells.Count; i++)
            {
                Cell c = _cells[i];
                byte owner = c.Grid.CellAt(c.X, c.Z);
                if (owner == (byte)Team.Alpha)
                {
                    alpha += c.Weight;
                }
                else if (owner == (byte)Team.Bravo)
                {
                    bravo += c.Weight;
                }
            }
            AlphaRatio = alpha / _totalWeight;
            BravoRatio = bravo / _totalWeight;
        }

        // 點在區域內(俯視多邊形 + 高度範圍,容差 0.3m)
        public bool Contains(Vector3 point)
        {
            return point.y >= _minY - 0.3f && point.y <= _maxY + 0.3f && ZonePolygon.Contains(_polygon, point.x, point.z);
        }

        // 俯視距離區域邊界多遠(m;區域內外皆為正值)
        public float DistanceToEdge(Vector3 point)
        {
            return ZonePolygon.DistanceToEdge(_polygon, point.x, point.z);
        }

        // 第 i 個區域格的世界座標(測試/特效)
        public Vector3 CellPosition(int i)
        {
            return _cells[i].Position;
        }

        // 佔領瞬間重塗:歸屬格全部改成 team,ink map 以多邊形遮罩整片寫入(不計入任何人的塗地點數、不充大招)
        public void Recolor(Team team)
        {
            if (team == Team.None)
            {
                return;
            }
            byte id = (byte)team;
            for (int i = 0; i < _cells.Count; i++)
            {
                Cell c = _cells[i];
                c.Grid.SetCellOwner(c.X, c.Z, id);
            }
            if (_fillMaterial != null)
            {
                _fillMaterial.SetVector(ZoneChannelId, TeamPalette.ChannelOf(team));
                for (int s = 0; s < _surfaces.Count; s++)
                {
                    if (_surfaces[s] != null)
                    {
                        _surfaces[s].DrawIntoInkMap(_fillMaterial);
                    }
                }
            }
            AlphaRatio = team == Team.Alpha ? 1f : 0f;
            BravoRatio = team == Team.Bravo ? 1f : 0f;
            SpawnCaptureSplashes(team);
        }

        // 佔領特效:區域內散佈幾朵墨花
        private void SpawnCaptureSplashes(Team team)
        {
            InkSplashFxPool pool = InkSplashFxPool.Instance;
            if (pool == null || _cells.Count == 0)
            {
                return;
            }
            Color color = TeamPalette.ColorOf(team);
            const int count = 14;
            for (int i = 0; i < count; i++)
            {
                // 決定性散佈:黃金比步進取格
                int index = (int)(Mathf.Repeat(i * 0.618034f + 0.31f, 1f) * (_cells.Count - 1));
                pool.Spawn(_cells[index].Position + Vector3.up * 0.05f, Vector3.up, color);
            }
        }
    }
}

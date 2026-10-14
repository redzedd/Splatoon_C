using UnityEngine;

namespace SplatoonC.Core.Painting
{
    // 腳下墨歸屬的 CPU 側粗網格——塗色時同步標記,查詢 O(1),不做 GPU readback。
    // 0 = 無墨;非零 = 隊伍 id(見 Core.Team)。座標是所屬面的平面座標(公尺)。
    // 各隊格數隨標記增量維護:佔地判定直接加總,不必掃整張網格。
    public sealed class InkOwnershipGrid
    {
        // 隊伍 id 超過此值不列入計數(歸屬照樣記錄)
        public const int CountedTeams = 4;

        // 被其他物件壓住、永遠塗不到的格:不計入總格數、塗墨跳過、查詢回 0(佔地 100% 才可能達成)
        public const byte Blocked = 255;

        private int _blockedCount;

        private readonly byte[] _cells;
        private readonly int[] _counts = new int[CountedTeams];
        private readonly int _width;
        private readonly int _height;
        private readonly float _minX;
        private readonly float _minZ;
        private readonly float _cellSize;

        public InkOwnershipGrid(float minX, float minZ, float sizeX, float sizeZ, float cellSize)
        {
            _minX = minX;
            _minZ = minZ;
            _cellSize = Mathf.Max(cellSize, 0.01f);
            _width = Mathf.Max(1, Mathf.CeilToInt(sizeX / _cellSize));
            _height = Mathf.Max(1, Mathf.CeilToInt(sizeZ / _cellSize));
            _cells = new byte[_width * _height];
            _counts[0] = _cells.Length;
        }

        public int TotalCells => _cells.Length - _blockedCount;

        public int Width => _width;
        public int Height => _height;

        // 格中心的平面座標
        public float CellCenterX(int cx) => _minX + (cx + 0.5f) * _cellSize;
        public float CellCenterZ(int cz) => _minZ + (cz + 0.5f) * _cellSize;

        public void BlockCell(int cx, int cz)
        {
            if (cx < 0 || cx >= _width || cz < 0 || cz >= _height)
            {
                return;
            }
            int index = cz * _width + cx;
            byte previous = _cells[index];
            if (previous == Blocked)
            {
                return;
            }
            if (previous < CountedTeams)
            {
                _counts[previous]--;
            }
            _cells[index] = Blocked;
            _blockedCount++;
        }

        public float CellSize => _cellSize;

        // 某隊目前擁有的格數(team 0 = 無墨格)。
        public int CountOf(byte team)
        {
            return team < CountedTeams ? _counts[team] : 0;
        }

        // 清墨(被壓住的格維持封鎖)
        public void Clear()
        {
            for (int i = 0; i < _cells.Length; i++)
            {
                if (_cells[i] != Blocked)
                {
                    _cells[i] = 0;
                }
            }
            System.Array.Clear(_counts, 0, _counts.Length);
            _counts[0] = _cells.Length - _blockedCount;
        }

        private void SetCell(int index, byte team)
        {
            byte previous = _cells[index];
            if (previous < CountedTeams)
            {
                _counts[previous]--;
            }
            if (team < CountedTeams)
            {
                _counts[team]++;
            }
            _cells[index] = team;
        }

        // 回傳這次「新變成 team」的格數(重塗自家墨回 0;覆蓋敵墨算新增):大招能量只算新塗到的面積。
        public int MarkCircle(float worldX, float worldZ, float radius, byte team)
        {
            int newlyOwned = 0;
            // 半徑小於 cell 時圓內可能不含任何 cell 中心(滴墨 0.22m vs cell 0.25m),
            // 先無條件標記圓心那格:任何塗色都該讓落點成為自家墨。
            int originCellX = Mathf.FloorToInt((worldX - _minX) / _cellSize);
            int originCellZ = Mathf.FloorToInt((worldZ - _minZ) / _cellSize);
            if (originCellX >= 0 && originCellX < _width && originCellZ >= 0 && originCellZ < _height)
            {
                int originIndex = originCellZ * _width + originCellX;
                if (_cells[originIndex] != team && _cells[originIndex] != Blocked)
                {
                    SetCell(originIndex, team);
                    newlyOwned++;
                }
            }

            int minCx = Mathf.FloorToInt((worldX - radius - _minX) / _cellSize);
            int maxCx = Mathf.FloorToInt((worldX + radius - _minX) / _cellSize);
            int minCz = Mathf.FloorToInt((worldZ - radius - _minZ) / _cellSize);
            int maxCz = Mathf.FloorToInt((worldZ + radius - _minZ) / _cellSize);
            float radiusSq = radius * radius;

            for (int cz = minCz; cz <= maxCz; cz++)
            {
                if (cz < 0 || cz >= _height)
                {
                    continue;
                }
                for (int cx = minCx; cx <= maxCx; cx++)
                {
                    if (cx < 0 || cx >= _width)
                    {
                        continue;
                    }
                    float centerX = _minX + (cx + 0.5f) * _cellSize;
                    float centerZ = _minZ + (cz + 0.5f) * _cellSize;
                    float dx = centerX - worldX;
                    float dz = centerZ - worldZ;
                    if (dx * dx + dz * dz <= radiusSq)
                    {
                        int index = cz * _width + cx;
                        if (_cells[index] != team && _cells[index] != Blocked)
                        {
                            SetCell(index, team);
                            newlyOwned++;
                        }
                    }
                }
            }
            return newlyOwned;
        }

        // 某格的原始值(含 Blocked = 255);界外回 Blocked。模式目標區域(真格區域)逐格統計用。
        public byte CellAt(int cx, int cz)
        {
            if (cx < 0 || cx >= _width || cz < 0 || cz >= _height)
            {
                return Blocked;
            }
            return _cells[cz * _width + cx];
        }

        // 直接改某格的歸屬(被壓住的格不動);回傳是否有變。區域佔領「瞬間重塗」用——不經過圓形標記。
        public bool SetCellOwner(int cx, int cz, byte team)
        {
            if (cx < 0 || cx >= _width || cz < 0 || cz >= _height || team == Blocked)
            {
                return false;
            }
            int index = cz * _width + cx;
            byte previous = _cells[index];
            if (previous == Blocked || previous == team)
            {
                return false;
            }
            SetCell(index, team);
            return true;
        }

        // 界外一律回 0(無墨)。
        public byte Sample(float worldX, float worldZ)
        {
            int cx = Mathf.FloorToInt((worldX - _minX) / _cellSize);
            int cz = Mathf.FloorToInt((worldZ - _minZ) / _cellSize);
            if (cx < 0 || cx >= _width || cz < 0 || cz >= _height)
            {
                return 0;
            }
            byte value = _cells[cz * _width + cx];
            return value == Blocked ? (byte)0 : value;
        }
    }
}

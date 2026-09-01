using UnityEngine;

namespace SplatoonC.Core.Painting
{
    // 腳下墨歸屬的 CPU 側粗網格——塗色時同步標記,查詢 O(1),不做 GPU readback。
    // 0 = 無墨;非零 = 隊伍 id(M1 單隊固定 1)。水平面(XZ)投影。
    public sealed class InkOwnershipGrid
    {
        private readonly byte[] _cells;
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
        }

        public void MarkCircle(float worldX, float worldZ, float radius, byte team)
        {
            // 半徑小於 cell 時圓內可能不含任何 cell 中心(滴墨 0.22m vs cell 0.25m),
            // 先無條件標記圓心那格:任何塗色都該讓落點成為自家墨。
            int originCellX = Mathf.FloorToInt((worldX - _minX) / _cellSize);
            int originCellZ = Mathf.FloorToInt((worldZ - _minZ) / _cellSize);
            if (originCellX >= 0 && originCellX < _width && originCellZ >= 0 && originCellZ < _height)
            {
                _cells[originCellZ * _width + originCellX] = team;
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
                        _cells[cz * _width + cx] = team;
                    }
                }
            }
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
            return _cells[cz * _width + cx];
        }
    }
}

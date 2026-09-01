using SplatoonC.Core.Painting;
using UnityEngine;

namespace SplatoonC.Gameplay.Painting
{
    // 場景級腳下墨網格單例:PaintableSurface.Paint 自動登記,烏賊態查詢腳下歸屬。
    public sealed class InkWorld : MonoBehaviour
    {
        public static InkWorld Instance { get; private set; }

        [SerializeField, Tooltip("場地範圍中心(XZ)")]
        private Vector2 _worldCenter = Vector2.zero;

        [SerializeField, Tooltip("場地範圍大小(XZ,公尺)")]
        private Vector2 _worldSize = new Vector2(50f, 50f);

        [SerializeField, Tooltip("網格 cell 大小(公尺)")]
        private float _cellSize = 0.5f;

        private InkOwnershipGrid _grid;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError("InkWorld:場景中有多個實例,只保留第一個", this);
                enabled = false;
                return;
            }
            Instance = this;
            _grid = new InkOwnershipGrid(
                _worldCenter.x - _worldSize.x * 0.5f,
                _worldCenter.y - _worldSize.y * 0.5f,
                _worldSize.x,
                _worldSize.y,
                _cellSize);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void RegisterSplat(Vector3 worldPosition, float radius)
        {
            _grid.MarkCircle(worldPosition.x, worldPosition.z, radius, 1);
        }

        public byte SampleOwnership(Vector3 worldPosition)
        {
            return _grid.Sample(worldPosition.x, worldPosition.z);
        }
    }
}

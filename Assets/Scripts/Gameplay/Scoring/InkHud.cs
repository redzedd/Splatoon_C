using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Scoring
{
    // 墨量條 HUD:fill 寬度隨墨量縮放(免 sprite fill 機制,程式控寬)。
    public sealed class InkHud : MonoBehaviour
    {
        [SerializeField, Tooltip("墨量來源(Player 上的 PlayerInkTank)")]
        private PlayerInkTank _tank;

        [SerializeField, Tooltip("填充條 RectTransform")]
        private RectTransform _fill;

        [SerializeField, Tooltip("滿墨時的條寬(像素)")]
        private float _fullWidth = 240f;

        private float _shownNormalized = -1f;

        private void Update()
        {
            if (_tank == null || _fill == null)
            {
                return;
            }
            float normalized = _tank.Normalized;
            if (Mathf.Abs(normalized - _shownNormalized) < 0.002f)
            {
                return;
            }
            _shownNormalized = normalized;
            Vector2 size = _fill.sizeDelta;
            size.x = _fullWidth * normalized;
            _fill.sizeDelta = size;
        }
    }
}

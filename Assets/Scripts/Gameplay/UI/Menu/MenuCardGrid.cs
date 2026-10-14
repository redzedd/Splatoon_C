using System.Collections.Generic;
using SplatoonC.Core.Menu;
using UnityEngine;
using UnityEngine.UI;

namespace SplatoonC.Gameplay.UI.Menu
{
    // 卡片格(武器、場地):MenuGridLayout 決定欄數與卡片大小(少量放大置中、多量縮小捲動),
    // 卡片之間用明確導覽(方向鍵走格子,不會跳到別的區塊),選到的卡片自動捲進可視範圍。
    public sealed class MenuCardGrid
    {
        private readonly RectTransform _viewport;
        private readonly RectTransform _content;
        private readonly ScrollRect _scroll;
        private readonly List<MenuButton> _cards = new List<MenuButton>();
        private GridLayoutResult _layout;
        private float _targetScroll;

        public RectTransform Content => _content;
        public RectTransform Viewport => _viewport;
        public IReadOnlyList<MenuButton> Cards => _cards;
        public GridLayoutResult Layout => _layout;
        public float Width { get; }
        public float Height { get; }

        public MenuCardGrid(Transform parent, float x, float y, float width, float height)
        {
            Width = width;
            Height = height;
            _viewport = MenuWidgets.TopLeft("Grid", parent, x, y, width, height);
            // 放大的選取卡片(+4.5%)與外框會超出格子一點:遮罩四周留邊
            var mask = _viewport.gameObject.AddComponent<RectMask2D>();
            mask.padding = new Vector4(-24f, -24f, -24f, -24f);
            var hit = _viewport.gameObject.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            _content = MenuWidgets.TopLeft("Content", _viewport, 0f, 0f, width, height);
            _scroll = _viewport.gameObject.AddComponent<ScrollRect>();
            _scroll.viewport = _viewport;
            _scroll.content = _content;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.inertia = false;
            _scroll.scrollSensitivity = 60f;
        }

        // 清掉舊卡片並算新版面;呼叫端依 CellRect 逐張建立卡片後 Add
        public GridLayoutResult Begin(int count, float spacing, float aspect, float minCell, float maxCell)
        {
            Clear();
            _layout = MenuGridLayout.Compute(count, Width, Height, spacing, aspect, minCell, maxCell);
            _content.sizeDelta = new Vector2(Width, Mathf.Max(Height, _layout.ContentHeight));
            _content.anchoredPosition = Vector2.zero;
            _targetScroll = 0f;
            _scroll.enabled = _layout.Scrolls;
            return _layout;
        }

        public Rect CellRect(int index)
        {
            Vector2 topLeft = MenuGridLayout.CellTopLeft(_layout, index);
            return new Rect(topLeft.x, topLeft.y, _layout.CellWidth, _layout.CellHeight);
        }

        public void Add(MenuButton card)
        {
            int index = _cards.Count;
            _cards.Add(card);
            card.Focused += () => Reveal(index);
        }

        // 卡片間的明確導覽;走出邊界時接到 up/down/left/right(可為 null)
        public void LinkNavigation(Selectable up, Selectable down, Selectable left, Selectable right)
        {
            int columns = _layout.Columns;
            int count = _cards.Count;
            for (int i = 0; i < count; i++)
            {
                var nav = new Navigation { mode = Navigation.Mode.Explicit };
                nav.selectOnLeft = Pick(MenuGridLayout.Neighbor(i, count, columns, -1, 0), left);
                nav.selectOnRight = Pick(MenuGridLayout.Neighbor(i, count, columns, 1, 0), right);
                nav.selectOnUp = Pick(MenuGridLayout.Neighbor(i, count, columns, 0, -1), up);
                nav.selectOnDown = Pick(MenuGridLayout.Neighbor(i, count, columns, 0, 1), down);
                _cards[i].navigation = nav;
            }
        }

        private Selectable Pick(int index, Selectable fallback)
        {
            return index >= 0 ? _cards[index] : fallback;
        }

        public void Clear()
        {
            foreach (MenuButton card in _cards)
            {
                if (card != null)
                {
                    Object.Destroy(card.gameObject);
                }
            }
            _cards.Clear();
        }

        // 讓第 index 張完整可見(平滑捲動在 Tick 裡做)
        public void Reveal(int index)
        {
            if (!_layout.Scrolls || index < 0 || index >= _cards.Count)
            {
                return;
            }
            Rect cell = CellRect(index);
            _targetScroll = MenuGridLayout.ScrollToReveal(_content.anchoredPosition.y, Height, _layout.ContentHeight,
                cell.y, cell.height, 26f);
        }

        public void RevealImmediate(int index)
        {
            Reveal(index);
            _content.anchoredPosition = new Vector2(0f, _targetScroll);
        }

        public void Tick()
        {
            if (!_layout.Scrolls)
            {
                return;
            }
            // 使用者用滾輪/拖曳時不搶;只在導覽觸發 Reveal 後追目標
            Vector2 p = _content.anchoredPosition;
            if (Mathf.Abs(p.y - _targetScroll) > 0.5f && !MouseScrolling())
            {
                float k = 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime);
                _content.anchoredPosition = new Vector2(0f, Mathf.Lerp(p.y, _targetScroll, k));
                _scroll.velocity = Vector2.zero;
            }
            else
            {
                _targetScroll = p.y;
            }
        }

        // 滾輪捲動中不要被導覽的平滑捲動拉回去
        private static bool MouseScrolling()
        {
            UnityEngine.InputSystem.Mouse mouse = UnityEngine.InputSystem.Mouse.current;
            return mouse != null && Mathf.Abs(mouse.scroll.ReadValue().y) > 0.01f;
        }
    }
}

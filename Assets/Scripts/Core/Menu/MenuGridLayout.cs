using UnityEngine;

namespace SplatoonC.Core.Menu
{
    // 選單卡片格的版面(武器、場地):依卡片數量決定欄數與卡片大小。
    // 數量少時卡片放大並置中(1 把武器也不會縮在角落),數量多時縮到下限後改為垂直捲動。
    public readonly struct GridLayoutResult
    {
        public readonly int Count;
        public readonly int Columns;
        public readonly int Rows;
        public readonly float CellWidth;
        public readonly float CellHeight;
        public readonly float Spacing;
        // 內容總高(捲動區的 content 高度)
        public readonly float ContentHeight;
        // 整塊格子相對區域左上角的偏移(置中用)
        public readonly float OffsetX;
        public readonly float OffsetY;
        public readonly bool Scrolls;

        public GridLayoutResult(int count, int columns, int rows, float cellWidth, float cellHeight, float spacing,
            float contentHeight, float offsetX, float offsetY, bool scrolls)
        {
            Count = count;
            Columns = columns;
            Rows = rows;
            CellWidth = cellWidth;
            CellHeight = cellHeight;
            Spacing = spacing;
            ContentHeight = contentHeight;
            OffsetX = offsetX;
            OffsetY = offsetY;
            Scrolls = scrolls;
        }
    }

    public static class MenuGridLayout
    {
        // areaWidth/Height:可用區域;aspect = 卡高 / 卡寬;minCell/maxCell:卡寬上下限
        public static GridLayoutResult Compute(int count, float areaWidth, float areaHeight, float spacing, float aspect,
            float minCell, float maxCell)
        {
            if (count <= 0)
            {
                return new GridLayoutResult(0, 1, 0, maxCell, maxCell * aspect, spacing, 0f, 0f, 0f, false);
            }
            // 由少欄往多欄試,取「塞得進高度」且卡片最大的欄數;卡寬相同(都頂到上限)時取欄數多的——
            // 橫向區域裡並排比疊成一直列好看
            int bestColumns = -1;
            float bestWidth = 0f;
            for (int columns = 1; columns <= count; columns++)
            {
                float width = Mathf.Min(maxCell, (areaWidth - (columns - 1) * spacing) / columns);
                if (width < minCell)
                {
                    break;
                }
                int rows = (count + columns - 1) / columns;
                float height = rows * width * aspect + (rows - 1) * spacing;
                if (height <= areaHeight + 0.01f && width >= bestWidth - 0.01f)
                {
                    bestColumns = columns;
                    bestWidth = width;
                }
            }
            bool scrolls = bestColumns < 0;
            if (scrolls)
            {
                // 塞不下:用下限卡寬能排的最多欄數,多出來的列往下捲
                bestColumns = Mathf.Max(1, Mathf.FloorToInt((areaWidth + spacing) / (minCell + spacing)));
                bestWidth = Mathf.Min(maxCell, (areaWidth - (bestColumns - 1) * spacing) / bestColumns);
            }
            int finalRows = (count + bestColumns - 1) / bestColumns;
            float cellHeight = bestWidth * aspect;
            float contentHeight = finalRows * cellHeight + (finalRows - 1) * spacing;
            int usedColumns = Mathf.Min(bestColumns, count);
            float blockWidth = usedColumns * bestWidth + (usedColumns - 1) * spacing;
            float offsetX = Mathf.Max(0f, (areaWidth - blockWidth) * 0.5f);
            float offsetY = scrolls ? 0f : Mathf.Max(0f, (areaHeight - contentHeight) * 0.5f);
            return new GridLayoutResult(count, bestColumns, finalRows, bestWidth, cellHeight, spacing, contentHeight,
                offsetX, offsetY, scrolls);
        }

        // 第 index 張卡片的左上角(相對區域左上角;y 往下為正)
        public static Vector2 CellTopLeft(in GridLayoutResult layout, int index)
        {
            int column = index % layout.Columns;
            int row = index / layout.Columns;
            float x = layout.OffsetX + column * (layout.CellWidth + layout.Spacing);
            float y = layout.OffsetY + row * (layout.CellHeight + layout.Spacing);
            return new Vector2(x, y);
        }

        // 格子導覽:dx/dy 各 -1/0/+1(y +1 = 往下)。走不過去回傳 -1(交給外層決定,例如切到其他元件)。
        // 往下走到不完整的最後一列時落在最後一張(S3 武器清單的行為)。
        public static int Neighbor(int index, int count, int columns, int dx, int dy)
        {
            if (count <= 0 || columns <= 0 || index < 0 || index >= count)
            {
                return -1;
            }
            int column = index % columns;
            int row = index / columns;
            int lastRow = (count - 1) / columns;
            if (dx != 0)
            {
                int target = index + dx;
                int targetColumn = column + dx;
                if (targetColumn < 0 || targetColumn >= columns || target < 0 || target >= count)
                {
                    return -1;
                }
                return target;
            }
            if (dy != 0)
            {
                int targetRow = row + dy;
                if (targetRow < 0 || targetRow > lastRow)
                {
                    return -1;
                }
                return Mathf.Min(targetRow * columns + column, count - 1);
            }
            return index;
        }

        // 捲動:讓 [top, top+height] 這段完整出現在可視範圍內,回傳新的捲動量(0 = 最上)
        public static float ScrollToReveal(float currentScroll, float viewportHeight, float contentHeight, float top,
            float height, float margin)
        {
            float maxScroll = Mathf.Max(0f, contentHeight - viewportHeight);
            float scroll = currentScroll;
            if (top - margin < scroll)
            {
                scroll = top - margin;
            }
            else if (top + height + margin > scroll + viewportHeight)
            {
                scroll = top + height + margin - viewportHeight;
            }
            return Mathf.Clamp(scroll, 0f, maxScroll);
        }
    }
}

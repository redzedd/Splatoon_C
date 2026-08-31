namespace SplatoonC.Core
{
    // 佔地率計算——純邏輯、無 MonoBehaviour,輸入來自計分系統的 AsyncGPUReadback 像素統計。
    public static class CoverageCalculator
    {
        public readonly struct Result
        {
            // 各值為 0~1 比例;PaintedRatio = 全部已塗面積佔可塗面積的比例。
            public readonly float TeamRatio;
            public readonly float EnemyRatio;
            public readonly float PaintedRatio;

            public Result(float teamRatio, float enemyRatio, float paintedRatio)
            {
                TeamRatio = teamRatio;
                EnemyRatio = enemyRatio;
                PaintedRatio = paintedRatio;
            }
        }

        public static Result Compute(int teamPixels, int enemyPixels, int totalPixels)
        {
            if (totalPixels <= 0)
            {
                return new Result(0f, 0f, 0f);
            }

            // 統計值不可信任來源(readback 縮圖取樣),防呆夾住而不是丟例外。
            int team = teamPixels < 0 ? 0 : teamPixels;
            int enemy = enemyPixels < 0 ? 0 : enemyPixels;
            long painted = (long)team + enemy;
            if (painted > totalPixels)
            {
                painted = totalPixels;
            }

            float total = totalPixels;
            return new Result(team / total, enemy / total, painted / total);
        }
    }
}

namespace SplatoonC.Core.Match
{
    // 塗地點數(p)與面積的換算。S3 未公開精確比例;SplatNet 2 與擊倒爆墨(r5 ≈ 22p)推得約 3.3 m²/p。
    // 場地 2100~3000p、大招 180~220p 都以這個比例在本專案的公尺尺度下成立。
    public static class TurfPoints
    {
        public const float SquareMetresPerPoint = 3.3f;

        public static float FromArea(float squareMetres)
        {
            return squareMetres / SquareMetresPerPoint;
        }

        public static int Rounded(float squareMetres)
        {
            return (int)(FromArea(squareMetres) + 0.5f);
        }
    }
}

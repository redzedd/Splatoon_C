using UnityEngine;

namespace SplatoonC.Core.Menu
{
    // 視角靈敏度(S3 選項:-5 ~ +5,每格 0.5)→ 實際倍率。
    // S3 沒有公開的換算表;研究建議「±5 以指數對應」:每 +2.5 級倍率 ×2,±5 = ×4 / ×0.25 [EST]。
    // 指數而非線性:每一格的「手感差」一致(從 ×1 到 ×1.32 與 ×3 到 ×4 感覺一樣大)。
    public static class LookSensitivity
    {
        public const float Min = -5f;
        public const float Max = 5f;
        public const float Step = 0.5f;

        // 倍率翻倍所需的級數 [EST]
        public const float DoublingLevels = 2.5f;

        // 級數總數(-5, -4.5, …, +5 = 21 格)
        public static int StepCount => Mathf.RoundToInt((Max - Min) / Step) + 1;

        // 對齊到 0.5 格並限制在 ±5
        public static float Clamp(float level)
        {
            float snapped = Mathf.Round(level / Step) * Step;
            return Mathf.Clamp(snapped, Min, Max);
        }

        public static float Multiplier(float level)
        {
            return Mathf.Pow(2f, Clamp(level) / DoublingLevels);
        }

        // 格位(0 = -5)↔ 級數
        public static float LevelOfIndex(int index)
        {
            return Clamp(Min + index * Step);
        }

        public static int IndexOfLevel(float level)
        {
            return Mathf.RoundToInt((Clamp(level) - Min) / Step);
        }

        // 顯示:「+2.5」「0」「-1.5」(S3 選項頁的寫法)
        public static string Format(float level)
        {
            float v = Clamp(level);
            if (Mathf.Abs(v) < 0.001f)
            {
                return "0";
            }
            return v > 0f ? "+" + v.ToString("0.0") : v.ToString("0.0");
        }

        // 把裝置視角增量(度)套上靈敏度與反轉;y 為正 = 上仰(與 CameraOrbitSolver 一致)
        public static Vector2 Apply(Vector2 deltaDeg, float levelX, float levelY, bool invertX, bool invertY)
        {
            float x = deltaDeg.x * Multiplier(levelX) * (invertX ? -1f : 1f);
            float y = deltaDeg.y * Multiplier(levelY) * (invertY ? -1f : 1f);
            return new Vector2(x, y);
        }
    }
}

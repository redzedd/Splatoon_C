using UnityEngine;

namespace SplatoonC.Core.Combat
{
    // 龍捲風光柱的生命週期純邏輯:由細變粗(smoothstep)→ 停留 → 淡出 → 結束。
    public static class TornadoGrowth
    {
        public enum Phase
        {
            Growing,
            Holding,
            Fading,
            Done
        }

        public static float Radius(float elapsed, float startRadius, float maxRadius, float growDuration)
        {
            if (growDuration <= 0f || elapsed >= growDuration)
            {
                return maxRadius;
            }
            float t = elapsed <= 0f ? 0f : elapsed / growDuration;
            t = t * t * (3f - 2f * t);
            return startRadius + (maxRadius - startRadius) * t;
        }

        public static Phase GetPhase(float elapsed, float growDuration, float holdDuration, float fadeDuration)
        {
            if (elapsed < growDuration)
            {
                return Phase.Growing;
            }
            if (elapsed < growDuration + holdDuration)
            {
                return Phase.Holding;
            }
            if (elapsed < growDuration + holdDuration + fadeDuration)
            {
                return Phase.Fading;
            }
            return Phase.Done;
        }

        // 淡出前 1,淡出期間線性到 0。
        public static float Alpha(float elapsed, float growDuration, float holdDuration, float fadeDuration)
        {
            float fadeStart = growDuration + holdDuration;
            if (elapsed <= fadeStart)
            {
                return 1f;
            }
            if (fadeDuration <= 0f)
            {
                return 0f;
            }
            return Mathf.Clamp01(1f - (elapsed - fadeStart) / fadeDuration);
        }
    }
}

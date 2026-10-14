using System.Collections.Generic;
using UnityEngine;

namespace SplatoonC.Gameplay.Scoring
{
    // OS 動態字型的共用快取:內建 LegacyRuntime 字型無繁中字元,HUD 一律用 OS 字型(見 CoverageHud)。
    // 同名同字級只建一次,避免每個 HUD 元件各自 CreateDynamicFontFromOSFont。
    public static class HudFont
    {
        private static readonly Dictionary<(string, int), Font> Cache = new Dictionary<(string, int), Font>();

        public static Font Get(string fontName, int size)
        {
            var key = (fontName, size);
            if (Cache.TryGetValue(key, out Font cached) && cached != null)
            {
                return cached;
            }
            Font font = Font.CreateDynamicFontFromOSFont(fontName, size);
            Cache[key] = font;
            return font;
        }
    }
}

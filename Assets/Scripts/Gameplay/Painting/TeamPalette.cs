using System;
using SplatoonC.Core;
using UnityEngine;

namespace SplatoonC.Gameplay.Painting
{
    // 本場比賽的兩隊墨色。ink map 只存「兩隊各佔多少」(R = Alpha、G = Bravo),實際顏色由 shader
    // 讀全域 _InkColorAlpha/_InkColorBravo 合成——換色不必重畫任何 ink map。
    // 以 Vector 傳 linear 值:SetGlobalColor 的色彩空間轉換在不同 API 間不一致,Vector 永遠原樣。
    public static class TeamPalette
    {
        private static readonly int AlphaColorId = Shader.PropertyToID("_InkColorAlpha");
        private static readonly int BravoColorId = Shader.PropertyToID("_InkColorBravo");

        public static readonly Color DefaultAlpha = new Color(1f, 0.5f, 0f, 1f);
        public static readonly Color DefaultBravo = new Color(0.16f, 0.28f, 1f, 1f);

        public static Color Alpha { get; private set; } = DefaultAlpha;
        public static Color Bravo { get; private set; } = DefaultBravo;

        // 顏色變更時通知(角色觸手、HUD、特效等快取了顏色的元件重新上色)
        public static event Action ColorsChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetOnLoad()
        {
            Alpha = DefaultAlpha;
            Bravo = DefaultBravo;
            ColorsChanged = null;
            ApplyGlobals();
        }

        public static void Set(Color alpha, Color bravo)
        {
            Alpha = alpha;
            Bravo = bravo;
            ApplyGlobals();
            ColorsChanged?.Invoke();
        }

        public static Color ColorOf(Team team)
        {
            switch (team)
            {
                case Team.Alpha: return Alpha;
                case Team.Bravo: return Bravo;
                default: return Color.white;
            }
        }

        // splat pass 的通道遮罩:這一發要往哪個通道累積
        public static Vector4 ChannelOf(Team team)
        {
            return team == Team.Bravo ? new Vector4(0f, 1f, 0f, 0f) : new Vector4(1f, 0f, 0f, 0f);
        }

        public static void ApplyGlobals()
        {
            Shader.SetGlobalVector(AlphaColorId, Alpha.linear);
            Shader.SetGlobalVector(BravoColorId, Bravo.linear);
        }
    }
}

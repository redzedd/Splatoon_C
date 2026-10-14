using UnityEngine;

namespace SplatoonC.Core.Match
{
    // Splatoon 3 對戰配色(datamine,Inkipedia Template:InkColor):Alpha / Bravo / 中立色。
    // 中立色用在「GAME!」字樣、未佔領的目標、彈跳墊等不屬於任何一隊的東西。
    public readonly struct InkColorSet
    {
        public readonly string Name;
        public readonly Color Alpha;
        public readonly Color Bravo;
        public readonly Color Neutral;

        public InkColorSet(string name, string alpha, string bravo, string neutral)
        {
            Name = name;
            Alpha = Parse(alpha);
            Bravo = Parse(bravo);
            Neutral = Parse(neutral);
        }

        private static Color Parse(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out Color color) ? color : Color.magenta;
        }
    }

    public static class InkColorSets
    {
        public static readonly InkColorSet[] All =
        {
            new InkColorSet("YellowBlue", "#d0be08", "#3a0ccd", "#b62ea7"),
            new InkColorSet("BlueYellow", "#1a1aae", "#e38d24", "#cd43a6"),
            new InkColorSet("GreenPurple", "#a0c937", "#ba30b0", "#e1820d"),
            new InkColorSet("LimegreenPurple", "#becd41", "#6325cd", "#31c4a9"),
            new InkColorSet("OrangeBlue", "#de6624", "#343bc4", "#cdcd34"),
            new InkColorSet("OrangePurple", "#cd510a", "#6e04b6", "#94c921"),
            new InkColorSet("PinkGreen", "#c12d74", "#2cb721", "#3a28c4"),
            new InkColorSet("TurquoisePink", "#1bbeab", "#c43a6e", "#4e4edd"),
            new InkColorSet("TurquoiseRed", "#1ec0ad", "#d74b31", "#0d0ddc"),
            new InkColorSet("YellowPurple", "#ceb121", "#9025c6", "#5dab21"),
        };

        // 試射場用的固定配色(S1 風格橘 vs 藍)
        public static InkColorSet Default => All[4];

        // S3「色彩鎖定」(Color Lock,色覺友善):每場固定黃 vs 藍紫(S3 主視覺配色 YellowBlue),Alpha = 黃
        public static InkColorSet ColorLock => All[0];

        public static InkColorSet Pick(int index)
        {
            int count = All.Length;
            return All[((index % count) + count) % count];
        }
    }
}

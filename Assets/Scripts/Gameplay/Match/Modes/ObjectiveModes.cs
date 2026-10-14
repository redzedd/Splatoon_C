using SplatoonC.Core.Match.Ranked;

namespace SplatoonC.Gameplay.Match
{
    // 模式註冊表:哪些模式已實作(選單據此開放)、顯示名稱、開場副標、規則說明。
    // 新模式完成時在 IsImplemented 補上,並在 ObjectiveModeController.CreateDriver 接上驅動。
    public static class ObjectiveModes
    {
        public static bool IsImplemented(MatchMode mode)
        {
            switch (mode)
            {
                case MatchMode.TurfWar:
                case MatchMode.SplatZones:
                    return true;
                default:
                    return false;
            }
        }

        // S3 繁中版名稱
        public static string DisplayName(MatchMode mode)
        {
            switch (mode)
            {
                case MatchMode.SplatZones: return "真格區域";
                case MatchMode.TowerControl: return "真格塔樓";
                case MatchMode.Rainmaker: return "真格魚虎";
                case MatchMode.ClamBlitz: return "真格蛤蜊";
                default: return "塗地對戰";
            }
        }

        public static string EnglishName(MatchMode mode)
        {
            switch (mode)
            {
                case MatchMode.SplatZones: return "SPLAT ZONES";
                case MatchMode.TowerControl: return "TOWER CONTROL";
                case MatchMode.Rainmaker: return "RAINMAKER";
                case MatchMode.ClamBlitz: return "CLAM BLITZ";
                default: return "TURF WAR";
            }
        }

        // 開場標題卡的副標(S3:模式名 + 一句目標)
        public static string Objective(MatchMode mode)
        {
            switch (mode)
            {
                case MatchMode.SplatZones: return "佔領區域,讓計數先歸零!";
                case MatchMode.TowerControl: return "搭上塔樓,把它推到終點!";
                case MatchMode.Rainmaker: return "把魚虎扛到對方的台座!";
                case MatchMode.ClamBlitz: return "把蛤蜊丟進對方的球籃!";
                default: return "塗得比對手多就能獲勝!";
            }
        }

        // 選單的規則說明(S3 規則說明摘要 + 本作數值)
        public static string Rules(MatchMode mode)
        {
            switch (mode)
            {
                case MatchMode.SplatZones:
                    return "塗滿區域7成即可佔領;佔領期間每0.6秒計數減1,先歸零的隊伍KO獲勝。5分鐘到時計數少的一方勝。";
                case MatchMode.TowerControl:
                    return "搭上塔樓,把它護送到對方基地的終點。";
                case MatchMode.Rainmaker:
                    return "搶下魚虎(Rainmaker),扛到對方基地的台座。";
                case MatchMode.ClamBlitz:
                    return "收集蛤蜊做成大蛤蜊,丟進對方的球籃。";
                default:
                    return "3分鐘內,地面塗得比對手多的隊伍獲勝。牆壁不算分;被擊倒7.5秒後在基地重生。";
            }
        }
    }
}

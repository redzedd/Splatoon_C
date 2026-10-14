namespace SplatoonC.Core.Match.Ranked
{
    // 對戰模式:一般對戰(塗地)與蠻頹對戰(Anarchy)的四種真格模式。
    // 選單用字串 id(MatchSettings.ModeId);這裡是 id ↔ 列舉的唯一對照。
    public enum MatchMode
    {
        TurfWar = 0,
        SplatZones = 1,
        TowerControl = 2,
        Rainmaker = 3,
        ClamBlitz = 4,
    }

    public static class MatchModes
    {
        public const string TurfWarId = "turf_war";
        public const string SplatZonesId = "splat_zones";
        public const string TowerControlId = "tower_control";
        public const string RainmakerId = "rainmaker";
        public const string ClamBlitzId = "clam_blitz";

        public static string IdOf(MatchMode mode)
        {
            switch (mode)
            {
                case MatchMode.SplatZones: return SplatZonesId;
                case MatchMode.TowerControl: return TowerControlId;
                case MatchMode.Rainmaker: return RainmakerId;
                case MatchMode.ClamBlitz: return ClamBlitzId;
                default: return TurfWarId;
            }
        }

        // 不認得的 id 一律當塗地(舊存檔/打錯字不會讓對戰開不起來)
        public static MatchMode FromId(string id)
        {
            switch (id)
            {
                case SplatZonesId: return MatchMode.SplatZones;
                case TowerControlId: return MatchMode.TowerControl;
                case RainmakerId: return MatchMode.Rainmaker;
                case ClamBlitzId: return MatchMode.ClamBlitz;
                default: return MatchMode.TurfWar;
            }
        }

        // 蠻頹對戰(計數制、5:00 + 加時)
        public static bool IsRanked(MatchMode mode)
        {
            return mode != MatchMode.TurfWar;
        }
    }
}

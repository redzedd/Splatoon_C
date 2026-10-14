namespace SplatoonC.Core
{
    // 隊伍 id:墨水歸屬網格、傷害判定、計分共用同一組值。0 = 無墨/中立。
    // 值與 ink map 通道綁定(Alpha → R、Bravo → G),改值前先看 PaintableSurface 的通道約定。
    public enum Team : byte
    {
        None = 0,
        Alpha = 1,
        Bravo = 2,
    }

    public static class TeamUtil
    {
        public static Team Opponent(Team team)
        {
            switch (team)
            {
                case Team.Alpha: return Team.Bravo;
                case Team.Bravo: return Team.Alpha;
                default: return Team.None;
            }
        }

        // 腳下/牆上的墨對某隊而言是不是敵墨(中立地不算)。
        public static bool IsEnemyInk(byte owner, Team self)
        {
            return owner != 0 && owner != (byte)self;
        }
    }
}

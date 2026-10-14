namespace SplatoonC.Core.Match.Ranked
{
    // 蠻頹對戰的最終結果(判定畫面、結算用)
    public readonly struct AnarchyResult
    {
        public readonly Team Winner;
        // 顯示用計數(無進度時 Alpha 顯示 99:S3「Alpha 1-0 勝」)
        public readonly int AlphaCount;
        public readonly int BravoCount;
        public readonly bool Knockout;
        public readonly bool NoProgress;
        public readonly bool WentToOvertime;

        public AnarchyResult(Team winner, int alphaCount, int bravoCount, bool knockout, bool noProgress, bool overtime)
        {
            Winner = winner;
            AlphaCount = alphaCount;
            BravoCount = bravoCount;
            Knockout = knockout;
            NoProgress = noProgress;
            WentToOvertime = overtime;
        }

        public int CountOf(Team team)
        {
            return team == Team.Bravo ? BravoCount : AlphaCount;
        }

        // 顯示用「進度」(100 - 計數;KO = 100)
        public int ProgressOf(Team team)
        {
            return TeamScore.StartCount - CountOf(team);
        }
    }

    // 蠻頹對戰共通規則(DESIGN_REFERENCE §6.4;research/05 §3):
    //   5:00 + 加時;計數 100 起、歸 0 = KNOCKOUT 立即結束;時間到計數低者勝;
    //   平手迴避:後達到同一計數的一方算差 1;兩隊都沒進度 → Alpha 99 : Bravo 100(不加時)。
    public static class AnarchyRules
    {
        // S3:蠻頹對戰 5:00(= 18000F)
        public const float RegulationSeconds = 300f;
        public const int RegulationFrames = 18000;

        // 比較兩隊:< 0 = a 領先、> 0 = b 領先、0 = 完全相同(同計數且同一幀達到——只有開場 100:100 會發生)。
        // 罰分不參與比較(jpwiki:時間切れ時の勝敗判定にはペナルティカウントは関わらず)。
        public static int Compare(TeamScore a, TeamScore b)
        {
            if (a.Count != b.Count)
            {
                return a.Count < b.Count ? -1 : 1;
            }
            if (a.ReachedFrame != b.ReachedFrame)
            {
                // 先達到的一方領先
                return a.ReachedFrame < b.ReachedFrame ? -1 : 1;
            }
            return 0;
        }

        // 目前領先的隊伍(完全相同 = None)
        public static Team Leader(TeamScore alpha, TeamScore bravo)
        {
            int c = Compare(alpha, bravo);
            return c < 0 ? Team.Alpha : c > 0 ? Team.Bravo : Team.None;
        }

        // 某隊是否落後(完全相同時兩隊都不算落後)
        public static bool IsBehind(Team team, TeamScore alpha, TeamScore bravo)
        {
            Team leader = Leader(alpha, bravo);
            return leader != Team.None && team != Team.None && leader != team;
        }

        public static bool AnyProgress(TeamScore alpha, TeamScore bravo)
        {
            return alpha.HasProgress || bravo.HasProgress;
        }

        // 最終結果:KO > 無進度(Alpha 1-0)> 計數比較(平手時後到的一方顯示 +1,畫面永遠不會出現同分)
        public static AnarchyResult Resolve(TeamScore alpha, TeamScore bravo, Team knockoutTeam, bool overtime)
        {
            if (knockoutTeam != Team.None)
            {
                return new AnarchyResult(knockoutTeam, alpha.Count, bravo.Count, true, false, overtime);
            }
            if (!AnyProgress(alpha, bravo))
            {
                return new AnarchyResult(Team.Alpha, TeamScore.StartCount - 1, TeamScore.StartCount, false, true, overtime);
            }
            Team leader = Leader(alpha, bravo);
            if (leader == Team.None)
            {
                leader = Team.Alpha;
            }
            int alphaCount = alpha.Count;
            int bravoCount = bravo.Count;
            if (alphaCount == bravoCount)
            {
                if (leader == Team.Alpha)
                {
                    bravoCount++;
                }
                else
                {
                    alphaCount++;
                }
            }
            return new AnarchyResult(leader, alphaCount, bravoCount, false, false, overtime);
        }
    }
}

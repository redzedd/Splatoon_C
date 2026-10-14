namespace SplatoonC.Core.Match.Ranked
{
    // 蠻頹對戰一隊的計數(DESIGN_REFERENCE §6.9):從 100 往下數,歸 0 = KNOCKOUT。
    // Penalty(罰分)只有區域模式使用:顯示在計數下方,重新確保時先扣罰分,且不參與勝負比較。
    // ReachedFrame:目前這個計數是第幾幀達到的——平手時「先到的一方領先」(後追平者算差 1,S3 平手迴避)。
    public sealed class TeamScore
    {
        public const int StartCount = 100;

        public TeamScore(Team team)
        {
            Team = team;
        }

        public Team Team { get; }

        public int Count { get; private set; } = StartCount;

        public int Penalty { get; private set; }

        public int ReachedFrame { get; private set; }

        // 計數有沒有動過(S3:時間到時兩隊都沒進度 → Alpha 1-0 勝、不加時)
        public bool HasProgress => Count < StartCount;

        // 罰分公式的 start / end 用「含罰分的計數」
        public int CountWithPenalty => Count + Penalty;

        // 計數減 1(歸 0 為止);回傳是否剛好歸 0
        public bool Decrement(int frame)
        {
            if (Count <= 0)
            {
                return false;
            }
            Count--;
            ReachedFrame = frame;
            return Count == 0;
        }

        public void AddPenalty(int amount)
        {
            if (amount > 0)
            {
                Penalty += amount;
            }
        }

        // 罰分減 1;回傳是否剛好扣完(S3:最後 1 點罰分歸 0 的同一刻本計數也減 1)
        public bool BurnPenalty()
        {
            if (Penalty <= 0)
            {
                return false;
            }
            Penalty--;
            return Penalty == 0;
        }

        // 平手迴避:後追平的一方失去控制時計數 +1(Inkipedia Overtime 註:加在計數,不加在罰分)
        public void BumpForTie()
        {
            Count++;
        }

        // 測試/除錯:直接設定
        public void Set(int count, int penalty, int reachedFrame)
        {
            Count = count < 0 ? 0 : count;
            Penalty = penalty < 0 ? 0 : penalty;
            ReachedFrame = reachedFrame;
        }
    }
}

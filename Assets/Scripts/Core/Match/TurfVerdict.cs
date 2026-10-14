using UnityEngine;

namespace SplatoonC.Core.Match
{
    // 塗地對戰判定:比較兩隊地面投影面積,顯示到小數一位。兩隊百分比相加不必是 100(未塗地不屬於任何人)。
    // 平手規則(S1):面積完全相等時 Alpha 勝,並在 Alpha 的顯示值 +0.1%——判定畫面永遠不會出現平手。
    // 若面積有差但四捨五入後顯示相同,勝方顯示值 +0.1%,避免「贏了但數字一樣」。
    public readonly struct TurfVerdict
    {
        public readonly float AlphaPercent;
        public readonly float BravoPercent;
        public readonly Team Winner;

        public TurfVerdict(float alphaPercent, float bravoPercent, Team winner)
        {
            AlphaPercent = alphaPercent;
            BravoPercent = bravoPercent;
            Winner = winner;
        }

        public static TurfVerdict Judge(float alphaArea, float bravoArea, float totalArea)
        {
            float alpha = totalArea > 0f ? Round1(Mathf.Clamp01(alphaArea / totalArea) * 100f) : 0f;
            float bravo = totalArea > 0f ? Round1(Mathf.Clamp01(bravoArea / totalArea) * 100f) : 0f;
            Team winner = bravoArea > alphaArea ? Team.Bravo : Team.Alpha;
            if (Mathf.Approximately(alpha, bravo))
            {
                if (winner == Team.Alpha)
                {
                    alpha = Round1(alpha + 0.1f);
                }
                else
                {
                    bravo = Round1(bravo + 0.1f);
                }
            }
            return new TurfVerdict(alpha, bravo, winner);
        }

        public float PercentOf(Team team)
        {
            return team == Team.Bravo ? BravoPercent : AlphaPercent;
        }

        private static float Round1(float value)
        {
            return Mathf.Round(value * 10f) / 10f;
        }
    }
}

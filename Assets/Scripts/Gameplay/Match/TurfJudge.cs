using SplatoonC.Core;
using SplatoonC.Core.Match;
using SplatoonC.Gameplay.Painting;
using UnityEngine;

namespace SplatoonC.Gameplay.Match
{
    // 塗地判定:加總所有可塗表面的地面歸屬(CPU 網格,不做 GPU readback),交給 TurfVerdict 決定勝負。
    public static class TurfJudge
    {
        public struct Tally
        {
            public float Alpha;
            public float Bravo;
            public float Total;

            public float Of(Team team)
            {
                return team == Team.Bravo ? Bravo : team == Team.Alpha ? Alpha : 0f;
            }
        }

        public static Tally Count()
        {
            var tally = new Tally();
            var surfaces = PaintableSurface.Active;
            for (int i = 0; i < surfaces.Count; i++)
            {
                surfaces[i].AccumulateTurf(ref tally.Alpha, ref tally.Bravo, ref tally.Total);
            }
            return tally;
        }

        public static TurfVerdict Judge()
        {
            Tally tally = Count();
            return TurfVerdict.Judge(tally.Alpha, tally.Bravo, tally.Total);
        }

        // 比賽開始:清掉全場墨(視覺 + 歸屬),並把被壓住的地面格排除出分母
        public static void ClearAll()
        {
            var surfaces = PaintableSurface.Active;
            int blocked = 0;
            for (int i = 0; i < surfaces.Count; i++)
            {
                blocked += surfaces[i].ExcludeOccludedTurf();
                surfaces[i].ClearInk();
            }
            if (blocked > 0)
            {
                Debug.Log($"[MATCH] 排除被壓住的地面格 {blocked} 格");
            }
        }
    }
}

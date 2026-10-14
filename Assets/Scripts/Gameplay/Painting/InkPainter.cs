using SplatoonC.Core;
using UnityEngine;

namespace SplatoonC.Gameplay.Painting
{
    // 塗墨者:塗到新地盤時被回報(大招充能、個人塗地分數)。敵我判定只看 Team。
    public interface IInkPainter
    {
        Team Team { get; }

        // areaSquareMetres = 這次新轉成自家墨的面積(覆蓋敵墨也算,重塗自家墨不算)。
        // chargesSpecial = false:大招本身塗的墨,只計分不充能(Splatoon 規則)。
        void OnTurfInked(float areaSquareMetres, bool chargesSpecial);
    }

    // 一次塗墨的來源:隊伍 + (可選)回報對象。環境/測試塗墨可以沒有 Painter。
    public readonly struct InkSource
    {
        public readonly Team Team;
        public readonly IInkPainter Painter;
        public readonly bool ChargesSpecial;

        public InkSource(Team team, IInkPainter painter = null, bool chargesSpecial = true)
        {
            Team = team;
            Painter = painter;
            ChargesSpecial = chargesSpecial;
        }

        public static InkSource From(IInkPainter painter, bool chargesSpecial = true)
        {
            return new InkSource(painter != null ? painter.Team : Team.Alpha, painter, chargesSpecial);
        }
    }

    // 球形塗墨:塗「所有」與球相交的可塗表面——轉角、牆腳、台階邊緣自然連續,
    // 不再只塗射線命中的那一面。每面只塗朝向球心的那一側(薄牆背面不會透墨)。
    public static class InkPainter
    {
        // 回傳新增歸屬面積(m²),並回報給 source.Painter。
        public static float PaintSphere(Vector3 center, float radius, float hardness, in InkSource source)
        {
            if (radius <= 0f)
            {
                return 0f;
            }
            float area = 0f;
            float radiusSq = radius * radius;
            var surfaces = PaintableSurface.Active;
            for (int i = 0; i < surfaces.Count; i++)
            {
                PaintableSurface surface = surfaces[i];
                if (surface.WorldBounds.SqrDistance(center) > radiusSq)
                {
                    continue;
                }
                area += surface.Paint(center, radius, source.Team, hardness);
            }
            if (area > 0f && source.Painter != null)
            {
                source.Painter.OnTurfInked(area, source.ChargesSpecial);
            }
            return area;
        }

        // 查詢射線命中點的墨歸屬(0 = 無墨)。命中物沒有可塗表面也回 0。
        public static byte SampleAt(RaycastHit hit)
        {
            var surface = hit.collider != null ? hit.collider.GetComponent<PaintableSurface>() : null;
            return surface != null ? surface.SampleOwnership(hit.point, hit.normal) : (byte)0;
        }
    }
}

using UnityEngine;

namespace SplatoonC.Core.Combat
{
    // 墨彈地面落點的半寬(S3 PaintParam):
    //   離射手水平距離 ≤ DistanceNear(1.1 u)= WidthHalfNear,到 DistanceFar(預設 20 u;滾筒橫甩 Splat 12 / Carbon 8.5)
    //   線性到 WidthHalfFar,之後維持;
    //   另有隨飛行幀數的縮小(滾筒 ChangeFrameWidthRate:ChangeWidthStartFrame → EndFrame 線性縮到 × rate;rate ≤ 0 = 不縮)。
    // InkBulletSpec.FloorHalfWidthAt 與 EditMode 測試共用。
    public static class InkStampWidth
    {
        public const float DistanceNear = 1.1f;
        public const float DefaultDistanceFar = 20f;

        public static float HalfWidth(float widthNear, float widthFar, float distanceFar, float flatDistance,
            int frame, float shrinkRate, int shrinkStartFrame, int shrinkEndFrame)
        {
            float far = distanceFar > 0f ? distanceFar : DefaultDistanceFar;
            float halfWidth = Mathf.Lerp(widthNear, widthFar,
                Mathf.InverseLerp(DistanceNear, Mathf.Max(DistanceNear + 0.1f, far), flatDistance));
            if (shrinkRate > 0f && shrinkEndFrame > shrinkStartFrame)
            {
                float t = Mathf.InverseLerp(shrinkStartFrame, shrinkEndFrame, frame);
                halfWidth *= Mathf.Lerp(1f, shrinkRate, t);
            }
            return halfWidth;
        }
    }
}

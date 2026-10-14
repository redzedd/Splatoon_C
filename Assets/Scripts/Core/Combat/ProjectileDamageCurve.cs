namespace SplatoonC.Core.Combat
{
    // 墨彈傷害曲線:直飛段全額;超過射程進入急墜段後,在 decayDistance 公尺內線性衰到 minDamage。
    // 以「沿彈道飛行距離」判定,與 InkProjectile 判段用的是同一個量。
    public static class ProjectileDamageCurve
    {
        public static float Evaluate(float travelled, float straightRange, float fullDamage,
            float minDamage, float decayDistance)
        {
            if (travelled <= straightRange)
            {
                return fullDamage;
            }
            if (decayDistance <= 0f)
            {
                return minDamage;
            }
            float t = (travelled - straightRange) / decayDistance;
            if (t >= 1f)
            {
                return minDamage;
            }
            return fullDamage + (minDamage - fullDamage) * t;
        }

        // S3 以「飛行幀數」衰減(§3.1、research 07 §2.3):frame ≤ reduceStart 全額,到 reduceEnd 線性降到 min,
        // 之後維持 min;最後向下取整到 0.1。例:Splattershot 36/18、8→40F,第 12F = 33.75 → 33.7(三發擊倒的最後一幀)。
        // reduceStart < 0 = 不衰減(爆破槍直擊)。frame 從 1 起算(第一次 60 Hz 步進就命中 = 第 1F)。
        public static float EvaluateByFrame(int frame, float maxDamage, float minDamage, int reduceStartFrame,
            int reduceEndFrame)
        {
            float damage;
            if (reduceStartFrame < 0 || frame <= reduceStartFrame)
            {
                damage = maxDamage;
            }
            else if (frame >= reduceEndFrame || reduceEndFrame <= reduceStartFrame)
            {
                damage = minDamage;
            }
            else
            {
                float t = (frame - reduceStartFrame) / (float)(reduceEndFrame - reduceStartFrame);
                damage = maxDamage + (minDamage - maxDamage) * t;
            }
            return VitalsModel.Floor01(damage);
        }
    }
}

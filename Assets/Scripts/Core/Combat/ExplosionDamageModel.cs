namespace SplatoonC.Core.Combat
{
    // 爆炸傷害:近距離範圍內全額(180),超過近距離但在遠距離範圍內為低額(30),再遠沒有。
    public static class ExplosionDamageModel
    {
        public static float Evaluate(float distance, float nearRadius, float farRadius,
            float nearDamage, float farDamage)
        {
            if (distance <= nearRadius)
            {
                return nearDamage;
            }
            if (distance <= farRadius)
            {
                return farDamage;
            }
            return 0f;
        }

        // 線性爆炸(爆破槍:內圈 ≤ nearRadius 全額 70,到 farRadius 線性降到 50,再遠無傷;§3.3)。
        // S3 的炸彈是階梯(DamageLinear:false),爆破槍是線性。
        public static float EvaluateLinear(float distance, float nearRadius, float farRadius,
            float nearDamage, float farDamage)
        {
            if (distance <= nearRadius)
            {
                return nearDamage;
            }
            if (distance > farRadius)
            {
                return 0f;
            }
            float span = farRadius - nearRadius;
            if (span <= 1e-6f)
            {
                return farDamage;
            }
            float t = (distance - nearRadius) / span;
            return VitalsModel.Floor01(nearDamage + (farDamage - nearDamage) * t);
        }
    }
}

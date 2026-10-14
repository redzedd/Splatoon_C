namespace SplatoonC.Core.Combat
{
    // 血量純邏輯:ApplyDamage 只在「活 → 死」那一次回 true,已死再打不重複計殺。
    public struct Health
    {
        public float Max { get; private set; }
        public float Current { get; private set; }

        public bool IsDead => Current <= 0f;

        public static Health Create(float max)
        {
            var health = new Health();
            health.Max = max > 0f ? max : 0f;
            health.Current = health.Max;
            return health;
        }

        public bool ApplyDamage(float amount)
        {
            if (IsDead || amount <= 0f)
            {
                return false;
            }
            Current -= amount;
            if (Current <= 0f)
            {
                Current = 0f;
                return true;
            }
            return false;
        }

        public void ResetFull()
        {
            Current = Max;
        }
    }
}

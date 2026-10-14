namespace SplatoonC.Core.Combat
{
    // 頭頂傷害數字的累計器:加總的是「全額」傷害(含溢傷),殺死時才會顯示 120 而不是 100。
    // 閒置判定交給呼叫端(HasExpired),重置時機由假人決定(閒置回血或重生)。
    public struct DamageAccumulator
    {
        public float Total { get; private set; }
        public float LastHitTime { get; private set; }
        public bool HasHits { get; private set; }

        public void Add(float fullAmount, float time)
        {
            Total += fullAmount;
            LastHitTime = time;
            HasHits = true;
        }

        public bool HasExpired(float now, float idleDelay)
        {
            return HasHits && now - LastHitTime >= idleDelay;
        }

        public void Reset()
        {
            Total = 0f;
            LastHitTime = 0f;
            HasHits = false;
        }
    }
}

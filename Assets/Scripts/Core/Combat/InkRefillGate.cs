namespace SplatoonC.Core.Combat
{
    // 回墨閘門:按住扳機期間不回墨,放開後還要等一段延遲才恢復。
    // 每幀呼叫一次(未按住也要呼叫),與 FireClock 同樣的契約。
    public struct InkRefillGate
    {
        private float _blockedUntil;

        public bool Evaluate(bool triggerHeld, float time, float delayAfterRelease)
        {
            if (triggerHeld)
            {
                _blockedUntil = time + delayAfterRelease;
                return false;
            }
            return time >= _blockedUntil;
        }
    }
}

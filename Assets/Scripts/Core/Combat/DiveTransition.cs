namespace SplatoonC.Core.Combat
{
    // 鑽進/鑽出墨水的過場進度:0 = 完全露出,1 = 完全潛入。
    // 進與出用不同時長(鑽出通常較快),外部再用 Ease 取平滑值驅動位移。
    public struct DiveTransition
    {
        private float _progress;

        public float Progress => _progress;

        public float Advance(bool submergeTarget, float deltaTime, float diveDuration, float surfaceDuration)
        {
            float duration = submergeTarget ? diveDuration : surfaceDuration;
            float target = submergeTarget ? 1f : 0f;
            if (duration <= 0f || deltaTime <= 0f)
            {
                if (duration <= 0f)
                {
                    _progress = target;
                }
                return _progress;
            }

            float step = deltaTime / duration;
            if (_progress < target)
            {
                _progress += step;
                if (_progress > target)
                {
                    _progress = target;
                }
            }
            else if (_progress > target)
            {
                _progress -= step;
                if (_progress < target)
                {
                    _progress = target;
                }
            }
            return _progress;
        }

        // smoothstep:頭尾慢、中間快,比線性像「被墨吸進去」
        public static float Ease(float t)
        {
            if (t <= 0f)
            {
                return 0f;
            }
            if (t >= 1f)
            {
                return 1f;
            }
            return t * t * (3f - 2f * t);
        }
    }
}

namespace SplatoonC.Core.Locomotion
{
    // 速度倍率的「上升即時、下降緩降」:剛離開墨水時速度不該瞬間歸零,
    // 而是在固定秒數內滑落到平時速度。加速(進墨)維持即時,否則入墨手感會鈍。
    public struct SpeedBoostDecay
    {
        private float _current;
        private bool _started;

        public float Current => _current;

        public float Update(float target, float deltaTime, float decayRate)
        {
            if (!_started)
            {
                _started = true;
                _current = target;
                return _current;
            }
            if (target >= _current || decayRate <= 0f || deltaTime <= 0f)
            {
                _current = target;
                return _current;
            }
            _current -= decayRate * deltaTime;
            if (_current < target)
            {
                _current = target;
            }
            return _current;
        }
    }
}

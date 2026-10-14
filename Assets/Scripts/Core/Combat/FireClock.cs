namespace SplatoonC.Core.Combat
{
    // 連射節奏純邏輯:按住觸發器時每 interval 出一發,與幀率無關。
    // 規則:首發立即;放開不累積欠帳;點放不能繞過冷卻;長掉幀單幀補射有上限、其餘欠帳丟棄。
    // 呼叫契約:每幀呼叫一次(未按住也要呼叫),否則放開期間會被誤判為按住中的掉幀欠帳。
    public struct FireClock
    {
        private float _nextShotTime;
        private bool _started;

        public static FireClock CreateReady()
        {
            return new FireClock();
        }

        public int ConsumeShots(bool triggerHeld, float time, float interval, int maxShotsPerFrame)
        {
            if (!_started)
            {
                _started = true;
                _nextShotTime = time;
            }

            if (!triggerHeld)
            {
                // 放開時把時鐘追到現在(不累積欠帳);冷卻未到則保留(點放不可超速)。
                if (_nextShotTime < time)
                {
                    _nextShotTime = time;
                }
                return 0;
            }

            int shots = 0;
            while (_nextShotTime <= time && shots < maxShotsPerFrame)
            {
                shots++;
                _nextShotTime += interval;
            }
            if (_nextShotTime <= time)
            {
                // 補射打頂仍有欠帳:丟棄,從現在起算冷卻。
                _nextShotTime = time + interval;
            }
            return shots;
        }
    }
}

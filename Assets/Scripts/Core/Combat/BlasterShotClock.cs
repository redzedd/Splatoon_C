using UnityEngine;

namespace SplatoonC.Core.Combat
{
    // 爆破槍一發的時間軸(秒;由幀數 ÷ 60 換算)
    public struct BlasterTiming
    {
        // 先行入力 [EST]:冷卻快結束前這麼多幀內按下(之後放開也算),到可以開始預備時照樣出這一發。
        // S3 資料沒有公開這個值;取 10F(1/6 秒),點按連打不會因為早按一點點就被吞掉,又不會把冷卻初期的誤按排進去
        public const int DefaultInputBufferFrames = 10;

        // 人形第一發延遲(S3 14F = 4 + PreDelayFrame_HumanShot 10)
        public float PreDelay;
        // 剛從烏賊態出來的第一發延遲(從解除烏賊起算;S3 24F = 9 + PreDelayFrame_SquidShot 15)
        public float SquidPreDelay;
        // 兩發之間(RepeatFrame;Blaster 50F、Luna 40F)
        public float Repeat;
        // 出膛後不能變烏賊 / 丟副武器的時間(PostDelayFrame_Blaster;Blaster 22F、Luna 26F)
        public float PostDelay;
        // 先行入力(秒;見 DefaultInputBufferFrames)
        public float InputBuffer;

        public static BlasterTiming FromFrames(int preDelay, int squidPreDelay, int repeat, int postDelay,
            int inputBuffer = DefaultInputBufferFrames)
        {
            return new BlasterTiming
            {
                PreDelay = preDelay / 60f,
                SquidPreDelay = squidPreDelay / 60f,
                Repeat = repeat / 60f,
                PostDelay = postDelay / 60f,
                InputBuffer = Mathf.Max(0, inputBuffer) / 60f,
            };
        }
    }

    // 爆破槍的扳機時間軸(純邏輯,與幀率無關;S3 research 02 §12.1):
    //   一發 = 預備(人形 14F;剛出烏賊態從解除起算 24F)→ 出膛。按下 = 這一發「確定」(點一下也會打出去,放開不會取消;
    //     預備中不能變烏賊 / 丟副武器)。出膛後 PostDelay(22F / 26F)內也不能。
    //   下一發的預備最早在「上一發 + RepeatFrame − 人形預備」開始(Blaster 36F、Luna 26F 後),所以按住的連射間隔剛好是
    //     RepeatFrame(50F / 40F),每一發前面都有 14F 的預備動作。那一刻還按著(或在先行入力時間內按過)才確定下一發;
    //     在這之前放開 = 不再出下一發(S3:射完放開 ZR 就能在 PostDelay 後潛入,不會被下一發綁住)。
    //   出膛時刻 = max(確定 + 人形預備, 解除烏賊 + 24F);第二發起確定時刻 ≥ 上一發 + RepeatFrame − 預備,節奏不會變快。
    // 呼叫契約:每幀呼叫一次 Tick(沒按也要呼叫,才抓得到按下的瞬間);死亡 / 大招佔用時 Cancel 放棄還沒出膛的那一發。
    public struct BlasterShotClock
    {
        private const float Epsilon = 1e-4f;

        private bool _initialized;
        private bool _pending;
        private bool _wasTrigger;
        private float _pendingTime;
        private float _commitTime;
        private float _nextReady;
        private float _lastShot;
        // 最近一次按下的時刻(先行入力;確定一發後清掉)與這次按住的起點
        private float _pressTime;
        private float _heldSince;

        // 有一發已確定、正在預備(還沒出膛)
        public bool Pending => _pending;
        // 預備中的那一發的出膛時刻 / 確定(開始預備)的時刻
        public float PendingShotTime => _pendingTime;
        public float CommitTime => _commitTime;
        // 最後一發的(排定)出膛時刻;還沒開過火 = −∞
        public float LastShotTime => _initialized ? _lastShot : float.NegativeInfinity;

        private void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }
            _initialized = true;
            _nextReady = float.NegativeInfinity;
            _lastShot = float.NegativeInfinity;
            _pressTime = float.NegativeInfinity;
        }

        // 最早可以開始下一發預備的時刻(上一發 + RepeatFrame − 人形預備;還沒開過火 = −∞)
        public float WindupAllowedAt(in BlasterTiming timing)
        {
            return _initialized ? _nextReady - timing.PreDelay : float.NegativeInfinity;
        }

        // 每幀一次:trigger = 此刻按著扳機且允許開火(人形、沒按副武器、大招不佔用)。
        // lastSquidTime = 最後一次處於烏賊態的時間(從沒變過 = −∞)。回傳 true = 本幀出一發,shotTime = 排定的出膛時刻
        //(≤ now;幀邊界會晚 0~1 幀,節奏以排定時刻計,不會越打越慢)
        public bool Tick(bool trigger, float now, float lastSquidTime, in BlasterTiming timing, out float shotTime)
        {
            EnsureInitialized();
            shotTime = 0f;
            if (trigger && !_wasTrigger)
            {
                _pressTime = now;
                _heldSince = now;
            }
            _wasTrigger = trigger;

            float allowed = _nextReady - timing.PreDelay;
            if (!_pending && now + Epsilon >= allowed)
            {
                bool buffered = now - _pressTime <= timing.InputBuffer + Epsilon;
                if (trigger || buffered)
                {
                    // 預備從「可開始預備」與「按下」較晚者起算(不是從偵測到的這一幀):幀邊界不會讓節奏變慢,
                    // 按住連射剛好每 RepeatFrame 一發。起點 ≥ 上一發 + RepeatFrame − 預備,所以出膛 ≥ 可再出膛的時刻
                    float start = Mathf.Max(allowed, trigger ? _heldSince : _pressTime);
                    _pendingTime = Mathf.Max(start + timing.PreDelay, lastSquidTime + timing.SquidPreDelay);
                    _commitTime = start;
                    _pending = true;
                    // 同一次按下只確定一發(點一下不會變兩發)
                    _pressTime = float.NegativeInfinity;
                }
            }
            if (_pending && now + Epsilon >= _pendingTime)
            {
                shotTime = _pendingTime;
                _pending = false;
                _lastShot = _pendingTime;
                _nextReady = _pendingTime + timing.Repeat;
                return true;
            }
            return false;
        }

        // 放開還沒出膛的那一發(被擊倒、大招佔用主武器、換裝)
        public void Cancel()
        {
            _pending = false;
            _pressTime = float.NegativeInfinity;
        }

        // 出膛後的 PostDelay 內(不能變烏賊 / 丟副武器)
        public bool InPostDelay(float now, in BlasterTiming timing)
        {
            return _initialized && now < _lastShot + timing.PostDelay - Epsilon;
        }

        // 預備進度 0 → 1(沒在預備 = −1):HUD / 角色姿勢的蓄勢動作
        public float WindupProgress(float now)
        {
            if (!_pending)
            {
                return -1f;
            }
            float span = _pendingTime - _commitTime;
            return span > Epsilon ? Mathf.Clamp01((now - _commitTime) / span) : 1f;
        }

        // 現在按住的話,再過幾秒出膛:bot 為預備時間補提前量(預備中 = 剩下的預備;冷卻中 = 等到可預備再加預備)
        public float SecondsUntilShot(float now, float lastSquidTime, in BlasterTiming timing)
        {
            if (_pending)
            {
                return Mathf.Max(0f, _pendingTime - now);
            }
            float commit = Mathf.Max(now, WindupAllowedAt(timing));
            float at = Mathf.Max(commit + timing.PreDelay, lastSquidTime + timing.SquidPreDelay);
            return at - now;
        }

        // 冷卻進度 0 → 1(上一發到可以再出膛;1 = 就緒):HUD
        public float CooldownProgress(float now, in BlasterTiming timing)
        {
            if (!_initialized || float.IsNegativeInfinity(_lastShot) || timing.Repeat <= Epsilon)
            {
                return 1f;
            }
            return Mathf.Clamp01((now - _lastShot) / timing.Repeat);
        }
    }
}

using System;
using SplatoonC.Core.Combat;
using SplatoonC.Core.Match;
using SplatoonC.Gameplay.Combat;
using UnityEngine;

namespace SplatoonC.Gameplay.Player
{
    // 大招能量的唯一持有者:訂閱 Inkling.TurfInked(自己新塗到的地面)充能,集滿按 Special(R)啟動目前裝備的大招;
    // 啟動中依大招持續秒數衰減(ActiveDuration ≤ 0 則由大招行為自己結束),啟動中不充能(S3 規則)。
    // 大招行為(SpecialWeapon 子類)由 Equip 依武器組合掛上;HUD 與 bot 讀值。
    //
    // 兩種消耗型態(research/04 §1.4,DESIGN_REFERENCE §5.1):
    //   持續型:能量就是計時器(上面的衰減);行為提早結束(Trizooka 第 3 發)→ 歸 0。
    //   鎖定型:行為呼叫 BeginLock(秒數) → 能量自己倒數到 0,期間不充能、不能再發動;以時間計算,
    //     角色陣亡(本元件被停用)也照樣倒數。裝置被破壞 → EndLock(序號) 立刻歸 0。
    //   「行為啟動中」(IsActive / ActiveSpecial:佔用主副武器與烏賊)與能量鎖定分開:Big Bubbler 設置動作期間兩者同時成立。
    // 被擊倒(S3):持續型在死亡瞬間停止衰減,剩下的套保留比例;鎖定型不中斷也不懲罰。
    public sealed class PlayerSpecialGauge : MonoBehaviour
    {
        [SerializeField, Tooltip("預設大招設定(未由武器組合指定時使用)")]
        private SpecialWeaponConfig _config;

        private PlayerInputRouter _input;
        private Inkling _inkling;
        private SpecialGauge _gauge;
        private SpecialWeapon _special;
        private float _costPoints;
        // 快取 delegate:訂閱/退訂用同一個實例,不每次配置
        private Action<float, bool> _onTurfInked;
        private bool _initialized;
        // 大招行為啟動中(佔用主副武器/烏賊的依據);能量可能同時在鎖定倒數
        private bool _active;
        // 鎖定倒數:每秒扣多少點、上次同步的時間、目前鎖定的序號
        private float _lockDrainPerSecond;
        private float _lockSyncedAt;
        private int _lockToken;

        public float Points
        {
            get
            {
                SyncLock();
                return _gauge.Points;
            }
        }

        public float Normalized => Points / SpecialGauge.Full;

        // 集滿可發動(鎖定倒數中不算)
        public bool IsFull
        {
            get
            {
                SyncLock();
                return _gauge.IsFull && !_gauge.IsLocked;
            }
        }

        public bool IsActive => _active;

        // 鎖定型大招放出後的自動倒數中
        public bool IsLocked
        {
            get
            {
                SyncLock();
                return _gauge.IsLocked;
            }
        }

        // 鎖定倒數剩餘秒數(沒有鎖定 = 0)
        public float LockRemainingSeconds => IsLocked && _lockDrainPerSecond > 0f ? _gauge.Points / _lockDrainPerSecond : 0f;

        public int ActivationCount { get; private set; }

        // 按下但此刻不能發動(Big Bubbler 在空中…)的次數
        public int RefusedCount { get; private set; }

        // 鎖定倒數結束(自然倒數到 0 或 EndLock)
        public event Action LockEnded;

        // 集滿所需點數(p)
        public float CostPoints => _costPoints > 0f ? _costPoints : (_config != null ? _config.FallbackCostPoints : 200f);

        public SpecialWeaponConfig SpecialConfig => _config;
        public SpecialWeapon Special => _special;

        // 啟動中的大招(未啟動 = null):主武器/副武器/烏賊依它的 Blocks* 判斷能不能用
        public SpecialWeapon ActiveSpecial => _active ? _special : null;

        // ── 舊 API 相容(三重龍捲風):M4 AutoTest 與 SpecialHud 仍在用 ──
        public TripleInkstrikeConfig Config => _config as TripleInkstrikeConfig;
        public int DevicesRemaining => _special is TripleInkstrikeSpecial strike ? strike.DevicesRemaining : 0;
        public bool CanThrowDevice => _special is TripleInkstrikeSpecial strike && strike.CanThrowDevice;

        private void Awake()
        {
            EnsureInitialized();
        }

        private void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }
            _initialized = true;
            _input = GetComponent<PlayerInputRouter>();
            _inkling = GetComponent<Inkling>();
            _onTurfInked = OnTurfInked;
            if (_config != null && _special == null)
            {
                AttachBehaviour(_config);
            }
        }

        // 換大招(WeaponLoadout 呼叫):集滿點數、行為元件一併更換;能量保留
        public void Equip(SpecialWeaponConfig config, float costPoints)
        {
            EnsureInitialized();
            if (config == null)
            {
                return;
            }
            if (_active)
            {
                EndActive();
            }
            _config = config;
            _costPoints = costPoints;
            AttachBehaviour(config);
        }

        private void AttachBehaviour(SpecialWeaponConfig config)
        {
            Type wanted = config.BehaviourType;
            if (_special != null && _special.GetType() != wanted)
            {
                DestroyImmediate(_special);
                _special = null;
            }
            if (_special == null)
            {
                _special = GetComponent(wanted) as SpecialWeapon;
                if (_special == null)
                {
                    _special = (SpecialWeapon)gameObject.AddComponent(wanted);
                }
            }
            _special.Configure(config);
        }

        private void OnEnable()
        {
            EnsureInitialized();
            if (_inkling != null)
            {
                _inkling.TurfInked += _onTurfInked;
            }
        }

        private void OnDisable()
        {
            if (_inkling != null)
            {
                _inkling.TurfInked -= _onTurfInked;
            }
        }

        private void OnTurfInked(float areaSquareMetres, bool chargesSpecial)
        {
            // 大招行為還在跑(能量耗盡後的延續:Ink Vac 發射模式…)也不充能(S3:啟動中一律不充)
            if (!chargesSpecial || _active)
            {
                return;
            }
            SyncLock();
            // 能量條 0~100 = 點數 / 集滿點數;暫時效果的充能倍率(SCU 曲線 × SpecialCharge×,無效果 = 1)
            float points = TurfPoints.FromArea(areaSquareMetres);
            if (_inkling != null)
            {
                points *= _inkling.Modifiers.SpecialChargeFactor;
            }
            _gauge.Charge(points * SpecialGauge.Full / Mathf.Max(1f, CostPoints));
        }

        // 蠻頹對戰的被動充能(§6.4:例 區域被敵方控制 4.5 p/s):以塗地點數計,依本大招集滿點數換算;
        // 不經塗地、不吃充能加速的暫時效果;啟動中不充能(SpecialGauge.Charge 本身擋掉)。
        public void ChargePassive(float turfPoints)
        {
            EnsureInitialized();
            if (turfPoints <= 0f)
            {
                return;
            }
            // 大招行為還在跑(能量耗盡後的延續:Ink Vac 發射模式…)也不充能
            if (_active)
            {
                return;
            }
            // 鎖定型大招倒數中不充能(SpecialGauge.Charge 擋);先補算倒數,剛倒數完的這一刻就能開始充
            SyncLock();
            _gauge.Charge(turfPoints * SpecialGauge.Full / Mathf.Max(1f, CostPoints));
        }

        private void Update()
        {
            SyncLock();
            if (_config == null || _input == null)
            {
                return;
            }
            if (_input.SpecialPressedThisFrame && !_active && _gauge.IsFull && !_gauge.IsLocked)
            {
                if (_special != null && !_special.CanActivateNow)
                {
                    // 此刻不能發動(Big Bubbler 在空中…):不消耗能量,交給大招提示「Can't Use!」
                    RefusedCount++;
                    _special.OnActivationRefused();
                }
                else if (_gauge.TryActivate())
                {
                    _active = true;
                    ActivationCount++;
                    // 使用大招:墨量立刻回滿(Splatoon 規則)、計入結算的大招次數
                    var tank = GetComponent<PlayerInkTank>();
                    if (tank != null)
                    {
                        tank.RefillFull();
                    }
                    if (_inkling != null)
                    {
                        _inkling.RegisterSpecialUse();
                    }
                    if (_special != null)
                    {
                        _special.OnActivated();
                    }
                }
            }
            if (_active && _gauge.IsActive && _config.ActiveDuration > 0f)
            {
                bool finished = _gauge.Tick(Time.deltaTime, SpecialGauge.Full / _config.ActiveDuration);
                // 能量耗盡:大招可要求行為繼續(Ink Vac 轉發射模式…),之後自己 EndActive
                if (finished && (_special == null || !_special.ContinueAfterGaugeEmpty()))
                {
                    FinishBehaviour();
                }
            }
        }

        // 大招行為自行結束(丟完、打完),或換裝/死亡中斷。持續型能量歸 0;鎖定倒數不受影響
        public void EndActive()
        {
            if (!_active)
            {
                return;
            }
            if (_gauge.IsActive)
            {
                _gauge.ForceEnd();
            }
            FinishBehaviour();
        }

        private void FinishBehaviour()
        {
            if (!_active)
            {
                return;
            }
            _active = false;
            if (_special != null)
            {
                _special.OnEnded();
            }
        }

        // 鎖定型大招放出:能量從目前值開始在 seconds 秒內倒數到 0(research/04 §1.4 表:
        // Wave Breaker 丟出起 ~390F、Big Bubbler 設置起 1001F、Tacticooler 600F…)。
        // keepActive = false → 行為同時結束(OnEnded);true → 行為繼續到它自己 EndActive。回傳鎖定序號。
        public int BeginLock(float seconds, bool keepActive = false)
        {
            SyncLock();
            _gauge.BeginLock();
            if (!_gauge.IsLocked)
            {
                if (!keepActive)
                {
                    FinishBehaviour();
                }
                return 0;
            }
            _lockDrainPerSecond = _gauge.Points / Mathf.Max(0.01f, seconds);
            _lockSyncedAt = Time.time;
            _lockToken++;
            if (!keepActive)
            {
                FinishBehaviour();
            }
            return _lockToken;
        }

        // 裝置被破壞:該次鎖定立刻結束、能量歸 0(序號不符 = 那次鎖定早就結束,不動目前的能量)
        public void EndLock(int token)
        {
            SyncLock();
            if (token == 0 || token != _lockToken || !_gauge.IsLocked)
            {
                return;
            }
            _gauge.EndLock();
            LockEnded?.Invoke();
        }

        // 這個序號的鎖定是否仍在倒數
        public bool IsLockCurrent(int token)
        {
            return token != 0 && token == _lockToken && IsLocked;
        }

        // 以時間同步鎖定倒數(陣亡期間本元件停用,讀值時補算)
        private void SyncLock()
        {
            if (!_gauge.IsLocked)
            {
                return;
            }
            float now = Time.time;
            float dt = now - _lockSyncedAt;
            _lockSyncedAt = now;
            if (dt > 0f && _gauge.TickLock(dt, _lockDrainPerSecond))
            {
                LockEnded?.Invoke();
            }
        }

        // 舊 API:ThrowController 丟出一個龍捲風裝置後回報(新流程由 TripleInkstrikeSpecial.OnThrow 處理)
        public bool TryRegisterDeviceThrow()
        {
            return false;
        }

        // 被擊倒時由 InklingLifecycle 呼叫(S3):啟動中的持續型大招在死亡瞬間停止衰減、剩下的保留 keepRatio;
        // 鎖定倒數中不中斷、不懲罰;未啟動保留 keepRatio
        public void ApplyDeathPenalty(float keepRatio)
        {
            SyncLock();
            _gauge.ApplyDeathPenalty(keepRatio);
            FinishBehaviour();
        }

        // 測試/除錯:直接設能量(清掉鎖定倒數;不改行為啟動狀態)。
        public void SetPointsForTest(float points)
        {
            _gauge.ClearLock();
            _gauge.SetPoints(points);
        }
    }
}

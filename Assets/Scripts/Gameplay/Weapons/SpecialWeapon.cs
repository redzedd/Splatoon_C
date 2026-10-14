using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 大招設定基底。繼承 ThrowableConfig 只是為了「會丟東西的大招」(三重龍捲風的信標、Booyah Bomb…)
    // 共用投擲/預覽參數;不丟東西的大招忽略那些欄位即可。
    public abstract class SpecialWeaponConfig : ThrowableConfig
    {
        [Header("大招")]
        [SerializeField, Tooltip("顯示名稱")]
        protected string _specialName = "大招";

        [SerializeField, Tooltip("沒有武器組合(WeaponKit)時的集滿點數(p)")]
        protected float _fallbackCostPoints = 200f;

        public string SpecialName => _specialName;
        public float FallbackCostPoints => _fallbackCostPoints;

        // 對應的 SpecialWeapon 子類
        public abstract System.Type BehaviourType { get; }

        // 啟動後能量從 100 耗到 0 的秒數(≤0 = 不隨時間消耗,由行為自己結束)
        public abstract float ActiveDuration { get; }
    }

    // 大招執行期行為基底:PlayerSpecialGauge 在集滿並按下時啟動它;
    // 啟動期間可以佔用主武器/副武器/烏賊,或接管投擲(ThrowOverride:放開副武器鍵丟大招物件)。
    public abstract class SpecialWeapon : MonoBehaviour
    {
        protected Inkling Owner { get; private set; }
        protected PlayerInputRouter Input { get; private set; }
        protected PlayerSpecialGauge Gauge { get; private set; }

        public abstract SpecialWeaponConfig BaseConfig { get; }

        public abstract void Configure(SpecialWeaponConfig config);

        public bool IsActive => Gauge != null && Gauge.IsActive && Gauge.Special == this;

        // 啟動中是否佔用主武器/副武器/烏賊態
        public virtual bool BlocksMainWeapon => true;
        public virtual bool BlocksSubWeapon => true;
        public virtual bool BlocksSquid => false;

        // 啟動中放開副武器鍵要丟的東西(null = 不接管投擲)
        public virtual ThrowableConfig ThrowOverride => null;

        // 啟動中 HUD 能量條下的短提示(null = 預設「發動中」;例:Trizooka「剩 2 發」、Wave Breaker「右鍵 丟出」)。
        // 每幀讀取:回傳快取字串,不要每次組新字串
        public virtual string HudHint => null;

        protected virtual void Awake()
        {
            Owner = GetComponent<Inkling>();
            Input = GetComponent<PlayerInputRouter>();
            Gauge = GetComponent<PlayerSpecialGauge>();
        }

        // 集滿按下時此刻能不能發動(預設可以)。回 false:不發動、不消耗能量,改呼叫 OnActivationRefused
        //(S3 Big Bubbler 在空中、懸崖邊、低天花板下顯示「Can't Use!」)
        public virtual bool CanActivateNow => true;

        // 發動被拒(CanActivateNow 為 false 時按下):提示玩家
        public virtual void OnActivationRefused()
        {
        }

        // 能量條剛進入啟動狀態
        public virtual void OnActivated()
        {
        }

        // 能量耗盡、行為自行結束或被擊倒中斷
        public virtual void OnEnded()
        {
        }

        // 持續型能量(ActiveDuration)自然耗盡的那一刻呼叫。回傳 true = 行為還沒結束、之後自己 EndSpecial
        //(S3:Ink Vac 6 秒吸完轉發射模式、Crab Tank 時間到的 25F 收尾硬直、Ultra Stamp 丟槌起手中);
        // 預設 false = 立刻結束(OnEnded)。能量已歸 0,行為繼續期間不充能。
        public virtual bool ContinueAfterGaugeEmpty()
        {
            return false;
        }

        // ThrowController 在接管投擲時放開副武器鍵後呼叫(原點與初速已算好,與預覽一致)
        public virtual void OnThrow(Vector3 origin, Vector3 velocity)
        {
        }

        // ── bot 用法 ──

        // 集滿時 bot 要不要現在開(預設:看得到敵人就開,否則偶爾開)
        public virtual bool BotWantsActivate(IBotControl bot)
        {
            return (bot.HasTarget && bot.TargetVisible) || Random.value < 0.25f;
        }

        // 啟動中每幀由 bot 呼叫:設定視線/按鍵/移動(預設什麼都不做 → 大招自己跑完)
        public virtual void BotDrive(IBotControl bot)
        {
        }

        // 行為自行結束(丟完、打完)
        protected void EndSpecial()
        {
            if (Gauge != null)
            {
                Gauge.EndActive();
            }
        }

        // 鎖定型大招放出(research/04 §1.4):能量改為 seconds 秒自己倒數到 0,期間不能充能、死亡不中斷。
        // keepActive = false:行為同時結束(Wave Breaker 丟出後玩家恢復行動);true:行為繼續到 EndSpecial(Big Bubbler 設置動作)。
        // 回傳鎖定序號,裝置被破壞時用 EndGaugeLock(序號) 讓能量立刻歸 0(舊裝置不會誤結束新的鎖定)。
        protected int LockGauge(float seconds, bool keepActive)
        {
            return Gauge != null ? Gauge.BeginLock(seconds, keepActive) : 0;
        }
    }
}

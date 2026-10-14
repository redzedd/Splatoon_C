using SplatoonC.Core.Combat;
using UnityEngine;

namespace SplatoonC.Gameplay.Player
{
    // 墨量的唯一持有者:InkShooter 消耗、本類依烏賊狀態回墨、HUD 讀值。
    public sealed class PlayerInkTank : MonoBehaviour
    {
        [SerializeField, Tooltip("角色移動設定資產(讀回墨速率)")]
        private PlayerLocomotionConfig _config;

        [SerializeField, Tooltip("無限墨(除錯/效能測試用,出貨關閉)")]
        private bool _infiniteInk;

        private SquidController _squid;
        private PlayerInputRouter _input;
        private InkTank _tank = InkTank.CreateFull();
        // S3 墨量鎖:每個動作各自上鎖(射擊 20F、炸彈 60F…),以最晚結束者為準;動作因墨不足失敗也照樣上鎖
        private float _lockedUntil;
        private InklingModifiers _modifiers;

        // 暫時效果(毒霧停止回墨/扣墨、Zipcaster 耗墨 ×0.67…)
        private InklingModifiers Modifiers => _modifiers != null ? _modifiers : (_modifiers = InklingModifiers.For(this));
        // 墨槽容量倍率(S3 Splattershot Jr. 1.1):耗墨參數都以標準墨槽為單位,實扣 = 參數 ÷ 容量。
        // 回墨仍以「整槽」計(潛墨 3 秒回滿任何大小的墨槽;sendou 的回墨秒數不隨墨槽大小換算)。
        private float _capacityScale = 1f;

        public float Normalized => _tank.Normalized;

        public float CapacityScale => _capacityScale;

        public bool InfiniteInk
        {
            get => _infiniteInk;
            set => _infiniteInk = value;
        }

        private void Awake()
        {
            _squid = GetComponent<SquidController>();
            _input = GetComponent<PlayerInputRouter>();
            if (_config == null)
            {
                Debug.LogError("PlayerInkTank:缺少移動設定資產,回墨失效", this);
            }
        }

        private void Update()
        {
            if (_config == null)
            {
                return;
            }
            // 暫時效果的持續扣墨(毒霧、Zipcaster):不看墨量鎖,扣到 0 為止;無限墨(除錯)時不扣
            float drain = Modifiers.ConsumeInkDrain(Time.deltaTime);
            if (drain > 0f && !_infiniteInk)
            {
                _tank.Drain(drain);
            }
            bool squid = _squid != null && _squid.IsSquid;
            if (Time.time < _lockedUntil)
            {
                return;
            }
            // 貼在自家墨牆上爬也算泡在墨裡(IsInOwnInk 同時涵蓋地面與牆面)
            bool squidOnInk = squid && _squid.IsInOwnInk;
            float rate = squidOnInk
                ? _config.SquidInkRefillPerSecond
                : _config.StandingRefillPerSecond;
            // 回墨倍率:毒霧/Zipcaster 歸零、Ink Recovery Up 加快(無效果 = 1)
            rate *= Modifiers.InkRefillFactor(squidOnInk);
            _tank.Refill(rate, Time.deltaTime);
        }

        public bool IsLocked => Time.time < _lockedUntil;

        // 上墨量鎖(秒);與現有鎖取較晚結束者
        public void LockRefill(float seconds)
        {
            _lockedUntil = Mathf.Max(_lockedUntil, Time.time + seconds);
        }

        // 重生、使用大招:墨量回滿
        public void RefillFull()
        {
            _tank.Refill(1f, 1f);
        }

        // 動作因墨不足失敗(低墨提示音/HUD)
        public event System.Action Shortage;

        // 直接扣墨(毒霧分段扣墨等);扣到 0 為止,回傳實際扣掉的量
        public float Drain(float fraction)
        {
            return _infiniteInk ? 0f : _tank.Drain(fraction);
        }

        // 換裝時由 WeaponLoadout 設定;墨量比例不變(S3 不能在場上換武器,這裡只影響之後的消耗)
        public void SetCapacityScale(float scale)
        {
            _capacityScale = scale > 0f ? scale : 1f;
        }

        // costPerShot = 標準墨槽的分數(0.0092 = 0.92%);容量大的墨槽實際扣得少
        public bool TryConsume(float costPerShot)
        {
            if (_infiniteInk)
            {
                return true;
            }
            // 耗墨倍率(Zipcaster 主武器 ×0.67;大招期間不能丟副武器,全域套用等價)
            costPerShot *= Modifiers.InkCostMultiplier;
            if (_tank.TryConsume(costPerShot / _capacityScale))
            {
                return true;
            }
            Shortage?.Invoke();
            return false;
        }
    }
}

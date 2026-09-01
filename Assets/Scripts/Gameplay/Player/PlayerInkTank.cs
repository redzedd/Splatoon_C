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
        private InkRefillGate _refillGate;

        public float Normalized => _tank.Normalized;

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
            bool squid = _squid != null && _squid.IsSquid;
            // 烏賊態本來就不能射擊,按住攻擊鍵不算開火,不該擋回墨
            bool firing = _input != null && _input.AttackHeld && !squid;
            if (!_refillGate.Evaluate(firing, Time.time, _config.RefillDelayAfterFiring))
            {
                return;
            }
            bool squidOnInk = squid && _squid.OnOwnInk;
            float rate = squidOnInk
                ? _config.SquidInkRefillPerSecond
                : _config.StandingRefillPerSecond;
            _tank.Refill(rate, Time.deltaTime);
        }

        public bool TryConsume(float costPerShot)
        {
            if (_infiniteInk)
            {
                return true;
            }
            return _tank.TryConsume(costPerShot);
        }
    }
}

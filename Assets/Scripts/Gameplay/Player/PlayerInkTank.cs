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
        private InkTank _tank = InkTank.CreateFull();

        public float Normalized => _tank.Normalized;

        public bool InfiniteInk
        {
            get => _infiniteInk;
            set => _infiniteInk = value;
        }

        private void Awake()
        {
            _squid = GetComponent<SquidController>();
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
            bool squidOnInk = _squid != null && _squid.IsSquid && _squid.OnOwnInk;
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

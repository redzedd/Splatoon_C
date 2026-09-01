using SplatoonC.Gameplay.Painting;
using UnityEngine;

namespace SplatoonC.Gameplay.Player
{
    // 烏賊態:按住 Crouch 變形——自家墨上加速、乾地減速、視覺下沉壓扁、不可射擊(InkShooter 讀 IsSquid)。
    public sealed class SquidController : MonoBehaviour
    {
        [SerializeField, Tooltip("角色移動設定資產(讀烏賊態倍率)")]
        private PlayerLocomotionConfig _config;

        [SerializeField, Tooltip("輸入來源;留空自動抓同物件上的 PlayerInputRouter")]
        private PlayerInputRouter _input;

        [SerializeField, Tooltip("要壓扁的視覺根(Player/Visual)")]
        private Transform _visualRoot;

        [SerializeField, Tooltip("腳下偵測射線圖層(場景接線時排除 Player)")]
        private LayerMask _groundMask = ~0;

        public bool IsSquid { get; private set; }

        public float CurrentSpeedMultiplier { get; private set; } = 1f;

        // 供測試/HUD 讀:目前腳下是否自家墨。
        public bool OnOwnInk { get; private set; }

        private CharacterController _controller;
        private float _squashVelocity;
        private bool _wasGrounded;
        private float _lastVerticalSpeed;

        private void Awake()
        {
            if (_config == null)
            {
                Debug.LogError("SquidController:缺少移動設定資產,烏賊態失效", this);
            }
            if (_input == null)
            {
                _input = GetComponent<PlayerInputRouter>();
            }
            _controller = GetComponent<CharacterController>();
        }

        private void Update()
        {
            if (_config == null || _input == null)
            {
                return;
            }

            IsSquid = _input.SquidHeld;

            // M2 重構:改問腳下表面自己的歸屬網格(牆面查詢走同一模式)。
            OnOwnInk = false;
            if (Physics.Raycast(transform.position + Vector3.up * 0.3f, Vector3.down,
                    out RaycastHit groundHit, 1.2f, _groundMask, QueryTriggerInteraction.Ignore))
            {
                var surface = groundHit.collider.GetComponent<PaintableSurface>();
                if (surface != null)
                {
                    OnOwnInk = surface.SampleOwnership(groundHit.point) == 1;
                }
            }

            CurrentSpeedMultiplier = IsSquid
                ? (OnOwnInk ? _config.SquidInkSpeedMultiplier : _config.SquidDrySpeedMultiplier)
                : 1f;

            // 落地擠壓:夠快落地時往壓扁方向踢一下,交給彈簧自然回彈
            bool grounded = _controller != null && _controller.isGrounded;
            if (grounded && !_wasGrounded && _lastVerticalSpeed < -_config.LandSquashMinFallSpeed)
            {
                _squashVelocity += _config.LandSquashKick;
            }
            _wasGrounded = grounded;
            _lastVerticalSpeed = _controller != null ? _controller.velocity.y : 0f;

            // 彈簧變形:剛性拉向目標、指數阻尼,過衝一次回穩(取代線性 MoveTowards)
            if (_visualRoot != null)
            {
                float targetY = IsSquid ? _config.SquidVisualScaleY : 1f;
                float current = _visualRoot.localScale.y;
                _squashVelocity += (targetY - current) * _config.SquashStiffness * Time.deltaTime;
                _squashVelocity *= Mathf.Exp(-_config.SquashDamping * Time.deltaTime);
                Vector3 scale = _visualRoot.localScale;
                scale.y = Mathf.Clamp(current + _squashVelocity * Time.deltaTime, 0.15f, 1.35f);
                _visualRoot.localScale = scale;
            }
        }
    }
}

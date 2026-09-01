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

        public bool IsSquid { get; private set; }

        public float CurrentSpeedMultiplier { get; private set; } = 1f;

        // 供測試/HUD 讀:目前腳下是否自家墨。
        public bool OnOwnInk { get; private set; }

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
        }

        private void Update()
        {
            if (_config == null || _input == null)
            {
                return;
            }

            IsSquid = _input.SquidHeld;
            OnOwnInk = InkWorld.Instance != null
                && InkWorld.Instance.SampleOwnership(transform.position) == 1;

            CurrentSpeedMultiplier = IsSquid
                ? (OnOwnInk ? _config.SquidInkSpeedMultiplier : _config.SquidDrySpeedMultiplier)
                : 1f;

            if (_visualRoot != null)
            {
                float targetY = IsSquid ? _config.SquidVisualScaleY : 1f;
                Vector3 scale = _visualRoot.localScale;
                scale.y = Mathf.MoveTowards(scale.y, targetY, _config.SquidSquashSpeed * Time.deltaTime);
                _visualRoot.localScale = scale;
            }
        }
    }
}

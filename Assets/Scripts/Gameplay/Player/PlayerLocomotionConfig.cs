using SplatoonC.Core.Locomotion;
using UnityEngine;

namespace SplatoonC.Gameplay.Player
{
    [CreateAssetMenu(fileName = "PlayerLocomotionConfig", menuName = "SplatoonC/角色移動設定")]
    public sealed class PlayerLocomotionConfig : ScriptableObject
    {
        [SerializeField, Tooltip("移動速度(公尺/秒)")]
        private float _moveSpeed = 6f;

        [SerializeField, Tooltip("重力加速度(負值;非真實重力,遊戲手感優先)")]
        private float _gravity = -25f;

        [SerializeField, Tooltip("跳躍高度(公尺),初速由此反算")]
        private float _jumpHeight = 1.6f;

        [SerializeField, Range(0f, 1f), Tooltip("空中控制係數(1 = 空中與地面同速,0 = 空中完全不可控)")]
        private float _airControl = 0.6f;

        [SerializeField, Tooltip("土狼時間(秒):離地後仍可起跳的寬限")]
        private float _coyoteTime = 0.12f;

        [SerializeField, Tooltip("跳躍緩衝(秒):落地前按跳的寬限")]
        private float _jumpBuffer = 0.10f;

        [SerializeField, Tooltip("轉向角速度(度/秒)")]
        private float _turnSpeed = 720f;

        [SerializeField, Tooltip("著地時的下壓速度(負值),防著地旗標抖動")]
        private float _groundedStick = -2f;

        [Header("烏賊態")]
        [SerializeField, Tooltip("烏賊態在自家墨上的速度倍率")]
        private float _squidInkSpeedMultiplier = 1.8f;

        [SerializeField, Tooltip("烏賊態在無墨地面的速度倍率(蹲行慢)")]
        private float _squidDrySpeedMultiplier = 0.7f;

        [SerializeField, Range(0.1f, 1f), Tooltip("烏賊態視覺下沉:Visual 的 Y 縮放")]
        private float _squidVisualScaleY = 0.3f;

        [SerializeField, Tooltip("視覺壓扁/回彈速度(每秒縮放變化量)")]
        private float _squidSquashSpeed = 6f;

        public float SquidInkSpeedMultiplier => _squidInkSpeedMultiplier;
        public float SquidDrySpeedMultiplier => _squidDrySpeedMultiplier;
        public float SquidVisualScaleY => _squidVisualScaleY;
        public float SquidSquashSpeed => _squidSquashSpeed;

        public float TurnSpeedDegPerSec => _turnSpeed;

        public LocomotionSettings ToSettings()
        {
            return new LocomotionSettings(
                _moveSpeed,
                _gravity,
                _jumpHeight,
                _airControl,
                _coyoteTime,
                _jumpBuffer,
                _turnSpeed,
                _groundedStick);
        }
    }
}

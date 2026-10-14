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

        [SerializeField, Tooltip("加速時間(秒):0→滿速;0 = 瞬時")]
        private float _accelTime = 0.12f;

        [SerializeField, Tooltip("減速時間(秒):滿速→0;0 = 瞬時")]
        private float _decelTime = 0.08f;

        [SerializeField, Tooltip("著地時的下壓速度(負值),防著地旗標抖動")]
        private float _groundedStick = -2f;

        [Header("烏賊態")]
        [SerializeField, Tooltip("烏賊態在自家墨上的速度倍率")]
        private float _squidInkSpeedMultiplier = 1.8f;

        [SerializeField, Tooltip("烏賊態在無墨地面的速度倍率(蹲行慢)")]
        private float _squidDrySpeedMultiplier = 0.7f;

        [SerializeField, Tooltip("離開墨水後速度倍率滑落到平時速度所需的秒數(0 = 立刻歸位)")]
        private float _inkExitSpeedDecayDuration = 0.36f;

        [SerializeField, Range(0, 57), Tooltip("常駐裝備:游泳速度提升(Swim Speed Up)AP,所有角色(含 bot)都套 S3 游速曲線。" +
                                                  "0 = 原作不帶裝備(中量級 11.52 m/s);57 = S3 上限,三個重量級都 14.4 m/s")]
        private int _swimSpeedUpAp;

        [SerializeField, Range(0.1f, 1f), Tooltip("烏賊態視覺下沉:Visual 的 Y 縮放")]
        private float _squidVisualScaleY = 0.3f;

        [SerializeField, Tooltip("變形彈簧剛性(越大變形越快)")]
        private float _squashStiffness = 250f;

        [SerializeField, Tooltip("變形彈簧阻尼(越小過衝回彈越明顯)")]
        private float _squashDamping = 16f;

        [SerializeField, Tooltip("落地擠壓踢速(負值 = 往壓扁方向)")]
        private float _landSquashKick = -5f;

        [SerializeField, Tooltip("觸發落地擠壓的最小下落速度(公尺/秒)")]
        private float _landSquashMinFallSpeed = 6f;

        [Header("鑽進/鑽出墨水的過場")]
        [SerializeField, Tooltip("鑽進墨裡的秒數;過場期間視覺仍顯示,只是往下沉")]
        private float _diveDuration = 0.18f;

        [SerializeField, Tooltip("鑽出墨面的秒數(通常比鑽進快,起身要俐落)")]
        private float _surfaceDuration = 0.12f;

        [SerializeField, Tooltip("下沉深度(公尺);要大於角色高度才會被地面完全遮住")]
        private float _diveDepth = 1.4f;

        [SerializeField, Range(0f, 1f), Tooltip("下沉過程的橫向收縮量;0 = 不縮,做出被墨吸進去的感覺")]
        private float _diveHorizontalShrink = 0.45f;

        [Header("S3 狀態速度")]
        [SerializeField, Tooltip("按住副武器(投擲姿勢)時的走速(公尺/秒;S3 固定 0.72 DU/F = 4.32)")]
        private float _throwStanceSpeed = 4.32f;

        [SerializeField, Tooltip("敵墨中起跳高度倍率(S3 初速 0.8 vs 1.1 DU/F → (0.8/1.1)² ≈ 0.53,跳不上 1.5m 台階)")]
        private float _enemyInkJumpMultiplier = 0.53f;

        [SerializeField, Tooltip("攻擊中(開火/投擲/大招姿勢)加速度倍率(S3:0.02 vs 0.01 u/F²)")]
        private float _attackAccelMultiplier = 2f;

        public float ThrowStanceSpeed => _throwStanceSpeed;
        public float EnemyInkJumpMultiplier => _enemyInkJumpMultiplier;
        public float AttackAccelMultiplier => _attackAccelMultiplier;

        [Header("爬牆(烏賊態,自家墨牆)")]
        [SerializeField, Tooltip("爬牆速度(公尺/秒)")]
        private float _climbSpeed = 3.5f;

        [SerializeField, Tooltip("貼牆速度(公尺/秒),維持吸附不脫落")]
        private float _wallStickSpeed = 1f;

        [SerializeField, Tooltip("牆面探測距離(公尺,自胸口向前)")]
        private float _climbProbeDistance = 0.7f;

        [SerializeField, Tooltip("到頂翻越時間(秒):斜上位移把角色送上牆頂")]
        private float _mantleDuration = 0.35f;

        [SerializeField, Tooltip("到頂翻越的推送速度(公尺/秒);與爬牆速度分開,爬得快也不會翻過頭")]
        private float _mantleSpeed = 4f;

        public float ClimbSpeed => _climbSpeed;
        public float MantleSpeed => _mantleSpeed;
        public float WallStickSpeed => _wallStickSpeed;
        public float ClimbProbeDistance => _climbProbeDistance;
        public float MantleDuration => _mantleDuration;

        [Header("墨量")]
        [SerializeField, Tooltip("烏賊在自家墨上的回墨速率(每秒,0~1;0.2 = 五秒回滿)")]
        private float _squidInkRefillPerSecond = 0.2f;

        [SerializeField, Tooltip("非烏賊時的緩慢回墨速率(每秒,0~1);要泡在墨裡才回得快")]
        private float _standingRefillPerSecond = 0.05f;


        public float SquidInkRefillPerSecond => _squidInkRefillPerSecond;
        public float StandingRefillPerSecond => _standingRefillPerSecond;

        public float SquidInkSpeedMultiplier => _squidInkSpeedMultiplier;
        public float SquidDrySpeedMultiplier => _squidDrySpeedMultiplier;
        public float SquidVisualScaleY => _squidVisualScaleY;
        public float InkExitSpeedDecayDuration => _inkExitSpeedDecayDuration;
        public int SwimSpeedUpAp => _swimSpeedUpAp;
        public float DiveDuration => _diveDuration;
        public float SurfaceDuration => _surfaceDuration;
        public float DiveDepth => _diveDepth;
        public float DiveHorizontalShrink => _diveHorizontalShrink;
        public float SquashStiffness => _squashStiffness;
        public float SquashDamping => _squashDamping;
        public float LandSquashKick => _landSquashKick;
        public float LandSquashMinFallSpeed => _landSquashMinFallSpeed;

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
                _groundedStick,
                _accelTime,
                _decelTime);
        }
    }
}

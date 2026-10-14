using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 投擲物共用設定基底:炸彈與大招裝置各自繼承,投擲/預覽參數放這裡。
    public abstract class ThrowableConfig : ScriptableObject
    {
        [Header("投擲")]
        [SerializeField, Tooltip("投擲初速(公尺/秒)")]
        protected float _throwSpeed = 14f;

        [SerializeField, Tooltip("投擲上抬角(度):相機瞄準方向再往上抬,拋物線才有弧度")]
        protected float _liftAngleDeg = 20f;

        [SerializeField, Tooltip("重力(負值;投擲物專用,不用步槍的兩段式)")]
        protected float _gravity = -20f;

        [SerializeField, Tooltip("安全壽命(秒):超過就回收(掉出場外用)")]
        protected float _lifetime = 8f;

        [SerializeField, Tooltip("命中圖層(預設排除第 8 槽 Player)")]
        protected LayerMask _hitMask = ~(1 << 8);

        [SerializeField, Tooltip("可受傷目標圖層(第 6 槽 Target);視線判定時要排除目標本身")]
        protected LayerMask _targetMask = 1 << 6;

        [SerializeField, Tooltip("瞄準射線取點距離(公尺):相機中心射線上這麼遠的點(或先撞到的點)當投擲目標")]
        protected float _aimReach = 15f;

        [Header("拋物線預覽")]
        [SerializeField, Tooltip("預覽積分步長(秒)")]
        protected float _previewStepTime = 0.03f;

        [SerializeField, Tooltip("預覽最多步數(60 步 × 0.03s = 1.8 秒飛行)")]
        protected int _previewMaxSteps = 60;

        [SerializeField, Tooltip("投擲後的墨量鎖(秒;S3 斯普拉炸彈 60F = 1.0)")]
        protected float _inkLockSeconds = 1f;

        public float InkLockSeconds => _inkLockSeconds;

        // 墨色由投擲者的隊伍決定(TeamPalette)
        [Header("墨")]
        [SerializeField, Range(0f, 1f), Tooltip("塗墨筆刷硬度")]
        protected float _paintHardness = 0.7f;

        public float ThrowSpeed => _throwSpeed;
        public float LiftAngleDeg => _liftAngleDeg;
        public float Gravity => _gravity;
        public float Lifetime => _lifetime;
        public LayerMask HitMask => _hitMask;
        public LayerMask TargetMask => _targetMask;
        public float AimReach => _aimReach;
        public float PreviewStepTime => _previewStepTime;
        public int PreviewMaxSteps => _previewMaxSteps;
        public float PaintHardness => _paintHardness;
    }
}

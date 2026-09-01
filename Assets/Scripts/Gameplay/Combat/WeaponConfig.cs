using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    [CreateAssetMenu(fileName = "WeaponConfig", menuName = "SplatoonC/武器設定")]
    public sealed class WeaponConfig : ScriptableObject
    {
        [Header("連射")]
        [SerializeField, Tooltip("連射間隔(秒);0.125 = 每秒 8 發")]
        private float _fireInterval = 0.125f;

        [SerializeField, Tooltip("長掉幀時單幀最大補射數,其餘欠帳丟棄")]
        private int _maxShotsPerFrame = 4;

        [Header("彈道")]
        // 2026-09-01 調參:22/-18 落點僅約 5.6m 遠短於準星;32/-10 在 14m 瞄準點只低約 1m。
        [SerializeField, Tooltip("墨彈初速(公尺/秒)")]
        private float _muzzleSpeed = 32f;

        [SerializeField, Tooltip("墨彈重力(負值;比世界重力輕,飛出墨水拋物線)")]
        private float _projectileGravity = -10f;

        [SerializeField, Tooltip("墨彈存活秒數,超時自動回收")]
        private float _projectileLifetime = 3f;

        [SerializeField, Tooltip("散布角(度):每發在瞄準方向的隨機錐形內偏移")]
        private float _spreadAngleDeg = 2.5f;

        [SerializeField, Range(0f, 1f), Tooltip("每發墨量消耗(0~1 正規化;0.045 約 22 發射空)")]
        private float _inkCostPerShot = 0.045f;

        [SerializeField, Tooltip("墨彈命中偵測圖層(排除 Player)")]
        private LayerMask _hitMask = ~0;

        [Header("塗色")]
        [SerializeField, Tooltip("墨水顏色")]
        private Color _inkColor = new Color(1f, 0.5f, 0f, 1f);

        [SerializeField, Tooltip("主 splat 半徑(公尺)")]
        private float _splatRadius = 0.65f;

        [SerializeField, Range(0f, 1f), Tooltip("主 splat 筆刷硬度")]
        private float _splatHardness = 0.7f;

        [SerializeField, Tooltip("噴濺小點數量(命中時繞主點灑出)")]
        private int _splashCount = 2;

        [SerializeField, Tooltip("噴濺小點半徑(公尺)")]
        private float _splashRadius = 0.3f;

        [SerializeField, Tooltip("噴濺散布距離(公尺,自主點外擴)")]
        private float _splashSpread = 0.6f;

        public float FireInterval => _fireInterval;
        public int MaxShotsPerFrame => _maxShotsPerFrame;
        public float MuzzleSpeed => _muzzleSpeed;
        public float ProjectileGravity => _projectileGravity;
        public float ProjectileLifetime => _projectileLifetime;
        public float SpreadAngleDeg => _spreadAngleDeg;
        public float InkCostPerShot => _inkCostPerShot;
        public LayerMask HitMask => _hitMask;
        public Color InkColor => _inkColor;
        public float SplatRadius => _splatRadius;
        public float SplatHardness => _splatHardness;
        public int SplashCount => _splashCount;
        public float SplashRadius => _splashRadius;
        public float SplashSpread => _splashSpread;
    }
}

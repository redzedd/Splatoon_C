using SplatoonC.Core.Match;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 大招「三重龍捲風」(S3 Triple Inkstrike):啟動後 6 秒內用副武器鍵丟最多 3 個信標,
    // 信標貼牆/地後長出龍捲風光柱。投擲參數在 ThrowableConfig;集滿點數由武器組合決定(沒有組合時用 FallbackCostPoints)。
    [CreateAssetMenu(fileName = "TripleInkstrikeConfig", menuName = "SplatoonC/三重龍捲風設定")]
    public sealed class TripleInkstrikeConfig : SpecialWeaponConfig
    {
        [Header("預製物")]
        [SerializeField, Tooltip("信標 prefab(Assets/Prefabs/InkstrikeDevice)")]
        private InkstrikeDevice _devicePrefab;

        [SerializeField, Tooltip("龍捲風 prefab(Assets/Prefabs/Tornado)")]
        private Tornado _tornadoPrefab;

        [Header("大招持續")]
        [SerializeField, Tooltip("啟動後能量從 100 衰減到 0 的秒數;歸零即結束,不管裝置有沒有丟完")]
        private float _activeDuration = 6f;

        [SerializeField, Tooltip("每次大招最多丟幾個裝置;丟滿能量直接歸零結束")]
        private int _maxDevices = 3;

        [Header("裝置")]
        [SerializeField, Tooltip("附著後幾秒長出龍捲風")]
        private float _armDelay = 0.5f;

        [SerializeField, Tooltip("附著時沿法線外推(公尺),防陷入面")]
        private float _stickOffset = 0.05f;

        [SerializeField, Tooltip("裝置視覺邊長(公尺)")]
        private float _visualSize = 0.35f;

        [Header("龍捲風")]
        [SerializeField, Tooltip("起始半徑(公尺)")]
        private float _tornadoStartRadius = 0.2f;

        [SerializeField, Tooltip("最大半徑(公尺)")]
        private float _tornadoMaxRadius = 3f;

        [SerializeField, Tooltip("光柱高度(公尺),一律世界垂直向上")]
        private float _tornadoHeight = 6f;

        [SerializeField, Tooltip("由細變粗到最大半徑的秒數")]
        private float _tornadoGrowDuration = 1f;

        [SerializeField, Tooltip("到最大後停留秒數(0 = 到頂即消失)")]
        private float _tornadoHoldDuration = 0.2f;

        [SerializeField, Tooltip("消失淡出秒數(淡出期間不傷不塗)")]
        private float _tornadoFadeDuration = 0.25f;

        [SerializeField, Tooltip("範圍內每秒傷害(180 → 假人 0.56 秒死)")]
        private float _tornadoDamagePerSecond = 180f;

        [SerializeField, Tooltip("傷害結算間隔(秒);不每幀結算,頭頂數字才不會每幀重建字串")]
        private float _tornadoDamageTickInterval = 0.1f;

        [SerializeField, Tooltip("塗墨間隔(秒)")]
        private float _tornadoPaintInterval = 0.1f;

        [SerializeField, Tooltip("每次塗墨的 splat 半徑(公尺)")]
        private float _tornadoPaintRadius = 1f;

        [SerializeField, Tooltip("每次塗墨在當前半徑圓周打幾條向下射線(相位每次轉黃金角,掃滿整個圓盤)")]
        private int _tornadoRingRayCount = 8;

        [SerializeField, Tooltip("每次塗墨的徑向射線數(貼牆時牆面才有墨)")]
        private int _tornadoWallRayCount = 8;

        public override System.Type BehaviourType => typeof(TripleInkstrikeSpecial);
        public override float ActiveDuration => _activeDuration;
        public InkstrikeDevice DevicePrefab => _devicePrefab;
        public Tornado TornadoPrefab => _tornadoPrefab;

        // 能量條 0~100:每 m² 新地盤換算成 100 / (大招所需點數 × 每點面積)(除錯顯示用;實際點數由 PlayerSpecialGauge 決定)
        public float ChargePerSquareMetre => 100f / (Mathf.Max(1f, _fallbackCostPoints) * TurfPoints.SquareMetresPerPoint);
        public int MaxDevices => _maxDevices;
        public float ArmDelay => _armDelay;
        public float StickOffset => _stickOffset;
        public float VisualSize => _visualSize;
        public float TornadoStartRadius => _tornadoStartRadius;
        public float TornadoMaxRadius => _tornadoMaxRadius;
        public float TornadoHeight => _tornadoHeight;
        public float TornadoGrowDuration => _tornadoGrowDuration;
        public float TornadoHoldDuration => _tornadoHoldDuration;
        public float TornadoFadeDuration => _tornadoFadeDuration;
        public float TornadoDamagePerSecond => _tornadoDamagePerSecond;
        public float TornadoDamageTickInterval => _tornadoDamageTickInterval;
        public float TornadoPaintInterval => _tornadoPaintInterval;
        public float TornadoPaintRadius => _tornadoPaintRadius;
        public int TornadoRingRayCount => _tornadoRingRayCount;
        public int TornadoWallRayCount => _tornadoWallRayCount;
    }
}

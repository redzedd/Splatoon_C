using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 大招「Big Bubbler」(S3 SpGreatBarrier,research/04 §2.2、JP wiki グレートバリア;v11.3.0 0 AP):
    // 在腳下放發射座,無人機升到 8.5 u 撐開球形屏障(半徑 7.5,前隙 50F)。屏障擋下罩外敵方的子彈與爆炸,
    // 敵人本身可以走進來、罩內往外打不擋。耐久 1536(地面裝置與頂端無人機 = 弱點 ×1;打在屏障上 ×0.64,共用耐久),
    // 每秒自然扣 92.1 → 約 1001F 消失;耐久越低屏障越小(RadiusRatioCurve)。鎖定型:設置起 1001F 自動倒數,被打破即歸 0。
    [CreateAssetMenu(fileName = "Special_big_bubbler", menuName = "SplatoonC/大招/Big Bubbler")]
    public sealed class BigBubblerConfig : SpecialWeaponConfig
    {
        [Header("預製物")]
        [SerializeField, Tooltip("裝置 prefab(BigBubblerDevice:地面發射座 + 無人機 + 屏障)")]
        private BigBubblerDevice _devicePrefab;

        [Header("設置")]
        [SerializeField, Tooltip("設置前隙(JP wiki 前隙 50F):這段時間不能行動,屏障展開完成才開始擋")]
        private int _frontLagFrames = 50;

        [SerializeField, Tooltip("無人機點火(DroneParam.IgnitionFrame 15F)")]
        private int _ignitionFrame = 15;

        [SerializeField, Tooltip("無人機上升時間(AscendFrame 30F,AscendCurve)")]
        private int _ascendFrames = 30;

        [SerializeField, Tooltip("無人機最高高度(AscendHeight 8.5 u = 屏障頂端)")]
        private float _droneMaxHeight = 8.5f;

        [SerializeField, Tooltip("設置時腳下塗墨半徑(BaseParam.PaintRadius 4.5)")]
        private float _paintRadius = 4.5f;

        [SerializeField, Tooltip("天花板低於此高度(公尺)不能發動(S3 低天花板「Can't Use!」)[EST]")]
        private float _minCeiling = 3.5f;

        [SerializeField, Tooltip("懸崖邊判定:腳底周圍這個半徑(公尺)的四個點都要踩得到地 [EST;JP wiki:前腳超出 1/4 就不行]")]
        private float _edgeProbeRadius = 0.35f;

        [Header("屏障")]
        [SerializeField, Tooltip("最大 / 最小半徑(MaxRadius 7.5 / MinRadius 2.255)")]
        private Vector2 _radius = new Vector2(7.5f, 2.255f);

        [SerializeField, Tooltip("球心離地高度(公尺):頂端 = 無人機 8.5 u、半徑 7.5 → 1.0 [推導]")]
        private float _centerHeight = 1f;

        [SerializeField, Tooltip("耐久(MaxHP 15360 → 1536;SPU 1843.2)")]
        private float _maxHp = 1536f;

        [SerializeField, Tooltip("每秒自然扣耐久(TimeDamage 921 → 92.1/s;1536 / 92.1 = 16.68 s ≈ 1001F)")]
        private float _decayPerSecond = 92.1f;

        [SerializeField, Tooltip("打在屏障上的傷害倍率(DamgeRatio 0.64;打發射座/無人機 ×1)")]
        private float _barrierDamageRatio = 0.64f;

        [SerializeField, Tooltip("能量鎖定倒數(幀;設置起自然消失的時間 1001F,被打破即歸 0)")]
        private int _lockFrames = 1001;

        [Header("音效")]
        [SerializeField, Tooltip("設置")]
        private string _deploySound = "big_bubbler_deploy";

        [SerializeField, Tooltip("屏障擋下攻擊")]
        private string _hitSound = "big_bubbler_hit";

        [SerializeField, Tooltip("屏障破掉/消失")]
        private string _popSound = "big_bubbler_pop";

        public override System.Type BehaviourType => typeof(BigBubblerSpecial);

        // 行為只有設置動作,由 BigBubblerSpecial 在前隙結束時自己結束(能量走鎖定倒數)
        public override float ActiveDuration => 0f;

        public BigBubblerDevice DevicePrefab => _devicePrefab;
        public int FrontLagFrames => _frontLagFrames;
        public int IgnitionFrame => _ignitionFrame;
        public int AscendFrames => _ascendFrames;
        public float DroneMaxHeight => _droneMaxHeight;
        public float PaintRadius => _paintRadius;
        public float MinCeiling => _minCeiling;
        public float EdgeProbeRadius => _edgeProbeRadius;
        public float MaxRadius => _radius.x;
        public float MinRadius => _radius.y;
        public float CenterHeight => _centerHeight;
        public float MaxHp => _maxHp;
        public float DecayPerSecond => _decayPerSecond;
        public float BarrierDamageRatio => _barrierDamageRatio;
        public float LockSeconds => _lockFrames / 60f;
        public string DeploySound => _deploySound;
        public string HitSound => _hitSound;
        public string PopSound => _popSound;
    }
}

using SplatoonC.Core.Combat;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 大招「Ultra Stamp」(S3 SpUltraStamp,research/04 §2.11、JP wiki ウルトラハンコ、DESIGN_REFERENCE §5.2;v11.3.0 0 AP):
    // 扛起巨大印章槌 450F(能量 = 計時器),發動前隙 17F;拿著時可以潛游。
    // 地上點 ZR:30F(從烏賊 35F)舉起砸下 → 直擊 100(槌頭掃過的範圍,約 5 m)+ 衝擊波 40(≤ 4.0)、擊退 460/6、蓋印章;
    //   兩次敲之間 20F。按住 ZR:連敲(每 13F 一下)自動前進 0.155 u/F(塗不到的地面 0.144),擊退 500/8,跳起來 = 原地連敲。
    // 空中 ZR:迴旋揮擊(8F)100,半徑約 4.5。揮擊中槌頭擋住正面的子彈、炸彈與爆炸(舉起的瞬間擋不到)。
    // R(副武器鍵):42F 起手後把槌丟出(0.85 u/F + 上 0.17、重力 0.013),直擊 220(貫穿角色)、
    //   撞到地形/物件爆炸 220(≤ 3.6)/ 60(≤ 8.0)、塗 5.0;丟出即結束大招。
    [CreateAssetMenu(fileName = "Special_ultra_stamp", menuName = "SplatoonC/大招/Ultra Stamp")]
    public sealed class UltraStampConfig : SpecialWeaponConfig
    {
        [Header("預製物")]
        [SerializeField, Tooltip("手上的大槌(插槽座標,+Z = 握把往槌頭;名為 Head 的子物件 = 槌頭中心、InkGuide = 丟出預覽線)")]
        private GameObject _hammerPrefab;

        [SerializeField, Tooltip("槌頭擋彈判定(UltraStampGuard;Target 圖層,揮擊中跟著槌頭)")]
        private UltraStampGuard _guardPrefab;

        [SerializeField, Tooltip("丟出的槌(InkBullet)")]
        private InkBullet _throwPrefab;

        [Header("時間(幀,60 Hz)")]
        [SerializeField, Tooltip("效果時間(SpecialTotalFrame 450F;SPU 660F)")]
        private int _durationFrames = UltraStampModel.DurationFrames;

        [SerializeField, Tooltip("發動前隙(17F)")]
        private int _activationLagFrames = UltraStampModel.ActivationLagFrames;

        [SerializeField, Tooltip("敲地前隙:人形 / 從烏賊(30F / 35F)")]
        private Vector2Int _stampLag = new Vector2Int(UltraStampModel.StampFrontLagFrames, UltraStampModel.StampFrontLagSquidFrames);

        [SerializeField, Tooltip("點按兩次敲之間(振り間隔 20F)")]
        private int _stampIntervalFrames = UltraStampModel.StampIntervalFrames;

        [SerializeField, Tooltip("連敲間隔(13F)")]
        private int _rushIntervalFrames = UltraStampModel.RushIntervalFrames;

        [SerializeField, Tooltip("空中迴旋前隙(8F)")]
        private int _airSwingLagFrames = UltraStampModel.AirSwingLagFrames;

        [SerializeField, Tooltip("空中迴旋後接連敲(40F)")]
        private int _airToRushFrames = UltraStampModel.AirToRushFrames;

        [SerializeField, Tooltip("丟出起手(42F)")]
        private int _throwWindupFrames = UltraStampModel.ThrowWindupFrames;

        [Header("揮擊")]
        [SerializeField, Tooltip("直擊傷害(100)")]
        private float _swingDamage = UltraStampModel.SwingDamage;

        [SerializeField, Tooltip("衝擊波傷害 / 半徑(SwingBlastParam 400 @ 4.0 → 40)")]
        private Vector2 _swingBlast = new Vector2(UltraStampModel.SwingBlastDamage, UltraStampModel.SwingBlastRadius);

        [SerializeField, Tooltip("槌頭落點離腳底(公尺)[EST:直擊射程約 6 u = 落點 + 槌頭半徑]")]
        private float _impactDistance = 3.6f;

        [SerializeField, Tooltip("直擊判定半徑(槌頭掃過的膠囊;公尺)[EST]")]
        private float _headRadius = 1.5f;

        [SerializeField, Tooltip("空中迴旋半徑(約 0.9 條試射線 = 4.5 u)")]
        private float _airSwingRadius = UltraStampModel.AirSwingRadius;

        [SerializeField, Tooltip("擊退:單敲 / 連敲(SwingSmallBlast 460/6、HoldSwingKnockBack 500/8)")]
        private Vector4 _knockback = new Vector4(460f, 6f, 500f, 8f);

        [SerializeField, Tooltip("印章塗墨的主圓半徑(公尺;一個印章約 4.5p)[EST]")]
        private float _stampPaintRadius = 1.3f;

        [Header("移動(u/F)")]
        [SerializeField, Tooltip("連敲前進:一般 / 塗不到的地面(0.155 / 0.144)")]
        private Vector2 _rushSpeed = new Vector2(UltraStampModel.RushSpeed, UltraStampModel.RushSpeedNoPaint);

        [SerializeField, Tooltip("連敲轉向(度/秒)[EST:v3.1.0 起比較好轉]")]
        private float _rushTurnDegPerSecond = 150f;

        [SerializeField, Tooltip("單敲時的走速倍率 [EST]")]
        private float _stampMoveMultiplier = 0.3f;

        [Header("丟出")]
        [SerializeField, Tooltip("直擊(DirectDamageValue 2200 → 220;貫穿角色)")]
        private float _throwDirect = UltraStampModel.ThrowDirect;

        [SerializeField, Tooltip("爆炸:近 220 ≤ 3.6、遠 60 ≤ 8.0(v11.1.0)")]
        private Vector4 _throwBlast = new Vector4(UltraStampModel.ThrowNearDamage, UltraStampModel.ThrowNearRadius,
            UltraStampModel.ThrowFarDamage, UltraStampModel.ThrowFarRadius);

        [SerializeField, Tooltip("爆炸塗墨(PaintRadius 5.0)")]
        private float _throwPaint = UltraStampModel.ThrowPaintRadius;

        [SerializeField, Tooltip("丟出點相對腳底(右、上、前;公尺)")]
        private Vector3 _throwOrigin = new Vector3(0.2f, 2.3f, 0.6f);

        [Header("音效")]
        [SerializeField, Tooltip("扛起")]
        private string _activateSound = "ultra_stamp_activate";

        [SerializeField, Tooltip("舉起揮下")]
        private string _swingSound = "ultra_stamp_swing";

        [SerializeField, Tooltip("敲到地面")]
        private string _stampSound = "ultra_stamp_stamp";

        [SerializeField, Tooltip("丟出起手")]
        private string _windupSound = "ultra_stamp_windup";

        [SerializeField, Tooltip("丟出")]
        private string _throwSound = "ultra_stamp_throw";

        [SerializeField, Tooltip("丟出的槌爆炸(未合成時退回 bomb_explode)")]
        private string _explodeSound = "ultra_stamp_explode";

        public override System.Type BehaviourType => typeof(UltraStampSpecial);
        public override float ActiveDuration => _durationFrames / 60f;

        public GameObject HammerPrefab => _hammerPrefab;
        public UltraStampGuard GuardPrefab => _guardPrefab;
        public InkBullet ThrowPrefab => _throwPrefab;
        public float ActivationLag => _activationLagFrames / 60f;
        public float StampLagHuman => _stampLag.x / 60f;
        public float StampLagSquid => _stampLag.y / 60f;
        public float StampInterval => _stampIntervalFrames / 60f;
        public float RushInterval => _rushIntervalFrames / 60f;
        public float AirSwingLag => _airSwingLagFrames / 60f;
        public float AirToRush => _airToRushFrames / 60f;
        public float ThrowWindup => _throwWindupFrames / 60f;
        public float SwingDamage => _swingDamage;
        public float SwingBlastDamage => _swingBlast.x;
        public float SwingBlastRadius => _swingBlast.y;
        public float ImpactDistance => _impactDistance;
        public float HeadRadius => _headRadius;
        public float AirSwingRadius => _airSwingRadius;
        public Vector4 Knockback => _knockback;
        public float StampPaintRadius => _stampPaintRadius;
        public float RushSpeed => _rushSpeed.x;
        public float RushSpeedNoPaint => _rushSpeed.y;
        public float RushTurnDegPerSecond => _rushTurnDegPerSecond;
        public float StampMoveMultiplier => _stampMoveMultiplier;
        public Vector3 ThrowOrigin => _throwOrigin;
        public string ActivateSound => _activateSound;
        public string SwingSound => _swingSound;
        public string StampSound => _stampSound;
        public string WindupSound => _windupSound;
        public string ThrowSound => _throwSound;

        // 敲地衝擊波:40(≤ 4.0,無衰減)、擊退依單敲/連敲;不塗(印章另外塗)
        public ExplosionSpec BuildSwingBlast(bool rush)
        {
            return new ExplosionSpec
            {
                NearDamage = _swingBlast.x,
                NearRadius = _swingBlast.y,
                FarDamage = _swingBlast.x,
                FarRadius = _swingBlast.y,
                AttackClass = AttackClass.UltraStampSwing,
                Kind = DamageKind.Special,
                NoSpecialCharge = true,
                KnockbackAccel = rush ? _knockback.z : _knockback.x,
                KnockbackRange = rush ? _knockback.w : _knockback.y,
                FxBursts = 0,
                FlashRadius = -1f,
                Sound = null,
            };
        }

        // 丟出的槌:S3 彈道(0.85 u/F、重力 0.013、[EST] 阻力 0.006)、判定半徑 1.1(GuideRadius)、直擊 220 貫穿角色、
        // 碰到地形/物件爆炸 220 / 60、塗 5.0、擊退 460/8
        public InkBulletSpec BuildThrowSpec(string explodeSound)
        {
            return new InkBulletSpec
            {
                Motion = UltraStampModel.ThrowMotion,
                LifetimeFrames = 300,
                RadiusForPlayer = UltraStampModel.ThrowGuideRadius,
                RadiusForField = 0.45f,
                PiercePlayers = true,
                DamageMax = _throwDirect,
                DamageMin = _throwDirect,
                ReduceStartFrame = -1,
                AttackClass = AttackClass.UltraStampThrow,
                Kind = DamageKind.Special,
                PaintWidthHalfNear = 0f,
                PaintWidthHalfFar = 0f,
                PaintRadiusShock = 0f,
                NoSpecialCharge = true,
                ExplodeOnImpact = true,
                Explosion = new ExplosionSpec
                {
                    NearDamage = _throwBlast.x,
                    NearRadius = _throwBlast.y,
                    FarDamage = _throwBlast.z,
                    FarRadius = _throwBlast.w,
                    AttackClass = AttackClass.UltraStampThrow,
                    Kind = DamageKind.Special,
                    PaintRadius = _throwPaint,
                    PaintRayCount = 12,
                    PaintRayRadius = 1.2f,
                    PaintRayReach = 6f,
                    DropCount = 10,
                    DropRadius = 0.9f,
                    DropReach = _throwPaint * 1.4f,
                    PaintHardness = 0.75f,
                    NoSpecialCharge = true,
                    KnockbackAccel = 460f,
                    KnockbackRange = 8f,
                    FxBursts = 8,
                    FlashRadius = _throwBlast.y,
                    Sound = explodeSound,
                    SoundVolume = 1f,
                },
                VisualScale = 1f,
                TrailTime = 0.12f,
                Silent = true,
            };
        }
    }
}

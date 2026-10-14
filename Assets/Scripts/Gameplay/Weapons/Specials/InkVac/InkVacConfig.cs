using SplatoonC.Core.Combat;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 大招「Ink Vac」(S3 SpBlower,research/04 §2.8、JP wiki キューインキ、DESIGN_REFERENCE §5.2;v11.3.0 0 AP):
    // 背著吸塵器、雙手端噴嘴。發動前隙 17F;按住攻擊(人形前隙 12F、從烏賊 18F)→ 前方吸引漩渦(錐長 45F 長到 15 u,
    // 半徑 0.8 → 3.3)吸走敵方的墨彈、炸彈與爆炸(不傷人、不塗墨),容量以吸進的傷害量計(1100 滿);
    // 漩渦碰到的敵人不受傷但像毒霧一樣變慢、每幀扣 0.22% 墨,容量每幀 +1.5;碰到的場上裝置每幀 2 傷。
    // 放開攻擊 = 停止吸(可以潛游)。吸滿或 360F(能量 = 計時器)到 → 發射模式:20F 準備,放開攻擊發射,150F 沒放就自動發射。
    // 反擊彈:0.55 → 0.7 u/F(依吸入量)、幾乎不掉速;直擊 220(對物件 0);落地 50F 後爆炸 220(無衰減),
    // 半徑 6 → 11 u 依吸入量,落點顯示圓圈(bot 50F 內跑出去)。走速 0.09 → 0.07 u/F(越吸越重)、發射模式 0.072 u/F;
    // 吸引中每 20F 在身後塗一小片。
    [CreateAssetMenu(fileName = "Special_ink_vac", menuName = "SplatoonC/大招/Ink Vac")]
    public sealed class InkVacConfig : SpecialWeaponConfig
    {
        [Header("預製物")]
        [SerializeField, Tooltip("反擊彈(InkBullet;Assets/Prefabs/Weapons/InkVacShot)")]
        private InkBullet _shotPrefab;

        [SerializeField, Tooltip("落地後倒數的爆炸(InkVacBlast:地上的範圍圈 + 50F 後爆)")]
        private InkVacBlast _blastPrefab;

        [SerializeField, Tooltip("吸引漩渦的外觀(InkVacVortex:半透明錐 + 往槍口捲的環)")]
        private InkVacVortex _vortexPrefab;

        [SerializeField, Tooltip("被吸進噴嘴的墨點特效(InkVacSuckFx)")]
        private InkVacSuckFx _suckFxPrefab;

        [SerializeField, Tooltip("手上的噴嘴模型(插槽座標,+Z = 噴口方向;Ink* 上隊伍色)")]
        private GameObject _nozzlePrefab;

        [SerializeField, Tooltip("背上的吸塵器罐(胸口骨;名為 InkFill 的部位隨吸入量升高)")]
        private GameObject _backpackPrefab;

        [Header("時序(幀,60 Hz)")]
        [SerializeField, Tooltip("吸引時間 = 能量(360F = 6 s),到了轉發射模式")]
        private int _suctionFrames = InkVacModel.SuctionFrames;

        [SerializeField, Tooltip("發動前隙(スペシャル発動隙 17F)")]
        private int _activationLagFrames = InkVacModel.ActivationLagFrames;

        [SerializeField, Tooltip("按下攻擊到開始吸:人形 / 從烏賊(12F / 18F)")]
        private Vector2Int _suctionLag = new Vector2Int(InkVacModel.FrontLagHumanFrames, InkVacModel.FrontLagSquidFrames);

        [SerializeField, Tooltip("轉發射模式後不能射的幀數(InhaleToExhaleWaitFrame 20)")]
        private int _transitionFrames = InkVacModel.InhaleToExhaleFrames;

        [SerializeField, Tooltip("發射模式最多等幾幀就自動發射(ExhaleWaitFrame 150)")]
        private int _exhaleWaitFrames = InkVacModel.ExhaleWaitFrames;

        [SerializeField, Tooltip("反擊彈落地到爆炸(SpawnBlastWaitFrame 50)")]
        private int _blastWaitFrames = InkVacModel.BlastWaitFrames;

        [Header("吸引")]
        [SerializeField, Tooltip("容量(吸進的傷害量;v11.3.0 1100)")]
        private float _capacity = InkVacModel.Capacity;

        [SerializeField, Tooltip("吸到敵方角色每幀加的容量(JP wiki 實測 1.5)")]
        private float _playerFillPerFrame = InkVacModel.PlayerFillPerFrame;

        [SerializeField, Tooltip("漩渦對場上裝置每幀的傷害(DamagePerFrameByObject 20 → 2.0)")]
        private float _objectDamagePerFrame = InkVacModel.ObjectDamagePerFrame;

        [SerializeField, Tooltip("被吸的敵人每秒扣墨(0.22%/F = 13.2%/s)")]
        private float _poisonInkDrainPerSecond = 0.0022f * 60f;

        [SerializeField, Tooltip("被吸的敵人移動倍率(PoisonMistForPlayer:同毒霧 [EST 0.5])")]
        private float _poisonMoveMultiplier = 0.5f;

        [SerializeField, Tooltip("噴嘴轉向上限(度/秒;JP wiki「装置の旋回速度がカメラより遅い」[EST])")]
        private float _nozzleTurnDegPerSecond = 300f;

        [SerializeField, Tooltip("噴嘴口相對腳底(右、上、前;公尺)")]
        private Vector3 _muzzleOffset = new Vector3(0.25f, 1.05f, 0.9f);

        [Header("反擊彈")]
        [SerializeField, Tooltip("直擊傷害(DirectDamage 2200 → 220,v2.0.0)")]
        private float _shotDamage = InkVacModel.ShotDamage;

        [SerializeField, Tooltip("對人判定半徑:沒吸 / 吸滿(RadiusForPlayerMinCharge 0.4;吸滿 [EST 0.6])")]
        private Vector2 _shotRadius = new Vector2(0.4f, 0.6f);

        [SerializeField, Tooltip("爆炸半徑:沒吸 / 吸滿(DistanceDamage 6.0 / 11.0,v10.1.0)")]
        private Vector2 _blastRadius = new Vector2(InkVacModel.BlastRadiusMin, InkVacModel.BlastRadiusMax);

        [SerializeField, Tooltip("爆炸傷害(2200 → 220,半徑內不衰減)")]
        private float _blastDamage = InkVacModel.ShotDamage;

        [SerializeField, Tooltip("擊退(KnockBackParam Accel 700 / Distance 9)")]
        private Vector2 _knockback = new Vector2(700f, 9f);

        [SerializeField, Tooltip("飛行途中塗墨帶寬的半徑:沒吸 / 吸滿(PaintRadiusMin/MaxCharge 2.4 / 3.2 當帶寬,滴半徑取一半)")]
        private Vector2 _trailPaint = new Vector2(InkVacModel.ShotPaintMin, InkVacModel.ShotPaintMax);

        [Header("移動(u/F)")]
        [SerializeField, Tooltip("人形走速:沒吸 / 吸滿(MoveSpeedMinCharge 0.09 / FullCharge 0.07)")]
        private Vector2 _moveSpeed = new Vector2(InkVacModel.MoveSpeedEmpty, InkVacModel.MoveSpeedFull);

        [SerializeField, Tooltip("發射模式走速(JP wiki 0.72 DU/F)")]
        private float _fireMoveSpeed = InkVacModel.MoveSpeedFire;

        [Header("塗墨")]
        [SerializeField, Tooltip("吸引中每幾幀在身後塗一片(PaintSplashSpanFrame 20,v8.0.0)")]
        private int _paintSpanFrames = InkVacModel.PaintSplashSpanFrames;

        [SerializeField, Tooltip("身後那片的半徑(PaintSplashParam WidthHalf 1.5)")]
        private float _paintRadius = 1.5f;

        [Header("音效")]
        [SerializeField, Tooltip("發動")]
        private string _activateSound = "ink_vac_activate";

        [SerializeField, Tooltip("吸引中(循環)")]
        private string _suctionLoop = "ink_vac_suction_loop";

        [SerializeField, Tooltip("吸到東西")]
        private string _absorbSound = "ink_vac_absorb";

        [SerializeField, Tooltip("吸滿 / 轉發射模式")]
        private string _fullSound = "ink_vac_full";

        [SerializeField, Tooltip("發射")]
        private string _fireSound = "ink_vac_fire";

        [SerializeField, Tooltip("落地後的倒數警告")]
        private string _warnSound = "ink_vac_blast_warn";

        [SerializeField, Tooltip("爆炸(未合成時退回 bomb_explode)")]
        private string _explodeSound = "ink_vac_explode";

        public override System.Type BehaviourType => typeof(InkVacSpecial);

        // 吸引 360F = 能量計時器;到了由行為接著跑發射模式(ContinueAfterGaugeEmpty)
        public override float ActiveDuration => _suctionFrames / 60f;

        public InkBullet ShotPrefab => _shotPrefab;
        public InkVacBlast BlastPrefab => _blastPrefab;
        public InkVacVortex VortexPrefab => _vortexPrefab;
        public InkVacSuckFx SuckFxPrefab => _suckFxPrefab;
        public GameObject NozzlePrefab => _nozzlePrefab;
        public GameObject BackpackPrefab => _backpackPrefab;
        public int ActivationLagFrames => _activationLagFrames;
        public int SuctionLagHuman => _suctionLag.x;
        public int SuctionLagSquid => _suctionLag.y;
        public int TransitionFrames => _transitionFrames;
        public int ExhaleWaitFrames => _exhaleWaitFrames;
        public int BlastWaitFrames => _blastWaitFrames;
        public float Capacity => _capacity;
        public float PlayerFillPerFrame => _playerFillPerFrame;
        public float ObjectDamagePerFrame => _objectDamagePerFrame;
        public float PoisonInkDrainPerSecond => _poisonInkDrainPerSecond;
        public float PoisonMoveMultiplier => _poisonMoveMultiplier;
        public float NozzleTurnDegPerSecond => _nozzleTurnDegPerSecond;
        public Vector3 MuzzleOffset => _muzzleOffset;
        public float ShotDamage => _shotDamage;
        public float BlastDamage => _blastDamage;
        public Vector2 Knockback => _knockback;
        public float FireMoveSpeed => _fireMoveSpeed;
        public int PaintSpanFrames => _paintSpanFrames;
        public float PaintRadius => _paintRadius;
        public string ActivateSound => _activateSound;
        public string SuctionLoop => _suctionLoop;
        public string AbsorbSound => _absorbSound;
        public string FullSound => _fullSound;
        public string FireSound => _fireSound;
        public string WarnSound => _warnSound;
        public string ExplodeSound => _explodeSound;

        public float BlastRadius(float fill01)
        {
            return Mathf.Lerp(_blastRadius.x, _blastRadius.y, Mathf.Clamp01(fill01));
        }

        public float MoveSpeed(float fill01)
        {
            return Mathf.Lerp(_moveSpeed.x, _moveSpeed.y, Mathf.Clamp01(fill01));
        }

        // 反擊彈規格:S3 彈道(依吸入量的初速)、直擊 220 貫穿角色、不爆(落地後由 InkVacBlast 倒數)、沿途塗墨帶
        public InkBulletSpec BuildShotSpec(float fill01)
        {
            float trail = Mathf.Lerp(_trailPaint.x, _trailPaint.y, Mathf.Clamp01(fill01));
            return new InkBulletSpec
            {
                Motion = InkVacModel.ShotMotion(fill01),
                LifetimeFrames = 600,
                RadiusForPlayer = Mathf.Lerp(_shotRadius.x, _shotRadius.y, Mathf.Clamp01(fill01)),
                RadiusForField = 0.25f,
                PiercePlayers = true,
                DamageMax = _shotDamage,
                DamageMin = _shotDamage,
                ReduceStartFrame = -1,
                AttackClass = AttackClass.InkVac,
                Kind = DamageKind.Special,
                PaintWidthHalfNear = trail * 0.5f,
                PaintWidthHalfFar = trail * 0.5f,
                PaintDepthScaleMax = 1.4f,
                PaintDepthScaleMin = 1.0f,
                PaintRadiusShock = trail * 0.5f,
                PaintHardness = 0.75f,
                DripSpacing = 2.5f,
                DripStartDistance = 2f,
                DripRadius = trail * 0.5f,
                NoSpecialCharge = true,
                VisualScale = Mathf.Lerp(1f, 1.5f, Mathf.Clamp01(fill01)),
                TrailTime = 0.18f,
            };
        }

        // 落地爆炸:220(半徑內無衰減)、塗同半徑、擊退 700/9
        public ExplosionSpec BuildBlastSpec(float radius, string sound)
        {
            return new ExplosionSpec
            {
                NearDamage = _blastDamage,
                NearRadius = radius,
                FarDamage = _blastDamage,
                FarRadius = radius,
                AttackClass = AttackClass.InkVac,
                Kind = DamageKind.Special,
                PaintRadius = radius,
                PaintRayCount = 16,
                PaintRayRadius = 1.4f,
                PaintRayReach = radius,
                DropCount = 10,
                DropRadius = 1.2f,
                DropReach = radius * 1.1f,
                PaintHardness = 0.75f,
                NoSpecialCharge = true,
                KnockbackAccel = _knockback.x,
                KnockbackRange = _knockback.y,
                FxBursts = 8,
                FlashRadius = radius * 0.8f,
                Sound = sound,
                SoundVolume = 1f,
            };
        }
    }
}

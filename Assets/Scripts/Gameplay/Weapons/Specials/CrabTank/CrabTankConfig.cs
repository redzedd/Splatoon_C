using SplatoonC.Core.Combat;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 大招「Crab Tank」(S3 SpChariot,research/04 §2.12、JP wiki カニタンク、DESIGN_REFERENCE §5.2;v11.3.0 0 AP):
    // 搭上蟹型戰車 480F(能量 = 計時器)。發動 45F 後裝甲(耐久 500、TargetClass.CrabTank 倍率)才出現;
    // 駕駛從上方、背後、側面打得到。步行:橫移 0.075 u/F、前後 0.015 u/F,車身慢慢轉向準心。
    // ZR 連射(12F → 4F、左右砲管輪流、32 → 16 飛行 9 → 25F);R 砲(每 75F、直擊 50 貫穿角色 + 著地爆炸 30 ≤ 4.8、塗 4.5,
    // 發射後 32F 不能連射);ZL 滾球(0.1 u/F 全方向、可爬塗得到的牆、腳下一直塗、接觸 40、全身受保護、回血同潛墨,
    // 解除後 20F 不能射、車身轉向鏡頭)。時間到或被打爆:戰車消失,25F 不能攻擊(被打爆 = 駕駛彈出、不算被擊倒)。
    [CreateAssetMenu(fileName = "Special_crab_tank", menuName = "SplatoonC/大招/Crab Tank")]
    public sealed class CrabTankConfig : SpecialWeaponConfig
    {
        [Header("預製物")]
        [SerializeField, Tooltip("戰車本體(CrabTankBody:裝甲碰撞體 + 步行/滾球外觀)")]
        private CrabTankBody _bodyPrefab;

        [SerializeField, Tooltip("連射彈(InkBullet)")]
        private InkBullet _bulletPrefab;

        [SerializeField, Tooltip("砲彈(InkBullet)")]
        private InkBullet _cannonPrefab;

        [SerializeField, Tooltip("駕駛手上的操縱桿(藏起主武器用;Ink* 上隊伍色)")]
        private GameObject _gripPrefab;

        [Header("時間(幀,60 Hz)")]
        [SerializeField, Tooltip("效果時間(SpecialTotalFrame 480F;SPU 660F)")]
        private int _durationFrames = CrabTankModel.DurationFrames;

        [SerializeField, Tooltip("裝甲出現(使用者畫面 45F)")]
        private int _armorDelayFrames = CrabTankModel.ArmorDelayFrames;

        [SerializeField, Tooltip("結束/被打爆後不能攻擊(NoShotBombReqFrm_FinishSpecial 25F,v3.1.0)")]
        private int _endLagFrames = CrabTankModel.EndLagFrames;

        [SerializeField, Tooltip("結束前閃紅(秒)[EST]")]
        private float _warnSeconds = 1.5f;

        [Header("裝甲與移動")]
        [SerializeField, Tooltip("裝甲耐久(ArmorHP 5000 → 500)")]
        private float _armorHp = CrabTankModel.ArmorHp;

        [SerializeField, Tooltip("步行:橫移 / 前後(MoveSpeedX 0.075、MoveSpeedZ 0.015 u/F)")]
        private Vector2 _walkSpeed = new Vector2(CrabTankModel.WalkSpeedLateral, CrabTankModel.WalkSpeedForward);

        [SerializeField, Tooltip("車身轉向(度/秒)[EST:S3 旋回極慢]")]
        private float _turnDegPerSecond = CrabTankModel.TurnDegPerSecond;

        [SerializeField, Tooltip("滾球速度(u/F;JP wiki 約 1.00 DU/F)")]
        private float _ballSpeed = CrabTankModel.BallSpeed;

        [SerializeField, Tooltip("車身擋牆的半徑(公尺;CollisionRadiusForField 1.15)")]
        private float _bodyRadius = 1.15f;

        [Header("ZR 連射")]
        [SerializeField, Tooltip("連射間隔:起始 / 最快(ShooterRepeatFrame_First 12 → 4)")]
        private Vector2 _turretRepeat = new Vector2(CrabTankModel.TurretRepeatFirst, CrabTankModel.TurretRepeatLast);

        [SerializeField, Tooltip("連射幾幀後到最快(ShooterRepeatFrame_Term 90)")]
        private int _turretRampFrames = CrabTankModel.TurretRampFrames;

        [SerializeField, Tooltip("傷害 最大 / 最小(ValueMax 320 / ValueMin 160 → 32 / 16)")]
        private Vector2 _turretDamage = new Vector2(CrabTankModel.TurretDamageMax, CrabTankModel.TurretDamageMin);

        [SerializeField, Tooltip("傷害衰減 開始 / 結束幀(ReduceStartFrame 9 / ReduceEndFrame 25)")]
        private Vector2Int _turretReduce = new Vector2Int(CrabTankModel.TurretReduceStart, CrabTankModel.TurretReduceEnd);

        [SerializeField, Tooltip("散布(度;JP wiki 拡散 3.0°)")]
        private float _turretSpread = CrabTankModel.TurretSpreadDeg;

        [SerializeField, Tooltip("左右砲管口相對腳底(右、上、前;公尺;左邊鏡射 x)")]
        private Vector3 _turretMuzzle = new Vector3(0.78f, 1.05f, 1.15f);

        [Header("R 砲")]
        [SerializeField, Tooltip("砲擊間隔(75F)")]
        private int _cannonIntervalFrames = CrabTankModel.CannonIntervalFrames;

        [SerializeField, Tooltip("砲擊後不能連射(CannonNoShotFrame 32F)")]
        private int _cannonNoShotFrames = CrabTankModel.CannonNoShotFrames;

        [SerializeField, Tooltip("直擊(DirectDamage 500 → 50,v10.0.0)")]
        private float _cannonDirect = CrabTankModel.CannonDirect;

        [SerializeField, Tooltip("爆炸傷害 / 半徑(DistanceDamage 300 @ 4.8,v11.2.0)")]
        private Vector2 _cannonBlast = new Vector2(CrabTankModel.CannonBlast, CrabTankModel.CannonBlastRadius);

        [SerializeField, Tooltip("爆炸塗墨半徑(PaintRadius 4.5)")]
        private float _cannonPaint = CrabTankModel.CannonPaintRadius;

        [SerializeField, Tooltip("砲口相對腳底(中間那門;公尺)")]
        private Vector3 _cannonMuzzle = new Vector3(0f, 1.3f, 1.3f);

        [SerializeField, Tooltip("砲彈往上抬的角度(度)[EST:曲射,平射約 28 u]")]
        private float _cannonLiftDeg = 4f;

        [Header("滾球")]
        [SerializeField, Tooltip("接觸傷害(JP wiki 40)")]
        private float _ballContactDamage = CrabTankModel.BallContactDamage;

        [SerializeField, Tooltip("同一人再吃接觸傷害的間隔(秒)[EST]")]
        private float _ballContactInterval = 0.33f;

        [SerializeField, Tooltip("接觸判定半徑(CollisionRadiusForPlayer 1.45)")]
        private float _ballContactRadius = 1.45f;

        [SerializeField, Tooltip("解除滾球後不能射(PreDelayFrm_Squid_ShotBomb 20F)")]
        private int _unrollNoShotFrames = CrabTankModel.UnrollNoShotFrames;

        [SerializeField, Tooltip("滾球腳下塗墨半徑(公尺)[EST]")]
        private float _ballPaintRadius = 1.2f;

        [Header("音效")]
        [SerializeField, Tooltip("召喚戰車")]
        private string _activateSound = "crab_tank_activate";

        [SerializeField, Tooltip("連射")]
        private string _turretSound = "crab_tank_shot";

        [SerializeField, Tooltip("砲擊")]
        private string _cannonSound = "crab_tank_cannon";

        [SerializeField, Tooltip("砲彈爆炸(未合成時退回 bomb_explode)")]
        private string _cannonExplodeSound = "crab_tank_cannon_explode";

        [SerializeField, Tooltip("變形(步行 ↔ 滾球)")]
        private string _transformSound = "crab_tank_transform";

        [SerializeField, Tooltip("滾動(循環)")]
        private string _rollLoop = "crab_tank_roll_loop";

        [SerializeField, Tooltip("被打爆")]
        private string _breakSound = "crab_tank_break";

        [SerializeField, Tooltip("時間到消失")]
        private string _endSound = "crab_tank_end";

        public override System.Type BehaviourType => typeof(CrabTankSpecial);
        public override float ActiveDuration => _durationFrames / 60f;

        public CrabTankBody BodyPrefab => _bodyPrefab;
        public InkBullet BulletPrefab => _bulletPrefab;
        public InkBullet CannonPrefab => _cannonPrefab;
        public GameObject GripPrefab => _gripPrefab;
        public float ArmorDelaySeconds => _armorDelayFrames / 60f;
        public float EndLagSeconds => _endLagFrames / 60f;
        public float WarnSeconds => _warnSeconds;
        public float ArmorHp => _armorHp;
        public float WalkLateral => _walkSpeed.x;
        public float WalkForward => _walkSpeed.y;
        public float TurnDegPerSecond => _turnDegPerSecond;
        public float BallSpeed => _ballSpeed;
        public float BodyRadius => _bodyRadius;
        public int TurretRampFrames => _turretRampFrames;
        public float TurretSpread => _turretSpread;
        public Vector3 TurretMuzzle => _turretMuzzle;
        public float CannonInterval => _cannonIntervalFrames / 60f;
        public float CannonNoShot => _cannonNoShotFrames / 60f;
        public Vector3 CannonMuzzle => _cannonMuzzle;
        public float CannonLiftDeg => _cannonLiftDeg;
        public float BallContactDamage => _ballContactDamage;
        public float BallContactInterval => _ballContactInterval;
        public float BallContactRadius => _ballContactRadius;
        public float UnrollNoShot => _unrollNoShotFrames / 60f;
        public float BallPaintRadius => _ballPaintRadius;
        public string ActivateSound => _activateSound;
        public string TurretSound => _turretSound;
        public string CannonSound => _cannonSound;
        public string TransformSound => _transformSound;
        public string RollLoop => _rollLoop;
        public string BreakSound => _breakSound;
        public string EndSound => _endSound;

        // 連射 heldFrames 幀時的發射間隔(幀):12 → 4 線性
        public float TurretRepeatFrames(float heldFrames)
        {
            float t = Mathf.Clamp01(heldFrames / Mathf.Max(1, _turretRampFrames));
            return Mathf.Lerp(_turretRepeat.x, _turretRepeat.y, t);
        }

        // 連射彈:射擊槍類彈道(3.36 u/F × 7F → 減速上限 2.232)、32 → 16、對人/地形 0.2、塗 1.6 / 1.5、牆 1.3、墨滴每 10 m
        public InkBulletSpec BuildTurretSpec()
        {
            InkBulletSpec spec = InkBulletSpec.ShooterFamily(CrabTankModel.TurretSpawnSpeed, CrabTankModel.TurretStraightFrames,
                CrabTankModel.TurretStraightEndMaxSpeed, _turretDamage.x, _turretDamage.y, _turretReduce.x, _turretReduce.y,
                0.2f, AttackClass.CrabTankCannon);
            spec.Kind = DamageKind.Special;
            spec.RadiusForField = 0.2f;
            spec.PaintWidthHalfNear = 1.6f;
            spec.PaintWidthHalfFar = 1.5f;
            spec.PaintRadiusShock = 1.3f;
            spec.DripSpacing = 10f;
            spec.DripStartDistance = 1f;
            spec.DripRadius = 1f;
            spec.DripMaxCount = 3;
            spec.NoSpecialCharge = true;
            spec.VisualScale = 1.1f;
            spec.Silent = false;
            return spec;
        }

        // 砲彈:直線 8F → 自由落體(0.12 / 0.05);直擊 50 貫穿角色;碰到地形/物件爆炸 30(≤ 4.8)、塗 4.5、擊退 470/5
        public InkBulletSpec BuildCannonSpec(string explodeSound)
        {
            return new InkBulletSpec
            {
                Motion = CrabTankModel.CannonMotion,
                LifetimeFrames = 300,
                RadiusForPlayer = 0.04f,
                RadiusForPlayerEnd = 0.4f,
                RadiusForPlayerChangeFrames = 5,
                RadiusForField = 0.1f,
                RadiusForFieldEnd = 0.25f,
                RadiusForFieldChangeFrames = 4,
                PiercePlayers = true,
                DamageMax = _cannonDirect,
                DamageMin = _cannonDirect,
                ReduceStartFrame = -1,
                AttackClass = AttackClass.CrabTankCannon,
                Kind = DamageKind.Special,
                // 落點由爆炸塗(4.5);飛行途中每 3.1 m 滴一滴(SplashParam SpawnBetweenLength 3.1、SpawnNum 10)
                PaintWidthHalfNear = 0f,
                PaintWidthHalfFar = 0f,
                PaintRadiusShock = 0f,
                NoSpecialCharge = true,
                ExplodeOnImpact = true,
                Explosion = new ExplosionSpec
                {
                    NearDamage = _cannonBlast.x,
                    NearRadius = _cannonBlast.y,
                    FarDamage = _cannonBlast.x,
                    FarRadius = _cannonBlast.y,
                    AttackClass = AttackClass.CrabTankCannon,
                    Kind = DamageKind.Special,
                    PaintRadius = _cannonPaint,
                    PaintRayCount = 8,
                    PaintRayRadius = 1f,
                    PaintRayReach = _cannonBlast.y,
                    DropCount = 10,
                    DropRadius = 1.12f,
                    DropReach = _cannonPaint * 1.4f,
                    PaintHardness = 0.75f,
                    NoSpecialCharge = true,
                    KnockbackAccel = 470f,
                    KnockbackRange = 5f,
                    FxBursts = 5,
                    FlashRadius = _cannonBlast.y * 0.6f,
                    Sound = explodeSound,
                    SoundVolume = 1f,
                },
                DripSpacing = 3.1f,
                DripStartDistance = 3f,
                DripRadius = 1.12f,
                DripMaxCount = 10,
                VisualScale = 2.2f,
                TrailTime = 0.1f,
                Silent = true,
            };
        }
    }
}

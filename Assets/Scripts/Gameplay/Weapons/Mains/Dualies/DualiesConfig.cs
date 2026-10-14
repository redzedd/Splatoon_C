using SplatoonC.Core.Combat;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 雙槍(Dualies)主武器設定:兩把槍輪流開火(S3 三狀態墨彈 InkBullet,數值原樣取自 Leanny v11.3.0 Maneuver_*)、
    // 閃避翻滾(SideStepParam)、翻滾後的砲台模式(LapOver*:射速變快、不散布、判定變大)。
    // 資產由 DualiesSetup 從 S3DualiesParams 產生(Assets/Resources/Weapons/Main_splat_dualies.asset)。
    //
    // 為什麼用 InkBullet 而不是射擊槍的 InkShooter/InkProjectile:射擊槍那條路是以 Splattershot 手感校準的兩段式近似彈道
    //(ShooterCalibration 換算),而雙槍需要的正是 S3 原始三狀態彈道、依飛行幀數衰減的傷害(30→15 於 7→15F)、
    // 可切換的對人判定半徑(0.31 → 砲台 0.335)——InkBulletSpec 可以一比一裝進 MoveParam/DamageParam/CollisionParam/PaintParam,
    // 兩把槍、翻滾、砲台模式則是 InkShooter 沒有的狀態機,所以另寫 DualiesWeapon,彈道共用 I1 的 InkBullet。
    [CreateAssetMenu(fileName = "Main_splat_dualies", menuName = "SplatoonC/雙槍設定")]
    public sealed class DualiesConfig : MainWeaponConfig
    {
        [Header("外觀")]
        [SerializeField, Tooltip("單把槍模 prefab(左右手各生成一把;DualiesGunModel 掛到 CharacterVisual 的左右插槽)")]
        private GameObject _gunModelPrefab;

        [SerializeField, Tooltip("墨彈 prefab;留空用 CombatAssets 的通用墨彈")]
        private InkBullet _bulletPrefab;

        [Header("墨彈(S3 三狀態彈道)")]
        [SerializeField, Tooltip("一般模式墨彈(S3 30/15 衰減 7→15F、2.37 u/F × 3F、對人 0.31)")]
        private InkBulletSpec _bullet;

        [SerializeField, Tooltip("砲台模式墨彈(CollisionLapOverParam:對人 0.335;其餘同一般)")]
        private InkBulletSpec _turretBullet;

        [SerializeField, Tooltip("準星鎖定幀數 ShotGuideFrame(有效射程公式的 G;Splat Dualies 7)")]
        private int _shotGuideFrames = 7;

        [Header("連射")]
        [SerializeField, Tooltip("一般模式射擊間隔(F;S3 RepeatFrame 5 = 每秒 12 發)")]
        private int _repeatFrames = 5;

        [SerializeField, Tooltip("砲台模式射擊間隔(F;S3 LapOver_RepeatFrame 4 = 每秒 15 發)")]
        private int _turretRepeatFrames = 4;

        [SerializeField, Tooltip("人形第一發延遲(F;S3 3F)")]
        private int _firstShotFrames = 3;

        [SerializeField, Tooltip("從烏賊形態開火的第一發延遲(F;9 + 4 − SquidShotShorteningFrame 0 = 13F)")]
        private int _squidFirstShotFrames = 13;

        [SerializeField, Tooltip("開火後多久才能變烏賊(F;S3 PostDelayFrame 4)")]
        private int _postDelayFrames = 4;

        [SerializeField, Tooltip("長掉幀時單幀最大補射數")]
        private int _maxShotsPerFrame = 4;

        [Header("墨量")]
        [SerializeField, Range(0f, 1f), Tooltip("每發耗墨(標準墨槽的分數;S3 0.72% → 138 發)")]
        private float _inkConsume = 0.0072f;

        [SerializeField, Tooltip("每發的墨量鎖(F;S3 InkRecoverStop 預設 20F)")]
        private int _inkRecoverStopFrames = 20;

        [Header("散布(一般模式;砲台模式 LapOver_DegSwerve)")]
        [SerializeField, Tooltip("S3 散布模型:地面 2.0° / 空中 7.5°、b 0.01 +0.01/發 上限 0.25、回復 0.005/F、跳射 b 0.4")]
        private SpreadSettings _spread;

        [SerializeField, Tooltip("砲台模式最大偏角(度;S3 Splat Dualies 0 = 完全不散)")]
        private float _turretSwerveDeg;

        [Header("墨滴(SplashSpawnParam)")]
        [SerializeField, Tooltip("墨滴樣式數 SplitNum(S3 7):每發的墨滴落點相位輪替,連續 7 發把沿路填滿")]
        private int _dropletPatterns = 7;

        [Header("玩家速度疊加到墨彈(spl__SpawnBulletAdditionMovePlayerParam)")]
        [SerializeField, Tooltip("前向分量倍率(S3 ZRate 2.0)")]
        private float _inheritForwardRate = 2f;

        [SerializeField, Tooltip("橫向分量倍率(S3 XRate 預設 0.4)")]
        private float _inheritSideRate = 0.4f;

        [Header("閃避翻滾(SideStepParam)")]
        [SerializeField, Tooltip("翻滾時間軸與數值(S3:2 滾、7%/滾、4.0 + 1.0 u、4F 起手 → 12F → 硬直 32F、塗 1.8、墨量鎖 70F)")]
        private DodgeRollParams _roll = DodgeRollParams.SplatDualies;

        [Header("槍口(相對 Visual,公尺;高度與前距沿用角色 Muzzle)")]
        [SerializeField, Tooltip("一般模式兩把槍口的左右間距(半距;兩把槍各朝自己的準星平行射出)")]
        private float _muzzleHalfSpacing = 0.26f;

        [SerializeField, Tooltip("砲台模式兩把槍口的左右半距(雙槍併攏,兩發都朝同一個準星)")]
        private float _turretMuzzleHalfSpacing = 0.08f;

        [SerializeField, Tooltip("砲台模式槍口下移(公尺;蹲低架槍)")]
        private float _turretMuzzleDrop = 0.12f;

        [Header("音效")]
        [SerializeField, Tooltip("翻滾音效 id(未合成前退回 squid_dive)")]
        private string _rollSound = "dualies_roll";

        [Header("bot")]
        [SerializeField, Tooltip("bot 交戰中主動翻滾的間隔(秒,隨機範圍)[EST]")]
        private Vector2 _botRollInterval = new Vector2(2.5f, 5f);

        [SerializeField, Tooltip("bot 被打後幾秒內會翻滾閃躲 [EST]")]
        private float _botRollWhenHurtWindow = 0.4f;

        public GameObject GunModelPrefab => _gunModelPrefab;
        public InkBullet BulletPrefab => _bulletPrefab;
        public InkBulletSpec Bullet => _bullet;
        public InkBulletSpec TurretBullet => _turretBullet;
        public int ShotGuideFrames => _shotGuideFrames;
        public int RepeatFrames => _repeatFrames;
        public int TurretRepeatFrames => _turretRepeatFrames;
        public int FirstShotFrames => _firstShotFrames;
        public int SquidFirstShotFrames => _squidFirstShotFrames;
        public int PostDelayFrames => _postDelayFrames;
        public int MaxShotsPerFrame => _maxShotsPerFrame;
        public float InkConsume => _inkConsume;
        public int InkRecoverStopFrames => _inkRecoverStopFrames;
        public SpreadSettings Spread => _spread;
        public float TurretSwerveDeg => _turretSwerveDeg;
        public int DropletPatterns => _dropletPatterns;
        public float InheritForwardRate => _inheritForwardRate;
        public float InheritSideRate => _inheritSideRate;
        public DodgeRollParams Roll => _roll;
        public float MuzzleHalfSpacing => _muzzleHalfSpacing;
        public float TurretMuzzleHalfSpacing => _turretMuzzleHalfSpacing;
        public float TurretMuzzleDrop => _turretMuzzleDrop;
        public string RollSound => _rollSound;
        public Vector2 BotRollInterval => _botRollInterval;
        public float BotRollWhenHurtWindow => _botRollWhenHurtWindow;

        public float FireIntervalSeconds => _repeatFrames / 60f;
        public float TurretFireIntervalSeconds => _turretRepeatFrames / 60f;
        public float InkLockSeconds => _inkRecoverStopFrames / 60f;

        public override WeaponClass Class => WeaponClass.Dualies;
        public override System.Type BehaviourType => typeof(DualiesWeapon);

        // S3 有效射程公式(Template:Effective_range):10.89 m
        public override float EffectiveRange =>
            BulletBallistics.EffectiveRange(_bullet.Motion, _shotGuideFrames, _bullet.RadiusForPlayer);

        // bot 提前量用的平均彈速:有效射程 ÷ 準星鎖定幀(彈在直線段 142 m/s,之後急速減速,平均約 93 m/s)
        public override float ProjectileSpeed => EffectiveRange / Mathf.Max(1f / 60f, _shotGuideFrames / 60f);

        // 編輯器產生器用(DualiesSetup):一次寫入 S3 換算後的全部欄位
        public void Setup(GameObject gunModel, InkBullet bulletPrefab, in S3DualiesParams s3, in InkBulletSpec bullet,
            in InkBulletSpec turretBullet, string fireSound, string rollSound)
        {
            _gunModelPrefab = gunModel;
            _bulletPrefab = bulletPrefab;
            _bullet = bullet;
            _turretBullet = turretBullet;
            _shotGuideFrames = s3.ShotGuideFrame;
            _repeatFrames = s3.RepeatFrame;
            _turretRepeatFrames = s3.TurretRepeatFrame;
            _firstShotFrames = s3.FirstShotFrames;
            _squidFirstShotFrames = s3.SquidFirstShotFrames;
            _postDelayFrames = s3.PostDelayFrames;
            _inkConsume = s3.InkConsume;
            _inkRecoverStopFrames = s3.InkRecoverStop;
            _spread = s3.ToSpreadSettings();
            _turretSwerveDeg = s3.TurretSwerve;
            _dropletPatterns = s3.SplitNum;
            _inheritForwardRate = s3.InheritForwardRate;
            _inheritSideRate = s3.InheritSideRate;
            _roll = s3.Roll;
            _moveSpeedWhileFiring = s3.MoveSpeedWhileFiring;
            _fireSound = fireSound;
            _rollSound = rollSound;
        }
    }
}

using SplatoonC.Core.Combat;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 爆破槍(Blaster)主武器設定:慢速大墨彈(S3 三狀態墨彈 InkBullet,BlasterFamily)——直擊 125 一擊必殺,
    // 飛滿 BurstFrame 幀在空中爆炸(70 → 50 線性爆風),途中撞到地形/目標提早爆(撞地形爆風半徑 × ShotCollisionHitRadiusRate)。
    // 數值原樣取自 Leanny v11.3.0 Blaster_Middle / Blaster_Short(S3BlasterParams);資產由 BlasterSetup 產生
    //(Assets/Resources/Weapons/Main_blaster.asset、Main_luna_blaster.asset)。
    // 執行期行為在 BlasterWeapon:14F 預備(烏賊態出來 24F)→ 出膛 → RepeatFrame 冷卻、PostDelay 不能潛/丟副武器;
    // 爆炸塗墨(空爆 / 撞地兩組半徑)、腳前墨滴由 BlasterWeapon 依墨彈結束方式補上(InkBullet 的爆炸只算傷害/擊退)。
    [CreateAssetMenu(fileName = "Main_blaster", menuName = "SplatoonC/爆破槍設定")]
    public sealed class BlasterConfig : MainWeaponConfig
    {
        [Header("外觀")]
        [SerializeField, Tooltip("槍模 prefab(掛到 CharacterVisual.WeaponSocket;名稱 Ink 開頭的零件套隊伍色)")]
        private GameObject _gunModelPrefab;

        [SerializeField, Tooltip("墨彈 prefab(爆破槍的大墨團);留空用 CombatAssets 的通用墨彈")]
        private InkBullet _bulletPrefab;

        [Header("墨彈(S3 三狀態彈道 + 爆炸)")]
        [SerializeField, Tooltip("墨彈規格:直擊 125、BurstFrame 空爆、線性爆風、撞地爆風倍率(S3 BlasterFamily)")]
        private InkBulletSpec _bullet;

        [SerializeField, Tooltip("準星鎖定幀數 ShotGuideFrame(有效射程公式的 G;Blaster 13、Luna 11)")]
        private int _shotGuideFrames = 13;

        [Header("時間軸(F,60 Hz)")]
        [SerializeField, Tooltip("人形第一發延遲(S3 14F = 4 + PreDelayFrame_HumanShot 10)")]
        private int _firstShotFrames = 14;

        [SerializeField, Tooltip("從烏賊形態開火的第一發延遲(從解除烏賊起算;S3 24F = 9 + PreDelayFrame_SquidShot 15)")]
        private int _squidFirstShotFrames = 24;

        [SerializeField, Tooltip("射擊間隔 RepeatFrame(Blaster 50F = 1.2 發/秒、Luna 40F = 1.5 發/秒)")]
        private int _repeatFrames = 50;

        [SerializeField, Tooltip("出膛後不能變烏賊 / 丟副武器 PostDelayFrame_Blaster(Blaster 22F、Luna 26F)")]
        private int _postDelayFrames = 22;

        [Header("墨量")]
        [SerializeField, Range(0f, 1f), Tooltip("每發耗墨(標準墨槽的分數;Blaster 10% → 10 發、Luna 7.5% → 13 發)")]
        private float _inkConsume = 0.1f;

        [SerializeField, Tooltip("每發的墨量鎖 InkRecoverStop(F;Blaster 57F、Luna 55F)")]
        private int _inkRecoverStopFrames = 57;

        [Header("散布(地面 0°;跳射 Jump_DegSwerve、b 0.5)")]
        [SerializeField, Tooltip("S3 散布模型:地面 0°(完全不散)/ 空中 10°(Luna 5.5°),空中偏向 b 固定 0.5,起跳 25F 後收、70F 回到 0°")]
        private SpreadSettings _spread;

        [Header("玩家速度疊加到墨彈(spl__SpawnBulletAdditionMovePlayerParam)")]
        [SerializeField, Tooltip("前向分量倍率(S3 ZRate 2.0)")]
        private float _inheritForwardRate = 2f;

        [SerializeField, Tooltip("橫向分量倍率(S3 XRate 預設 0.4)")]
        private float _inheritSideRate = 0.4f;

        [Header("塗墨(BlasterBurstParam / SplashPaintParam;公尺)")]
        [SerializeField, Tooltip("腳前墨滴的塗墨半徑(WidthHalfNearest × √1.2 等面積 [EST];Blaster 2.43 → 2.66)")]
        private float _nearestPaintRadius = 2.66f;

        [SerializeField, Tooltip("腳前墨滴離角色的距離(SpawnNearestLength;Blaster 0.5、Luna 1.0)")]
        private float _nearestDistance = 0.5f;

        [SerializeField, Tooltip("空爆:爆心附近球形塗墨半徑(SplashPaintRadius;Luna 2.4、Blaster [EST] 2.17)")]
        private float _burstPaintRadius = 2.17f;

        [SerializeField, Tooltip("空爆:爆心正下方地面塗墨半徑(SplashDropPaintRadius;Luna 3.84、Blaster [EST] 3.48)")]
        private float _burstDropPaintRadius = 3.48f;

        [SerializeField, Tooltip("撞地形:球形塗墨半徑(SplashPaintShotColHitRadius;Luna 1.68、Blaster [EST] 1.52)")]
        private float _terrainPaintRadius = 1.52f;

        [SerializeField, Tooltip("撞地形:下方地面塗墨半徑(SplashDropPaintShotColHitRadius;Blaster 2.5、Luna 2.76)")]
        private float _terrainDropPaintRadius = 2.5f;

        [SerializeField, Tooltip("爆炸噴到牆上的墨(SplashWallDropPaintParam.PaintRadiusShock 1.2):爆心水平四周的牆")]
        private float _wallPaintRadius = 1.2f;

        [SerializeField, Tooltip("空爆的墨往下落到地面的最大落差(公尺)[EST]:朝天空爆太高就塗不到地面")]
        private float _burstDropReach = 12f;

        [Header("音效")]
        [SerializeField, Tooltip("爆炸音效 id(未合成前退回 bomb_explode)")]
        private string _burstSound = "blaster_burst";

        [SerializeField, Tooltip("直擊確認音效 id(本機玩家直擊時的高音;DESIGN_REFERENCE §8.1,未合成前不發聲,照樣有 hit_marker)")]
        private string _directHitSound = "blaster_direct_hit";

        [Header("持槍姿勢(CharacterVisual;槍的座標:右、上、前)")]
        [SerializeField, Tooltip("握把點相對右肩(公尺):大槍抱得比射擊槍低、近")]
        private Vector3 _gripOffset = new Vector3(0.075f, -0.1f, 0.23f);

        [SerializeField, Tooltip("左手扶槍點相對握把(公尺):前握把在槍身前段下方")]
        private Vector3 _supportOffset = new Vector3(-0.04f, -0.03f, 0.15f);

        [SerializeField, Tooltip("每發後座力倍率(1 = 射擊槍一發)")]
        private float _recoilScale = 2.2f;

        [Header("bot")]
        [SerializeField, Tooltip("交戰時想維持的距離 = 空爆距離 × 此值(稍近於空爆點:直擊不中,彈飛過目標後在身後空爆仍炸得到)")]
        private float _botPreferredBurstRatio = 0.85f;

        [SerializeField, Tooltip("塗地時瞄準的地面距離 = 空爆距離 × 此值(瞄得比空爆點遠 → 彈在貼近地面處空爆,地面塗墨半徑最大;" +
                                 "BotBrain 掃射會把距離 ±30%,1.5 倍讓掃到最近時仍多半是空爆)")]
        private float _botPaintBurstRatio = 1.5f;

        public GameObject GunModelPrefab => _gunModelPrefab;
        public InkBullet BulletPrefab => _bulletPrefab;
        public InkBulletSpec Bullet => _bullet;
        public int ShotGuideFrames => _shotGuideFrames;
        public int BurstFrame => _bullet.ExplodeAtFrame;
        public int FirstShotFrames => _firstShotFrames;
        public int SquidFirstShotFrames => _squidFirstShotFrames;
        public int RepeatFrames => _repeatFrames;
        public int PostDelayFrames => _postDelayFrames;
        public float InkConsume => _inkConsume;
        public int InkRecoverStopFrames => _inkRecoverStopFrames;
        public SpreadSettings Spread => _spread;
        public float InheritForwardRate => _inheritForwardRate;
        public float InheritSideRate => _inheritSideRate;
        public float NearestPaintRadius => _nearestPaintRadius;
        public float NearestDistance => _nearestDistance;
        public float BurstPaintRadius => _burstPaintRadius;
        public float BurstDropPaintRadius => _burstDropPaintRadius;
        public float TerrainPaintRadius => _terrainPaintRadius;
        public float TerrainDropPaintRadius => _terrainDropPaintRadius;
        public float WallPaintRadius => _wallPaintRadius;
        public float BurstDropReach => _burstDropReach;
        public string BurstSound => _burstSound;
        public string DirectHitSound => _directHitSound;
        public Vector3 GripOffset => _gripOffset;
        public Vector3 SupportOffset => _supportOffset;
        public float RecoilScale => _recoilScale;
        public float BotPreferredBurstRatio => _botPreferredBurstRatio;
        public float BotPaintBurstRatio => _botPaintBurstRatio;

        public float DirectDamage => _bullet.DamageMax;
        public ExplosionSpec Explosion => _bullet.Explosion;
        public float FireIntervalSeconds => _repeatFrames / 60f;
        public float InkLockSeconds => _inkRecoverStopFrames / 60f;

        public BlasterTiming Timing =>
            BlasterTiming.FromFrames(_firstShotFrames, _squidFirstShotFrames, _repeatFrames, _postDelayFrames);

        public override WeaponClass Class => WeaponClass.Blaster;
        public override System.Type BehaviourType => typeof(BlasterWeapon);

        // S3 有效射程公式(Template:Effective_range):Blaster 10.14、Luna 7.65
        public override float EffectiveRange =>
            BulletBallistics.EffectiveRange(_bullet.Motion, _shotGuideFrames, _bullet.RadiusForPlayer);

        // 空爆距離(飛滿 BurstFrame 幀的沿彈道距離):Blaster 9.86、Luna 7.35
        public float BurstDistance => BulletBallistics.EffectiveRange(_bullet.Motion, Mathf.Max(1, BurstFrame), 0f);

        // bot 提前量用的平均彈速:空爆距離 ÷ 空爆幀(Blaster 9.86 m / 13F ≈ 45 m/s)
        public override float ProjectileSpeed => BurstDistance / Mathf.Max(1f / 60f, BurstFrame / 60f);

        // 編輯器產生器用(BlasterSetup):一次寫入 S3 換算後的全部欄位
        public void Setup(GameObject gunModel, InkBullet bulletPrefab, in S3BlasterParams s3, in InkBulletSpec bullet,
            AttackClass attackClass, string fireSound, string burstSound, string directHitSound)
        {
            _gunModelPrefab = gunModel;
            _bulletPrefab = bulletPrefab;
            _bullet = bullet;
            _shotGuideFrames = s3.ShotGuideFrame;
            _firstShotFrames = s3.FirstShotFrames;
            _squidFirstShotFrames = s3.SquidFirstShotFrames;
            _repeatFrames = s3.RepeatFrame;
            _postDelayFrames = s3.PostDelayFrames;
            _inkConsume = s3.InkConsume;
            _inkRecoverStopFrames = s3.InkRecoverStop;
            _spread = s3.ToSpreadSettings();
            _inheritForwardRate = s3.InheritForwardRate;
            _inheritSideRate = s3.InheritSideRate;
            _nearestPaintRadius = s3.NearestPaintRadius;
            _nearestDistance = s3.SpawnNearestLength;
            _burstPaintRadius = s3.BurstPaintRadius;
            _burstDropPaintRadius = s3.BurstDropPaintRadius;
            _terrainPaintRadius = s3.TerrainPaintRadius;
            _terrainDropPaintRadius = s3.TerrainDropPaintRadius;
            _wallPaintRadius = s3.BurstWallPaintRadius;
            _moveSpeedWhileFiring = s3.MoveSpeedWhileFiring;
            _overrideAttackClass = attackClass != DefaultAttackClass(WeaponClass.Blaster);
            _attackClass = attackClass;
            _fireSound = fireSound;
            _burstSound = burstSound;
            _directHitSound = directHitSound;
        }
    }
}

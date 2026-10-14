using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 射擊槍(Shooter)主武器設定;其他種類各有自己的 MainWeaponConfig 子類。
    // 墨彈:有 S3 墨彈規格(_bullet,M7 Setup 由 S3ShooterParams 產生)時走 InkBullet 的 S3 三狀態彈道——速度、傷害衰減、
    // 塗墨橢圓、沿路墨滴都是 S3 原值;沒有規格(初速 0)時走舊的兩段式 InkProjectile(下面「兩段式彈道」「沿路滴墨」各欄)。
    [CreateAssetMenu(fileName = "WeaponConfig", menuName = "SplatoonC/武器設定")]
    public sealed class WeaponConfig : MainWeaponConfig
    {
        [Header("預製物")]
        [SerializeField, Tooltip("墨彈 prefab(Assets/Prefabs/InkProjectile);執行期換裝時用")]
        private InkProjectile _projectilePrefab;

        [SerializeField, Tooltip("墨滴 prefab(Assets/Prefabs/InkDrip)")]
        private InkDrip _dripPrefab;

        [SerializeField, Tooltip("槍模 prefab(變體的程序化外觀,取代角色內建的 GunBarrel);留空 = 用內建槍身")]
        private GameObject _gunModelPrefab;

        public InkProjectile ProjectilePrefab => _projectilePrefab;
        public InkDrip DripPrefab => _dripPrefab;
        public GameObject GunModelPrefab => _gunModelPrefab;

        public override WeaponClass Class => WeaponClass.Shooter;
        public override System.Type BehaviourType => typeof(InkShooter);
        // 直飛段 + 急墜滑行一小段(S3 Splattershot 有效射程 11.56)
        public override float EffectiveRange => _straightRange + 1.5f;
        public override float ProjectileSpeed => UsesInkBullet ? _bullet.Motion.SpawnSpeed * 60f : _muzzleSpeed;
        public override float InkTankScale => _inkTankScale;

        [Header("S3 墨彈(InkBullet)")]
        [SerializeField, Tooltip("S3 三狀態彈道的墨彈規格(M7 Setup 由 S3ShooterParams 產生);初速 ≤ 0 = 用舊的兩段式 InkProjectile")]
        private InkBulletSpec _bullet;

        [SerializeField, Tooltip("墨滴落點的相位樣式數(S3 SplashSpawnParam.SplitNum):連續 N 發把沿路墨滴錯開填滿")]
        private int _dropletSplitNum = 8;

        [SerializeField, Tooltip("玩家移動速度疊加到墨彈的倍率(S3 SpawnBulletAdditionMovePlayerParam:前向 ZRate 2.0、橫向 XRate 0.4)")]
        private float _inheritForwardRate = 2f;

        [SerializeField]
        private float _inheritSideRate = 0.4f;

        public InkBulletSpec Bullet => _bullet;
        public bool UsesInkBullet => _bullet.Motion.SpawnSpeed > 0f;
        public int DropletSplitNum => _dropletSplitNum;
        public float InheritForwardRate => _inheritForwardRate;
        public float InheritSideRate => _inheritSideRate;

        // 編輯器產生器用(ShooterVariantsSetup)
        public void SetBullet(in InkBulletSpec bullet, int dropletSplitNum)
        {
            _bullet = bullet;
            _dropletSplitNum = dropletSplitNum;
        }

        [Header("連射")]
        [SerializeField, Tooltip("連射間隔(秒);0.0769 = 每秒 13 發")]
        private float _fireInterval = 0.0769231f;

        [SerializeField, Tooltip("長掉幀時單幀最大補射數,其餘欠帳丟棄")]
        private int _maxShotsPerFrame = 4;

        [Header("彈道")]
        [SerializeField, Tooltip("墨彈初速(公尺/秒)")]
        private float _muzzleSpeed = 48.75f;

        [SerializeField, Tooltip("墨彈重力(負值;比世界重力輕,飛出墨水拋物線)")]
        private float _projectileGravity = -10f;

        [SerializeField, Tooltip("墨彈存活秒數,超時自動回收")]
        private float _projectileLifetime = 3f;


        // 每發都上墨量鎖(InkLockSeconds 比射擊間隔長),連射期間完全不回墨,故連射秒數 = 1 ÷ (射速 × 單發消耗)。
        // S3 Splattershot:10 發/秒 × 0.0092 = 0.092/s → 滿槽 108 發、連射 10.8 秒。
        [SerializeField, Range(0f, 1f), Tooltip("每發墨量消耗(0~1 正規化);與射速共同決定連射秒數")]
        private float _inkCostPerShot = 0.0076923f;

        [SerializeField, Tooltip("每發的墨量鎖(秒;S3 Splattershot 20F = 0.333):鎖期間不回墨,墨不足也會上鎖")]
        private float _inkLockSeconds = 20f / 60f;

        [SerializeField, Tooltip("墨槽容量倍率(S3:標準 1;Splattershot Jr. 1.1)。每發耗墨以標準墨槽為單位")]
        private float _inkTankScale = 1f;


        [SerializeField, Tooltip("散布模型(S3):最大偏角(地面/空中)、偏向參數 b 的起始/每發增量/上限/回復速率、跳射 b")]
        private SplatoonC.Core.Combat.SpreadSettings _spread = SplatoonC.Core.Combat.SpreadSettings.Splattershot;

        [Header("兩段式彈道(Splatoon 式:直飛到射程極限後急墜)")]
        [SerializeField, Tooltip("直飛射程(公尺):在此距離內近乎直線,維持準心高度")]
        private float _straightRange = 10f;

        [SerializeField, Tooltip("直飛段重力(負值,很小):只做微微下墜")]
        private float _straightGravity = -1.5f;

        [SerializeField, Tooltip("超過射程後的墜落重力(負值,很大):高速落地")]
        private float _dropGravity = -38f;

        [SerializeField, Tooltip("墜落階段的水平阻力(每秒指數衰減);夠大才會近乎垂直落下,仰射才不會比平射遠")]
        private float _dropHorizontalDrag = 7f;

        [Header("地面痕跡來源:沿路滴墨(確定性,不是機率)")]
        [SerializeField, Tooltip("每發固定滴幾滴;所需滴數 ≥ 射程 ÷ (射速 × 目標成路秒數 × 滴墨直徑)")]
        private int _dripsPerShot = 2;

        [SerializeField, Tooltip("相位循環發數;連續 N 發把滴落點錯開成 N 組,後面的發次填前面的縫")]
        private int _dripPhaseCycle = 4;

        [SerializeField, Tooltip("第一滴最早出現的飛行距離(公尺)")]
        private float _dripStartDistance = 0.8f;

        [SerializeField, Tooltip("滴落窗口 = 直飛射程 × 此倍率;要涵蓋墜落段才鋪得到落點附近")]
        private float _dripRangeMultiplier = 1.5f;

        [SerializeField, Tooltip("滴落距離的隨機抖動(公尺,±);太大會在路上開洞,上限約等於間距減去滴墨直徑")]
        private float _dripJitterDistance = 0.25f;

        [SerializeField, Tooltip("墨滴落地的塗色半徑(公尺)")]
        private float _dripRadius = 1.2f;

        [SerializeField, Tooltip("墨滴繼承墨彈速度的比例;越小越接近原地垂直落下")]
        private float _dripInheritSpeed = 0.25f;

        [SerializeField, Tooltip("墨滴下墜重力(負值)")]
        private float _dripGravity = -25f;

        [SerializeField, Tooltip("墨滴的隨機橫向擾動(公尺/秒),讓墨路不是一直線")]
        private float _dripSideSpeed = 0.8f;

        [SerializeField, Tooltip("墨滴存活秒數,超時回收")]
        private float _dripLifetime = 3f;

        [SerializeField, Tooltip("槍口噴濺半徑(公尺);每次射擊必定在腳前濺一點")]
        private float _muzzleSplashRadius = 1.18f;

        [SerializeField, Tooltip("槍口噴濺落點的最近距離(公尺,沿瞄準水平方向)")]
        private float _muzzleSplashDistance = 1.3f;

        [SerializeField, Tooltip("槍口噴濺落點的最遠距離;逐發在近↔遠之間輪替,否則每發都疊在同一點")]
        private float _muzzleSplashDistanceFar = 4f;

        [SerializeField, Tooltip("墨彈命中偵測圖層(排除 Player)")]
        private LayerMask _hitMask = ~0;

        // 墨色不在武器上:由射手的隊伍決定(TeamPalette)
        [Header("塗色")]
        [SerializeField, Tooltip("主 splat 半徑(公尺)")]
        private float _splatRadius = 1.82f;

        [SerializeField, Range(0f, 1f), Tooltip("主 splat 筆刷硬度")]
        private float _splatHardness = 0.7f;

        [SerializeField, Tooltip("噴濺小點數量(命中時繞主點灑出)")]
        private int _splashCount = 2;

        [SerializeField, Tooltip("噴濺小點半徑(公尺)")]
        private float _splashRadius = 0.84f;

        [SerializeField, Tooltip("噴濺散布距離(公尺,自主點外擴)")]
        private float _splashSpread = 0.6f;

        [Header("傷害")]
        [SerializeField, Tooltip("直飛段每發傷害")]
        private float _damage = 30f;

        [SerializeField, Tooltip("衰減下限:急墜段衰到此值後不再降")]
        private float _minDamage = 0.1f;

        [SerializeField, Tooltip("進入急墜段後再飛幾公尺就衰到下限(線性;墜落段滑行約 7m,落地前必達下限)")]
        private float _damageDecayDistance = 3f;

        [SerializeField, Tooltip("全傷的最遠飛行距離(公尺;S3 有效射程 − 對人判定半徑)。≤ 0 = 直飛射程(舊行為)")]
        private float _damageFalloffStart;

        public float FireInterval => _fireInterval;
        public int MaxShotsPerFrame => _maxShotsPerFrame;
        public float MuzzleSpeed => _muzzleSpeed;
        public float ProjectileGravity => _projectileGravity;
        public float ProjectileLifetime => _projectileLifetime;
        public float InkCostPerShot => _inkCostPerShot;
        public float InkLockSeconds => _inkLockSeconds;
        public SplatoonC.Core.Combat.SpreadSettings Spread => _spread;
        public float StraightRange => _straightRange;
        public float StraightGravity => _straightGravity;
        public float DropGravity => _dropGravity;
        public float DropHorizontalDrag => _dropHorizontalDrag;
        public int DripsPerShot => _dripsPerShot;
        public int DripPhaseCycle => _dripPhaseCycle;
        public float DripStartDistance => _dripStartDistance;
        public float DripRangeMultiplier => _dripRangeMultiplier;
        public float DripJitterDistance => _dripJitterDistance;
        public float DripRadius => _dripRadius;
        public float DripInheritSpeed => _dripInheritSpeed;
        public float DripGravity => _dripGravity;
        public float DripSideSpeed => _dripSideSpeed;
        public float DripLifetime => _dripLifetime;
        public float MuzzleSplashRadius => _muzzleSplashRadius;
        public float MuzzleSplashDistance => _muzzleSplashDistance;
        public float MuzzleSplashDistanceFar => _muzzleSplashDistanceFar;
        public LayerMask HitMask => _hitMask;
        public float SplatRadius => _splatRadius;
        public float SplatHardness => _splatHardness;
        public int SplashCount => _splashCount;
        public float SplashRadius => _splashRadius;
        public float SplashSpread => _splashSpread;
        public float Damage => _damage;
        public float MinDamage => _minDamage;
        public float DamageDecayDistance => _damageDecayDistance;
        // 傷害從這個飛行距離開始衰減(S3 以幀數計:ReduceStartFrame = ShotGuideFrame,即有效射程處)
        public float DamageFalloffStart => _damageFalloffStart > 0f ? _damageFalloffStart : _straightRange;
    }
}

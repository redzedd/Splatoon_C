using SplatoonC.Core.Combat;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 大招「Trizooka」(S3 SpUltraShot,research/04 §2.1、DESIGN_REFERENCE §5.2;v11.3.0 0 AP):
    // 扛起三管火箭筒,5.5 秒(330F)內最多射 3 發;每發 3 顆墨團每 4F 一顆、繞同一軸螺旋。
    // 直擊 220(貫穿角色、不在角色身上爆)、打到地形爆炸 53(≤ 2.5)/ 35(≤ 4.0)並塗 3.2;同一發對同一目標只算一次
    //(先吃爆風後被直擊 → 補到 220)。持續型:能量就是計時器,3 發射完或時間到即結束。
    [CreateAssetMenu(fileName = "Special_trizooka", menuName = "SplatoonC/大招/Trizooka")]
    public sealed class TrizookaConfig : SpecialWeaponConfig
    {
        [Header("預製物")]
        [SerializeField, Tooltip("墨團 prefab(InkBullet;Assets/Prefabs/Weapons/TrizookaGlob)")]
        private InkBullet _globPrefab;

        [SerializeField, Tooltip("手上的發射器模型(掛在 CharacterVisual.WeaponSocket;名稱 Ink* 的部位上隊伍色)")]
        private GameObject _launcherPrefab;

        [Header("持續與節奏(幀,60 Hz)")]
        [SerializeField, Tooltip("效果時間(SpecialDurationFrame 330F = 5.5 s;SPU 480F)")]
        private int _durationFrames = TrizookaModel.DurationFrames;

        [SerializeField, Tooltip("發動後多久才能開始射(JP wiki スペシャル発動隙 23F,含 StartDelayFrame 5)")]
        private int _startDelayFrames = TrizookaModel.StartDelayFrames;

        [SerializeField, Tooltip("按下到出膛(ShotDelayFrame 15F);發動後最快 23 + 15 = 38F 出第一發")]
        private int _shotDelayFrames = TrizookaModel.ShotDelayFrames;

        [SerializeField, Tooltip("出膛後的硬直(JP wiki 後隙 40F);按住連射間隔 = 15 + 40 = 55F(RepeatFrame)")]
        private int _postShotFrames = TrizookaModel.PostShotFrames;

        [SerializeField, Tooltip("每次大招的發數")]
        private int _shots = TrizookaModel.Shots;

        [Header("墨團(BulletSpUltraShot)")]
        [SerializeField, Tooltip("每發幾顆")]
        private int _globsPerShot = TrizookaModel.GlobsPerShot;

        [SerializeField, Tooltip("墨團之間隔幾幀(4F)")]
        private int _globIntervalFrames = TrizookaModel.GlobIntervalFrames;

        [SerializeField, Tooltip("直擊傷害(DirectHitDamage 2200 → 220;貫穿角色)")]
        private float _directDamage = 220f;

        [SerializeField, Tooltip("對人判定半徑:初始 / 終值(0.01 → 0.75,10F 內長滿)")]
        private Vector2 _playerRadius = new Vector2(0.01f, 0.75f);

        [SerializeField, Tooltip("對人判定半徑成長幀數(ChangeFrameForPlayer 10)")]
        private int _playerRadiusFrames = 10;

        [SerializeField, Tooltip("對地形判定半徑:初始 / 終值(0.01 → 0.3,20F 內長滿)")]
        private Vector2 _fieldRadius = new Vector2(0.01f, 0.3f);

        [SerializeField, Tooltip("對地形判定半徑成長幀數(ChangeFrameForField 20)")]
        private int _fieldRadiusFrames = 20;

        [SerializeField, Tooltip("螺旋半徑(OrbitalRadiusEnd 1.0)")]
        private float _orbitRadius = TrizookaModel.OrbitRadius;

        [SerializeField, Tooltip("螺旋半徑長滿的幀數(OrbitalRadiusTransitionFrame 10)")]
        private int _orbitGrowFrames = TrizookaModel.OrbitGrowFrames;

        [SerializeField, Tooltip("螺旋角速度(度/幀;15 [推導]:同時刻三顆差 120°、同距離排成一直線)")]
        private float _orbitDegPerFrame = TrizookaModel.OrbitDegPerFrame;

        [SerializeField, Tooltip("墨團外觀縮放(通用墨彈 0.34 m 的倍率)")]
        private float _globVisualScale = 2.6f;

        [Header("地形爆炸(BlastParam)")]
        [SerializeField, Tooltip("近爆風傷害 / 半徑(53 / 2.5 u)")]
        private Vector2 _nearBlast = new Vector2(53f, 2.5f);

        [SerializeField, Tooltip("遠爆風傷害 / 半徑(35 / 4.0 u)")]
        private Vector2 _farBlast = new Vector2(35f, 4f);

        [SerializeField, Tooltip("爆炸塗墨半徑(PaintRadius 3.2;SPU 4.0)")]
        private float _paintRadius = 3.2f;

        [SerializeField, Tooltip("擊退(KnockBackParam Accel 470 / Distance 8)")]
        private Vector2 _knockback = new Vector2(470f, 8f);

        [Header("移動(u/F;JP wiki 非射撃時 0.70 / 射撃時 0.40 DU/F)")]
        [SerializeField, Tooltip("扛著時人形走速(MoveSpeed 0.07 u/F = 4.2 m/s;跑速能力無效)")]
        private float _moveSpeed = TrizookaModel.MoveSpeed;

        [SerializeField, Tooltip("射擊前後(前隙 15F + 後隙 40F)走速(MoveSpeedInCharge 0.04 u/F = 2.4 m/s)")]
        private float _moveSpeedInShot = TrizookaModel.MoveSpeedInShot;

        [SerializeField, Tooltip("游速倍率(JP wiki イカ速約 1.82 / 一般 1.92 DU/F)")]
        private float _swimSpeedRatio = TrizookaModel.SwimSpeedRatio;

        [Header("瞄準")]
        [SerializeField, Tooltip("歸零距離(公尺;直擊射程 5.8 條試射線 = 29 u,中距離彈道高於準心)")]
        private float _zeroDistance = TrizookaModel.ZeroDistance;

        [SerializeField, Tooltip("發射點相對角色腳底(右、上、前;公尺):扛在右肩的管口")]
        private Vector3 _muzzleOffset = new Vector3(0.32f, 1.5f, 0.75f);

        [Header("音效")]
        [SerializeField, Tooltip("扛起發射器(S3 v2.0.0:對敵人較大聲)")]
        private string _activateSound = "trizooka_activate";

        [SerializeField, Tooltip("出膛")]
        private string _fireSound = "trizooka_fire";

        [SerializeField, Tooltip("地形爆炸(未合成時退回 bomb_explode)")]
        private string _explodeSound = "trizooka_explode";

        public override System.Type BehaviourType => typeof(TrizookaSpecial);
        public override float ActiveDuration => _durationFrames / 60f;

        public InkBullet GlobPrefab => _globPrefab;
        public GameObject LauncherPrefab => _launcherPrefab;
        public int DurationFrames => _durationFrames;
        public int StartDelayFrames => _startDelayFrames;
        public int ShotDelayFrames => _shotDelayFrames;
        public int PostShotFrames => _postShotFrames;
        public int Shots => _shots;
        public int GlobsPerShot => _globsPerShot;
        public int GlobIntervalFrames => _globIntervalFrames;
        public float DirectDamage => _directDamage;
        public float NearDamage => _nearBlast.x;
        public float NearRadius => _nearBlast.y;
        public float FarDamage => _farBlast.x;
        public float FarRadius => _farBlast.y;
        public float PaintRadius => _paintRadius;
        public float MoveSpeed => _moveSpeed;
        public float MoveSpeedInShot => _moveSpeedInShot;
        public float SwimSpeedRatio => _swimSpeedRatio;
        public float ZeroDistance => _zeroDistance;
        public Vector3 MuzzleOffset => _muzzleOffset;
        public string ActivateSound => _activateSound;
        public string FireSound => _fireSound;
        public string ExplodeSound => _explodeSound;

        // 墨團規格(InkBullet):S3 彈道 + 對人/地形判定成長 + 螺旋 + 貫穿直擊 220(同一發累計上限 220)+ 地形爆炸
        public InkBulletSpec BuildGlobSpec(string explodeSound)
        {
            return new InkBulletSpec
            {
                Motion = TrizookaModel.Motion,
                LifetimeFrames = 300,
                RadiusForPlayer = _playerRadius.x,
                RadiusForPlayerEnd = _playerRadius.y,
                RadiusForPlayerChangeFrames = _playerRadiusFrames,
                RadiusForField = _fieldRadius.x,
                RadiusForFieldEnd = _fieldRadius.y,
                RadiusForFieldChangeFrames = _fieldRadiusFrames,
                PiercePlayers = true,
                DamageMax = _directDamage,
                DamageMin = _directDamage,
                ReduceStartFrame = -1,
                AttackClass = AttackClass.Trizooka,
                Kind = DamageKind.Special,
                // 直擊可以「補」先吃到的爆風到 220;爆風以 cap 0 登記 → 同一發只吃一次爆風、被直擊過就不再吃爆風
                PerAttackDamageCap = _directDamage,
                // 墨團本身不塗(落點的 3.2 由爆炸塗);大招自己的墨不充大招
                NoPaint = true,
                NoSpecialCharge = true,
                ExplodeOnImpact = true,
                Explosion = new ExplosionSpec
                {
                    NearDamage = _nearBlast.x,
                    NearRadius = _nearBlast.y,
                    FarDamage = _farBlast.x,
                    FarRadius = _farBlast.y,
                    AttackClass = AttackClass.Trizooka,
                    Kind = DamageKind.Special,
                    PaintRadius = _paintRadius,
                    PaintRayCount = 8,
                    PaintRayRadius = 1.0f,
                    PaintRayReach = _farBlast.y,
                    DropCount = 6,
                    DropRadius = 0.9f,
                    DropReach = _paintRadius * 1.5f,
                    PaintHardness = 0.75f,
                    NoSpecialCharge = true,
                    KnockbackAccel = _knockback.x,
                    KnockbackRange = _knockback.y,
                    FxBursts = 5,
                    FlashRadius = _farBlast.y * 0.8f,
                    Sound = explodeSound,
                    SoundVolume = 1f,
                },
                OrbitRadius = _orbitRadius,
                OrbitGrowFrames = _orbitGrowFrames,
                OrbitDegPerFrame = _orbitDegPerFrame,
                VisualScale = _globVisualScale,
                TrailTime = 0.12f,
                Silent = true,
            };
        }
    }
}

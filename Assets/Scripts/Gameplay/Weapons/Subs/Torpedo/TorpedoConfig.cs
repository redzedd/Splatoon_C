using SplatoonC.Core.Combat;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // Torpedo(トーピード / Bomb_Torpedo;S3 v11.3.0,research 03 §2.14、DESIGN_REFERENCE §4.3):
    // 65% 墨、88F 墨量鎖(全副武器最長);同時只能有 1 顆(還在場上時「使用不可」,不扣墨);
    // 投擲 1.40 u/F(通用 S3 彈道,穿過敵人);投出時腳下小塗;空中 6F 後搜尋水平 9.0 內最近的敵人 →
    // 停住變身 45F(HP 20,一打就壞:不爆不塗;變身中的 Torpedo 會讓敵方炸彈立刻爆)→ 朝目標當時的位置直線衝刺,
    // 碰到地形/敵人爆炸 60(2.6)/ 35(6.0)、塗 3.5、擊退 230 + 8 顆散彈(仰角 65°、0.42~0.48 u/F,碰地形各 12(2.6)、塗 2.0);
    // 先碰到地形:失去搜尋,落地滾 30F 後爆(60/35、塗 3.5、擊退 700,沒有散彈,打不掉)。
    [CreateAssetMenu(fileName = "Sub_torpedo", menuName = "SplatoonC/副武器/魚雷")]
    public sealed class TorpedoConfig : S3ThrownSubConfig
    {
        [Header("魚雷(S3 參數)")]
        [SerializeField, Tooltip("S3 原始參數(u/F、F)")]
        private TorpedoParams _s3 = TorpedoParams.S3;

        [Header("幾何")]
        [SerializeField, Tooltip("碰撞球半徑(公尺;[EST] S3 GuideRadius 0.575 是導引線粗細,本體取小一點)")]
        private float _radius = 0.2f;

        [SerializeField, Tooltip("變身後受擊碰撞球半徑(公尺;[EST] 子彈打得到的大小)")]
        private float _hullRadius = 0.35f;

        [SerializeField, Tooltip("散彈碰撞半徑(公尺;[EST])")]
        private float _dropletRadius = 0.1f;

        [SerializeField, Tooltip("可滾上的台階高度(公尺;[EST])")]
        private float _stepHeight = 0.25f;

        [Header("爆炸")]
        [SerializeField, Tooltip("衝刺命中的爆炸(S3 BlastParamChase:60(2.6)/ 35(6.0)、塗 3.5、擊退 230 / 12)")]
        private ExplosionSpec _chaseBlast;

        [SerializeField, Tooltip("落地滾動/空中逾時的爆炸(S3 BlastParamThrow:60 / 35、塗 3.5、擊退 700 / 12、沒有散彈)")]
        private ExplosionSpec _throwBlast;

        [SerializeField, Tooltip("散彈落地的小爆炸(S3 SplashBlastParam:12(2.6)、塗 2.0、交叉 1.0、無擊退)")]
        private ExplosionSpec _dropletBlast;

        [Header("塗墨")]
        [SerializeField, Tooltip("投出時投擲者腳下的小塗墨半徑(公尺;[EST] JP「投げた時の足元塗り」)")]
        private float _throwPaintRadius = 0.8f;

        [SerializeField, Tooltip("變身時正下方的塗墨滴數(S3 FindSpawnSplashAroundParam Num 6)")]
        private int _transformDropCount = 6;

        [SerializeField, Tooltip("變身時每滴塗墨半徑(公尺;S3 PaintRadius 0.7)")]
        private float _transformDropRadius = 0.7f;

        [SerializeField, Tooltip("變身時墨滴散開距離(公尺;[EST] 由 S3 初速 0.15~0.23、仰角 30~45° 估)")]
        private float _transformDropReach = 1.2f;

        [Header("音效")]
        [SerializeField, Tooltip("鎖定目標音效 id")]
        private string _lockSound = "torpedo_lock";

        [SerializeField, Tooltip("變身音效 id")]
        private string _transformSound = "torpedo_transform";

        [SerializeField, Tooltip("開始衝刺音效 id")]
        private string _dashSound = "torpedo_dash";

        [SerializeField, Tooltip("落地滾動警告音效 id(S3 WarningSERestFrame 30)")]
        private string _rollWarningSound = "torpedo_roll";

        [SerializeField, Tooltip("被打壞音效 id")]
        private string _destroyedSound = "torpedo_destroyed";

        public TorpedoParams S3 => _s3;
        public float Radius => _radius;
        public float HullRadius => _hullRadius;
        public float DropletRadius => _dropletRadius;
        public float StepHeight => _stepHeight;
        public ExplosionSpec ChaseBlast => _chaseBlast;
        public ExplosionSpec ThrowBlast => _throwBlast;
        public ExplosionSpec DropletBlast => _dropletBlast;
        public float ThrowPaintRadius => _throwPaintRadius;
        public int TransformDropCount => _transformDropCount;
        public float TransformDropRadius => _transformDropRadius;
        public float TransformDropReach => _transformDropReach;
        public string LockSound => _lockSound;
        public string TransformSound => _transformSound;
        public string DashSound => _dashSound;
        public string RollWarningSound => _rollWarningSound;
        public string DestroyedSound => _destroyedSound;

        public override S3ThrowParams ThrowParams => _s3.Throw;
        public override float BodyRadius => _radius;

        // S3:同時 1 顆,還在場上時不能再丟(JP「使用不可と表示される」)
        public override bool BlockWhileMaxActive => true;

        // bot:搜尋半徑 9 內會自己找,丟到目標附近就好
        public override float BotHoldSeconds(float targetDistance)
        {
            return 0.3f;
        }

        // 編輯器設定(ThrowablesSetup)呼叫:全部欄位寫成 S3 0 AP 數值(冪等)
        public void ResetToS3()
        {
            _s3 = TorpedoParams.S3;
            _displayName = "魚雷";
            _maxActive = 1;
            ResetThrowFields(0.65f, 88, "bomb_throw");
            _botUsage = BotSubUsage.ThrowAtEnemy;
            // 投擲約 20 m + 搜尋 9 m:遠一點的敵人也丟
            _botRange = new Vector2(6f, 24f);

            _radius = 0.2f;
            _hullRadius = 0.35f;
            _dropletRadius = 0.1f;
            _stepHeight = 0.25f;
            _throwPaintRadius = 0.8f;
            _transformDropCount = 6;
            _transformDropRadius = 0.7f;
            _transformDropReach = 1.2f;
            _lockSound = "torpedo_lock";
            _transformSound = "torpedo_transform";
            _dashSound = "torpedo_dash";
            _rollWarningSound = "torpedo_roll";
            _destroyedSound = "torpedo_destroyed";
            _chaseBlast = new ExplosionSpec
            {
                NearDamage = 60f,
                NearRadius = 2.6f,
                FarDamage = 35f,
                FarRadius = 6f,
                AttackClass = AttackClass.Torpedo,
                Kind = DamageKind.Sub,
                PaintRadius = 3.5f,
                PaintHardness = 0.7f,
                KnockbackAccel = 230f,
                KnockbackRange = 12f,
                FxBursts = 4,
                Sound = "bomb_explode",
                SoundVolume = 0.85f,
            };
            _throwBlast = new ExplosionSpec
            {
                NearDamage = 60f,
                NearRadius = 2.6f,
                FarDamage = 35f,
                FarRadius = 6f,
                AttackClass = AttackClass.Torpedo,
                Kind = DamageKind.Sub,
                PaintRadius = 3.5f,
                PaintHardness = 0.7f,
                KnockbackAccel = 700f,
                KnockbackRange = 12f,
                FxBursts = 4,
                Sound = "bomb_explode",
                SoundVolume = 0.85f,
            };
            _dropletBlast = new ExplosionSpec
            {
                NearDamage = 12f,
                NearRadius = 2.6f,
                FarDamage = 0f,
                FarRadius = 2.6f,
                AttackClass = AttackClass.TorpedoDrop,
                Kind = DamageKind.Sub,
                PaintRadius = 2f,
                PaintHardness = 0.7f,
                FxBursts = 1,
                FlashRadius = 0.9f,
                Sound = "torpedo_droplet",
                SoundVolume = 0.5f,
            };
        }
    }
}

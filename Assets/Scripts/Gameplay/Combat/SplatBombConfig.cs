using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    [CreateAssetMenu(fileName = "SplatBombConfig", menuName = "SplatoonC/斯普拉炸彈設定")]
    public sealed class SplatBombConfig : SubWeaponConfig
    {
        [Header("引信與傷害")]
        [SerializeField, Tooltip("引信(秒):S3 只在貼地時倒數(60F = 1.0)")]
        private float _fuseSeconds = 2.5f;

        [SerializeField, Tooltip("近距離半徑(公尺):內側全額傷害")]
        private float _nearRadius = 2f;

        [SerializeField, Tooltip("遠距離半徑(公尺):近距離外到此為低額傷害,再遠無傷")]
        private float _farRadius = 4f;

        [SerializeField, Tooltip("近距離傷害")]
        private float _nearDamage = 180f;

        [SerializeField, Tooltip("遠距離傷害")]
        private float _farDamage = 30f;

        [SerializeField, Tooltip("爆心到目標之間被牆擋住就不傷")]
        private bool _requireLineOfSight = true;


        [Header("落地")]
        [SerializeField, Range(0f, 1f), Tooltip("反彈係數(法向速度保留比例)")]
        private float _restitution = 0.35f;

        [SerializeField, Range(0f, 1f), Tooltip("反彈時切向速度保留比例")]
        private float _tangentialFriction = 0.6f;

        [SerializeField, Tooltip("反彈後「垂直」速度低於此值且面朝上就停住(公尺/秒);1 ≈ 落地彈一下就停")]
        private float _restSpeed = 1f;

        [SerializeField, Tooltip("炸彈視覺/碰撞半徑(公尺)")]
        private float _visualRadius = 0.25f;

        [Header("爆炸塗墨與特效")]
        [SerializeField, Tooltip("爆心正下方的大 splat 半徑(公尺)")]
        private float _paintCentreRadius = 2.2f;

        [SerializeField, Tooltip("隨機方向噴墨射線數(牆面也吃得到)")]
        private int _paintRayCount = 12;

        [SerializeField, Tooltip("每條噴墨射線落點的 splat 半徑(公尺)")]
        private float _paintRayRadius = 1f;

        [SerializeField, Tooltip("爆炸噴濺粒子數(用 InkSplashFxPool)")]
        private int _fxBursts = 6;

        [Header("擊退(S3 KnockBackParam)")]
        [SerializeField, Tooltip("擊退強度 Accel(S3 必殺炸彈 700)")]
        private float _knockbackAccel = 700f;

        [SerializeField, Tooltip("擊退半徑 Distance(公尺;S3 必殺炸彈 12)")]
        private float _knockbackRange = 12f;

        public float FuseSeconds => _fuseSeconds;
        public float NearRadius => _nearRadius;
        public float FarRadius => _farRadius;
        public float NearDamage => _nearDamage;
        public float FarDamage => _farDamage;
        public bool RequireLineOfSight => _requireLineOfSight;
        public float Restitution => _restitution;
        public float TangentialFriction => _tangentialFriction;
        public float RestSpeed => _restSpeed;
        public float VisualRadius => _visualRadius;
        public float PaintCentreRadius => _paintCentreRadius;
        public int PaintRayCount => _paintRayCount;
        public float PaintRayRadius => _paintRayRadius;
        public int FxBursts => _fxBursts;
        public float KnockbackAccel => _knockbackAccel;
        public float KnockbackRange => _knockbackRange;

        // 交給共用 Explosion 的參數(數值全部來自本資產,與重構前的 SplatBomb.Explode 相同):
        // 主塗墨 = 爆心下方地面 PaintCentreRadius;噴墨射線 PaintRayCount 條、長 FarRadius、半徑 PaintRayRadius;
        // 前 FxBursts 條射線噴濺粒子;視線判定依 RequireLineOfSight;攻擊類別 Bomb(物件 ×2.0)
        public ExplosionSpec ToExplosionSpec()
        {
            return new ExplosionSpec
            {
                NearDamage = _nearDamage,
                NearRadius = _nearRadius,
                FarDamage = _farDamage,
                FarRadius = _farRadius,
                IgnoreLineOfSight = !_requireLineOfSight,
                AttackClass = SplatoonC.Core.Combat.AttackClass.Bomb,
                Kind = DamageKind.Sub,
                PaintRadius = _paintCentreRadius,
                PaintRayCount = _paintRayCount,
                PaintRayRadius = _paintRayRadius,
                PaintRayReach = _farRadius,
                PaintHardness = _paintHardness,
                KnockbackAccel = _knockbackAccel,
                KnockbackRange = _knockbackRange,
                FxBursts = _fxBursts,
                Sound = "bomb_explode",
            };
        }
    }
}

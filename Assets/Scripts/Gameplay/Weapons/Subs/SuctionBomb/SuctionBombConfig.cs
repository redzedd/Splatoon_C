using SplatoonC.Core.Combat;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // Suction Bomb(キューバンボム / Bomb_Suction;S3 v11.3.0,research 03 §2.2、DESIGN_REFERENCE §4.3):
    // 70% 墨、60F 墨量鎖;黏住任何地形表面(地板、牆、天花板、格柵),穿過角色;黏住後 120F 爆炸,剩 60F 起警告音;
    // 180(4.6)/ 30(8.0),傷害中心離黏住的面 +0.6;只塗黏住的那一面 r 5.0 + 交叉塗墨 2.5 + 15 滴 r 1.116;
    // 擊退 700 / 12。投擲與斯普拉炸彈相同(S3 SpawnSpeedZ 1.12 u/F、SpawnSpeedY 0.24):沿用專案已校準的拋物線。
    [CreateAssetMenu(fileName = "Sub_suction_bomb", menuName = "SplatoonC/副武器/吸盤炸彈")]
    public sealed class SuctionBombConfig : SubWeaponConfig
    {
        [Header("吸盤炸彈")]
        [SerializeField, Tooltip("碰撞球半徑(公尺;飛行 sphere cast 與黏住時離面的距離)")]
        private float _radius = 0.2f;

        [SerializeField, Tooltip("黏住後的引信(F;S3 120)")]
        private int _fuseFrames = 120;

        [SerializeField, Tooltip("剩幾 F 開始警告音(S3 WarningSERestFrame 60)")]
        private int _warningRestFrames = 60;

        [SerializeField, Tooltip("剩幾 F 開始膨脹閃爍(S2 Wait 90 + Warn 30)")]
        private int _swellRestFrames = 30;

        [SerializeField, Tooltip("傷害中心離黏住的面的距離(公尺;S3 DamageOffsetY 0.6)")]
        private float _damageOffset = 0.6f;

        [SerializeField, Tooltip("黏住的那一面塗墨半徑(公尺;S3 PaintRadius 5.0)")]
        private float _surfacePaintRadius = 5f;

        [SerializeField, Tooltip("交叉塗墨半徑(公尺;S3 CrossPaintRadius 2.5):黏住點附近與黏住的面相交的其他面")]
        private float _crossPaintRadius = 2.5f;

        [SerializeField, Tooltip("平面圓盤的筆刷半徑(公尺;[EST] 越小越只塗黏住的那一面,相交面最多被帶到這麼寬)")]
        private float _discStampRadius = 1.25f;

        [SerializeField, Tooltip("爆炸(傷害、擊退、噴墨滴);主塗墨由上面兩欄的平面圓盤負責,這裡的 PaintRadius 為 0")]
        private ExplosionSpec _blast;

        [SerializeField, Tooltip("黏住音效 id")]
        private string _stickSound = "suction_stick";

        [SerializeField, Tooltip("爆炸前警告音效 id")]
        private string _warningSound = "suction_warning";

        public float Radius => _radius;
        public int FuseFrames => _fuseFrames;
        public int WarningRestFrames => _warningRestFrames;
        public int SwellRestFrames => _swellRestFrames;
        public float DamageOffset => _damageOffset;
        public float SurfacePaintRadius => _surfacePaintRadius;
        public float CrossPaintRadius => _crossPaintRadius;
        public float DiscStampRadius => _discStampRadius;
        public ExplosionSpec Blast => _blast;
        public string StickSound => _stickSound;
        public string WarningSound => _warningSound;

        // 預覽用與實飛同半徑的 sphere cast:落點標記 = 實際黏住的點
        public override float PreviewRadius => _radius;

        // 編輯器設定(BombsSetup)呼叫:全部欄位寫成 S3 0 AP 數值(冪等)
        public void ResetToS3()
        {
            _displayName = "吸盤炸彈";
            _inkCost = 0.7f;
            _inkLockSeconds = SimFrames.ToSeconds(60);
            _maxActive = 0;
            _throwSound = "bomb_throw";
            // 投擲:S3 與斯普拉炸彈同速(1.12 u/F),沿用專案校準(20 m/s、上抬 20°、g −20:平丟約 15 m)
            _throwSpeed = 20f;
            _liftAngleDeg = 20f;
            _gravity = -20f;
            _lifetime = 8f;
            // 只黏地形:角色與目標圖層穿過(S3 GuideHitCollisionType EnemyOffFenceOn)
            _hitMask = CombatMasks.Terrain;
            _targetMask = CombatMasks.TargetLayerMask;
            _aimReach = 15f;
            // 預覽與實飛同一個 60 Hz 步進、同半徑的 sphere cast(PreviewRadius):預覽落點 = 實際接觸點
            _previewStepTime = SimFrames.Dt;
            _previewMaxSteps = 180;
            _paintHardness = 0.7f;
            _botUsage = BotSubUsage.ThrowAtEnemy;
            _botRange = new Vector2(5f, 14f);

            _radius = 0.2f;
            _fuseFrames = 120;
            _warningRestFrames = 60;
            _swellRestFrames = 30;
            _damageOffset = 0.6f;
            _surfacePaintRadius = 5f;
            _crossPaintRadius = 2.5f;
            _discStampRadius = 1.25f;
            _stickSound = "suction_stick";
            _warningSound = "suction_warning";
            _blast = new ExplosionSpec
            {
                NearDamage = 180f,
                NearRadius = 4.6f,
                FarDamage = 30f,
                FarRadius = 8f,
                AttackClass = AttackClass.Bomb,
                Kind = DamageKind.Sub,
                PaintRadius = 0f,
                // 15 滴 r 1.116(S3 SplashAroundParam;以噴墨射線近似,牆面也吃得到)
                PaintRayCount = 15,
                PaintRayRadius = 1.116f,
                PaintRayReach = 8f,
                PaintHardness = 0.7f,
                KnockbackAccel = 700f,
                KnockbackRange = 12f,
                FxBursts = 6,
                Sound = "bomb_explode",
            };
        }
    }
}

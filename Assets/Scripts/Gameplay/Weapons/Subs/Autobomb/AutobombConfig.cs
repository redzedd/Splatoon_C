using SplatoonC.Core.Combat;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // Autobomb(ロボットボム / Bomb_Robot;S3 v11.3.0,research 03 §2.8、DESIGN_REFERENCE §4.3):
    // 55% 墨、85F 墨量鎖;拋物線飛行(牆面反彈 0.7),落地後在水平 12.5 u、上下 ±5 u 內找最近的敵人(含潛伏者)
    // → 轉向 10F → 追最多 150F(每幀 +0.003、最高 0.09 u/F、每幀最多轉 2°;能走過敵墨、不會爬牆、會走下平台)
    // → 進入近距半徑(或追滿 150F)後放慢、膨脹,60F 爆炸;沒找到目標 → 落地 60F 爆炸。打不掉,走路有腳步聲。
    // 爆炸 180(2.85)/ 30(6.5)、塗墨 4.0 + 10 滴 r 0.7、擊退 470 / 9。
    [CreateAssetMenu(fileName = "Sub_autobomb", menuName = "SplatoonC/副武器/機器人炸彈")]
    public sealed class AutobombConfig : SubWeaponConfig
    {
        [Header("機器人炸彈(S3 參數)")]
        [SerializeField, Tooltip("S3 原始參數(u/F、F)")]
        private AutobombParams _s3 = AutobombParams.S3;

        [Header("幾何與手感")]
        [SerializeField, Tooltip("身體碰撞球半徑(公尺;飛行 sphere cast,走路時球心離地高度)")]
        private float _radius = 0.22f;

        [SerializeField, Tooltip("飛行撞牆的反彈係數(S2 HitVerticalWallReboundMaxRate 0.7)")]
        private float _wallRestitution = 0.7f;

        [SerializeField, Tooltip("可走上的台階高度(公尺;[EST] 不會爬牆,只跨得過路緣)")]
        private float _stepHeight = 0.25f;

        [SerializeField, Tooltip("腳步聲間隔(F;[EST])")]
        private int _footstepFrames = 10;

        [SerializeField, Tooltip("搜尋圈展開秒數(視覺)")]
        private float _searchRingSeconds = 0.45f;

        [SerializeField, Tooltip("爆炸(近/遠傷害、塗墨、擊退)")]
        private ExplosionSpec _blast;

        [Header("音效")]
        [SerializeField, Tooltip("鎖定目標音效 id")]
        private string _findSound = "autobomb_find";

        [SerializeField, Tooltip("腳步聲 id")]
        private string _footstepSound = "autobomb_step";

        [SerializeField, Tooltip("爆炸前警告音效 id(最後 60F)")]
        private string _warningSound = "autobomb_warning";

        public AutobombParams S3 => _s3;
        public float Radius => _radius;
        public float WallRestitution => _wallRestitution;
        public float StepHeight => _stepHeight;
        public int FootstepFrames => _footstepFrames;
        public float SearchRingSeconds => _searchRingSeconds;
        public ExplosionSpec Blast => _blast;
        public string FindSound => _findSound;
        public string FootstepSound => _footstepSound;
        public string WarningSound => _warningSound;

        // 預覽用與實飛同半徑的 sphere cast(S3 GuideRadius 0.5 也是 sphere cast):落點標記 = 實際落地點
        public override float PreviewRadius => _radius;

        // 編輯器設定(BombsSetup)呼叫:全部欄位寫成 S3 0 AP 數值(冪等)
        public void ResetToS3()
        {
            _displayName = "機器人炸彈";
            _inkCost = 0.55f;
            _inkLockSeconds = SimFrames.ToSeconds(85);
            _maxActive = 0;
            _throwSound = "bomb_throw";
            // 投擲同斯普拉炸彈(1.12 u/F),沿用專案校準
            _throwSpeed = 20f;
            _liftAngleDeg = 20f;
            _gravity = -20f;
            _lifetime = 8f;
            _hitMask = CombatMasks.Terrain;
            _targetMask = CombatMasks.TargetLayerMask;
            _aimReach = 15f;
            // 預覽與實飛同一個 60 Hz 步進、同半徑的 sphere cast(PreviewRadius):預覽落點 = 實際接觸點
            _previewStepTime = SimFrames.Dt;
            _previewMaxSteps = 180;
            _paintHardness = 0.7f;
            _botUsage = BotSubUsage.ThrowAtEnemy;
            // 丟到敵人附近讓它自己找:搜尋半徑 12.5 內
            _botRange = new Vector2(4f, 17f);

            _s3 = AutobombParams.S3;
            _radius = 0.22f;
            _wallRestitution = 0.7f;
            _stepHeight = 0.25f;
            _footstepFrames = 10;
            _searchRingSeconds = 0.45f;
            _findSound = "autobomb_find";
            _footstepSound = "autobomb_step";
            _warningSound = "autobomb_warning";
            _blast = new ExplosionSpec
            {
                NearDamage = 180f,
                NearRadius = 2.85f,
                FarDamage = 30f,
                FarRadius = 6.5f,
                AttackClass = AttackClass.Bomb,
                Kind = DamageKind.Sub,
                // S2 預設 4.0(S3 檔案沒列);交叉塗墨 1.75 由噴墨射線涵蓋
                PaintRadius = 4f,
                PaintRayCount = 10,
                PaintRayRadius = 0.7f,
                PaintRayReach = 6.5f,
                PaintHardness = 0.7f,
                KnockbackAccel = 470f,
                KnockbackRange = 9f,
                FxBursts = 6,
                Sound = "bomb_explode",
            };
        }
    }
}

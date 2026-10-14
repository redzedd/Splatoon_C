using SplatoonC.Core.Combat;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // Burst Bomb(クイックボム / Bomb_Quick;S3 v11.3.0,research 03 §2.3、DESIGN_REFERENCE §4.3):
    // 45% 墨、60F 墨量鎖;碰到任何東西(地形、敵人、假人、裝置)立刻爆炸,沒有引信;
    // 爆炸 35(2.8)/ 25(4.0)階梯(DamageLinear:false);直擊 = 直擊 25(投擲者端)+ 近距爆炸 35 = 60;
    // 塗墨 4.0 + 10 滴 r 0.7;擊退 470 / 8。投擲同斯普拉炸彈(1.12 u/F)。
    [CreateAssetMenu(fileName = "Sub_burst_bomb", menuName = "SplatoonC/副武器/快速炸彈")]
    public sealed class BurstBombConfig : SubWeaponConfig
    {
        [Header("快速炸彈")]
        [SerializeField, Tooltip("碰撞球半徑(公尺)")]
        private float _radius = 0.16f;

        [SerializeField, Tooltip("直擊傷害(S3 DirectHit 25;之後近距爆炸再 35 → 共 60)")]
        private float _directDamage = 25f;

        [SerializeField, Tooltip("爆炸(近/遠傷害、塗墨、擊退)")]
        private ExplosionSpec _blast;

        public float Radius => _radius;
        public float DirectDamage => _directDamage;
        public ExplosionSpec Blast => _blast;

        // 預覽用與實飛同半徑的 sphere cast:落點標記 = 實際爆炸的接觸點
        public override float PreviewRadius => _radius;

        // 編輯器設定(BombsSetup)呼叫:全部欄位寫成 S3 0 AP 數值(冪等)
        public void ResetToS3()
        {
            _displayName = "快速炸彈";
            _inkCost = 0.45f;
            _inkLockSeconds = SimFrames.ToSeconds(60);
            _maxActive = 0;
            _throwSound = "bomb_throw";
            _throwSpeed = 20f;
            _liftAngleDeg = 20f;
            _gravity = -20f;
            _lifetime = 8f;
            // 會打到敵人(S3 EnemyOnFenceOn):預覽也停在目標圖層(假人、裝置;角色圖層由預覽自己排除)
            _hitMask = CombatMasks.Terrain | CombatMasks.TargetLayerMask;
            _targetMask = CombatMasks.TargetLayerMask;
            _aimReach = 15f;
            // 預覽與實飛同一個 60 Hz 步進、同半徑的 sphere cast(PreviewRadius):預覽落點 = 實際接觸點
            _previewStepTime = SimFrames.Dt;
            _previewMaxSteps = 180;
            _paintHardness = 0.7f;
            _botUsage = BotSubUsage.ThrowAtEnemy;
            // 補傷用:近距離交戰就丟
            _botRange = new Vector2(2.5f, 12f);

            _radius = 0.16f;
            _directDamage = 25f;
            _blast = new ExplosionSpec
            {
                NearDamage = 35f,
                NearRadius = 2.8f,
                FarDamage = 25f,
                FarRadius = 4f,
                AttackClass = AttackClass.Bomb,
                Kind = DamageKind.Sub,
                PaintRadius = 4f,
                // 10 滴 r 0.7(S3 SplashAroundParam)
                PaintRayCount = 10,
                PaintRayRadius = 0.7f,
                PaintRayReach = 4f,
                PaintHardness = 0.7f,
                KnockbackAccel = 470f,
                KnockbackRange = 8f,
                FxBursts = 5,
                Sound = "bomb_explode",
                SoundVolume = 0.75f,
            };
        }
    }
}

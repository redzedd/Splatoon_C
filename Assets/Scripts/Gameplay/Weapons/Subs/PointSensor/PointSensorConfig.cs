using SplatoonC.Core.Combat;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // Point Sensor(ポイントセンサー / PointSensor;S3 v11.3.0,research 03 §2.10、DESIGN_REFERENCE §4.3):
    // 45% 墨、75F 墨量鎖;投擲 1.38 u/F(通用 S3 彈道;JP 4.0 線);碰到任何東西(地形、敵人)就爆開,
    // 半徑 6.0 的感應球停留 150F、穿牆;範圍內的敵人被標記(Marking)480F——從命中起算,晚進來的較短。無傷害、無塗墨。
    [CreateAssetMenu(fileName = "Sub_point_sensor", menuName = "SplatoonC/副武器/偵測器")]
    public sealed class PointSensorConfig : S3ThrownSubConfig
    {
        [Header("偵測器(S3 參數)")]
        [SerializeField, Tooltip("S3 原始參數(u/F、F)")]
        private PointSensorParams _s3 = PointSensorParams.S3;

        [Header("外觀")]
        [SerializeField, Tooltip("感應球展開秒數(視覺)")]
        private float _expandSeconds = 0.18f;

        [SerializeField, Tooltip("感應球透明度")]
        private float _domeAlpha = 0.22f;

        [Header("音效")]
        [SerializeField, Tooltip("爆開(開始感應)音效 id")]
        private string _burstSound = "point_sensor_burst";

        public PointSensorParams S3 => _s3;
        public float ExpandSeconds => _expandSeconds;
        public float DomeAlpha => _domeAlpha;
        public string BurstSound => _burstSound;

        public override S3ThrowParams ThrowParams => _s3.Throw;
        public override float BodyRadius => _s3.CollisionRadius;

        // S3 GuideHitCollisionType EnemyOnFenceOnTorpedoOff:會打到敵人(預覽也停在敵人身上)
        public override bool FlightHitsTargets => true;

        // bot:丟向交戰中的敵人(或藏在牆後的最後位置),r 6 內都會被標記
        public override float BotHoldSeconds(float targetDistance)
        {
            return 0.3f;
        }

        // 編輯器設定(ThrowablesSetup)呼叫:全部欄位寫成 S3 0 AP 數值(冪等)
        public void ResetToS3()
        {
            _s3 = PointSensorParams.S3;
            _displayName = "偵測器";
            _maxActive = 0;
            ResetThrowFields(0.45f, 75, "bomb_throw");
            // 會打到敵人:預覽也停在目標圖層(假人、裝置;角色圖層由預覽自己排除)
            _hitMask = CombatMasks.Terrain | CombatMasks.TargetLayerMask;
            _botUsage = BotSubUsage.ThrowAtEnemy;
            _botRange = new Vector2(6f, 20f);
            _expandSeconds = 0.18f;
            _domeAlpha = 0.22f;
            _burstSound = "point_sensor_burst";
        }
    }
}

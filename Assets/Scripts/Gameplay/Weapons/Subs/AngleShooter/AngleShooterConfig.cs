using SplatoonC.Core;
using SplatoonC.Core.Combat;
using Unity.Collections;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // Angle Shooter(ラインマーカー / LineMarker;S3 v11.3.0,research 03 §2.13、DESIGN_REFERENCE §4.3):
    // 40% 墨、50F 墨量鎖;從右手直線射出(無重力、無阻力、不繼承動量),6.3 u/F × 7F = 44.1 u(反彈後的路徑也算);
    // 碰到任何面都反彈(牆面鏡射、地面出射角 = 入射角 × 0.1 貼地前進),每次反彈塗 2.6;
    // 直擊敵人 40(AttackClass.Other:datamine 沒有 LineMarker 列 → 對物件 ×1.0)並停下、目標腳下塗 2.0;
    // 線段飛完後殘留 90F,碰到線的敵人被標記 300F(從丟出起算)。按住時顯示直線導引(含反彈)並拉近鏡頭。
    [CreateAssetMenu(fileName = "Sub_angle_shooter", menuName = "SplatoonC/副武器/標線器")]
    public sealed class AngleShooterConfig : SubWeaponConfig
    {
        // 預覽/實飛單一步的反彈上限(轉角卡住時不會無限迴圈)
        public const int MaxBounces = 24;

        [Header("標線器(S3 參數)")]
        [SerializeField, Tooltip("S3 原始參數(u/F、F)")]
        private AngleShooterParams _s3 = AngleShooterParams.S3;

        [Header("外觀")]
        [SerializeField, Tooltip("線段寬度(公尺)")]
        private float _lineWidth = 0.12f;

        [SerializeField, Tooltip("線段最後幾秒淡出")]
        private float _fadeSeconds = 0.35f;

        [Header("音效")]
        [SerializeField, Tooltip("反彈音效 id")]
        private string _bounceSound = "angle_shooter_bounce";

        [SerializeField, Tooltip("直擊音效 id")]
        private string _hitSound = "angle_shooter_hit";

        public AngleShooterParams S3 => _s3;
        public float Range => AngleShooterModel.Range(_s3);
        public float LineWidth => _lineWidth;
        public float FadeSeconds => _fadeSeconds;
        public string BounceSound => _bounceSound;
        public string HitSound => _hitSound;

        public override ThrowStyle Style => ThrowStyle.Straight;

        // 導引線粗細 = 碰撞半徑(S3 GuideRadius 0.25)
        public override float PreviewRadius => _s3.CollisionRadius;

        // 直線射出:方向 = ThrowController 算好的瞄準方向(收斂到準心點),速度 6.3 u/F;S3 動量倍率全 0
        public override bool TryComputeLaunch(ref Vector3 origin, Vector3 aimDirection, Vector3 ownerVelocity,
            float charge01, out Vector3 velocity)
        {
            Vector3 direction = aimDirection.sqrMagnitude > 1e-8f ? aimDirection.normalized : Vector3.forward;
            velocity = direction * SimFrames.PerFrameToPerSecond(_s3.SpeedPerFrame);
            return true;
        }

        // 預覽:與實飛同一個 AngleShooterTrace 一次走完 44.1 u(含反彈),停在第一個敵人/球罩;落點標記 = 終點
        public override int BuildPreview(Vector3 origin, Vector3 velocity, float charge01, Team team,
            NativeArray<Vector3> points, out bool hasLanding, out Vector3 landing, out Vector3 landingNormal)
        {
            var trace = new AngleShooterTrace();
            trace.Init(origin, velocity, Range);
            int capacity = points.Length;
            int count = 0;
            if (capacity > 0)
            {
                points[count++] = origin;
            }
            hasLanding = true;
            landing = origin;
            landingNormal = -trace.Direction;
            for (int guard = 0; guard <= MaxBounces && !trace.Finished; guard++)
            {
                AngleShooterEvent e = trace.Advance(_s3, trace.Remaining, team, true, out _, out RaycastHit hit, out _,
                    out _);
                if (e == AngleShooterEvent.None)
                {
                    break;
                }
                if (count < capacity - 1)
                {
                    points[count++] = trace.Position;
                }
                if (e != AngleShooterEvent.Bounce)
                {
                    landing = hit.point;
                    landingNormal = hit.normal.sqrMagnitude > 1e-6f ? hit.normal : -trace.Direction;
                    return count;
                }
            }
            if (count < capacity)
            {
                points[count++] = trace.Position;
            }
            landing = trace.Position;
            landingNormal = -trace.Direction;
            return count;
        }

        // bot:直線射出,看目標就放開
        public override float BotHoldSeconds(float targetDistance)
        {
            return 0.25f;
        }

        // 編輯器設定(ThrowablesSetup)呼叫:全部欄位寫成 S3 0 AP 數值(冪等)
        public void ResetToS3()
        {
            _s3 = AngleShooterParams.S3;
            _displayName = "標線器";
            _inkCost = 0.4f;
            _inkLockSeconds = SimFrames.ToSeconds(50);
            _maxActive = 0;
            _throwSound = "angle_shooter_fire";
            // 一般拋物線欄位只給大招接管/除錯用;實際出手走 TryComputeLaunch(直線)
            _throwSpeed = SimFrames.PerFrameToPerSecond(_s3.SpeedPerFrame);
            _liftAngleDeg = 0f;
            _gravity = 0f;
            _lifetime = 3f;
            _hitMask = CombatMasks.Terrain | CombatMasks.TargetLayerMask;
            _targetMask = CombatMasks.TargetLayerMask;
            // 準心射線取到射程那麼遠:直線收斂到準心點
            _aimReach = AngleShooterModel.Range(_s3);
            _previewStepTime = SimFrames.Dt;
            _previewMaxSteps = 60;
            _paintHardness = 0.7f;
            _botUsage = BotSubUsage.ThrowAtEnemy;
            _botRange = new Vector2(4f, 30f);
            _lineWidth = 0.12f;
            _fadeSeconds = 0.35f;
            _bounceSound = "angle_shooter_bounce";
            _hitSound = "angle_shooter_hit";
        }
    }
}

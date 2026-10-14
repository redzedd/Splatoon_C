using SplatoonC.Core;
using SplatoonC.Core.Combat;
using Unity.Collections;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // Curling Bomb(カーリングボム / Bomb_Curling;S3 v11.3.0,research 03 §2.7、DESIGN_REFERENCE §4.3):
    // 65% 墨;按住蓄力(5F 準備後最多 60F),墨量鎖 70F → 滿蓄 30F;放開從腰部低拋(0.40 → 滿蓄 0.20 u/F)後貼地滑行
    // (巡航 0.22 → 滿蓄 0.10 u/F,剩 90F 減速停住),牆面鏡射、可爬小台階;引信 210 − 2 × 蓄力幀(放開起算,空中也爆);
    // 滑行留下可游的自隊墨軌跡(r 1.075 → 1.29);滑過敵人 20 傷(同一目標每 40F 最多一次)+ 小擊退(350、60°);
    // 爆炸 180(1.6 → 4.6)/ 30(5.0 → 8.0)、塗墨 2.133 → 5.0 + 12 滴 r 0.805、擊退 700 / 9。
    // ThrowStyle.GroundRoll:出手與預覽由本設定提供(TryComputeLaunch / BuildPreview,與 CurlingBomb 共用 CurlingMotion)。
    [CreateAssetMenu(fileName = "Sub_curling_bomb", menuName = "SplatoonC/副武器/冰壺炸彈")]
    public sealed class CurlingBombConfig : SubWeaponConfig
    {
        [Header("冰壺炸彈(S3 參數)")]
        [SerializeField, Tooltip("S3 原始參數(u/F、F)")]
        private CurlingBombParams _s3 = CurlingBombParams.S3;

        [Header("幾何與手感")]
        [SerializeField, Tooltip("碰撞球半徑(公尺;炸彈中心離地高度)")]
        private float _radius = 0.22f;

        [SerializeField, Tooltip("出手點比槍口低多少(公尺;S3 從腰部低拋)")]
        private float _launchDrop = 0.45f;

        [SerializeField, Tooltip("可爬上的台階高度(公尺;[EST] S3「能爬小台階」)")]
        private float _stepHeight = 0.35f;

        [SerializeField, Tooltip("軌跡塗墨間距(公尺;每滑這麼遠塗一筆)")]
        private float _trailSpacing = 0.4f;

        [SerializeField, Tooltip("接觸判定半徑(公尺;[EST] 滑過敵人的碰撞)")]
        private float _contactRadius = 0.5f;

        [SerializeField, Tooltip("接觸擊退強度(S3 KnockBackParam Accel 350)")]
        private float _contactKnockbackAccel = 350f;

        [SerializeField, Tooltip("接觸擊退仰角(度;S3 Degree 60)")]
        private float _contactKnockbackDegree = 60f;

        [SerializeField, Tooltip("接觸擊退半徑(公尺;S3 Distance 10)")]
        private float _contactKnockbackRange = 10f;

        [SerializeField, Tooltip("爆炸(半徑、塗墨半徑依蓄力由 S3 參數覆寫;這裡給傷害值、噴墨滴、擊退、音效)")]
        private ExplosionSpec _blast;

        [SerializeField, Tooltip("預覽每幾幀取一點")]
        private int _previewStride = 2;

        [Header("音效")]
        [SerializeField, Tooltip("滑行循環音效 id")]
        private string _slideLoopSound = "curling_slide_loop";

        [SerializeField, Tooltip("撞牆反彈音效 id")]
        private string _bounceSound = "curling_bounce";

        [SerializeField, Tooltip("剩 90F 起的警告音效 id")]
        private string _warningSound = "curling_warning";

        [SerializeField, Tooltip("蓄滿音效 id")]
        private string _chargedSound = "curling_charge_full";

        public CurlingBombParams S3 => _s3;
        public float Radius => _radius;
        public float LaunchDrop => _launchDrop;
        public float StepHeight => _stepHeight;
        public float TrailSpacing => _trailSpacing;
        public float ContactRadius => _contactRadius;
        public float ContactKnockbackAccel => _contactKnockbackAccel;
        public float ContactKnockbackDegree => _contactKnockbackDegree;
        public float ContactKnockbackRange => _contactKnockbackRange;
        public string SlideLoopSound => _slideLoopSound;
        public string BounceSound => _bounceSound;
        public string WarningSound => _warningSound;

        public override ThrowStyle Style => ThrowStyle.GroundRoll;
        public override int MaxChargeFrames => _s3.MaxChargeFrames;
        public override int ChargeWindupFrames => _s3.ChargeWindupFrames;
        public override string ChargedSound => _chargedSound;

        // S3:70F → 滿蓄 30F
        public override float InkLockSecondsAt(float charge01)
        {
            return SimFrames.ToSeconds(CurlingBombModel.InkLockFrames(_s3, charge01));
        }

        // 蓄力後的爆炸規格:半徑與塗墨半徑依 S3 內插
        public ExplosionSpec BlastAt(float charge01)
        {
            ExplosionSpec spec = _blast;
            spec.NearRadius = CurlingBombModel.NearRadius(_s3, charge01);
            spec.FarRadius = CurlingBombModel.FarRadius(_s3, charge01);
            spec.PaintRadius = CurlingBombModel.PaintRadius(_s3, charge01);
            spec.PaintRayReach = spec.FarRadius;
            return spec;
        }

        // S3 低拋:從腰部(槍口往下 LaunchDrop)出手,v = 視線 × SpawnSpeedZ + 上 × 0.12 + 玩家動量(u/F → m/s)
        public override bool TryComputeLaunch(ref Vector3 origin, Vector3 aimDirection, Vector3 ownerVelocity,
            float charge01, out Vector3 velocity)
        {
            origin += Vector3.down * _launchDrop;
            Vector3 perFrame = CurlingBombModel.LaunchVelocity(_s3, aimDirection, charge01,
                ownerVelocity * SimFrames.Dt);
            velocity = perFrame * SimFrames.Rate;
            return true;
        }

        // 預覽:與 CurlingBomb 同一個 CurlingMotion 逐幀跑到引信結束(或碰到敵方屏障提前爆炸);落點標記 = 爆炸位置
        public override int BuildPreview(Vector3 origin, Vector3 velocity, float charge01, Team team,
            NativeArray<Vector3> points, out bool hasLanding, out Vector3 landing, out Vector3 landingNormal)
        {
            var motion = new CurlingMotion();
            motion.Init(origin, velocity * SimFrames.Dt, charge01, _s3, _radius, _stepHeight);
            int capacity = points.Length;
            int count = 0;
            if (capacity > 0)
            {
                points[count++] = origin;
            }
            int stride = Mathf.Max(1, _previewStride);
            bool detonated = false;
            // 一定跑到引信結束(落點要正確);點數緩衝滿了就只是不再加點
            while (true)
            {
                Vector3 previous = motion.Position;
                bool alive = motion.Step(_s3);
                // 與實飛同一條規則(CurlingBomb.Step):碰到敵方屏障就在屏障前爆
                if (BombRules.TouchDetonator(previous, motion.Position, _radius, team, out Vector3 touch))
                {
                    motion.Position = touch;
                    motion.Grounded = false;
                    detonated = true;
                    break;
                }
                if (!alive)
                {
                    break;
                }
                if (count < capacity - 1 && (motion.Bounced || motion.Landed || motion.Frame % stride == 0))
                {
                    points[count++] = PreviewPoint(motion);
                }
            }
            if (count < capacity)
            {
                points[count++] = PreviewPoint(motion);
            }
            hasLanding = true;
            landing = motion.Grounded ? motion.GroundPoint : motion.Position;
            landingNormal = motion.Grounded ? motion.GroundNormal : Vector3.up;
            if (detonated)
            {
                // 屏障前爆炸:標記貼在爆點正下方的地面(找不到地面就放在爆點)
                if (Physics.Raycast(motion.Position, Vector3.down, out RaycastHit ground, _radius + 1f,
                        CombatMasks.Terrain, QueryTriggerInteraction.Ignore))
                {
                    landing = ground.point;
                    landingNormal = ground.normal;
                }
            }
            return count;
        }

        private static Vector3 PreviewPoint(in CurlingMotion motion)
        {
            return motion.Grounded ? motion.GroundPoint + motion.GroundNormal * 0.05f : motion.Position;
        }

        // bot:依目標距離蓄力,讓爆炸落在目標附近(假設往目標腳下俯 15° 丟)
        public override float BotHoldSeconds(float targetDistance)
        {
            float charge = CurlingBombModel.ChargeForDistance(_s3, targetDistance, -15f, 0.55f);
            float frames = _s3.ChargeWindupFrames + charge * _s3.MaxChargeFrames + 2f;
            return SimFrames.ToSeconds(frames);
        }

        // 編輯器設定(BombsSetup)呼叫:全部欄位寫成 S3 0 AP 數值(冪等)
        public void ResetToS3()
        {
            _displayName = "冰壺炸彈";
            _inkCost = 0.65f;
            _inkLockSeconds = SimFrames.ToSeconds(70);
            _maxActive = 0;
            _throwSound = "bomb_throw";
            // 一般拋物線欄位只給大招接管/除錯用;實際出手走 TryComputeLaunch(S3 低拋)
            _throwSpeed = 24f;
            _liftAngleDeg = 0f;
            _gravity = -20f;
            _lifetime = 8f;
            _hitMask = CombatMasks.Terrain;
            _targetMask = CombatMasks.TargetLayerMask;
            _aimReach = 15f;
            _previewStepTime = 0.03f;
            _previewMaxSteps = 100;
            _paintHardness = 0.7f;
            _botUsage = BotSubUsage.ThrowAtEnemy;
            _botRange = new Vector2(3f, 18f);

            _s3 = CurlingBombParams.S3;
            _radius = 0.22f;
            _launchDrop = 0.45f;
            _stepHeight = 0.35f;
            _trailSpacing = 0.4f;
            _contactRadius = 0.5f;
            _contactKnockbackAccel = 350f;
            _contactKnockbackDegree = 60f;
            _contactKnockbackRange = 10f;
            _previewStride = 2;
            _slideLoopSound = "curling_slide_loop";
            _bounceSound = "curling_bounce";
            _warningSound = "curling_warning";
            _chargedSound = "curling_charge_full";
            _blast = new ExplosionSpec
            {
                NearDamage = 180f,
                NearRadius = _s3.NearRadiusMinCharge,
                FarDamage = 30f,
                FarRadius = _s3.FarRadiusMinCharge,
                AttackClass = AttackClass.Bomb,
                Kind = DamageKind.Sub,
                PaintRadius = _s3.PaintRadiusMinCharge,
                // 12 滴 r 0.805(S3 SplashAroundParam)
                PaintRayCount = 12,
                PaintRayRadius = 0.805f,
                PaintRayReach = _s3.FarRadiusMinCharge,
                PaintHardness = 0.7f,
                KnockbackAccel = 700f,
                KnockbackRange = 9f,
                FxBursts = 6,
                Sound = "bomb_explode",
            };
        }
    }
}

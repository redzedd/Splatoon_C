using SplatoonC.Core;
using SplatoonC.Core.Combat;
using Unity.Collections;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 走 S3 通用投擲彈道的副武器設定基底(Torpedo 1.40、Fizzy Bomb 1.36、Point Sensor 1.38 u/F):
    // 出手 = S3ThrowModel.LaunchVelocity(視線 × SpawnSpeedZ + 上 × 0.24、垂直下限 −0.4、繼承動量 1.6 / 2.0 / 4.0 ≤ 0.32);
    // 預覽 = 與投擲物同一個 S3ThrownFlight 逐幀跑到第一次接觸(同半徑的 BombRules.CastPath:敵方屏障與球罩也會停),
    // 落點標記 = 實際接觸點。ThrowController 的預設拋物線(專案校準的 20 m/s 無阻力)不用在這些副武器上。
    public abstract class S3ThrownSubConfig : SubWeaponConfig
    {
        [Header("S3 投擲預覽")]
        [SerializeField, Tooltip("預覽每幾幀取一點")]
        protected int _previewStride = 2;

        // 彈道參數(S3,u/F)
        public abstract S3ThrowParams ThrowParams { get; }

        // 投擲物碰撞球半徑(公尺;實飛與預覽的 sphere cast)
        public abstract float BodyRadius { get; }

        // 飛行中會不會打到敵人(Point Sensor 會,Torpedo / Fizzy 穿過)
        public virtual bool FlightHitsTargets => false;

        public override float PreviewRadius => BodyRadius;

        public override bool TryComputeLaunch(ref Vector3 origin, Vector3 aimDirection, Vector3 ownerVelocity,
            float charge01, out Vector3 velocity)
        {
            S3ThrowParams p = ThrowParams;
            velocity = S3ThrowModel.LaunchVelocity(p, aimDirection, ownerVelocity * SimFrames.Dt) * SimFrames.Rate;
            return true;
        }

        public override int BuildPreview(Vector3 origin, Vector3 velocity, float charge01, Team team,
            NativeArray<Vector3> points, out bool hasLanding, out Vector3 landing, out Vector3 landingNormal)
        {
            S3ThrowParams p = ThrowParams;
            var flight = new S3ThrownFlight();
            flight.Init(origin, velocity * SimFrames.Dt);
            int capacity = points.Length;
            int count = 0;
            if (capacity > 0)
            {
                points[count++] = origin;
            }
            hasLanding = false;
            landing = origin;
            landingNormal = Vector3.up;
            int stride = Mathf.Max(1, _previewStride);
            int maxFrames = Mathf.Max(1, _previewMaxSteps);
            float radius = BodyRadius;
            bool targets = FlightHitsTargets;
            for (int frame = 1; frame <= maxFrames; frame++)
            {
                BombContact contact = flight.Step(p, radius, team, targets, out RaycastHit hit, out _);
                if (contact != BombContact.None)
                {
                    hasLanding = true;
                    landing = hit.point;
                    landingNormal = hit.normal.sqrMagnitude > 1e-6f ? hit.normal : Vector3.up;
                    if (count < capacity)
                    {
                        points[count++] = flight.Position;
                    }
                    break;
                }
                if (count < capacity - 1 && frame % stride == 0)
                {
                    points[count++] = flight.Position;
                }
            }
            if (!hasLanding && count < capacity)
            {
                points[count++] = flight.Position;
            }
            return count;
        }

        // bot 丟到目標前/後多少公尺(Fizzy 落地後還會滾、彈跳往前 → 落短一點)
        protected virtual float BotLandOffset => 0f;

        // bot 瞄準:S3 彈道加了阻力與 +0.24 上拋,直接看目標腳下會丟太遠。以二分法解出「從手上(腳下 + 1.2 m [EST])
        // 丟出、落在目標(+ BotLandOffset)」的視線俯仰角(平地近似),回傳沿該方向 AimReach 遠的點
        //(ThrowController 以視線射線上的點當目標,從投擲原點指過去 → 方向一致)。超出最大射程 → 用最佳仰角附近的 12°。
        public override Vector3 BotAimPoint(Vector3 thrower, Vector3 target)
        {
            Vector3 origin = thrower + Vector3.up * 1.2f;
            Vector3 flat = target - origin;
            flat.y = 0f;
            float distance = flat.magnitude + BotLandOffset;
            if (distance < 0.5f || flat.sqrMagnitude < 1e-6f)
            {
                return target + Vector3.up * 0.3f;
            }
            flat.Normalize();
            float height = Mathf.Max(0.2f, origin.y - target.y);
            S3ThrowParams p = ThrowParams;
            float lo = -60f;
            float hi = 12f;
            for (int i = 0; i < 12; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (S3ThrowModel.PredictFlatRange(p, mid, height, out _) < distance)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }
            float pitch = (lo + hi) * 0.5f * Mathf.Deg2Rad;
            Vector3 direction = flat * Mathf.Cos(pitch) + Vector3.up * Mathf.Sin(pitch);
            return origin + direction * Mathf.Max(8f, _aimReach);
        }

        // 無限平地上的落點距離(公尺;不帶動量,出手高 launchHeight):測試與 bot 驗算用
        public float PredictFlatRange(float pitchDeg, float launchHeight)
        {
            return S3ThrowModel.PredictFlatRange(ThrowParams, pitchDeg, launchHeight, out _);
        }

        // 子類 ResetToS3 共用:S3 通用投擲的 ThrowableConfig 欄位(一般拋物線欄位只給大招接管/除錯用)
        protected void ResetThrowFields(float inkCost, int inkLockFrames, string throwSound)
        {
            _inkCost = inkCost;
            _inkLockSeconds = SimFrames.ToSeconds(inkLockFrames);
            _throwSound = throwSound;
            S3ThrowParams p = ThrowParams;
            _throwSpeed = SimFrames.PerFrameToPerSecond(p.SpawnSpeedZ);
            _liftAngleDeg = 12f;
            _gravity = -p.FlyGravity * SimFrames.Rate * SimFrames.Rate;
            _lifetime = 8f;
            _hitMask = CombatMasks.Terrain;
            _targetMask = CombatMasks.TargetLayerMask;
            _aimReach = 20f;
            _previewStepTime = SimFrames.Dt;
            _previewMaxSteps = 180;
            _previewStride = 2;
            _paintHardness = 0.7f;
        }
    }
}

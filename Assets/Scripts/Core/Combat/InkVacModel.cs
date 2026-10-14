using UnityEngine;

namespace SplatoonC.Core.Combat
{
    // Ink Vac(S3 SpBlower,research/04 §2.8、JP wiki キューインキ;v11.3.0 0 AP)的純邏輯:吸引錐幾何、容量換算、時序。
    //
    // 吸引錐(InhaleParam):按住攻擊後長度每幀 +0.3334 u 長到 15 u(45F);半徑沿錐長線性 0.8(槍口)→ 3.3(15 u 處);
    //   另有槍口球(MuzzleCollisionOffset 1.0、Radius 1.2)正面防禦較厚(JP wiki:「銃口付近にも吸引判定」)。
    // 容量:吸進的攻擊以傷害量計,1100 滿(v11.3.0);吸到敵方角色每幀 +1.5(JP wiki 實測 326F → 146.7°)。
    // 發射(ExhaleParam):直擊 220、落地 50F 後爆炸 220(無衰減),半徑 6 u(沒吸)→ 11 u(吸滿)線性 [EST 線性];
    //   初速 0.55 u/F(沒吸)→ 0.7 u/F(吸滿,SpawnSpeedZMaxCharge)、上 0.08 u/F;重力 0.003 u/F²、空氣阻力 0.01/F。
    // 移動(WeaponParam):人形 0.09 u/F(沒吸)→ 0.07 u/F(吸滿);發射模式 0.072 u/F(JP wiki 0.72 DU/F)。
    public static class InkVacModel
    {
        // ── 時序(幀)──
        public const int ActivationLagFrames = 17;
        public const int SuctionFrames = 360;
        public const int FrontLagHumanFrames = 12;
        public const int FrontLagSquidFrames = 18;
        public const int InhaleToExhaleFrames = 20;
        public const int ExhaleWaitFrames = 150;
        public const int BlastWaitFrames = 50;
        public const int PaintSplashSpanFrames = 20;

        // ── 吸引錐 ──
        public const float LengthMax = 15f;
        public const float LengthAddPerFrame = 0.3334f;
        public const float RadiusNear = 0.8f;
        public const float RadiusFar = 3.3f;
        public const float MuzzleOffset = 1f;
        public const float MuzzleRadius = 1.2f;

        // ── 容量 ──
        public const float Capacity = 1100f;
        public const float PlayerFillPerFrame = 1.5f;
        public const float ObjectDamagePerFrame = 2f;

        // ── 發射 ──
        public const float ShotDamage = 220f;
        public const float BlastRadiusMin = 6f;
        public const float BlastRadiusMax = 11f;
        public const float ShotSpeedMin = 0.55f;
        public const float ShotSpeedMax = 0.7f;
        public const float ShotSpeedY = 0.08f;
        public const float ShotGravity = 0.003f;
        public const float ShotAirResist = 0.01f;
        public const float ShotPaintMin = 2.4f;
        public const float ShotPaintMax = 3.2f;

        // ── 移動(u/F)──
        public const float MoveSpeedEmpty = 0.09f;
        public const float MoveSpeedFull = 0.07f;
        public const float MoveSpeedFire = 0.072f;

        // 反擊彈的彈道:直接自由落體(下限 20/10 = 跳過減速),阻力 0.01、重力 0.003(幾乎直線、遠距離仍不太掉速)
        public static BulletMotion ShotMotion(float fill01)
        {
            return new BulletMotion
            {
                SpawnSpeed = ShotSpeed(fill01),
                StraightFrames = 0,
                StraightEndMaxSpeed = 1000f,
                BrakeAirResist = ShotAirResist,
                BrakeGravity = ShotGravity,
                BrakeToFreeVelocityXZ = 20f,
                BrakeToFreeVelocityY = 10f,
                FreeAirResist = ShotAirResist,
                FreeGravity = ShotGravity,
            };
        }

        // 按住吸引 heldFrames 幀時的錐長(前隙結束才開始長)
        public static float LengthAt(float heldFrames)
        {
            return Mathf.Clamp(heldFrames * LengthAddPerFrame, 0f, LengthMax);
        }

        // 沿錐軸 s 公尺處的半徑(0.8 → 3.3 線性,以 15 u 為全長)
        public static float RadiusAt(float s)
        {
            return Mathf.Lerp(RadiusNear, RadiusFar, Mathf.Clamp01(s / LengthMax));
        }

        public static float Fill01(float absorbed)
        {
            return Mathf.Clamp01(absorbed / Capacity);
        }

        public static float BlastRadius(float fill01)
        {
            return Mathf.Lerp(BlastRadiusMin, BlastRadiusMax, Mathf.Clamp01(fill01));
        }

        public static float ShotSpeed(float fill01)
        {
            return Mathf.Lerp(ShotSpeedMin, ShotSpeedMax, Mathf.Clamp01(fill01));
        }

        public static float ShotPaintRadius(float fill01)
        {
            return Mathf.Lerp(ShotPaintMin, ShotPaintMax, Mathf.Clamp01(fill01));
        }

        // 人形走速(u/F):吸越多越重
        public static float MoveSpeed(float fill01)
        {
            return Mathf.Lerp(MoveSpeedEmpty, MoveSpeedFull, Mathf.Clamp01(fill01));
        }

        // point 在不在吸引範圍(錐 + 槍口球)。apex = 槍口、axis = 單位方向、length = 目前錐長
        public static bool Contains(Vector3 apex, Vector3 axis, float length, Vector3 point, float pad = 0f)
        {
            if (length <= 0f)
            {
                return false;
            }
            Vector3 muzzle = apex + axis * MuzzleOffset;
            float muzzleRadius = MuzzleRadius + pad;
            if ((point - muzzle).sqrMagnitude <= muzzleRadius * muzzleRadius)
            {
                return true;
            }
            Vector3 d = point - apex;
            float s = Vector3.Dot(d, axis);
            if (s < 0f || s > length + pad)
            {
                return false;
            }
            Vector3 perpendicular = d - axis * s;
            float radius = RadiusAt(s) + pad;
            return perpendicular.sqrMagnitude <= radius * radius;
        }

        // 線段 from + dir·t(0 ≤ t ≤ maxDistance)第一次進入吸引範圍的距離;以 ≤ 0.35 m 的間距取樣(最多 64 點)。
        // pad = 攻擊判定半徑(球)。回傳 false = 沒碰到
        public static bool SegmentEntry(Vector3 apex, Vector3 axis, float length, Vector3 from, Vector3 direction,
            float maxDistance, float pad, out float distance)
        {
            distance = 0f;
            if (length <= 0f || maxDistance < 0f)
            {
                return false;
            }
            // 粗篩:線段與包圍球(錐長一半為心)離太遠就不取樣
            Vector3 center = apex + axis * (length * 0.5f);
            float boundRadius = length * 0.5f + RadiusAt(length) + MuzzleRadius + pad;
            Vector3 toCenter = center - from;
            float along = Mathf.Clamp(Vector3.Dot(toCenter, direction), 0f, maxDistance);
            if ((from + direction * along - center).sqrMagnitude > boundRadius * boundRadius)
            {
                return false;
            }
            int samples = Mathf.Clamp(Mathf.CeilToInt(maxDistance / 0.35f), 1, 64);
            for (int i = 0; i <= samples; i++)
            {
                float t = maxDistance * i / samples;
                if (Contains(apex, axis, length, from + direction * t, pad))
                {
                    distance = t;
                    return true;
                }
            }
            return false;
        }
    }
}

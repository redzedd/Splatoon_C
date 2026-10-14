using UnityEngine;

namespace SplatoonC.Core.Combat
{
    // Killer Wail 5.1(S3 SpMicroLaser,research/04 §2.9、JP wiki メガホンレーザー5.1ch)的時間表與鎖定判定純邏輯。
    // 6 支喇叭 = 3 組 × 2 支,由上而下每 36F 一組(ChargeFrame 36):鎖定 → 20F 預告線 → 照射 180F(LaserFrame)。
    // 第一組照射開始到最後一組照射結束 = 36 × 2 + 180 = 252F(JP wiki 総効果時間 252F)。
    // 鎖定範圍:準心方向 ±80°(LockonDeg 80,全角 160°)內、離準心射線的垂直距離 ≤ 10 u(LockonRadius)。
    public static class KillerWailModel
    {
        public const int PairCount = 3;
        public const int PairIntervalFrames = 36;
        public const int WarningFrames = 20;
        public const int LaserFrames = 180;
        // 發動到第一組鎖定:喇叭飛出排好隊形的時間 [EST 16F:讓第一組在 36F 開始照射,與 36F 間隔同拍]
        public const int FirstLockFrame = 16;
        public const int DamageIntervalFrames = 5;
        public const float DamagePerTick = 3.5f;
        public const float LockonDeg = 80f;
        public const float LockonRadius = 10f;
        // 發動後主/副武器與烏賊不能用的時間(AfterShot_NoShotFrm / NoBombFrm / NoSquidFrm 40)
        public const int UserLockFrames = 40;

        public static int LockFrame(int pair)
        {
            return FirstLockFrame + pair * PairIntervalFrames;
        }

        public static int FireFrame(int pair)
        {
            return LockFrame(pair) + WarningFrames;
        }

        public static int EndFrame(int pair, int laserFrames = LaserFrames)
        {
            return FireFrame(pair) + laserFrames;
        }

        // 發動到最後一組照射結束(持續型大招的能量計時):16 + 72 + 20 + 180 = 288F
        public static int TotalFrames(int laserFrames = LaserFrames)
        {
            return EndFrame(PairCount - 1, laserFrames);
        }

        // 一支喇叭照射期間的傷害次數與總傷害(180F / 5F = 36 次 × 3.5 = 126;6 支 756 = JP wiki 大假人總傷)
        public static int TicksPerBeam(int laserFrames = LaserFrames)
        {
            return laserFrames / DamageIntervalFrames;
        }

        // 鎖定判定:point 是否在 origin 沿 forward(已正規化)的鎖定範圍內;lateral = 離準心射線的垂直距離(越小越優先)
        public static bool InLockon(Vector3 origin, Vector3 forward, Vector3 point, out float lateral,
            float maxDeg = LockonDeg, float radius = LockonRadius)
        {
            Vector3 v = point - origin;
            float along = Vector3.Dot(v, forward);
            float distance = v.magnitude;
            lateral = float.MaxValue;
            if (distance < 1e-4f)
            {
                lateral = 0f;
                return true;
            }
            float cos = along / distance;
            if (cos < Mathf.Cos(maxDeg * Mathf.Deg2Rad))
            {
                return false;
            }
            lateral = (v - forward * along).magnitude;
            return lateral <= radius;
        }

        // 六角形隊形上第 slot 支喇叭(0..5)在「面向 +Z、上 +Y」座標系的偏移:由上而下成對
        //(0,1 = 上左/上右 60°/120°;2,3 = 左右 0°/180°;4,5 = 下 240°/300°),半徑 LaserBitRadius 1.3
        public static Vector2 HexSlot(int slot, float radius = 1.3f)
        {
            float deg;
            switch (slot)
            {
                case 0: deg = 120f; break;
                case 1: deg = 60f; break;
                case 2: deg = 180f; break;
                case 3: deg = 0f; break;
                case 4: deg = 240f; break;
                default: deg = 300f; break;
            }
            float rad = deg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * radius;
        }
    }
}

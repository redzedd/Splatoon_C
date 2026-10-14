using System;
using UnityEngine;

namespace SplatoonC.Core.Combat
{
    // Angle Shooter 的 S3 參數(Leanny v11.3.0 WeaponLineMarker;u = m、F = 1/60 s)。
    [Serializable]
    public struct AngleShooterParams
    {
        [Header("飛行(直線、無重力、無阻力、不繼承動量)")]
        [Tooltip("速度(u/F;S3 SpawnSpeedZSpecUp.Low 6.3)")]
        public float SpeedPerFrame;

        [Tooltip("飛行幀數(S3 FlyingFrame 7 → 6.3 × 7 = 44.1 u)")]
        public int FlyingFrames;

        [Tooltip("碰撞半徑(u;S3 CollisionRadius / GuideRadius 0.25)")]
        public float CollisionRadius;

        [Header("反彈")]
        [Tooltip("地面反射角倍率(S3 ReboundGroundReflectAngleRate 0.1:出射角 = 入射角 × 0.1,貼地前進)")]
        public float GroundReflectAngleRate;

        [Tooltip("牆面反射角倍率(S3 ReboundWallReflectAngleRate 1.0:鏡射)")]
        public float WallReflectAngleRate;

        [Tooltip("視為「地面」的法線 y 下限([EST] 與炸彈貼地判定同 0.6)")]
        public float GroundNormalY;

        [Header("傷害與塗墨")]
        [Tooltip("直擊傷害(S3 DirectDamage 400 = 40.0)")]
        public float DirectDamage;

        [Tooltip("每次反彈在反彈點塗墨半徑(u;S3 PaintRadius 2.6)")]
        public float BouncePaintRadius;

        [Tooltip("直擊時在目標腳下塗墨半徑(u;S3 BlastParam.PaintRadius 2.0)")]
        public float HitPaintRadius;

        [Tooltip("直擊塗墨往下找地面的高度(u;S3 PaintHeight 3.0)")]
        public float HitPaintHeight;

        [Header("線段殘留與標記")]
        [Tooltip("飛完後線段殘留幀數(S3 TailFrame 90;JP 實測約 105F)")]
        public int TailFrames;

        [Tooltip("標記幀數(S3 MarkingFrame.Low 300,從丟出起算)")]
        public int MarkingFrames;

        [Tooltip("線段碰到敵人的判定半徑(u;[EST] 同碰撞半徑)")]
        public float TouchRadius;

        public static AngleShooterParams S3 => new AngleShooterParams
        {
            SpeedPerFrame = 6.3f,
            FlyingFrames = 7,
            CollisionRadius = 0.25f,
            GroundReflectAngleRate = 0.1f,
            WallReflectAngleRate = 1f,
            GroundNormalY = 0.6f,
            DirectDamage = 40f,
            BouncePaintRadius = 2.6f,
            HitPaintRadius = 2f,
            HitPaintHeight = 3f,
            TailFrames = 90,
            MarkingFrames = 300,
            TouchRadius = 0.25f,
        };
    }

    // Angle Shooter 純邏輯(research 03 §2.13、DESIGN_REFERENCE §4.3):
    //   直線 6.3 u/F × 7F = 44.1 u(反彈後的路徑也算在總長裡);碰到任何面都反彈(格柵除外):
    //   牆面鏡射(角度倍率 1.0)、地面出射角 = 入射角 × 0.1(貼著地面繼續走);直擊敵人 40 並停下;
    //   線段飛完後殘留 90F,碰到線的敵人被標記 300F(從丟出起算:丟出後 1.5 s 才碰到 → 只剩 3.5 s)。
    public static class AngleShooterModel
    {
        public static float Range(in AngleShooterParams p)
        {
            return p.SpeedPerFrame * p.FlyingFrames;
        }

        // 反彈後的方向(單位向量):direction = 入射方向(單位),normal = 表面法線(單位,朝外)
        public static Vector3 Reflect(in AngleShooterParams p, Vector3 direction, Vector3 normal)
        {
            float dn = Vector3.Dot(direction, normal);
            if (dn >= 0f)
            {
                // 已經離開表面(擦邊):方向不變
                return direction;
            }
            float rate = normal.y >= p.GroundNormalY ? p.GroundReflectAngleRate : p.WallReflectAngleRate;
            Vector3 tangent = direction - normal * dn;
            float tangentLength = tangent.magnitude;
            if (tangentLength < 1e-5f)
            {
                // 正面垂直打進去:出射角 = 90° × rate,切線方向不存在 → 原路彈回(鏡射)或沿法線
                return rate >= 1f - 1e-4f ? -direction : normal;
            }
            // 入射角(與表面的夾角)× 倍率 = 出射角;rate 1 = 鏡射
            float incidence = Mathf.Atan2(-dn, tangentLength);
            float outgoing = incidence * rate;
            Vector3 along = tangent / tangentLength;
            return (along * Mathf.Cos(outgoing) + normal * Mathf.Sin(outgoing)).normalized;
        }

        // 丟出後 framesSinceThrow 幀才碰到線:剩下的標記秒數(從丟出起算 300F;超過 = 0)
        public static float MarkSecondsAt(in AngleShooterParams p, int framesSinceThrow)
        {
            return Mathf.Max(0, p.MarkingFrames - Mathf.Max(0, framesSinceThrow)) / 60f;
        }

        // 線段存在的總幀數(飛行 + 殘留)
        public static int LifetimeFrames(in AngleShooterParams p)
        {
            return p.FlyingFrames + p.TailFrames;
        }
    }
}

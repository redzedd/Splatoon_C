using System;
using UnityEngine;

namespace SplatoonC.Core.Combat
{
    // Torpedo 的 S3 參數(Leanny v11.3.0 WeaponBombTorpedo + JP wiki トーピード;u = m、F = 1/60 s、速度 u/F)。
    [Serializable]
    public struct TorpedoParams
    {
        [Header("投擲")]
        [Tooltip("通用投擲彈道(SpawnSpeedZ 1.40 u/F,9.2.0 +3%)")]
        public S3ThrowParams Throw;

        [Header("空中搜尋")]
        [Tooltip("投出後幾 F 才開始搜尋([EST] JP:約 1.5 線 ≈ 7.5 u,以初速換算 5~6F)")]
        public int FindStartFrame;

        [Tooltip("水平搜尋半徑(u;JP 1.8 線 = 9.0)")]
        public float SearchRadius;

        [Tooltip("搜尋窗:目標可在 Torpedo 上方多高(u;[EST] JP「上 約 1.0 / 下 約 0.5」與「上方約イカ 1 匹、下方約 2 匹」兩說,取下方較寬)")]
        public float SearchAbove;

        [Tooltip("搜尋窗:目標可在 Torpedo 下方多深(u;[EST] 見上;山なり投げ在頭頂展開的打法成立)")]
        public float SearchBelow;

        [Header("變身與衝刺")]
        [Tooltip("變身:轉向目標的幀數([EST] 變身共約 45F = 轉向 25F + BetweenRotEndAndStartChaseFrame 20)")]
        public int RotateFrames;

        [Tooltip("轉完到開始衝刺的幀數(S3 BetweenRotEndAndStartChaseFrame 20)")]
        public int HoldFrames;

        [Tooltip("衝刺速度(u/F;[EST] S3 檔案未列,影片目測約 12~15 m/s)")]
        public float ChaseSpeed;

        [Tooltip("衝刺最長幀數(F;[EST] 沒撞到東西就在空中爆)")]
        public int ChaseMaxFrames;

        [Tooltip("變身後的耐久(JP 基本性能 20.0)")]
        public float Hp;

        [Header("落地(先碰到地形:失去追蹤、像炸彈一樣滾)")]
        [Tooltip("落地後幾 F 爆炸(S3 BurstFrame 30)")]
        public int BurstFrame;

        [Tooltip("爆炸前幾 F 起警告音(S3 WarningSERestFrame 30 → 落地就響)")]
        public int WarningRestFrame;

        [Tooltip("地面滾動阻力(每幀 ×(1 − 此值);S3 GroundPositionHorizonAirResist 0.3)")]
        public float GroundResist;

        [Tooltip("撞牆反彈係數(S3 HitVerticalWallReboundMaxRate 0.7)")]
        public float WallRebound;

        [Tooltip("落地瞬間保留的水平速度比例([EST] 直接砸地不會滑很遠)")]
        public float LandingKeep;

        [Tooltip("沒找到目標、也沒碰到地形時,空中幾 F 後自爆([EST] JP「空中 150F?」)")]
        public int AirborneTimeoutFrames;

        [Header("散彈(追蹤爆炸後,S3 SplashAroundParam Num 8)")]
        [Tooltip("散彈數(S3 8)")]
        public int DropletCount;

        [Tooltip("散彈仰角(度;S3 PitchMin = PitchMax = 65)")]
        public float DropletPitch;

        [Tooltip("散彈初速下限(u/F;S3 VelocityMin 0.42)")]
        public float DropletSpeedMin;

        [Tooltip("散彈初速上限(u/F;S3 VelocityMax 0.48)")]
        public float DropletSpeedMax;

        [Tooltip("散彈最長飛行幀數([EST] 保險:落不到地形就消失)")]
        public int DropletMaxFrames;

        // S3 v11.3.0、0 AP
        public static TorpedoParams S3 => new TorpedoParams
        {
            Throw = S3ThrowParams.Bomb(1.4f),
            FindStartFrame = 6,
            SearchRadius = 9f,
            SearchAbove = 2.5f,
            SearchBelow = 5f,
            RotateFrames = 25,
            HoldFrames = 20,
            ChaseSpeed = 0.22f,
            ChaseMaxFrames = 150,
            Hp = 20f,
            BurstFrame = 30,
            WarningRestFrame = 30,
            GroundResist = 0.3f,
            WallRebound = 0.7f,
            LandingKeep = 0.6f,
            AirborneTimeoutFrames = 150,
            DropletCount = 8,
            DropletPitch = 65f,
            DropletSpeedMin = 0.42f,
            DropletSpeedMax = 0.48f,
            DropletMaxFrames = 120,
        };
    }

    // Torpedo 純邏輯(research 03 §2.14、DESIGN_REFERENCE §4.3):
    //   空中:投出 FindStartFrame 後開始搜尋水平 9.0、上下窗內最近的敵人 → 停在空中變身(轉向 25F + 20F)→
    //         朝目標「當時的位置」直線衝刺(不再追蹤),碰到地形/敵人就爆炸 60(2.6)/ 35(6.0)+ 8 顆散彈(各 12 / 2.6)。
    //   先碰到地形:失去搜尋;落地滾 30F 爆炸(只有 60/35,沒有散彈);變身中 HP 20,被打壞不爆不塗。
    public static class TorpedoModel
    {
        // 變身總幀數(開始變身 → 開始衝刺)
        public static int TransformFrames(in TorpedoParams p)
        {
            return p.RotateFrames + p.HoldFrames;
        }

        // 這一幀開始可以搜尋了嗎(frame = 投出後第幾幀,從 1 起算)
        public static bool CanSearch(in TorpedoParams p, int frame)
        {
            return frame >= p.FindStartFrame;
        }

        // offset = 目標位置 − Torpedo 位置:水平 ≤ SearchRadius 且 −SearchBelow ≤ dy ≤ SearchAbove
        public static bool InSearchWindow(in TorpedoParams p, Vector3 offset)
        {
            if (offset.y > p.SearchAbove || offset.y < -p.SearchBelow)
            {
                return false;
            }
            return offset.x * offset.x + offset.z * offset.z <= p.SearchRadius * p.SearchRadius;
        }

        // 第 index 顆散彈的初速(u/F):水平方向等分一圈(yawOffsetDeg 整體旋轉),仰角 65°,速度在上下限間(speed01)
        public static Vector3 DropletVelocity(in TorpedoParams p, int index, float yawOffsetDeg, float speed01)
        {
            int count = Mathf.Max(1, p.DropletCount);
            float yaw = (yawOffsetDeg + 360f * index / count) * Mathf.Deg2Rad;
            float pitch = p.DropletPitch * Mathf.Deg2Rad;
            float speed = Mathf.Lerp(p.DropletSpeedMin, p.DropletSpeedMax, Mathf.Clamp01(speed01));
            float horizontal = Mathf.Cos(pitch) * speed;
            return new Vector3(Mathf.Sin(yaw) * horizontal, Mathf.Sin(pitch) * speed, Mathf.Cos(yaw) * horizontal);
        }

        // 散彈在平地上的落點水平距離(u):從高 height 以 DropletVelocity 飛出(通用阻力與重力)。測試/視覺驗算用
        public static float DropletFlatReach(in TorpedoParams p, float speed01, float height)
        {
            Vector3 v = DropletVelocity(p, 0, 0f, speed01);
            Vector3 position = new Vector3(0f, height, 0f);
            for (int i = 0; i < p.DropletMaxFrames; i++)
            {
                S3ThrowModel.StepAir(p.Throw, ref position, ref v);
                if (position.y <= 0f)
                {
                    break;
                }
            }
            return new Vector2(position.x, position.z).magnitude;
        }
    }
}

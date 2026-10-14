using System.Collections;
using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Combat;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // Autobomb 驗收(S3 v11.3.0,research 03 §2.8),試射場,全走真實路徑(意圖 → ThrowController → Autobomb → Explosion):
    //   (a) 換裝:手持模型、bot 掛勾;耗墨 55%、墨量鎖 85F
    //   (b) 追蹤:落地 5.5 m 外的假人 → 搜尋圈內立即鎖定 → 加速 0.003 u/F² 到 0.09 u/F(5.4 m/s,約 30F)→
    //       進入近距半徑 2.85 後 60F 爆炸 → 180;走路有腳步聲
    //   (c) 沒有目標:落地 60F(1.0 s)爆炸;爆炸塗地 r 4.0
    //   (d) 不會爬牆:目標在牆後 → 撞牆原地踏步 → 追滿 150F → 60F 後爆炸;牆擋住爆炸視線,牆後假人無傷
    //   (e) 炸彈共通規則:追蹤中走到敵方 Splash Wall 立刻在牆前爆炸(不等倒數),牆吃 180 × 2.0,牆後假人被擋住
    [AutoTest(Scene = AutoTestAttribute.TrainingScene, Timeout = 120f, Order = 233)]
    public sealed class AutobombAutoTest : BombTestBase
    {
        private const string Label = "Autobomb";

        public static void Run()
        {
            Launch<AutobombAutoTest>();
        }

        private IEnumerator Start()
        {
            if (!Prepare("autobomb", out AutobombConfig config))
            {
                yield break;
            }
            yield return MoveTo(new Vector3(-20f, 0.1f, 20f), Vector3.right);
            yield return CheckHeldModel(config, Label);
            CheckBotUsage(config, Label);
            yield return WaitSeconds(2.2f);

            yield return Seek(config);
            yield return NoTarget();
            yield return WallBlock(config);
            yield return BarrierDetonate();

            yield return BotStyleThrow(config, Label, new Vector3(-4f, 0.1f, -17f), Vector3.left, 9f,
                () => Autobomb.ExplosionCount, 6f);
            CheckNoExceptions(Label);
            Debug.Log($"[AUTOTEST] INFO {Label} 量測期間最大幀時間 {MaxDeltaTime * 1000f:0.0} ms");
            Cleanup();
            Finish();
        }

        private IEnumerator Seek(AutobombConfig config)
        {
            TargetDummy dummy = SpawnDummy(new Vector3(-10f, 0f, -8f));
            yield return MoveTo(new Vector3(-10f, 0.1f, -20f), Vector3.forward);
            Tank.RefillFull();
            yield return AimToLand(new Vector3(-10f, 0f, -13.5f));
            Vector3 predicted = Thrower.PredictedLanding;
            float lands = Autobomb.LastLandTime;
            int explosions = Autobomb.ExplosionCount;
            int steps = Autobomb.FootstepCount;
            yield return Release();
            float spent = LastInkBefore - LastInkAfter;
            Check($"{Label} 耗墨 55%", LastThrew && Mathf.Abs(spent - 0.55f) < 0.005f, $"扣墨 {spent:0.000}");

            // 盯著場上那顆:記錄狀態轉換時間與走速
            Autobomb live = null;
            float chaseStart = -1f;
            float reachedMax = -1f;
            float maxSpeed = 0f;
            float lockStart = LastThrowTime;
            float unlockedAt = -1f;
            float level = Tank.Normalized;
            float deadline = Time.time + 8f;
            while (Autobomb.ExplosionCount == explosions && Time.time < deadline)
            {
                yield return null;
                Track();
                if (unlockedAt < 0f)
                {
                    if (Tank.Normalized > level + 1e-5f)
                    {
                        unlockedAt = Time.time;
                    }
                    level = Tank.Normalized;
                }
                if (live == null || !live.IsActive)
                {
                    live = FindLive();
                    continue;
                }
                if (live.State == AutobombState.Chasing && chaseStart < 0f)
                {
                    chaseStart = Time.time;
                }
                maxSpeed = Mathf.Max(maxSpeed, live.Speed);
                if (reachedMax < 0f && live.Speed >= 5.4f * 0.95f)
                {
                    reachedMax = Time.time;
                }
            }
            yield return Frames(2);
            float lockSeconds = unlockedAt > 0f ? unlockedAt - lockStart : -1f;
            Check($"{Label} 墨量鎖 85F = 1.417 s", Mathf.Abs(lockSeconds - 85f / 60f) <= LockTolerance,
                $"丟出後 {lockSeconds:0.000} s 開始回墨(容許 ±{LockTolerance:0.000})");
            Check($"{Label} 落地點 = 預覽(同一個 60 Hz 步進、同半徑 sphere cast)",
                Autobomb.LastLandTime > lands && Flat(Autobomb.LastLandPoint, predicted) < 0.15f,
                $"落地 {Autobomb.LastLandPoint} 預覽 {predicted} 誤差 {Flat(Autobomb.LastLandPoint, predicted):0.00} m");
            float findDelay = Autobomb.LastFindTime - Autobomb.LastLandTime;
            Check($"{Label} 落地搜尋:鎖定 12.5 m 內最近的敵人(假人)",
                Autobomb.LastLandTime > lands && ReferenceEquals(Autobomb.LastTarget, dummy) && findDelay >= 0f
                && findDelay <= 0.2f,
                $"落地 {Autobomb.LastLandPoint} 距假人 {Flat(Autobomb.LastLandPoint, dummy.transform.position):0.00} m 鎖定延遲 {findDelay:0.000} s");
            float ramp = reachedMax - chaseStart;
            Check($"{Label} 追蹤加速到 0.09 u/F(5.4 m/s),約 29F 到 95%",
                Mathf.Abs(Autobomb.LastMaxSpeed - 5.4f) < 0.05f && maxSpeed <= 5.41f && chaseStart > 0f
                && Mathf.Abs(ramp - 29f / 60f) <= 0.08f,
                $"最高 {Autobomb.LastMaxSpeed:0.000} m/s(輪詢 {maxSpeed:0.000})追蹤開始到 95% 最高速 {ramp:0.000} s");
            float warn = Autobomb.LastExplosionTime - Autobomb.LastWarningTime;
            float distance = Closest(dummy, Autobomb.LastExplosionCenter);
            Check($"{Label} 追到近距半徑內 → 60F(1.0 s)後爆炸 → 180",
                Autobomb.LastWarnReason == AutobombWarnReason.Approach && Mathf.Abs(warn - 1f) <= 0.05f
                && distance <= 2.85f && Mathf.Abs(dummy.PopupTotal - 180f) < 0.01f,
                $"倒數原因 {Autobomb.LastWarnReason} 倒數 {warn:0.000} s 爆心距假人 {distance:0.00} m 傷害 {dummy.PopupTotal}");
            Check($"{Label} 走路有腳步聲", Autobomb.FootstepCount - steps >= 3,
                $"腳步 {Autobomb.FootstepCount - steps} 次(追蹤 {Autobomb.LastChaseFrames}F)");
            Remove(dummy);
        }

        private IEnumerator NoTarget()
        {
            yield return MoveTo(new Vector3(-20f, 0.1f, 8f), Vector3.forward);
            Tank.RefillFull();
            yield return AimToLand(new Vector3(-20f, 0f, 15f));
            int explosions = Autobomb.ExplosionCount;
            yield return Release();
            yield return WaitUntil(() => Autobomb.ExplosionCount > explosions, 4f);
            yield return Frames(3);
            float fuse = Autobomb.LastExplosionTime - Autobomb.LastLandTime;
            Check($"{Label} 沒有目標:落地 60F(1.0 s)爆炸",
                Autobomb.LastWarnReason == AutobombWarnReason.NoTarget && Autobomb.LastLandToExplodeFrames == 60
                && Mathf.Abs(fuse - 1f) <= 0.05f,
                $"原因 {Autobomb.LastWarnReason} 模擬 {Autobomb.LastLandToExplodeFrames}F 實測 {fuse:0.000} s");
            Vector3 blast = Autobomb.LastExplosionCenter;
            int ring = RingInked(blast, 3f);
            int inner = RingInked(blast, 1.5f);
            Check($"{Label} 爆炸塗地 r 4.0", ring >= 6 && inner == 8, $"r 1.5 圓周 {inner}/8、r 3.0 圓周 {ring}/8 有墨");
        }

        private IEnumerator WallBlock(AutobombConfig config)
        {
            // 牆(z 1.75 ~ 2.25)擋在落點與假人之間
            SpawnWall(new Vector3(-10f, 1.5f, 2f), new Vector3(12f, 3f, 0.5f), "AutobombTestWall");
            TargetDummy dummy = SpawnDummy(new Vector3(-10f, 0f, 5.5f));
            yield return MoveTo(new Vector3(-10f, 0.1f, -11f), Vector3.forward);
            Tank.RefillFull();
            yield return AimToLand(new Vector3(-10f, 0f, -3.5f));
            int explosions = Autobomb.ExplosionCount;
            yield return Release();
            yield return WaitUntil(() => Autobomb.ExplosionCount > explosions, 6f);
            yield return Frames(3);
            AutobombParams p = config.S3;
            int expectedFrames = p.FindRotateFrames + p.ChaseFrames + p.ChaseBurstWaitFrames;
            float fuse = Autobomb.LastExplosionTime - Autobomb.LastLandTime;
            Vector3 blast = Autobomb.LastExplosionCenter;
            Check($"{Label} 不會爬牆:撞牆原地踏步、追滿 150F → 60F 後爆炸",
                ReferenceEquals(Autobomb.LastTarget, dummy) && Autobomb.LastWarnReason == AutobombWarnReason.ChaseTimeout
                && Autobomb.LastChaseFrames == p.ChaseFrames && Autobomb.LastBlockedFrames >= 60 && blast.z < 1.75f
                && Mathf.Abs(Autobomb.LastLandToExplodeFrames - expectedFrames) <= 2
                && Mathf.Abs(fuse - expectedFrames / 60f) <= 0.08f,
                $"原因 {Autobomb.LastWarnReason} 追蹤 {Autobomb.LastChaseFrames}F 撞牆 {Autobomb.LastBlockedFrames}F " +
                $"落地到爆炸 {Autobomb.LastLandToExplodeFrames}F / {fuse:0.00} s(期望 {expectedFrames}F)爆心 {blast}");
            float distance = Closest(dummy, blast);
            Check($"{Label} 牆擋住爆炸視線:牆後假人無傷(無牆時 {config.Blast.Evaluate(distance)})",
                !dummy.PopupVisible && distance < config.Blast.FarRadius,
                $"最近點距離 {distance:0.00} m 受擊={dummy.PopupVisible}");
            Remove(dummy);
        }

        private IEnumerator BarrierDetonate()
        {
            // 敵方牆(z 1.8 ~ 2.2,擋敵方攻擊)擋在落點與假人之間;Autobomb 追假人時走進牆
            Team enemyTeam = Player.Team == Team.Alpha ? Team.Bravo : Team.Alpha;
            TestDeployable wall = TestDeployable.Create("AutobombTest_EnemyWall", new Vector3(-20f, 1.5f, 2f),
                new Vector3(6f, 3f, 0.4f));
            wall.Setup(null, enemyTeam, 800f, TargetClass.SplashWall, false, true);
            TargetDummy dummy = SpawnDummy(new Vector3(-20f, 0f, 5.5f));
            yield return MoveTo(new Vector3(-20f, 0.1f, -11f), Vector3.forward);
            Tank.RefillFull();
            yield return AimToLand(new Vector3(-20f, 0f, -3.5f));
            int explosions = Autobomb.ExplosionCount;
            int detonations = Autobomb.DetonatorHitCount;
            yield return Release();
            yield return WaitUntil(() => Autobomb.ExplosionCount > explosions, 6f);
            yield return Frames(3);
            Vector3 blast = Autobomb.LastExplosionCenter;
            Check($"{Label} 追蹤中走到敵方 Splash Wall 立刻在牆前爆炸(不等 60F 倒數)",
                ReferenceEquals(Autobomb.LastTarget, dummy) && Autobomb.DetonatorHitCount == detonations + 1
                && Autobomb.LastChaseFrames < 150 && blast.z < 1.8f && blast.z > 1.2f,
                $"追蹤 {Autobomb.LastChaseFrames}F 後爆炸,爆心 {blast}(牆面 z = 1.8)");
            float distance = Closest(dummy, blast);
            Check($"{Label} 敵方 Splash Wall 吃 180 × 2.0、牆後假人被擋住無傷",
                Mathf.Abs(wall.Hp - 440f) < 0.01f && !dummy.PopupVisible && distance < 6.5f,
                $"牆 HP {wall.Hp}(期望 440)假人最近點 {distance:0.00} m 受擊={dummy.PopupVisible}");
            Remove(dummy);
            Destroy(wall.gameObject);
            yield return Frames(2);
        }

        private static Autobomb FindLive()
        {
            foreach (Autobomb bomb in FindObjectsByType<Autobomb>(FindObjectsSortMode.None))
            {
                if (bomb.IsActive)
                {
                    return bomb;
                }
            }
            return null;
        }
    }
}

using System.Collections;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // Angle Shooter 驗收(S3 v11.3.0,research 03 §2.13),試射場,全走真實路徑(意圖 → ThrowController → AngleShooter):
    //   (a) 換裝:手持模型、ThrowStyle.Straight、bot 掛勾
    //   (b) 射程:直線 6.3 u/F × 7F = 44.1 m(第 7 幀走完)、終點 = 預覽;耗墨 40%、墨量鎖 50F;線段殘留 90F(1.5 s)
    //   (c) 牆面反彈:45° 入射 → 45° 反射(鏡射)、反彈點塗 2.6;預覽含反彈
    //   (d) 地面反彈:入射 20° → 出射 2°(× 0.1,貼地前進)
    //   (e) 直擊假人 40、線段停在假人、腳下塗 2.0
    //   (f) 標記:丟出 1 秒後才走到線上的敵人被標記 300 − 60F;線段消失後不再標記;直擊敵人角色 40 並標記 300F
    //   (g) bot 用法(模擬 BotBrain:看目標腳下 + 0.3、按住 BotHoldSeconds)直擊 12 m 外的目標
    [AutoTest(Scene = AutoTestAttribute.TrainingScene, Timeout = 120f, Order = 243)]
    public sealed class AngleShooterAutoTest : ThrowableTestBase
    {
        private const string Label = "Angle Shooter";

        public static void Run()
        {
            Launch<AngleShooterAutoTest>();
        }

        private IEnumerator Start()
        {
            if (!Prepare("angle_shooter", out AngleShooterConfig config))
            {
                yield break;
            }
            yield return MoveTo(new Vector3(-6f, 0.1f, 20f), Vector3.back);
            yield return CheckHeldModel(config, Label);
            Vector2 range = config.BotRange;
            Check($"{Label} ThrowStyle.Straight、射程 44.1 m;bot 掛勾:直擊敵人",
                config.Style == ThrowStyle.Straight && Mathf.Abs(config.Range - 44.1f) < 1e-3f
                && config.BotUsage == BotSubUsage.ThrowAtEnemy && range.y > range.x && config.BotHoldSeconds(10f) > 0f,
                $"樣式 {config.Style} 射程 {config.Range:0.00} m bot 距離 {range.x:0}~{range.y:0} m");
            yield return WaitSeconds(1.8f);

            yield return RangeAndTail(config);
            yield return WallBounce();
            yield return GroundBounce();
            yield return DirectHitDummy();
            yield return MarkingCases();
            yield return BotUse(config);

            CheckNoExceptions(Label);
            Debug.Log($"[AUTOTEST] INFO {Label} 量測期間最大幀時間 {MaxDeltaTime * 1000f:0.0} ms");
            Cleanup();
            Finish();
        }

        private IEnumerator Fire(Vector3 standAt, Vector3 facing, float pitch)
        {
            yield return MoveTo(standAt, facing);
            yield return WaitSeconds(0.55f);
            Tank.RefillFull();
            AimYawPitch(facing, pitch);
            Intent.Sub = true;
            yield return Frames(3);
        }

        // ── (b) 射程、墨、殘留 ──
        private IEnumerator RangeAndTail(AngleShooterConfig config)
        {
            yield return Fire(new Vector3(-24f, 0.1f, -20f), Vector3.right, 0f);
            Vector3 predicted = Thrower.PredictedLanding;
            int finished = AngleShooter.FinishedCount;
            float releaseBefore = AngleShooter.LastReleaseTime;
            yield return Release();
            float spent = LastInkBefore - LastInkAfter;
            Check($"{Label} 耗墨 40%", LastThrew && Mathf.Abs(spent - 0.4f) < 0.005f, $"扣墨 {spent:0.000}");
            float lockSeconds = -1f;
            yield return MeasureInkLock(50f / 60f, s => lockSeconds = s);
            Check($"{Label} 墨量鎖 50F = 0.833 s", Mathf.Abs(lockSeconds - 50f / 60f) <= LockTolerance,
                $"丟出後 {lockSeconds:0.000} s 開始回墨(容許 ±{LockTolerance:0.000})");
            yield return WaitUntil(() => AngleShooter.FinishedCount > finished, 1f);
            float flight = AngleShooter.LastEndTime - AngleShooter.LastFireTime;
            Vector3 origin = AngleShooter.LastOrigin;
            Vector3 end = AngleShooter.LastEndPoint;
            Check($"{Label} 直線 6.3 u/F × 7F = 44.1 m(第 7 幀走完,不下墜)",
                AngleShooter.LastEndFrame == 7 && AngleShooter.LastBounces == 0
                && Mathf.Abs(AngleShooter.LastPathLength - 44.1f) < 0.02f && Mathf.Abs(end.y - origin.y) < 0.02f
                && flight < 7f / 60f + Mathf.Max(0.05f, MaxDeltaTime * 2f),
                $"第 {AngleShooter.LastEndFrame}F 結束、反彈 {AngleShooter.LastBounces} 次、路徑 {AngleShooter.LastPathLength:0.00} m、" +
                $"高度 {origin.y:0.00} → {end.y:0.00}、飛行 {flight:0.000} s");
            Check($"{Label} 直線導引(預覽)終點 = 實際終點", Vector3.Distance(predicted, end) < 0.05f,
                $"預覽 {predicted} 實際 {end}");
            // 殘留期間(每幀線段標記查詢 + LineRenderer 更新)的主執行緒配置
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 40; i++)
            {
                yield return null;
            }
            long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
            Debug.Log($"[AUTOTEST] INFO {Label} 線段殘留中 40 幀主執行緒配置 {allocated} bytes");
            yield return WaitUntil(() => AngleShooter.LastReleaseTime > releaseBefore, 2.5f);
            float tail = AngleShooter.LastReleaseTime - AngleShooter.LastEndTime;
            Check($"{Label} 線段飛完後殘留 90F(1.5 s)", Mathf.Abs(tail - 1.5f) <= 0.05f, $"殘留 {tail:0.000} s");
        }

        // ── (c) 牆面反彈 ──
        private IEnumerator WallBounce()
        {
            // 東西向的牆,面朝 −z(z = 2.75);從西南往東北 45° 射
            SpawnWall(new Vector3(-10f, 1.5f, 3f), new Vector3(20f, 3f, 0.5f), "AngleTestWall");
            Vector3 heading = new Vector3(1f, 0f, 1f).normalized;
            yield return Fire(new Vector3(-20f, 0.1f, -5f), heading, 0f);
            Vector3 predicted = Thrower.PredictedLanding;
            int finished = AngleShooter.FinishedCount;
            yield return Release();
            yield return WaitUntil(() => AngleShooter.FinishedCount > finished, 1f);
            yield return Frames(2);
            Vector3 inDir = AngleShooter.LastFirstBounceIn;
            Vector3 outDir = AngleShooter.LastFirstBounceOut;
            Vector3 normal = AngleShooter.LastFirstBounceNormal;
            Vector3 mirror = inDir - 2f * Vector3.Dot(inDir, normal) * normal;
            float error = Vector3.Angle(outDir, mirror);
            float incidence = 90f - Vector3.Angle(-inDir, normal);
            Vector3 bounce = AngleShooter.LastFirstBouncePoint;
            Check($"{Label} 牆面鏡射:入射 45° → 反射 45°(S3 ReboundWallReflectAngleRate 1.0),路徑總長不變",
                AngleShooter.LastBounces >= 1 && Mathf.Abs(normal.z + 1f) < 0.01f && error < 0.2f
                && Mathf.Abs(incidence - 45f) < 1.5f && Mathf.Abs(AngleShooter.LastPathLength - 44.1f) < 0.05f,
                $"入射 {incidence:0.0}° 法線 {normal} 反射誤差 {error:0.00}° 路徑 {AngleShooter.LastPathLength:0.00} m");
            Check($"{Label} 反彈點塗墨 r 2.6(反彈點正下方地面是自隊墨)", GroundInked(bounce) && RingInked(bounce, 1.5f) >= 4,
                $"反彈點 {bounce} 正下方={GroundInked(bounce)} 半徑 1.5 m 圓周 {RingInked(bounce, 1.5f)}/8");
            Check($"{Label} 預覽含反彈:終點 = 實際", Vector3.Distance(predicted, AngleShooter.LastEndPoint) < 0.05f,
                $"預覽 {predicted} 實際 {AngleShooter.LastEndPoint}");
        }

        // ── (d) 地面反彈 ──
        private IEnumerator GroundBounce()
        {
            yield return Fire(new Vector3(-24f, 0.1f, -12f), Vector3.right, -20f);
            int finished = AngleShooter.FinishedCount;
            yield return Release();
            yield return WaitUntil(() => AngleShooter.FinishedCount > finished, 1f);
            Vector3 inDir = AngleShooter.LastFirstBounceIn;
            Vector3 outDir = AngleShooter.LastFirstBounceOut;
            float inAngle = Mathf.Asin(Mathf.Clamp(-inDir.y, -1f, 1f)) * Mathf.Rad2Deg;
            float outAngle = Mathf.Asin(Mathf.Clamp(outDir.y, -1f, 1f)) * Mathf.Rad2Deg;
            Check($"{Label} 地面反彈:出射角 = 入射角 × 0.1(S3 ReboundGroundReflectAngleRate,貼地前進)",
                AngleShooter.LastFirstBounceNormal.y > 0.95f && inAngle > 15f && Mathf.Abs(outAngle - inAngle * 0.1f) < 0.05f,
                $"入射 {inAngle:0.00}° → 出射 {outAngle:0.00}°;反彈 {AngleShooter.LastBounces} 次、路徑 {AngleShooter.LastPathLength:0.00} m");
        }

        // ── (e) 直擊假人 ──
        private IEnumerator DirectHitDummy()
        {
            TargetDummy dummy = SpawnDummy(new Vector3(-10f, 0f, 8f));
            yield return Fire(new Vector3(-24f, 0.1f, 8f), Vector3.right, 0f);
            Aim.Forward = (dummy.transform.position + Vector3.up * 1f - Muzzle.position).normalized;
            yield return Frames(2);
            Vector3 predicted = Thrower.PredictedLanding;
            int hits = AngleShooter.DirectHitCount;
            int finished = AngleShooter.FinishedCount;
            yield return Release();
            yield return WaitUntil(() => AngleShooter.FinishedCount > finished, 1f);
            yield return Frames(2);
            float dealt = dummy.PopupVisible ? dummy.PopupTotal : 0f;
            Vector3 end = AngleShooter.LastEndPoint;
            Check($"{Label} 直擊假人 40、線段停在假人身上(預覽也停在那裡)",
                AngleShooter.DirectHitCount == hits + 1 && ReferenceEquals(AngleShooter.LastDirectTarget, dummy)
                && Mathf.Abs(dealt - 40f) < 0.01f && Closest(dummy, end) < 0.35f && Vector3.Distance(predicted, end) < 0.5f
                && AngleShooter.LastPathLength < 15f,
                $"直擊 {AngleShooter.DirectHitCount - hits} 次 假人 {dealt} 終點離假人 {Closest(dummy, end):0.00} m 預覽 {predicted}");
            Vector3 under = new Vector3(end.x, 0f, end.z);
            Check($"{Label} 直擊時目標腳下塗 2.0", GroundInked(under) && RingInked(under, 1.4f) >= 5,
                $"腳下={GroundInked(under)} 半徑 1.4 m 圓周 {RingInked(under, 1.4f)}/8");
            Remove(dummy);
        }

        // ── (f) 標記 ──
        private IEnumerator MarkingCases()
        {
            // 線沿 z = 12 往 +x;敵人先站在線外 2 m,丟出 1 秒後走到線上
            Inkling walker = SpawnEnemy("LineEnemy_Walker", new Vector3(-12f, 0.1f, 14.2f), true);
            Inkling late = SpawnEnemy("LineEnemy_Late", new Vector3(-6f, 0.1f, 15f), true);
            yield return Fire(new Vector3(-24f, 0.1f, 12f), Vector3.right, 0f);
            int finished = AngleShooter.FinishedCount;
            yield return Release();
            float fired = AngleShooter.LastFireTime;
            yield return WaitUntil(() => AngleShooter.FinishedCount > finished, 1f);
            Vector3 origin = AngleShooter.LastOrigin;
            yield return Frames(2);
            bool markedBefore = Marking.IsMarked(walker, Player.Team);
            while (Time.time - fired < 1f)
            {
                yield return null;
                Track();
            }
            walker.Teleport(new Vector3(-12f, 0.1f, origin.z), 0f, false);
            yield return Frames(3);
            float remaining = Marking.Remaining(walker, Player.Team);
            float total = remaining + (Time.time - fired);
            Check($"{Label} 丟出 1 秒後才碰到線的敵人:標記從丟出起算 300F(約剩 4 秒)",
                !markedBefore && Marking.IsMarked(walker, Player.Team)
                && Mathf.Abs(total - 5f) < 0.06f && remaining < 4.05f,
                $"碰線前標記={markedBefore};剩 {remaining:0.00} s(丟出起算合計 {total:0.00} s)");
            // 線段消失(丟出後 0.117 + 1.5 s)後才走上去:不標記
            while (Time.time - fired < 1.8f)
            {
                yield return null;
                Track();
            }
            late.Teleport(new Vector3(-6f, 0.1f, origin.z), 0f, false);
            yield return Frames(4);
            Check($"{Label} 線段消失後走上原位置不會被標記", !Marking.IsMarked(late, Player.Team),
                $"標記={Marking.IsMarked(late, Player.Team)}");
            RemoveEnemies();

            // 直擊敵人角色:40 傷 + 標記 300F
            Inkling target = SpawnEnemy("LineEnemy_Direct", new Vector3(-12f, 0.1f, 12f), false);
            yield return Fire(new Vector3(-24f, 0.1f, 12f), Vector3.right, 0f);
            Aim.Forward = (target.transform.position + Vector3.up * 0.8f - Muzzle.position).normalized;
            yield return Frames(2);
            int hits = AngleShooter.DirectHitCount;
            yield return Release();
            yield return WaitUntil(() => AngleShooter.DirectHitCount > hits, 1f);
            yield return Frames(2);
            var health = target.GetComponent<InklingHealth>();
            float left = Marking.Remaining(target, Player.Team);
            float sinceFire = Time.time - AngleShooter.LastFireTime;
            Check($"{Label} 直擊敵人角色 40(血量 100 → 60)並標記 300F",
                ReferenceEquals(AngleShooter.LastDirectTarget, health) && Mathf.Abs(health.Damage - 40f) < 0.01f
                && Mathf.Abs(left + sinceFire - 5f) < 0.06f,
                $"受傷 {health.Damage} 標記剩 {left:0.00} s(丟出起算 {left + sinceFire:0.00} s)");
            RemoveEnemies();
        }

        // ── (g) bot 用法 ──
        private IEnumerator BotUse(AngleShooterConfig config)
        {
            yield return WaitSeconds(0.6f);
            TargetDummy dummy = SpawnDummy(new Vector3(-12f, 0f, 18f));
            int hits = AngleShooter.DirectHitCount;
            bool threw = false;
            float hold = 0f;
            yield return BotAimThrow(config, new Vector3(-24f, 0.1f, 18f), Vector3.right, 12f, (t, h) =>
            {
                threw = t;
                hold = h;
            });
            yield return WaitUntil(() => AngleShooter.DirectHitCount > hits, 1f);
            yield return Frames(2);
            float dealt = dummy.PopupVisible ? dummy.PopupTotal : 0f;
            Check($"{Label} bot 用法(模擬 BotBrain:看目標腳下 + 0.3、按住 BotHoldSeconds)直擊 12 m 外的目標",
                threw && ReferenceEquals(AngleShooter.LastDirectTarget, dummy) && Mathf.Abs(dealt - 40f) < 0.01f,
                $"投出={threw} 按住 {hold:0.00} s 假人受傷 {dealt}");
            Remove(dummy);
        }
    }
}

using System.Collections;
using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Combat;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // Fizzy Bomb 驗收(S3 v11.3.0,research 03 §2.6),試射場,全走真實路徑(意圖 → ThrowController 蓄力 → FizzyBomb → Explosion → 假人):
    //   (a) 換裝:手持模型(段數指示燈)、bot 掛勾(近 → 第 2 段、遠 → 第 3 段;瞄準解 S3 彈道)
    //   (b) 蓄力段數:只按住 → 5F 準備 + 40F = 第 2 段、+ 120F = 第 3 段;手上指示燈與升段音
    //   (c) 第 3 段:耗墨 60%、墨量鎖 85F;爆 3 次:第一次著地 30F 後、之後每次彈跳再著地 8F 後;
    //       每爆 50 / 35,半徑 1.6/3.8 → 1.95/4.5 → 2.6/5.45(每爆在該爆的近/遠邊界之間擺假人,量傷害)
    //   (d) 第 1 段:只爆 1 次;飛行軌跡每 2.2 m 落墨成帶(半寬 1.2);第 1 爆半徑 1.6 / 3.8 / 範圍外 0
    //   (e) 連打:跳躍鍵每按一次折抵 8F(每幀最多兌現 2.5F)、搖桿甩出 11F → 約 1 秒內蓄滿
    //   (f) 炸彈共通規則:自隊 Splash Wall 穿過;碰到敵方 Splash Wall 立刻爆(牆吃 50 × 3.6),預覽也停在牆前
    //   (g) bot 用法(模擬 BotBrain:BotAimPoint + BotHoldSeconds)丟中 10 m 外的目標
    [AutoTest(Scene = AutoTestAttribute.TrainingScene, Timeout = 120f, Order = 241)]
    public sealed class FizzyBombAutoTest : ThrowableTestBase
    {
        private const string Label = "Fizzy Bomb";

        private static readonly float[] NearAt = { 1.3f, 1.75f, 2.35f };
        private static readonly float[] FarAt = { 3.3f, 4.2f, 5.1f };

        public static void Run()
        {
            Launch<FizzyBombAutoTest>();
        }

        private IEnumerator Start()
        {
            if (!Prepare("fizzy_bomb", out FizzyBombConfig config))
            {
                yield break;
            }
            Random.InitState(20260928);
            Intent.JumpHold = false;
            yield return MoveTo(new Vector3(-6f, 0.1f, 20f), Vector3.back);
            yield return CheckHeldModel(config, Label);
            var indicator = Visual != null && Visual.HeldSubItem != null
                ? Visual.HeldSubItem.GetComponent<FizzyChargeIndicator>()
                : null;
            Vector2 range = config.BotRange;
            float holdNear = config.BotHoldSeconds(5f);
            float holdFar = config.BotHoldSeconds(12f);
            Check($"{Label} bot 掛勾:丟向敵人;近距離蓄第 2 段、遠距離蓄第 3 段;手持模型有段數指示燈",
                config.BotUsage == BotSubUsage.ThrowAtEnemy && range.x > 0f && range.y > range.x
                && config.LevelAt(holdNear * 60f - 5f) == 2 && config.LevelAt(holdFar * 60f - 5f) == 3 && indicator != null,
                $"距離 {range.x:0}~{range.y:0} m;5 m 按住 {holdNear:0.00} s、12 m 按住 {holdFar:0.00} s;指示燈={(indicator != null)}");
            yield return WaitSeconds(1.5f);

            yield return FullCharge(config, indicator);
            yield return Uncharged(config);
            yield return Mash(config);
            yield return DetonatorRule();
            yield return BotUse(config);

            CheckNoExceptions(Label);
            Debug.Log($"[AUTOTEST] INFO {Label} 量測期間最大幀時間 {MaxDeltaTime * 1000f:0.0} ms");
            Cleanup();
            Finish();
        }

        // ── (b)(c) 只按住蓄到第 3 段,三爆 ──
        private IEnumerator FullCharge(FizzyBombConfig config, FizzyChargeIndicator indicator)
        {
            yield return MoveTo(new Vector3(-22f, 0.1f, -21f), Vector3.right);
            yield return WaitSeconds(0.2f);
            Tank.RefillFull();
            AimYawPitch(Vector3.right, -12f);
            Intent.Sub = true;
            float pressed = Time.time;
            int cuesBefore = indicator != null ? indicator.LevelCues : 0;
            float[] marks = { 0.70f, 0.80f, 2.0f, 2.2f };
            int[] levels = new int[marks.Length];
            float[] charges = new float[marks.Length];
            float[] held = new float[marks.Length];
            for (int i = 0; i < marks.Length; i++)
            {
                while (Time.time - pressed < marks[i])
                {
                    yield return null;
                    Track();
                }
                held[i] = (Time.time - pressed) * 60f;
                charges[i] = Thrower.SubChargeFrames;
                levels[i] = config.LevelAt(charges[i]);
            }
            int shown = indicator != null ? indicator.DisplayedLevel : 0;
            int cues = indicator != null ? indicator.LevelCues - cuesBefore : 0;
            bool chargeOk = true;
            for (int i = 0; i < marks.Length; i++)
            {
                chargeOk &= Mathf.Abs(charges[i] - Mathf.Min(120f, held[i] - 5f)) <= 2f;
            }
            Check($"{Label} 只按住:5F 準備後開始蓄;40F → 第 2 段、120F → 第 3 段(按住 0.7 / 0.8 / 2.0 / 2.2 s)",
                chargeOk && levels[0] == 1 && levels[1] == 2 && levels[2] == 2 && levels[3] == 3
                && Mathf.Abs(Thrower.SubCharge01 - 1f) < 1e-4f,
                $"按住 {held[0]:0}/{held[1]:0}/{held[2]:0}/{held[3]:0}F → 蓄 {charges[0]:0.0}/{charges[1]:0.0}/{charges[2]:0.0}/{charges[3]:0.0}F" +
                $" 段數 {levels[0]}/{levels[1]}/{levels[2]}/{levels[3]}");
            Check($"{Label} 手上罐子的段數指示燈與升段提示", shown == 3 && cues >= 2,
                $"指示燈段數 {shown} 升段提示 {cues} 次");

            int finished = FizzyBomb.FinishedCount;
            int bursts = FizzyBomb.BurstCount;
            yield return Release();
            float spent = LastInkBefore - LastInkAfter;
            Check($"{Label} 耗墨 60%", LastThrew && Mathf.Abs(spent - 0.6f) < 0.005f, $"扣墨 {spent:0.000}");
            // 每次著地(滾動、還沒爆下一爆)時,在「這一爆的近/遠邊界之間」擺假人;爆完就量、移除
            TargetDummy[] near = new TargetDummy[3];
            TargetDummy[] far = new TargetDummy[3];
            string[] results = new string[3];
            bool[] ok = new bool[3];
            float unlockedAt = -1f;
            float level = Tank.Normalized;
            float deadline = Time.time + 6f;
            while (Time.time < deadline && (FizzyBomb.FinishedCount == finished || unlockedAt < 0f))
            {
                yield return null;
                Track();
                if (unlockedAt < 0f && Tank.Normalized > level + 1e-5f)
                {
                    unlockedAt = Time.time;
                }
                level = Tank.Normalized;
                int done = FizzyBomb.BurstCount - bursts;
                for (int k = 0; k < 3; k++)
                {
                    if (done > k && near[k] != null && results[k] == null)
                    {
                        results[k] = Measure(config, k, near[k], far[k], out ok[k]);
                        Remove(near[k]);
                        Remove(far[k]);
                    }
                }
                FizzyBomb live = LiveOf<FizzyBomb>();
                if (live != null && live.State == FizzyState.Rolling && live.Bursts < 3 && near[live.Bursts] == null
                    && live.BurstRestFrames > 2)
                {
                    int k = live.Bursts;
                    Vector3 rest = live.RestEstimate;
                    Vector3 heading = new Vector3(1f, 0f, 0.3f * (k - 1)).normalized;
                    Vector3 side = Vector3.Cross(Vector3.up, heading);
                    near[k] = SpawnDummyAtDistance(rest, side, NearAt[k], 0f);
                    far[k] = SpawnDummyAtDistance(rest, -side, FarAt[k], 0f);
                }
            }
            float lockSeconds = unlockedAt > 0f ? unlockedAt - LastThrowTime : -1f;
            Check($"{Label} 墨量鎖 85F = 1.417 s", Mathf.Abs(lockSeconds - 85f / 60f) <= LockTolerance,
                $"丟出後 {lockSeconds:0.000} s 開始回墨(容許 ±{LockTolerance:0.000})");
            int first = FizzyBomb.LastFrameOf(0) - FizzyBomb.LastFirstLandFrame;
            int second = FizzyBomb.LastFrameOf(1) - FizzyBomb.LastLandFrameOf(1);
            int third = FizzyBomb.LastFrameOf(2) - FizzyBomb.LastLandFrameOf(2);
            Check($"{Label} 第 3 段 = 爆 3 次:第一次著地 30F 後第 1 爆,之後每次再著地 8F 後下一爆",
                FizzyBomb.FinishedCount == finished + 1 && FizzyBomb.LastLevel == 3 && FizzyBomb.LastBurstTotal == 3
                && first == 30 && second == 8 && third == 8,
                $"段數 {FizzyBomb.LastLevel} 爆 {FizzyBomb.LastBurstTotal} 次;著地 → 爆 {first}F / {second}F / {third}F");
            float hop1 = Flat(FizzyBomb.LastCenterOf(0), FizzyBomb.LastCenterOf(1));
            float hop2 = Flat(FizzyBomb.LastCenterOf(1), FizzyBomb.LastCenterOf(2));
            Check($"{Label} 每爆完往前彈跳一小段(S3 0.24~0.32 u/F、48~58°)", hop1 > 0.5f && hop1 < 4f && hop2 > 0.5f && hop2 < 4f,
                $"第 1→2 爆 {hop1:0.00} m、第 2→3 爆 {hop2:0.00} m");
            for (int k = 0; k < 3; k++)
            {
                Check($"{Label} 第 {k + 1} 爆 50 / 35(近 {FizzyBombModel.NearRadius(config.S3, k)}、遠 {FizzyBombModel.FarRadius(config.S3, k)})",
                    ok[k], results[k] ?? "沒擺到假人(沒偵測到這一爆的著地)");
            }
            for (int k = 0; k < 3; k++)
            {
                Remove(near[k]);
                Remove(far[k]);
            }
        }

        // 該爆的兩個假人:傷害 = 模型(量到實際爆心),且模型值剛好是 50(近)/ 35(遠)——擺在前一爆半徑之外
        private string Measure(FizzyBombConfig config, int k, TargetDummy near, TargetDummy far, out bool ok)
        {
            Vector3 center = FizzyBomb.LastCenterOf(k);
            ExplosionSpec spec = config.BlastAt(k);
            float dn = Closest(near, center);
            float df = Closest(far, center);
            float en = spec.Evaluate(dn);
            float ef = spec.Evaluate(df);
            float gn = near.PopupVisible ? near.PopupTotal : 0f;
            float gf = far.PopupVisible ? far.PopupTotal : 0f;
            float previousNear = k > 0 ? FizzyBombModel.NearRadius(config.S3, k - 1) : 0f;
            float previousFar = k > 0 ? FizzyBombModel.FarRadius(config.S3, k - 1) : 0f;
            ok = Mathf.Abs(gn - en) < 0.01f && Mathf.Abs(gf - ef) < 0.01f && Mathf.Abs(en - 50f) < 0.01f
                 && Mathf.Abs(ef - 35f) < 0.01f && dn > previousNear && df > previousFar;
            return $"近假人 {dn:0.00} m 得 {gn}(模型 {en});遠假人 {df:0.00} m 得 {gf}(模型 {ef})";
        }

        // ── (d) 第 1 段:一爆、飛行軌跡 ──
        private IEnumerator Uncharged(FizzyBombConfig config)
        {
            yield return WaitUntil(() => LiveOf<FizzyBomb>() == null, 3f);
            yield return MoveTo(new Vector3(-24f, 0.1f, -4f), Vector3.right);
            yield return WaitSeconds(0.2f);
            Tank.RefillFull();
            AimYawPitch(Vector3.right, 0f);
            Intent.Sub = true;
            yield return Frames(3);
            Vector3 predicted = Thrower.PredictedLanding;
            int finished = FizzyBomb.FinishedCount;
            float landBefore = FizzyBomb.LastFirstLandTime;
            yield return Release();
            Vector3 origin = Thrower.LastLaunchOrigin;
            // 飛行中(每幀步進 + 軌跡落墨)的主執行緒配置
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 15; i++)
            {
                yield return null;
            }
            long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
            Debug.Log($"[AUTOTEST] INFO {Label} 飛行中 15 幀主執行緒配置 {allocated} bytes");
            yield return WaitUntil(() => FizzyBomb.LastFirstLandTime > landBefore, 2f);
            Vector3 land = FizzyBomb.LastFirstLandPoint;
            // 著地滾停的估計位置旁擺三個假人:近 1.3(50)、遠 3.3(35)、範圍外 4.3(0)
            FizzyBomb live = LiveOf<FizzyBomb>();
            TargetDummy near = null;
            TargetDummy far = null;
            TargetDummy outside = null;
            if (live != null)
            {
                Vector3 rest = live.RestEstimate;
                near = SpawnDummyAtDistance(rest, Vector3.forward, 1.3f, 0f);
                far = SpawnDummyAtDistance(rest, Vector3.back, 3.3f, 0f);
                outside = SpawnDummyAtDistance(rest, new Vector3(1f, 0f, 1f), 4.3f, 0f);
            }
            yield return WaitUntil(() => FizzyBomb.FinishedCount > finished, 2f);
            yield return Frames(3);
            Check($"{Label} 著地點 = 預覽落點(同一個 S3ThrownFlight)", Flat(land, predicted) < 0.35f,
                $"著地 {land} 預覽 {predicted} 誤差 {Flat(land, predicted):0.00} m");
            Check($"{Label} 第 1 段:著地 30F 後爆 1 次就結束", FizzyBomb.LastLevel == 1 && FizzyBomb.LastBurstTotal == 1
                && FizzyBomb.LastFrameOf(0) - FizzyBomb.LastFirstLandFrame == 30,
                $"段數 {FizzyBomb.LastLevel} 爆 {FizzyBomb.LastBurstTotal} 次;著地 → 爆 {FizzyBomb.LastFrameOf(0) - FizzyBomb.LastFirstLandFrame}F");
            if (near != null)
            {
                Vector3 center = FizzyBomb.LastCenterOf(0);
                ExplosionSpec spec = config.BlastAt(0);
                float dn = Closest(near, center);
                float df = Closest(far, center);
                float dout = Closest(outside, center);
                float gn = near.PopupVisible ? near.PopupTotal : 0f;
                float gf = far.PopupVisible ? far.PopupTotal : 0f;
                float gout = outside.PopupVisible ? outside.PopupTotal : 0f;
                Check($"{Label} 第 1 爆:近 50(1.6)/ 遠 35(3.8)/ 範圍外 0",
                    Mathf.Abs(gn - spec.Evaluate(dn)) < 0.01f && Mathf.Abs(gf - spec.Evaluate(df)) < 0.01f
                    && gout == 0f && spec.Evaluate(dn) == 50f && spec.Evaluate(df) == 35f && dout > 3.8f,
                    $"{dn:0.00} m 得 {gn}、{df:0.00} m 得 {gf}、{dout:0.00} m 得 {gout}");
            }
            else
            {
                Check($"{Label} 第 1 爆假人", false, "著地後找不到滾動中的 FizzyBomb");
            }
            // 飛行軌跡:從出手 3 m 後到著地前 4 m,每 1.1 m 取樣:中線與側 0.8 m 是自隊墨、側 1.9 m 不是
            int samples = 0;
            int line = 0;
            int side = 0;
            int outer = 0;
            float z = origin.z;
            for (float x = origin.x + 3f; x < land.x - 4f; x += 1.1f)
            {
                samples++;
                line += GroundInked(new Vector3(x, 0f, z)) ? 1 : 0;
                side += GroundInked(new Vector3(x, 0f, z + 0.8f)) && GroundInked(new Vector3(x, 0f, z - 0.8f)) ? 1 : 0;
                outer += GroundInked(new Vector3(x, 0f, z + 1.9f)) || GroundInked(new Vector3(x, 0f, z - 1.9f)) ? 1 : 0;
            }
            float flight = land.x - origin.x;
            int expectedStamps = Mathf.FloorToInt(flight / 2.2f);
            Check($"{Label} 飛行軌跡:每 2.2 m 往下落墨,連成約 2.4 m 寬的帶(第 1 段半寬 1.2)",
                samples >= 8 && line == samples && side >= samples - 1 && outer <= 1
                && Mathf.Abs(FizzyBomb.LastTrailStamps - expectedStamps) <= 1,
                $"{samples} 點:中線 {line}、兩側 0.8 m {side}、兩側 1.9 m {outer};落墨 {FizzyBomb.LastTrailStamps} 筆" +
                $"(飛 {flight:0.0} m / 2.2 ≈ {expectedStamps}),軌跡塗地 {FizzyBomb.LastTrailArea:0.0} m²");
            Remove(near);
            Remove(far);
            Remove(outside);
        }

        // ── (e) 連打折抵 ──
        private IEnumerator Mash(FizzyBombConfig config)
        {
            yield return WaitUntil(() => LiveOf<FizzyBomb>() == null, 3f);
            yield return MoveTo(new Vector3(-22f, 0.1f, 12f), Vector3.right);
            Tank.RefillFull();
            AimYawPitch(Vector3.right, -15f);
            Intent.JumpHold = false;
            Intent.Sub = true;
            float pressed = Time.time;
            yield return WaitSeconds(0.15f);
            // 一次跳躍 = 折抵 8F,每幀最多兌現 2.5F
            float before = Thrower.SubMashFrames;
            Intent.JumpHold = true;
            yield return null;
            Track();
            yield return null;
            Track();
            float afterOne = Thrower.SubMashFrames - before;
            float cap = 2.5f * Mathf.Max(1f, MaxDeltaTime * 60f) * 2f + 0.01f;
            yield return Frames(6);
            float afterAll = Thrower.SubMashFrames - before;
            Intent.JumpHold = false;
            yield return Frames(2);
            // 搖桿從中間甩出 = 11F
            float stickBefore = Thrower.SubMashFrames;
            Intent.Move = new Vector2(1f, 0f);
            yield return Frames(2);
            Intent.Move = Vector2.zero;
            yield return Frames(8);
            float stick = Thrower.SubMashFrames - stickBefore;
            Check($"{Label} 連打:跳躍一次折抵 8F(分幀兌現,每幀 ≤ 2.5F)、搖桿甩出一次 11F",
                afterOne > 0f && afterOne <= cap && Mathf.Abs(afterAll - 8f) < 0.01f && Mathf.Abs(stick - 11f) < 0.01f,
                $"按下後兩幀 {afterOne:0.00}F(上限 {cap:0.00})、共 {afterAll:0.00}F;搖桿 {stick:0.00}F");
            // 持續連打:每 2 幀按一次跳躍 → 約 1 秒內蓄滿(只按住要 2.08 s)
            while (Thrower.SubChargeFrames < 120f && Time.time - pressed < 2f)
            {
                Intent.JumpHold = !Intent.JumpHold;
                yield return null;
                Track();
                yield return null;
                Track();
            }
            float fullAt = Time.time - pressed;
            Intent.JumpHold = false;
            Check($"{Label} 連打加速:約 1 秒內到第 3 段(只按住要 125F = 2.08 s)",
                Thrower.SubChargeFrames >= 120f && fullAt < 1.3f && config.LevelAt(Thrower.SubChargeFrames) == 3,
                $"{fullAt:0.00} s 蓄滿;連打折抵共 {Thrower.SubMashFrames:0.0}F");
            int finished = FizzyBomb.FinishedCount;
            yield return Release();
            yield return WaitUntil(() => FizzyBomb.FinishedCount > finished, 5f);
            Check($"{Label} 連打蓄滿丟出也是 3 爆", FizzyBomb.LastLevel == 3 && FizzyBomb.LastBurstTotal == 3,
                $"段數 {FizzyBomb.LastLevel} 爆 {FizzyBomb.LastBurstTotal} 次");
        }

        // ── (f) 炸彈共通規則 ──
        private IEnumerator DetonatorRule()
        {
            yield return WaitUntil(() => LiveOf<FizzyBomb>() == null, 3f);
            TestDeployable friendly = TestDeployable.Create("FizzyTest_FriendlyWall", new Vector3(-17f, 2f, 4f),
                new Vector3(0.4f, 4f, 4f));
            friendly.Setup(Player, Player.Team, 800f, TargetClass.SplashWall, false, true);
            TestDeployable enemy = TestDeployable.Create("FizzyTest_EnemyWall", new Vector3(-11f, 2f, 4f),
                new Vector3(0.4f, 4f, 4f));
            enemy.Setup(null, EnemyTeam, 800f, TargetClass.SplashWall, false, true);
            Physics.SyncTransforms();
            yield return MoveTo(new Vector3(-24f, 0.1f, 4f), Vector3.right);
            Tank.RefillFull();
            AimYawPitch(Vector3.right, 0f);
            Intent.Sub = true;
            yield return Frames(3);
            Vector3 predicted = Thrower.PredictedLanding;
            int detonations = FizzyBomb.DetonatorHitCount;
            int finished = FizzyBomb.FinishedCount;
            yield return Release();
            yield return WaitUntil(() => FizzyBomb.FinishedCount > finished, 3f);
            yield return Frames(2);
            Vector3 blast = FizzyBomb.LastCenterOf(0);
            const float face = -11.2f;
            Check($"{Label} 自隊 Splash Wall 穿過;碰到敵方 Splash Wall 立刻在牆前爆(不等著地)",
                FizzyBomb.DetonatorHitCount == detonations + 1 && blast.x < face && blast.x > face - 0.6f
                && Mathf.Abs(friendly.Hp - 800f) < 0.01f,
                $"爆心 {blast}(牆面 x = {face})自隊牆 HP {friendly.Hp}");
            Check($"{Label} 敵方 Splash Wall 吃 50 × 3.6 = 180(AttackClass.Fizzy)", Mathf.Abs(enemy.Hp - 620f) < 0.01f,
                $"敵方牆 HP {enemy.Hp}(期望 620)");
            Check($"{Label} 預覽也停在敵方牆前", Mathf.Abs(predicted.x - face) < 0.5f && predicted.x < face + 0.05f,
                $"預覽 {predicted}");
            Destroy(friendly.gameObject);
            Destroy(enemy.gameObject);
            yield return Frames(2);
        }

        // ── (g) bot 用法 ──
        private IEnumerator BotUse(FizzyBombConfig config)
        {
            yield return WaitUntil(() => LiveOf<FizzyBomb>() == null, 3f);
            var standAt = new Vector3(-22f, 0.1f, 18f);
            TargetDummy dummy = SpawnDummy(new Vector3(-12f, 0f, 18f));
            int finished = FizzyBomb.FinishedCount;
            bool threw = false;
            float hold = 0f;
            yield return BotAimThrow(config, standAt, Vector3.right, 10f, (t, h) =>
            {
                threw = t;
                hold = h;
            });
            yield return WaitUntil(() => FizzyBomb.FinishedCount > finished, 5f);
            yield return Frames(3);
            float dealt = dummy.PopupVisible ? dummy.PopupTotal : 0f;
            Check($"{Label} bot 用法(模擬 BotBrain:BotAimPoint 解 S3 彈道、按住 BotHoldSeconds)丟中 10 m 外的目標",
                threw && FizzyBomb.LastLevel == 3 && dealt >= 35f,
                $"投出={threw} 按住 {hold:0.00} s 段數 {FizzyBomb.LastLevel} 假人受傷 {dealt}");
            Remove(dummy);
        }
    }
}

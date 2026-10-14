using System.Collections;
using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Combat;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // Curling Bomb 驗收(S3 v11.3.0,research 03 §2.7),試射場,全走真實路徑(意圖 → ThrowController 蓄力 → CurlingBomb):
    //   (a) 換裝:手持模型、bot 掛勾(近距離蓄比較久)、ThrowStyle.GroundRoll
    //   (b) 蓄力:出手準備 5F 後開始,0.5 s 時 25F、60F 蓄滿;耗墨 65%;滿蓄墨量鎖 30F
    //   (c) 滿蓄:引信 90F(1.5 s);爆炸半徑 4.6 / 8.0 → 最近點 3.0 m 得 180、6.5 m 得 30;爆點 = 預覽
    //   (d) 未蓄力:墨量鎖 70F;引信 210F(3.5 s);巡航 0.22 u/F = 13.2 m/s;總距離 ≈ 模型;
    //       滑過路上的假人只吃 20(接觸,不擋路);爆炸半徑 1.6 → 3.0 m 只得 30;留下可游的自隊墨軌跡(r ≈ 1.075)
    //   (e) 牆面鏡射:45° 入射 → 45° 反射,速度不減;預覽含反射
    //   (f) 炸彈共通規則:自隊 Splash Wall 穿過;滑到敵方 Splash Wall 立刻在牆前爆炸(不等引信、不吃接觸 20),
    //       牆吃 180 × 2.0、牆後假人被擋住無傷;預覽落點也停在牆前
    [AutoTest(Scene = AutoTestAttribute.TrainingScene, Timeout = 120f, Order = 232)]
    public sealed class CurlingBombAutoTest : BombTestBase
    {
        private const string Label = "Curling Bomb";
        private const float Pitch = -15f;

        public static void Run()
        {
            Launch<CurlingBombAutoTest>();
        }

        private IEnumerator Start()
        {
            if (!Prepare("curling_bomb", out CurlingBombConfig config))
            {
                yield break;
            }
            yield return MoveTo(new Vector3(-6f, 0.1f, 20f), Vector3.back);
            yield return CheckHeldModel(config, Label);
            CheckBotUsage(config, Label);
            float holdNear = config.BotHoldSeconds(6f);
            float holdFar = config.BotHoldSeconds(30f);
            Check($"{Label} ThrowStyle.GroundRoll;bot 依距離蓄力(近 → 蓄久、遠 → 幾乎不蓄)",
                config.Style == ThrowStyle.GroundRoll && holdNear > holdFar + 0.3f && holdFar < 0.3f,
                $"6 m 按住 {holdNear:0.00} s、30 m 按住 {holdFar:0.00} s");
            yield return WaitSeconds(3.2f);

            yield return FullCharge(config);
            yield return Uncharged(config);
            yield return WallReflection();
            yield return DetonatorRule();

            yield return BotStyleThrow(config, Label, new Vector3(-22f, 0.1f, 6f), Vector3.right, 10f,
                () => CurlingBomb.ExplosionCount, 5f);
            CheckNoExceptions(Label);
            Debug.Log($"[AUTOTEST] INFO {Label} 量測期間最大幀時間 {MaxDeltaTime * 1000f:0.0} ms");
            Cleanup();
            Finish();
        }

        private IEnumerator FullCharge(CurlingBombConfig config)
        {
            yield return MoveTo(new Vector3(-20f, 0.1f, -18f), Vector3.right);
            Tank.RefillFull();
            AimYawPitch(Vector3.right, Pitch);
            Intent.Sub = true;
            float pressed = Time.time;
            yield return null;
            while (Time.time - pressed < 0.5f)
            {
                yield return null;
                Track();
            }
            float heldFrames = (Time.time - pressed) * 60f;
            float chargeAtHalf = Thrower.SubChargeFrames;
            bool charging = Thrower.IsChargingSub;
            while (Time.time - pressed < 1.25f)
            {
                yield return null;
                Track();
            }
            var indicator = Visual != null && Visual.HeldSubItem != null
                ? Visual.HeldSubItem.GetComponent<SubChargeIndicator>()
                : null;
            Check($"{Label} 蓄力:5F 準備後開始、60F 蓄滿;手上炸彈的指示燈顯示蓄力",
                charging && Mathf.Abs(chargeAtHalf - (heldFrames - 5f)) <= 2f && Thrower.SubChargeFrames >= 60f
                && Mathf.Abs(Thrower.SubCharge01 - 1f) < 1e-4f && indicator != null && indicator.DisplayedCharge > 0.99f,
                $"按住 {heldFrames:0.0}F 時蓄 {chargeAtHalf:0.0}F;1.25 s 時蓄 {Thrower.SubChargeFrames:0.0}F({Thrower.SubCharge01:0.00})" +
                $" 指示燈 {(indicator != null ? indicator.DisplayedCharge.ToString("0.00") : "無")}");
            Vector3 predicted = Thrower.PredictedLanding;
            Vector3 center = predicted + Vector3.up * config.Radius;
            TargetDummy near = SpawnDummyAtDistance(center, Vector3.forward, 3f, 0f);
            TargetDummy far = SpawnDummyAtDistance(center, Vector3.back, 6.5f, 0f);
            yield return Frames(2);
            int explosions = CurlingBomb.ExplosionCount;
            yield return Release();
            float spent = LastInkBefore - LastInkAfter;
            Check($"{Label} 耗墨 65%", LastThrew && Mathf.Abs(spent - 0.65f) < 0.005f, $"扣墨 {spent:0.000}");
            float lockSeconds = -1f;
            yield return MeasureInkLock(0.5f, s => lockSeconds = s);
            Check($"{Label} 滿蓄墨量鎖 30F = 0.500 s",
                Mathf.Abs(Thrower.LastThrowCharge01 - 1f) < 1e-4f && Mathf.Abs(lockSeconds - 0.5f) <= LockTolerance,
                $"蓄力 {Thrower.LastThrowCharge01:0.00} 丟出後 {lockSeconds:0.000} s 開始回墨(容許 ±{LockTolerance:0.000})");
            yield return WaitUntil(() => CurlingBomb.ExplosionCount > explosions, 3f);
            yield return Frames(2);
            float fuse = CurlingBomb.LastExplosionTime - CurlingBomb.LastLaunchTime;
            Vector3 blast = CurlingBomb.LastExplosionCenter;
            Check($"{Label} 滿蓄引信 210 − 2×60 = 90F(1.5 s,放開起算)",
                CurlingBomb.LastExplosionFrame == 90 && Mathf.Abs(fuse - 1.5f) <= 0.05f,
                $"模擬 {CurlingBomb.LastExplosionFrame}F;實測 {fuse:0.000} s");
            Check($"{Label} 爆點 = 預覽(同一個 CurlingMotion)", Flat(blast, predicted) < 0.3f,
                $"爆心 {blast} 預覽 {predicted} 誤差 {Flat(blast, predicted):0.00} m");
            CheckDamage(config, 1f, near, blast, 180f, "滿蓄近距 180(半徑 4.6)");
            CheckDamage(config, 1f, far, blast, 30f, "滿蓄遠距 30(半徑 8.0)");
            float travel = Flat(blast, Thrower.LastLaunchOrigin);
            float model = CurlingBombModel.PredictFlatTravel(config.S3, 1f, Pitch,
                Thrower.LastLaunchOrigin.y - config.Radius, out _);
            Check($"{Label} 滿蓄總距離 ≈ 模型(JP 實測約 1 線 = 5 m)", Mathf.Abs(travel - model) < 1f && travel < 7f,
                $"實測 {travel:0.00} m 模型 {model:0.00} m");
            Remove(near);
            Remove(far);
        }

        private IEnumerator Uncharged(CurlingBombConfig config)
        {
            yield return MoveTo(new Vector3(-22f, 0.1f, -22f), Vector3.right);
            yield return WaitSeconds(0.3f);
            Tank.RefillFull();
            AimYawPitch(Vector3.right, Pitch);
            Intent.Sub = true;
            yield return Frames(2);
            Vector3 predicted = Thrower.PredictedLanding;
            float chargeAtRelease = Thrower.SubChargeFrames;
            // 路徑中段擋一個假人(接觸 20、穿過);終點側邊 3 m 一個假人(未蓄近距 1.6 → 只得 30)
            TargetDummy onPath = SpawnDummy(new Vector3(-8f, 0f, predicted.z));
            Vector3 center = predicted + Vector3.up * config.Radius;
            TargetDummy side = SpawnDummyAtDistance(center, Vector3.forward, 3f, 0f);
            int contacts = CurlingBomb.ContactHitCount;
            int explosions = CurlingBomb.ExplosionCount;
            yield return Release();
            float lockSeconds = -1f;
            yield return MeasureInkLock(70f / 60f, s => lockSeconds = s);
            Check($"{Label} 未蓄力墨量鎖 70F = 1.167 s",
                chargeAtRelease < 0.01f && Thrower.LastThrowCharge01 < 0.01f
                && Mathf.Abs(lockSeconds - 70f / 60f) <= LockTolerance,
                $"蓄力 {chargeAtRelease:0.0}F 丟出後 {lockSeconds:0.000} s 開始回墨(容許 ±{LockTolerance:0.000})");

            // 滑行中量每幀配置(穩態零 GC)
            yield return WaitSeconds(0.3f);
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 40; i++)
            {
                yield return null;
            }
            long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
            Debug.Log($"[AUTOTEST] INFO {Label} 滑行中 40 幀主執行緒配置 {allocated} bytes");

            yield return WaitUntil(() => CurlingBomb.ExplosionCount > explosions, 4f);
            yield return Frames(3);
            float fuse = CurlingBomb.LastExplosionTime - CurlingBomb.LastLaunchTime;
            Vector3 blast = CurlingBomb.LastExplosionCenter;
            Check($"{Label} 未蓄力引信 210F(3.5 s)", CurlingBomb.LastExplosionFrame == 210 && Mathf.Abs(fuse - 3.5f) <= 0.06f,
                $"模擬 {CurlingBomb.LastExplosionFrame}F;實測 {fuse:0.000} s");
            float speed = CurlingBomb.LastMaxGroundSpeed;
            Check($"{Label} 未蓄力巡航 0.22 u/F = 13.2 m/s", speed > 12.4f && speed <= 13.25f,
                $"最高滑速 {speed:0.00} m/s");
            float travel = Flat(blast, Thrower.LastLaunchOrigin);
            float model = CurlingBombModel.PredictFlatTravel(config.S3, 0f, Pitch,
                Thrower.LastLaunchOrigin.y - config.Radius, out _);
            Check($"{Label} 未蓄力總距離 ≈ 模型(JP 實測 6.9 線 ≈ 34.5 m)、爆點 = 預覽",
                Mathf.Abs(travel - model) < 1.5f && travel > 28f && Flat(blast, predicted) < 0.35f,
                $"實測 {travel:0.0} m 模型 {model:0.0} m;爆心 {blast} 預覽 {predicted}");
            Check($"{Label} 滑過敵人:接觸 20(同一目標 40F 一次),穿過不停",
                CurlingBomb.ContactHitCount == contacts + 1 && onPath.PopupVisible
                && Mathf.Abs(onPath.PopupTotal - 20f) < 0.01f && blast.x > onPath.transform.position.x + 5f,
                $"接觸 {CurlingBomb.ContactHitCount - contacts} 次、路上假人累計 {onPath.PopupTotal} 爆心 x = {blast.x:0.0}");
            CheckDamage(config, 0f, side, blast, 30f, "未蓄力近距半徑 1.6:3.0 m 只得 30");

            // 軌跡:落地後到終點前的路徑上是自隊墨(可游);側邊 0.7 m 有、1.8 m 沒有(r ≈ 1.075)。
            // 從起點 12 m 後開始取樣(避開前一顆滿蓄炸彈的爆炸塗墨)
            Vector3 origin = Thrower.LastLaunchOrigin;
            int onLine = 0;
            int nearSide = 0;
            int farSide = 0;
            int samples = 0;
            float z = predicted.z;
            for (float x = origin.x + 12f; x < blast.x - 4f; x += 2.5f)
            {
                samples++;
                onLine += GroundInked(new Vector3(x, 0f, z)) ? 1 : 0;
                nearSide += GroundInked(new Vector3(x, 0f, z + 0.7f)) ? 1 : 0;
                farSide += GroundInked(new Vector3(x, 0f, z + 1.8f)) ? 1 : 0;
            }
            Check($"{Label} 滑行軌跡塗成可游的自隊墨(r 1.075)",
                samples >= 6 && onLine == samples && nearSide >= samples - 1 && farSide <= 1,
                $"{samples} 點:中線 {onLine}、側 0.7 m {nearSide}、側 1.8 m {farSide};軌跡塗地 {CurlingBomb.LastTrailArea:0.0} m²");
            Remove(onPath);
            Remove(side);
        }

        private IEnumerator WallReflection()
        {
            // 東西向的牆,面朝 −z(z = 2.75);從西南往東北 45° 丟
            SpawnWall(new Vector3(-10f, 1.5f, 3f), new Vector3(20f, 3f, 0.5f), "CurlingTestWall");
            Vector3 heading = new Vector3(1f, 0f, 1f).normalized;
            yield return MoveTo(new Vector3(-18f, 0.1f, -5f), heading);
            Tank.RefillFull();
            AimYawPitch(heading, Pitch);
            Intent.Sub = true;
            yield return Frames(2);
            Vector3 predicted = Thrower.PredictedLanding;
            int explosions = CurlingBomb.ExplosionCount;
            yield return Release();
            CurlingBomb live = null;
            float deadline = Time.time + 1f;
            while (live == null && Time.time < deadline)
            {
                yield return null;
                foreach (CurlingBomb bomb in FindObjectsByType<CurlingBomb>(FindObjectsSortMode.None))
                {
                    if (bomb.IsActive)
                    {
                        live = bomb;
                    }
                }
            }
            float speedBefore = 0f;
            deadline = Time.time + 3f;
            while (live != null && live.IsActive && live.Bounces == 0 && Time.time < deadline)
            {
                speedBefore = live.GroundSpeed;
                yield return null;
            }
            Vector3 after = live != null ? live.Heading : Vector3.zero;
            float speedAfter = live != null ? live.GroundSpeed : 0f;
            Vector3 ideal = new Vector3(heading.x, 0f, -heading.z);
            float error = Vector3.Angle(after, ideal);
            Check($"{Label} 牆面鏡射:入射 45° → 反射 45°,速度不減",
                live != null && live.Bounces >= 1 && error < 3f && speedAfter > speedBefore * 0.95f,
                $"反射後方向 {after} 理想 {ideal} 誤差 {error:0.0}° 速度 {speedBefore:0.00} → {speedAfter:0.00} m/s");
            yield return WaitUntil(() => CurlingBomb.ExplosionCount > explosions, 4f);
            yield return Frames(2);
            Vector3 blast = CurlingBomb.LastExplosionCenter;
            Check($"{Label} 預覽含牆面反射:爆點 = 預覽", Flat(blast, predicted) < 0.4f && blast.z < 2.75f,
                $"爆心 {blast} 預覽 {predicted}");
        }

        private IEnumerator DetonatorRule()
        {
            // z = 14 的東西向跑道:x = −14 自隊牆、x = −6 敵方牆(面朝 −x 的那一面在 x = −6.2),牆後 2.5 m 一個假人
            Team enemyTeam = Player.Team == Team.Alpha ? Team.Bravo : Team.Alpha;
            TestDeployable friendly = TestDeployable.Create("CurlingTest_FriendlyWall", new Vector3(-14f, 1.5f, 14f),
                new Vector3(0.4f, 3f, 4f));
            friendly.Setup(Player, Player.Team, 800f, TargetClass.SplashWall, false, true);
            TestDeployable enemy = TestDeployable.Create("CurlingTest_EnemyWall", new Vector3(-6f, 1.5f, 14f),
                new Vector3(0.4f, 3f, 4f));
            enemy.Setup(null, enemyTeam, 800f, TargetClass.SplashWall, false, true);
            yield return MoveTo(new Vector3(-22f, 0.1f, 14f), Vector3.right);
            TargetDummy behind = SpawnDummy(new Vector3(-3.3f, 0f, Muzzle.position.z));
            Tank.RefillFull();
            AimYawPitch(Vector3.right, Pitch);
            Intent.Sub = true;
            yield return Frames(3);
            Vector3 predicted = Thrower.PredictedLanding;
            int explosions = CurlingBomb.ExplosionCount;
            int detonations = CurlingBomb.DetonatorHitCount;
            int contacts = CurlingBomb.ContactHitCount;
            yield return Release();
            yield return WaitUntil(() => CurlingBomb.ExplosionCount > explosions, 4f);
            yield return Frames(3);
            Vector3 blast = CurlingBomb.LastExplosionCenter;
            const float face = -6.2f;
            Check($"{Label} 自隊 Splash Wall 穿過;滑到敵方 Splash Wall 立刻在牆前爆炸(不等 210F 引信)",
                CurlingBomb.ExplosionCount == explosions + 1 && CurlingBomb.DetonatorHitCount == detonations + 1
                && CurlingBomb.LastExplosionFrame < 150 && blast.x < face && blast.x > face - 0.5f
                && Mathf.Abs(friendly.Hp - 800f) < 0.01f,
                $"爆炸於第 {CurlingBomb.LastExplosionFrame}F 爆心 {blast}(牆面 x = {face})自隊牆 HP {friendly.Hp}");
            Check($"{Label} 敵方 Splash Wall 吃爆炸 180 × 2.0(不吃接觸 20)、牆後假人被擋住無傷",
                Mathf.Abs(enemy.Hp - 440f) < 0.01f && CurlingBomb.ContactHitCount == contacts && !behind.PopupVisible
                && Closest(behind, blast) < 5f,
                $"敵方牆 HP {enemy.Hp}(期望 440)接觸 {CurlingBomb.ContactHitCount - contacts} 次;" +
                $"牆後假人最近點 {Closest(behind, blast):0.00} m 受擊={behind.PopupVisible}");
            Check($"{Label} 預覽落點也停在敵方牆前", Flat(predicted, blast) < 0.4f,
                $"預覽 {predicted} 爆心 {blast} 誤差 {Flat(predicted, blast):0.00} m");
            Remove(behind);
            Destroy(friendly.gameObject);
            Destroy(enemy.gameObject);
            yield return Frames(2);
        }

        private void CheckDamage(CurlingBombConfig config, float charge, TargetDummy dummy, Vector3 blast, float expected,
            string band)
        {
            float distance = Closest(dummy, blast);
            float model = config.BlastAt(charge).Evaluate(distance);
            float dealt = dummy.PopupVisible ? dummy.PopupTotal : 0f;
            Check($"{Label} {band}",
                Mathf.Abs(model - expected) < 0.01f && Mathf.Abs(dealt - expected) < 0.01f,
                $"最近點距離 {distance:0.00} m 模型 {model} 實得 {dealt}");
        }
    }
}

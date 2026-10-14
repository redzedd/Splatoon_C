using System.Collections;
using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Painting;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // Suction Bomb 驗收(S3 v11.3.0,research 03 §2.2),試射場,全走真實路徑(意圖 → ThrowController → SuctionBomb → Explosion):
    //   (a) 換裝:手持模型、bot 掛勾;耗墨 70%、墨量鎖 60F
    //   (b) 丟向 ClimbWall_High(可塗牆,x = 12 面朝 −x),路徑上擋一個假人:穿過假人、黏在牆上(法線 −x)、不掉落;
    //       黏住 → 警告音 60F(1.0 s)→ 爆炸 120F(2.0 s);爆心離牆 +0.6;
    //       黏住後在爆心周圍擺假人:最近點 3.6 m → 180、6.3 m → 30、9.2 m → 0;牆面圓盤 r 5.0 塗墨;
    //   (c) 丟到地上:黏在預測落點(不彈不滾)、地面圓盤 r 5.0(r 4.5 圓周有墨);黏牆時的地面塗地量遠小於黏地
    //       (只塗黏住的那一面 + 交叉塗墨 2.5)
    //   (d) 炸彈共通規則:自隊 Splash Wall 穿過;碰到敵方 Splash Wall(擋敵方攻擊的裝置)立刻爆炸,牆吃 180 × 2.0
    [AutoTest(Scene = AutoTestAttribute.TrainingScene, Timeout = 120f, Order = 230)]
    public sealed class SuctionBombAutoTest : BombTestBase
    {
        private const string Label = "Suction Bomb";

        public static void Run()
        {
            Launch<SuctionBombAutoTest>();
        }

        private IEnumerator Start()
        {
            if (!Prepare("suction_bomb", out SuctionBombConfig config))
            {
                yield break;
            }
            yield return MoveTo(new Vector3(-12f, 0.1f, 10f), Vector3.forward);
            yield return CheckHeldModel(config, Label);
            CheckBotUsage(config, Label);
            yield return WaitSeconds(2.4f);
            Tank.RefillFull();

            yield return WallStick(config);
            yield return FloorStick(config);
            yield return DetonatorRule();

            yield return BotStyleThrow(config, Label, new Vector3(-20f, 0.1f, 12f), Vector3.forward, 8f,
                () => SuctionBomb.ExplosionCount, 4f);
            CheckNoExceptions(Label);
            Debug.Log($"[AUTOTEST] INFO {Label} 量測期間最大幀時間 {MaxDeltaTime * 1000f:0.0} ms");
            Cleanup();
            Finish();
        }

        private float _wallDiscArea = -1f;

        private IEnumerator WallStick(SuctionBombConfig config)
        {
            // 站在牆西側 7.5 m,瞄牆面高 2 m 處
            yield return MoveTo(new Vector3(4f, 0.1f, 0f), Vector3.right);
            Tank.RefillFull();
            Intent.Sub = true;
            float lo = -40f;
            float hi = 30f;
            for (int i = 0; i < 14; i++)
            {
                float mid = (lo + hi) * 0.5f;
                AimYawPitch(Vector3.right, mid);
                yield return Frames(2);
                bool onWall = Thrower.HasPredictedLanding && Thrower.PredictedLanding.x > 11.3f;
                if (!onWall || Thrower.PredictedLanding.y > 2f)
                {
                    // 沒打到牆(落在牆前地面)→ 往上;打太高 → 往下
                    if (onWall)
                    {
                        hi = mid;
                    }
                    else
                    {
                        lo = mid;
                    }
                }
                else
                {
                    lo = mid;
                }
            }
            AimYawPitch(Vector3.right, (lo + hi) * 0.5f);
            yield return Frames(2);
            Vector3 predicted = Thrower.PredictedLanding;

            // 路徑中段擋一個假人(穿過角色/假人:S3 EnemyOffFenceOn)
            TargetDummy blocker = SpawnDummy(new Vector3(8f, 0f, 0f));
            yield return Frames(2);
            int blockerVersion = blocker.PopupVersion;
            int sticks = SuctionBomb.StickCount;
            int explosions = SuctionBomb.ExplosionCount;
            yield return Release();
            float inkSpent = LastInkBefore - LastInkAfter;
            Check($"{Label} 耗墨 70%", LastThrew && Mathf.Abs(inkSpent - 0.7f) < 0.005f,
                $"投出={LastThrew} 扣墨 {inkSpent:0.000}");
            float lockSeconds = -1f;
            yield return MeasureInkLock(1f, s => lockSeconds = s);
            Check($"{Label} 墨量鎖 60F = 1.000 s", Mathf.Abs(lockSeconds - 1f) <= LockTolerance,
                $"丟出後 {lockSeconds:0.000} s 開始回墨(容許 ±{LockTolerance:0.000})");

            yield return WaitUntil(() => SuctionBomb.StickCount > sticks, 3f);
            Vector3 origin = Thrower.LastLaunchOrigin;
            Vector3 velocity = Thrower.LastLaunchVelocity;
            // 彈道在假人處(x = 8)的高度:證明路徑確實穿過假人膠囊(0~2 m)
            float t = (8f - origin.x) / Mathf.Max(0.01f, velocity.x);
            float heightAtBlocker = origin.y + velocity.y * t + 0.5f * config.Gravity * t * t;
            Vector3 stick = SuctionBomb.LastStickPoint;
            Vector3 normal = SuctionBomb.LastStickNormal;
            Check($"{Label} 穿過路徑上的假人、黏在牆面(法線朝 −x),落點 = 預覽",
                SuctionBomb.StickCount == sticks + 1 && stick.x > 11.3f && normal.x < -0.95f
                && blocker.PopupVersion == blockerVersion && heightAtBlocker > 0.2f && heightAtBlocker < 1.9f
                && Vector3.Distance(stick, predicted) < 0.15f,
                $"黏住 {stick} 法線 {normal} 預覽 {predicted} 經過假人時高度 {heightAtBlocker:0.00} m " +
                $"假人受擊={(blocker.PopupVersion != blockerVersion)}");
            Remove(blocker);
            float stickTime = SuctionBomb.LastStickTime;

            // 爆心(離牆 +0.6)周圍擺假人:180 / 30 / 0
            Vector3 center = stick + normal * 0.6f;
            TargetDummy near = SpawnDummyAtDistance(center, new Vector3(-1f, 0f, -0.6f), 3.6f, 0f);
            TargetDummy far = SpawnDummyAtDistance(center, new Vector3(-1f, 0f, 0.8f), 6.3f, 0f);
            TargetDummy outside = SpawnDummyAtDistance(center, new Vector3(-1f, 0f, -1f), 9.2f, 0f);
            yield return WaitSeconds(0.5f);
            SuctionBomb live = FindLive<SuctionBomb>();
            Check($"{Label} 黏住後不動(牆上不下滑)",
                live != null && live.IsStuck && Vector3.Distance(live.SimulatedPosition, stick + normal * config.Radius) < 0.01f,
                $"0.5 s 後位置 {(live != null ? live.SimulatedPosition.ToString() : "無")}");

            yield return WaitUntil(() => SuctionBomb.ExplosionCount > explosions, 3f);
            yield return Frames(2);
            float fuse = SuctionBomb.LastExplosionTime - stickTime;
            float warning = SuctionBomb.LastWarningTime - stickTime;
            Check($"{Label} 黏住後 120F(2.0 s)爆炸、剩 60F(1.0 s)開始警告",
                SuctionBomb.LastFuseFrames == 120 && Mathf.Abs(fuse - 2f) <= 0.05f && Mathf.Abs(warning - 1f) <= 0.05f,
                $"模擬 {SuctionBomb.LastFuseFrames}F;實測 爆炸 {fuse:0.000} s、警告 {warning:0.000} s");
            Vector3 blast = Explosion.LastCenter;
            Check($"{Label} 傷害中心離黏住的面 +0.6 m", Mathf.Abs((stick.x - blast.x) - 0.6f) < 0.03f,
                $"爆心 {blast},離牆面(x = {stick.x:0.00}){stick.x - blast.x:0.000} m");
            CheckDamage(config, near, blast, 180f, "近距 180(≤ 4.6 m)");
            CheckDamage(config, far, blast, 30f, "遠距 30(4.6 ~ 8.0 m)");
            CheckDamage(config, outside, blast, 0f, "範圍外 0(> 8.0 m)");

            // 牆面圓盤:黏住點周圍 2.8 m 內的牆面點都是自隊墨
            PaintableSurface wall = GameObject.Find("ClimbWall_High").GetComponent<PaintableSurface>();
            int wallInked = 0;
            Vector3[] probes =
            {
                stick + new Vector3(0f, 0f, 2.4f), stick + new Vector3(0f, 0f, -2.2f),
                stick + new Vector3(0f, 1.8f, 0f), stick + new Vector3(0f, -1.7f, 1.2f),
            };
            foreach (Vector3 p in probes)
            {
                if (wall.SampleOwnership(p, Vector3.left) == (byte)Player.Team)
                {
                    wallInked++;
                }
            }
            _wallDiscArea = SuctionBomb.LastSurfacePaintArea;
            Check($"{Label} 牆面圓盤塗墨(黏住點 1.7~2.4 m 的牆面點)", wallInked == probes.Length,
                $"{wallInked}/{probes.Length} 點有墨;圓盤的地面塗地(交叉塗墨){_wallDiscArea:0.0} m²");
            Remove(near);
            Remove(far);
            Remove(outside);
        }

        private IEnumerator FloorStick(SuctionBombConfig config)
        {
            yield return MoveTo(new Vector3(-12f, 0.1f, -20f), Vector3.forward);
            Tank.RefillFull();
            Vector3 target = new Vector3(-12f, 0f, -11f);
            yield return AimToLand(target);
            Vector3 predicted = Thrower.PredictedLanding;
            int sticks = SuctionBomb.StickCount;
            int explosions = SuctionBomb.ExplosionCount;
            yield return Release();
            yield return WaitUntil(() => SuctionBomb.StickCount > sticks, 3f);
            Vector3 stick = SuctionBomb.LastStickPoint;
            yield return WaitSeconds(0.5f);
            SuctionBomb live = FindLive<SuctionBomb>();
            bool stayed = live != null && live.IsStuck
                          && Vector3.Distance(live.SimulatedPosition, stick + Vector3.up * config.Radius) < 0.01f;
            Check($"{Label} 丟到地上:黏在預覽落點、不彈不滾",
                SuctionBomb.StickCount == sticks + 1 && SuctionBomb.LastStickNormal.y > 0.95f
                && Flat(stick, predicted) < 0.15f && stayed,
                $"黏住 {stick} 預覽 {predicted} 誤差 {Flat(stick, predicted):0.00} m 0.5 s 後仍在原地={stayed}");
            yield return WaitUntil(() => SuctionBomb.ExplosionCount > explosions, 3f);
            yield return Frames(3);
            float floorArea = SuctionBomb.LastSurfacePaintArea;
            float expected = Mathf.PI * 5f * 5f;
            int ring45 = RingInked(stick, 4.5f);
            int ring2 = RingInked(stick, 2f);
            Check($"{Label} 地面圓盤 r 5.0(π·5² ≈ {expected:0} m²)",
                ring45 >= 7 && ring2 == 8 && floorArea > expected * 0.85f && floorArea < expected * 1.3f,
                $"r 2.0 圓周 {ring2}/8、r 4.5 圓周 {ring45}/8 有墨;圓盤新增塗地 {floorArea:0.0} m²");
            Check($"{Label} 只塗黏住的那一面:黏牆時地面只有交叉塗墨(< 35% 黏地面積)",
                _wallDiscArea >= 0f && _wallDiscArea < floorArea * 0.35f,
                $"黏牆 {_wallDiscArea:0.0} m² / 黏地 {floorArea:0.0} m²");
        }

        // 炸彈共通規則:碰到敵方 Splash Wall(擋敵方攻擊的裝置)立刻爆炸;自隊的穿過
        private IEnumerator DetonatorRule()
        {
            TestDeployable friendly = TestDeployable.Create("SuctionTest_FriendlyWall", new Vector3(-16f, 1.5f, 0f),
                new Vector3(0.4f, 3f, 3f));
            friendly.Setup(Player, Player.Team, 800f, TargetClass.SplashWall, false, true);
            yield return MoveTo(new Vector3(-20f, 0.1f, 0f), Vector3.right);
            Tank.RefillFull();
            yield return AimToLand(new Vector3(-8f, 0f, 0f));
            int sticks = SuctionBomb.StickCount;
            int explosions = SuctionBomb.ExplosionCount;
            yield return Release();
            yield return WaitUntil(() => SuctionBomb.StickCount > sticks || SuctionBomb.ExplosionCount > explosions, 2f);
            Check($"{Label} 自隊的 Splash Wall 穿過(黏在牆後地面)",
                SuctionBomb.StickCount == sticks + 1 && SuctionBomb.ExplosionCount == explosions
                && SuctionBomb.LastStickPoint.x > -15f,
                $"黏住 {SuctionBomb.LastStickPoint}");
            yield return WaitUntil(() => SuctionBomb.ExplosionCount > explosions, 3f);
            Destroy(friendly.gameObject);

            // 先瞄好(落點 x = −8)再立敵方牆:預覽照炸彈共通規則會停在敵方牆前(A2),
            // 牆先立好的話 AimToLand 以預覽落點二分會一路抬高、把炸彈丟過牆
            Tank.RefillFull();
            yield return AimToLand(new Vector3(-8f, 0f, 0f));
            TestDeployable enemy = TestDeployable.Create("SuctionTest_EnemyWall", new Vector3(-12f, 1.5f, 0f),
                new Vector3(0.4f, 3f, 3f));
            enemy.Setup(null, Player.Team == Team.Alpha ? Team.Bravo : Team.Alpha,
                800f, TargetClass.SplashWall, false, true);
            Physics.SyncTransforms();
            yield return Frames(2);
            Check($"{Label} 預覽停在敵方 Splash Wall 前(預設拋物線預覽也套炸彈共通規則)",
                Thrower.HasPredictedLanding && Thrower.PredictedLanding.x < -12.1f && Thrower.PredictedLanding.x > -13f,
                $"預覽落點 {Thrower.PredictedLanding}");
            sticks = SuctionBomb.StickCount;
            explosions = SuctionBomb.ExplosionCount;
            yield return Release();
            float thrown = LastThrowTime;
            yield return WaitUntil(() => SuctionBomb.ExplosionCount > explosions, 2f);
            float delay = SuctionBomb.LastExplosionTime - thrown;
            Check($"{Label} 碰到敵方 Splash Wall 立刻爆炸(不黏、不等引信);牆吃 180 × 2.0",
                SuctionBomb.ExplosionCount == explosions + 1 && SuctionBomb.StickCount == sticks && delay < 0.6f
                && Mathf.Abs(enemy.Hp - 440f) < 0.01f,
                $"投出到爆炸 {delay:0.000} s 爆心 {SuctionBomb.LastExplosionCenter} 牆 HP {enemy.Hp}(期望 440)");
            Destroy(enemy.gameObject);
        }

        private void CheckDamage(SuctionBombConfig config, TargetDummy dummy, Vector3 blast, float expected, string band)
        {
            float distance = Closest(dummy, blast);
            ExplosionSpec spec = config.Blast;
            float model = spec.Evaluate(distance);
            bool hit = dummy.PopupVisible;
            float dealt = hit ? dummy.PopupTotal : 0f;
            Check($"{Label} {band}",
                Mathf.Abs(model - expected) < 0.01f && Mathf.Abs(dealt - expected) < 0.01f,
                $"最近點距離 {distance:0.00} m 模型 {model} 實得 {dealt}");
        }

        private static T FindLive<T>() where T : SimulatedBomb
        {
            T[] all = FindObjectsByType<T>(FindObjectsSortMode.None);
            foreach (T bomb in all)
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

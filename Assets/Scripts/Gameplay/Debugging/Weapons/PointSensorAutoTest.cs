using System.Collections;
using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // Point Sensor 驗收(S3 v11.3.0,research 03 §2.10),試射場,全走真實路徑(意圖 → ThrowController → PointSensor → Marking):
    //   (a) 換裝:手持模型、bot 掛勾、飛行會打到敵人
    //   (b) 丟到地上:耗墨 45%、墨量鎖 75F;爆開點 = 預覽落點;r 6.0 內的敵人(牆後也算)被標記 480F(從命中起算),
    //       6.0 外的不標記;1 秒後才走進範圍的敵人只剩 480 − 60F;範圍停留 150F(2.5 s)後消失、不再標記;無傷害、無塗墨
    //   (c) 直接打中敵人:碰到就爆開(接觸目標 = 該敵人),該敵人被標記
    //   (d) bot 用法(模擬 BotBrain:BotAimPoint 解 S3 彈道)丟向 12 m 外的敵人 → 被標記
    //   (e) S3 GuideHitCollisionType …TorpedoOff:飛行中穿過敵方變身中的 Torpedo(不在它身上爆開、不卡住)
    [AutoTest(Scene = AutoTestAttribute.TrainingScene, Timeout = 120f, Order = 242)]
    public sealed class PointSensorAutoTest : ThrowableTestBase
    {
        private const string Label = "Point Sensor";

        public static void Run()
        {
            Launch<PointSensorAutoTest>();
        }

        private IEnumerator Start()
        {
            if (!Prepare("point_sensor", out PointSensorConfig config))
            {
                yield break;
            }
            yield return MoveTo(new Vector3(-6f, 0.1f, 20f), Vector3.back);
            yield return CheckHeldModel(config, Label);
            Vector2 range = config.BotRange;
            Check($"{Label} bot 掛勾:丟向敵人;飛行會打到敵人(S3 EnemyOn…)",
                config.BotUsage == BotSubUsage.ThrowAtEnemy && range.x > 0f && range.y > range.x
                && config.BotHoldSeconds(10f) > 0f && config.FlightHitsTargets,
                $"用法={config.BotUsage} 距離 {range.x:0}~{range.y:0} m 打到敵人={config.FlightHitsTargets}");
            yield return WaitSeconds(2.8f);
            Marking.ClearAll();

            yield return GroundBurst(config);
            yield return DirectHit(config);
            yield return PassThroughTorpedo(config);
            yield return BotUse(config);

            CheckNoExceptions(Label);
            Debug.Log($"[AUTOTEST] INFO {Label} 量測期間最大幀時間 {MaxDeltaTime * 1000f:0.0} ms");
            Cleanup();
            Finish();
        }

        // ── (b) 丟到地上 ──
        private IEnumerator GroundBurst(PointSensorConfig config)
        {
            yield return MoveTo(new Vector3(-22f, 0.1f, -14f), Vector3.right);
            Tank.RefillFull();
            AimYawPitch(Vector3.right, -12f);
            Intent.Sub = true;
            yield return Frames(3);
            Vector3 predicted = Thrower.PredictedLanding;
            // 牆後的敵人(爆心到敵人之間擋一面牆):距離 4.5 m;範圍外 7.5 m;晚到的敵人先站在 9 m 外
            Vector3 flatCenter = new Vector3(predicted.x, 0f, predicted.z);
            Inkling behindWall = SpawnEnemy("SensorEnemy_Wall", flatCenter + new Vector3(0f, 0f, 4.5f), true);
            Inkling outside = SpawnEnemy("SensorEnemy_Out", flatCenter + new Vector3(0f, 0f, -7.6f), true);
            Inkling late = SpawnEnemy("SensorEnemy_Late", flatCenter + new Vector3(9f, 0f, 0f), true);
            SpawnWall(flatCenter + new Vector3(0f, 1.5f, 2.2f), new Vector3(4f, 3f, 0.4f), "SensorTestWall");
            yield return Frames(3);
            Intent.Sub = true;
            yield return Frames(2);
            predicted = Thrower.PredictedLanding;
            int bursts = PointSensor.BurstCount;
            int detonations = Explosion.DetonationCount;
            yield return Release();
            float spent = LastInkBefore - LastInkAfter;
            Check($"{Label} 耗墨 45%", LastThrew && Mathf.Abs(spent - 0.45f) < 0.005f, $"扣墨 {spent:0.000}");
            float unlockedAt = -1f;
            float level = Tank.Normalized;
            float deadline = Time.time + 2f;
            while (Time.time < deadline && (PointSensor.BurstCount == bursts || unlockedAt < 0f))
            {
                yield return null;
                Track();
                if (unlockedAt < 0f && Tank.Normalized > level + 1e-5f)
                {
                    unlockedAt = Time.time;
                }
                level = Tank.Normalized;
            }
            float lockSeconds = unlockedAt > 0f ? unlockedAt - LastThrowTime : -1f;
            Check($"{Label} 墨量鎖 75F = 1.250 s", Mathf.Abs(lockSeconds - 75f / 60f) <= LockTolerance,
                $"丟出後 {lockSeconds:0.000} s 開始回墨(容許 ±{LockTolerance:0.000})");
            // 命中當下:牆後 4.5 m 的敵人被標記 8 s(從命中起算),7.6 m 外的沒有
            float burstTime = PointSensor.LastBurstTime;
            Vector3 center = PointSensor.LastBurstCenter;
            float elapsed = Time.time - burstTime;
            float wallRemaining = Marking.Remaining(behindWall, Player.Team);
            bool losBlocked = Physics.Linecast(center, behindWall.transform.position + Vector3.up, CombatMasks.Terrain,
                QueryTriggerInteraction.Ignore);
            Check($"{Label} 碰地就爆開、爆開點 = 預覽落點", PointSensor.BurstCount == bursts + 1
                && PointSensor.LastContact == BombContact.Terrain && Flat(center, predicted) < 0.35f,
                $"接觸 {PointSensor.LastContact} 爆心 {center} 預覽 {predicted}");
            Check($"{Label} r 6.0 內(牆後也算)的敵人被標記 480F(從命中起算),範圍外不標記",
                losBlocked && Mathf.Abs(wallRemaining + elapsed - 8f) < 0.05f && !Marking.IsMarked(outside, Player.Team)
                && !Marking.IsMarked(late, Player.Team),
                $"牆擋視線={losBlocked};牆後敵人剩 {wallRemaining:0.00} s + 經過 {elapsed:0.00} s = {wallRemaining + elapsed:0.00} s;" +
                $"7.6 m 外標記={Marking.IsMarked(outside, Player.Team)}");
            Check($"{Label} 無傷害、無塗墨(沒有爆炸解算)",
                Explosion.DetonationCount == detonations && !GroundInked(center) && behindWall.GetComponent<InklingHealth>().Damage <= 0f,
                $"爆炸解算 +{Explosion.DetonationCount - detonations} 爆心地面自隊墨={GroundInked(center)}");

            // 命中 1 秒後才走進範圍:標記 480 − 60F
            while (Time.time - burstTime < 1f)
            {
                yield return null;
                Track();
            }
            late.Teleport(new Vector3(center.x + 3f, 0.1f, center.z), 0f, false);
            float enteredAt = Time.time - burstTime;
            yield return Frames(3);
            float lateRemaining = Marking.Remaining(late, Player.Team);
            float lateTotal = lateRemaining + (Time.time - burstTime);
            Check($"{Label} 晚到的敵人:從命中起算 480F(1 秒後進來 → 約剩 7 秒)",
                Marking.IsMarked(late, Player.Team) && Mathf.Abs(lateTotal - 8f) < 0.06f && lateRemaining < 7.05f,
                $"命中後 {enteredAt:0.00} s 進入;剩 {lateRemaining:0.00} s(命中起算合計 {lateTotal:0.00} s)");

            // 範圍停留 150F(2.5 s)
            PointSensor live = LiveOf<PointSensor>();
            bool sensingAt2 = false;
            while (Time.time - burstTime < 2.4f)
            {
                yield return null;
                Track();
            }
            sensingAt2 = live != null && live.IsActive && live.IsSensing;
            float endedAt = -1f;
            deadline = Time.time + 0.6f;
            while (Time.time < deadline)
            {
                yield return null;
                Track();
                if (live == null || !live.IsActive)
                {
                    endedAt = Time.time - burstTime;
                    break;
                }
            }
            Check($"{Label} 感應範圍停留 150F = 2.5 s", sensingAt2 && Mathf.Abs(endedAt - 2.5f) <= 0.06f,
                $"2.4 s 時還在感應={sensingAt2};{endedAt:0.000} s 消失");
            // 範圍消失後才進來:不再標記
            Marking.Clear(outside);
            outside.Teleport(new Vector3(center.x, 0.1f, center.z - 2f), 0f, false);
            yield return Frames(4);
            Check($"{Label} 範圍消失後走進原位置不會被標記", !Marking.IsMarked(outside, Player.Team),
                $"標記={Marking.IsMarked(outside, Player.Team)}");
            RemoveEnemies();
        }

        // ── (c) 直接打中敵人 ──
        private IEnumerator DirectHit(PointSensorConfig config)
        {
            yield return MoveTo(new Vector3(-22f, 0.1f, 4f), Vector3.right);
            Tank.RefillFull();
            Inkling enemy = SpawnEnemy("SensorEnemy_Direct", new Vector3(-12f, 0.1f, 4f), true);
            yield return Frames(4);
            // 瞄準:解 S3 彈道落在敵人身後 1.2 m(弧線下降段穿過敵人身體 → 先碰到敵人)
            AimYawPitch(Vector3.right, 0f);
            Vector3 beyond = enemy.transform.position + Vector3.right * 1.2f;
            Aim.Forward = (config.BotAimPoint(PlayerGo.transform.position, beyond) - Muzzle.position).normalized;
            Intent.Sub = true;
            yield return Frames(3);
            Vector3 predicted = Thrower.PredictedLanding;
            int bursts = PointSensor.BurstCount;
            yield return Release();
            yield return WaitUntil(() => PointSensor.BurstCount > bursts, 1.5f);
            yield return Frames(2);
            var health = enemy.GetComponent<InklingHealth>();
            Check($"{Label} 直接打中敵人就爆開(接觸目標 = 該敵人)、該敵人被標記;預覽也停在敵人身上",
                PointSensor.LastContact == BombContact.Target && ReferenceEquals(PointSensor.LastContactTarget, health)
                && Marking.IsMarked(enemy, Player.Team) && Flat(predicted, enemy.transform.position) < 1f,
                $"接觸 {PointSensor.LastContact} 目標={(ReferenceEquals(PointSensor.LastContactTarget, health) ? "該敵人" : "其他")}" +
                $" 標記={Marking.IsMarked(enemy, Player.Team)} 預覽 {predicted}");
            yield return WaitUntil(() => LiveOf<PointSensor>() == null, 3f);
            RemoveEnemies();
        }

        // ── (e) 穿過敵方變身中的 Torpedo ──
        private IEnumerator PassThroughTorpedo(PointSensorConfig config)
        {
            var torpedoConfig = Resources.Load<TorpedoConfig>("Weapons/Sub_torpedo");
            if (torpedoConfig == null || !(torpedoConfig.Prefab is Torpedo torpedoPrefab)
                || !(config.Prefab is PointSensor sensorPrefab))
            {
                Check($"{Label} 穿過敵方 Torpedo 測試前提", false, "缺 Sub_torpedo / prefab(先跑 M7 Setup)");
                yield break;
            }
            yield return MoveTo(new Vector3(-22f, 0.1f, 10f), Vector3.right);
            var health = Player.GetComponent<InklingHealth>();
            bool wasInvulnerable = health != null && health.Invulnerable;
            if (health != null)
            {
                health.Invulnerable = true;
            }
            // 敵方投擲者站遠處;Torpedo 放在本機玩家前方 5 m、高 2.5 m → 第 6F 找到本機玩家、停在空中變身
            Inkling owner = SpawnEnemy("SensorEnemy_TorpedoOwner", new Vector3(-6f, 0.1f, 22f), true);
            yield return Frames(3);
            int transforms = Torpedo.TransformCount;
            Torpedo torpedo = SharedPool.Spawn(torpedoPrefab);
            torpedo.Launch(new Vector3(-17f, 2.5f, 10f), Vector3.zero, torpedoConfig, owner);
            yield return WaitUntil(() => Torpedo.TransformCount > transforms, 1f);
            yield return Frames(2);
            bool transformed = torpedo.IsActive && torpedo.State == TorpedoState.Transforming;
            Vector3 hull = torpedo.SimulatedPosition;
            // 偵測器從 Torpedo 前 1.6 m 以 S3 1.38 u/F 水平飛過它的受擊球
            int bursts = PointSensor.BurstCount;
            PointSensor sensor = SharedPool.Spawn(sensorPrefab);
            sensor.Launch(hull - Vector3.right * 1.6f, Vector3.right * SimFrames.PerFrameToPerSecond(config.S3.Throw.SpawnSpeedZ),
                config, Player);
            float passDeadline = Time.time + 0.3f;
            bool passed = false;
            while (Time.time < passDeadline && !passed)
            {
                yield return null;
                passed = sensor.IsActive && !sensor.IsSensing && sensor.SimulatedPosition.x > hull.x + 1f
                         || PointSensor.BurstCount > bursts;
            }
            bool stillTransformed = torpedo.IsActive && torpedo.IsTransformed;
            yield return WaitUntil(() => PointSensor.BurstCount > bursts, 1.5f);
            Check($"{Label} 飛行中穿過敵方變身中的 Torpedo(S3 …TorpedoOff):不在它身上爆開、繼續飛到更遠處",
                transformed && passed && stillTransformed && PointSensor.BurstCount == bursts + 1
                && !ReferenceEquals(PointSensor.LastContactTarget, torpedo)
                && PointSensor.LastBurstCenter.x > hull.x + 1f,
                $"變身={transformed} 穿過={passed} Torpedo 仍變身={stillTransformed} 爆開 +{PointSensor.BurstCount - bursts}" +
                $" 接觸 {PointSensor.LastContact} 爆心 x {PointSensor.LastBurstCenter.x:0.0}(Torpedo x {hull.x:0.0})");
            if (torpedo.IsActive)
            {
                torpedo.Expire();
            }
            if (health != null)
            {
                health.Invulnerable = wasInvulnerable;
            }
            yield return WaitUntil(() => LiveOf<PointSensor>() == null, 3f);
            RemoveEnemies();
            Marking.ClearAll();
        }

        // ── (d) bot 用法 ──
        private IEnumerator BotUse(PointSensorConfig config)
        {
            var standAt = new Vector3(-22f, 0.1f, 16f);
            // 敵人站在 12 m 外(bot 丟向目標腳下)
            Inkling enemy = SpawnEnemy("SensorEnemy_Bot", new Vector3(-10f, 0.1f, 16f), true);
            int bursts = PointSensor.BurstCount;
            bool threw = false;
            float hold = 0f;
            yield return BotAimThrow(config, standAt, Vector3.right, 12f, (t, h) =>
            {
                threw = t;
                hold = h;
            });
            yield return WaitUntil(() => PointSensor.BurstCount > bursts, 2f);
            yield return Frames(3);
            Vector3 center = PointSensor.LastBurstCenter;
            Check($"{Label} bot 用法(模擬 BotBrain:BotAimPoint 解 S3 彈道)丟向 12 m 外的敵人 → 被標記",
                threw && PointSensor.BurstCount == bursts + 1 && Marking.IsMarked(enemy, Player.Team),
                $"投出={threw} 按住 {hold:0.00} s 爆心離敵人 {Flat(center, enemy.transform.position):0.0} m 標記={Marking.IsMarked(enemy, Player.Team)}");
            yield return WaitUntil(() => LiveOf<PointSensor>() == null, 3f);
            RemoveEnemies();
        }
    }
}

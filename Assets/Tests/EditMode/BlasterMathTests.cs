using NUnit.Framework;
using SplatoonC.Core.Combat;
using UnityEngine;

namespace SplatoonC.Tests
{
    // 爆破槍(Blaster / Luna Blaster,Leanny v11.3.0 Blaster_Middle / Blaster_Short)的 S3 推導值、
    // 扳機時間軸(預備 / 冷卻 / 硬直)、跳射散布、bot 瞄準幾何
    public class BlasterMathTests
    {
        private static S3BlasterParams Blaster => S3BlasterParams.Blaster;
        private static S3BlasterParams Luna => S3BlasterParams.Luna;
        private const float F = 1f / 60f;

        [Test]
        public void 有效射程_Blaster10點14_Luna7點65()
        {
            // 0.945 × 9 + 0.9131 × 0.64 × (1 − 0.64⁴) / 0.36 + 0.285 = 10.141
            Assert.AreEqual(10.141f, Blaster.EffectiveRange, 0.005f);
            // 0.87 × 7 + 0.8484 × 0.64 × (1 − 0.64⁴) / 0.36 + 0.3 = 7.645
            Assert.AreEqual(7.645f, Luna.EffectiveRange, 0.005f);
        }

        [Test]
        public void 空爆距離_Blaster約9點86_Luna約7點35()
        {
            Assert.AreEqual(9.856f, Blaster.BurstDistance, 0.005f);
            Assert.AreEqual(7.345f, Luna.BurstDistance, 0.005f);
            Assert.AreEqual(Blaster.BurstDistance + 3.385f, Blaster.MaxSplashReach, 1e-4f);
        }

        [Test]
        public void 直線段等於空爆幀減4()
        {
            Assert.AreEqual(Blaster.BurstFrame - 4, Blaster.StraightFrames);
            Assert.AreEqual(Luna.BurstFrame - 4, Luna.StraightFrames);
        }

        [Test]
        public void 三狀態彈道第13F的水平位置接近空爆距離()
        {
            // 實際步進(含減速段的下限夾值)比有效射程公式多飛一點:第 13F 水平 ≈ 9.94 m、落下約 0.48 m
            BulletMotion motion = Blaster.Motion;
            BulletState state = BulletBallistics.Launch(Vector3.zero, Vector3.forward, motion);
            for (int i = 0; i < Blaster.BurstFrame; i++)
            {
                BulletBallistics.Step(ref state, motion);
            }
            Assert.AreEqual(9.938f, state.Position.z, 0.01f);
            Assert.AreEqual(-0.478f, state.Position.y, 0.01f);
            Assert.AreEqual(BulletPhase.Free, state.Phase, "第 13F 兩個速度都低於下限 → 自由落體");
        }

        [Test]
        public void 直擊一發_爆風兩發_TTK50F與40F()
        {
            Assert.AreEqual(1, Blaster.DirectHitsToSplat());
            Assert.AreEqual(2, Blaster.SplashHitsToSplat(Blaster.NearDamage));
            Assert.AreEqual(2, Blaster.SplashHitsToSplat(Blaster.FarDamage));
            Assert.AreEqual(50, Blaster.SplashTtkFrames());
            Assert.AreEqual(40, Luna.SplashTtkFrames());
        }

        [Test]
        public void 一槽10發與13發_射速_走速_時間軸()
        {
            Assert.AreEqual(10, Blaster.ShotsPerTank);
            Assert.AreEqual(13, Luna.ShotsPerTank);
            Assert.AreEqual(50f / 60f, Blaster.FireIntervalSeconds, 1e-6f);
            Assert.AreEqual(40f / 60f, Luna.FireIntervalSeconds, 1e-6f);
            Assert.AreEqual(2.7f, Blaster.MoveSpeedWhileFiring, 1e-4f, "0.045 u/F × 60");
            Assert.AreEqual(3.0f, Luna.MoveSpeedWhileFiring, 1e-4f, "0.05 u/F × 60");
            Assert.AreEqual(14, Blaster.FirstShotFrames, "4 + PreDelayFrame_HumanShot 10");
            Assert.AreEqual(24, Blaster.SquidFirstShotFrames, "9 + PreDelayFrame_SquidShot 15");
            Assert.AreEqual(22, Blaster.PostDelayFrames);
            Assert.AreEqual(26, Luna.PostDelayFrames);
            Assert.AreEqual(57f / 60f, Blaster.InkLockSeconds, 1e-6f);
        }

        [Test]
        public void 爆風線性70到50_撞地形半徑縮小()
        {
            Assert.AreEqual(70f, Blaster.ExplosionDamage(0.5f, false), 1e-4f);
            Assert.AreEqual(70f, Blaster.ExplosionDamage(1.025f, false), 1e-4f);
            Assert.AreEqual(60f, Blaster.ExplosionDamage((1.025f + 3.385f) * 0.5f, false), 0.11f);
            Assert.AreEqual(50f, Blaster.ExplosionDamage(3.385f, false), 1e-4f);
            Assert.AreEqual(0f, Blaster.ExplosionDamage(3.4f, false), 1e-4f);
            // 撞地形 × 0.4234:內圈 0.434、外圈 1.433
            Assert.AreEqual(70f, Blaster.ExplosionDamage(0.43f, true), 1e-4f);
            Assert.AreEqual(50f, Blaster.ExplosionDamage(1.433f, true), 0.05f);
            Assert.AreEqual(0f, Blaster.ExplosionDamage(1.5f, true), 1e-4f);
            // Luna:70 (1.7) → 50 (3.9),撞地形 × 0.4166
            Assert.AreEqual(70f, Luna.ExplosionDamage(1.7f, false), 1e-4f);
            Assert.AreEqual(50f, Luna.ExplosionDamage(3.9f, false), 1e-4f);
            Assert.AreEqual(0f, Luna.ExplosionDamage(1.7f, true), 1e-4f);
        }

        [Test]
        public void 沿路墨滴_空爆前6滴與4滴_上限7與6()
        {
            Assert.AreEqual(6, Blaster.DropletCount(Blaster.BurstDistance));
            Assert.AreEqual(4, Luna.DropletCount(Luna.BurstDistance));
            Assert.AreEqual(7, Blaster.DropletCount(20f));
            Assert.AreEqual(6, Luna.DropletCount(20f));
            Assert.AreEqual(0, Blaster.DropletCount(1.4f));
            Assert.AreEqual(1.62f * Mathf.Sqrt(1.2f), Blaster.DropletPaintRadius, 1e-4f);
        }

        [Test]
        public void 地面散布為零_跳射偏角上限10度且中位數在一半()
        {
            SpreadSettings s = Blaster.ToSpreadSettings();
            var model = new SpreadModel();
            for (int i = 0; i < 20; i++)
            {
                Assert.AreEqual(0f, model.NextYawOffset(i * 0.8f, s, false, 0f, (i * 0.37f) % 1f, 0.7f), 1e-6f);
            }
            // 空中 b 固定 0.5 → 偏角 = s × x(均勻分佈),中位數 = 一半
            Assert.AreEqual(5f, SpreadModel.AngleFor(s.JumpSwerveDeg, s.JumpBiasMax, 0.5f), 1e-4f);
            float max = 0f;
            for (int i = 0; i <= 10; i++)
            {
                float yaw = Mathf.Abs(model.NextYawOffset(20f + i, s, true, 0f, i / 10f, 0.2f));
                max = Mathf.Max(max, yaw);
            }
            Assert.AreEqual(10f, max, 1e-4f);
            // 起跳 70F 後收回地面的 0°
            Assert.AreEqual(0f, model.NextYawOffset(40f, s, true, 71f / 60f, 1f, 0.9f), 1e-4f);
            Assert.AreEqual(5.5f, Luna.ToSpreadSettings().JumpSwerveDeg);
        }

        [Test]
        public void 扳機_點一下也出膛_人形預備14F()
        {
            BlasterTiming timing = Timing(Blaster);
            var clock = new BlasterShotClock();
            float shotTime = -1f;
            bool fired = false;
            for (int frame = 0; frame < 40 && !fired; frame++)
            {
                // 只按第 0 幀
                fired = clock.Tick(frame == 0, frame * F, float.NegativeInfinity, timing, out shotTime);
                if (frame == 5)
                {
                    Assert.IsTrue(clock.Pending, "放開後仍在預備(已確定)");
                    Assert.AreEqual(5f / 14f, clock.WindupProgress(frame * F), 1e-3f);
                }
            }
            Assert.IsTrue(fired);
            Assert.AreEqual(14 * F, shotTime, 1e-5f);
        }

        [Test]
        public void 扳機_按住每50F一發()
        {
            BlasterTiming timing = Timing(Blaster);
            var clock = new BlasterShotClock();
            var shots = new System.Collections.Generic.List<float>();
            for (int frame = 0; frame < 200; frame++)
            {
                if (clock.Tick(true, frame * F, float.NegativeInfinity, timing, out float t))
                {
                    shots.Add(t);
                }
            }
            Assert.AreEqual(4, shots.Count, "14、64、114、164F");
            Assert.AreEqual(14 * F, shots[0], 1e-5f);
            Assert.AreEqual(50 * F, shots[1] - shots[0], 1e-5f);
            Assert.AreEqual(50 * F, shots[3] - shots[2], 1e-5f);

            // 幀率不整(每幀 22 ms,不對齊 60 Hz):排定的出膛時刻仍剛好每 50F,不會被幀邊界拖慢
            var uneven = new BlasterShotClock();
            var times = new System.Collections.Generic.List<float>();
            for (float now = 0f; now < 3.5f; now += 0.022f)
            {
                if (uneven.Tick(true, now, float.NegativeInfinity, timing, out float t))
                {
                    times.Add(t);
                }
            }
            Assert.GreaterOrEqual(times.Count, 4);
            for (int i = 1; i < times.Count; i++)
            {
                Assert.AreEqual(50 * F, times[i] - times[i - 1], 1e-4f);
            }
        }

        [Test]
        public void 扳機_烏賊態出來24F()
        {
            BlasterTiming timing = Timing(Blaster);
            var clock = new BlasterShotClock();
            float lastSquid = 1f;
            float shotTime = -1f;
            for (int frame = 61; frame < 120; frame++)
            {
                if (clock.Tick(true, frame * F, lastSquid, timing, out shotTime))
                {
                    break;
                }
            }
            Assert.AreEqual(lastSquid + 24 * F, shotTime, 1e-5f);
        }

        // Luna:第 0F 按下 → 14F 出膛;下一發的預備最早在 14 + 40 − 14 = 40F 開始(出膛 54F)
        private static System.Collections.Generic.List<float> LunaShots(System.Func<int, bool> held, int frames = 120)
        {
            BlasterTiming timing = Timing(Luna);
            var clock = new BlasterShotClock();
            var shots = new System.Collections.Generic.List<float>();
            for (int frame = 0; frame < frames; frame++)
            {
                if (clock.Tick(held(frame), frame * F, float.NegativeInfinity, timing, out float t))
                {
                    shots.Add(t);
                }
            }
            return shots;
        }

        [Test]
        public void 扳機_射完放開就不出下一發()
        {
            // 按住到第 30F(出膛後 16F)放開:下一發的預備時刻(40F)已經沒按 → 只有一發,PostDelay 後就能潛入
            BlasterTiming timing = Timing(Luna);
            var clock = new BlasterShotClock();
            int shots = 0;
            for (int frame = 0; frame < 120; frame++)
            {
                shots += clock.Tick(frame <= 30, frame * F, float.NegativeInfinity, timing, out _) ? 1 : 0;
                if (frame == 45)
                {
                    Assert.IsFalse(clock.Pending, "放開後不會確定下一發");
                }
            }
            Assert.AreEqual(1, shots);
            Assert.AreEqual(40 * F, clock.WindupAllowedAt(timing), 1e-5f, "下一發最早 14 + 40 − 14 = 40F 開始預備");
            Assert.IsFalse(clock.InPostDelay((14 + 26) * F + 1e-3f, timing), "出膛 26F 後 PostDelay 結束,可以潛入");
            Assert.AreEqual(1, LunaShots(frame => frame <= 30).Count);
        }

        [Test]
        public void 扳機_冷卻初期點一下不排隊_先行入力10F內會出()
        {
            // 第 20F 點一下(離可預備的 40F 還有 20F,超過先行入力 10F)→ 吞掉
            var early = LunaShots(frame => frame == 0 || frame == 20);
            Assert.AreEqual(1, early.Count, "冷卻初期的誤按不排進下一發");
            // 第 32F 點一下(8F 前,在先行入力內)→ 40F 開始預備、54F 出膛
            var buffered = LunaShots(frame => frame == 0 || frame == 32);
            Assert.AreEqual(2, buffered.Count);
            Assert.AreEqual(54 * F, buffered[1], 1e-5f);
            // 第 20F 起按住到 45F → 40F 還按著 → 54F 出膛
            var held = LunaShots(frame => frame == 0 || (frame >= 20 && frame <= 45));
            Assert.AreEqual(2, held.Count);
            Assert.AreEqual(54 * F, held[1], 1e-5f);
        }

        [Test]
        public void 扳機_按住連射每一發前都有14F預備()
        {
            BlasterTiming timing = Timing(Blaster);
            var clock = new BlasterShotClock();
            int shots = 0;
            float commitBeforeSecond = -1f;
            for (int frame = 0; frame < 70; frame++)
            {
                if (clock.Tick(true, frame * F, float.NegativeInfinity, timing, out _))
                {
                    shots++;
                }
                if (shots == 1 && clock.Pending && commitBeforeSecond < 0f)
                {
                    commitBeforeSecond = clock.CommitTime;
                    Assert.AreEqual(0f, clock.WindupProgress(frame * F), 1e-4f);
                }
            }
            Assert.AreEqual(2, shots);
            Assert.AreEqual(50 * F, commitBeforeSecond, 1e-5f, "第二發在 14 + 50 − 14 = 50F 開始預備");
            Assert.AreEqual(64 * F, clock.LastShotTime, 1e-5f);
        }

        [Test]
        public void 扳機_出膛後PostDelay與取消()
        {
            BlasterTiming timing = Timing(Blaster);
            var clock = new BlasterShotClock();
            Assert.IsFalse(clock.InPostDelay(0f, timing));
            float shot = 0f;
            for (int frame = 0; frame < 20; frame++)
            {
                if (clock.Tick(frame == 0, frame * F, float.NegativeInfinity, timing, out shot))
                {
                    break;
                }
            }
            Assert.IsTrue(clock.InPostDelay(shot + 21 * F, timing));
            Assert.IsFalse(clock.InPostDelay(shot + 22 * F + 1e-3f, timing));
            Assert.AreEqual(0.5f, clock.CooldownProgress(shot + 25 * F, timing), 1e-3f);

            clock.Tick(true, 2f, float.NegativeInfinity, timing, out _);
            Assert.IsTrue(clock.Pending);
            clock.Cancel();
            Assert.IsFalse(clock.Pending);
            Assert.IsFalse(clock.Tick(false, 2f + 20 * F, float.NegativeInfinity, timing, out _), "取消的那一發不出膛");
        }

        [Test]
        public void 扳機_到出膛還要幾秒_給bot補提前量()
        {
            BlasterTiming timing = Timing(Blaster);
            var clock = new BlasterShotClock();
            Assert.AreEqual(14 * F, clock.SecondsUntilShot(0f, float.NegativeInfinity, timing), 1e-5f, "閒置:人形預備 14F");
            Assert.AreEqual(24 * F, clock.SecondsUntilShot(1f, 1f, timing), 1e-5f, "剛出烏賊態:24F");
            clock.Tick(true, 0f, float.NegativeInfinity, timing, out _);
            Assert.AreEqual(9 * F, clock.SecondsUntilShot(5 * F, float.NegativeInfinity, timing), 1e-5f, "預備中:剩下的預備");
            for (int frame = 1; frame <= 14; frame++)
            {
                clock.Tick(false, frame * F, float.NegativeInfinity, timing, out _);
            }
            Assert.AreEqual(44 * F, clock.SecondsUntilShot(20 * F, float.NegativeInfinity, timing), 1e-5f,
                "冷卻中:max(20 + 14, 14 + 50) − 20 = 44F");
        }

        [Test]
        public void bot瞄準_近距離瞄腳邊_空爆距離瞄胸口()
        {
            float burst = Blaster.BurstDistance;
            Assert.AreEqual(BlasterAim.FeetAimHeight, BlasterAim.AimHeight(2f, burst), 1e-4f, "近距離瞄腳邊(小腿)");
            Assert.AreEqual(BlasterAim.FeetAimHeight, BlasterAim.AimHeight(burst - 2.6f, burst), 1e-4f);
            // 腳邊瞄點要高於彈對地形的判定半徑(0.2):否則彈在目標前方就先撞地
            Assert.Greater(BlasterAim.FeetAimHeight, 0.2f);
            Assert.Less(BlasterAim.FeetAimHeight, 0.5f);
            Assert.AreEqual(BlasterAim.ChestAimHeight, BlasterAim.AimHeight(burst, burst), 1e-4f);
            Assert.AreEqual(BlasterAim.ChestAimHeight, BlasterAim.AimHeight(burst + 2f, burst), 1e-4f);
            float mid = BlasterAim.AimHeight(burst - 1.6f, burst);
            Assert.Greater(mid, BlasterAim.FeetAimHeight);
            Assert.Less(mid, BlasterAim.ChestAimHeight);
        }

        [Test]
        public void bot瞄準_掩體空爆點與可及判定()
        {
            Vector3 origin = new Vector3(0f, 1f, 0f);
            Vector3 aim = new Vector3(0f, 3f, 12f);
            Vector3 burst = BlasterAim.BurstPoint(origin, aim, 9.856f);
            Assert.AreEqual(9.856f, Vector3.Distance(origin, burst), 1e-3f);
            Assert.IsTrue(BlasterAim.BurstReaches(burst, new Vector3(0f, 0.9f, 10.5f), 3.385f));
            Assert.IsFalse(BlasterAim.BurstReaches(burst, new Vector3(0f, 0.9f, 14f), 3.385f));
            Assert.Less(BlasterAim.CoverAimHeight(0), BlasterAim.CoverAimHeight(1));
        }

        private static BlasterTiming Timing(in S3BlasterParams s3)
        {
            return BlasterTiming.FromFrames(s3.FirstShotFrames, s3.SquidFirstShotFrames, s3.RepeatFrame, s3.PostDelayFrames);
        }
    }
}

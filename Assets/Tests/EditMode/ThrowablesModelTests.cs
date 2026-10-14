using NUnit.Framework;
using SplatoonC.Core.Combat;
using UnityEngine;

namespace SplatoonC.Tests
{
    // 副武器 A2「投擲物 II」純邏輯(S3 v11.3.0:WeaponBombTorpedo / WeaponBombFizzy / WeaponPointSensor / WeaponLineMarker;
    // research 03 §1、§2.6、§2.10、§2.13、§2.14)
    public class ThrowablesModelTests
    {
        // ── S3 通用投擲彈道 ──

        [Test]
        public void 通用彈道平丟距離與JP實測一致()
        {
            float bomb = S3ThrowModel.PredictFlatRange(S3ThrowParams.Bomb(1.12f), 0f, 1f, out int frames);
            Assert.AreEqual(16.2f, bomb, 0.3f, "1.12 u/F 平丟 ≈ 16.2 u(JP 3.0~3.5 線)");
            Assert.AreEqual(31, frames, 1, "約 31F 落地");
            Assert.AreEqual(20.2f, S3ThrowModel.PredictFlatRange(TorpedoParams.S3.Throw, 0f, 1f, out _), 0.3f,
                "Torpedo 1.40 u/F ≈ 20.2 u(JP 4.4 線)");
            Assert.AreEqual(19.6f, S3ThrowModel.PredictFlatRange(FizzyBombParams.S3.Throw, 0f, 1f, out _), 0.3f,
                "Fizzy 1.36 u/F ≈ 19.6 u(JP 4.2 線)");
            Assert.AreEqual(19.9f, S3ThrowModel.PredictFlatRange(PointSensorParams.S3.Throw, 0f, 1f, out _), 0.3f,
                "Point Sensor 1.38 u/F ≈ 19.9 u(JP 4.0 線)");
        }

        [Test]
        public void 出手速度加上0點24往上且垂直下限負0點4()
        {
            S3ThrowParams p = S3ThrowParams.Bomb(1.4f);
            Vector3 level = S3ThrowModel.LaunchVelocity(p, Vector3.forward, Vector3.zero);
            Assert.AreEqual(1.4f, level.z, 1e-5f);
            Assert.AreEqual(0.24f, level.y, 1e-5f);
            Vector3 down = S3ThrowModel.LaunchVelocity(p, Vector3.down, Vector3.zero);
            Assert.AreEqual(-0.4f, down.y, 1e-5f, "往正下方丟:垂直速度被夾在 −0.4");
        }

        [Test]
        public void 繼承動量前向乘2橫向乘1點6上升中加上限0點32()
        {
            S3ThrowParams p = S3ThrowParams.Bomb(1.36f);
            Vector3 still = S3ThrowModel.LaunchVelocity(p, Vector3.forward, Vector3.zero);
            Vector3 moving = S3ThrowModel.LaunchVelocity(p, Vector3.forward, new Vector3(0.1f, 0.2f, 0.1f));
            Assert.AreEqual(0.2f, moving.z - still.z, 1e-5f, "前向 0.1 × 2.0");
            Assert.AreEqual(0.16f, moving.x - still.x, 1e-5f, "橫向 0.1 × 1.6");
            Assert.AreEqual(0.32f, moving.y - still.y, 1e-5f, "上升 min(0.2 × 4, 0.32)");
            float withMomentum = 0f;
            for (int pitch = -10; pitch <= 40; pitch++)
            {
                float rad = pitch * Mathf.Deg2Rad;
                Vector3 v = S3ThrowModel.LaunchVelocity(p, new Vector3(0f, Mathf.Sin(rad), Mathf.Cos(rad)),
                    new Vector3(0f, 0.08f, 0.192f));
                Vector3 pos = new Vector3(0f, 1f, 0f);
                for (int f = 0; f < 400 && pos.y > 0f; f++)
                {
                    S3ThrowModel.StepAir(p, ref pos, ref v);
                }
                withMomentum = Mathf.Max(withMomentum, pos.z);
            }
            Assert.AreEqual(29f, withMomentum, 1.5f, "游泳衝刺 + 跳躍丟出 ≈ 29 u(JP 慣性込み 6.0 線)");
        }

        [Test]
        public void 地面滾動速度每幀乘1減阻力()
        {
            Assert.AreEqual(0.7f, S3ThrowModel.StepGroundSpeed(1f, 0.3f), 1e-6f);
            Assert.AreEqual(0.81f, S3ThrowModel.StepGroundSpeed(1f, 0.19f), 1e-6f);
        }

        // ── Torpedo ──

        [Test]
        public void Torpedo第6F起搜尋且變身45F()
        {
            TorpedoParams p = TorpedoParams.S3;
            Assert.IsFalse(TorpedoModel.CanSearch(p, 5));
            Assert.IsTrue(TorpedoModel.CanSearch(p, 6));
            Assert.AreEqual(45, TorpedoModel.TransformFrames(p), "轉向 25 + BetweenRotEndAndStartChaseFrame 20");
            Assert.AreEqual(20f, p.Hp);
            Assert.AreEqual(30, p.BurstFrame);
        }

        [Test]
        public void Torpedo搜尋窗水平9上下不對稱()
        {
            TorpedoParams p = TorpedoParams.S3;
            Assert.IsTrue(TorpedoModel.InSearchWindow(p, new Vector3(8.9f, 0f, 0f)));
            Assert.IsFalse(TorpedoModel.InSearchWindow(p, new Vector3(6.5f, 0f, 6.5f)), "水平 9.19 > 9");
            Assert.IsTrue(TorpedoModel.InSearchWindow(p, new Vector3(0f, -4.9f, 3f)), "下方 5 以內");
            Assert.IsFalse(TorpedoModel.InSearchWindow(p, new Vector3(0f, -5.1f, 3f)));
            Assert.IsTrue(TorpedoModel.InSearchWindow(p, new Vector3(0f, 2.4f, 3f)), "上方 2.5 以內");
            Assert.IsFalse(TorpedoModel.InSearchWindow(p, new Vector3(0f, 2.6f, 3f)));
        }

        [Test]
        public void Torpedo散彈仰角65度初速042到048且水平等分()
        {
            TorpedoParams p = TorpedoParams.S3;
            for (int i = 0; i < p.DropletCount; i++)
            {
                Vector3 v = TorpedoModel.DropletVelocity(p, i, 10f, i / (float)(p.DropletCount - 1));
                float speed = v.magnitude;
                Assert.GreaterOrEqual(speed, 0.42f - 1e-4f);
                Assert.LessOrEqual(speed, 0.48f + 1e-4f);
                float pitch = Mathf.Asin(v.y / speed) * Mathf.Rad2Deg;
                Assert.AreEqual(65f, pitch, 0.01f);
            }
            Vector3 a = TorpedoModel.DropletVelocity(p, 0, 0f, 0.5f);
            Vector3 b = TorpedoModel.DropletVelocity(p, 1, 0f, 0.5f);
            Assert.AreEqual(45f, Vector3.Angle(new Vector3(a.x, 0f, a.z), new Vector3(b.x, 0f, b.z)), 0.01f, "8 顆 → 每 45°");
            float reach = TorpedoModel.DropletFlatReach(p, 0.5f, 0.5f);
            Assert.Greater(reach, 1.5f, "散彈落在爆心外圍");
            Assert.Less(reach, 4f);
        }

        // ── Fizzy Bomb ──

        [Test]
        public void Fizzy段數40F與120F()
        {
            FizzyBombParams p = FizzyBombParams.S3;
            Assert.AreEqual(120, FizzyBombModel.MaxChargeFrames(p));
            Assert.AreEqual(1, FizzyBombModel.LevelFor(p, 0f));
            Assert.AreEqual(1, FizzyBombModel.LevelFor(p, 39.5f));
            Assert.AreEqual(2, FizzyBombModel.LevelFor(p, 40f));
            Assert.AreEqual(2, FizzyBombModel.LevelFor(p, 119.5f));
            Assert.AreEqual(3, FizzyBombModel.LevelFor(p, 120f));
            Assert.AreEqual(3, FizzyBombModel.LevelFor01(p, 1f));
            Assert.AreEqual(2, FizzyBombModel.LevelFor01(p, 0.5f));
            Assert.AreEqual(5, FizzyBombModel.HoldFramesForLevel(p, 1));
            Assert.AreEqual(45, FizzyBombModel.HoldFramesForLevel(p, 2));
            Assert.AreEqual(125, FizzyBombModel.HoldFramesForLevel(p, 3));
        }

        [Test]
        public void Fizzy第1爆著地30F後之後8F()
        {
            FizzyBombParams p = FizzyBombParams.S3;
            Assert.AreEqual(30, FizzyBombModel.BurstDelayFrames(p, 0));
            Assert.AreEqual(8, FizzyBombModel.BurstDelayFrames(p, 1));
            Assert.AreEqual(8, FizzyBombModel.BurstDelayFrames(p, 2));
        }

        [Test]
        public void Fizzy每爆半徑與塗墨遞增()
        {
            FizzyBombParams p = FizzyBombParams.S3;
            Assert.AreEqual(1.6f, FizzyBombModel.NearRadius(p, 0), 1e-5f);
            Assert.AreEqual(1.95f, FizzyBombModel.NearRadius(p, 1), 1e-5f);
            Assert.AreEqual(2.6f, FizzyBombModel.NearRadius(p, 2), 1e-5f);
            Assert.AreEqual(3.8f, FizzyBombModel.FarRadius(p, 0), 1e-5f);
            Assert.AreEqual(4.5f, FizzyBombModel.FarRadius(p, 1), 1e-5f);
            Assert.AreEqual(5.45f, FizzyBombModel.FarRadius(p, 2), 1e-5f);
            Assert.AreEqual(3f, FizzyBombModel.PaintRadius(p, 0), 1e-5f);
            Assert.AreEqual(3.8f, FizzyBombModel.PaintRadius(p, 2), 1e-5f);
            Assert.AreEqual(1.2f, FizzyBombModel.TrailHalfWidth(p, 1), 1e-5f);
            Assert.AreEqual(1.32f, FizzyBombModel.TrailHalfWidth(p, 3), 1e-5f);
            Assert.AreEqual(50f, p.NearDamage);
            Assert.AreEqual(35f, p.FarDamage);
        }

        [Test]
        public void Fizzy連打折抵每幀最多兌現2點5F()
        {
            FizzyBombParams p = FizzyBombParams.S3;
            float bank = p.MashJumpFrames;
            Assert.AreEqual(2.5f, FizzyBombModel.TakeMash(p, ref bank, 1f), 1e-5f);
            Assert.AreEqual(2.5f, FizzyBombModel.TakeMash(p, ref bank, 1f), 1e-5f);
            Assert.AreEqual(2.5f, FizzyBombModel.TakeMash(p, ref bank, 1f), 1e-5f);
            Assert.AreEqual(0.5f, FizzyBombModel.TakeMash(p, ref bank, 1f), 1e-5f, "8F = 2.5 + 2.5 + 2.5 + 0.5(JP)");
            Assert.AreEqual(0f, FizzyBombModel.TakeMash(p, ref bank, 1f));
            // 理論最快:每幀 1 + 2.5 = 3.5F → 35F 蓄滿 120F
            float charge = 0f;
            float fastBank = 0f;
            int frames = 0;
            while (charge < 120f && frames < 200)
            {
                fastBank += p.MashJumpFrames;
                charge += 1f + FizzyBombModel.TakeMash(p, ref fastBank, 1f);
                frames++;
            }
            Assert.AreEqual(35, frames, "JP:理論最速 35F 蓄滿");
        }

        [Test]
        public void Fizzy彈跳初速與仰角在S3範圍()
        {
            FizzyBombParams p = FizzyBombParams.S3;
            for (int i = 0; i <= 4; i++)
            {
                float t = i / 4f;
                Vector3 v = FizzyBombModel.HopVelocity(p, Vector3.forward, t, t, 0.5f);
                float speed = v.magnitude;
                Assert.AreEqual(Mathf.Lerp(0.24f, 0.32f, t), speed, 1e-4f);
                Assert.AreEqual(Mathf.Lerp(48f, 58f, t), Mathf.Asin(v.y / speed) * Mathf.Rad2Deg, 0.01f);
                Assert.AreEqual(0f, v.x, 1e-5f, "yaw01 0.5 = 正前方");
            }
        }

        // ── Point Sensor ──

        [Test]
        public void PointSensor標記480F從命中起算範圍150F()
        {
            PointSensorParams p = PointSensorParams.S3;
            Assert.AreEqual(8f, PointSensorModel.MarkSecondsAt(p, 0), 1e-5f);
            Assert.AreEqual(7f, PointSensorModel.MarkSecondsAt(p, 60), 1e-5f);
            Assert.AreEqual((480f - 149f) / 60f, PointSensorModel.MarkSecondsAt(p, 149), 1e-5f);
            Assert.AreEqual(0f, PointSensorModel.MarkSecondsAt(p, 150), "範圍停留 150F 後不再標記");
            Assert.IsTrue(PointSensorModel.Inside(p, Vector3.zero, new Vector3(5.9f, 0f, 0f)));
            Assert.IsFalse(PointSensorModel.Inside(p, Vector3.zero, new Vector3(4.3f, 0f, 4.3f)), "6.08 > 6.0");
        }

        // ── Angle Shooter ──

        [Test]
        public void AngleShooter射程44點1()
        {
            AngleShooterParams p = AngleShooterParams.S3;
            Assert.AreEqual(44.1f, AngleShooterModel.Range(p), 1e-4f);
            Assert.AreEqual(97, AngleShooterModel.LifetimeFrames(p), "飛行 7 + 殘留 90");
        }

        [Test]
        public void AngleShooter牆面鏡射地面出射角乘0點1()
        {
            AngleShooterParams p = AngleShooterParams.S3;
            Vector3 incoming = new Vector3(1f, 0f, 1f).normalized;
            Vector3 wall = AngleShooterModel.Reflect(p, incoming, Vector3.back);
            Assert.AreEqual(0f, Vector3.Distance(wall, new Vector3(1f, 0f, -1f).normalized), 1e-4f, "牆:45° → 45°");

            float down = 20f * Mathf.Deg2Rad;
            Vector3 dive = new Vector3(0f, -Mathf.Sin(down), Mathf.Cos(down));
            Vector3 floor = AngleShooterModel.Reflect(p, dive, Vector3.up);
            float outAngle = Mathf.Asin(floor.y) * Mathf.Rad2Deg;
            Assert.AreEqual(2f, outAngle, 0.01f, "地面:入射 20° → 出射 2°(貼地前進)");
            Assert.Greater(floor.z, 0.99f);

            Vector3 away = AngleShooterModel.Reflect(p, Vector3.up, Vector3.up);
            Assert.AreEqual(Vector3.up, away, "已離開表面:方向不變");
        }

        [Test]
        public void AngleShooter標記300F從丟出起算()
        {
            AngleShooterParams p = AngleShooterParams.S3;
            Assert.AreEqual(5f, AngleShooterModel.MarkSecondsAt(p, 0), 1e-5f);
            Assert.AreEqual(3.5f, AngleShooterModel.MarkSecondsAt(p, 90), 1e-5f, "丟出後 1.5 s 才碰到 → 3.5 s");
            Assert.AreEqual(0f, AngleShooterModel.MarkSecondsAt(p, 300));
            Assert.AreEqual(40f, p.DirectDamage);
        }
    }
}

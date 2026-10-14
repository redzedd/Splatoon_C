using NUnit.Framework;
using SplatoonC.Core.Combat;

namespace SplatoonC.Tests
{
    // 滾筒 S3 參數(Leanny v11.3.0)與純邏輯:分段距離傷害、時間衰減、弱甩、一擊必殺距離、推滾耗墨/塗寬、墨團排列、衝刺計時
    public class RollerParamsTests
    {
        private static S3RollerParams Splat => S3RollerParams.SplatRoller;
        private static S3RollerParams Carbon => S3RollerParams.CarbonRoller;
        private static S3RollerParams Dynamo => S3RollerParams.DynamoRoller;

        [Test]
        public void Splat橫甩中央_150到42_100到72_35到107()
        {
            RollerDamageCurve inside = Splat.Horizontal.Damage.Inside;
            Assert.AreEqual(150f, inside.Evaluate(0f), 1e-4f);
            Assert.AreEqual(150f, inside.Evaluate(4.2f), 1e-4f);
            Assert.AreEqual(125f, inside.Evaluate(5.7f), 1e-3f, "4.2 → 7.2 線性");
            Assert.AreEqual(100f, inside.Evaluate(7.2f), 1e-4f);
            Assert.AreEqual(67.5f, inside.Evaluate(8.95f), 1e-3f, "7.2 → 10.7 線性到 35");
            Assert.AreEqual(35f, inside.Evaluate(10.7f), 1e-4f);
            Assert.AreEqual(35f, inside.Evaluate(30f), 1e-4f, "之後維持最小值");
        }

        [Test]
        public void Splat橫甩側邊_100到20_50到72_35到92()
        {
            RollerDamageCurve outside = Splat.Horizontal.Damage.Outside;
            Assert.AreEqual(100f, outside.Evaluate(2f), 1e-4f);
            Assert.AreEqual(50f, outside.Evaluate(7.2f), 1e-4f);
            Assert.AreEqual(35f, outside.Evaluate(9.2f), 1e-4f);
            Assert.AreEqual(2f, outside.LethalDistance(), 1e-4f, "側邊墨團只有 2 m 內一擊");
        }

        [Test]
        public void 一擊必殺距離_橫甩72_47_129_直甩82_655_140()
        {
            Assert.AreEqual(7.2f, Splat.HorizontalLethalRange, 1e-3f);
            Assert.AreEqual(4.7f, Carbon.HorizontalLethalRange, 1e-3f);
            Assert.AreEqual(12.9f, Dynamo.HorizontalLethalRange, 1e-3f);
            Assert.AreEqual(8.2f, Splat.VerticalLethalRange, 1e-3f);
            Assert.AreEqual(6.551f, Carbon.VerticalLethalRange, 2e-3f, "120@4.2 → 50@12.43 線性,100 在 6.55");
            Assert.AreEqual(14.0f, Dynamo.VerticalLethalRange, 1e-3f);
        }

        [Test]
        public void 時間衰減_25F後線性到45F剩一半()
        {
            RollerFlingDamage damage = Splat.Horizontal.Damage;
            Assert.AreEqual(150f, damage.Evaluate(3f, 25, true, false), 1e-4f);
            Assert.AreEqual(112.5f, damage.Evaluate(3f, 35, true, false), 1e-4f, "一半時間 → ×0.75");
            Assert.AreEqual(75f, damage.Evaluate(3f, 45, true, false), 1e-4f);
            Assert.AreEqual(17.5f, damage.Evaluate(20f, 60, true, false), 1e-4f, "最小 35 × 0.5 = 17.5");
        }

        [Test]
        public void 弱甩傷害乘025_直甩乘05_Dynamo中央小墨團0傷()
        {
            Assert.AreEqual(37.5f, Splat.Horizontal.Damage.Evaluate(3f, 5, true, true), 1e-4f);
            Assert.AreEqual(75f, Splat.Vertical.Damage.Evaluate(3f, 5, true, true), 1e-4f);
            RollerFlingUnit center = Dynamo.Horizontal.Units[1];
            Assert.AreEqual(0f, Dynamo.Horizontal.Damage.Evaluate(3f, 5, true, false, center.DamageRate), 1e-4f);
        }

        [Test]
        public void 傷害取整到01()
        {
            // 150 → 100 在 4.2–7.2:4.3 m = 148.333… → 148.3
            Assert.AreEqual(148.3f, Splat.Horizontal.Damage.Evaluate(4.3f, 1, true, false), 1e-4f);
        }

        [Test]
        public void 中央判定_偏角16度內()
        {
            RollerFlingDamage damage = Splat.Horizontal.Damage;
            Assert.IsTrue(damage.IsInside(0f));
            Assert.IsTrue(damage.IsInside(-16f));
            Assert.IsFalse(damage.IsInside(18f));
        }

        [Test]
        public void 墨團數_橫甩13_13_18_直甩5_弱甩較少()
        {
            Assert.AreEqual(13, Splat.Horizontal.BulletCount(false));
            Assert.AreEqual(13, Carbon.Horizontal.BulletCount(false));
            Assert.AreEqual(18, Dynamo.Horizontal.BulletCount(false));
            Assert.AreEqual(5, Splat.Vertical.BulletCount(false));
            Assert.AreEqual(5, Dynamo.Vertical.BulletCount(false));
            Assert.AreEqual(4, Splat.Horizontal.BulletCount(true), "12 → 3 + 中央 1");
            Assert.AreEqual(3, Splat.Vertical.BulletCount(true), "1 + 2 + 0");
        }

        [Test]
        public void 耗墨與墨量鎖()
        {
            Assert.AreEqual(0.085f, Splat.Horizontal.InkConsume, 1e-6f);
            Assert.AreEqual(11, Splat.FlicksPerTank);
            Assert.AreEqual(25, Carbon.FlicksPerTank);
            Assert.AreEqual(4, Dynamo.FlicksPerTank);
            Assert.AreEqual(43f / 60f, Splat.Horizontal.InkLockSeconds, 1e-6f);
            Assert.AreEqual(58f / 60f, Splat.Vertical.InkLockSeconds, 1e-6f);
            Assert.AreEqual(21f / 60f, Splat.Horizontal.SwingSeconds, 1e-6f);
            Assert.AreEqual(10f / 60f, Carbon.Horizontal.SwingSeconds, 1e-6f);
            Assert.AreEqual(45f / 60f, Dynamo.Horizontal.SwingSeconds, 1e-6f);
            Assert.AreEqual(16.667f, Splat.FullTankRollSeconds, 1e-2f, "衝刺推滾 0.1%/F → 1000F");
        }

        [Test]
        public void 甩動循環_至少舉起加6F_S2連甩間隔()
        {
            Assert.AreEqual(43, Splat.Horizontal.CycleFrames);
            Assert.AreEqual(40, Carbon.Horizontal.CycleFrames);
            Assert.AreEqual(55, Dynamo.Horizontal.CycleFrames);
            Assert.AreEqual(70, Dynamo.Vertical.CycleFrames, "max(55 + 6, InkRecoverStop 70)");
        }

        [Test]
        public void 推滾耗墨隨速度_002到01每幀()
        {
            RollerRollParams roll = Splat.Roll;
            Assert.AreEqual(0f, roll.InkPerFrame(0f), 1e-7f, "靜止不耗墨");
            Assert.AreEqual(0.0002f, roll.InkPerFrame(0.02f), 1e-7f);
            Assert.AreEqual(0.001f, roll.InkPerFrame(0.132f), 1e-7f);
            float normal = roll.InkPerFrame(0.108f);
            Assert.AreEqual(0.0002f + 0.0008f * (0.088f / 0.112f), normal, 1e-6f);
            // 每秒:衝刺 6%/s、普通 ≈ 4.97%/s(S3「1.2%–6%/s」)
            Assert.AreEqual(0.06f, roll.InkPerFrame(0.132f) * 60f, 1e-5f);
        }

        [Test]
        public void 推滾塗寬_衝刺時56()
        {
            Assert.AreEqual(2.8f, Splat.Roll.PaintHalfWidth(0.132f), 1e-5f);
            Assert.AreEqual(2.2f, Carbon.Roll.PaintHalfWidth(0.16f), 1e-5f);
            Assert.AreEqual(3.4f, Dynamo.Roll.PaintHalfWidth(0.108f), 1e-5f);
            Assert.Less(Splat.Roll.PaintHalfWidth(0.05f), 2.8f, "慢速較窄");
            Assert.AreEqual(1.4f, Splat.Roll.PaintHalfWidth(0f), 1e-5f, "最窄 = 一半");
        }

        [Test]
        public void 推滾速度_普通衝刺急轉()
        {
            Assert.AreEqual(6.48f, Splat.RollSpeedNormalPerSecond, 1e-4f);
            Assert.AreEqual(7.92f, Splat.RollSpeedDashPerSecond, 1e-4f);
            Assert.AreEqual(9.6f, Carbon.RollSpeedDashPerSecond, 1e-4f);
            Assert.AreEqual(5.28f, Dynamo.RollSpeedNormalPerSecond, 1e-4f);
            Assert.AreEqual(0.108f, Splat.Roll.SpeedFor(true, true), 1e-6f);
        }

        [Test]
        public void 衝刺計時_連續推滾90F後衝刺_停下或急轉歸零()
        {
            RollerRollParams roll = Splat.Roll;
            var dash = new RollerDashTracker();
            float cap = 0f;
            for (int i = 0; i < 89; i++)
            {
                cap = dash.Advance(roll, 1f, 0.108f, 0f, out _);
            }
            Assert.AreEqual(0.108f, cap, 1e-6f, "第 89F 還是普通速度");
            cap = dash.Advance(roll, 1f, 0.108f, 0f, out _);
            Assert.IsTrue(dash.IsDashing(roll));
            Assert.AreEqual(0.132f, cap, 1e-6f, "第 90F 進衝刺");
            cap = dash.Advance(roll, 1f, 0.132f, 5f, out bool turning);
            Assert.IsTrue(turning);
            Assert.AreEqual(0.108f, cap, 1e-6f, "急轉掉回 SpeedDashTurnBreak");
            Assert.IsFalse(dash.IsDashing(roll), "急轉後衝刺重算");
            for (int i = 0; i < 30; i++)
            {
                dash.Advance(roll, 1f, 0.108f, 0f, out _);
            }
            dash.Advance(roll, 1f, 0f, 0f, out _);
            Assert.AreEqual(0f, dash.RollFrames, "停下歸零");
        }

        [Test]
        public void Carbon衝刺只要20F()
        {
            RollerRollParams roll = Carbon.Roll;
            var dash = new RollerDashTracker();
            for (int i = 0; i < 40; i++)
            {
                dash.Advance(roll, 0.5f, 0.128f, 0f, out _);
            }
            Assert.IsTrue(dash.IsDashing(roll), "0.5F × 40 = 20F");
        }

        [Test]
        public void 橫甩墨團扇形對稱_18度_出生點橫向08()
        {
            RollerFlingUnit unit = Splat.Horizontal.Units[0];
            RollerGlobSpawn first = RollerMotion.Layout(unit, 0, 12, false, 0.5f, 0.5f);
            RollerGlobSpawn last = RollerMotion.Layout(unit, 11, 12, false, 0.5f, 0.5f);
            Assert.AreEqual(-18f, first.YawDeg, 1e-4f);
            Assert.AreEqual(18f, last.YawDeg, 1e-4f);
            Assert.AreEqual(-0.4f, first.Lateral, 1e-4f);
            Assert.AreEqual(0.4f, last.Lateral, 1e-4f);
            Assert.AreEqual(1.05f, first.Speed, 1e-4f, "亂數 0.5 = 基準速度");
            Assert.AreEqual(1.41f, RollerMotion.Layout(unit, 3, 12, false, 1f, 0.5f).Speed, 1e-4f, "+0.36");
            Assert.AreEqual(0.525f, RollerMotion.Layout(unit, 3, 12, true, 0.5f, 0.5f).Speed, 1e-4f, "弱甩 × 0.5");
            RollerGlobSpawn jittered = RollerMotion.Layout(unit, 0, 12, false, 0.5f, 0f);
            Assert.GreaterOrEqual(jittered.YawDeg, -18f, "抖動不超出扇形");
        }

        [Test]
        public void 直甩一列_逐顆變慢變低()
        {
            RollerFlingUnit row = Splat.Vertical.Units[1];
            RollerGlobSpawn a = RollerMotion.Layout(row, 0, 2, false, 0.5f, 0.5f);
            RollerGlobSpawn b = RollerMotion.Layout(row, 1, 2, false, 0.5f, 0.5f);
            Assert.AreEqual(1.6338f, a.Speed, 1e-4f);
            Assert.AreEqual(1.4338f, b.Speed, 1e-4f);
            Assert.AreEqual(5f, a.PitchDeg, 1e-4f);
            Assert.AreEqual(2.5f, b.PitchDeg, 1e-4f);
            Assert.AreEqual(-0.25f, a.Height, 1e-4f);
            Assert.AreEqual(0f, b.Height, 1e-4f);
            Assert.AreEqual(0f, a.YawDeg, 1e-4f, "直甩沒有水平偏角");
        }

        [Test]
        public void 墨團落點半寬_近到DistanceFar線性_飛久縮到06()
        {
            RollerSwingParams h = Splat.Horizontal;
            RollerFlingUnit u = h.Units[0];
            float At(float distance, int frame) => InkStampWidth.HalfWidth(u.PaintWidthHalfNear, u.PaintWidthHalfFar,
                h.PaintDistanceFar, distance, frame, h.PaintShrinkRate, h.PaintShrinkStartFrame, h.PaintShrinkEndFrame);
            Assert.AreEqual(1.89f, At(0.5f, 10), 1e-4f, "≤ 1.1 u = WidthHalfNear");
            Assert.AreEqual(1.6275f, At(12f, 10), 1e-4f, "Splat 橫甩 DistanceFar 12");
            Assert.AreEqual(1.6275f, At(20f, 10), 1e-4f, "之後維持");
            Assert.AreEqual(1.89f * 0.8f, At(0.5f, 35), 1e-4f, "20 → 50F 的一半 = × 0.8");
            Assert.AreEqual(1.89f * 0.6f, At(0.5f, 60), 1e-4f, "50F 之後 × 0.6");
            Assert.AreEqual(8.5f, Carbon.Horizontal.PaintDistanceFar, 1e-4f);
            Assert.AreEqual(30f, Dynamo.Vertical.PaintDistanceFar, 1e-4f);
            // 其他武器(DistanceFar 0、不縮)= 射擊槍的 1.1 → 20 m
            Assert.AreEqual(1.71f, InkStampWidth.HalfWidth(1.93f, 1.71f, 0f, 20f, 60, 0f, 0, 0), 1e-4f);
            Assert.AreEqual(1.82f, InkStampWidth.HalfWidth(1.93f, 1.71f, 0f, 10.55f, 60, 0f, 0, 0), 1e-4f);
        }

        [Test]
        public void 接觸傷害與擊退參數()
        {
            Assert.AreEqual(125f, Splat.Roll.Damage);
            Assert.AreEqual(70f, Carbon.Roll.Damage);
            Assert.AreEqual(125f, Dynamo.Roll.Damage);
            Assert.AreEqual(420f * KnockbackModel.SpeedPerAccel, Splat.Roll.OpponentKnockbackSpeed(0f), 1e-5f);
            Assert.AreEqual(800f * KnockbackModel.SpeedPerAccel, Splat.Roll.OpponentKnockbackSpeed(1f), 1e-5f);
            Assert.AreEqual(280f * KnockbackModel.SpeedPerAccel, Splat.Roll.SelfKnockbackSpeed(1f, false), 1e-5f);
        }
    }
}

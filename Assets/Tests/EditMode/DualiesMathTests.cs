using NUnit.Framework;
using SplatoonC.Core.Combat;
using UnityEngine;

namespace SplatoonC.Tests
{
    // 雙槍(Splat Dualies,Leanny v11.3.0 Maneuver_Normal)的 S3 推導值、閃避翻滾時間軸、瞄準補償
    public class DualiesMathTests
    {
        private static S3DualiesParams Dualies => S3DualiesParams.SplatDualies;
        private static DodgeRollParams Roll => DodgeRollParams.SplatDualies;

        [Test]
        public void 有效射程等於S3的10點89()
        {
            // 2.37 × 3 + 2.3425 × 0.64 × (1 − 0.64⁴) / 0.36 + 0.31 = 10.886
            Assert.AreEqual(10.886f, Dualies.EffectiveRange, 0.005f);
        }

        [Test]
        public void 四發擊倒_一般TTK15F_砲台12F()
        {
            Assert.AreEqual(4, Dualies.ShotsToSplat());
            Assert.AreEqual(15, Dualies.TtkFrames());
            Assert.AreEqual(12, Dualies.TurretTtkFrames());
        }

        [Test]
        public void 一槽138發_一般11點5秒_砲台9點2秒()
        {
            Assert.AreEqual(138, Dualies.ShotsPerTank);
            Assert.AreEqual(11.5f, Dualies.FullTankFireSeconds, 0.01f);
            Assert.AreEqual(9.2f, Dualies.TurretFullTankFireSeconds, 0.01f);
            Assert.AreEqual(14, Dualies.RollsPerTank, "每滾 7% → 滿槽 14 滾");
        }

        [Test]
        public void 開火走速與射擊間隔換算()
        {
            Assert.AreEqual(4.8f, Dualies.MoveSpeedWhileFiring, 1e-4f, "0.08 u/F × 60");
            Assert.AreEqual(5f / 60f, Dualies.FireIntervalSeconds, 1e-6f);
            Assert.AreEqual(4f / 60f, Dualies.TurretFireIntervalSeconds, 1e-6f);
            Assert.AreEqual(13, Dualies.SquidFirstShotFrames, "9 + 4 − 0");
        }

        [Test]
        public void 散布設定換算每秒()
        {
            SpreadSettings s = Dualies.ToSpreadSettings();
            Assert.AreEqual(2f, s.StandSwerveDeg);
            Assert.AreEqual(7.5f, s.JumpSwerveDeg);
            Assert.AreEqual(0.3f, s.BiasDecreasePerSecond, 1e-5f, "0.005/F × 60");
            Assert.AreEqual(5f / 60f, s.RepeatSeconds, 1e-6f);
        }

        [Test]
        public void 墨滴樣式七種輪替且都落在彈飛得到的距離內()
        {
            float reach = Dualies.EffectiveRange - Dualies.RadiusForPlayer;
            Assert.AreEqual(reach * 0.5f / 7f, Dualies.DropletPhaseDistance(0), 1e-4f, "間距 14 > 可飛 10.58:鋪在 10.58 內");
            Assert.AreEqual(Dualies.DropletPhaseDistance(0), Dualies.DropletPhaseDistance(7), 1e-6f);
            for (int i = 0; i < 7; i++)
            {
                float d = Dualies.DropletPhaseDistance(i);
                Assert.That(d, Is.GreaterThan(0f).And.LessThan(reach));
            }
            Assert.AreEqual(9.2f * 0.5f / 8f, S3DualiesParams.DropletPhase(8, 8, 9.2f, 11.3f), 1e-5f, "間距較短時用間距");
        }

        [Test]
        public void 翻滾時間軸總長48F_20F後可開火_16F後可接滾()
        {
            Assert.AreEqual(48, DodgeRollModel.TotalFrames(Roll));
            Assert.AreEqual(20, DodgeRollModel.FireResumeFrame(Roll));
            Assert.AreEqual(16, DodgeRollModel.MoveEndFrame(Roll));
            Assert.IsFalse(DodgeRollModel.CanFireAt(Roll, 19.9f));
            Assert.IsTrue(DodgeRollModel.CanFireAt(Roll, 20f));
            Assert.IsFalse(DodgeRollModel.CanChainAt(Roll, 15.9f));
            Assert.IsTrue(DodgeRollModel.CanChainAt(Roll, 16f));
            Assert.IsTrue(DodgeRollModel.IsLockedAt(Roll, 47.9f));
            Assert.IsFalse(DodgeRollModel.IsLockedAt(Roll, 48f));
        }

        [Test]
        public void 翻滾各階段()
        {
            Assert.AreEqual(DodgeRollPhase.Startup, DodgeRollModel.PhaseAt(Roll, 0f));
            Assert.AreEqual(DodgeRollPhase.Startup, DodgeRollModel.PhaseAt(Roll, 3.9f));
            Assert.AreEqual(DodgeRollPhase.Move, DodgeRollModel.PhaseAt(Roll, 4f));
            Assert.AreEqual(DodgeRollPhase.Unrelax, DodgeRollModel.PhaseAt(Roll, 16f));
            Assert.AreEqual(DodgeRollPhase.None, DodgeRollModel.PhaseAt(Roll, 48f));
            Assert.AreEqual(DodgeRollPhase.None, DodgeRollModel.PhaseAt(Roll, -1f));
        }

        [Test]
        public void 翻滾位移_起手不動_翻4點0_滑1點0()
        {
            Assert.AreEqual(0f, DodgeRollModel.DistanceAt(Roll, 4f), 1e-6f);
            Assert.AreEqual(2f, DodgeRollModel.DistanceAt(Roll, 10f), 1e-5f, "等速:6F × 4/12");
            Assert.AreEqual(4f, DodgeRollModel.DistanceAt(Roll, 16f), 1e-5f);
            Assert.AreEqual(5f, DodgeRollModel.DistanceAt(Roll, 22f), 1e-5f);
            Assert.AreEqual(5f, DodgeRollModel.DistanceAt(Roll, 47f), 1e-5f);
            Assert.AreEqual(5f, DodgeRollModel.TotalDistance(Roll), 1e-6f);
        }

        [Test]
        public void 翻滾到滑行速度連續()
        {
            float rollSpeed = DodgeRollModel.RollSpeed(Roll);
            float slideStart = (DodgeRollModel.DistanceAt(Roll, 16.01f) - DodgeRollModel.DistanceAt(Roll, 16f)) / 0.01f;
            Assert.AreEqual(rollSpeed, slideStart, 0.01f, "滑行起速 2×1.0/6 = 翻滾速度 4.0/12");
        }

        [Test]
        public void 位移單調不減()
        {
            float last = 0f;
            for (float f = 0f; f <= 48f; f += 0.25f)
            {
                float d = DodgeRollModel.DistanceAt(Roll, f);
                Assert.That(d, Is.GreaterThanOrEqualTo(last - 1e-6f));
                last = d;
            }
        }

        [Test]
        public void 瞄準補償_直線段內不補_減速段往上補()
        {
            BulletMotion motion = Dualies.Motion;
            Assert.AreEqual(0f, BulletAim.DropAtDistance(motion, 5f), 1e-5f, "3F 直線 7.11 m 內沒有重力");
            float atRange = BulletAim.DropAtDistance(motion, 10.5f);
            Assert.That(atRange, Is.GreaterThan(0.2f).And.LessThan(0.8f), "減速 4F 約掉 0.4~0.5 m");

            Vector3 origin = new Vector3(0f, 1f, 0f);
            Vector3 target = new Vector3(0f, 1f, 10.5f);
            Vector3 dir = BulletAim.CompensatedDirection(motion, origin, target);
            Assert.That(dir.y, Is.GreaterThan(0f), "目標在減速段:發射角往上");

            // 補償後真的飛過目標附近(模擬驗證)
            BulletState state = BulletBallistics.Launch(origin, dir, motion);
            float yAtTarget = float.NaN;
            for (int i = 0; i < 60; i++)
            {
                Vector3 before = state.Position;
                BulletBallistics.Step(ref state, motion);
                if (state.Position.z >= target.z)
                {
                    float t = (target.z - before.z) / (state.Position.z - before.z);
                    yAtTarget = Mathf.Lerp(before.y, state.Position.y, t);
                    break;
                }
            }
            Assert.AreEqual(target.y, yAtTarget, 0.08f);
        }
    }
}

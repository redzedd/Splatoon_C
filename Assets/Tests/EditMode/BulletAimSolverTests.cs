using NUnit.Framework;
using SplatoonC.Core.Combat;
using UnityEngine;

namespace SplatoonC.Tests
{
    // BulletAim.SolvedDirection:曲射彈(Crab Tank 砲彈)反解仰角後,模擬的彈道真的通過目標點
    public class BulletAimSolverTests
    {
        private static float HeightAt(in BulletMotion motion, Vector3 origin, Vector3 direction, Vector3 target)
        {
            Vector3 flat = target - origin;
            flat.y = 0f;
            float distance = flat.magnitude;
            Vector3 along = flat / distance;
            BulletState state = BulletBallistics.Launch(origin, direction, motion);
            for (int i = 0; i < 300; i++)
            {
                Vector3 before = state.Position;
                BulletBallistics.Step(ref state, motion);
                float d0 = Vector3.Dot(before - origin, along);
                float d1 = Vector3.Dot(state.Position - origin, along);
                if (d1 >= distance)
                {
                    return Mathf.Lerp(before.y, state.Position.y, (distance - d0) / (d1 - d0));
                }
            }
            return float.NaN;
        }

        [Test]
        public void 曲射砲彈_反解後通過目標點()
        {
            BulletMotion motion = CrabTankModel.CannonMotion;
            var origin = new Vector3(0f, 1.3f, 0f);
            foreach (float distance in new[] { 6f, 12f, 20f, 28f })
            {
                var target = new Vector3(3f, 0f, distance);
                Vector3 direction = BulletAim.SolvedDirection(motion, origin, target);
                float height = HeightAt(motion, origin, direction, target);
                Assert.AreEqual(target.y, height, 0.02f, $"{distance} m:到目標水平距離時高度 {height}");
            }
        }

        [Test]
        public void 曲射砲彈_高台與低處也打得到()
        {
            BulletMotion motion = CrabTankModel.CannonMotion;
            var origin = new Vector3(0f, 1.3f, 0f);
            foreach (Vector3 target in new[] { new Vector3(0f, 5f, 18f), new Vector3(0f, -4f, 24f), new Vector3(-6f, 3f, 26f) })
            {
                Vector3 direction = BulletAim.SolvedDirection(motion, origin, target);
                float height = HeightAt(motion, origin, direction, target);
                Assert.AreEqual(target.y, height, 0.02f, $"目標 {target}:到目標水平距離時高度 {height}");
            }
        }
    }
}

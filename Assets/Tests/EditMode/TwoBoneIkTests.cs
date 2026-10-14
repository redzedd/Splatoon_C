using NUnit.Framework;
using SplatoonC.Core.Animation;
using UnityEngine;

namespace SplatoonC.Tests.EditMode
{
    public sealed class TwoBoneIkTests
    {
        [Test]
        public void 可及目標_骨長不變且末端到位()
        {
            Vector3 root = new Vector3(0.144f, 1.077f, 0f);
            Vector3 target = root + new Vector3(0.05f, -0.1f, 0.25f);

            Vector3 mid = TwoBoneIk.SolveMid(root, target, new Vector3(0.5f, -0.8f, -0.3f), 0.205f, 0.185f,
                out Vector3 reached);

            Assert.AreEqual(0.205f, (mid - root).magnitude, 1e-4f);
            Assert.AreEqual(0.185f, (reached - mid).magnitude, 1e-4f);
            Assert.AreEqual(0f, (reached - target).magnitude, 1e-4f);
        }

        [Test]
        public void 目標太遠_手臂伸直停在最遠處不拉長()
        {
            Vector3 root = Vector3.zero;
            Vector3 target = new Vector3(0f, 0f, 2f);

            Vector3 mid = TwoBoneIk.SolveMid(root, target, Vector3.down, 0.3f, 0.3f, out Vector3 reached);

            Assert.AreEqual(0.3f, mid.magnitude, 1e-3f);
            Assert.AreEqual(0.6f, reached.magnitude, 0.002f);
            Assert.Greater(reached.z, 0.59f);
        }

        [Test]
        public void 中間關節凸向_pole()
        {
            Vector3 root = Vector3.zero;
            Vector3 target = new Vector3(0f, -0.4f, 0f);

            Vector3 knee = TwoBoneIk.SolveMid(root, target, Vector3.forward, 0.298f, 0.288f, out _);

            Assert.Greater(knee.z, 0.1f, "膝蓋往前凸");
        }

        [Test]
        public void 骨頭旋轉_腿的前面朝膝蓋_手臂的前面背離手肘()
        {
            Vector3 hip = new Vector3(0f, 0.65f, 0f);
            Vector3 ankle = new Vector3(0f, 0.1f, 0.05f);
            Vector3 knee = TwoBoneIk.SolveMid(hip, ankle, Vector3.forward, 0.298f, 0.288f, out Vector3 reached);
            TwoBoneIk.BoneRotations(hip, knee, reached, Vector3.forward, true, out Quaternion thigh, out Quaternion shin);

            // 大腿 +Y 沿骨頭(往下)、+Z 朝前(膝蓋那側);鉸鏈軸 +X 與靜止骨架一致(朝角色左邊 = 模型 -X)
            Assert.Greater(Vector3.Dot(thigh * Vector3.up, (knee - hip).normalized), 0.999f);
            Assert.Greater(Vector3.Dot(thigh * Vector3.forward, Vector3.forward), 0.9f);
            Assert.Greater(Vector3.Dot(thigh * Vector3.right, Vector3.left), 0.99f);
            // 上下骨共用鉸鏈軸
            Assert.Greater(Vector3.Dot(thigh * Vector3.right, shin * Vector3.right), 0.999f);

            Vector3 shoulder = new Vector3(0.144f, 1.077f, 0f);
            Vector3 wrist = shoulder + new Vector3(0.05f, -0.08f, 0.25f);
            Vector3 pole = new Vector3(0.55f, -0.8f, -0.25f);
            Vector3 elbow = TwoBoneIk.SolveMid(shoulder, wrist, pole, 0.205f, 0.185f, out Vector3 hand);
            TwoBoneIk.BoneRotations(shoulder, elbow, hand, pole, false, out Quaternion upper, out Quaternion lower);

            // 手肘往後下凸,手臂的「前面」(拇指側/肘窩)朝另一邊;前臂往前面那側彎
            Vector3 bulge = elbow - (shoulder + wrist) * 0.5f;
            Assert.Less(Vector3.Dot(upper * Vector3.forward, bulge), 0f);
            Vector3 lowerDir = (hand - elbow).normalized;
            Assert.Greater(Vector3.Dot(lowerDir, upper * Vector3.forward), 0f);
        }
    }
}

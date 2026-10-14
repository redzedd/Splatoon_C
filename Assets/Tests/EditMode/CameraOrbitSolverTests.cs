using NUnit.Framework;
using SplatoonC.Core.Locomotion;
using UnityEngine;

namespace SplatoonC.Tests
{
    public class CameraOrbitSolverTests
    {
        [Test]
        public void Pitch超出上下限_被夾住()
        {
            float yaw = 0f;
            float pitch = 50f;
            CameraOrbitSolver.AdvanceAngles(ref yaw, ref pitch, new Vector2(0f, -100f), -30f, 60f);
            Assert.AreEqual(60f, pitch);
            CameraOrbitSolver.AdvanceAngles(ref yaw, ref pitch, new Vector2(0f, 200f), -30f, 60f);
            Assert.AreEqual(-30f, pitch);
        }

        [Test]
        public void Yaw連續累加_維持在零到三百六十度()
        {
            float yaw = 350f;
            float pitch = 0f;
            CameraOrbitSolver.AdvanceAngles(ref yaw, ref pitch, new Vector2(20f, 0f), -30f, 60f);
            Assert.AreEqual(10f, yaw, 1e-4f);
        }

        [Test]
        public void 滑鼠上推_視角上仰_Pitch變小()
        {
            float yaw = 0f;
            float pitch = 10f;
            CameraOrbitSolver.AdvanceAngles(ref yaw, ref pitch, new Vector2(0f, 5f), -30f, 60f);
            Assert.AreEqual(5f, pitch, 1e-4f);
        }

        [Test]
        public void 零俯仰零偏航_相機在目標正後方()
        {
            Vector3 position = CameraOrbitSolver.ResolvePosition(new Vector3(0f, 1.5f, 0f), 0f, 0f, 5f);
            Assert.AreEqual(0f, position.x, 1e-4f);
            Assert.AreEqual(1.5f, position.y, 1e-4f);
            Assert.AreEqual(-5f, position.z, 1e-4f);
        }

        [Test]
        public void 正俯角_相機高於目標()
        {
            Vector3 position = CameraOrbitSolver.ResolvePosition(Vector3.zero, 0f, 30f, 5f);
            Assert.Greater(position.y, 0f);
        }
    }
}

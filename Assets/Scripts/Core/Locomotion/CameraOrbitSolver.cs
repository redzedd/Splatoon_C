using UnityEngine;

namespace SplatoonC.Core.Locomotion
{
    // 第三人稱環繞相機的角度與定位數學。純邏輯,遮擋處理留給 Gameplay 層。
    public static class CameraOrbitSolver
    {
        public static void AdvanceAngles(
            ref float yawDeg,
            ref float pitchDeg,
            Vector2 lookDeltaDeg,
            float minPitchDeg,
            float maxPitchDeg)
        {
            yawDeg = Mathf.Repeat(yawDeg + lookDeltaDeg.x, 360f);
            // 滑鼠上推(正 y)= 視角上仰 = pitch 變小。
            pitchDeg = Mathf.Clamp(pitchDeg - lookDeltaDeg.y, minPitchDeg, maxPitchDeg);
        }

        public static Vector3 ResolvePosition(Vector3 pivot, float yawDeg, float pitchDeg, float distance)
        {
            Quaternion rotation = Quaternion.Euler(pitchDeg, yawDeg, 0f);
            return pivot - rotation * Vector3.forward * distance;
        }
    }
}

using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 大招 AutoTest 用的固定視線:直接指定準心起點與方向(取代相機),瞄準可重現。
    // 用法:player.AimView = aim; aim.LookAt(point);測完 player.AimView = rig。
    public sealed class SpecialTestAim : IAimView
    {
        public Vector3 Origin;
        public Vector3 Forward = Vector3.forward;

        public Vector3 AimOrigin => Origin;
        public Vector3 AimForward => Forward;

        public float Yaw
        {
            get
            {
                Vector3 flat = new Vector3(Forward.x, 0f, Forward.z);
                return flat.sqrMagnitude > 1e-6f ? Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg : 0f;
            }
        }

        // 從 origin 看向 point
        public void Set(Vector3 origin, Vector3 point)
        {
            Origin = origin;
            Vector3 dir = point - origin;
            Forward = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
        }

        // 從角色肩上(1.6 m、後 0.3 m)看向 point
        public void From(Transform character, Vector3 point)
        {
            Vector3 toward = point - character.position;
            toward.y = 0f;
            Vector3 back = toward.sqrMagnitude > 1e-6f ? -toward.normalized * 0.3f : Vector3.zero;
            Set(character.position + Vector3.up * 1.6f + back, point);
        }
    }
}

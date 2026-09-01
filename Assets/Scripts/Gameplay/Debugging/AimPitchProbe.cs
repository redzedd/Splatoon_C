using System.Collections;
using SplatoonC.Gameplay.CameraRig;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 調參工具:量各俯角下「準心指向地面的距離」與「墨彈實際落點距離」,
    // 找出兩者吻合的視角區間(固定中心準心要打得準,兩者必須接近)。
    // 相機有 SmoothDamp,設角度後必須等幀收斂才能量——單次 RunCommand 量到的是舊位置。
    public sealed class AimPitchProbe : MonoBehaviour
    {
        private sealed class FireIntent : IPlayerIntentSource
        {
            public Vector2 MoveInput => Vector2.zero;
            public Vector2 LookDeltaDeg => Vector2.zero;
            public bool JumpPressedThisFrame => false;
            public bool AttackHeld { get; set; }
            public bool SquidHeld => false;
        }

        public static void Run()
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("[PROBE] 需在 Play mode 中執行");
                return;
            }
            var host = new GameObject("AimPitchProbe");
            host.AddComponent<AimPitchProbe>();
        }

        private IEnumerator Start()
        {
            var player = GameObject.Find("Player");
            var router = player.GetComponent<PlayerInputRouter>();
            var controller = player.GetComponent<CharacterController>();
            var rig = Camera.main.GetComponent<ThirdPersonCameraRig>();
            var cam = Camera.main;
            player.GetComponent<PlayerInkTank>().InfiniteInk = true;
            int mask = ~(1 << LayerMask.NameToLayer("Player"));

            var intent = new FireIntent();
            router.SetOverrideSource(intent);
            controller.enabled = false;
            player.transform.position = new Vector3(-16f, 0.1f, 16f);
            controller.enabled = true;

            var poolRoot = GameObject.Find("InkProjectilePool");
            float[] pitches = { 6f, 12f, 18f, 24f, 30f };

            foreach (float pitch in pitches)
            {
                rig.SetAngles(215f, pitch);
                // 等相機 SmoothDamp 收斂
                for (int i = 0; i < 30; i++)
                {
                    yield return null;
                }

                float aimDist = Physics.Raycast(new Ray(cam.transform.position, cam.transform.forward),
                    out RaycastHit hit, 80f, mask, QueryTriggerInteraction.Ignore)
                    ? Vector3.Distance(Flat(player.transform.position), Flat(hit.point))
                    : -1f;

                // 射一發,追到落地
                intent.AttackHeld = true;
                yield return null;
                yield return null;
                intent.AttackHeld = false;

                Vector3 lastSeen = Vector3.zero;
                bool saw = false;
                float watchdog = 0f;
                while (watchdog < 3f)
                {
                    bool any = false;
                    foreach (Transform c in poolRoot.transform)
                    {
                        if (c.gameObject.activeInHierarchy)
                        {
                            any = true;
                            lastSeen = c.position;
                            saw = true;
                        }
                    }
                    if (saw && !any)
                    {
                        break;
                    }
                    watchdog += Time.deltaTime;
                    yield return null;
                }
                float shotDist = Vector3.Distance(Flat(player.transform.position), Flat(lastSeen));
                Debug.Log($"[PROBE] pitch={pitch:F0}° 準心指向={aimDist:F1}m 彈落點={shotDist:F1}m 落差={aimDist - shotDist:F1}m");
                yield return new WaitForSeconds(0.3f);
            }

            router.ClearOverrideSource();
            Debug.Log("[PROBE] DONE");
            Destroy(gameObject);
        }

        private static Vector3 Flat(Vector3 v)
        {
            return new Vector3(v.x, 0f, v.z);
        }
    }
}

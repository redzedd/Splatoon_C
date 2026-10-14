using System.Collections;
using SplatoonC.Gameplay.CameraRig;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 驗收「平射最遠、仰射不增加攻擊距離」:射程以沿彈道飛行距離計,
    // 仰角把射程花在高度上,水平距離必須隨仰角遞減。
    public sealed class RangeByPitchProbe : MonoBehaviour
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
            var host = new GameObject("RangeByPitchProbe");
            host.AddComponent<RangeByPitchProbe>();
        }

        private IEnumerator Start()
        {
            var player = GameObject.Find("Player");
            var router = player.GetComponent<PlayerInputRouter>();
            var controller = player.GetComponent<CharacterController>();
            var rig = Camera.main.GetComponent<ThirdPersonCameraRig>();
            player.GetComponent<PlayerInkTank>().InfiniteInk = true;

            var intent = new FireIntent();
            router.SetOverrideSource(intent);
            controller.enabled = false;
            player.transform.position = new Vector3(-16f, 0.1f, 18f);
            controller.enabled = true;

            var poolRoot = GameObject.Find("InkProjectilePool");
            // 負值 = 仰視。0 是平射,應為水平射程最大者
            float[] pitches = { 0f, -15f, -30f, -50f, -70f };
            float flatRange = -1f;
            bool monotonic = true;

            foreach (float pitch in pitches)
            {
                rig.SetAngles(180f, pitch);
                for (int i = 0; i < 30; i++)
                {
                    yield return null;
                }

                intent.AttackHeld = true;
                yield return null;
                yield return null;
                intent.AttackHeld = false;

                Vector3 launchPos = Vector3.zero;
                Vector3 lastSeen = Vector3.zero;
                bool launched = false;
                float watchdog = 0f;
                while (watchdog < 4f)
                {
                    bool any = false;
                    foreach (Transform c in poolRoot.transform)
                    {
                        if (!c.gameObject.activeInHierarchy)
                        {
                            continue;
                        }
                        any = true;
                        lastSeen = c.position;
                        if (!launched)
                        {
                            launchPos = c.position;
                            launched = true;
                        }
                    }
                    if (launched && !any)
                    {
                        break;
                    }
                    watchdog += Time.deltaTime;
                    yield return null;
                }

                float horizontal = new Vector2(
                    lastSeen.x - launchPos.x, lastSeen.z - launchPos.z).magnitude;
                if (pitch == 0f)
                {
                    flatRange = horizontal;
                }
                else if (horizontal > flatRange + 0.5f)
                {
                    monotonic = false;
                }
                Debug.Log($"[PROBE] pitch={pitch:F0}°(負=仰視) 水平射程={horizontal:F1}m");
                yield return new WaitForSeconds(0.3f);
            }

            router.ClearOverrideSource();
            Debug.Log($"[PROBE] DONE 平射水平射程={flatRange:F1}m,仰射皆未超過:{monotonic}");
            Destroy(gameObject);
        }
    }
}

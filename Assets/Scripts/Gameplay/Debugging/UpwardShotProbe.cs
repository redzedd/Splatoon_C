using System.Collections;
using SplatoonC.Gameplay.CameraRig;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 量仰射時墨彈的爬升高度與總飛行距離。
    // 射程是以「沿彈道飛行距離」判定的,但墜落段只煞停水平速度——
    // 仰射時垂直速度沒人煞,子彈會遠遠超出應有的射程往上衝。
    public sealed class UpwardShotProbe : MonoBehaviour
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
            var host = new GameObject("UpwardShotProbe");
            host.AddComponent<UpwardShotProbe>();
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
            // 負值 = 仰視
            float[] pitches = { 0f, -30f, -50f, -70f, -80f };

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
                Vector3 prev = Vector3.zero;
                float peakY = float.NegativeInfinity;
                float travelled = 0f;
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
                        if (!launched)
                        {
                            launchPos = c.position;
                            prev = c.position;
                            launched = true;
                        }
                        travelled += Vector3.Distance(c.position, prev);
                        prev = c.position;
                        peakY = Mathf.Max(peakY, c.position.y);
                    }
                    if (launched && !any)
                    {
                        break;
                    }
                    watchdog += Time.deltaTime;
                    yield return null;
                }

                float climb = launched ? peakY - launchPos.y : 0f;
                Debug.Log($"[PROBE] pitch={pitch:F0}°(負=仰視) 爬升高度={climb:F1}m " +
                    $"沿彈道總飛行={travelled:F1}m(射程設定 10m + 煞停滑行)");
                yield return new WaitForSeconds(0.3f);
            }

            router.ClearOverrideSource();
            Debug.Log("[PROBE] DONE 仰射高度量測");
            Destroy(gameObject);
        }
    }
}

using System.Collections;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 驗收:進遊戲的「預設視角」下,固定中心準心指向的地面點與墨彈落點是否吻合。
    // 不呼叫 SetAngles——刻意使用玩家開場就有的角度。
    public sealed class DefaultAimProbe : MonoBehaviour
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
            var host = new GameObject("DefaultAimProbe");
            host.AddComponent<DefaultAimProbe>();
        }

        private IEnumerator Start()
        {
            var player = GameObject.Find("Player");
            var router = player.GetComponent<PlayerInputRouter>();
            var cam = Camera.main;
            player.GetComponent<PlayerInkTank>().InfiniteInk = true;
            int mask = ~(1 << LayerMask.NameToLayer("Player"));
            var intent = new FireIntent();
            router.SetOverrideSource(intent);

            // 等相機安定(不改角度,用預設)
            for (int i = 0; i < 40; i++)
            {
                yield return null;
            }
            float pitch = cam.transform.eulerAngles.x;
            float aimDist = Physics.Raycast(new Ray(cam.transform.position, cam.transform.forward),
                out RaycastHit hit, 80f, mask, QueryTriggerInteraction.Ignore)
                ? Vector3.Distance(Flat(player.transform.position), Flat(hit.point))
                : -1f;

            var poolRoot = GameObject.Find("InkProjectilePool");
            float total = 0f;
            int shots = 0;
            for (int s = 0; s < 5; s++)
            {
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
                float d = Vector3.Distance(Flat(player.transform.position), Flat(lastSeen));
                if (d > 1f)
                {
                    total += d;
                    shots++;
                }
                yield return new WaitForSeconds(0.25f);
            }

            float avgShot = shots > 0 ? total / shots : -1f;
            Debug.Log($"[PROBE] 預設視角 pitch={pitch:F1}° 準心指向={aimDist:F1}m 平均落點={avgShot:F1}m 落差={aimDist - avgShot:F1}m");
            router.ClearOverrideSource();
            Destroy(gameObject);
        }

        private static Vector3 Flat(Vector3 v)
        {
            return new Vector3(v.x, 0f, v.z);
        }
    }
}

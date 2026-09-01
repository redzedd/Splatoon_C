using System.Collections;
using SplatoonC.Gameplay.CameraRig;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 驗收彈道補償:不論俯角為何,墨彈飛到射程極限時應該落在「準心射線」上。
    // 量法 = 追蹤彈道,取水平飛行達射程時的位置,計算它到準心射線的垂直距離。
    // 相機有 SmoothDamp:設角度後必須等幀收斂才能量(單次 LateUpdate 讀到舊位置)。
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
            var shooter = player.GetComponent<InkShooter>();
            var rig = Camera.main.GetComponent<ThirdPersonCameraRig>();
            var cam = Camera.main;
            player.GetComponent<PlayerInkTank>().InfiniteInk = true;
            float range = shooter.Config.StraightRange;

            var intent = new FireIntent();
            router.SetOverrideSource(intent);
            // 站到空曠處,朝 -Z 沒有障礙的方向
            controller.enabled = false;
            player.transform.position = new Vector3(-16f, 0.1f, 18f);
            controller.enabled = true;

            var poolRoot = GameObject.Find("InkProjectilePool");
            float[] pitches = { -10f, 0f, 10f, 20f, 30f };
            int passed = 0;

            foreach (float pitch in pitches)
            {
                rig.SetAngles(180f, pitch);
                for (int i = 0; i < 30; i++)
                {
                    yield return null;
                }

                // 準心射線(相機位置與朝向於發射瞬間取樣)
                Vector3 camPos = cam.transform.position;
                Vector3 camDir = cam.transform.forward;

                // 兩種情況分別驗收:
                // A. 準心在射程內就指到實物 → 彈應命中該點
                // B. 準心指向空中/遠方 → 彈應在射程極限時位於準心射線上
                int mask = ~(1 << LayerMask.NameToLayer("Player"));
                float aimReach = Vector3.Distance(camPos, player.transform.position + Vector3.up * 1.05f) + range;
                bool aimHasTarget = Physics.Raycast(new Ray(camPos, camDir), out RaycastHit aimHit,
                    aimReach, mask, QueryTriggerInteraction.Ignore);
                Vector3 aimTarget = aimHasTarget ? aimHit.point : Vector3.zero;

                // 每角度射 3 發取最佳值:18% 的彈刻意提前墜落(路徑痕跡機制),
                // 單發量測會被它污染;主彈道準度要看正常彈。
                float bestMetric = -1f;
                for (int shot = 0; shot < 3; shot++)
                {
                intent.AttackHeld = true;
                yield return null;
                yield return null;
                intent.AttackHeld = false;

                // 追蹤:記錄沿彈道飛行達射程時,彈到準心射線的距離
                Vector3 launchPos = Vector3.zero;
                Vector3 lastSeen = Vector3.zero;
                Vector3 prevPos = Vector3.zero;
                float travelled3D = 0f;
                bool launched = false;
                float distToAimLineAtRange = -1f;
                float watchdog = 0f;
                while (watchdog < 3f)
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
                            prevPos = c.position;
                            launched = true;
                        }
                        // 射程以沿彈道 3D 距離計(與 InkProjectile 一致)
                        travelled3D += Vector3.Distance(c.position, prevPos);
                        prevPos = c.position;
                        if (distToAimLineAtRange < 0f && travelled3D >= range)
                        {
                            // 點到射線的垂直距離
                            Vector3 toPoint = c.position - camPos;
                            float along = Vector3.Dot(toPoint, camDir);
                            Vector3 onLine = camPos + camDir * along;
                            distToAimLineAtRange = Vector3.Distance(c.position, onLine);
                        }
                    }
                    if (launched && !any)
                    {
                        break;
                    }
                    watchdog += Time.deltaTime;
                    yield return null;
                }

                float metric = aimHasTarget
                    ? Vector3.Distance(lastSeen, aimTarget)
                    : distToAimLineAtRange;
                if (metric >= 0f && (bestMetric < 0f || metric < bestMetric))
                {
                    bestMetric = metric;
                }
                yield return new WaitForSeconds(0.2f);
                }

                bool ok = bestMetric >= 0f && bestMetric < 0.8f;
                string detail = aimHasTarget
                    ? $"準心指向實物 命中偏差={bestMetric:F2}m"
                    : $"準心指向空中 射程{range:F0}m 處距準心線={bestMetric:F2}m";
                if (ok)
                {
                    passed++;
                }
                Debug.Log($"[PROBE] pitch={pitch:F0}° {detail} {(ok ? "OK" : "偏離")}");
            }

            router.ClearOverrideSource();
            Debug.Log($"[PROBE] DONE {passed}/{pitches.Length} 個俯角通過(彈在射程極限落在準心線上)");
            Destroy(gameObject);
        }
    }
}

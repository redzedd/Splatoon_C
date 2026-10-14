using System.Collections;
using System.Collections.Generic;
using SplatoonC.Gameplay.CameraRig;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 量「命中點相對準心的系統性偏移」——AimPitchProbe 取 3 發最佳值且只報絕對距離,
    // 剛好會把系統性偏差藏起來;這支取平均、帶正負號,並同時報畫面座標的上下偏移
    //(玩家看到的「偏上偏下」是螢幕空間的事)。
    public sealed class AimBiasProbe : MonoBehaviour
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
            var host = new GameObject("AimBiasProbe");
            host.AddComponent<AimBiasProbe>();
        }

        private IEnumerator Start()
        {
            var player = GameObject.Find("Player");
            var router = player.GetComponent<PlayerInputRouter>();
            var controller = player.GetComponent<CharacterController>();
            var rig = Camera.main.GetComponent<ThirdPersonCameraRig>();
            var cam = Camera.main;
            player.GetComponent<PlayerInkTank>().InfiniteInk = true;

            var intent = new FireIntent();
            router.SetOverrideSource(intent);
            controller.enabled = false;
            player.transform.position = new Vector3(-16f, 0.1f, 18f);
            controller.enabled = true;

            var poolRoot = GameObject.Find("InkProjectilePool");
            int mask = ~(1 << LayerMask.NameToLayer("Player"));
            float[] pitches = { 10f, 20f, 30f, 40f };
            const int shotsPerAngle = 5;

            foreach (float pitch in pitches)
            {
                rig.SetAngles(180f, pitch);
                for (int i = 0; i < 30; i++)
                {
                    yield return null;
                }

                Vector3 camPos = cam.transform.position;
                Vector3 camDir = cam.transform.forward;
                if (!Physics.Raycast(new Ray(camPos, camDir), out RaycastHit aimHit, 80f, mask,
                        QueryTriggerInteraction.Ignore))
                {
                    Debug.Log($"[PROBE] pitch={pitch:F0}° 準心沒指到實物,跳過");
                    continue;
                }
                Vector3 crosshairPoint = aimHit.point;
                float crosshairDistance = Vector3.Distance(camPos, crosshairPoint);
                Vector2 crosshairScreen = cam.WorldToScreenPoint(crosshairPoint);

                var overshoots = new List<float>();
                var screenDeltas = new List<float>();
                for (int shot = 0; shot < shotsPerAngle; shot++)
                {
                    intent.AttackHeld = true;
                    yield return null;
                    yield return null;
                    intent.AttackHeld = false;

                    Vector3 lastSeen = Vector3.zero;
                    bool launched = false;
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
                            launched = true;
                        }
                        if (launched && !any)
                        {
                            break;
                        }
                        watchdog += Time.deltaTime;
                        yield return null;
                    }
                    if (!launched)
                    {
                        continue;
                    }

                    // 沿準心水平方向的「超前量」:正 = 打得比準心遠
                    Vector3 flatAim = new Vector3(camDir.x, 0f, camDir.z).normalized;
                    Vector3 toImpact = lastSeen - crosshairPoint;
                    overshoots.Add(Vector3.Dot(new Vector3(toImpact.x, 0f, toImpact.z), flatAim));

                    // 螢幕空間:正 = 命中點顯示在準心上方
                    Vector3 impactScreen = cam.WorldToScreenPoint(lastSeen);
                    if (impactScreen.z > 0f)
                    {
                        screenDeltas.Add(impactScreen.y - crosshairScreen.y);
                    }
                    yield return new WaitForSeconds(0.15f);
                }

                Debug.Log($"[PROBE] pitch={pitch:F0}° 準心落點距離={crosshairDistance:F1}m " +
                    $"平均超前={Mean(overshoots):F2}m(正=打得比準心遠) " +
                    $"螢幕垂直偏移={Mean(screenDeltas):F0}px(正=命中顯示在準心上方) " +
                    $"樣本={overshoots.Count}");
            }

            // 第二段:打垂直牆面。牆是垂直的,命中點與準心的高度差可直接讀,
            // 比地面更能反映玩家說的「偏上/偏下」。ClimbWall_High 在 (12,2,0) 面向 -X。
            float[] wallDistances = { 4f, 8f, 14f, 20f };
            foreach (float distance in wallDistances)
            {
                controller.enabled = false;
                player.transform.position = new Vector3(12f - distance, 0.1f, 0f);
                controller.enabled = true;
                rig.SetAngles(90f, 0f);
                for (int i = 0; i < 30; i++)
                {
                    yield return null;
                }

                Vector3 camPos = cam.transform.position;
                Vector3 camDir = cam.transform.forward;
                if (!Physics.Raycast(new Ray(camPos, camDir), out RaycastHit wallHit, 80f, mask,
                        QueryTriggerInteraction.Ignore))
                {
                    Debug.Log($"[PROBE] 牆距 {distance:F0}m:準心沒指到實物,跳過");
                    continue;
                }
                Vector3 aimPoint = wallHit.point;

                var heightDeltas = new List<float>();
                for (int shot = 0; shot < shotsPerAngle; shot++)
                {
                    intent.AttackHeld = true;
                    yield return null;
                    yield return null;
                    intent.AttackHeld = false;

                    Vector3 lastSeen = Vector3.zero;
                    bool launched = false;
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
                            launched = true;
                        }
                        if (launched && !any)
                        {
                            break;
                        }
                        watchdog += Time.deltaTime;
                        yield return null;
                    }
                    if (launched)
                    {
                        heightDeltas.Add(lastSeen.y - aimPoint.y);
                    }
                    yield return new WaitForSeconds(0.15f);
                }

                Debug.Log($"[PROBE] 牆距 {distance:F0}m 準心高度={aimPoint.y:F2} " +
                    $"平均命中高度差={Mean(heightDeltas):F2}m(正=打在準心上方) 樣本={heightDeltas.Count}");
            }

            router.ClearOverrideSource();
            Debug.Log("[PROBE] DONE 瞄準偏移量測");
            Destroy(gameObject);
        }

        private static float Mean(List<float> values)
        {
            if (values.Count == 0)
            {
                return 0f;
            }
            float sum = 0f;
            for (int i = 0; i < values.Count; i++)
            {
                sum += values[i];
            }
            return sum / values.Count;
        }
    }
}

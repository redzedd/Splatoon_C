using System.Collections;
using SplatoonC.Gameplay.CameraRig;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 量測探針(不斷言):啟動大招連丟 3 顆裝置到 TargetWall(間隔 0.6 秒),
    // 每 6 幀印出所有活裝置的位置/速度/貼附狀態與貼附/龍捲風計數,共 3 秒。
    public sealed class InkstrikeFlightProbe : MonoBehaviour
    {
        private sealed class TestIntentSource : IPlayerIntentSource
        {
            public Vector2 MoveInput => Vector2.zero;
            public Vector2 LookDeltaDeg => Vector2.zero;
            public bool JumpPressedThisFrame => false;
            public bool AttackHeld => false;
            public bool SquidHeld => false;
            public bool SubWeaponHeld { get; set; }
            public bool SpecialPressedThisFrame { get; set; }
        }

        public static void Run()
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("[PROBE] 需在 Play mode 中執行");
                return;
            }
            var host = new GameObject("InkstrikeFlightProbe");
            host.AddComponent<InkstrikeFlightProbe>();
        }

        private IEnumerator Start()
        {
            GameObject player = GameObject.Find("Player");
            var router = player.GetComponent<PlayerInputRouter>();
            var controller = player.GetComponent<CharacterController>();
            var gauge = player.GetComponent<PlayerSpecialGauge>();
            var thrower = player.GetComponent<ThrowController>();
            var rig = Camera.main.GetComponent<ThirdPersonCameraRig>();
            var intent = new TestIntentSource();
            router.SetOverrideSource(intent);
            Vector3 spawnPos = player.transform.position;

            controller.enabled = false;
            player.transform.position = new Vector3(20f, 0.1f, 4f);
            controller.enabled = true;
            rig.SetAngles(90f, 12f);
            yield return new WaitForSeconds(0.35f);

            gauge.SetPointsForTest(100f);
            yield return null;
            intent.SpecialPressedThisFrame = true;
            yield return null;
            intent.SpecialPressedThisFrame = false;

            GameObject root = GameObject.Find("InkstrikeDevicePool");
            float start = Time.time;
            int frame = 0;
            int throwsDone = 0;
            float nextThrowAt = start;
            bool holding = false;
            while (Time.time < start + 3.5f)
            {
                if (throwsDone < 3)
                {
                    if (!holding && Time.time >= nextThrowAt)
                    {
                        intent.SubWeaponHeld = true;
                        holding = true;
                        nextThrowAt = Time.time + 0.35f;
                    }
                    else if (holding && Time.time >= nextThrowAt)
                    {
                        intent.SubWeaponHeld = false;
                        holding = false;
                        throwsDone++;
                        nextThrowAt = Time.time + 0.25f;
                        Debug.Log($"[PROBE] 丟出第 {throwsDone} 顆 t={Time.time - start:0.00} origin={thrower.LastLaunchOrigin} v={thrower.LastLaunchVelocity} 相機={Camera.main.transform.position} fwd={Camera.main.transform.forward}");
                    }
                }
                if (frame % 6 == 0)
                {
                    string line = $"[PROBE] t={Time.time - start:0.00} 貼附={InkstrikeDevice.AttachedCount} 龍捲風生成={Tornado.SpawnCount} 活={Tornado.ActiveCount}";
                    for (int i = 0; i < root.transform.childCount; i++)
                    {
                        GameObject child = root.transform.GetChild(i).gameObject;
                        if (!child.activeInHierarchy)
                        {
                            continue;
                        }
                        var device = child.GetComponent<InkstrikeDevice>();
                        line += $" | #{i} pos={device.transform.position} v={device.Velocity} 貼={device.IsAttached}/{(device.AttachedCollider != null ? device.AttachedCollider.name : "-")}";
                    }
                    Debug.Log(line);
                }
                frame++;
                yield return null;
            }

            controller.enabled = false;
            player.transform.position = spawnPos;
            controller.enabled = true;
            rig.SetAngles(0f, 12f);
            router.ClearOverrideSource();
            Debug.Log("[PROBE] DONE 裝置飛行量測");
            Destroy(gameObject);
        }
    }
}

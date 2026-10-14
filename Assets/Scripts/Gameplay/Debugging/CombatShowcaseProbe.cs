using System.Collections;
using System.IO;
using SplatoonC.Gameplay.CameraRig;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 截圖探針(不斷言):站到近假人前打 3 發截白字 90.0,再補 1 發截紅字 120.0。
    // 用 ScreenCapture 在協程內於正確的幀截圖——MCP 截圖往返 2~4 秒,追不上 1.2 秒的紅字。
    // 輸出 Builds/Screenshots/(不入版控)。
    public sealed class CombatShowcaseProbe : MonoBehaviour
    {
        private sealed class TestIntentSource : IPlayerIntentSource
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
            var host = new GameObject("CombatShowcaseProbe");
            host.AddComponent<CombatShowcaseProbe>();
        }

        private IEnumerator Start()
        {
            GameObject player = GameObject.Find("Player");
            var router = player != null ? player.GetComponent<PlayerInputRouter>() : null;
            var controller = player != null ? player.GetComponent<CharacterController>() : null;
            var tank = player != null ? player.GetComponent<PlayerInkTank>() : null;
            var rig = Camera.main != null ? Camera.main.GetComponent<ThirdPersonCameraRig>() : null;
            GameObject nearGo = GameObject.Find("Dummy_Near");
            var near = nearGo != null ? nearGo.GetComponent<TargetDummy>() : null;
            if (router == null || controller == null || tank == null || rig == null || near == null)
            {
                Debug.LogError("[PROBE] 前置失敗:找不到 Player/CameraRig/Dummy_Near");
                Destroy(gameObject);
                yield break;
            }
            Directory.CreateDirectory("Builds/Screenshots");
            tank.InfiniteInk = true;
            var intent = new TestIntentSource();
            router.SetOverrideSource(intent);
            Vector3 spawnPos = player.transform.position;

            controller.enabled = false;
            player.transform.position = new Vector3(21f, 0.1f, -16f);
            controller.enabled = true;
            rig.SetAngles(0f, 4f);
            for (int i = 0; i < 30; i++)
            {
                yield return null;
            }

            for (int shot = 0; shot < 3; shot++)
            {
                yield return FireOne(intent, near);
            }
            yield return null;
            yield return null;
            ScreenCapture.CaptureScreenshot("Builds/Screenshots/m4_damage_white.png");
            Debug.Log($"[PROBE] 白字截圖:累計 {near.PopupTotal:0.0}");
            yield return null;
            yield return null;

            yield return FireOne(intent, near);
            yield return null;
            yield return null;
            ScreenCapture.CaptureScreenshot("Builds/Screenshots/m4_damage_kill.png");
            Debug.Log($"[PROBE] 紅字截圖:累計 {near.PopupTotal:0.0} 擊殺={near.PopupIsKill}");
            yield return null;
            yield return null;
            yield return null;

            controller.enabled = false;
            player.transform.position = spawnPos;
            controller.enabled = true;
            rig.SetAngles(0f, 12f);
            router.ClearOverrideSource();
            tank.InfiniteInk = false;
            Debug.Log("[PROBE] DONE 截圖完成");
            Destroy(gameObject);
        }

        private static IEnumerator FireOne(TestIntentSource intent, TargetDummy target)
        {
            int version = target.PopupVersion;
            intent.AttackHeld = true;
            yield return null;
            intent.AttackHeld = false;
            float deadline = Time.time + 0.6f;
            while (target.PopupVersion == version && Time.time < deadline)
            {
                yield return null;
            }
            // 讓 FireClock 冷卻過去,下一發才不會被吃掉
            yield return new WaitForSeconds(0.1f);
        }
    }
}

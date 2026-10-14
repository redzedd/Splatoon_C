using System.Collections;
using System.IO;
using SplatoonC.Gameplay.CameraRig;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 截圖探針(不斷言):啟動大招丟一個裝置到 TargetWall,截龍捲風「細」與「粗」兩張。輸出 Builds/Screenshots/。
    public sealed class InkstrikeShowcaseProbe : MonoBehaviour
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
            var host = new GameObject("InkstrikeShowcaseProbe");
            host.AddComponent<InkstrikeShowcaseProbe>();
        }

        private IEnumerator Start()
        {
            GameObject player = GameObject.Find("Player");
            var router = player != null ? player.GetComponent<PlayerInputRouter>() : null;
            var controller = player != null ? player.GetComponent<CharacterController>() : null;
            var gauge = player != null ? player.GetComponent<PlayerSpecialGauge>() : null;
            var rig = Camera.main != null ? Camera.main.GetComponent<ThirdPersonCameraRig>() : null;
            if (router == null || controller == null || gauge == null || rig == null)
            {
                Debug.LogError("[PROBE] 前置失敗:找不到 Player/PlayerSpecialGauge/CameraRig");
                Destroy(gameObject);
                yield break;
            }
            Directory.CreateDirectory("Builds/Screenshots");
            var intent = new TestIntentSource();
            router.SetOverrideSource(intent);
            Vector3 spawnPos = player.transform.position;

            controller.enabled = false;
            player.transform.position = new Vector3(18f, 0.1f, 4f);
            controller.enabled = true;
            // 俯角 12°:平視或更淺,初速 22 + 上抬 25° 會從 3m 牆頂飛過
            rig.SetAngles(90f, 12f);
            yield return new WaitForSeconds(0.35f);

            gauge.SetPointsForTest(100f);
            yield return null;
            intent.SpecialPressedThisFrame = true;
            yield return null;
            intent.SpecialPressedThisFrame = false;
            intent.SubWeaponHeld = true;
            yield return new WaitForSeconds(0.35f);
            intent.SubWeaponHeld = false;

            float deadline = Time.time + 3f;
            while (Tornado.ActiveCount == 0 && Time.time < deadline)
            {
                yield return null;
            }
            yield return new WaitForSeconds(0.12f);
            ScreenCapture.CaptureScreenshot("Builds/Screenshots/m4_tornado_thin.png");
            Debug.Log("[PROBE] 龍捲風細截圖");
            yield return new WaitForSeconds(0.85f);
            ScreenCapture.CaptureScreenshot("Builds/Screenshots/m4_tornado_thick.png");
            Debug.Log("[PROBE] 龍捲風粗截圖");
            yield return new WaitForSeconds(1.5f);

            controller.enabled = false;
            player.transform.position = spawnPos;
            controller.enabled = true;
            rig.SetAngles(0f, 12f);
            router.ClearOverrideSource();
            Debug.Log("[PROBE] DONE 龍捲風截圖完成");
            Destroy(gameObject);
        }
    }
}

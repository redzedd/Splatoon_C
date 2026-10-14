using System.Collections;
using System.IO;
using SplatoonC.Gameplay.CameraRig;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 截圖探針(不斷言):按住副武器截拋物線 + 落點標記,放開後截爆炸噴墨。輸出 Builds/Screenshots/。
    public sealed class BombShowcaseProbe : MonoBehaviour
    {
        private sealed class TestIntentSource : IPlayerIntentSource
        {
            public Vector2 MoveInput => Vector2.zero;
            public Vector2 LookDeltaDeg => Vector2.zero;
            public bool JumpPressedThisFrame => false;
            public bool AttackHeld => false;
            public bool SquidHeld => false;
            public bool SubWeaponHeld { get; set; }
        }

        public static void Run()
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("[PROBE] 需在 Play mode 中執行");
                return;
            }
            var host = new GameObject("BombShowcaseProbe");
            host.AddComponent<BombShowcaseProbe>();
        }

        private IEnumerator Start()
        {
            GameObject player = GameObject.Find("Player");
            var router = player != null ? player.GetComponent<PlayerInputRouter>() : null;
            var controller = player != null ? player.GetComponent<CharacterController>() : null;
            var tank = player != null ? player.GetComponent<PlayerInkTank>() : null;
            var thrower = player != null ? player.GetComponent<ThrowController>() : null;
            var rig = Camera.main != null ? Camera.main.GetComponent<ThirdPersonCameraRig>() : null;
            if (router == null || controller == null || tank == null || thrower == null || rig == null)
            {
                Debug.LogError("[PROBE] 前置失敗:找不到 Player/ThrowController/CameraRig");
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
            rig.SetAngles(0f, 15f);
            for (int i = 0; i < 30; i++)
            {
                yield return null;
            }

            intent.SubWeaponHeld = true;
            for (int i = 0; i < 40; i++)
            {
                yield return null;
            }
            ScreenCapture.CaptureScreenshot("Builds/Screenshots/m4_bomb_arc.png");
            Debug.Log($"[PROBE] 拋物線截圖:落點={thrower.PredictedLanding} 相機距離={rig.CurrentDistance:0.00}");
            yield return null;
            yield return null;

            int explosions = SplatBomb.ExplosionCount;
            intent.SubWeaponHeld = false;
            float deadline = Time.time + 4f;
            while (SplatBomb.ExplosionCount == explosions && Time.time < deadline)
            {
                yield return null;
            }
            for (int i = 0; i < 4; i++)
            {
                yield return null;
            }
            ScreenCapture.CaptureScreenshot("Builds/Screenshots/m4_bomb_explosion.png");
            Debug.Log($"[PROBE] 爆炸截圖:爆心={SplatBomb.LastExplosionCenter}");
            for (int i = 0; i < 3; i++)
            {
                yield return null;
            }

            controller.enabled = false;
            player.transform.position = spawnPos;
            controller.enabled = true;
            rig.SetAngles(0f, 12f);
            router.ClearOverrideSource();
            tank.InfiniteInk = false;
            Debug.Log("[PROBE] DONE 炸彈截圖完成");
            Destroy(gameObject);
        }
    }
}

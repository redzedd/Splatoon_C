using System.Collections;
using SplatoonC.Gameplay.CameraRig;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 驗收潛水手感三項(使用者指定 2026-09-02):
    // 平時/潛水速度倍數、離墨後 0.36 秒緩降、潛水跳躍保留原方向與原速度。
    public sealed class SwimFeelAutoTest : MonoBehaviour
    {
        private sealed class TestIntentSource : IPlayerIntentSource
        {
            public Vector2 MoveInput { get; set; }
            public Vector2 LookDeltaDeg => Vector2.zero;
            public bool JumpPressedThisFrame { get; set; }
            public bool AttackHeld => false;
            public bool SquidHeld { get; set; }
        }

        public static void Run()
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("[AUTOTEST] 需在 Play mode 中執行");
                return;
            }
            var host = new GameObject("SwimFeelAutoTest");
            host.AddComponent<SwimFeelAutoTest>();
        }

        private int _passed;
        private int _failed;

        private IEnumerator Start()
        {
            GameObject player = GameObject.Find("Player");
            var router = player.GetComponent<PlayerInputRouter>();
            var controller = player.GetComponent<CharacterController>();
            var squid = player.GetComponent<SquidController>();
            var rig = Camera.main.GetComponent<ThirdPersonCameraRig>();
            PaintableSurface ground = null;
            foreach (var surface in PaintableSurface.Active)
            {
                if (surface.name == "Ground")
                {
                    ground = surface;
                }
            }
            if (router == null || squid == null || rig == null || ground == null)
            {
                Debug.LogError("[AUTOTEST] FAIL 前置:Player/Squid/Rig/Ground 缺一");
                Debug.Log("[AUTOTEST] DONE passed=0 failed=1");
                Destroy(gameObject);
                yield break;
            }

            var intent = new TestIntentSource();
            router.SetOverrideSource(intent);
            controller.enabled = false;
            player.transform.position = new Vector3(-16f, 0.1f, 18f);
            controller.enabled = true;
            rig.SetAngles(180f, 10f);
            // 沿 -Z 鋪一條夠長的墨帶(潛水速度 14 m/s,1 秒就跑很遠)
            for (int i = 0; i <= 20; i++)
            {
                ground.Paint(new Vector3(-16f, 0f, 18f - i), 2.5f,
                    new Color(1f, 0.5f, 0f, 1f), 0.7f);
            }
            for (int i = 0; i < 10; i++)
            {
                yield return null;
            }

            // 案 1:平時移動速度 ≈ 9(MoveSpeed x1.5)
            Vector3 before = player.transform.position;
            intent.MoveInput = new Vector2(0f, 1f);
            yield return new WaitForSeconds(1f);
            intent.MoveInput = Vector2.zero;
            float walkSpeed = HorizontalDistance(player.transform.position, before);
            Check("平時移動速度", walkSpeed > 7.5f && walkSpeed < 10f,
                $"1 秒位移={walkSpeed:F2}m(期望約 9)");
            yield return new WaitForSeconds(0.4f);

            // 案 2:潛水移動速度 ≈ 14(9 x 1.56)
            controller.enabled = false;
            player.transform.position = new Vector3(-16f, 0.1f, 18f);
            controller.enabled = true;
            yield return null;
            intent.SquidHeld = true;
            yield return new WaitForSeconds(0.3f);
            before = player.transform.position;
            intent.MoveInput = new Vector2(0f, 1f);
            yield return new WaitForSeconds(1f);
            float swimSpeed = HorizontalDistance(player.transform.position, before);
            Check("潛水移動速度", swimSpeed > 12f && swimSpeed < 16f,
                $"1 秒位移={swimSpeed:F2}m(期望約 14.0)");

            // 案 3:離開墨水後倍率在 0.36 秒內滑落,不是瞬間歸零
            float multiplierInInk = squid.CurrentSpeedMultiplier;
            intent.MoveInput = Vector2.zero;
            intent.SquidHeld = false;
            yield return null;
            yield return null;
            float multiplierJustAfter = squid.CurrentSpeedMultiplier;
            yield return new WaitForSeconds(0.5f);
            float multiplierSettled = squid.CurrentSpeedMultiplier;
            Check("離墨速度緩降",
                multiplierInInk > 1.4f && multiplierJustAfter > 1.2f
                && multiplierJustAfter < multiplierInInk && Mathf.Abs(multiplierSettled - 1f) < 0.02f,
                $"墨中={multiplierInInk:F2} 離墨兩幀後={multiplierJustAfter:F2}(應仍明顯 >1) " +
                $"0.5 秒後={multiplierSettled:F2}(應為 1.00)");

            // 案 4:潛水跳躍保留原行進方向與原潛水速度
            controller.enabled = false;
            player.transform.position = new Vector3(-16f, 0.1f, 18f);
            controller.enabled = true;
            yield return null;
            intent.SquidHeld = true;
            intent.MoveInput = new Vector2(0f, 1f);
            yield return new WaitForSeconds(0.6f);
            Vector3 preJump = player.transform.position;
            intent.JumpPressedThisFrame = true;
            yield return null;
            intent.JumpPressedThisFrame = false;
            // 起跳後立刻放開輸入並離開烏賊態:動量鎖住的話水平速度不該掉
            intent.MoveInput = Vector2.zero;
            intent.SquidHeld = false;
            yield return null;
            Vector3 airStart = player.transform.position;
            yield return new WaitForSeconds(0.2f);
            Vector3 airEnd = player.transform.position;
            float airSpeed = HorizontalDistance(airEnd, airStart) / 0.2f;
            Vector3 airDir = airEnd - airStart;
            airDir.y = 0f;
            // 起跳方向是世界 -Z
            float dirDot = Vector3.Dot(airDir.normalized, Vector3.back);
            Check("潛水跳躍保留速度與方向",
                airSpeed > 12f && dirDot > 0.95f,
                $"空中水平速度={airSpeed:F2}m/s(期望約 14) 方向吻合度={dirDot:F3}(期望 >0.95)");
            intent.JumpPressedThisFrame = false;
            intent.MoveInput = Vector2.zero;
            yield return new WaitForSeconds(1f);

            router.ClearOverrideSource();
            Debug.Log($"[AUTOTEST] DONE passed={_passed} failed={_failed}");
            Destroy(gameObject);
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            return new Vector2(a.x - b.x, a.z - b.z).magnitude;
        }

        private void Check(string caseName, bool pass, string evidence)
        {
            if (pass)
            {
                _passed++;
                Debug.Log($"[AUTOTEST] PASS {caseName}: {evidence}");
            }
            else
            {
                _failed++;
                Debug.LogError($"[AUTOTEST] FAIL {caseName}: {evidence}");
            }
        }
    }
}

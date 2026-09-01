using System.Collections;
using SplatoonC.Gameplay.CameraRig;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // M2 步驟 4 煙霧測試:烏賊爬牆——乾牆不可爬、自家墨牆爬升、到頂翻越登上平台。
    public sealed class ClimbAutoTest : MonoBehaviour
    {
        private sealed class TestIntentSource : IPlayerIntentSource
        {
            public Vector2 MoveInput { get; set; }
            public Vector2 LookDeltaDeg { get; set; }
            public bool JumpPressedThisFrame { get; set; }
            public bool AttackHeld { get; set; }
            public bool SquidHeld { get; set; }
        }

        public static void Run()
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("[AUTOTEST] 需在 Play mode 中執行");
                return;
            }
            var host = new GameObject("ClimbAutoTest");
            host.AddComponent<ClimbAutoTest>();
        }

        private int _passed;
        private int _failed;

        private IEnumerator Start()
        {
            GameObject player = GameObject.Find("Player");
            PlayerInputRouter router = player != null ? player.GetComponent<PlayerInputRouter>() : null;
            CharacterController controller = player != null ? player.GetComponent<CharacterController>() : null;
            ThirdPersonCameraRig rig = Camera.main != null
                ? Camera.main.GetComponent<ThirdPersonCameraRig>() : null;
            PaintableSurface wall = null;
            foreach (var surface in PaintableSurface.Active)
            {
                if (surface.name == "ClimbWall_High")
                {
                    wall = surface;
                }
            }
            if (router == null || controller == null || rig == null || wall == null)
            {
                Debug.LogError("[AUTOTEST] FAIL 前置:Player/Controller/Rig/ClimbWall_High 缺一");
                Debug.Log("[AUTOTEST] DONE passed=0 failed=1");
                Destroy(gameObject);
                yield break;
            }

            var intent = new TestIntentSource();
            router.SetOverrideSource(intent);

            // 就位:牆前 1m,面向牆(+X),越過舊遮擋牆的射線問題(直接傳送)
            controller.enabled = false;
            player.transform.position = new Vector3(11f, 0.1f, 0f);
            controller.enabled = true;
            rig.SetAngles(90f, 10f);
            yield return null;
            yield return null;

            // 案 1:乾牆不可爬——烏賊態頂著沒墨的牆推 1 秒,不上升
            intent.SquidHeld = true;
            intent.MoveInput = new Vector2(0f, 1f);
            yield return new WaitForSeconds(1f);
            float dryY = player.transform.position.y;
            Check("乾牆不可爬", dryY < 0.5f, $"y={dryY:F2}");
            intent.MoveInput = Vector2.zero;

            // 鋪牆面墨帶(全高)
            for (int i = 0; i <= 4; i++)
            {
                wall.Paint(new Vector3(12f, i, 0f), 1.2f, new Color(1f, 0.5f, 0f, 1f), 0.7f);
            }
            yield return null;

            // 案 2:自家墨牆爬升——推牆 0.6 秒應明顯離地
            intent.MoveInput = new Vector2(0f, 1f);
            yield return new WaitForSeconds(0.6f);
            float climbY = player.transform.position.y;
            Check("墨牆爬升", climbY > 0.8f, $"y={climbY:F2}(爬牆速度 3.5)");

            // 案 2.5:爬牆中泡在墨牆裡 → 完全隱形,且視覺往牆內沉(不是往下沉)
            //(OnOwnInk 是向下射線,爬牆時腳下沒墨,所以必須靠牆面狀態才不會整個人露在牆外)
            var squid = player.GetComponent<SquidController>();
            var visual = player.transform.Find("Visual");
            var climbRenderers = visual.GetComponentsInChildren<Renderer>(true);
            int visibleOnWall = 0;
            foreach (var r in climbRenderers)
            {
                if (r.enabled)
                {
                    visibleOnWall++;
                }
            }
            // 牆面法線是 -X,往牆裡沉 = 世界 +X;Player 未旋轉故局部 x 應為正
            float intoWallOffset = visual.localPosition.x;
            Check("爬牆時隱形且沉入牆內",
                visibleOnWall == 0 && squid.IsSubmerged && intoWallOffset > 0.5f,
                $"可見={visibleOnWall}/{climbRenderers.Length} submerged={squid.IsSubmerged} " +
                $"沉入牆內={intoWallOffset:F2}m 視覺Y偏移={visual.localPosition.y:F2}(應接近 0)");

            // 案 3:繼續推到頂 → 翻越 → 落在平台上。
            // 只再推 1 秒:0.53s 到頂 + 0.35s 翻越 + 0.12s 前進;推太久會走過 3m 深的平台掉下去
            //(2026-09-02 首輪紅:多推 1.5s,落點 x=18.5 地面)。
            yield return new WaitForSeconds(1f);
            intent.MoveInput = Vector2.zero;
            intent.SquidHeld = false;
            yield return new WaitForSeconds(0.8f);
            Vector3 final = player.transform.position;
            Check("翻越登台", controller.isGrounded && final.y > 3.7f && final.x > 12.1f,
                $"grounded={controller.isGrounded} pos={final:F2}(平台頂 y=4,x 12.1~15.1)");

            router.ClearOverrideSource();
            Debug.Log($"[AUTOTEST] DONE passed={_passed} failed={_failed}");
            Destroy(gameObject);
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

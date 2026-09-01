using System.Collections;
using SplatoonC.Gameplay.CameraRig;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // M3 步驟 3 煙霧測試:手感層——FOV 隨速、落地擠壓回彈。
    // 瞬態數值必須在遊戲內 coroutine 讀(MCP 兩段式 probe 往返 4 秒起跳,玩家早衝出場)。
    public sealed class FeelAutoTest : MonoBehaviour
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
            var host = new GameObject("FeelAutoTest");
            host.AddComponent<FeelAutoTest>();
        }

        private int _passed;
        private int _failed;

        private IEnumerator Start()
        {
            GameObject player = GameObject.Find("Player");
            PlayerInputRouter router = player != null ? player.GetComponent<PlayerInputRouter>() : null;
            CharacterController controller = player != null ? player.GetComponent<CharacterController>() : null;
            Transform visual = player != null ? player.transform.Find("Visual") : null;
            ThirdPersonCameraRig rig = Camera.main != null
                ? Camera.main.GetComponent<ThirdPersonCameraRig>() : null;
            PaintableSurface ground = null;
            foreach (var surface in PaintableSurface.Active)
            {
                if (surface.name == "Ground")
                {
                    ground = surface;
                }
            }
            if (router == null || controller == null || visual == null || rig == null || ground == null)
            {
                Debug.LogError("[AUTOTEST] FAIL 前置:Player/Controller/Visual/Rig/Ground 缺一");
                Debug.Log("[AUTOTEST] DONE passed=0 failed=1");
                Destroy(gameObject);
                yield break;
            }

            var intent = new TestIntentSource();
            router.SetOverrideSource(intent);

            // 案 1:FOV 隨速——沿 50m 長軸鋪墨帶,烏賊衝刺 1 秒中讀 FOV
            controller.enabled = false;
            player.transform.position = new Vector3(20f, 0.1f, 22f);
            controller.enabled = true;
            rig.SetAngles(180f, 10f);
            var orange = new Color(1f, 0.5f, 0f, 1f);
            for (int i = 0; i <= 30; i += 2)
            {
                ground.Paint(new Vector3(20f, 0f, 22f - i), 2.2f, orange, 0.7f);
            }
            yield return null;
            yield return null;
            intent.SquidHeld = true;
            intent.MoveInput = new Vector2(0f, 1f);
            yield return new WaitForSeconds(1f);
            float dashFov = Camera.main.fieldOfView;
            intent.MoveInput = Vector2.zero;
            intent.SquidHeld = false;
            Check("FOV隨速", dashFov > 62f, $"衝刺中 FOV={dashFov:F1}(基準 60)");
            yield return new WaitForSeconds(0.6f);
            float idleFov = Camera.main.fieldOfView;
            Check("FOV回落", idleFov < 61f, $"靜止 FOV={idleFov:F1}");

            // 案 2:落地擠壓——從 5m 高落下,落地瞬間壓扁再回彈
            controller.enabled = false;
            player.transform.position = new Vector3(0f, 5f, -15f);
            controller.enabled = true;
            float minScaleY = 1f;
            float elapsed = 0f;
            bool landed = false;
            while (elapsed < 2f)
            {
                yield return null;
                elapsed += Time.deltaTime;
                if (!landed && controller.isGrounded && elapsed > 0.2f)
                {
                    landed = true;
                    elapsed = 1.3f; // 落地後再觀測 0.7 秒
                }
                if (landed)
                {
                    minScaleY = Mathf.Min(minScaleY, visual.localScale.y);
                }
            }
            float settled = visual.localScale.y;
            Check("落地擠壓", landed && minScaleY < 0.9f, $"landed={landed} 最低縮放={minScaleY:F2}");
            Check("擠壓回彈", settled > 0.9f, $"安定縮放={settled:F2}");

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

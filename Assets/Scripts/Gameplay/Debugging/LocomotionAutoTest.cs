using System.Collections;
using SplatoonC.Gameplay.CameraRig;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // M1 步驟 2 煙霧測試:scripted intent 驅動真實路徑
    // (Router → Solver → CharacterController → CameraRig),結果以 [AUTOTEST] 標記供 log 掃描。
    public sealed class LocomotionAutoTest : MonoBehaviour
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
            var host = new GameObject("LocomotionAutoTest");
            host.AddComponent<LocomotionAutoTest>();
        }

        private int _passed;
        private int _failed;

        private IEnumerator Start()
        {
            GameObject player = GameObject.Find("Player");
            PlayerInputRouter router = player != null ? player.GetComponent<PlayerInputRouter>() : null;
            Camera cam = Camera.main;
            Transform pivot = player != null ? player.transform.Find("CameraPivot") : null;
            if (player == null || router == null || cam == null || pivot == null)
            {
                Debug.LogError("[AUTOTEST] FAIL 前置:找不到 Player / Router / Main Camera / CameraPivot");
                Debug.Log("[AUTOTEST] DONE passed=0 failed=1");
                Destroy(gameObject);
                yield break;
            }

            var intent = new TestIntentSource();
            router.SetOverrideSource(intent);
            // 相機角度歸位:遮擋案假設 yaw 從 0 起算;真滑鼠可能在測試前污染角度。
            var rig = cam.GetComponent<ThirdPersonCameraRig>();
            if (rig != null)
            {
                rig.SetAngles(0f, 10f);
            }
            yield return null;

            // 案 1:重力著地——靜置 1 秒後 y 收斂於地面附近,不持續下墜
            yield return new WaitForSeconds(1f);
            float restY = player.transform.position.y;
            Check("重力著地", restY > -0.5f && restY < 0.3f, $"restY={restY:F3}");

            // 案 2:視角一幀轉 90 度,相機 yaw 零延遲跟上(此時相機轉到 -X 側,無遮擋)
            float yawBefore = cam.transform.eulerAngles.y;
            intent.LookDeltaDeg = new Vector2(90f, 0f);
            yield return null;
            intent.LookDeltaDeg = Vector2.zero;
            yield return null;
            float yawDelta = Mathf.DeltaAngle(yawBefore, cam.transform.eulerAngles.y);
            Check("視角轉動", Mathf.Abs(yawDelta - 90f) < 5f, $"yawDelta={yawDelta:F1}(期望 90)");

            // 案 3:無遮擋時相機距離 ≈ 5
            float freeDistance = Vector3.Distance(cam.transform.position, pivot.position);
            Check("相機距離", Mathf.Abs(freeDistance - 5f) < 0.4f, $"dist={freeDistance:F2}(期望 5)");

            // 案 4:再轉 180 度(共 270),相機落到 +X 側,出生點旁的牆擋在中間,SphereCast 應拉近
            intent.LookDeltaDeg = new Vector2(180f, 0f);
            yield return null;
            intent.LookDeltaDeg = Vector2.zero;
            yield return null;
            float occludedDistance = Vector3.Distance(cam.transform.position, pivot.position);
            Check("遮擋拉近", occludedDistance < 2.5f, $"dist={occludedDistance:F2}(牆面約 1.25)");

            // 案 5:前進 1 秒位移 ≈ 移動速度(6);沿當前相機朝向(-X)移動,遠離牆與矮階
            Vector3 before = player.transform.position;
            intent.MoveInput = new Vector2(0f, 1f);
            yield return new WaitForSeconds(1f);
            intent.MoveInput = Vector2.zero;
            Vector3 moved = player.transform.position - before;
            moved.y = 0f;
            Check("前進位移", moved.magnitude > 4.5f && moved.magnitude < 7.5f,
                $"|d|={moved.magnitude:F2}(期望約 6)");

            // 案 6:跳躍峰值 ≈ 跳躍高度(1.6)
            yield return new WaitForSeconds(0.5f);
            float baseY = player.transform.position.y;
            intent.JumpPressedThisFrame = true;
            yield return null;
            intent.JumpPressedThisFrame = false;
            float peak = baseY;
            float elapsed = 0f;
            while (elapsed < 1.2f)
            {
                peak = Mathf.Max(peak, player.transform.position.y);
                elapsed += Time.deltaTime;
                yield return null;
            }
            float jumpHeight = peak - baseY;
            Check("跳躍峰值", jumpHeight > 1.3f && jumpHeight < 1.9f,
                $"峰值={jumpHeight:F2}(期望約 1.6)");

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

using System.Collections;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 驗收「鑽進/鑽出墨水有過場」(使用者回饋 2026-09-02:原本是突然出現突然消失)。
    // 關鍵是中間態要存在:按下的下一幀角色仍看得見、但已經開始下沉。
    public sealed class DiveAutoTest : MonoBehaviour
    {
        private sealed class TestIntentSource : IPlayerIntentSource
        {
            public Vector2 MoveInput => Vector2.zero;
            public Vector2 LookDeltaDeg => Vector2.zero;
            public bool JumpPressedThisFrame => false;
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
            var host = new GameObject("DiveAutoTest");
            host.AddComponent<DiveAutoTest>();
        }

        private int _passed;
        private int _failed;

        private IEnumerator Start()
        {
            GameObject player = GameObject.Find("Player");
            var router = player.GetComponent<PlayerInputRouter>();
            var squid = player.GetComponent<SquidController>();
            var controller = player.GetComponent<CharacterController>();
            Transform visual = player.transform.Find("Visual");
            PaintableSurface ground = null;
            foreach (var surface in PaintableSurface.Active)
            {
                if (surface.name == "Ground")
                {
                    ground = surface;
                }
            }
            if (router == null || squid == null || visual == null || ground == null)
            {
                Debug.LogError("[AUTOTEST] FAIL 前置:Player/Squid/Visual/Ground 缺一");
                Debug.Log("[AUTOTEST] DONE passed=0 failed=1");
                Destroy(gameObject);
                yield break;
            }

            var intent = new TestIntentSource();
            router.SetOverrideSource(intent);
            controller.enabled = false;
            player.transform.position = new Vector3(-16f, 0.1f, 18f);
            controller.enabled = true;
            for (int i = 0; i < 10; i++)
            {
                yield return null;
            }
            // 腳下鋪自家墨才潛得下去
            ground.Paint(player.transform.position, 3f, new Color(1f, 0.5f, 0f, 1f), 0.7f);
            yield return null;

            var renderers = visual.GetComponentsInChildren<Renderer>(true);
            float baseY = visual.localPosition.y;

            // 案 1:按下潛行後的下一幀——還沒隱形,但已經開始下沉
            intent.SquidHeld = true;
            yield return null;
            yield return null;
            float midProgress = squid.DiveProgress;
            int visibleMidDive = CountVisible(renderers);
            float midY = visual.localPosition.y;
            Check("鑽進有中間態",
                midProgress > 0f && midProgress < 1f && visibleMidDive == renderers.Length && midY < baseY - 0.01f,
                $"進度={midProgress:F2}(期望 0~1) 可見={visibleMidDive}/{renderers.Length} 視覺 Y={midY:F2}(基準 {baseY:F2})");

            // 案 2:過場走完 → 完全隱形
            yield return new WaitForSeconds(0.4f);
            int visibleSubmerged = CountVisible(renderers);
            Check("過場結束完全隱形",
                squid.IsSubmerged && Mathf.Approximately(squid.DiveProgress, 1f) && visibleSubmerged == 0,
                $"submerged={squid.IsSubmerged} 進度={squid.DiveProgress:F2} 可見={visibleSubmerged}");

            // 案 3:放開後的下一幀——立刻重新出現,但還沒回到地面高度
            intent.SquidHeld = false;
            yield return null;
            yield return null;
            float riseProgress = squid.DiveProgress;
            int visibleMidRise = CountVisible(renderers);
            float riseY = visual.localPosition.y;
            Check("鑽出有中間態",
                riseProgress > 0f && riseProgress < 1f && visibleMidRise == renderers.Length && riseY < baseY - 0.01f,
                $"進度={riseProgress:F2}(期望 0~1) 可見={visibleMidRise}/{renderers.Length} 視覺 Y={riseY:F2}");

            // 案 4:回到水面 → 位置與橫向縮放復原
            yield return new WaitForSeconds(0.4f);
            float finalY = visual.localPosition.y;
            float finalScaleX = visual.localScale.x;
            Check("起身復原",
                Mathf.Approximately(squid.DiveProgress, 0f) && Mathf.Abs(finalY - baseY) < 0.01f
                && Mathf.Abs(finalScaleX - 1f) < 0.01f,
                $"進度={squid.DiveProgress:F2} 視覺 Y={finalY:F2}(基準 {baseY:F2}) 橫向縮放={finalScaleX:F2}");

            router.ClearOverrideSource();
            Debug.Log($"[AUTOTEST] DONE passed={_passed} failed={_failed}");
            Destroy(gameObject);
        }

        private static int CountVisible(Renderer[] renderers)
        {
            int visible = 0;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null && renderers[i].enabled)
                {
                    visible++;
                }
            }
            return visible;
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

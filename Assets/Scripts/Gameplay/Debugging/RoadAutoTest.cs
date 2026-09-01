using System.Collections;
using SplatoonC.Gameplay.CameraRig;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 驗收「0.5 秒連射即成路」(使用者指定 2026-09-02):
    // 沿瞄準方向從腳邊取樣到落點,量覆蓋率與最長缺口。
    // 每個取樣距離檢查一條橫向小窗(散布與抖動會讓墨路左右擺),
    // 只要窗內任一點有墨就算「這個距離上有路」——這才是玩家看到的判準。
    public sealed class RoadAutoTest : MonoBehaviour
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
                Debug.LogError("[AUTOTEST] 需在 Play mode 中執行");
                return;
            }
            var host = new GameObject("RoadAutoTest");
            host.AddComponent<RoadAutoTest>();
        }

        private int _passed;
        private int _failed;

        private IEnumerator Start()
        {
            GameObject player = GameObject.Find("Player");
            var router = player.GetComponent<PlayerInputRouter>();
            var controller = player.GetComponent<CharacterController>();
            var rig = Camera.main.GetComponent<ThirdPersonCameraRig>();
            PaintableSurface ground = null;
            foreach (var surface in PaintableSurface.Active)
            {
                if (surface.name == "Ground")
                {
                    ground = surface;
                }
            }
            if (router == null || rig == null || ground == null)
            {
                Debug.LogError("[AUTOTEST] FAIL 前置:Player/Rig/Ground 缺一");
                Debug.Log("[AUTOTEST] DONE passed=0 failed=1");
                Destroy(gameObject);
                yield break;
            }
            player.GetComponent<PlayerInkTank>().InfiniteInk = true;

            var intent = new TestIntentSource();
            router.SetOverrideSource(intent);
            // 未塗過的空曠處,朝 -Z(該方向 16m 內無障礙)
            controller.enabled = false;
            player.transform.position = new Vector3(-16f, 0.1f, 18f);
            controller.enabled = true;
            rig.SetAngles(180f, 8f);
            for (int i = 0; i < 30; i++)
            {
                yield return null;
            }

            Vector3 origin = player.transform.position;
            intent.AttackHeld = true;
            yield return new WaitForSeconds(0.5f);
            intent.AttackHeld = false;
            // 等最後一發落地與墨滴沉降
            yield return new WaitForSeconds(2f);

            const float startDistance = 1f;
            const float endDistance = 16f;
            const float step = 0.25f;
            const float lateralHalfWidth = 0.6f;
            int samples = 0;
            int inked = 0;
            int currentGap = 0;
            int longestGap = 0;
            float firstInkedDistance = -1f;

            for (float d = startDistance; d <= endDistance; d += step)
            {
                samples++;
                bool hasInk = false;
                for (float lateral = -lateralHalfWidth; lateral <= lateralHalfWidth; lateral += lateralHalfWidth)
                {
                    // 朝 -Z 射擊,橫向即 x
                    var probe = new Vector3(origin.x + lateral, 0f, origin.z - d);
                    if (ground.SampleOwnership(probe) == 1)
                    {
                        hasInk = true;
                        break;
                    }
                }
                if (hasInk)
                {
                    inked++;
                    if (firstInkedDistance < 0f)
                    {
                        firstInkedDistance = d;
                    }
                    currentGap = 0;
                }
                else
                {
                    currentGap++;
                    if (currentGap > longestGap)
                    {
                        longestGap = currentGap;
                    }
                }
            }

            float coverage = samples > 0 ? inked / (float)samples : 0f;
            float longestGapMeters = longestGap * step;

            Check("0.5 秒連射覆蓋率", coverage >= 0.9f,
                $"覆蓋率={coverage:P0}(期望 ≥90%,取樣 {inked}/{samples} 點,{startDistance}~{endDistance}m)");
            Check("路上無明顯缺口", longestGapMeters <= 1.5f,
                $"最長缺口={longestGapMeters:F2}m(期望 ≤1.5m)");
            Check("墨路從腳邊起", firstInkedDistance >= 0f && firstInkedDistance <= 2f,
                $"第一個有墨的距離={firstInkedDistance:F2}m(期望 ≤2m)");

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

using System.Collections;
using SplatoonC.Gameplay.CameraRig;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // M3 手感回饋修正的驗收:準星=真落點、彈道沿途留痕、烏賊潛行隱形。
    public sealed class AimAutoTest : MonoBehaviour
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
            var host = new GameObject("AimAutoTest");
            host.AddComponent<AimAutoTest>();
        }

        private int _passed;
        private int _failed;

        private IEnumerator Start()
        {
            GameObject player = GameObject.Find("Player");
            PlayerInputRouter router = player != null ? player.GetComponent<PlayerInputRouter>() : null;
            CharacterController controller = player != null ? player.GetComponent<CharacterController>() : null;
            InkShooter shooter = player != null ? player.GetComponent<InkShooter>() : null;
            SquidController squid = player != null ? player.GetComponent<SquidController>() : null;
            Transform visual = player != null ? player.transform.Find("Visual") : null;
            ThirdPersonCameraRig rig = Camera.main != null
                ? Camera.main.GetComponent<ThirdPersonCameraRig>() : null;
            var canvas = GameObject.Find("HudCanvas");
            AimReticle reticle = canvas != null ? canvas.GetComponent<AimReticle>() : null;
            PaintableSurface ground = null;
            foreach (var surface in PaintableSurface.Active)
            {
                if (surface.name == "Ground")
                {
                    ground = surface;
                }
            }
            if (router == null || controller == null || shooter == null || squid == null
                || visual == null || rig == null || reticle == null || ground == null)
            {
                Debug.LogError("[AUTOTEST] FAIL 前置:Player/Shooter/Squid/Rig/AimReticle/Ground 缺一");
                Debug.Log("[AUTOTEST] DONE passed=0 failed=1");
                Destroy(gameObject);
                yield break;
            }

            var intent = new TestIntentSource();
            router.SetOverrideSource(intent);
            player.GetComponent<PlayerInkTank>().InfiniteInk = true;

            // 就位:空曠 -Z 區,平視
            controller.enabled = false;
            player.transform.position = new Vector3(-8f, 0.1f, 6f);
            controller.enabled = true;
            rig.SetAngles(180f, 6f);
            yield return null;
            yield return null;
            yield return null;

            // 案 1:準星預測落點 vs 實跑落點(容忍散布:2.5° 在 10m 約偏 0.44m)
            Vector3 predicted = reticle.PredictedLanding;
            intent.AttackHeld = true;
            yield return null;
            yield return null;
            intent.AttackHeld = false;

            // 追蹤該發彈直到回收,記下最後位置
            var poolRoot = GameObject.Find("InkProjectilePool");
            Vector3 lastSeen = Vector3.zero;
            bool sawProjectile = false;
            float watchdog = 0f;
            while (watchdog < 3f)
            {
                bool anyActive = false;
                foreach (Transform c in poolRoot.transform)
                {
                    if (c.gameObject.activeInHierarchy)
                    {
                        anyActive = true;
                        lastSeen = c.position;
                        sawProjectile = true;
                    }
                }
                if (sawProjectile && !anyActive)
                {
                    break;
                }
                watchdog += Time.deltaTime;
                yield return null;
            }
            float aimError = Vector3.Distance(
                new Vector3(predicted.x, 0f, predicted.z), new Vector3(lastSeen.x, 0f, lastSeen.z));
            Check("準星對齊落點", sawProjectile && aimError < 1.5f,
                $"預測={predicted:F1} 實落={lastSeen:F1} 誤差={aimError:F2}m");

            // 案 2:彈道沿途留痕——沿路徑中段取樣 20 點(滴墨是離散點,單點取樣會抽中空隙)
            int inkedSamples = 0;
            for (int i = 0; i < 20; i++)
            {
                float t = Mathf.Lerp(0.25f, 0.85f, i / 19f);
                Vector3 sample = Vector3.Lerp(player.transform.position, lastSeen, t);
                if (ground.SampleOwnership(new Vector3(sample.x, 0f, sample.z)) == 1)
                {
                    inkedSamples++;
                }
            }
            Check("沿途滴墨", inkedSamples >= 12,
                $"路徑取樣 20 點中 {inkedSamples} 點有墨(期望 ≥12,連續痕跡)");

            // 案 3:烏賊潛入自家墨 → 視覺完全隱形;起身 → 恢復
            ground.Paint(player.transform.position, 3f, new Color(1f, 0.5f, 0f, 1f), 0.7f);
            yield return null;
            intent.SquidHeld = true;
            yield return new WaitForSeconds(0.4f);
            var renderers = visual.GetComponentsInChildren<Renderer>(true);
            int visibleWhileSubmerged = 0;
            foreach (var r in renderers)
            {
                if (r.enabled)
                {
                    visibleWhileSubmerged++;
                }
            }
            bool submergedFlag = squid.IsSubmerged;
            intent.SquidHeld = false;
            yield return new WaitForSeconds(0.4f);
            int visibleAfter = 0;
            foreach (var r in renderers)
            {
                if (r.enabled)
                {
                    visibleAfter++;
                }
            }
            Check("潛行隱形", submergedFlag && visibleWhileSubmerged == 0,
                $"submerged={submergedFlag} 潛行中可見={visibleWhileSubmerged}");
            Check("起身現形", visibleAfter == renderers.Length,
                $"起身後可見={visibleAfter}/{renderers.Length}");

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

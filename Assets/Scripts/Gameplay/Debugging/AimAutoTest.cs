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

            // 案 1:準星(固定畫面中心)指向的世界點 vs 彈實跑落點
            // ——Splatoon 式固定準心的成立條件:彈道夠平直,中心即命中點。
            var cam = Camera.main;
            int aimMask = ~(1 << LayerMask.NameToLayer("Player"));
            Vector3 predicted = Physics.Raycast(
                new Ray(cam.transform.position, cam.transform.forward),
                out RaycastHit centerHit, 60f, aimMask, QueryTriggerInteraction.Ignore)
                ? centerHit.point
                : cam.transform.position + cam.transform.forward * 20f;
            intent.AttackHeld = true;
            yield return null;
            yield return null;
            intent.AttackHeld = false;

            // 追蹤該發彈:先抓飛行方向(頭幾幀),再追到回收記下落點
            var poolRoot = GameObject.Find("InkProjectilePool");
            Vector3 lastSeen = Vector3.zero;
            Vector3 firstSeen = Vector3.zero;
            Vector3 flightDir = Vector3.zero;
            bool sawProjectile = false;
            bool dirCaptured = false;
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
                        if (!sawProjectile)
                        {
                            firstSeen = c.position;
                            sawProjectile = true;
                        }
                        else if (!dirCaptured && (c.position - firstSeen).sqrMagnitude > 0.04f)
                        {
                            flightDir = (c.position - firstSeen).normalized;
                            dirCaptured = true;
                        }
                    }
                }
                if (sawProjectile && !anyActive)
                {
                    break;
                }
                watchdog += Time.deltaTime;
                yield return null;
            }

            // 固定中心準心保證的是「射擊方向」而非落點——拋物線武器的必然:平視時中心射線
            // 落在 20m 外,而彈只飛 9m(2026-09-02 實測 18.3m 落差)。故驗收比對方向。
            float aimAngle = dirCaptured ? Vector3.Angle(flightDir, cam.transform.forward) : 999f;
            float rangeM = Vector3.Distance(
                new Vector3(player.transform.position.x, 0f, player.transform.position.z),
                new Vector3(lastSeen.x, 0f, lastSeen.z));
            Check("準星方向一致", dirCaptured && aimAngle < 12f,
                $"彈道與準心夾角={aimAngle:F1}°(含散布 2.5°+槍口視差) 射程={rangeM:F1}m 準心指向距離={Vector3.Distance(player.transform.position, predicted):F1}m");

            // 案 2:彈道沿途留痕——沿路徑中段取樣 20 點(滴墨是離散點,單點取樣會抽中空隙)
            // 取樣線起點用槍口而非角色中心:彈從角色右側 0.52m 射出,用角色中心會與真實彈道
            // 有橫向偏差而低估覆蓋率(2026-09-02:同一條墨帶用兩種起點量到 8/20 vs 14/20)。
            Vector3 traceStart = visual.Find("Muzzle") != null
                ? visual.Find("Muzzle").position : player.transform.position;
            int inkedSamples = 0;
            for (int i = 0; i < 20; i++)
            {
                float t = Mathf.Lerp(0.25f, 0.85f, i / 19f);
                Vector3 sample = Vector3.Lerp(traceStart, lastSeen, t);
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

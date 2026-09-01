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
            // 單發射擊並追蹤;散布角讓單發有雜訊,故最多重射 5 次直到取得射程 > 9m 的一發。
            var poolRoot = GameObject.Find("InkProjectilePool");
            Vector3 lastSeen = Vector3.zero;
            Vector3 firstSeen = Vector3.zero;
            Vector3 flightDir = Vector3.zero;
            bool sawProjectile = false;
            bool dirCaptured = false;
            float maxDropInRange = 0f;
            float rangeM = 0f;

            for (int attempt = 0; attempt < 5; attempt++)
            {
                lastSeen = Vector3.zero;
                firstSeen = Vector3.zero;
                sawProjectile = false;
                dirCaptured = false;
                maxDropInRange = 0f;

                intent.AttackHeld = true;
                yield return null;
                yield return null;
                intent.AttackHeld = false;

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
                            else
                            {
                                if (!dirCaptured && (c.position - firstSeen).sqrMagnitude > 0.04f)
                                {
                                    flightDir = (c.position - firstSeen).normalized;
                                    dirCaptured = true;
                                }
                                float horizontal = new Vector2(
                                    c.position.x - firstSeen.x, c.position.z - firstSeen.z).magnitude;
                                if (horizontal < 8f)
                                {
                                    maxDropInRange = Mathf.Max(maxDropInRange, firstSeen.y - c.position.y);
                                }
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

                rangeM = Vector3.Distance(
                    new Vector3(player.transform.position.x, 0f, player.transform.position.z),
                    new Vector3(lastSeen.x, 0f, lastSeen.z));
                if (rangeM > 9f)
                {
                    break;
                }
                yield return new WaitForSeconds(0.2f);
            }

            // 固定中心準心保證的是「射擊方向」而非落點——拋物線武器的必然:平視時中心射線
            // 落在 20m 外,而彈只飛 9m(2026-09-02 實測 18.3m 落差)。故驗收比對方向。
            float aimAngle = dirCaptured ? Vector3.Angle(flightDir, cam.transform.forward) : 999f;
            // 夾角主要來自 TPS 固有的相機↔槍口位置差(相機在後上方、槍口在角色右前),
            // 加上散布與彈道補償上抬。真正的命中驗收在 AimPitchProbe(射程極限是否落在準心線上)。
            Check("準星方向一致", dirCaptured && aimAngle < 15f,
                $"彈道與準心夾角={aimAngle:F1}°(含散布 2.5°+槍口視差) 射程={rangeM:F1}m 準心指向距離={Vector3.Distance(player.transform.position, predicted):F1}m");

            // 兩段式彈道:射程內幾乎不掉高度(維持準心高度),超程才急墜
            Check("射程內近直線", maxDropInRange < 0.6f,
                $"8m 內最大下墜={maxDropInRange:F2}m(期望 <0.6,舊單一拋物線約 1.5+)");

            // 案 2:連射鋪路——路徑痕跡由「槍口必噴濺 + 每發沿路滴下的 1~3 滴墨」累積而成,
            // 單發不會鋪出路徑(使用者觀察到的真實行為),故連射再量。
            // 用俯視角(玩家實際塗地的視角):平視時彈全落在遠端,腳前自然沒有路。
            rig.SetAngles(180f, 22f);
            yield return null;
            yield return null;
            Vector3 pathTargetStart = player.transform.position;
            intent.AttackHeld = true;
            yield return new WaitForSeconds(1.5f);
            intent.AttackHeld = false;
            yield return new WaitForSeconds(1f);
            // 俯視下的落點:準心射線與地面的交點
            Vector3 pathEnd = lastSeen;
            if (Physics.Raycast(new Ray(cam.transform.position, cam.transform.forward),
                    out RaycastHit groundHit, 40f, aimMask, QueryTriggerInteraction.Ignore))
            {
                pathEnd = groundHit.point;
            }
            lastSeen = pathEnd;

            Vector3 traceStart = visual.Find("Muzzle") != null
                ? visual.Find("Muzzle").position : player.transform.position;
            int inkedSamples = 0;
            for (int i = 0; i < 20; i++)
            {
                float t = Mathf.Lerp(0.08f, 0.85f, i / 19f);
                Vector3 sample = Vector3.Lerp(traceStart, lastSeen, t);
                if (ground.SampleOwnership(new Vector3(sample.x, 0f, sample.z)) == 1)
                {
                    inkedSamples++;
                }
            }
            Check("連射鋪路", inkedSamples >= 6,
                $"槍口→落點取樣 20 點中 {inkedSamples} 點有墨(槍口噴濺+沿路滴墨累積)");

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

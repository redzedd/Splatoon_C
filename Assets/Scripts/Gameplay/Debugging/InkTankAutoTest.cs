using System.Collections;
using SplatoonC.Gameplay.CameraRig;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;
using UnityEngine.Rendering;

namespace SplatoonC.Gameplay.Debugging
{
    // M3 步驟 1 煙霧測試:墨量迴圈——連射耗盡停火、烏賊自家墨回墨、恢復射擊、HUD 同步。
    public sealed class InkTankAutoTest : MonoBehaviour
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
            var host = new GameObject("InkTankAutoTest");
            host.AddComponent<InkTankAutoTest>();
        }

        private int _passed;
        private int _failed;
        private int _lastCount;

        private IEnumerator Start()
        {
            GameObject player = GameObject.Find("Player");
            PlayerInputRouter router = player != null ? player.GetComponent<PlayerInputRouter>() : null;
            PlayerInkTank tank = player != null ? player.GetComponent<PlayerInkTank>() : null;
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
            var fillGo = GameObject.Find("InkBarFill");
            RectTransform fill = fillGo != null ? fillGo.GetComponent<RectTransform>() : null;
            if (router == null || tank == null || rig == null || ground == null || fill == null)
            {
                Debug.LogError("[AUTOTEST] FAIL 前置:Player/Tank/Rig/Ground/InkBarFill 缺一");
                Debug.Log("[AUTOTEST] DONE passed=0 failed=1");
                Destroy(gameObject);
                yield break;
            }

            var intent = new TestIntentSource();
            router.SetOverrideSource(intent);
            rig.SetAngles(180f, 10f);
            yield return null;
            yield return null;

            // 設計目標(使用者指定 2026-09-02):連射可撐 10 秒;開火期間完全不回墨,
            // 放開 0.5 秒後才恢復;泡在自家墨裡(烏賊)從 0 回滿約 5 秒。
            // 案 1:連射 10 秒才見底,且 5 秒時仍過半(證明是 10 秒而不是 5 秒就空)
            intent.AttackHeld = true;
            yield return new WaitForSeconds(5f);
            float midpoint = tank.Normalized;
            yield return new WaitForSeconds(5.5f);
            float drained = tank.Normalized;
            Check("連射 10 秒才見底", midpoint > 0.35f && drained < 0.12f,
                $"5 秒時={midpoint:F3}(期望 >0.35) 10.5 秒時={drained:F3}(期望 <0.12)");

            // 案 2:放開後 0.5 秒內不回墨,過了延遲才開始回
            //(仍按住時已由案 1 的持續下降證明「開火不回墨」)
            intent.AttackHeld = false;
            float atRelease = tank.Normalized;
            yield return new WaitForSeconds(0.4f);
            float inDelay = tank.Normalized;
            yield return new WaitForSeconds(1.1f);
            float afterDelay = tank.Normalized;
            Check("放開 0.5 秒後才回墨",
                Mathf.Abs(inDelay - atRelease) < 0.005f && afterDelay - inDelay > 0.01f,
                $"放開瞬間={atRelease:F4} 0.4 秒後={inDelay:F4}(應幾乎不變) 1.5 秒後={afterDelay:F4}(應已上升)");

            // 案 3:泡在自家墨裡(烏賊)從 0 回滿約 5 秒
            ground.Paint(player.transform.position, 3f, new Color(1f, 0.5f, 0f, 1f), 0.7f);
            yield return null;
            intent.SquidHeld = true;
            yield return new WaitForSeconds(5f);
            intent.SquidHeld = false;
            float squidRefilled = tank.Normalized;
            Check("墨中 5 秒回滿", squidRefilled > 0.9f, $"normalized={squidRefilled:F3}");

            // 案 4:恢復射擊——以墨量下降證明實彈發射
            //(不依賴落彈區 delta:案 1 已塗滿 -Z 區,落舊墨區 delta 會假紅,2026-09-02 首輪教訓)。
            float costPerShot = player.GetComponent<Combat.InkShooter>().Config.InkCostPerShot;
            float beforeResume = tank.Normalized;
            intent.AttackHeld = true;
            yield return new WaitForSeconds(0.8f);
            intent.AttackHeld = false;
            float consumed = beforeResume - tank.Normalized;
            Check("恢復射擊", consumed > 0.02f,
                $"淨消耗={consumed:F3}(單發 {costPerShot:F5})");

            // 案 4:HUD 墨量條寬度與墨量同步
            yield return null;
            float expected = tank.Normalized * 240f;
            Check("HUD同步", Mathf.Abs(fill.sizeDelta.x - expected) < 12f,
                $"fill={fill.sizeDelta.x:F0} 期望≈{expected:F0}");

            router.ClearOverrideSource();
            Debug.Log($"[AUTOTEST] DONE passed={_passed} failed={_failed}");
            Destroy(gameObject);
        }

        private IEnumerator CountInk(PaintableSurface surface)
        {
            var request = AsyncGPUReadback.Request(surface.InkMap, 0, TextureFormat.RGBA32);
            while (!request.done)
            {
                yield return null;
            }
            if (request.hasError)
            {
                Debug.LogError("[AUTOTEST] AsyncGPUReadback 失敗");
                _lastCount = -1;
                yield break;
            }
            var data = request.GetData<Color32>();
            int count = 0;
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i].a > 32)
                {
                    count++;
                }
            }
            _lastCount = count;
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

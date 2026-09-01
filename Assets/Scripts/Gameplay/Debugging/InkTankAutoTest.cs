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

            // 設計目標(使用者指定 2026-09-02):連射可撐 10 秒、站立從 0 回滿約 5 秒。
            // 開火期間回墨照走,故淨消耗 = 射速 × 單發消耗 − 站立回墨率。
            // 案 1:連射 10 秒才見底,且 5 秒時仍過半(證明是 10 秒而不是 5 秒就空)
            intent.AttackHeld = true;
            yield return new WaitForSeconds(5f);
            float midpoint = tank.Normalized;
            yield return new WaitForSeconds(5.5f);
            float drained = tank.Normalized;
            intent.AttackHeld = false;
            Check("連射 10 秒才見底", midpoint > 0.35f && drained < 0.12f,
                $"5 秒時={midpoint:F3}(期望 >0.35) 10.5 秒時={drained:F3}(期望 <0.12)");

            // 案 2:站立(非烏賊)從 0 回滿約 5 秒
            yield return new WaitForSeconds(5f);
            float standingRefilled = tank.Normalized;
            Check("站立 5 秒回滿", standingRefilled > 0.9f, $"normalized={standingRefilled:F3}");

            // 案 3:烏賊在自家墨上回墨更快——同樣 1 秒,烏賊的增量要明顯高於站立速率
            intent.AttackHeld = true;
            yield return new WaitForSeconds(5f);
            intent.AttackHeld = false;
            float beforeSquid = tank.Normalized;
            ground.Paint(player.transform.position, 3f, new Color(1f, 0.5f, 0f, 1f), 0.7f);
            yield return null;
            intent.SquidHeld = true;
            yield return new WaitForSeconds(1f);
            intent.SquidHeld = false;
            float squidGain = tank.Normalized - beforeSquid;
            Check("烏賊回墨更快", squidGain > 0.3f,
                $"1 秒增量={squidGain:F2}(站立同時間只有約 0.20)");

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

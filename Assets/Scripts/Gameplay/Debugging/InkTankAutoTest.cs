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

            // 案 1:連射 5 秒耗盡墨(消耗 0.36/s vs 站立回墨 0.05/s → 約 3.2 秒見底)
            intent.AttackHeld = true;
            yield return new WaitForSeconds(5f);
            float drained = tank.Normalized;
            Check("連射耗盡", drained < 0.12f, $"normalized={drained:F3}");
            intent.AttackHeld = false;

            // 案 2:烏賊在自家墨上快速回墨(0.5/s → 2.5 秒回滿)
            ground.Paint(player.transform.position, 3f, new Color(1f, 0.5f, 0f, 1f), 0.7f);
            yield return null;
            intent.SquidHeld = true;
            yield return new WaitForSeconds(2.5f);
            intent.SquidHeld = false;
            float refilled = tank.Normalized;
            Check("烏賊回墨", refilled > 0.9f, $"normalized={refilled:F3}");

            // 案 3:回墨後恢復射擊——以墨量下降證明實彈發射
            //(不依賴落彈區 delta:案 1 已塗滿 -Z 區,落舊墨區 delta 會假紅,2026-09-02 首輪教訓)。
            float beforeResume = tank.Normalized;
            intent.AttackHeld = true;
            yield return new WaitForSeconds(0.8f);
            intent.AttackHeld = false;
            float consumed = beforeResume - tank.Normalized;
            Check("恢復射擊", consumed > 0.15f, $"消耗={consumed:F2}(約 {consumed / 0.045f:F0} 發)");

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

using System.Collections;
using SplatoonC.Gameplay.Painting;
using UnityEngine;
using UnityEngine.Rendering;

namespace SplatoonC.Gameplay.Debugging
{
    // M1 步驟 3 煙霧測試:塗色資料層(RT 真的被寫入)+ 瞄準真路徑 + 連塗效能取樣。
    // readback 一律 AsyncGPUReadback 輪詢(專案紅線:禁同步 readback)。
    public sealed class PaintAutoTest : MonoBehaviour
    {
        public static void Run()
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("[AUTOTEST] 需在 Play mode 中執行");
                return;
            }
            var host = new GameObject("PaintAutoTest");
            host.AddComponent<PaintAutoTest>();
        }

        private int _passed;
        private int _failed;
        private int _lastCount;

        private IEnumerator Start()
        {
            PaintableSurface ground = null;
            foreach (var surface in PaintableSurface.Active)
            {
                if (surface.name == "Ground")
                {
                    ground = surface;
                }
            }
            var debugTools = GameObject.Find("DebugTools");
            var painter = debugTools != null ? debugTools.GetComponent<InkPaintDebugger>() : null;
            if (ground == null || painter == null)
            {
                Debug.LogError("[AUTOTEST] FAIL 前置:找不到 Ground 的 PaintableSurface 或 InkPaintDebugger");
                Debug.Log("[AUTOTEST] DONE passed=0 failed=1");
                Destroy(gameObject);
                yield break;
            }

            yield return null;
            var inkColor = new Color(1f, 0.5f, 0f, 1f);

            // 案 1:初始乾淨——RT 建立後應近乎零墨
            yield return CountInk(ground);
            int baseline = _lastCount;
            Check("初始乾淨", baseline >= 0 && baseline < 50, $"count={baseline}");

            // 案 2:直呼 Paint——半徑 1m splat 在 50m/512px 面上約 330 texels
            ground.Paint(new Vector3(3f, 0f, 3f), 1f, inkColor, 0.6f);
            yield return CountInk(ground);
            int afterFirst = _lastCount;
            Check("塗色寫入", afterFirst - baseline > 120 && afterFirst - baseline < 900,
                $"delta={afterFirst - baseline}(期望約 330)");

            // 案 3:同點重塗——覆蓋而非疊加,增量應遠小於首筆
            ground.Paint(new Vector3(3f, 0f, 3f), 1f, inkColor, 0.6f);
            yield return CountInk(ground);
            int afterRepeat = _lastCount;
            Check("覆蓋不疊加", afterRepeat - afterFirst < (afterFirst - baseline) / 2,
                $"再塗增量={afterRepeat - afterFirst}");

            // 案 4:真路徑——相機中心射線瞄準塗色(半徑 0.75m 約 185 texels)
            bool aimHit = painter.PaintAtAim();
            yield return CountInk(ground);
            int afterAim = _lastCount;
            Check("瞄準塗色", aimHit && afterAim - afterRepeat > 80,
                $"hit={aimHit} delta={afterAim - afterRepeat}");

            // 案 5:連續塗色 3 秒(效能哨兵取樣用,不斷言)
            float endTime = Time.time + 3f;
            var interval = new WaitForSeconds(0.05f);
            int strokes = 0;
            while (Time.time < endTime)
            {
                ground.Paint(
                    new Vector3(-8f + (strokes % 20) * 0.8f, 0f, -8f + (strokes / 20) * 0.8f),
                    0.75f, inkColor, 0.6f);
                strokes++;
                yield return interval;
            }
            Debug.Log($"[AUTOTEST] INFO 連續塗色 {strokes} 筆完成(效能取樣用)");

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

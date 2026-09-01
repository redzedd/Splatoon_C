using System.Collections;
using SplatoonC.Gameplay.CameraRig;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;
using UnityEngine.Rendering;

namespace SplatoonC.Gameplay.Debugging
{
    // M2 步驟 3 煙霧測試:垂直牆塗色——RT 寫入、per-surface 歸屬查詢、真路徑射擊塗牆。
    public sealed class WallPaintAutoTest : MonoBehaviour
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
            var host = new GameObject("WallPaintAutoTest");
            host.AddComponent<WallPaintAutoTest>();
        }

        private int _passed;
        private int _failed;
        private int _lastCount;

        private IEnumerator Start()
        {
            PaintableSurface wall = null;
            foreach (var surface in PaintableSurface.Active)
            {
                if (surface.name == "ClimbWall_High")
                {
                    wall = surface;
                }
            }
            GameObject player = GameObject.Find("Player");
            PlayerInputRouter router = player != null ? player.GetComponent<PlayerInputRouter>() : null;
            ThirdPersonCameraRig rig = Camera.main != null
                ? Camera.main.GetComponent<ThirdPersonCameraRig>() : null;
            if (wall == null || router == null || rig == null)
            {
                Debug.LogError("[AUTOTEST] FAIL 前置:找不到 ClimbWall_High 的 PaintableSurface / Router / Rig");
                Debug.Log("[AUTOTEST] DONE passed=0 failed=1");
                Destroy(gameObject);
                yield break;
            }

            var intent = new TestIntentSource();
            router.SetOverrideSource(intent);
            yield return null;

            // 案 1:牆面初始乾淨
            yield return CountInk(wall);
            int baseline = _lastCount;
            Check("牆初始乾淨", baseline >= 0 && baseline < 50, $"count={baseline}");

            // 案 2:直呼 Paint——RT 寫入 + 垂直面歸屬網格
            var paintPoint = new Vector3(12f, 2f, 0f);
            wall.Paint(paintPoint, 0.5f, new Color(1f, 0.5f, 0f, 1f), 0.7f);
            yield return CountInk(wall);
            int afterDirect = _lastCount;
            Check("牆塗色寫入", afterDirect - baseline > 800 && afterDirect - baseline < 4000,
                $"delta={afterDirect - baseline}(6×4m/256px,r=0.5 期望約 2100)");
            Check("牆面歸屬查詢", wall.SampleOwnership(paintPoint) == 1
                && wall.SampleOwnership(new Vector3(12f, 3.8f, 2.8f)) == 0,
                $"塗點={wall.SampleOwnership(paintPoint)} 遠角={wall.SampleOwnership(new Vector3(12f, 3.8f, 2.8f))}");

            // 案 3:真路徑——面向牆射擊(平視,命中牆中段)。
            // 先傳送到 (5, 0.1, 2.5):越過出生點旁的舊遮擋牆(x=1.25,不可塗,z±1.5),
            // 否則瞄準線先撞舊牆、彈全數陣亡(2026-09-02 首輪紅的原因)。
            var controller = player.GetComponent<CharacterController>();
            controller.enabled = false;
            player.transform.position = new Vector3(5f, 0.1f, 2.5f);
            controller.enabled = true;
            rig.SetAngles(90f, 0f);
            yield return null;
            yield return null;
            intent.AttackHeld = true;
            yield return new WaitForSeconds(0.6f);
            intent.AttackHeld = false;
            yield return new WaitForSeconds(1.2f);
            yield return CountInk(wall);
            int afterShots = _lastCount;
            Check("射擊塗牆", afterShots - afterDirect > 500,
                $"delta={afterShots - afterDirect}(約 5 發主點+噴濺)");

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

using System.Collections;
using SplatoonC.Gameplay.CameraRig;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using SplatoonC.Gameplay.Scoring;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace SplatoonC.Gameplay.Debugging
{
    // M1 步驟 5 煙霧測試:烏賊態(變速/下沉/禁射)+ 佔地計分 HUD,走 intent 真路徑。
    public sealed class SquidCoverageAutoTest : MonoBehaviour
    {
        // 假紅驗證(sabotage)時設 true,略過 60 秒效能階段。
        public static bool SkipPerfPhase;

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
            var host = new GameObject("SquidCoverageAutoTest");
            host.AddComponent<SquidCoverageAutoTest>();
        }

        private int _passed;
        private int _failed;
        private int _lastCount;

        private IEnumerator Start()
        {
            GameObject player = GameObject.Find("Player");
            PlayerInputRouter router = player != null ? player.GetComponent<PlayerInputRouter>() : null;
            SquidController squid = player != null ? player.GetComponent<SquidController>() : null;
            Transform visual = player != null ? player.transform.Find("Visual") : null;
            PaintableSurface ground = null;
            foreach (var surface in PaintableSurface.Active)
            {
                if (surface.name == "Ground")
                {
                    ground = surface;
                }
            }
            var systems = GameObject.Find("GameSystems");
            CoverageScorer scorer = systems != null ? systems.GetComponent<CoverageScorer>() : null;
            var hudGo = GameObject.Find("CoverageText");
            Text hudText = hudGo != null ? hudGo.GetComponent<Text>() : null;
            ThirdPersonCameraRig rig = Camera.main != null
                ? Camera.main.GetComponent<ThirdPersonCameraRig>() : null;

            if (router == null || squid == null || visual == null || ground == null
                || scorer == null || hudText == null || rig == null)
            {
                Debug.LogError("[AUTOTEST] FAIL 前置:Player/Squid/Visual/Ground/Scorer/HUD/Rig 缺一");
                Debug.Log("[AUTOTEST] DONE passed=0 failed=1");
                Destroy(gameObject);
                yield break;
            }

            var intent = new TestIntentSource();
            router.SetOverrideSource(intent);
            // 朝 -Z 空曠區:+Z 有不可塗矮階,「彈打矮階不塗色」會讓禁射案假綠(2026-09-01 假紅驗證抓到)。
            rig.SetAngles(180f, 10f);
            yield return null;
            yield return null;

            // 案 1:烏賊態視覺下沉與回彈
            intent.SquidHeld = true;
            yield return new WaitForSeconds(0.5f);
            float squashedY = visual.localScale.y;
            intent.SquidHeld = false;
            yield return new WaitForSeconds(0.5f);
            float restoredY = visual.localScale.y;
            Check("烏賊下沉回彈", squashedY < 0.5f && restoredY > 0.9f,
                $"壓扁={squashedY:F2} 回彈={restoredY:F2}");

            // 案 2:烏賊態不可射擊
            yield return CountInk(ground);
            int beforeSquidFire = _lastCount;
            intent.SquidHeld = true;
            intent.AttackHeld = true;
            yield return new WaitForSeconds(0.5f);
            // 放開前先數活彈:禁射生效 = 按住期間完全沒出彈(雙保險,不只看塗色)。
            var projectiles = FindObjectsByType<SplatoonC.Gameplay.Combat.InkProjectile>();
            int liveShots = 0;
            foreach (var p in projectiles)
            {
                if (p.gameObject.activeInHierarchy)
                {
                    liveShots++;
                }
            }
            intent.AttackHeld = false;
            intent.SquidHeld = false;
            yield return new WaitForSeconds(1f);
            yield return CountInk(ground);
            Check("烏賊不可射擊", liveShots == 0 && _lastCount - beforeSquidFire < 30,
                $"活彈={liveShots} delta={_lastCount - beforeSquidFire}");

            // 鋪一條 -Z 墨帶(Paint 會同步標記表面自身的歸屬網格)
            for (int i = 0; i <= 14; i++)
            {
                ground.Paint(new Vector3(0f, 0f, -i), 2f, new Color(1f, 0.5f, 0f, 1f), 0.7f);
            }
            yield return null;

            // 案 3:烏賊態在自家墨上加速(期望 9 × 1.56 ≈ 14.0);yaw=180 下 move(0,1) = 世界 -Z 沿墨帶
            intent.SquidHeld = true;
            yield return new WaitForSeconds(0.2f);
            Vector3 before = player.transform.position;
            intent.MoveInput = new Vector2(0f, 1f);
            yield return new WaitForSeconds(1f);
            intent.MoveInput = Vector2.zero;
            Vector3 moved = player.transform.position - before;
            moved.y = 0f;
            Check("墨上加速", moved.magnitude > 11.5f && moved.magnitude < 16f,
                $"|d|={moved.magnitude:F2}(期望約 14.0)");

            // 案 4:烏賊態在乾地減速(期望 9 × 0.7 ≈ 6.3;離墨後倍率有 0.36 秒緩降,量測窗要夠長)
            // 先橫移 0.6 秒脫離墨帶(半寬 2m)再量測,避免混速;yaw=180 下 move(1,0) = 世界 -X 空地
            intent.MoveInput = new Vector2(1f, 0f);
            yield return new WaitForSeconds(0.6f);
            before = player.transform.position;
            yield return new WaitForSeconds(1f);
            intent.MoveInput = Vector2.zero;
            intent.SquidHeld = false;
            moved = player.transform.position - before;
            moved.y = 0f;
            Check("乾地減速", moved.magnitude > 5f && moved.magnitude < 8.5f,
                $"|d|={moved.magnitude:F2}(期望約 6.3)");

            // 案 5:佔地率與 HUD(墨帶約佔 50×50 場地的 2~10%)
            yield return new WaitForSeconds(1f);
            float ratio = scorer.Latest.PaintedRatio;
            bool hudOk = hudText.text.Contains("%");
            Check("佔地率計分", ratio > 0.005f && ratio < 0.15f && hudOk,
                $"ratio={ratio:P1} hud=\"{hudText.text}\"");

            // 案 6:60 秒連射塗地(M1 驗收效能取樣;假紅驗證時略過)
            if (!SkipPerfPhase)
            {
                Debug.Log("[AUTOTEST] INFO 60 秒效能階段開始");
                intent.AttackHeld = true;
                intent.LookDeltaDeg = new Vector2(0.4f, 0f);
                float endTime = Time.time + 60f;
                while (Time.time < endTime)
                {
                    yield return null;
                }
                intent.AttackHeld = false;
                intent.LookDeltaDeg = Vector2.zero;
                Debug.Log("[AUTOTEST] INFO 60 秒效能階段結束");
            }

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

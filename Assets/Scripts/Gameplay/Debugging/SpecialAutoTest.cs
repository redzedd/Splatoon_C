using System.Collections;
using SplatoonC.Gameplay.CameraRig;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using SplatoonC.Gameplay.Scoring;
using UnityEngine;
using UnityEngine.UI;

namespace SplatoonC.Gameplay.Debugging
{
    // M4 步驟 4 驗收(A 段):能量開場 0 / 塗地充能 / 重塗不充能 / 未滿按 R 無效 / 滿格按 R 啟動並衰減 /
    // HUD fill 與 READY 文字 / 不丟裝置衰減到零結束。需新 play session(能量從 0 開始);站位在 x ≥ 17 靶場帶。
    public sealed class SpecialAutoTest : MonoBehaviour
    {
        private sealed class TestIntentSource : IPlayerIntentSource
        {
            public Vector2 MoveInput => Vector2.zero;
            public Vector2 LookDeltaDeg => Vector2.zero;
            public bool JumpPressedThisFrame => false;
            public bool AttackHeld { get; set; }
            public bool SquidHeld => false;
            public bool SubWeaponHeld { get; set; }
            public bool SpecialPressedThisFrame { get; set; }
        }

        public static void Run()
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("[AUTOTEST] 需在 Play mode 中執行");
                return;
            }
            var host = new GameObject("SpecialAutoTest");
            host.AddComponent<SpecialAutoTest>();
        }

        private int _passed;
        private int _failed;

        private IEnumerator Start()
        {
            GameObject player = GameObject.Find("Player");
            var router = player != null ? player.GetComponent<PlayerInputRouter>() : null;
            var controller = player != null ? player.GetComponent<CharacterController>() : null;
            var tank = player != null ? player.GetComponent<PlayerInkTank>() : null;
            var gauge = player != null ? player.GetComponent<PlayerSpecialGauge>() : null;
            var rig = Camera.main != null ? Camera.main.GetComponent<ThirdPersonCameraRig>() : null;
            GameObject fillGo = GameObject.Find("SpecialBarFill");
            GameObject readyGo = GameObject.Find("SpecialReadyText");
            var fill = fillGo != null ? fillGo.GetComponent<RectTransform>() : null;
            var readyText = readyGo != null ? readyGo.GetComponent<Text>() : null;
            if (router == null || controller == null || tank == null || gauge == null || rig == null
                || fill == null || readyText == null || gauge.Config == null)
            {
                Debug.LogError("[AUTOTEST] FAIL 前置:找不到 Player/PlayerSpecialGauge/CameraRig/SpecialBar HUD(先跑 Tools/SplatoonC/M4 Setup)");
                Debug.Log("[AUTOTEST] DONE passed=0 failed=1");
                Destroy(gameObject);
                yield break;
            }
            TripleInkstrikeConfig config = gauge.Config;
            var intent = new TestIntentSource();
            router.SetOverrideSource(intent);
            Vector3 spawnPos = player.transform.position;
            tank.InfiniteInk = true;

            // 案 1:開場能量 0
            Check("開場能量為 0", gauge.Points == 0f && !gauge.IsFull && !gauge.IsActive,
                $"能量={gauge.Points:0.0}");

            // 站位:x=18 面向 +Z 的未塗空地(假人在 x=21 / z=-10,側向 3m 不在射線上)
            Teleport(player, controller, new Vector3(18f, 0.1f, -22f));
            rig.SetAngles(0f, 8f);
            yield return new WaitForSeconds(0.35f);

            // 案 2:連射 1 秒 → 能量上升(印實值供調 ChargePerSquareMetre)
            float before = gauge.Points;
            intent.AttackHeld = true;
            yield return new WaitForSeconds(1f);
            intent.AttackHeld = false;
            yield return new WaitForSeconds(1.5f);
            float firstGain = gauge.Points - before;
            Check("塗地充能:連射 1 秒能量上升", firstGain > 5f,
                $"能量 {before:0.0} → {gauge.Points:0.0}(+{firstGain:0.0} 點;{config.ChargePerSquareMetre} 點/m²)");

            // 案 3:同一處再射 0.5 秒 → 增量遠小於第一次(重塗自家墨不充能)
            before = gauge.Points;
            intent.AttackHeld = true;
            yield return new WaitForSeconds(0.5f);
            intent.AttackHeld = false;
            yield return new WaitForSeconds(1.5f);
            float secondGain = gauge.Points - before;
            Check("重塗自家墨幾乎不充能", secondGain < firstGain * 0.2f,
                $"第二次 +{secondGain:0.0} 點(第一次 +{firstGain:0.0},期望 < 20%)");

            // 案 4:未滿按 R 不啟動
            bool wasFull = gauge.IsFull;
            intent.SpecialPressedThisFrame = true;
            yield return null;
            intent.SpecialPressedThisFrame = false;
            yield return null;
            Check("未滿按 R 不啟動", !wasFull && !gauge.IsActive && gauge.ActivationCount == 0,
                $"滿格={wasFull} 啟動={gauge.IsActive}");

            // 案 5:設滿 → HUD READY 可見;按 R → 啟動、HUD 顯示剩餘 3、1 秒後約衰減 100/6
            gauge.SetPointsForTest(100f);
            yield return null;
            yield return null;
            bool readyShown = readyText.enabled && readyText.text.Contains("READY");
            float fillWidthFull = fill.sizeDelta.x;
            intent.SpecialPressedThisFrame = true;
            yield return null;
            intent.SpecialPressedThisFrame = false;
            bool activated = gauge.IsActive;
            float pointsAtStart = gauge.Points;
            yield return new WaitForSeconds(1f);
            float drained = pointsAtStart - gauge.Points;
            float expectedDrain = 100f / config.ActiveDuration;
            bool activeShown = readyText.enabled && readyText.text.Contains($"剩餘 {config.MaxDevices}");
            Check("滿格按 R 啟動並每秒衰減 100/持續秒數",
                activated && gauge.ActivationCount == 1 && Mathf.Abs(drained - expectedDrain) < 2.5f,
                $"啟動={activated} 1 秒衰減 {drained:0.0}(期望 {expectedDrain:0.0})");
            Check("HUD:滿格顯示 READY、fill 滿寬;啟動中顯示剩餘裝置數",
                readyShown && Mathf.Abs(fillWidthFull - 240f) < 3f && activeShown,
                $"READY={readyShown} 滿格寬={fillWidthFull:0} 啟動文字={activeShown}「{readyText.text}」");

            // 案 6:HUD fill 寬跟隨能量
            yield return null;
            float expectedWidth = 240f * gauge.Normalized;
            Check("HUD fill 寬 = 240 × 能量比例", Mathf.Abs(fill.sizeDelta.x - expectedWidth) < 3f,
                $"寬={fill.sizeDelta.x:0.0} 期望={expectedWidth:0.0}");

            // 案 7:不丟裝置,等到衰減歸零 → 結束、HUD 文字隱藏
            float deadline = Time.time + config.ActiveDuration + 1f;
            while (gauge.IsActive && Time.time < deadline)
            {
                yield return null;
            }
            yield return null;
            Check("能量衰減歸零即結束(不管有沒有丟裝置)",
                !gauge.IsActive && gauge.Points == 0f && !readyText.enabled,
                $"啟動={gauge.IsActive} 能量={gauge.Points:0.0} 文字顯示={readyText.enabled}");

            Teleport(player, controller, spawnPos);
            rig.SetAngles(0f, 12f);
            router.ClearOverrideSource();
            tank.InfiniteInk = false;
            Debug.Log($"[AUTOTEST] DONE passed={_passed} failed={_failed}");
            Destroy(gameObject);
        }

        private static void Teleport(GameObject player, CharacterController controller, Vector3 position)
        {
            controller.enabled = false;
            player.transform.position = position;
            controller.enabled = true;
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

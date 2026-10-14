using System.Collections;
using SplatoonC.Gameplay.CameraRig;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Player;
using Unity.Profiling;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 效能哨兵探針(不斷言):分四段量每幀 managed 配置——閒置 / 步槍連射(空地)/ 大招龍捲風(無假人)/ 連射打假人(頭頂數字)。
    // 主要用 ProfilerRecorder「GC Allocated In Frame」,不可用時退回 GC.GetTotalMemory 差分(負值 = 發生 GC,略過)。
    // 站位 (18,0.1,-22) 朝 +Z:x=18 射道沒有假人;打假人段用 Dummy_Near(搬到 6m 外)。
    public sealed class GcProbe : MonoBehaviour
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
                Debug.LogError("[PROBE] 需在 Play mode 中執行");
                return;
            }
            var host = new GameObject("GcProbe");
            host.AddComponent<GcProbe>();
        }

        private ProfilerRecorder _recorder;

        private IEnumerator Start()
        {
            GameObject player = GameObject.Find("Player");
            var router = player.GetComponent<PlayerInputRouter>();
            var controller = player.GetComponent<CharacterController>();
            var tank = player.GetComponent<PlayerInkTank>();
            var gauge = player.GetComponent<PlayerSpecialGauge>();
            var rig = Camera.main.GetComponent<ThirdPersonCameraRig>();
            GameObject nearGo = GameObject.Find("Dummy_Near");
            var intent = new TestIntentSource();
            router.SetOverrideSource(intent);
            tank.InfiniteInk = true;
            Vector3 spawnPos = player.transform.position;
            Vector3 nearPos = nearGo != null ? nearGo.transform.position : Vector3.zero;

            _recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            Debug.Log($"[PROBE] GC 量測開始:ProfilerRecorder 可用={_recorder.Valid}");

            controller.enabled = false;
            player.transform.position = new Vector3(18f, 0.1f, -22f);
            controller.enabled = true;
            rig.SetAngles(0f, 8f);
            yield return new WaitForSeconds(0.5f);

            yield return Measure("閒置", 1.5f);

            intent.AttackHeld = true;
            yield return new WaitForSeconds(0.5f);
            yield return Measure("步槍連射(空地)", 3f);
            intent.AttackHeld = false;

            // 大招:丟一顆裝置到前方地面(無假人),量龍捲風存活期間
            gauge.SetPointsForTest(100f);
            yield return null;
            intent.SpecialPressedThisFrame = true;
            yield return null;
            intent.SpecialPressedThisFrame = false;
            rig.SetAngles(0f, 30f);
            intent.SubWeaponHeld = true;
            yield return new WaitForSeconds(0.35f);
            intent.SubWeaponHeld = false;
            float deadline = Time.time + 2f;
            while (Tornado.ActiveCount == 0 && Time.time < deadline)
            {
                yield return null;
            }
            yield return Measure("龍捲風(無假人)", 1.0f);

            // 打假人:Dummy_Near 搬到 6m 外,連射 2 秒(含頭頂數字字串)
            if (nearGo != null)
            {
                nearGo.transform.position = new Vector3(18f, 0f, -16f);
                rig.SetAngles(0f, 4f);
                yield return new WaitForSeconds(0.5f);
                intent.AttackHeld = true;
                yield return new WaitForSeconds(0.3f);
                yield return Measure("連射打假人(頭頂數字)", 2f);
                intent.AttackHeld = false;
                nearGo.transform.position = nearPos;
            }

            _recorder.Dispose();
            controller.enabled = false;
            player.transform.position = spawnPos;
            controller.enabled = true;
            rig.SetAngles(0f, 12f);
            router.ClearOverrideSource();
            tank.InfiniteInk = false;
            Debug.Log("[PROBE] DONE GC 量測");
            Destroy(gameObject);
        }

        private IEnumerator Measure(string label, float seconds)
        {
            long total = 0;
            long max = 0;
            int frames = 0;
            int allocFrames = 0;
            long lastHeap = System.GC.GetTotalMemory(false);
            float end = Time.time + seconds;
            while (Time.time < end)
            {
                yield return null;
                long bytes;
                if (_recorder.Valid)
                {
                    bytes = _recorder.LastValue;
                }
                else
                {
                    long heap = System.GC.GetTotalMemory(false);
                    bytes = heap - lastHeap;
                    lastHeap = heap;
                    if (bytes < 0)
                    {
                        continue;
                    }
                }
                frames++;
                total += bytes;
                if (bytes > 0)
                {
                    allocFrames++;
                }
                if (bytes > max)
                {
                    max = bytes;
                }
            }
            float avg = frames > 0 ? (float)total / frames : 0f;
            Debug.Log($"[PROBE] {label}:{frames} 幀,平均 {avg:0.0} B/幀,最大 {max} B,有配置的幀 {allocFrames}({(frames > 0 ? 100f * allocFrames / frames : 0f):0.0}%),合計 {total} B");
        }
    }
}

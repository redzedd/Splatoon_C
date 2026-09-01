using System;
using System.Collections;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // standalone FPS 哨兵(M1 60fps 保留項的驗收工具):
    // 帶 -autotest 參數啟動時,暖機後以 scripted intent 連射+旋轉 60 秒,統計幀時間寫進 player log。
    public sealed class AutoPerfRun : MonoBehaviour
    {
        private sealed class SpinFireIntent : IPlayerIntentSource
        {
            public Vector2 MoveInput => Vector2.zero;
            public Vector2 LookDeltaDeg => new Vector2(0.4f, 0f);
            public bool JumpPressedThisFrame => false;
            public bool AttackHeld => true;
            public bool SquidHeld => false;
        }

        [SerializeField, Tooltip("量測秒數")]
        private float _duration = 60f;

        [SerializeField, Tooltip("暖機秒數(不計入統計)")]
        private float _warmup = 3f;

        [SerializeField, Tooltip("達標 FPS(寫進結論行)")]
        private float _targetFps = 60f;

        [SerializeField, Tooltip("量測結束後自動離開(standalone 自動化用;編輯器內不生效)")]
        private bool _quitWhenDone = true;

        private void Start()
        {
            foreach (string arg in Environment.GetCommandLineArgs())
            {
                if (arg == "-autotest")
                {
                    StartCoroutine(RunRoutine());
                    return;
                }
            }
        }

        // 編輯器手動觸發用。
        public void RunNow()
        {
            StartCoroutine(RunRoutine());
        }

        private IEnumerator RunRoutine()
        {
            var player = GameObject.Find("Player");
            var router = player != null ? player.GetComponent<PlayerInputRouter>() : null;
            if (router == null)
            {
                Debug.LogError("[PERFRUN] 找不到 Player/PlayerInputRouter,中止");
                yield break;
            }

            yield return new WaitForSeconds(_warmup);
            router.SetOverrideSource(new SpinFireIntent());
            Debug.Log($"[PERFRUN] 開始:{_duration:F0} 秒連射+旋轉");

            int capacity = Mathf.CeilToInt(_duration * 1200f);
            var samples = new float[capacity];
            int count = 0;
            float endTime = Time.time + _duration;
            while (Time.time < endTime)
            {
                yield return null;
                if (count < capacity)
                {
                    samples[count++] = Time.unscaledDeltaTime;
                }
            }
            router.ClearOverrideSource();

            float total = 0f;
            for (int i = 0; i < count; i++)
            {
                total += samples[i];
            }
            float averageMs = total / count * 1000f;
            Array.Sort(samples, 0, count);
            float p95Ms = samples[Mathf.Min(count - 1, Mathf.FloorToInt(count * 0.95f))] * 1000f;
            float averageFps = count / total;
            string verdict = averageFps >= _targetFps ? "PASS" : "FAIL";
            Debug.Log($"[PERFRUN] frames={count} avgMs={averageMs:F2} p95Ms={p95Ms:F2} " +
                $"avgFps={averageFps:F1} target={_targetFps:F0} result={verdict}");

            // 塗色系統活性證據:60 秒連射後佔地率必須明顯 > 0(shader 沒打包時這裡會抓到)。
            var scorer = FindAnyObjectByType<Scoring.CoverageScorer>();
            if (scorer != null)
            {
                Debug.Log($"[PERFRUN] coverage={scorer.Latest.PaintedRatio:P1} scorerEnabled={scorer.enabled}");
            }

            if (_quitWhenDone && !Application.isEditor)
            {
                Application.Quit();
            }
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using System.Text;
using SplatoonC.Gameplay.CameraRig;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 量墨滴的「落點」分布,不是釋放點。
    // 滴墨是照飛行距離排程的,但釋放後還會邊掉邊往前飛;
    // 前飛距離取決於釋放時的高度與速度,所以排程均勻不代表落點均勻——
    // 落點分布如果有洞,連續射擊也補不起來(每發都在同一個地方漏)。
    public sealed class DripLandingProbe : MonoBehaviour
    {
        private sealed class FireIntent : IPlayerIntentSource
        {
            public Vector2 MoveInput => Vector2.zero;
            public Vector2 LookDeltaDeg => Vector2.zero;
            public bool JumpPressedThisFrame => false;
            public bool AttackHeld { get; set; }
            public bool SquidHeld => false;
        }

        public static void Run()
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("[PROBE] 需在 Play mode 中執行");
                return;
            }
            var host = new GameObject("DripLandingProbe");
            host.AddComponent<DripLandingProbe>();
        }

        private IEnumerator Start()
        {
            var player = GameObject.Find("Player");
            var router = player.GetComponent<PlayerInputRouter>();
            var controller = player.GetComponent<CharacterController>();
            var rig = Camera.main.GetComponent<ThirdPersonCameraRig>();
            player.GetComponent<PlayerInkTank>().InfiniteInk = true;

            var intent = new FireIntent();
            router.SetOverrideSource(intent);

            var dripRoot = GameObject.Find("InkDripPool");
            var projRoot = GameObject.Find("InkProjectilePool");
            float[] pitches = { 0f, 10f, 20f, 30f };

            for (int index = 0; index < pitches.Length; index++)
            {
                float pitch = pitches[index];
                controller.enabled = false;
                player.transform.position = new Vector3(-18f + index * 9f, 0.1f, 18f);
                controller.enabled = true;
                rig.SetAngles(180f, pitch);
                for (int i = 0; i < 30; i++)
                {
                    yield return null;
                }

                Vector3 origin = player.transform.position;
                var dripLast = new Dictionary<Transform, Vector3>();
                var dripLandings = new List<float>();
                var projLandings = new List<float>();
                var projLast = new Dictionary<Transform, Vector3>();

                intent.AttackHeld = true;
                float elapsed = 0f;
                while (elapsed < 0.5f)
                {
                    Track(dripRoot, dripLast, dripLandings, origin);
                    Track(projRoot, projLast, projLandings, origin);
                    elapsed += Time.deltaTime;
                    yield return null;
                }
                intent.AttackHeld = false;
                float tail = 0f;
                while (tail < 2f)
                {
                    Track(dripRoot, dripLast, dripLandings, origin);
                    Track(projRoot, projLast, projLandings, origin);
                    tail += Time.deltaTime;
                    yield return null;
                }
                Flush(dripLast, dripLandings, origin);
                Flush(projLast, projLandings, origin);

                Debug.Log($"[PROBE] pitch={pitch:F0}° 墨滴落點直方圖(每 1m 一格,0~20m):{Histogram(dripLandings)}");
                Debug.Log($"[PROBE] pitch={pitch:F0}° 墨彈落點直方圖:{Histogram(projLandings)}");
                yield return new WaitForSeconds(0.3f);
            }

            router.ClearOverrideSource();
            Debug.Log("[PROBE] DONE 落點分布量測");
            Destroy(gameObject);
        }

        // 物件還活著時持續記錄位置;消失(回收)的那一刻,最後位置就是落點。
        private static void Track(GameObject root, Dictionary<Transform, Vector3> last,
            List<float> landings, Vector3 origin)
        {
            var gone = new List<Transform>();
            foreach (var pair in last)
            {
                if (pair.Key == null || !pair.Key.gameObject.activeInHierarchy)
                {
                    gone.Add(pair.Key);
                }
            }
            foreach (var t in gone)
            {
                landings.Add(Distance(last[t], origin));
                last.Remove(t);
            }
            foreach (Transform c in root.transform)
            {
                if (c.gameObject.activeInHierarchy)
                {
                    last[c] = c.position;
                }
            }
        }

        private static void Flush(Dictionary<Transform, Vector3> last, List<float> landings, Vector3 origin)
        {
            foreach (var pair in last)
            {
                landings.Add(Distance(pair.Value, origin));
            }
            last.Clear();
        }

        private static float Distance(Vector3 point, Vector3 origin)
        {
            return new Vector2(point.x - origin.x, point.z - origin.z).magnitude;
        }

        private static string Histogram(List<float> values)
        {
            var bins = new int[21];
            foreach (float v in values)
            {
                int b = Mathf.Clamp(Mathf.FloorToInt(v), 0, 20);
                bins[b]++;
            }
            var sb = new StringBuilder();
            for (int i = 0; i < bins.Length; i++)
            {
                sb.Append(bins[i] == 0 ? "." : bins[i].ToString("X"));
            }
            sb.Append($"  (共 {values.Count} 個,'.'=該公尺區間無落點)");
            return sb.ToString();
        }
    }
}

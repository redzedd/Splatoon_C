using System.Collections;
using System.Collections.Generic;
using SplatoonC.Gameplay.CameraRig;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 隔離驗證「沿路滴墨」:單發射擊,追蹤 InkDripPool 內活著的墨滴,
    // 記錄它們的生成點與落地點,確認墨滴確實沿彈道分佈(而非全部堆在槍口或落點)。
    public sealed class DripProbe : MonoBehaviour
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
            var host = new GameObject("DripProbe");
            host.AddComponent<DripProbe>();
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
            controller.enabled = false;
            player.transform.position = new Vector3(-16f, 0.1f, 18f);
            controller.enabled = true;
            rig.SetAngles(180f, 12f);
            for (int i = 0; i < 30; i++)
            {
                yield return null;
            }

            var dripRoot = GameObject.Find("InkDripPool");
            if (dripRoot == null)
            {
                Debug.LogError("[PROBE] 找不到 InkDripPool——墨滴 prefab 未接上?");
                Destroy(gameObject);
                yield break;
            }

            Vector3 muzzle = player.transform.position;
            var firstSeen = new Dictionary<Transform, Vector3>();
            var lastSeen = new Dictionary<Transform, Vector3>();
            int peakActive = 0;

            intent.AttackHeld = true;
            yield return null;
            yield return null;
            intent.AttackHeld = false;

            float watchdog = 0f;
            while (watchdog < 4f)
            {
                int active = 0;
                foreach (Transform d in dripRoot.transform)
                {
                    if (!d.gameObject.activeInHierarchy)
                    {
                        continue;
                    }
                    active++;
                    if (!firstSeen.ContainsKey(d))
                    {
                        firstSeen[d] = d.position;
                    }
                    lastSeen[d] = d.position;
                }
                if (active > peakActive)
                {
                    peakActive = active;
                }
                watchdog += Time.deltaTime;
                yield return null;
            }

            var spawnDistances = new List<float>();
            foreach (var pair in firstSeen)
            {
                Vector3 p = pair.Value;
                spawnDistances.Add(new Vector2(p.x - muzzle.x, p.z - muzzle.z).magnitude);
            }
            spawnDistances.Sort();

            string list = string.Join(" / ", spawnDistances.ConvertAll(d => d.ToString("F1")));
            bool ok = firstSeen.Count >= 1 && firstSeen.Count <= 3;
            Debug.Log($"[PROBE] 單發滴墨數={firstSeen.Count}(期望 1~3,同時最多 {peakActive} 顆在空中)" +
                $" 生成點距槍口={list}m {(ok ? "OK" : "異常")}");

            router.ClearOverrideSource();
            Debug.Log("[PROBE] DONE 滴墨隔離驗證");
            Destroy(gameObject);
        }
    }
}

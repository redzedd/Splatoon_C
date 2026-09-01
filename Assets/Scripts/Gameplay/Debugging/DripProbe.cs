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

            // 滴墨已改成機率制(約每 4 發滴一次),單發量測沒有統計意義:射 20 發看總量與分佈。
            const int shots = 20;
            float range = player.GetComponent<InkShooter>().Config.StraightRange;
            Vector3 muzzle = player.transform.position;
            var spawnDistances = new List<float>();
            var seen = new HashSet<Transform>();

            for (int s = 0; s < shots; s++)
            {
                intent.AttackHeld = true;
                yield return null;
                yield return null;
                intent.AttackHeld = false;

                float watchdog = 0f;
                while (watchdog < 0.9f)
                {
                    foreach (Transform d in dripRoot.transform)
                    {
                        if (!d.gameObject.activeInHierarchy || seen.Contains(d))
                        {
                            continue;
                        }
                        seen.Add(d);
                        Vector3 p = d.position;
                        spawnDistances.Add(new Vector2(p.x - muzzle.x, p.z - muzzle.z).magnitude);
                    }
                    watchdog += Time.deltaTime;
                    yield return null;
                }
                seen.Clear();
            }

            spawnDistances.Sort();
            int nearHalf = 0;
            foreach (float d in spawnDistances)
            {
                if (d <= range * 0.5f)
                {
                    nearHalf++;
                }
            }
            float perShot = spawnDistances.Count / (float)shots;
            float median = spawnDistances.Count > 0 ? spawnDistances[spawnDistances.Count / 2] : -1f;
            // 期望:每發平均 0.2~0.8 滴(3~5 發滴 1~2 滴),且多數落在射程近半(腳邊也塗得到)
            bool ok = perShot > 0.2f && perShot < 0.8f && nearHalf * 2 >= spawnDistances.Count;
            Debug.Log($"[PROBE] {shots} 發共滴 {spawnDistances.Count} 滴(每發 {perShot:F2},期望 0.2~0.8)" +
                $" 中位距離={median:F1}m 落在近半({range * 0.5f:F1}m 內)={nearHalf}/{spawnDistances.Count}" +
                $" {(ok ? "OK" : "異常")}");

            router.ClearOverrideSource();
            Debug.Log("[PROBE] DONE 滴墨隔離驗證");
            Destroy(gameObject);
        }
    }
}

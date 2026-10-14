using System;
using System.Collections;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 新 AutoTest 的共用底:Check/Finish 標記格式、瞬移、清空墨槽、等待條件。
    // 子類:標 [AutoTest(...)]、寫 public static void Run() => Launch<子類>(); 在 Start 協程裡測,最後呼叫 Finish()。
    public abstract class AutoTestBase : MonoBehaviour
    {
        protected int Passed { get; private set; }
        protected int Failed { get; private set; }

        protected static T Launch<T>() where T : AutoTestBase
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("[AUTOTEST] 需在 Play mode 中執行");
                return null;
            }
            return new GameObject(typeof(T).Name).AddComponent<T>();
        }

        protected void Check(string name, bool pass, string evidence)
        {
            if (pass)
            {
                Passed++;
                Debug.Log($"[AUTOTEST] PASS {name}: {evidence}");
            }
            else
            {
                Failed++;
                Debug.LogError($"[AUTOTEST] FAIL {name}: {evidence}");
            }
        }

        protected void Finish()
        {
            Debug.Log($"[AUTOTEST] DONE passed={Passed} failed={Failed}");
            Destroy(gameObject);
        }

        // 前置條件不成立:記一個失敗並結束
        protected void FailSetup(string message)
        {
            Failed++;
            Debug.LogError("[AUTOTEST] FAIL 前置:" + message);
            Finish();
        }

        protected static GameObject FindPlayer()
        {
            return GameObject.Find("Player");
        }

        protected static void Teleport(Component character, Vector3 position)
        {
            // 角色走統一的 Inkling.Teleport(保留面向與視角,清運動/爬牆/烏賊狀態)
            var inkling = character.GetComponent<Inkling>();
            if (inkling != null)
            {
                inkling.Teleport(position);
                return;
            }
            var controller = character.GetComponent<CharacterController>();
            if (controller != null)
            {
                controller.enabled = false;
            }
            character.transform.position = position;
            if (controller != null)
            {
                controller.enabled = true;
            }
            var locomotion = character.GetComponent<PlayerLocomotion>();
            if (locomotion != null)
            {
                locomotion.ResetMotion();
            }
        }

        protected static void Drain(PlayerInkTank tank)
        {
            tank.InfiniteInk = false;
            int guard = 0;
            while (tank.TryConsume(0.01f) && guard++ < 200)
            {
            }
            tank.LockRefill(0f);
        }

        // 等到條件成立或逾時;回傳後用 condition() 再判一次即可知道結果
        protected static IEnumerator WaitUntil(Func<bool> condition, float timeoutSeconds)
        {
            float end = Time.time + timeoutSeconds;
            while (!condition() && Time.time < end)
            {
                yield return null;
            }
        }
    }
}

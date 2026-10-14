using System.Collections;
using SplatoonC.Core;
using SplatoonC.Core.Match;
using SplatoonC.Gameplay.CameraRig;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // M5.3 驗收:以真實輸入路徑量測與 Splatoon 3 對齊的數值——跑速 5.76、游速 11.52、按住跳頂點 ≈2.0m、
    // 點按跳較低、敵墨中跳不上 1.5m、回墨(潛墨 3s / 站立 10s)、一槽墨的塗地面積(校正 m²/p 用,只印不斷言)。
    // 在 SampleScene(試射場,平坦 50×50 地面)執行;需新 play session(會把地面塗滿、耗墨)。
    public sealed class S3AlignmentAutoTest : MonoBehaviour
    {
        private int _passed;
        private int _failed;

        public static void Run()
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("[AUTOTEST] 需在 Play mode 中執行");
                return;
            }
            new GameObject("S3AlignmentAutoTest").AddComponent<S3AlignmentAutoTest>();
        }

        private IEnumerator Start()
        {
            GameObject player = GameObject.Find("Player");
            var router = player != null ? player.GetComponent<PlayerInputRouter>() : null;
            var controller = player != null ? player.GetComponent<CharacterController>() : null;
            var tank = player != null ? player.GetComponent<PlayerInkTank>() : null;
            var inkling = player != null ? player.GetComponent<Inkling>() : null;
            var squid = player != null ? player.GetComponent<SquidController>() : null;
            var rig = Camera.main != null ? Camera.main.GetComponent<ThirdPersonCameraRig>() : null;
            GameObject groundGo = GameObject.Find("Ground");
            var ground = groundGo != null ? groundGo.GetComponent<PaintableSurface>() : null;
            if (router == null || controller == null || tank == null || inkling == null || squid == null || rig == null
                || ground == null)
            {
                Debug.LogError("[AUTOTEST] FAIL 前置:找不到 Player 元件 / 相機 / Ground(需在 SampleScene)");
                Debug.Log("[AUTOTEST] DONE passed=0 failed=1");
                Destroy(gameObject);
                yield break;
            }
            var intent = ScriptedIntent.Drive(player.transform);

            // 1) 跑速:x = -18 由 z = -20 往 +Z
            Teleport(player.transform, controller, new Vector3(-18f, 0.1f, -20f));
            rig.SetAngles(0f, 12f);
            yield return new WaitForSeconds(0.3f);
            intent.Move = new Vector2(0f, 1f);
            yield return new WaitForSeconds(1.2f);
            Vector3 a = player.transform.position;
            yield return new WaitForSeconds(1f);
            Vector3 b = player.transform.position;
            float runSpeed = Flat(b - a);
            intent.Move = Vector2.zero;
            Check("人形跑速 5.76 m/s", Mathf.Abs(runSpeed - 5.76f) < 0.2f, $"實測 {runSpeed:0.00} m/s");

            // 2) 游速:在 x = -18 鋪一條自家墨,烏賊態往 +Z
            for (int i = 0; i <= 44; i += 2)
            {
                ground.Paint(new Vector3(-18f, 0f, -22f + i), 2.5f, Team.Alpha, 0.8f);
            }
            Teleport(player.transform, controller, new Vector3(-18f, 0.1f, -20f));
            yield return new WaitForSeconds(0.3f);
            intent.Squid = true;
            intent.Move = new Vector2(0f, 1f);
            yield return new WaitForSeconds(0.9f);
            a = player.transform.position;
            yield return new WaitForSeconds(1f);
            b = player.transform.position;
            float swimSpeed = Flat(b - a);
            intent.Move = Vector2.zero;
            intent.Squid = false;
            // S3 中量級 0 AP 11.52 × 常駐游泳速度提升裝備(PlayerLocomotionConfig;57 AP = 14.4)
            int gearAp = InklingModifiers.For(player.transform).GearSwimSpeedAp;
            float expectedSwim = 11.52f * SplatoonC.Core.Combat.AbilityCurve.SwimSpeedRatio(gearAp, 1);
            Check($"自家墨潛游 {expectedSwim:0.00} m/s(11.52 × 游泳速度提升 {gearAp} AP)", Mathf.Abs(swimSpeed - expectedSwim) < 0.4f,
                $"實測 {swimSpeed:0.00} m/s");

            // 3) 跳躍:按住 vs 點按(頂點高度)
            Teleport(player.transform, controller, new Vector3(-10f, 0.1f, -10f));
            yield return new WaitForSeconds(0.5f);
            float held = 0f;
            yield return MeasureJump(player.transform, intent, true, h => held = h);
            float tapped = 0f;
            yield return MeasureJump(player.transform, intent, false, h => tapped = h);
            Check("按住跳頂點 ≈ 2.0m(1.5m 台階可跳上)", held > 1.8f && held < 2.25f, $"頂點 {held:0.00} m");
            Check("點按跳頂點較低(1.0~1.6m)", tapped > 1.0f && tapped < 1.6f && tapped < held - 0.3f,
                $"點按 {tapped:0.00} m / 按住 {held:0.00} m");

            // 4) 回墨:清空 → 等鎖解除 → 潛在自家墨裡計時(S3:3 秒回滿)
            tank.InfiniteInk = false;
            Teleport(player.transform, controller, new Vector3(-18f, 0.1f, 0f));
            yield return new WaitForSeconds(0.3f);
            Drain(tank);
            tank.LockRefill(0f);
            intent.Squid = true;
            yield return new WaitForSeconds(0.25f);
            float start = Time.time;
            while (tank.Normalized < 0.999f && Time.time - start < 8f)
            {
                yield return null;
            }
            float submergedRefill = Time.time - start;
            intent.Squid = false;
            Check("潛墨回滿 ≈ 3 秒", Mathf.Abs(submergedRefill - 3f) < 0.35f, $"實測 {submergedRefill:0.00} s");

            // 5) 站在乾地回墨(S3:10 秒回滿)——量 2 秒的回墨量推算
            Teleport(player.transform, controller, new Vector3(-10f, 0.1f, 10f));
            yield return new WaitForSeconds(0.3f);
            Drain(tank);
            yield return new WaitForSeconds(0.4f);
            float before = tank.Normalized;
            yield return new WaitForSeconds(2f);
            float perSecond = (tank.Normalized - before) / 2f;
            float standingFull = perSecond > 0f ? 1f / perSecond : 999f;
            Check("站立回滿 ≈ 10 秒", Mathf.Abs(standingFull - 10f) < 0.8f, $"推算 {standingFull:0.0} s(每秒 {perSecond:0.000})");

            // 6) 墨量鎖:開火後 0.33s 內不回墨
            Drain(tank);
            yield return new WaitForSeconds(0.6f);
            float beforeLock = tank.Normalized;
            intent.Attack = true;
            yield return new WaitForSeconds(0.25f);
            intent.Attack = false;
            float atRelease = tank.Normalized;
            yield return new WaitForSeconds(0.2f);
            bool lockHeld = tank.Normalized <= atRelease + 0.001f;
            Check("開火(即使墨不足)後 20F 內不回墨", lockHeld && beforeLock > 0.01f,
                $"放開時 {atRelease:0.000} → 0.2s 後 {tank.Normalized:0.000}");

            // 7) 一槽墨塗地量(只印):x = -8 由 z = +22 往 -Z 慢走連射到空
            ground.ClearInk();
            tank.RefillFull();
            Teleport(player.transform, controller, new Vector3(-6f, 0.1f, 22f));
            rig.SetAngles(180f, 14f);
            yield return new WaitForSeconds(0.5f);
            float areaBefore = inkling.TurfInkedArea;
            intent.Attack = true;
            intent.Move = new Vector2(0.35f, 0.55f);
            float fireStart = Time.time;
            while (tank.Normalized > 0.009f && Time.time - fireStart < 14f)
            {
                intent.Look = new Vector2(Mathf.Sin((Time.time - fireStart) * 2.2f) * 1.2f, 0f);
                yield return null;
            }
            intent.Attack = false;
            intent.Move = Vector2.zero;
            intent.Look = Vector2.zero;
            yield return new WaitForSeconds(0.8f);
            float areaPerTank = inkling.TurfInkedArea - areaBefore;
            Debug.Log($"[AUTOTEST] INFO 一槽墨(連射 {Time.time - fireStart:0.0}s)新塗地面 {areaPerTank:0} m²;" +
                      $"以 S3 Splattershot 一槽 ≈ 430p 推算 → {areaPerTank / 430f:0.00} m²/p(目前設定 {TurfPoints.SquareMetresPerPoint} m²/p = {TurfPoints.FromArea(areaPerTank):0}p)");

            router.ClearOverrideSource();
            Debug.Log($"[AUTOTEST] DONE passed={_passed} failed={_failed}");
            Destroy(gameObject);
        }

        private static IEnumerator MeasureJump(Transform player, ScriptedIntent intent, bool hold, System.Action<float> report)
        {
            float baseY = player.position.y;
            intent.JumpHold = hold;
            intent.PressJump();
            float peak = baseY;
            float end = Time.time + 1.6f;
            while (Time.time < end)
            {
                peak = Mathf.Max(peak, player.position.y);
                yield return null;
            }
            intent.JumpHold = true;
            report(peak - baseY);
            yield return new WaitForSeconds(0.3f);
        }

        private static void Drain(PlayerInkTank tank)
        {
            tank.InfiniteInk = false;
            int guard = 0;
            while (tank.TryConsume(0.01f) && guard++ < 200)
            {
            }
            tank.LockRefill(0f);
        }

        private static void Teleport(Transform t, CharacterController controller, Vector3 position)
        {
            controller.enabled = false;
            t.position = position;
            controller.enabled = true;
            var locomotion = t.GetComponent<PlayerLocomotion>();
            if (locomotion != null)
            {
                locomotion.ResetMotion();
            }
        }

        private static float Flat(Vector3 v)
        {
            v.y = 0f;
            return v.magnitude;
        }

        private void Check(string name, bool pass, string evidence)
        {
            if (pass)
            {
                _passed++;
                Debug.Log($"[AUTOTEST] PASS {name}: {evidence}");
            }
            else
            {
                _failed++;
                Debug.LogError($"[AUTOTEST] FAIL {name}: {evidence}");
            }
        }
    }
}

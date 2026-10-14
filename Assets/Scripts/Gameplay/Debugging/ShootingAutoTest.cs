using System.Collections;
using SplatoonC.Gameplay.CameraRig;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;
using UnityEngine.Rendering;

namespace SplatoonC.Gameplay.Debugging
{
    // M1 步驟 4 煙霧測試:走「intent → InkShooter → FireClock → 墨彈 → 塗色」全鏈真路徑。
    public sealed class ShootingAutoTest : MonoBehaviour
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
            var host = new GameObject("ShootingAutoTest");
            host.AddComponent<ShootingAutoTest>();
        }

        private int _passed;
        private int _failed;
        private int _lastCount;

        private IEnumerator Start()
        {
            GameObject player = GameObject.Find("Player");
            PlayerInputRouter router = player != null ? player.GetComponent<PlayerInputRouter>() : null;
            InkShooter shooter = player != null ? player.GetComponent<InkShooter>() : null;
            PaintableSurface ground = null;
            foreach (var surface in PaintableSurface.Active)
            {
                if (surface.name == "Ground")
                {
                    ground = surface;
                }
            }
            ThirdPersonCameraRig rig = Camera.main != null
                ? Camera.main.GetComponent<ThirdPersonCameraRig>() : null;
            if (router == null || shooter == null || ground == null || rig == null)
            {
                Debug.LogError("[AUTOTEST] FAIL 前置:找不到 Player/Router/InkShooter/Ground/CameraRig");
                Debug.Log("[AUTOTEST] DONE passed=0 failed=1");
                Destroy(gameObject);
                yield break;
            }

            var intent = new TestIntentSource();
            router.SetOverrideSource(intent);
            // 相機角度歸位:編輯器前景時真滑鼠會在測試前污染 yaw(2026-09-01 實戰:yaw 殘留 338 度)。
            // 瞄 -Z 空曠區:+Z 有不可塗的矮階,22m/s 墨彈受重力只飛約 5.6m,會全數落在矮階上假紅。
            rig.SetAngles(180f, 10f);
            yield return null;
            yield return null;

            // 案 1:按住 Attack 1 秒(約 8 發)→ 等落彈 → 地面墨量顯著增加
            yield return CountInk(ground);
            int baseline = _lastCount;
            intent.AttackHeld = true;
            yield return new WaitForSeconds(1f);
            intent.AttackHeld = false;
            yield return new WaitForSeconds(2.5f);
            yield return CountInk(ground);
            int afterVolley = _lastCount;
            Check("射擊塗色", afterVolley - baseline > 200,
                $"delta={afterVolley - baseline}(8 發主點+噴濺,期望 >200)");

            // 案 2:所有墨彈回收,場上無殘留活彈
            var activeProjectiles = FindObjectsByType<InkProjectile>();
            int stillActive = 0;
            foreach (var p in activeProjectiles)
            {
                if (p.gameObject.activeInHierarchy)
                {
                    stillActive++;
                }
            }
            Check("彈體回收", stillActive == 0, $"activeInHierarchy={stillActive}");

            // 案 3:命中非可塗表面時,地面只該留下槍口噴濺(路徑痕跡機制),不該有主 splat。
            // 槍口噴濺是刻意功能,故不能用絕對門檻;改用同長度連射的「空地 vs 牆」比值自我校準,
            // 半徑調參時門檻自動跟著走。
            var controller = player.GetComponent<CharacterController>();
            Vector3 spawnPos = player.transform.position;
            const float burst = 0.6f;

            // 3a 校準:移到未塗過的空地朝 -Z 連射(槍口噴濺 + 主 splat + 噴濺小點)
            controller.enabled = false;
            player.transform.position = new Vector3(-16f, 0.1f, 18f);
            controller.enabled = true;
            rig.SetAngles(180f, 10f);
            for (int i = 0; i < 5; i++)
            {
                yield return null;
            }
            yield return CountInk(ground);
            int beforeOpen = _lastCount;
            intent.AttackHeld = true;
            yield return new WaitForSeconds(burst);
            intent.AttackHeld = false;
            yield return new WaitForSeconds(1.5f);
            yield return CountInk(ground);
            int deltaOpen = _lastCount - beforeOpen;

            // 3b:回出生點面向旁邊的牆,同樣長度連射 → 地面只剩槍口噴濺
            controller.enabled = false;
            player.transform.position = spawnPos;
            controller.enabled = true;
            rig.SetAngles(90f, 10f);
            for (int i = 0; i < 5; i++)
            {
                yield return null;
            }
            yield return CountInk(ground);
            int beforeWall = _lastCount;
            intent.AttackHeld = true;
            yield return new WaitForSeconds(burst);
            intent.AttackHeld = false;
            yield return new WaitForSeconds(1.5f);
            yield return CountInk(ground);
            int deltaWall = _lastCount - beforeWall;

            Check("命中牆不塗地(地面僅槍口噴濺)", deltaOpen > 0 && deltaWall < deltaOpen * 0.5f,
                $"空地 delta={deltaOpen}、對牆 delta={deltaWall}(期望對牆 < 空地一半)");

            // 案 4:連射 3 秒(效能取樣用,不斷言)
            intent.AttackHeld = true;
            yield return new WaitForSeconds(3f);
            intent.AttackHeld = false;
            Debug.Log("[AUTOTEST] INFO 連射 3 秒完成(效能取樣用)");
            yield return new WaitForSeconds(1f);

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

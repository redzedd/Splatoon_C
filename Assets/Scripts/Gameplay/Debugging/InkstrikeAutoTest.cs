using System.Collections;
using SplatoonC.Gameplay.CameraRig;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // M4 步驟 5 驗收:啟動後右鍵丟裝置 ×3 → 能量歸零結束 / 裝置貼牆 / 0.5 秒後長出龍捲風並由細變粗 /
    // 龍捲風殺死牆腳假人 / 牆腳地面有墨 / 裝置不黏假人 / 大招結束後右鍵回到丟炸彈。
    // 需新 play session;站位 (20,0.1,4) 面向 +X 的 TargetWall(x=24~25,z=3~9)。
    // z 不可用 6:那是 Dummy_Moving 的巡邏線(x 18→23),它會把玩家的 CharacterController 一路推到牆邊(2026-09-09 實測)。
    public sealed class InkstrikeAutoTest : MonoBehaviour
    {
        private sealed class TestIntentSource : IPlayerIntentSource
        {
            public Vector2 MoveInput => Vector2.zero;
            public Vector2 LookDeltaDeg => Vector2.zero;
            public bool JumpPressedThisFrame => false;
            public bool AttackHeld => false;
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
            var host = new GameObject("InkstrikeAutoTest");
            host.AddComponent<InkstrikeAutoTest>();
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
            var thrower = player != null ? player.GetComponent<ThrowController>() : null;
            var rig = Camera.main != null ? Camera.main.GetComponent<ThirdPersonCameraRig>() : null;
            GameObject wall = GameObject.Find("TargetWall");
            GameObject nearGo = GameObject.Find("Dummy_Near");
            GameObject farGo = GameObject.Find("Dummy_Far");
            var near = nearGo != null ? nearGo.GetComponent<TargetDummy>() : null;
            var far = farGo != null ? farGo.GetComponent<TargetDummy>() : null;
            PaintableSurface ground = null;
            foreach (var surface in PaintableSurface.Active)
            {
                if (surface.name == "Ground")
                {
                    ground = surface;
                }
            }
            if (router == null || controller == null || tank == null || gauge == null || thrower == null
                || rig == null || wall == null || near == null || far == null || ground == null
                || gauge.Config == null || thrower.InkstrikeConfig == null)
            {
                Debug.LogError("[AUTOTEST] FAIL 前置:找不到 Player/PlayerSpecialGauge/ThrowController/CameraRig/TargetWall/假人(先跑 Tools/SplatoonC/M4 Setup)");
                Debug.Log("[AUTOTEST] DONE passed=0 failed=1");
                Destroy(gameObject);
                yield break;
            }
            TripleInkstrikeConfig config = gauge.Config;
            var intent = new TestIntentSource();
            router.SetOverrideSource(intent);
            Vector3 spawnPos = player.transform.position;
            tank.InfiniteInk = true;

            // 站位:面向 +X 的 TargetWall(4m 外),俯角 12°(平視時初速 22 + 上抬 25° 會從 3m 高的牆頂飛過);
            // Dummy_Near 搬到牆腳、稍偏離投擲線
            Teleport(player, controller, new Vector3(20f, 0.1f, 4f));
            rig.SetAngles(90f, 12f);
            near.transform.position = new Vector3(23.2f, 0f, 5.2f);
            yield return WaitSettle();

            // 案 1:設滿按 R 啟動 → 剩餘 3
            gauge.SetPointsForTest(100f);
            yield return null;
            intent.SpecialPressedThisFrame = true;
            yield return null;
            intent.SpecialPressedThisFrame = false;
            yield return null;
            Check("滿格按 R 啟動大招,可丟 3 個裝置",
                gauge.IsActive && gauge.DevicesRemaining == config.MaxDevices && thrower.WillThrowDevice,
                $"啟動={gauge.IsActive} 剩餘={gauge.DevicesRemaining} 放開會丟裝置={thrower.WillThrowDevice}");

            // 案 2/3/4:先丟第一顆 → 0.3 秒後應貼在 TargetWall 上 → 0.5 秒後長出龍捲風,量它由細變粗;再丟第二、三顆
            int attachedBefore = InkstrikeDevice.AttachedCount;
            int spawnedBefore = Tornado.SpawnCount;
            int nearVersion = near.PopupVersion;
            intent.SubWeaponHeld = true;
            yield return WaitSettle();
            intent.SubWeaponHeld = false;
            yield return null;
            InkstrikeDevice first = InkstrikeDevice.LastLaunched;
            yield return new WaitForSeconds(0.3f);
            bool firstAttached = first != null && first.IsAttached;
            string firstAttachedTo = first != null && first.AttachedCollider != null ? first.AttachedCollider.name : "(無)";
            float firstY = first != null ? first.transform.position.y : -1f;
            Check("裝置貼在牆上(TargetWall,不掉到地上)",
                firstAttached && firstAttachedTo == "TargetWall" && firstY > 0.5f,
                $"貼附={firstAttached} 貼在={firstAttachedTo} 高度={firstY:0.00}");

            float deadline = Time.time + 1.5f;
            while (Tornado.SpawnCount == spawnedBefore && Time.time < deadline)
            {
                yield return null;
            }
            Tornado tornado = Tornado.LastSpawned;
            float radiusEarly = tornado != null ? tornado.CurrentRadius : -1f;
            yield return new WaitForSeconds(config.TornadoGrowDuration);
            float radiusLate = tornado != null ? tornado.CurrentRadius : -1f;
            Check("附著約 70F 後長出龍捲風,並在 50F 內由細變粗到最大半徑(S3 7.7m)",
                tornado != null && radiusEarly < config.TornadoMaxRadius * 0.5f
                && radiusLate >= config.TornadoMaxRadius - 0.3f,
                $"龍捲風={tornado != null} 初期半徑={radiusEarly:0.00} 1 秒後={radiusLate:0.00}(最大 {config.TornadoMaxRadius})");

            for (int i = 1; i < config.MaxDevices; i++)
            {
                intent.SubWeaponHeld = true;
                yield return WaitSettle();
                intent.SubWeaponHeld = false;
                yield return null;
                yield return new WaitForSeconds(0.2f);
            }
            yield return null;
            Check("丟滿 3 個裝置:能量歸零、大招結束、之後放開改丟炸彈",
                thrower.DeviceThrowCount == config.MaxDevices && !gauge.IsActive && gauge.Points == 0f
                && gauge.DevicesRemaining == 0 && !thrower.WillThrowDevice,
                $"裝置投擲數={thrower.DeviceThrowCount} 啟動={gauge.IsActive} 能量={gauge.Points:0.0} 剩餘={gauge.DevicesRemaining}");

            // 案 5:牆腳假人被龍捲風高額持續傷害殺死(紅字)
            deadline = Time.time + 1.5f;
            while (near.IsAlive && Time.time < deadline)
            {
                yield return null;
            }
            Check("龍捲風殺死範圍內假人", !near.IsAlive && near.PopupIsKill && near.PopupVersion != nearVersion,
                $"存活={near.IsAlive} 累計={near.PopupTotal:0.0} 紅字={near.PopupIsKill}");

            // 等三個龍捲風都結束(S3:附著 70F 起算 → 長大 50F → 維持 ~51F → 淡出)
            yield return new WaitForSeconds(config.ArmDelay + config.TornadoGrowDuration
                                            + config.TornadoHoldDuration + config.TornadoFadeDuration + 0.6f);
            Check("三個裝置各長出一個龍捲風並結束",
                Tornado.SpawnCount - spawnedBefore == config.MaxDevices && Tornado.ActiveCount == 0
                && InkstrikeDevice.AttachedCount - attachedBefore == config.MaxDevices,
                $"龍捲風生成={Tornado.SpawnCount - spawnedBefore} 仍活著={Tornado.ActiveCount} 貼附數={InkstrikeDevice.AttachedCount - attachedBefore}");

            // 案 6:牆腳地面有墨(龍捲風掃盤塗地)
            Vector3 foot = new Vector3(22.5f, 0f, 4f);
            int inked = 0;
            for (int k = 0; k < 8; k++)
            {
                float angle = k * Mathf.PI * 2f / 8f;
                Vector3 probe = foot + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                if (ground.SampleOwnership(probe) == 1)
                {
                    inked++;
                }
            }
            Check("龍捲風塗墨:牆腳地面 8 點 ≥ 6 點有墨", ground.SampleOwnership(foot) == 1 && inked >= 6,
                $"中心有墨={ground.SampleOwnership(foot) == 1} 圓周有墨 {inked}/8");

            // 案 7:裝置不黏假人——退到 x=18,Dummy_Far 擺在槍口正前方 0.7m(槍口在角色前約 2.8m,擺太近會在槍口後面),
            // 俯角 15 直接砸中,裝置該彈開落地
            near.transform.position = new Vector3(21f, 0f, -10f);
            Teleport(player, controller, new Vector3(18f, 0.1f, 4f));
            rig.SetAngles(90f, 15f);
            gauge.SetPointsForTest(100f);
            yield return null;
            intent.SpecialPressedThisFrame = true;
            yield return null;
            intent.SpecialPressedThisFrame = false;
            intent.SubWeaponHeld = true;
            yield return WaitSettle();
            Transform muzzle = GameObject.Find("Muzzle").transform;
            far.transform.position = new Vector3(muzzle.position.x + 0.7f, 0f, muzzle.position.z);
            yield return null;
            yield return null;
            intent.SubWeaponHeld = false;
            yield return null;
            InkstrikeDevice loose = InkstrikeDevice.LastLaunched;
            // 砸中假人後只剩 0.2 倍法向速度,要落到地上約 0.5 秒
            yield return new WaitForSeconds(0.9f);
            bool attached = loose != null && loose.IsAttached;
            string attachedTo = loose != null && loose.AttachedCollider != null ? loose.AttachedCollider.name : "(無)";
            float looseY = loose != null ? loose.transform.position.y : -1f;
            // S3 裝置初速高,彈開後可能貼到後方牆面而非地面——重點是「不黏在假人身上」
            Check("裝置不黏假人:砸中假人後彈開貼到地形",
                loose != null && attached && attachedTo != "Dummy_Far",
                $"裝置={loose != null} 貼附={attached} 貼在={attachedTo} 高度={looseY:0.00}");

            // 案 8:大招結束後右鍵丟的是炸彈
            deadline = Time.time + config.ActiveDuration + 1f;
            while (gauge.IsActive && Time.time < deadline)
            {
                yield return null;
            }
            int bombsBefore = thrower.ThrowCount;
            rig.SetAngles(90f, 12f);
            intent.SubWeaponHeld = true;
            yield return WaitSettle();
            intent.SubWeaponHeld = false;
            yield return null;
            yield return null;
            Check("大招結束後右鍵回到丟炸彈", !gauge.IsActive && thrower.ThrowCount == bombsBefore + 1,
                $"啟動={gauge.IsActive} 炸彈投擲數 {bombsBefore}→{thrower.ThrowCount}");

            far.transform.position = new Vector3(24f, 0f, -2f);
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

        private static IEnumerator WaitSettle()
        {
            yield return new WaitForSeconds(0.35f);
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

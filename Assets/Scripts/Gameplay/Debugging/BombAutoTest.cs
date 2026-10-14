using System.Collections;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.CameraRig;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // M4 步驟 3 驗收:按住右鍵瞄準(拉近、拋物線、主武器停火)/ 放開丟炸彈扣墨 / 墨不夠不丟 /
    // 近距、遠距傷害以「真實爆心距離」對照模型 / 爆炸塗地 / 烏賊態不能瞄準。
    // 需新 play session(墨量要從滿開始);站位都在 x ≥ 17 靶場帶。
    public sealed class BombAutoTest : MonoBehaviour
    {
        private sealed class TestIntentSource : IPlayerIntentSource
        {
            public Vector2 MoveInput => Vector2.zero;
            public Vector2 LookDeltaDeg => Vector2.zero;
            public bool JumpPressedThisFrame => false;
            public bool AttackHeld { get; set; }
            public bool SquidHeld { get; set; }
            public bool SubWeaponHeld { get; set; }
        }

        public static void Run()
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("[AUTOTEST] 需在 Play mode 中執行");
                return;
            }
            var host = new GameObject("BombAutoTest");
            host.AddComponent<BombAutoTest>();
        }

        private int _passed;
        private int _failed;

        private IEnumerator Start()
        {
            GameObject player = GameObject.Find("Player");
            var router = player != null ? player.GetComponent<PlayerInputRouter>() : null;
            var controller = player != null ? player.GetComponent<CharacterController>() : null;
            var tank = player != null ? player.GetComponent<PlayerInkTank>() : null;
            var thrower = player != null ? player.GetComponent<ThrowController>() : null;
            var arc = player != null ? player.GetComponentInChildren<LineRenderer>(true) : null;
            var rig = Camera.main != null ? Camera.main.GetComponent<ThirdPersonCameraRig>() : null;
            TargetDummy near = FindDummy("Dummy_Near");
            TargetDummy far = FindDummy("Dummy_Far");
            PaintableSurface ground = null;
            foreach (var surface in PaintableSurface.Active)
            {
                if (surface.name == "Ground")
                {
                    ground = surface;
                }
            }
            // 主武器停火的判定看出手次數(S3 墨彈與舊的兩段式彈都算)
            var main = player != null ? player.GetComponent<MainWeapon>() : null;
            if (router == null || controller == null || tank == null || thrower == null || arc == null
                || rig == null || near == null || far == null || ground == null || main == null
                || thrower.BombConfig == null)
            {
                Debug.LogError("[AUTOTEST] FAIL 前置:找不到 Player/ThrowController/ThrowArc/CameraRig/Ground/假人(先跑 Tools/SplatoonC/M4 Setup)");
                Debug.Log("[AUTOTEST] DONE passed=0 failed=1");
                Destroy(gameObject);
                yield break;
            }
            SplatBombConfig config = thrower.BombConfig;
            Camera camera = Camera.main;
            var intent = new TestIntentSource();
            router.SetOverrideSource(intent);
            Vector3 spawnPos = player.transform.position;

            // 站位 A:靶場北端面向 +Z 邊緣,炸彈落在場邊空地,不會炸到假人。
            // S3 投擲初速高(平丟約 16m),俯角 32° 讓落點留在 9m 內的地面上,預覽才找得到落點
            Teleport(player, controller, new Vector3(21f, 0.1f, 16f));
            rig.SetAngles(0f, 32f);
            yield return WaitSettle();
            float baselineDistance = rig.CurrentDistance;
            float baselineFov = camera.fieldOfView;

            // 案 1:按住 20 幀 → 瞄準中、鏡頭拉近、FOV 減、拋物線、主武器停火(墨量不變、無活彈)
            intent.SubWeaponHeld = true;
            yield return WaitSettle();
            float aimDistance = rig.CurrentDistance;
            float aimFov = camera.fieldOfView;
            int arcCount = arc.positionCount;
            bool hasLanding = thrower.HasPredictedLanding;
            float inkAtAim = tank.Normalized;
            int attacksBefore = main.AttackCount;
            intent.AttackHeld = true;
            yield return WaitFrames(10);
            intent.AttackHeld = false;
            int liveProjectiles = main.AttackCount - attacksBefore;
            Check("按住副武器:瞄準中、鏡頭拉近、拋物線顯示、主武器停火",
                thrower.IsAiming && aimDistance < baselineDistance - 0.8f && aimFov < baselineFov - 2.5f
                && arcCount > 5 && hasLanding && liveProjectiles == 0
                && Mathf.Abs(tank.Normalized - inkAtAim) < 0.002f,
                $"瞄準={thrower.IsAiming} 距離 {baselineDistance:0.00}→{aimDistance:0.00} FOV {baselineFov:0.0}→{aimFov:0.0} "
                + $"弧線點={arcCount} 落點={hasLanding} 出手={liveProjectiles} 墨 {inkAtAim:0.000}→{tank.Normalized:0.000}");

            // 案 2:放開 → 丟出、扣墨 0.7、結束瞄準
            int throwsBefore = thrower.ThrowCount;
            float inkBeforeThrow = tank.Normalized;
            int explosionsBeforeA = SplatBomb.ExplosionCount;
            intent.SubWeaponHeld = false;
            yield return null;
            yield return null;
            float spent = inkBeforeThrow - tank.Normalized;
            Check("放開丟出斯普拉炸彈並扣墨",
                thrower.ThrowCount == throwsBefore + 1 && thrower.ActiveBombs >= 1 && !thrower.IsAiming
                && Mathf.Abs(spent - config.InkCost) < 0.01f,
                $"投擲數 {throwsBefore}→{thrower.ThrowCount} 活炸彈={thrower.ActiveBombs} 扣墨={spent:0.000}(期望 {config.InkCost})");

            // 案 3:墨不足(剩 0.3 < 0.7)不丟
            throwsBefore = thrower.ThrowCount;
            inkBeforeThrow = tank.Normalized;
            intent.SubWeaponHeld = true;
            yield return WaitFrames(10);
            intent.SubWeaponHeld = false;
            yield return null;
            yield return null;
            Check("墨不足不丟",
                thrower.ThrowCount == throwsBefore && Mathf.Abs(tank.Normalized - inkBeforeThrow) < 0.01f,
                $"墨={inkBeforeThrow:0.000} 投擲數 {throwsBefore}→{thrower.ThrowCount}");
            tank.InfiniteInk = true;

            // 先等站位 A 那顆爆完:爆炸計數是全域的,不等的話它會被算成站位 B 那顆(2026-09-08 實測踩到)
            float deadline = Time.time + config.FuseSeconds + 1f;
            while (SplatBomb.ExplosionCount == explosionsBeforeA && Time.time < deadline)
            {
                yield return null;
            }

            // 站位 B:面向 Dummy_Near,掃俯角找「落點最接近假人腳前 1.5m」的角度,再丟
            Teleport(player, controller, new Vector3(21f, 0.1f, -16f));
            rig.SetAngles(0f, 10f);
            yield return WaitSettle();
            Vector3 aimPoint = near.transform.position + new Vector3(0f, 0f, -1.5f);
            float[] pitches = { -5f, 5f, 15f, 25f, 35f, 45f };
            float bestPitch = pitches[0];
            float bestError = float.MaxValue;
            intent.SubWeaponHeld = true;
            for (int i = 0; i < pitches.Length; i++)
            {
                rig.SetAngles(0f, pitches[i]);
                yield return WaitSettle();
                if (!thrower.HasPredictedLanding)
                {
                    continue;
                }
                float error = Vector3.Distance(Flat(thrower.PredictedLanding), Flat(aimPoint));
                if (error < bestError)
                {
                    bestError = error;
                    bestPitch = pitches[i];
                }
            }
            rig.SetAngles(0f, bestPitch);
            yield return WaitSettle();
            Vector3 predicted = thrower.PredictedLanding;
            int explosions = SplatBomb.ExplosionCount;
            int nearVersion = near.PopupVersion;
            intent.SubWeaponHeld = false;
            yield return null;
            deadline = Time.time + config.FuseSeconds + 1f;
            while (SplatBomb.ExplosionCount == explosions && Time.time < deadline)
            {
                yield return null;
            }
            yield return null;
            Vector3 center = SplatBomb.LastExplosionCenter;
            float nearDistance = Vector3.Distance(center, near.ClosestPoint(center));
            float expectedNear = ExplosionDamageModel.Evaluate(nearDistance,
                config.NearRadius, config.FarRadius, config.NearDamage, config.FarDamage);
            bool nearHit = near.PopupVersion != nearVersion;
            Check("炸彈近距爆炸:傷害符合真實爆心距離的模型",
                SplatBomb.ExplosionCount == explosions + 1 && expectedNear > 0f && nearHit
                && Mathf.Abs(near.LastDamage - expectedNear) < 0.01f,
                $"俯角={bestPitch}° 預測落點誤差={bestError:0.00}m 預測={predicted} 爆心={center} "
                + $"爆心距假人={nearDistance:0.00}m 期望={expectedNear} 實得={near.LastDamage}");
            Debug.Log($"[AUTOTEST] INFO 近距全額 {config.NearDamage}:{Mathf.Abs(near.LastDamage - config.NearDamage) < 0.01f}(爆心距 {nearDistance:0.00}m,近距半徑 {config.NearRadius})");

            // 案 5:遠距:把 Dummy_Far 搬到爆心旁 3m,同角度再丟一顆(落點確定性)→ 期望低額 30
            far.transform.position = new Vector3(center.x + 3f, 0f, center.z);
            yield return null;
            explosions = SplatBomb.ExplosionCount;
            int farVersion = far.PopupVersion;
            intent.SubWeaponHeld = true;
            yield return WaitSettle();
            intent.SubWeaponHeld = false;
            yield return null;
            deadline = Time.time + config.FuseSeconds + 1f;
            while (SplatBomb.ExplosionCount == explosions && Time.time < deadline)
            {
                yield return null;
            }
            yield return null;
            Vector3 center2 = SplatBomb.LastExplosionCenter;
            float farDistance = Vector3.Distance(center2, far.ClosestPoint(center2));
            float expectedFar = ExplosionDamageModel.Evaluate(farDistance,
                config.NearRadius, config.FarRadius, config.NearDamage, config.FarDamage);
            bool farHit = far.PopupVersion != farVersion;
            Check("炸彈遠距爆炸:傷害符合模型(期望低額)",
                SplatBomb.ExplosionCount == explosions + 1 && expectedFar > 0f && farHit
                && Mathf.Abs(far.LastDamage - expectedFar) < 0.01f,
                $"爆心={center2} 爆心距 Dummy_Far={farDistance:0.00}m 期望={expectedFar} 實得={far.LastDamage}");

            // 案 6:爆炸噴墨塗地:爆心 + 半徑 1.5m 圓周 8 點
            bool centerInked = ground.SampleOwnership(center2) == 1;
            int ringInked = 0;
            for (int k = 0; k < 8; k++)
            {
                float angle = k * Mathf.PI * 2f / 8f;
                Vector3 probe = center2 + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 1.5f;
                if (ground.SampleOwnership(probe) == 1)
                {
                    ringInked++;
                }
            }
            Check("爆炸噴墨塗地", centerInked && ringInked >= 6,
                $"爆心有墨={centerInked} 半徑 1.5m 圓周 8 點有墨 {ringInked} 點");

            // 案 7:烏賊態按副武器不瞄準也不丟
            intent.SquidHeld = true;
            yield return WaitFrames(2);
            intent.SubWeaponHeld = true;
            yield return WaitFrames(5);
            bool aimingWhileSquid = thrower.IsAiming;
            throwsBefore = thrower.ThrowCount;
            intent.SubWeaponHeld = false;
            yield return WaitFrames(2);
            intent.SquidHeld = false;
            yield return WaitFrames(2);
            Check("烏賊態按副武器不瞄準也不丟", !aimingWhileSquid && thrower.ThrowCount == throwsBefore,
                $"烏賊中瞄準={aimingWhileSquid} 投擲數 {throwsBefore}→{thrower.ThrowCount}");

            Teleport(player, controller, spawnPos);
            rig.SetAngles(0f, 12f);
            router.ClearOverrideSource();
            tank.InfiniteInk = false;
            Debug.Log($"[AUTOTEST] DONE passed={_passed} failed={_failed}");
            Destroy(gameObject);
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        private static TargetDummy FindDummy(string name)
        {
            GameObject go = GameObject.Find(name);
            return go != null ? go.GetComponent<TargetDummy>() : null;
        }

        private static void Teleport(GameObject player, CharacterController controller, Vector3 position)
        {
            controller.enabled = false;
            player.transform.position = position;
            controller.enabled = true;
        }

        // 相機 SmoothDamp 與拉近過渡以時間收斂,編輯器幀率高時 30 幀不夠,用時間等
        private static IEnumerator WaitSettle()
        {
            yield return new WaitForSeconds(0.35f);
        }

        private static IEnumerator WaitFrames(int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                yield return null;
            }
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

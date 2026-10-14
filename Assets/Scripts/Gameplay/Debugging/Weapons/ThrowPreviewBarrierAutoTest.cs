using System.Collections;
using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Combat;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 投擲預覽的炸彈共通規則(§4.3「碰到敵方 Splash Wall / Big Bubbler 就爆」):預覽落點要停在引爆物前,和實際一樣。
    //   (a) Suction Bomb(ThrowController 預設拋物線,HitMask 只有地形):自隊牆穿過、預覽停在敵方 Splash Wall 前,
    //       實際丟出也在那裡爆(A1 已知缺口)
    //   (b) Suction Bomb:預覽停在敵方 Big Bubbler 球罩(BarrierSphere,沒有 Collider)外,實際也在球面外爆
    //   (c) Autobomb(預設拋物線)同 (a)
    //   (d) A2 的 S3 彈道副武器(Point Sensor、Torpedo:自訂預覽 = BombRules.CastPath)也停在敵方牆前與球罩外
    [AutoTest(Scene = AutoTestAttribute.TrainingScene, Timeout = 90f, Order = 244)]
    public sealed class ThrowPreviewBarrierAutoTest : ThrowableTestBase
    {
        private const float WallFace = -12.2f;

        public static void Run()
        {
            Launch<ThrowPreviewBarrierAutoTest>();
        }

        private IEnumerator Start()
        {
            if (!Prepare("suction_bomb", out SuctionBombConfig suction))
            {
                yield break;
            }
            var autobomb = Resources.Load<SubWeaponConfig>("Weapons/Sub_autobomb");
            var sensor = Resources.Load<SubWeaponConfig>("Weapons/Sub_point_sensor");
            var torpedo = Resources.Load<SubWeaponConfig>("Weapons/Sub_torpedo");
            if (autobomb == null || sensor == null || torpedo == null)
            {
                FailSetup("缺 Sub_autobomb / Sub_point_sensor / Sub_torpedo(先跑 M7 Setup)");
                yield break;
            }

            // ── 敵方 / 自隊 Splash Wall(z = −6 跑道)──
            TestDeployable friendly = TestDeployable.Create("PreviewTest_FriendlyWall", new Vector3(-17f, 2f, -6f),
                new Vector3(0.4f, 4f, 4f));
            friendly.Setup(Player, Player.Team, 800f, TargetClass.SplashWall, false, true);
            TestDeployable enemy = TestDeployable.Create("PreviewTest_EnemyWall", new Vector3(-12f, 2f, -6f),
                new Vector3(0.4f, 4f, 4f));
            enemy.Setup(null, EnemyTeam, 800f, TargetClass.SplashWall, false, true);
            Physics.SyncTransforms();

            yield return MoveTo(new Vector3(-24f, 0.1f, -6f), Vector3.right);
            Vector3 suctionWall = default;
            yield return Predict(0f, v => suctionWall = v);
            int explosions = SuctionBomb.ExplosionCount;
            yield return Release();
            yield return WaitUntil(() => SuctionBomb.ExplosionCount > explosions, 2f);
            Vector3 blast = SuctionBomb.LastExplosionCenter;
            Check("Suction Bomb 預設拋物線預覽:自隊牆穿過、停在敵方 Splash Wall 前;實際也在那裡爆",
                Mathf.Abs(suctionWall.x - WallFace) < 0.3f && SuctionBomb.ExplosionCount == explosions + 1
                && blast.x < WallFace + 0.05f && Mathf.Abs(blast.x - suctionWall.x) < 0.8f,
                $"預覽 {suctionWall}(牆面 x = {WallFace})實際爆心 {blast}");

            Equip(autobomb);
            yield return WaitSeconds(0.6f);
            Vector3 autoWall = default;
            yield return Predict(0f, v => autoWall = v);
            Intent.Sub = false;
            yield return Frames(2);
            Check("Autobomb 預設拋物線預覽停在敵方 Splash Wall 前", Mathf.Abs(autoWall.x - WallFace) < 0.3f,
                $"預覽 {autoWall}");

            Equip(sensor);
            yield return WaitSeconds(0.6f);
            Vector3 sensorWall = default;
            yield return Predict(0f, v => sensorWall = v);
            Intent.Sub = false;
            yield return Frames(2);
            Equip(torpedo);
            yield return WaitSeconds(0.6f);
            Vector3 torpedoWall = default;
            yield return Predict(0f, v => torpedoWall = v);
            Intent.Sub = false;
            yield return Frames(2);
            Check("A2 S3 彈道副武器(Point Sensor / Torpedo)的預覽也停在敵方 Splash Wall 前",
                Mathf.Abs(sensorWall.x - WallFace) < 0.3f && Mathf.Abs(torpedoWall.x - WallFace) < 0.3f,
                $"Point Sensor {sensorWall} Torpedo {torpedoWall}");
            Destroy(friendly.gameObject);
            Destroy(enemy.gameObject);
            yield return Frames(2);

            // ── 敵方 Big Bubbler 球罩(z = 6 跑道,中心 x = −12、半徑 4)──
            var host = new GameObject("PreviewTest_Barrier");
            var center = new Vector3(-12f, 0f, 6f);
            var barrier = new BarrierSphere(host.transform, null) { Center = center, Radius = 4f, Blocking = true };
            barrier.Enable(EnemyTeam);
            Equip(suction);
            yield return MoveTo(new Vector3(-24f, 0.1f, 6f), Vector3.right);
            yield return WaitSeconds(0.6f);
            Vector3 shell = default;
            yield return Predict(0f, v => shell = v);
            explosions = SuctionBomb.ExplosionCount;
            yield return Release();
            yield return WaitUntil(() => SuctionBomb.ExplosionCount > explosions, 2f);
            blast = SuctionBomb.LastExplosionCenter;
            float shellError = Vector3.Distance(shell, center) - barrier.Radius;
            Check("Suction Bomb 預覽停在敵方球罩外(球面上);實際也在球面外爆",
                Mathf.Abs(shellError) < 0.3f && SuctionBomb.ExplosionCount == explosions + 1
                && Vector3.Distance(blast, center) >= barrier.Radius - 0.01f && Vector3.Distance(blast, shell) < 1f,
                $"預覽離球心 {Vector3.Distance(shell, center):0.00} m(半徑 {barrier.Radius})實際爆心離球心 {Vector3.Distance(blast, center):0.00} m");

            Equip(sensor);
            yield return WaitSeconds(0.6f);
            Vector3 sensorShell = default;
            yield return Predict(0f, v => sensorShell = v);
            Intent.Sub = false;
            yield return Frames(2);
            Check("Point Sensor 預覽也停在敵方球罩外",
                Mathf.Abs(Vector3.Distance(sensorShell, center) - barrier.Radius) < 0.35f,
                $"預覽離球心 {Vector3.Distance(sensorShell, center):0.00} m");
            barrier.Disable();
            Destroy(host);

            CheckNoExceptions("預覽屏障規則");
            Cleanup();
            Finish();
        }

        private void Equip(SubWeaponConfig config)
        {
            var kit = ScriptableObject.CreateInstance<WeaponKit>();
            kit.Setup("test_preview_" + config.name, config.DisplayName, config.name, "AutoTest 執行期組合", BaseKit.Main,
                config, BaseKit.Special, BaseKit.SpecialPoints, BaseKit.Weight, "射程", 50, "威力", 50, "射速", 50);
            Loadout.Equip(kit);
        }

        // 平視(pitch)按住副武器,回報預覽落點(按住中離開,呼叫端決定放開丟出或取消)
        private IEnumerator Predict(float pitch, System.Action<Vector3> report)
        {
            Tank.RefillFull();
            AimYawPitch(Vector3.right, pitch);
            Intent.Sub = true;
            yield return Frames(4);
            report(Thrower.HasPredictedLanding ? Thrower.PredictedLanding : new Vector3(999f, 999f, 999f));
        }
    }
}

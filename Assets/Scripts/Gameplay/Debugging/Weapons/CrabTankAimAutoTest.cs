using System.Collections;
using System.Collections.Generic;
using SplatoonC.Gameplay.CameraRig;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // Crab Tank × 真的鏡頭(ThirdPersonCameraRig):
    //   (a) 砲台子彈落在準心上——畫面中心射線打到的地面點 vs 砲台連射落點(散布關閉;S3 散布 3° 含上下,低角度打地面時
    //       前後落點會散到 5~17 m,量的是瞄準本身):每發都 < 0.5 m;
    //   (b) R 砲彈落點也在準心附近(< 1.5 m);
    //   (c) 步行時鏡頭水平轉速被壓到車身轉速(S3「步行時轉向與瞄準都很慢」),車身與鏡頭始終同向;
    //   (d) 大招結束後鏡頭限速解除。
    // 場地:試射場 x=−24~−4、z=−24~0 的空地,面向 +Z。
    [AutoTest(Scene = AutoTestAttribute.TrainingScene, Timeout = 60f, Order = 237)]
    public sealed class CrabTankAimAutoTest : AutoTestBase
    {
        private static readonly Vector3 Spot = new Vector3(-14f, 0.1f, -22f);

        private readonly List<Vector3> _turretImpacts = new List<Vector3>();
        private readonly List<Vector3> _cannonImpacts = new List<Vector3>();
        private Inkling _player;
        private CrabTankSpecial _special;
        private InkBulletImpactHandler _onImpact;

        public static void Run()
        {
            Launch<CrabTankAimAutoTest>();
        }

        private void OnDisable()
        {
            if (_onImpact != null)
            {
                InkBullet.Impacted -= _onImpact;
            }
        }

        private IEnumerator Start()
        {
            GameObject playerGo = FindPlayer();
            _player = playerGo != null ? playerGo.GetComponent<Inkling>() : null;
            var config = Resources.Load<CrabTankConfig>("Weapons/Special_crab_tank");
            var rig = Camera.main != null ? Camera.main.GetComponent<ThirdPersonCameraRig>() : null;
            if (_player == null || config == null || rig == null || !ReferenceEquals(_player.AimView, rig))
            {
                FailSetup("需要 Player、Special_crab_tank 與當作玩家視線的 ThirdPersonCameraRig");
                yield break;
            }
            ScriptedIntent intent = ScriptedIntent.Drive(_player);
            var gauge = _player.GetComponent<PlayerSpecialGauge>();
            WeaponKit original = _player.GetComponent<WeaponLoadout>().Kit;
            if (!SpecialTestKit.EquipSpecial(_player, config))
            {
                FailSetup("換裝 Crab Tank 失敗");
                yield break;
            }
            _special = _player.GetComponent<CrabTankSpecial>();
            _player.GetComponent<InklingHealth>().Invulnerable = true;
            _player.Teleport(Spot, 0f);
            rig.SetAngles(0f, 16f);
            yield return new WaitForSeconds(0.3f);
            gauge.SetPointsForTest(SplatoonC.Core.Combat.SpecialGauge.Full);
            yield return null;
            intent.PressSpecial();
            yield return WaitUntil(() => _special.Armored, 2f);
            yield return new WaitForSeconds(0.2f);

            _onImpact = OnImpact;
            InkBullet.Impacted += _onImpact;

            // (a) 砲台:準心點 = 鏡頭中心射線打到的地面(散布關閉,左右兩門都要落在準心上)
            Vector3 reticle = ReticlePoint(rig);
            _special.SpreadMultiplier = 0f;
            intent.Attack = true;
            yield return new WaitForSeconds(1f);
            intent.Attack = false;
            yield return new WaitForSeconds(0.5f);
            _special.SpreadMultiplier = 1f;
            float worst = 0f;
            foreach (Vector3 impact in _turretImpacts)
            {
                worst = Mathf.Max(worst, Flat(impact - reticle));
            }
            Check("砲台連射(左右兩門)落在準心上(每發 < 0.5 m)", _turretImpacts.Count >= 5 && worst < 0.5f,
                $"落點 {_turretImpacts.Count} 發 最大偏差 {worst:0.00} m;準心 {reticle};" +
                $"戰車瞄準點 {_special.LastAimTarget} 出膛 {_special.LastShotOrigin} 方向 {_special.LastShotDirection}");

            // (b) R 砲(曲射)也朝準心(準心點在發射當下重取)
            yield return new WaitForSeconds(0.6f);
            Vector3 cannonReticle = ReticlePoint(rig);
            intent.Sub = true;
            yield return WaitUntil(() => _cannonImpacts.Count > 0, 2.5f);
            intent.Sub = false;
            float cannonError = _cannonImpacts.Count > 0 ? Flat(_cannonImpacts[0] - cannonReticle) : -1f;
            Check("R 砲彈落在準心附近(< 1 m)", _cannonImpacts.Count > 0 && cannonError < 1f,
                _cannonImpacts.Count > 0
                    ? $"落點 {_cannonImpacts[0]} 準心 {cannonReticle} 差 {cannonError:0.00} m;戰車瞄準點 {_special.LastAimTarget} " +
                      $"出膛 {_special.LastShotOrigin} 方向 {_special.LastShotDirection}"
                    : "沒有落點");

            // (c) 步行時鏡頭水平轉速 = 車身轉速:每幀要求轉 30°,0.5 秒只轉得了約 35°
            float yaw0 = rig.Yaw;
            float start = Time.time;
            intent.Look = new Vector2(30f, 0f);
            float maxMismatch = 0f;
            while (Time.time - start < 0.5f)
            {
                yield return null;
                maxMismatch = Mathf.Max(maxMismatch, Mathf.Abs(Mathf.DeltaAngle(rig.Yaw, _special.TankYaw)));
            }
            intent.Look = Vector2.zero;
            float elapsed = Time.time - start;
            float turned = Mathf.Abs(Mathf.DeltaAngle(yaw0, rig.Yaw));
            float allowed = config.TurnDegPerSecond * elapsed;
            Check("步行時鏡頭轉速壓到車身轉速,車身與準心同向",
                turned <= allowed + 3f && turned >= allowed * 0.7f && maxMismatch < 3f,
                $"{elapsed:0.00} 秒鏡頭轉 {turned:0.0}°(上限 {allowed:0.0}°)、車身與鏡頭最大差 {maxMismatch:0.0}°");

            // (d) 結束後解除
            gauge.EndActive();
            yield return new WaitForSeconds(0.2f);
            Check("大招結束後鏡頭限速解除", rig.YawSpeedLimit <= 0f, $"YawSpeedLimit={rig.YawSpeedLimit}");

            InkBullet.Impacted -= _onImpact;
            _onImpact = null;
            intent.Attack = false;
            intent.Sub = false;
            _player.GetComponent<InklingHealth>().Invulnerable = false;
            _player.GetComponent<WeaponLoadout>().Equip(original);
            _player.GetComponent<PlayerInputRouter>().ClearOverrideSource();
            Finish();
        }

        private void OnImpact(in InkBulletImpact impact)
        {
            if (impact.Bullet == null || impact.Bullet.Owner != _player || impact.End != InkBulletEnd.Terrain)
            {
                return;
            }
            if (ReferenceEquals(impact.Bullet, _special.LastCannon))
            {
                _cannonImpacts.Add(impact.Point);
            }
            else
            {
                _turretImpacts.Add(impact.Point);
            }
        }

        private static Vector3 ReticlePoint(ThirdPersonCameraRig rig)
        {
            return Physics.Raycast(rig.AimOrigin, rig.AimForward, out RaycastHit hit, 60f, CombatMasks.Terrain,
                QueryTriggerInteraction.Ignore)
                ? hit.point
                : rig.AimOrigin + rig.AimForward * 20f;
        }

        private static float Flat(Vector3 v)
        {
            v.y = 0f;
            return v.magnitude;
        }
    }
}

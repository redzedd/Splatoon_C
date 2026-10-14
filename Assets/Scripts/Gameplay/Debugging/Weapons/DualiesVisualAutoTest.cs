using System.Collections;
using System.Collections.Generic;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 雙槍外觀驗收(試射場):雙持瞄準姿勢(兩把槍左右對稱、槍管對準準心、手握著槍)、
    // 翻滾姿勢(身體沿翻滾方向滾一圈、滾完回正)、砲台姿勢(雙槍併攏、蹲低),並輸出截圖:
    // Logs/shots/dualies_aim.png、dualies_guns.png、dualies_roll_sequence.png、dualies_turret.png。
    [AutoTest(Scene = AutoTestAttribute.TrainingScene, Timeout = 90f, Order = 221)]
    public sealed class DualiesVisualAutoTest : AutoTestBase
    {
        private sealed class TestAim : IAimView
        {
            public Vector3 Origin;
            public Vector3 Forward = Vector3.forward;

            public Vector3 AimOrigin => Origin;
            public Vector3 AimForward => Forward;
            public float Yaw => Mathf.Atan2(Forward.x, Forward.z) * Mathf.Rad2Deg;
        }

        private GameObject _player;
        private WeaponLoadout _loadout;
        private CharacterVisual _visual;
        private Transform _visualRoot;
        private PlayerInkTank _tank;
        private ScriptedIntent _intent;
        private TestAim _aim;
        private Camera _camera;
        private WeaponKit _baseKit;

        public static void Run()
        {
            Launch<DualiesVisualAutoTest>();
        }

        private DualiesWeapon Weapon => _loadout != null ? _loadout.Main as DualiesWeapon : null;

        private IEnumerator Start()
        {
            _player = FindPlayer();
            _loadout = _player != null ? _player.GetComponent<WeaponLoadout>() : null;
            _visual = _player != null ? _player.GetComponent<CharacterVisual>() : null;
            _tank = _player != null ? _player.GetComponent<PlayerInkTank>() : null;
            _visualRoot = _player != null ? _player.transform.Find("Visual") : null;
            var config = Resources.Load<DualiesConfig>(DualiesAutoTest.ConfigPath);
            var inkling = _player != null ? _player.GetComponent<Inkling>() : null;
            if (_loadout == null || _visual == null || !_visual.IsReady || _tank == null || _visualRoot == null
                || inkling == null || config == null || _loadout.Kit == null)
            {
                FailSetup("找不到 Player / CharacterVisual / 雙槍設定(先跑 M7 Setup)");
                yield break;
            }
            _baseKit = _loadout.Kit;
            var kit = ScriptableObject.CreateInstance<WeaponKit>();
            kit.Setup("splat_dualies", "斯普拉雙槍", "Splat Dualies", "AutoTest", config, _baseKit.Sub, _baseKit.Special,
                200f, WeightClass.Mid, "射程", 50, "威力", 29, "機動", 60);
            _intent = ScriptedIntent.Drive(_player.transform);
            _aim = new TestAim();
            inkling.AimView = _aim;
            _tank.InfiniteInk = true;
            Teleport(_player.transform, new Vector3(-12f, 0.1f, -14f));
            _loadout.Equip(kit);
            yield return new WaitForSeconds(0.4f);

            var camGo = new GameObject("DualiesShowcaseCamera");
            _camera = camGo.AddComponent<Camera>();
            _camera.enabled = false;
            _camera.fieldOfView = 38f;
            _camera.nearClipPlane = 0.05f;

            yield return CheckAimPose();
            yield return CheckRollAndTurretPose();

            Destroy(camGo);
            _intent.Attack = false;
            _intent.Move = Vector2.zero;
            _loadout.Equip(_baseKit);
            _tank.InfiniteInk = false;
            inkling.AimView = null;
            _player.GetComponent<PlayerInputRouter>().ClearOverrideSource();
            Teleport(_player.transform, new Vector3(0f, 0.1f, 0f));
            Finish();
        }

        private IEnumerator CheckAimPose()
        {
            DualiesWeapon weapon = Weapon;
            _intent.Attack = true;
            float end = Time.time + 0.5f;
            while (Time.time < end)
            {
                Aim(new Vector3(0f, -0.05f, 1f));
                yield return null;
            }
            DualiesGunPair guns = weapon.Guns;
            Transform handL = _visual.Rig.BoneTransform(HumanoidRig.Bone.HandL);
            Transform handR = _visual.Rig.BoneTransform(HumanoidRig.Bone.HandR);
            Vector3 left = Local(guns.Left.position);
            Vector3 right = Local(guns.Right.position);
            Vector3 aim = _aim.Forward;
            float leftAngle = Vector3.Angle(guns.Left.forward, aim);
            float rightAngle = Vector3.Angle(guns.Right.forward, aim);
            float separation = Vector3.Distance(guns.Left.position, guns.Right.position);
            float gripL = Vector3.Distance(handL.position, guns.Left.position);
            float gripR = Vector3.Distance(handR.position, guns.Right.position);
            _normalSeparation = separation;
            _normalHipsY = _visual.Rig.BoneTransform(HumanoidRig.Bone.Hips).position.y;
            Check("雙持瞄準:兩把槍左右對稱伸在身前、槍管都對準準心、手握在槍上",
                _visual.DualWieldPosing && left.x < -0.08f && right.x > 0.08f && left.z > 0.15f && right.z > 0.15f
                && separation > 0.3f && leftAngle < 15f && rightAngle < 15f && gripL < 0.12f && gripR < 0.12f,
                $"左槍 x={left.x:0.00} z={left.z:0.00} 右槍 x={right.x:0.00} z={right.z:0.00} 間距 {separation:0.00} m " +
                $"槍管偏差 左 {leftAngle:0.0}° 右 {rightAngle:0.0}° 手到槍 左 {gripL:0.00} 右 {gripR:0.00}");
            // 截圖前先停火幾幀:飛過相機旁的墨彈會糊成一大片(舉槍姿勢在停火後還維持 0.45 秒)
            _intent.Attack = false;
            for (int i = 0; i < 6; i++)
            {
                Aim(new Vector3(0f, -0.05f, 1f));
                yield return null;
            }
            Capture("dualies_aim", new Vector3(1.6f, 1.35f, 2.4f), new Vector3(0f, 1.0f, 0.2f), 1280, 720);
            Capture("dualies_guns", new Vector3(0.55f, 1.25f, 1.25f), new Vector3(0f, 1.0f, 0.45f), 1280, 720);
            yield return new WaitForSeconds(0.5f);
        }

        private float _normalSeparation;
        private float _normalHipsY;

        // 翻滾連續截圖的相機:角色左前方斜看(砲台模式的墨彈沿 +Z 飛,相機放正前方會被迎面的墨彈擋住)
        private static readonly Vector3 RollCameraOffset = new Vector3(-2.1f, 1.3f, 3.7f);
        private static readonly Vector3 RollCameraLook = new Vector3(0.3f, 0.6f, 0.2f);

        private IEnumerator CheckRollAndTurretPose()
        {
            DualiesWeapon weapon = Weapon;
            Teleport(_player.transform, new Vector3(-20f, 0.1f, -12f));
            yield return new WaitForSeconds(0.3f);
            Aim(Vector3.forward);
            yield return null;
            Transform hips = _visual.Rig.BoneTransform(HumanoidRig.Bone.Hips);
            Transform head = _visual.Rig.BoneTransform(HumanoidRig.Bone.Head);
            // 身體軸 = 骨盆 → 頭(骨頭局部軸的慣例不一,用位置差量最穩)
            float restTilt = Vector3.Angle(head.position - hips.position, Vector3.up);
            int rolls0 = weapon.RollCount;
            _intent.Attack = true;
            _intent.Move = new Vector2(1f, 0f);
            _intent.PressJump();
            var frames = new List<Texture2D>();
            float[] marks = { 0.2f, 0.45f, 0.6f, 0.75f, 0.9f };
            int mark = 0;
            float maxTilt = 0f;
            float maxAngle = 0f;
            float maxWeight = 0f;
            float deadline = Time.time + 1.5f;
            while (Time.time < deadline)
            {
                Aim(Vector3.forward);
                yield return null;
                if (weapon.RollCount == rolls0)
                {
                    continue;
                }
                maxTilt = Mathf.Max(maxTilt, Vector3.Angle(head.position - hips.position, Vector3.up) - restTilt);
                maxAngle = Mathf.Max(maxAngle, _visual.RollAngle);
                maxWeight = Mathf.Max(maxWeight, _visual.RollWeight);
                float progress = weapon.RollProgress;
                if (mark < marks.Length && progress >= marks[mark])
                {
                    frames.Add(RenderFrame(RollCameraOffset, RollCameraLook, 480, 360));
                    mark++;
                }
                if (weapon.IsTurret)
                {
                    break;
                }
            }
            _intent.Move = Vector2.zero;
            // 砲台姿勢定好(蹲低、雙槍併攏)
            float settle = Time.time + 0.35f;
            while (Time.time < settle)
            {
                Aim(Vector3.forward);
                yield return null;
            }
            frames.Add(RenderFrame(RollCameraOffset, RollCameraLook, 480, 360));
            DualiesGunPair guns = weapon.Guns;
            float separation = Vector3.Distance(guns.Left.position, guns.Right.position);
            float hipsY = hips.position.y;
            float tiltAfter = Vector3.Angle(head.position - hips.position, Vector3.up) - restTilt;
            Check("翻滾姿勢:身體沿翻滾方向滾一圈(骨盆翻過 90° 以上)、滾完回正",
                maxTilt > 90f && maxAngle > 300f && maxWeight > 0.9f && Mathf.Abs(tiltAfter) < 25f,
                $"骨盆最大傾角 {maxTilt:0}° 翻滾角 {maxAngle:0}° 權重 {maxWeight:0.00} 滾完傾角 {tiltAfter:0}°");
            Check("砲台姿勢:雙槍併攏在胸前、重心壓低",
                weapon.IsTurret && _visual.TurretWeight > 0.9f && separation < _normalSeparation * 0.6f
                && _normalHipsY - hipsY > 0.06f,
                $"砲台={weapon.IsTurret} 槍距 {separation:0.00} m(一般 {_normalSeparation:0.00})" +
                $" 骨盆降低 {_normalHipsY - hipsY:0.00} m");
            Capture("dualies_turret", new Vector3(2.1f, 1.35f, -0.9f), new Vector3(0f, 0.8f, 0.5f), 1280, 720);
            if (frames.Count > 0)
            {
                Texture2D sheet = ShotCapture.Stitch(frames.ToArray(), 3);
                ShotCapture.Save(sheet, "dualies_roll_sequence");
                Destroy(sheet);
            }
            foreach (Texture2D t in frames)
            {
                Destroy(t);
            }
            _intent.Attack = false;
            yield return WaitUntil(() => !weapon.IsRolling, 2f);
        }

        private void Aim(Vector3 direction)
        {
            Vector3 flat = new Vector3(direction.x, 0f, direction.z);
            if (flat.sqrMagnitude > 1e-6f)
            {
                _visualRoot.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
            }
            _aim.Forward = direction.normalized;
            DualiesWeapon weapon = Weapon;
            _aim.Origin = weapon != null ? weapon.MuzzleCenter(weapon.IsTurret) : _player.transform.position + Vector3.up;
        }

        private Vector3 Local(Vector3 world)
        {
            return Quaternion.Inverse(_visualRoot.rotation) * (world - _player.transform.position);
        }

        // 相機放在角色座標 offset、看向角色座標 look(公尺)
        private Texture2D RenderFrame(Vector3 offset, Vector3 look, int width, int height)
        {
            Quaternion yaw = Quaternion.Euler(0f, _visualRoot.eulerAngles.y, 0f);
            Vector3 origin = _player.transform.position;
            _camera.transform.position = origin + yaw * offset;
            _camera.transform.LookAt(origin + yaw * look);
            return ShotCapture.Render(_camera, width, height);
        }

        private void Capture(string name, Vector3 offset, Vector3 look, int width, int height)
        {
            Texture2D texture = RenderFrame(offset, look, width, height);
            ShotCapture.Save(texture, name);
            Destroy(texture);
        }
    }
}

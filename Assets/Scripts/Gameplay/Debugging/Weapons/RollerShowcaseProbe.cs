using System.Collections;
using SplatoonC.Gameplay.CameraRig;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 滾筒截圖探針:試射場依序換上 Splat / Carbon / Dynamo Roller,拍 拿著 / 跑步 / 橫甩舉起 / 甩出瞬間 / 推滾(側面、遊戲鏡頭)/
    // 空中直甩舉起 / 俯視的甩墨扇形與推滾墨帶。PNG 存在 <專案>/Logs/shots/roller_<id>_*.png,給人眼(與 Read 工具)逐張檢查。
    // 斷言只做健全性:每張攝影棚鏡頭「關掉滾筒 renderer 前後」差異像素要夠多(滾筒真的畫出來了、在鏡頭裡)。
    [AutoTest(Scene = AutoTestAttribute.TrainingScene, Timeout = 180f, Order = 962)]
    public sealed class RollerShowcaseProbe : AutoTestBase
    {
        private static readonly string[] Ids = { "splat_roller", "carbon_roller", "dynamo_roller" };

        private Camera _studio;
        private GameObject _player;
        private ThirdPersonCameraRig _rig;
        private ScriptedIntent _intent;
        private WeaponLoadout _loadout;
        private PaintableSurface _ground;

        public static void Run()
        {
            Launch<RollerShowcaseProbe>();
        }

        private void OnDestroy()
        {
            Time.captureDeltaTime = 0f;
        }

        private IEnumerator Start()
        {
            // 固定 60 Hz 步長:連拍的動作階段有代表性
            Time.captureDeltaTime = 1f / 60f;
            _player = FindPlayer();
            _rig = Camera.main != null ? Camera.main.GetComponent<ThirdPersonCameraRig>() : null;
            _loadout = _player != null ? _player.GetComponent<WeaponLoadout>() : null;
            foreach (PaintableSurface surface in PaintableSurface.Active)
            {
                if (surface.name == "Ground")
                {
                    _ground = surface;
                }
            }
            if (_player == null || _rig == null || _loadout == null || _loadout.Kit == null || _ground == null)
            {
                FailSetup("Player / 相機 rig / WeaponLoadout / Ground 缺一");
                yield break;
            }
            var go = new GameObject("RollerStudioCamera");
            _studio = go.AddComponent<Camera>();
            _studio.CopyFrom(Camera.main);
            _studio.targetTexture = null;
            _studio.nearClipPlane = 0.05f;
            _studio.enabled = false;
            _intent = ScriptedIntent.Drive(_player.transform);
            var tank = _player.GetComponent<PlayerInkTank>();
            tank.InfiniteInk = true;
            WeaponKit baseKit = _loadout.Kit;

            foreach (string id in Ids)
            {
                var config = Resources.Load<RollerConfig>("Weapons/Main_" + id);
                if (config == null)
                {
                    Check($"載入 Main_{id}", false, "先跑 M7 Setup");
                    continue;
                }
                var kit = ScriptableObject.CreateInstance<WeaponKit>();
                kit.Setup(id, id, id, "", config, baseKit.Sub, baseKit.Special, 180f, WeightClass.Mid, "", 0, "", 0, "", 0);
                _loadout.Equip(kit);
                var roller = _loadout.Main as RollerWeapon;
                yield return Shoot(id, roller);
            }

            _loadout.Equip(baseKit);
            tank.InfiniteInk = false;
            _player.GetComponent<PlayerInputRouter>().ClearOverrideSource();
            Destroy(_studio.gameObject);
            Finish();
        }

        private IEnumerator Shoot(string id, RollerWeapon roller)
        {
            _ground.ClearInk();
            Teleport(_player.transform, new Vector3(-12f, 0.1f, -16f));
            _rig.SetAngles(0f, 10f);
            yield return new WaitForSeconds(0.8f);
            StudioShot($"roller_{id}_idle", roller, new Vector3(2.4f, 1.1f, 2.2f), 0.8f, 36f);

            // 跑步拿著
            _intent.Move = new Vector2(0f, 1f);
            yield return new WaitForSeconds(0.7f);
            StudioShot($"roller_{id}_run", roller, new Vector3(3.2f, 1.0f, 0.4f), 0.8f, 38f);
            _intent.Move = Vector2.zero;
            yield return new WaitForSeconds(0.6f);

            // 橫甩:舉起 80% → 甩出瞬間 → 收招
            Teleport(_player.transform, new Vector3(-12f, 0.1f, -20f));
            _rig.SetAngles(0f, 8f);
            yield return new WaitForSeconds(0.5f);
            int flicks = roller.FlickCount;
            _intent.Attack = true;
            yield return null;
            _intent.Attack = false;
            while (roller.Action == RollerAction.Windup && roller.WindupProgress < 0.8f)
            {
                yield return null;
            }
            StudioShot($"roller_{id}_windup_side", roller, new Vector3(3.4f, 1.4f, 0.6f), 1.2f, 42f);
            while (roller.FlickCount == flicks)
            {
                yield return null;
            }
            yield return new WaitForSeconds(0.05f);
            StudioShot($"roller_{id}_fling_side", roller, new Vector3(3.4f, 1.2f, 0.8f), 1.0f, 42f);
            yield return new WaitForSeconds(0.12f);
            WorldShot($"roller_{id}_fling_top", _player.transform.position + new Vector3(0f, 16f, 4f),
                _player.transform.position + new Vector3(0f, 0f, 7f), 60f);
            yield return new WaitForSeconds(0.9f);
            WorldShot($"roller_{id}_fling_paint_top", _player.transform.position + new Vector3(0f, 18f, 6f),
                _player.transform.position + new Vector3(0f, 0f, 8f), 60f);

            // 推滾:按住 → 甩出後推滾,衝刺中拍側面 + 遊戲鏡頭
            _ground.ClearInk();
            Teleport(_player.transform, new Vector3(-8f, 0.1f, -23f));
            _rig.SetAngles(0f, 14f);
            yield return new WaitForSeconds(0.5f);
            _intent.Attack = true;
            _intent.Move = new Vector2(0f, 1f);
            float until = Time.time + 3f;
            while (!roller.IsDashing && Time.time < until)
            {
                yield return null;
            }
            yield return new WaitForSeconds(0.3f);
            StudioShot($"roller_{id}_roll_side", roller, new Vector3(3.0f, 1.0f, 0.8f), 0.6f, 42f);
            StudioShot($"roller_{id}_roll_front", roller, new Vector3(-1.2f, 1.2f, 3.6f), 0.6f, 40f);
            GameShot($"roller_{id}_roll_game");
            yield return new WaitForSeconds(0.4f);
            _intent.Attack = false;
            _intent.Move = Vector2.zero;
            yield return new WaitForSeconds(0.3f);
            WorldShot($"roller_{id}_roll_trail_top", _player.transform.position + new Vector3(0f, 20f, -7f),
                _player.transform.position + new Vector3(0f, 0f, -7f), 60f);

            // 空中直甩:舉起(往右後拉)
            Teleport(_player.transform, new Vector3(-18f, 0.1f, -20f));
            _rig.SetAngles(0f, 8f);
            yield return new WaitForSeconds(0.5f);
            _intent.PressJump();
            yield return new WaitForSeconds(0.15f);
            flicks = roller.FlickCount;
            _intent.Attack = true;
            yield return null;
            _intent.Attack = false;
            while (roller.Action == RollerAction.Windup && roller.WindupProgress < 0.8f)
            {
                yield return null;
            }
            StudioShot($"roller_{id}_vertical_windup", roller, new Vector3(-2.2f, 1.6f, 3.0f), 1.4f, 44f);
            while (roller.FlickCount == flicks)
            {
                yield return null;
            }
            yield return new WaitForSeconds(0.08f);
            StudioShot($"roller_{id}_vertical_fling", roller, new Vector3(-2.2f, 1.6f, 3.0f), 1.4f, 44f);
            yield return new WaitForSeconds(1.2f);
            WorldShot($"roller_{id}_vertical_paint_top", _player.transform.position + new Vector3(0f, 22f, 9f),
                _player.transform.position + new Vector3(0f, 0f, 10f), 60f);
        }

        private void GameShot(string shotName)
        {
            Camera cam = Camera.main;
            if (cam != null)
            {
                Destroy(ShotCapture.Capture(cam, shotName));
            }
        }

        private void WorldShot(string shotName, Vector3 position, Vector3 lookAt, float fov)
        {
            _studio.transform.SetPositionAndRotation(position, Quaternion.LookRotation(lookAt - position, Vector3.up));
            _studio.fieldOfView = fov;
            Destroy(ShotCapture.Capture(_studio, shotName));
        }

        // 相對角色(Visual 朝向)的鏡頭;關掉滾筒 renderer 再拍一次,差異像素 = 滾筒在畫面上的面積
        private void StudioShot(string shotName, RollerWeapon roller, Vector3 localOffset, float lookHeight, float fov)
        {
            Transform visual = _player.transform.Find("Visual");
            Quaternion facing = Quaternion.Euler(0f, visual != null ? visual.eulerAngles.y : 0f, 0f);
            Vector3 origin = _player.transform.position;
            Vector3 position = origin + facing * localOffset;
            _studio.transform.SetPositionAndRotation(position,
                Quaternion.LookRotation(origin + Vector3.up * lookHeight - position, Vector3.up));
            _studio.fieldOfView = fov;
            Texture2D shot = ShotCapture.Capture(_studio, shotName);
            RollerModel model = roller != null ? roller.Model : null;
            if (model != null)
            {
                Renderer[] renderers = model.GetComponentsInChildren<Renderer>(false);
                var states = new bool[renderers.Length];
                for (int i = 0; i < renderers.Length; i++)
                {
                    states[i] = renderers[i].enabled;
                    renderers[i].enabled = false;
                }
                Texture2D hidden = ShotCapture.Render(_studio);
                for (int i = 0; i < renderers.Length; i++)
                {
                    renderers[i].enabled = states[i];
                }
                int diff = ShotCapture.DiffPixels(shot, hidden);
                var cv = _player.GetComponent<CharacterVisual>();
                Check($"{shotName} 滾筒可見", diff > 1500,
                    $"滾筒像素差={diff} 動作={roller.Action} 舉起={roller.WindupProgress:0.00} 甩出={roller.FlingProgress:0.00} " +
                    $"雙手={(cv != null ? cv.RollerTwoHandWeight : 0f):0.00}");
                Destroy(hidden);
            }
            Destroy(shot);
        }
    }
}

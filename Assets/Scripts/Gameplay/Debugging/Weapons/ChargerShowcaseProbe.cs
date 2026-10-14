using System.Collections;
using SplatoonC.Gameplay.CameraRig;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 狙擊槍截圖探針:試射場依序換上 Splat Charger / E-liter 4K,拍 拿著 / 蓄一半 / 滿蓄(側面、斜前、遊戲鏡頭)/
    // 敵人視角(對面的狙擊手蓄滿時雷射指著鏡頭)/ 開槍後俯視墨線與終點圓 / 儲存蓄力的白圈。
    // PNG 存在 <專案>/Logs/shots/charger_<id>_*.png,給人眼(與 Read 工具)逐張檢查。
    // 斷言只做健全性:攝影棚鏡頭「關掉狙擊槍 renderer 前後」差異像素要夠多(槍真的畫出來了、在鏡頭裡);
    // 雷射鏡頭「關掉雷射前後」差異像素要夠多(兩隊都看得到的雷射真的畫出來了)。
    [AutoTest(Scene = AutoTestAttribute.TrainingScene, Timeout = 180f, Order = 963)]
    public sealed class ChargerShowcaseProbe : AutoTestBase
    {
        private static readonly string[] Ids = { "splat_charger", "e_liter_4k" };

        private Camera _studio;
        private GameObject _player;
        private ThirdPersonCameraRig _rig;
        private ScriptedIntent _intent;
        private WeaponLoadout _loadout;
        private PaintableSurface _ground;
        private Inkling _enemy;
        private ScriptedIntent _enemyIntent;
        private SpecialTestAim _enemyAim;

        public static void Run()
        {
            Launch<ChargerShowcaseProbe>();
        }

        private void OnDestroy()
        {
            Time.captureDeltaTime = 0f;
        }

        private IEnumerator Start()
        {
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
            var go = new GameObject("ChargerStudioCamera");
            _studio = go.AddComponent<Camera>();
            _studio.CopyFrom(Camera.main);
            _studio.targetTexture = null;
            _studio.nearClipPlane = 0.05f;
            _studio.enabled = false;
            // 敵方狙擊手(複製本機玩家,Bravo):換裝前複製,之後各自換上同一把狙擊槍
            _enemy = SpecialTestKit.CloneEnemy(_player, new Vector3(-22f, 0.1f, 22f), "ChargerShowcaseEnemy", out _enemyIntent);
            _enemyAim = new SpecialTestAim();
            _enemy.AimView = _enemyAim;
            _intent = ScriptedIntent.Drive(_player.transform);
            var tank = _player.GetComponent<PlayerInkTank>();
            tank.InfiniteInk = true;
            _enemy.GetComponent<PlayerInkTank>().InfiniteInk = true;
            WeaponKit baseKit = _loadout.Kit;

            foreach (string id in Ids)
            {
                var config = Resources.Load<ChargerConfig>("Weapons/Main_" + id);
                if (config == null)
                {
                    Check($"載入 Main_{id}", false, "先跑 M7 Setup");
                    continue;
                }
                var kit = ScriptableObject.CreateInstance<WeaponKit>();
                kit.Setup(id, id, id, "", config, baseKit.Sub, baseKit.Special, 200f, WeightClass.Mid, "", 0, "", 0, "", 0);
                _loadout.Equip(kit);
                _enemy.GetComponent<WeaponLoadout>().Equip(kit);
                var charger = _loadout.Main as ChargerWeapon;
                yield return Shoot(id, charger);
            }

            _loadout.Equip(baseKit);
            tank.InfiniteInk = false;
            _player.GetComponent<PlayerInputRouter>().ClearOverrideSource();
            Destroy(_enemy.gameObject);
            Destroy(_studio.gameObject);
            Finish();
        }

        private IEnumerator Shoot(string id, ChargerWeapon charger)
        {
            _ground.ClearInk();
            Teleport(_player.transform, new Vector3(-12f, 0.1f, -16f));
            _rig.SetAngles(0f, 6f);
            yield return new WaitForSeconds(0.8f);
            StudioShot($"charger_{id}_idle_side", charger, new Vector3(2.4f, 1.1f, 1.4f), 0.9f, 38f);

            // 蓄一半 → 滿蓄
            _intent.Attack = true;
            while (charger.Charge01 < 0.5f)
            {
                yield return null;
            }
            StudioShot($"charger_{id}_charge_side", charger, new Vector3(2.6f, 1.3f, 0.6f), 1.1f, 40f);
            while (!charger.IsFullyCharged)
            {
                yield return null;
            }
            yield return new WaitForSeconds(0.2f);
            StudioShot($"charger_{id}_full_side", charger, new Vector3(2.6f, 1.3f, 0.6f), 1.1f, 40f);
            StudioShot($"charger_{id}_full_front", charger, new Vector3(-1.4f, 1.5f, 3.0f), 1.1f, 42f);
            LaserShot($"charger_{id}_full_game", charger, Camera.main);
            // 開槍 → 俯視墨線
            _intent.Attack = false;
            yield return new WaitForSeconds(0.06f);
            GameShot($"charger_{id}_fire_game");
            yield return new WaitForSeconds(0.5f);
            WorldShot($"charger_{id}_trail_top", _player.transform.position + new Vector3(0f, 26f, 13f),
                _player.transform.position + new Vector3(0f, 0f, 14f), 60f);

            // 敵人視角:對面 20 m 的狙擊手蓄滿、雷射指著鏡頭(兩隊都看得到的預兆)
            _ground.ClearInk();
            Teleport(_player.transform, new Vector3(-10f, 0.1f, -20f));
            Teleport(_enemy.transform, new Vector3(-10f, 0.1f, 0f));
            yield return new WaitForSeconds(0.4f);
            Vector3 target = _player.transform.position + Vector3.up * 1.2f;
            _enemyAim.From(_enemy.transform, target);
            var enemyCharger = _enemy.GetComponent<WeaponLoadout>().Main as ChargerWeapon;
            _enemyIntent.Attack = true;
            float until = Time.time + 3f;
            while (enemyCharger != null && !enemyCharger.IsFullyCharged && Time.time < until)
            {
                _enemyAim.From(_enemy.transform, target);
                yield return null;
            }
            yield return new WaitForSeconds(0.2f);
            _studio.transform.SetPositionAndRotation(_player.transform.position + new Vector3(0.6f, 1.7f, -0.4f),
                Quaternion.LookRotation(_enemy.transform.position + Vector3.up * 1.1f
                                        - (_player.transform.position + new Vector3(0.6f, 1.7f, -0.4f)), Vector3.up));
            _studio.fieldOfView = 34f;
            LaserShot($"charger_{id}_enemy_view", enemyCharger, _studio);
            // 對面的狙擊手放掉(打到本機玩家不要緊:截圖探針)
            var health = _player.GetComponent<InklingHealth>();
            health.Invulnerable = true;
            _enemyIntent.Attack = false;
            yield return new WaitForSeconds(0.5f);
            health.Invulnerable = false;
            Teleport(_enemy.transform, new Vector3(-22f, 0.1f, 22f));

            // 儲存蓄力:潛在自家墨裡、頭上白圈
            Vector3 spot = new Vector3(-16f, 0.1f, -8f);
            InkPainter.PaintSphere(new Vector3(spot.x, 0f, spot.z), 3.5f, 1f, _player.GetComponent<Inkling>().InkSource(false));
            Teleport(_player.transform, spot);
            _rig.SetAngles(90f, 6f);
            yield return new WaitForSeconds(0.5f);
            _intent.Attack = true;
            while (!charger.IsFullyCharged)
            {
                yield return null;
            }
            _intent.Squid = true;
            yield return new WaitForSeconds(0.5f);
            WorldShot($"charger_{id}_keep_ring", spot + new Vector3(-2.6f, 2.2f, -2.6f), spot + Vector3.up * 0.8f, 40f);
            Check($"{id} 儲存蓄力白圈", charger.IsKeepingCharge && charger.Laser != null && charger.Laser.RingVisible,
                $"保留={charger.IsKeepingCharge} 白圈={charger.Laser?.RingVisible}");
            _intent.Attack = false;
            _intent.Squid = false;
            yield return new WaitForSeconds(0.5f);
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

        // 拍一張、再關掉雷射拍一張:差異像素 = 雷射(線 + 光點)在畫面上的面積
        private void LaserShot(string shotName, ChargerWeapon charger, Camera camera)
        {
            if (camera == null || charger == null || charger.Laser == null)
            {
                Check($"{shotName} 雷射可見", false, "沒有相機 / 狙擊槍 / 雷射");
                return;
            }
            Texture2D shot = ShotCapture.Capture(camera, shotName);
            Renderer[] renderers = charger.Laser.GetComponentsInChildren<Renderer>(false);
            var states = new bool[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                states[i] = renderers[i].enabled;
                renderers[i].enabled = false;
            }
            Texture2D hidden = ShotCapture.Render(camera);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].enabled = states[i];
            }
            int diff = ShotCapture.DiffPixels(shot, hidden);
            Check($"{shotName} 雷射可見", diff > 150, $"雷射像素差={diff} 雷射顯示={charger.Laser.LaserVisible}");
            Destroy(hidden);
            Destroy(shot);
        }

        // 相對角色(Visual 朝向)的鏡頭;關掉狙擊槍 renderer 再拍一次,差異像素 = 槍在畫面上的面積
        private void StudioShot(string shotName, ChargerWeapon charger, Vector3 localOffset, float lookHeight, float fov)
        {
            Transform visual = _player.transform.Find("Visual");
            Quaternion facing = Quaternion.Euler(0f, visual != null ? visual.eulerAngles.y : 0f, 0f);
            Vector3 origin = _player.transform.position;
            Vector3 position = origin + facing * localOffset;
            _studio.transform.SetPositionAndRotation(position,
                Quaternion.LookRotation(origin + Vector3.up * lookHeight - position, Vector3.up));
            _studio.fieldOfView = fov;
            Texture2D shot = ShotCapture.Capture(_studio, shotName);
            ChargerModel model = charger != null ? charger.Model : null;
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
                Check($"{shotName} 狙擊槍可見", diff > 1200,
                    $"槍像素差={diff} 狀態={charger.State} 蓄力={charger.Charge01:0.00}");
                Destroy(hidden);
            }
            Destroy(shot);
        }
    }
}

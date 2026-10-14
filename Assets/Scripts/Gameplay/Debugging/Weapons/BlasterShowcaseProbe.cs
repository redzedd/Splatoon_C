using System.Collections;
using SplatoonC.Gameplay.CameraRig;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 爆破槍截圖探針:試射場依序換上 Blaster / Luna Blaster,拍 拿著 / 預備蓄勢 / 出膛瞬間(墨彈)/ 空爆瞬間(爆風閃光)/
    // 遊戲鏡頭 / 俯視的塗墨(腳前、沿路墨滴、空爆大圓;撞地小圓)。PNG 存在 <專案>/Logs/shots/blaster_<id>_*.png,
    // 給人眼(與 Read 工具)逐張檢查。斷言只做健全性:攝影棚鏡頭「關掉槍模 renderer 前後」差異像素要夠多(槍模真的畫出來了)。
    [AutoTest(Scene = AutoTestAttribute.TrainingScene, Timeout = 180f, Order = 964)]
    public sealed class BlasterShowcaseProbe : AutoTestBase
    {
        private static readonly string[] Ids = { "blaster", "luna_blaster" };

        private Camera _studio;
        private GameObject _player;
        private ThirdPersonCameraRig _rig;
        private ScriptedIntent _intent;
        private WeaponLoadout _loadout;
        private PaintableSurface _ground;

        public static void Run()
        {
            Launch<BlasterShowcaseProbe>();
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
            var go = new GameObject("BlasterStudioCamera");
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
                var config = Resources.Load<BlasterConfig>("Weapons/Main_" + id);
                if (config == null)
                {
                    Check($"載入 Main_{id}", false, "先跑 M7 Setup");
                    continue;
                }
                var kit = ScriptableObject.CreateInstance<WeaponKit>();
                kit.Setup(id, id, id, "", config, baseKit.Sub, baseKit.Special, 190f, WeightClass.Mid, "", 0, "", 0, "", 0);
                _loadout.Equip(kit);
                yield return Shoot(id, _loadout.Main as BlasterWeapon);
            }

            _loadout.Equip(baseKit);
            tank.InfiniteInk = false;
            _player.GetComponent<PlayerInputRouter>().ClearOverrideSource();
            Destroy(_studio.gameObject);
            _ground.ClearInk();
            Finish();
        }

        private IEnumerator Shoot(string id, BlasterWeapon weapon)
        {
            if (weapon == null)
            {
                Check($"{id} 換裝", false, "主武器不是 BlasterWeapon");
                yield break;
            }
            _ground.ClearInk();
            Teleport(_player.transform, new Vector3(-12f, 0.1f, -22f));
            _rig.SetAngles(0f, 6f);
            yield return new WaitForSeconds(0.8f);
            StudioShot($"blaster_{id}_idle", weapon, new Vector3(1.9f, 1.3f, 1.6f), 1.0f, 34f);

            // 預備蓄勢(按下 8F 後)→ 出膛瞬間 → 空爆瞬間
            int shots = weapon.ShotsFired;
            int bursts = weapon.BurstCount;
            _intent.Attack = true;
            yield return null;
            _intent.Attack = false;
            for (int i = 0; i < 8; i++)
            {
                yield return null;
            }
            StudioShot($"blaster_{id}_windup", weapon, new Vector3(1.6f, 1.25f, 0.9f), 1.1f, 34f);
            while (weapon.ShotsFired == shots)
            {
                yield return null;
            }
            yield return null;
            yield return null;
            StudioShot($"blaster_{id}_fire", weapon, new Vector3(2.4f, 1.4f, 1.4f), 1.1f, 40f);
            // 飛行中(出膛後約 6F,墨團在 5~6 m 處):從側面看整條彈道
            for (int i = 0; i < 4; i++)
            {
                yield return null;
            }
            WorldShot($"blaster_{id}_flight", _player.transform.position + new Vector3(7f, 1.6f, 4f),
                _player.transform.position + new Vector3(0f, 1f, 4.5f), 50f);
            while (weapon.BurstCount == bursts)
            {
                yield return null;
            }
            yield return null;
            Vector3 burst = weapon.LastBurst.Point;
            WorldShot($"blaster_{id}_burst", _player.transform.position + new Vector3(6.5f, 3.2f, -1.5f),
                (burst + _player.transform.position) * 0.5f + Vector3.up * 0.6f, 55f);
            GameShot($"blaster_{id}_game");
            yield return new WaitForSeconds(0.6f);
            WorldShot($"blaster_{id}_paint_air_top", _player.transform.position + new Vector3(0f, 17f, 5f),
                _player.transform.position + new Vector3(0f, 0f, 5.5f), 60f);

            // 撞地形:瞄 5 m 外的地面
            _ground.ClearInk();
            Teleport(_player.transform, new Vector3(-18f, 0.1f, -22f));
            _rig.SetAngles(0f, 28f);
            yield return new WaitForSeconds(1f);
            bursts = weapon.BurstCount;
            _intent.Attack = true;
            yield return null;
            _intent.Attack = false;
            float until = Time.time + 1.5f;
            while (weapon.BurstCount == bursts && Time.time < until)
            {
                yield return null;
            }
            yield return new WaitForSeconds(0.4f);
            WorldShot($"blaster_{id}_paint_terrain_top", _player.transform.position + new Vector3(0f, 15f, 4f),
                _player.transform.position + new Vector3(0f, 0f, 4.5f), 60f);
            yield return new WaitForSeconds(0.8f);
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

        // 相對角色(Visual 朝向)的鏡頭;關掉槍模 renderer 再拍一次,差異像素 = 槍模在畫面上的面積
        private void StudioShot(string shotName, BlasterWeapon weapon, Vector3 localOffset, float lookHeight, float fov)
        {
            Transform visual = _player.transform.Find("Visual");
            Quaternion facing = Quaternion.Euler(0f, visual != null ? visual.eulerAngles.y : 0f, 0f);
            Vector3 origin = _player.transform.position;
            Vector3 position = origin + facing * localOffset;
            _studio.transform.SetPositionAndRotation(position,
                Quaternion.LookRotation(origin + Vector3.up * lookHeight - position, Vector3.up));
            _studio.fieldOfView = fov;
            Texture2D shot = ShotCapture.Capture(_studio, shotName);
            BlasterGunModel gun = weapon.Gun;
            if (gun != null && gun.Root != null)
            {
                Renderer[] renderers = gun.Root.GetComponentsInChildren<Renderer>(false);
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
                Check($"{shotName} 槍模可見", diff > 1500,
                    $"槍模像素差={diff} 預備={weapon.WindupProgress:0.00} 蓄勢={(cv != null ? cv.GunStanceWindup : 0f):0.00} " +
                    $"後座力={(cv != null ? cv.Recoil : 0f):0.00}");
                Destroy(hidden);
            }
            else
            {
                Check($"{shotName} 槍模可見", false, "沒有槍模");
            }
            Destroy(shot);
        }
    }
}

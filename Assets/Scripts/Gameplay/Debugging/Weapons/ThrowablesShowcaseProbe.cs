using System.Collections;
using System.Collections.Generic;
using SplatoonC.Gameplay.Combat;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 截圖探針(不斷言):試射場依序換上 Torpedo / Fizzy Bomb / Point Sensor / Angle Shooter,拍 手持模型 + 預覽、
    // 場上狀態(Torpedo 變身與衝刺、Fizzy 飛行與爆炸、Point Sensor 感應球、Angle Shooter 線段)與塗地,
    // 輸出 Logs/shots/throwables_<id>_<n>.png 與拼圖 throwables_sheet.png。手動執行:ThrowablesShowcaseProbe.Run()。
    public sealed class ThrowablesShowcaseProbe : ThrowableTestBase
    {
        private static readonly string[] Ids = { "torpedo", "fizzy_bomb", "point_sensor", "angle_shooter" };

        private readonly List<Texture2D> _frames = new List<Texture2D>();
        private Camera _camera;

        public static void Run()
        {
            Launch<ThrowablesShowcaseProbe>();
        }

        private IEnumerator Start()
        {
            if (!Prepare("torpedo", out TorpedoConfig _))
            {
                yield break;
            }
            var camGo = new GameObject("ThrowablesShowcaseCamera");
            _camera = camGo.AddComponent<Camera>();
            _camera.enabled = false;
            _camera.fieldOfView = 40f;
            Tank.InfiniteInk = true;

            for (int i = 0; i < Ids.Length; i++)
            {
                var config = Resources.Load<SubWeaponConfig>("Weapons/Sub_" + Ids[i]);
                var kit = ScriptableObject.CreateInstance<WeaponKit>();
                kit.Setup("probe_" + Ids[i], config.DisplayName, Ids[i], "probe", BaseKit.Main, config, BaseKit.Special,
                    BaseKit.SpecialPoints, BaseKit.Weight, "a", 1, "b", 1, "c", 1);
                Loadout.Equip(kit);
                yield return WaitSeconds(0.6f);
                yield return Showcase(i, Ids[i]);
            }

            if (_frames.Count > 0)
            {
                Texture2D sheet = ShotCapture.Stitch(_frames.ToArray(), 4);
                ShotCapture.Save(sheet, "throwables_sheet");
                Destroy(sheet);
            }
            foreach (Texture2D frame in _frames)
            {
                Destroy(frame);
            }
            Destroy(camGo);
            Cleanup();
            Finish();
        }

        private IEnumerator Showcase(int index, string id)
        {
            Vector3 start = new Vector3(-20f + index * 11f, 0.1f, -22f);
            yield return MoveTo(start, Vector3.forward);
            TargetDummy dummy = id == "torpedo" || id == "fizzy_bomb" || id == "angle_shooter"
                ? SpawnDummy(start + new Vector3(1.5f, -0.1f, 13f))
                : null;
            Physics.SyncTransforms();
            AimYawPitch(Vector3.forward, id == "angle_shooter" ? -2f : -6f);
            Intent.Sub = true;
            yield return WaitSeconds(id == "fizzy_bomb" ? 2.2f : 0.45f);
            // 1:手持模型特寫
            Vector3 hand = PlayerGo.transform.position + Vector3.up * 1.1f;
            Shoot(hand + new Vector3(-1.3f, 0.5f, 1.6f), hand + Vector3.left * 0.1f, id + "_1_held");
            // 2:預覽(角色後上方)
            Shoot(PlayerGo.transform.position + new Vector3(2.5f, 3.5f, -4f), PlayerGo.transform.position + Vector3.forward * 7f,
                id + "_2_preview");
            yield return Release();
            float[] waits;
            switch (id)
            {
                case "torpedo":
                    waits = new[] { 0.35f, 0.55f };
                    break;
                case "fizzy_bomb":
                    waits = new[] { 0.25f, 0.8f };
                    break;
                case "point_sensor":
                    waits = new[] { 0.9f, 0.5f };
                    break;
                default:
                    waits = new[] { 0.05f, 0.4f };
                    break;
            }
            for (int k = 0; k < 2; k++)
            {
                yield return WaitSeconds(waits[k]);
                SimulatedBomb live = LiveOf<SimulatedBomb>();
                Vector3 target = live != null ? live.transform.position : PlayerGo.transform.position + Vector3.forward * 8f;
                if (id == "angle_shooter")
                {
                    target = PlayerGo.transform.position + Vector3.forward * 10f;
                    Shoot(target + new Vector3(6f, 5f, -6f), target, id + "_" + (3 + k) + "_live");
                }
                else if (id == "point_sensor")
                {
                    Shoot(target + new Vector3(7f, 6f, -8f), target, id + "_" + (3 + k) + "_live");
                }
                else
                {
                    Shoot(target + new Vector3(1.6f, 1.1f, -1.8f), target, id + "_" + (3 + k) + "_live");
                }
            }
            yield return WaitSeconds(2.5f);
            Vector3 center = PlayerGo.transform.position + Vector3.forward * 10f;
            Shoot(center + new Vector3(0f, 17f, -6f), center, id + "_5_paint");
            if (dummy != null)
            {
                Remove(dummy);
            }
        }

        private void Shoot(Vector3 from, Vector3 lookAt, string name)
        {
            _camera.transform.position = from;
            _camera.transform.rotation = Quaternion.LookRotation(lookAt - from, Vector3.up);
            Texture2D texture = ShotCapture.Render(_camera, 640, 360);
            ShotCapture.Save(texture, "throwables_" + name);
            _frames.Add(texture);
        }
    }
}

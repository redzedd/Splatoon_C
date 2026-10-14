using System.Collections;
using System.Collections.Generic;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Painting;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 截圖探針(不斷言):試射場依序換上 Suction / Burst / Curling / Autobomb,拍 手持模型 + 預覽、飛行/黏住/滑行/追蹤中、
    // 爆炸後塗地,輸出 Logs/shots/bombs_<id>_<n>.png 與拼圖 bombs_sheet.png。手動執行:BombsShowcaseProbe.Run()。
    public sealed class BombsShowcaseProbe : BombTestBase
    {
        private static readonly string[] Ids = { "suction_bomb", "burst_bomb", "curling_bomb", "autobomb" };

        private readonly List<Texture2D> _frames = new List<Texture2D>();
        private Camera _camera;

        public static void Run()
        {
            Launch<BombsShowcaseProbe>();
        }

        private IEnumerator Start()
        {
            if (!Prepare("suction_bomb", out SuctionBombConfig _))
            {
                yield break;
            }
            var camGo = new GameObject("BombsShowcaseCamera");
            _camera = camGo.AddComponent<Camera>();
            _camera.enabled = false;
            _camera.fieldOfView = 38f;
            Tank.InfiniteInk = true;

            for (int i = 0; i < Ids.Length; i++)
            {
                var config = Resources.Load<SubWeaponConfig>("Weapons/Sub_" + Ids[i]);
                var kit = ScriptableObject.CreateInstance<WeaponKit>();
                kit.Setup("probe_" + Ids[i], config.DisplayName, Ids[i], "probe", BaseKit.Main, config, BaseKit.Special,
                    BaseKit.SpecialPoints, BaseKit.Weight, "a", 1, "b", 1, "c", 1);
                Loadout.Equip(kit);
                yield return Showcase(Ids[i], config);
            }

            if (_frames.Count > 0)
            {
                Texture2D sheet = ShotCapture.Stitch(_frames.ToArray(), 4);
                ShotCapture.Save(sheet, "bombs_sheet");
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

        private IEnumerator Showcase(string id, SubWeaponConfig config)
        {
            Vector3 start = new Vector3(-14f + System.Array.IndexOf(Ids, id) * 6f, 0.1f, -20f);
            yield return MoveTo(start, Vector3.forward);
            AimYawPitch(Vector3.forward, id == "curling_bomb" ? -15f : -8f);
            Intent.Sub = true;
            yield return WaitSeconds(id == "curling_bomb" ? 0.6f : 0.4f);
            // 1:手持模型特寫(角色右前方)
            Vector3 hand = PlayerGo.transform.position + Vector3.up * 1.1f;
            Shoot(hand + new Vector3(-1.3f, 0.5f, 1.6f), hand + Vector3.left * 0.1f, id + "_1_held");
            // 2:預覽弧線(角色後上方)
            Shoot(PlayerGo.transform.position + new Vector3(2.5f, 3.5f, -4f), PlayerGo.transform.position + Vector3.forward * 6f,
                id + "_2_preview");
            yield return Release();
            float[] waits = id == "burst_bomb" ? new[] { 0.12f, 0.1f } :
                id == "curling_bomb" ? new[] { 0.6f, 1.2f } :
                id == "autobomb" ? new[] { 0.9f, 0.5f } : new[] { 0.9f, 0.9f };
            // 3、4:場上的炸彈(自動追著最近一顆)
            for (int k = 0; k < 2; k++)
            {
                yield return WaitSeconds(waits[k]);
                SimulatedBomb bomb = FindBomb();
                Vector3 target = bomb != null ? bomb.transform.position : PlayerGo.transform.position + Vector3.forward * 8f;
                Shoot(target + new Vector3(1.2f, 0.9f, -1.4f), target, id + "_" + (3 + k) + "_live");
            }
            if (id == "autobomb")
            {
                // 追蹤展示:在落點前方放個假人
                TargetDummy dummy = SpawnDummy(PlayerGo.transform.position + new Vector3(0f, -0.1f, 14f));
                AimYawPitch(Vector3.forward, -12f);
                Intent.Sub = true;
                yield return WaitSeconds(0.3f);
                yield return Release();
                yield return WaitSeconds(1.0f);
                SimulatedBomb chaser = FindBomb();
                if (chaser != null)
                {
                    Vector3 p = chaser.transform.position;
                    Shoot(p + new Vector3(2.5f, 1.8f, -1f), p + Vector3.forward * 1.5f, id + "_5_chase");
                }
                yield return WaitSeconds(2.5f);
                Remove(dummy);
            }
            yield return WaitSeconds(3.2f);
            // 爆炸後塗地(俯視)
            Vector3 center = PlayerGo.transform.position + Vector3.forward * 9f;
            Shoot(center + new Vector3(0f, 16f, -6f), center, id + "_6_paint");
        }

        private SimulatedBomb FindBomb()
        {
            foreach (SimulatedBomb bomb in FindObjectsByType<SimulatedBomb>(FindObjectsSortMode.None))
            {
                if (bomb.IsActive)
                {
                    return bomb;
                }
            }
            return null;
        }

        private void Shoot(Vector3 from, Vector3 lookAt, string name)
        {
            _camera.transform.position = from;
            _camera.transform.rotation = Quaternion.LookRotation(lookAt - from, Vector3.up);
            Texture2D texture = ShotCapture.Render(_camera, 640, 360);
            ShotCapture.Save(texture, "bombs_" + name);
            _frames.Add(texture);
        }
    }
}

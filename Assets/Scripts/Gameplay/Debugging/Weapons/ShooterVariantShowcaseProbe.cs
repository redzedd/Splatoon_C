using System.Collections;
using System.IO;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 截圖探針(不斷言):試射場依序換上 Splattershot 與四把變體,各拍槍模側面特寫與開火中的畫面
    //(墨彈、塗地),輸出 Logs/shots/shooter_<id>_gun.png / _fire.png。批次模式也能跑(自建相機 → RenderTexture)。
    public sealed class ShooterVariantShowcaseProbe : MonoBehaviour
    {
        private static readonly string[] VariantIds = { "", "splattershot_jr", "52_gal", "n_zap_85", "splattershot_pro" };

        public static void Run()
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("[PROBE] 需在 Play mode 中執行");
                return;
            }
            new GameObject("ShooterVariantShowcaseProbe").AddComponent<ShooterVariantShowcaseProbe>();
        }

        private IEnumerator Start()
        {
            GameObject player = GameObject.Find("Player");
            var loadout = player != null ? player.GetComponent<WeaponLoadout>() : null;
            var tank = player != null ? player.GetComponent<PlayerInkTank>() : null;
            var inkling = player != null ? player.GetComponent<Inkling>() : null;
            Transform visual = player != null ? player.transform.Find("Visual") : null;
            if (loadout == null || loadout.Kit == null || tank == null || inkling == null || visual == null)
            {
                Debug.LogError("[PROBE] 前置失敗:找不到 Player/WeaponLoadout/Visual");
                Debug.Log("[PROBE] DONE");
                Destroy(gameObject);
                yield break;
            }
            string folder = Path.Combine(Application.dataPath, "..", "Logs", "shots");
            Directory.CreateDirectory(folder);
            var rt = new RenderTexture(960, 540, 24);
            var camGo = new GameObject("ShowcaseCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.fieldOfView = 40f;
            cam.targetTexture = rt;
            var tex = new Texture2D(960, 540, TextureFormat.RGB24, false);

            WeaponKit baseKit = loadout.Kit;
            var intent = ScriptedIntent.Drive(player.transform);
            tank.InfiniteInk = true;

            for (int v = 0; v < VariantIds.Length; v++)
            {
                string id = VariantIds[v];
                WeaponKit kit = baseKit;
                if (id.Length > 0)
                {
                    var config = Resources.Load<WeaponConfig>("Weapons/Main_" + id);
                    if (config == null)
                    {
                        Debug.LogError($"[PROBE] 找不到 Main_{id}");
                        continue;
                    }
                    kit = ScriptableObject.CreateInstance<WeaponKit>();
                    kit.Setup(id, id, id, "", config, baseKit.Sub, baseKit.Special, 200f, WeightClass.Mid,
                        "", 0, "", 0, "", 0);
                }
                loadout.Equip(kit);
                var controller = player.GetComponent<CharacterController>();
                controller.enabled = false;
                player.transform.position = new Vector3(-14f + v * 0.01f, 0.1f, -18f);
                controller.enabled = true;
                visual.rotation = Quaternion.identity;
                for (int i = 0; i < 20; i++)
                {
                    yield return null;
                }

                // 槍模特寫:從右側稍前方看槍身
                Vector3 gun = visual.TransformPoint(new Vector3(0.52f, 1.05f, 0.35f));
                camGo.transform.position = gun + new Vector3(1.3f, 0.35f, 0.25f);
                camGo.transform.LookAt(gun);
                Snap(cam, rt, tex, Path.Combine(folder, $"shooter_{(id.Length > 0 ? id : "splattershot")}_gun.png"));

                // 開火中:從右後上方看彈道與塗地
                intent.Attack = true;
                float end = Time.time + 0.45f;
                while (Time.time < end)
                {
                    visual.rotation = Quaternion.identity;
                    yield return null;
                }
                camGo.transform.position = player.transform.position + new Vector3(3.5f, 3.2f, -4.5f);
                camGo.transform.LookAt(player.transform.position + new Vector3(0f, 0.5f, 8f));
                Snap(cam, rt, tex, Path.Combine(folder, $"shooter_{(id.Length > 0 ? id : "splattershot")}_fire.png"));
                intent.Attack = false;
                yield return new WaitForSeconds(0.5f);
            }

            loadout.Equip(baseKit);
            tank.InfiniteInk = false;
            player.GetComponent<PlayerInputRouter>().ClearOverrideSource();
            Destroy(camGo);
            Debug.Log($"[PROBE] 截圖輸出到 {folder}");
            Debug.Log("[AUTOTEST] DONE passed=0 failed=0");
            Destroy(gameObject);
        }

        private static void Snap(Camera cam, RenderTexture rt, Texture2D tex, string path)
        {
            cam.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Debug.Log($"[PROBE] 截圖 {path}");
        }
    }
}

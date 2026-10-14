using SplatoonC.Core;
using System.Collections;
using System.IO;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Painting;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 墨面材質迭代用的固定基準截圖(不斷言):同一組墨漬、同一個機位,改 shader 前後可直接疊圖比對。
    // 決定性來源:先清空地面 ink RT,再用固定亂數種子鋪墨——否則波瓣相位每次不同,兩張圖無法比較。
    // 機位用自建相機而非 ThirdPersonCameraRig:rig 的距離/平滑/瞄準高度都是遊戲手感參數,
    // 拿來當量測基準會隨手感調整而漂移,且它的 SmoothDamp 還要等收斂。
    // 警告:清 RT 只清視覺,歸屬網格不清,跑完這支探針的 session 不要再拿去驗證烏賊/覆蓋率。
    public sealed class InkSurfaceShowcaseProbe : MonoBehaviour
    {
        private const int PaintSeed = 20260909;
        private static readonly Vector3 PatchCenter = new Vector3(-10f, 0f, -10f);

        private string _tag = "base";

        public static void Run()
        {
            Run("base");
        }

        public static void Run(string tag)
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("[PROBE] 需在 Play mode 中執行");
                return;
            }
            var host = new GameObject("InkSurfaceShowcaseProbe");
            host.AddComponent<InkSurfaceShowcaseProbe>()._tag = string.IsNullOrEmpty(tag) ? "base" : tag;
        }

        private IEnumerator Start()
        {
            GameObject player = GameObject.Find("Player");
            var shooter = player != null ? player.GetComponent<InkShooter>() : null;
            Camera gameCamera = Camera.main;
            GameObject groundGo = GameObject.Find("Ground");
            var ground = groundGo != null ? groundGo.GetComponent<PaintableSurface>() : null;
            if (shooter == null || gameCamera == null || ground == null)
            {
                Debug.LogError("[PROBE] 前置失敗:找不到 Player/InkShooter/Main Camera/Ground");
                Destroy(gameObject);
                yield break;
            }

            Directory.CreateDirectory("Builds/Screenshots");
            ClearInkMap(ground);
            PaintReferencePatch(ground, shooter.Config);

            // 玩家膠囊會擋住墨團,截圖期間整個關掉(結束還原)
            Renderer[] playerRenderers = player.GetComponentsInChildren<Renderer>(true);
            var wasVisible = new bool[playerRenderers.Length];
            for (int i = 0; i < playerRenderers.Length; i++)
            {
                wasVisible[i] = playerRenderers[i].enabled;
                playerRenderers[i].enabled = false;
            }

            var probeCamGo = new GameObject("InkSurfaceProbeCam");
            var probeCam = probeCamGo.AddComponent<Camera>();
            probeCam.CopyFrom(gameCamera);
            gameCamera.enabled = false;

            // specular 機位:主光 euler(50,-30) 在地面的鏡射方向 → yaw 150 / pitch 45。
            // 任何高光模型都在這角度最強;目前無高光,所以這張與 shape 只差視角。
            yield return Capture(probeCamGo, 150f, 45f, 15f, "specular");
            // shape 機位:高俯角看整片墨的輪廓、邊緣過渡與細滴接縫
            yield return Capture(probeCamGo, 0f, 60f, 17f, "shape");

            gameCamera.enabled = true;
            Destroy(probeCamGo);
            for (int i = 0; i < playerRenderers.Length; i++)
            {
                playerRenderers[i].enabled = wasVisible[i];
            }
            Debug.Log($"[PROBE] DONE 基準截圖完成 tag={_tag}(本 session 的墨歸屬已與畫面不一致,勿再跑覆蓋率測試)");
            Destroy(gameObject);
        }

        private static void ClearInkMap(PaintableSurface surface)
        {
            RenderTexture map = surface.InkMap;
            if (map == null)
            {
                return;
            }
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = map;
            GL.Clear(false, true, Color.clear);
            RenderTexture.active = previous;
        }

        // 固定種子 + 固定座標:大團(看實心與邊緣)、細滴列(看薄墨與相接處)、留白對照
        private static void PaintReferencePatch(PaintableSurface surface, WeaponConfig config)
        {
            Random.State previous = Random.state;
            Random.InitState(PaintSeed);

            for (int i = 0; i < 6; i++)
            {
                float angle = i * Mathf.PI * 2f / 6f;
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 1.4f;
                surface.Paint(PatchCenter + offset, config.SplatRadius, Team.Alpha, config.SplatHardness);
            }
            // 細滴排在大團左側同一條 Z 線上;間距 2.2 略小於滴直徑 2.4,剛好看得到接縫
            for (int i = 0; i < 4; i++)
            {
                Vector3 point = PatchCenter + new Vector3(-10.5f + i * 2.2f, 0f, 0f);
                surface.Paint(point, config.DripRadius, Team.Alpha, config.SplatHardness);
            }

            Random.state = previous;
        }

        private IEnumerator Capture(GameObject camGo, float yawDeg, float pitchDeg, float distance, string shotName)
        {
            Quaternion rotation = Quaternion.Euler(pitchDeg, yawDeg, 0f);
            Vector3 viewDir = rotation * Vector3.forward;
            camGo.transform.SetPositionAndRotation(PatchCenter - viewDir * distance, rotation);
            yield return null;

            string path = $"Builds/Screenshots/ink_surface_{_tag}_{shotName}.png";
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log($"[PROBE] 截圖 {path}");
            yield return null;
            yield return null;
            yield return null;
        }
    }
}

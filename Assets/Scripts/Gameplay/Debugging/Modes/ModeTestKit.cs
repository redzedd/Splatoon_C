using System.Collections;
using System.Collections.Generic;
using System.IO;
using SplatoonC.Core;
using SplatoonC.Core.Match;
using SplatoonC.Core.Match.Ranked;
using SplatoonC.Gameplay.CameraRig;
using SplatoonC.Gameplay.Match;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SplatoonC.Gameplay.Debugging
{
    // 蠻頹模式 AutoTest 共用工具:以指定模式重載對戰場景、凍結全員輸入、以真實塗墨路徑把區域塗到指定比例、
    // 含 HUD 的截圖(Overlay 畫布暫時改成相機空間)。
    public static class ModeTestKit
    {
        // 以 mode 重新載入目前的對戰場景(走 MatchSettings → MatchController.Awake 的正式路徑);呼叫端需 DontDestroyOnLoad
        public static IEnumerator ReloadWithMode(MatchMode mode, float timeout = 30f)
        {
            MatchSettings.Mode = mode;
            string scene = SceneManager.GetActiveScene().name;
            MatchController old = MatchController.Instance;
            SceneManager.LoadScene(scene);
            float end = Time.realtimeSinceStartup + timeout;
            while (Time.realtimeSinceStartup < end && (MatchController.Instance == null || MatchController.Instance == old
                                                       || MatchController.Instance.Roster.Count == 0))
            {
                yield return null;
            }
            // 等一幀讓 Start 系列(HUD、BotDirector、目標生成)跑完
            yield return null;
            yield return null;
        }

        // 所有角色輸入鎖住(bot 大腦照跑但按不下任何鍵):規則測試不被 bot 的墨干擾
        public static void LockAll(MatchController match, bool locked)
        {
            foreach (Inkling inkling in match.Roster)
            {
                var router = inkling != null ? inkling.GetComponent<PlayerInputRouter>() : null;
                if (router != null)
                {
                    router.InputLocked = locked;
                }
            }
        }

        // 區域內的塗墨點(1m 格,依列掃描;y = 區域地面平均高度 + 5cm)
        public static List<Vector3> PaintPoints(SplatZone zone, float spacing = 1f)
        {
            var points = new List<Vector3>();
            float minX = float.MaxValue;
            float maxX = float.MinValue;
            float minZ = float.MaxValue;
            float maxZ = float.MinValue;
            foreach (Vector2 v in zone.Polygon)
            {
                minX = Mathf.Min(minX, v.x);
                maxX = Mathf.Max(maxX, v.x);
                minZ = Mathf.Min(minZ, v.y);
                maxZ = Mathf.Max(maxZ, v.y);
            }
            for (float z = minZ + spacing * 0.5f; z < maxZ; z += spacing)
            {
                for (float x = minX + spacing * 0.5f; x < maxX; x += spacing)
                {
                    var p = new Vector3(x, zone.Center.y + 0.05f, z);
                    if (zone.Contains(p))
                    {
                        points.Add(p);
                    }
                }
            }
            return points;
        }

        // 以 team 的墨沿 points 依序塗(每幀 perFrame 發),直到 predicate 成立;回傳塗了幾發
        public static IEnumerator PaintUntil(SplatZone zone, List<Vector3> points, Team team, System.Func<bool> done,
            int perFrame, float radius, System.Action<int> onPainted)
        {
            var source = new InkSource(team);
            int painted = 0;
            for (int i = 0; i < points.Count && !done(); i++)
            {
                InkPainter.PaintSphere(points[i], radius, 1f, source);
                painted++;
                if (painted % perFrame == 0)
                {
                    onPainted?.Invoke(painted);
                    yield return null;
                }
            }
            onPainted?.Invoke(painted);
        }

        // 從區域斜上方俯瞰(相機暫時脫離跟隨),含 HUD 截圖;回傳檔案路徑
        public static IEnumerator ShotZone(SplatZone zone, string name, bool withHud, System.Action<string> saved)
        {
            Camera cam = Camera.main;
            if (cam == null || zone == null)
            {
                yield break;
            }
            var rig = cam.GetComponent<ThirdPersonCameraRig>();
            bool rigWasEnabled = rig != null && rig.enabled;
            if (rig != null)
            {
                rig.enabled = false;
            }
            Vector3 previousPos = cam.transform.position;
            Quaternion previousRot = cam.transform.rotation;
            Vector3 eye = zone.Center + new Vector3(-14f, 15f, -19f);
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(zone.Center + Vector3.up * 0.5f - eye));
            yield return Capture(cam, name, withHud, saved);
            cam.transform.SetPositionAndRotation(previousPos, previousRot);
            if (rig != null)
            {
                rig.enabled = rigWasEnabled;
            }
        }

        // 目前相機畫面(含 HUD)截圖
        public static IEnumerator Capture(Camera cam, string name, bool withHud, System.Action<string> saved)
        {
            var switched = new List<Canvas>();
            if (withHud)
            {
                foreach (Canvas canvas in Object.FindObjectsByType<Canvas>())
                {
                    if (canvas.isRootCanvas && canvas.renderMode == RenderMode.ScreenSpaceOverlay && canvas.isActiveAndEnabled)
                    {
                        canvas.renderMode = RenderMode.ScreenSpaceCamera;
                        canvas.worldCamera = cam;
                        canvas.planeDistance = cam.nearClipPlane + 0.05f;
                        switched.Add(canvas);
                    }
                }
            }
            var rt = new RenderTexture(ShotCapture.Width, ShotCapture.Height, 24, RenderTextureFormat.ARGB32);
            RenderTexture previousTarget = cam.targetTexture;
            cam.targetTexture = rt;
            yield return null;
            foreach (Canvas canvas in switched)
            {
                foreach (Graphic graphic in canvas.GetComponentsInChildren<Graphic>())
                {
                    graphic.SetAllDirty();
                }
            }
            Canvas.ForceUpdateCanvases();
            cam.Render();
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture.active = rt;
            var texture = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            texture.Apply();
            RenderTexture.active = previousActive;
            string path = Path.Combine(ShotCapture.Folder, name + ".png");
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Debug.Log($"[SHOT] {path}");
            Object.Destroy(texture);
            cam.targetTexture = previousTarget;
            foreach (Canvas canvas in switched)
            {
                if (canvas != null)
                {
                    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                }
            }
            rt.Release();
            Object.Destroy(rt);
            saved?.Invoke(path);
        }

        public static IEnumerator WaitPhase(MatchController match, MatchPhase phase, float timeout)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (match != null && match.Phase < phase && Time.realtimeSinceStartup < end)
            {
                yield return null;
            }
        }
    }
}

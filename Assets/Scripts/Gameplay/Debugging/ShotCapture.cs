using System.IO;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 截圖工具(探針用,非熱路徑):把相機畫進 RenderTexture 再讀回存 PNG。
    // 批次模式沒有 Game view,ScreenCapture 不能用;手動 Camera.Render 在 URP 也會走完整管線(含後製)。
    public static class ShotCapture
    {
        public const int Width = 1280;
        public const int Height = 720;

        public static string Folder
        {
            get
            {
                string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "shots"));
                Directory.CreateDirectory(folder);
                return folder;
            }
        }

        // 畫一張並讀回像素(呼叫端負責 Object.Destroy 回傳的 Texture2D)
        public static Texture2D Render(Camera camera, int width = Width, int height = Height)
        {
            var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.antiAliasing = 4;
            RenderTexture previousTarget = camera.targetTexture;
            float previousAspect = camera.aspect;
            camera.targetTexture = rt;
            camera.aspect = width / (float)height;
            camera.Render();
            camera.targetTexture = previousTarget;
            camera.aspect = previousAspect;
            camera.ResetAspect();

            RenderTexture previousActive = RenderTexture.active;
            RenderTexture.active = rt;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texture.Apply(false);
            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(rt);
            return texture;
        }

        public static string Save(Texture2D texture, string name)
        {
            string path = Path.Combine(Folder, name + ".png");
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Debug.Log($"[SHOT] {path}");
            return path;
        }

        // 畫一張、存檔、回傳像素(方便做「角色有沒有出現在畫面上」之類的檢查)
        public static Texture2D Capture(Camera camera, string name)
        {
            Texture2D texture = Render(camera);
            Save(texture, name);
            return texture;
        }

        // 把多張同尺寸小圖拼成一張(左上開始、逐列往右;Texture2D 的原點在左下)
        public static Texture2D Stitch(Texture2D[] frames, int columns)
        {
            int w = frames[0].width;
            int h = frames[0].height;
            int rows = (frames.Length + columns - 1) / columns;
            var sheet = new Texture2D(w * columns, h * rows, TextureFormat.RGBA32, false, false);
            for (int i = 0; i < frames.Length; i++)
            {
                int col = i % columns;
                int row = rows - 1 - i / columns;
                sheet.SetPixels32(col * w, row * h, w, h, frames[i].GetPixels32());
            }
            sheet.Apply(false);
            return sheet;
        }

        // 只數「角色像素」(有角色 vs 關掉角色 renderer 的兩張圖差異處)裡色相接近 target 的像素:
        // 地上的墨也是隊伍色,不遮罩會被地面灌水
        public static int HuePixelsInMask(Texture2D withCharacter, Texture2D withoutCharacter, Color target,
            float hueTolerance = 0.06f, float minSaturation = 0.35f)
        {
            Color.RGBToHSV(target, out float th, out _, out _);
            Color32[] pa = withCharacter.GetPixels32();
            Color32[] pb = withoutCharacter.GetPixels32();
            int count = 0;
            int n = Mathf.Min(pa.Length, pb.Length);
            for (int i = 0; i < n; i++)
            {
                if (Mathf.Abs(pa[i].r - pb[i].r) <= 24 && Mathf.Abs(pa[i].g - pb[i].g) <= 24
                    && Mathf.Abs(pa[i].b - pb[i].b) <= 24)
                {
                    continue;
                }
                Color.RGBToHSV(pa[i], out float h, out float s, out float v);
                if (s < minSaturation || v < 0.15f)
                {
                    continue;
                }
                float d = Mathf.Abs(h - th);
                d = Mathf.Min(d, 1f - d);
                if (d <= hueTolerance)
                {
                    count++;
                }
            }
            return count;
        }

        // 兩張同尺寸圖差異明顯的像素數(RGB 任一通道差 > threshold/255)
        public static int DiffPixels(Texture2D a, Texture2D b, int threshold = 24)
        {
            Color32[] pa = a.GetPixels32();
            Color32[] pb = b.GetPixels32();
            int count = 0;
            int n = Mathf.Min(pa.Length, pb.Length);
            for (int i = 0; i < n; i++)
            {
                if (Mathf.Abs(pa[i].r - pb[i].r) > threshold || Mathf.Abs(pa[i].g - pb[i].g) > threshold
                    || Mathf.Abs(pa[i].b - pb[i].b) > threshold)
                {
                    count++;
                }
            }
            return count;
        }

        // 顏色接近 target 色相(且夠飽和)的像素數:畫面上看得到多少隊伍色
        public static int HuePixels(Texture2D texture, Color target, float hueTolerance = 0.05f, float minSaturation = 0.45f)
        {
            Color.RGBToHSV(target, out float th, out _, out _);
            Color32[] pixels = texture.GetPixels32();
            int count = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                Color.RGBToHSV(pixels[i], out float h, out float s, out float v);
                if (s < minSaturation || v < 0.2f)
                {
                    continue;
                }
                float d = Mathf.Abs(h - th);
                d = Mathf.Min(d, 1f - d);
                if (d <= hueTolerance)
                {
                    count++;
                }
            }
            return count;
        }
    }
}

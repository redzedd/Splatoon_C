using System.Collections.Generic;
using UnityEngine;

namespace SplatoonC.Gameplay.UI
{
    // 程序化 UI 圖形:以 SDF 逐像素算出反鋸齒形狀,啟動時生成一次並快取——不需要任何貼圖資產。
    // 全部是白色 + alpha,顏色由 Image.color 染(隊伍色換了不必重畫)。
    public static class UiSprites
    {
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        // internal:選單圖示(MenuSprites)共用同一套 SDF 光柵化與快取
        internal delegate float Sdf(float x, float y);

        // 以 SDF(負值 = 內部,單位 = 像素)畫一張白色 alpha 圖
        internal static Sprite Make(string key, int width, int height, Sdf sdf, Vector4 border = default)
        {
            if (Cache.TryGetValue(key, out Sprite cached) && cached != null)
            {
                return cached;
            }
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "UiSprite_" + key,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float d = sdf(x + 0.5f, y + 0.5f);
                    byte a = (byte)(Mathf.Clamp01(0.5f - d) * 255f);
                    pixels[y * width + x] = new Color32(255, 255, 255, a);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, border);
            sprite.name = key;
            Cache[key] = sprite;
            return sprite;
        }

        internal static float Circle(float x, float y, float cx, float cy, float r)
        {
            return Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - r;
        }

        internal static float Box(float x, float y, float cx, float cy, float hx, float hy, float radius)
        {
            float qx = Mathf.Abs(x - cx) - hx + radius;
            float qy = Mathf.Abs(y - cy) - hy + radius;
            float outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
            return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
        }

        // 三角形 SDF(頂點逆/順時針皆可)
        internal static float Triangle(float px, float py, Vector2 a, Vector2 b, Vector2 c)
        {
            Vector2 p = new Vector2(px, py);
            Vector2 e0 = b - a, e1 = c - b, e2 = a - c;
            Vector2 v0 = p - a, v1 = p - b, v2 = p - c;
            Vector2 pq0 = v0 - e0 * Mathf.Clamp01(Vector2.Dot(v0, e0) / Vector2.Dot(e0, e0));
            Vector2 pq1 = v1 - e1 * Mathf.Clamp01(Vector2.Dot(v1, e1) / Vector2.Dot(e1, e1));
            Vector2 pq2 = v2 - e2 * Mathf.Clamp01(Vector2.Dot(v2, e2) / Vector2.Dot(e2, e2));
            float s = Mathf.Sign(e0.x * e2.y - e0.y * e2.x);
            Vector2 d = Vector2.Min(Vector2.Min(
                new Vector2(pq0.sqrMagnitude, s * (v0.x * e0.y - v0.y * e0.x)),
                new Vector2(pq1.sqrMagnitude, s * (v1.x * e1.y - v1.y * e1.x))),
                new Vector2(pq2.sqrMagnitude, s * (v2.x * e2.y - v2.y * e2.x)));
            return -Mathf.Sqrt(d.x) * Mathf.Sign(d.y);
        }

        public static Sprite Circle(int size = 128)
        {
            float r = size * 0.5f - 1.5f;
            return Make("circle" + size, size, size, (x, y) => Circle(x, y, size * 0.5f, size * 0.5f, r));
        }

        // 圓角矩形(九宮格可拉伸)
        public static Sprite RoundedRect(int radius = 16)
        {
            int size = radius * 2 + 4;
            float half = size * 0.5f;
            return Make("rrect" + radius, size, size, (x, y) => Box(x, y, half, half, half - 1f, half - 1f, radius),
                new Vector4(radius + 1, radius + 1, radius + 1, radius + 1));
        }

        // 分段圓環(S3 大招量表 23 段):Image.fillMethod = Radial360 疊在同一張上即可順時針填
        public static Sprite SegmentedRing(int size, float thickness, int segments, float gapDeg)
        {
            float outer = size * 0.5f - 1.5f;
            float inner = outer - thickness;
            float c = size * 0.5f;
            return Make($"ring{size}_{thickness}_{segments}_{gapDeg}", size, size, (x, y) =>
            {
                float dx = x - c, dy = y - c;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float ring = Mathf.Max(r - outer, inner - r);
                if (segments <= 0)
                {
                    return ring;
                }
                float angle = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg;
                float step = 360f / segments;
                float local = Mathf.Repeat(angle, step);
                float edge = Mathf.Min(local, step - local) - gapDeg * 0.5f;
                // 角度差換算成弧長(像素)
                float gap = -edge * Mathf.Deg2Rad * r;
                return Mathf.Max(ring, gap);
            });
        }

        // 烏賊頭剪影(S3 隊伍圖示):尖頂外套膜 + 兩側鰭 + 圓身,兩個白眼洞由另一張 Eyes 疊上
        public static Sprite SquidHead(int size = 128)
        {
            float s = size;
            return Make("squid" + size, size, size, (x, y) =>
            {
                float body = Box(x, y, s * 0.5f, s * 0.36f, s * 0.26f, s * 0.22f, s * 0.2f);
                float top = Triangle(x, y, new Vector2(s * 0.5f, s * 0.96f), new Vector2(s * 0.2f, s * 0.46f),
                    new Vector2(s * 0.8f, s * 0.46f));
                float finL = Triangle(x, y, new Vector2(s * 0.28f, s * 0.72f), new Vector2(s * 0.04f, s * 0.5f),
                    new Vector2(s * 0.3f, s * 0.46f));
                float finR = Triangle(x, y, new Vector2(s * 0.72f, s * 0.72f), new Vector2(s * 0.96f, s * 0.5f),
                    new Vector2(s * 0.7f, s * 0.46f));
                return Mathf.Min(Mathf.Min(body, top), Mathf.Min(finL, finR));
            });
        }

        public static Sprite SquidEyes(int size = 128)
        {
            float s = size;
            return Make("squideyes" + size, size, size, (x, y) =>
                Mathf.Min(Circle(x, y, s * 0.4f, s * 0.36f, s * 0.07f), Circle(x, y, s * 0.6f, s * 0.36f, s * 0.07f)));
        }

        public static Sprite Cross(int size = 128)
        {
            float s = size;
            float t = s * 0.09f;
            return Make("cross" + size, size, size, (x, y) =>
            {
                float u = (x - s * 0.5f) * 0.7071f;
                float v = (y - s * 0.5f) * 0.7071f;
                float a = Box(u + v, u - v, 0f, 0f, s * 0.42f, t, t);
                float b = Box(u - v, u + v, 0f, 0f, s * 0.42f, t, t);
                return Mathf.Min(a, b);
            });
        }

        // 墨漬(標題卡、倒數背景):有機波瓣輪廓 + 幾顆噴濺點
        public static Sprite Splat(int size = 256, int seed = 1)
        {
            float s = size;
            float c = s * 0.5f;
            var rng = new System.Random(seed);
            float phase1 = (float)rng.NextDouble() * 6.28f;
            float phase2 = (float)rng.NextDouble() * 6.28f;
            var drops = new Vector3[6];
            for (int i = 0; i < drops.Length; i++)
            {
                float ang = (float)rng.NextDouble() * 6.28f;
                float dist = s * (0.38f + (float)rng.NextDouble() * 0.08f);
                drops[i] = new Vector3(c + Mathf.Cos(ang) * dist, c + Mathf.Sin(ang) * dist, s * (0.025f + (float)rng.NextDouble() * 0.03f));
            }
            return Make($"splat{size}_{seed}", size, size, (x, y) =>
            {
                float dx = x - c, dy = y - c;
                float ang = Mathf.Atan2(dy, dx);
                float radius = s * 0.3f * (1f + 0.12f * Mathf.Sin(ang * 5f + phase1) + 0.07f * Mathf.Sin(ang * 9f + phase2));
                float d = Mathf.Sqrt(dx * dx + dy * dy) - radius;
                for (int i = 0; i < drops.Length; i++)
                {
                    d = Mathf.Min(d, Circle(x, y, drops[i].x, drops[i].y, drops[i].z));
                }
                return d;
            });
        }

        // 貓頭(判定員 Judd 風格):圓臉 + 兩耳
        public static Sprite CatHead(int size = 128)
        {
            float s = size;
            return Make("cat" + size, size, size, (x, y) =>
            {
                float head = Circle(x, y, s * 0.5f, s * 0.42f, s * 0.34f);
                float earL = Triangle(x, y, new Vector2(s * 0.18f, s * 0.95f), new Vector2(s * 0.14f, s * 0.55f),
                    new Vector2(s * 0.44f, s * 0.7f));
                float earR = Triangle(x, y, new Vector2(s * 0.82f, s * 0.95f), new Vector2(s * 0.86f, s * 0.55f),
                    new Vector2(s * 0.56f, s * 0.7f));
                return Mathf.Min(head, Mathf.Min(earL, earR));
            });
        }

        public static Sprite CatFace(int size = 128)
        {
            float s = size;
            return Make("catface" + size, size, size, (x, y) =>
            {
                float eyeL = Box(x, y, s * 0.38f, s * 0.48f, s * 0.035f, s * 0.07f, s * 0.035f);
                float eyeR = Box(x, y, s * 0.62f, s * 0.48f, s * 0.035f, s * 0.07f, s * 0.035f);
                float nose = Triangle(x, y, new Vector2(s * 0.5f, s * 0.34f), new Vector2(s * 0.46f, s * 0.39f),
                    new Vector2(s * 0.54f, s * 0.39f));
                return Mathf.Min(Mathf.Min(eyeL, eyeR), nose);
            });
        }

        // 狀態圖示「被標記」:杏仁眼 + 瞳孔(被看穿了)
        public static Sprite TrackedEye(int size = 128)
        {
            float s = size;
            float c = s * 0.5f;
            return Make("trackedeye" + size, size, size, (x, y) =>
            {
                // 兩個大圓的交集 = 杏仁形外框
                float lens = Mathf.Max(Circle(x, y, c, c - s * 0.3f, s * 0.52f), Circle(x, y, c, c + s * 0.3f, s * 0.52f));
                float iris = Circle(x, y, c, c, s * 0.2f);
                float ring = Mathf.Max(lens, -iris);
                float pupil = Circle(x, y, c, c, s * 0.11f);
                return Mathf.Min(ring, pupil);
            });
        }

        // 狀態圖示「減速/強化」:兩層 V 形箭頭(up = 朝上)
        public static Sprite Chevrons(bool up, int size = 128)
        {
            float s = size;
            float t = s * 0.13f;
            return Make((up ? "chevup" : "chevdown") + size, size, size, (x, y) =>
            {
                float py = up ? y : s - y;
                float a = Chevron(x, py, s * 0.5f, s * 0.62f, s * 0.36f, s * 0.26f, t);
                float b = Chevron(x, py, s * 0.5f, s * 0.3f, s * 0.36f, s * 0.26f, t);
                return Mathf.Min(a, b);
            });
        }

        // 朝上的 V 形:頂點 (cx, tipY),兩翼向下展開 halfWidth、高 height,線寬 thickness
        private static float Chevron(float x, float y, float cx, float tipY, float halfWidth, float height, float thickness)
        {
            var tip = new Vector2(cx, tipY);
            var left = new Vector2(cx - halfWidth, tipY - height);
            var right = new Vector2(cx + halfWidth, tipY - height);
            float outer = Triangle(x, y, tip, left, right);
            float inner = Triangle(x, y + thickness * 1.6f, tip, left, right);
            return Mathf.Max(outer, -inner);
        }

        // 軟邊暗角(受傷時的螢幕邊墨)
        public static Sprite Vignette(int size = 256)
        {
            float s = size;
            return Make("vignette" + size, size, size, (x, y) =>
            {
                float u = Mathf.Abs(x / s - 0.5f) * 2f;
                float v = Mathf.Abs(y / s - 0.5f) * 2f;
                float edge = Mathf.Max(u, v * 0.9f);
                // 0.5 - d = alpha:邊緣 1、往內淡出
                return 0.5f - Mathf.SmoothStep(0f, 1f, (edge - 0.55f) / 0.45f);
            });
        }
    }
}

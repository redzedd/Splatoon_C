using SplatoonC.Gameplay.Combat;
using UnityEngine;

namespace SplatoonC.Gameplay.UI.Menu
{
    // 選單用的程序化圖示(與 UiSprites 同一套 SDF 光柵化與快取):武器種類剪影、副武器/大招圖示、模式圖示、箭頭、斜紋。
    // 座標以 0~1 描述(y 往上),乘上邊長 s 後交給 SDF。白色 + alpha,顏色由 Image.color 染。
    public static class MenuSprites
    {
        private const int IconSize = 128;

        // 旋轉矩形(角度:逆時針,度)
        private static float RBox(float x, float y, float cx, float cy, float hx, float hy, float r, float angleDeg)
        {
            float a = -angleDeg * Mathf.Deg2Rad;
            float dx = x - cx;
            float dy = y - cy;
            float lx = dx * Mathf.Cos(a) - dy * Mathf.Sin(a);
            float ly = dx * Mathf.Sin(a) + dy * Mathf.Cos(a);
            return UiSprites.Box(lx, ly, 0f, 0f, hx, hy, r);
        }

        private static float Ring(float x, float y, float cx, float cy, float radius, float halfThickness)
        {
            return Mathf.Abs(UiSprites.Circle(x, y, cx, cy, radius)) - halfThickness;
        }

        private static float Union(float a, float b)
        {
            return Mathf.Min(a, b);
        }

        private static float Intersect(float a, float b)
        {
            return Mathf.Max(a, b);
        }

        // ── 武器種類剪影(槍口朝右)──

        public static Sprite WeaponClassIcon(WeaponClass weaponClass)
        {
            const int n = IconSize;
            const float s = n;
            switch (weaponClass)
            {
                case WeaponClass.Roller:
                    return UiSprites.Make("w_roller", n, n, (x, y) =>
                    {
                        float drum = UiSprites.Box(x, y, s * 0.6f, s * 0.74f, s * 0.32f, s * 0.13f, s * 0.08f);
                        float handle = RBox(x, y, s * 0.4f, s * 0.42f, s * 0.035f, s * 0.3f, s * 0.02f, 35f);
                        float grip = RBox(x, y, s * 0.24f, s * 0.19f, s * 0.06f, s * 0.09f, s * 0.03f, 35f);
                        return Union(Union(drum, handle), grip);
                    });
                case WeaponClass.Charger:
                    return UiSprites.Make("w_charger", n, n, (x, y) =>
                    {
                        float barrel = UiSprites.Box(x, y, s * 0.62f, s * 0.55f, s * 0.36f, s * 0.035f, s * 0.02f);
                        float body = UiSprites.Box(x, y, s * 0.3f, s * 0.52f, s * 0.2f, s * 0.09f, s * 0.04f);
                        float scope = UiSprites.Box(x, y, s * 0.42f, s * 0.68f, s * 0.13f, s * 0.045f, s * 0.03f);
                        float stock = RBox(x, y, s * 0.11f, s * 0.44f, s * 0.07f, s * 0.11f, s * 0.03f, 20f);
                        float grip = RBox(x, y, s * 0.3f, s * 0.35f, s * 0.05f, s * 0.1f, s * 0.02f, -15f);
                        return Union(Union(Union(barrel, body), Union(scope, stock)), grip);
                    });
                case WeaponClass.Slosher:
                    return UiSprites.Make("w_slosher", n, n, (x, y) =>
                    {
                        float bucket = UiSprites.Box(x, y, s * 0.5f, s * 0.38f, s * 0.25f, s * 0.22f, s * 0.07f);
                        float rim = UiSprites.Box(x, y, s * 0.5f, s * 0.6f, s * 0.31f, s * 0.05f, s * 0.03f);
                        float handle = Intersect(Ring(x, y, s * 0.5f, s * 0.6f, s * 0.3f, s * 0.035f), s * 0.6f - y);
                        float drop = UiSprites.Circle(x, y, s * 0.86f, s * 0.84f, s * 0.06f);
                        return Union(Union(bucket, rim), Union(handle, drop));
                    });
                case WeaponClass.Splatling:
                    return UiSprites.Make("w_splatling", n, n, (x, y) =>
                    {
                        float drum = UiSprites.Circle(x, y, s * 0.28f, s * 0.52f, s * 0.2f);
                        float barrel = UiSprites.Box(x, y, s * 0.64f, s * 0.54f, s * 0.3f, s * 0.11f, s * 0.04f);
                        float tip = UiSprites.Box(x, y, s * 0.93f, s * 0.54f, s * 0.04f, s * 0.14f, s * 0.02f);
                        float grip = RBox(x, y, s * 0.5f, s * 0.32f, s * 0.05f, s * 0.1f, s * 0.02f, -10f);
                        return Union(Union(drum, barrel), Union(tip, grip));
                    });
                case WeaponClass.Dualies:
                    return UiSprites.Make("w_dualies", n, n, (x, y) =>
                    {
                        float a = UiSprites.Box(x, y, s * 0.42f, s * 0.7f, s * 0.26f, s * 0.08f, s * 0.04f);
                        float ag = RBox(x, y, s * 0.28f, s * 0.56f, s * 0.05f, s * 0.1f, s * 0.02f, -15f);
                        float b = UiSprites.Box(x, y, s * 0.58f, s * 0.36f, s * 0.26f, s * 0.08f, s * 0.04f);
                        float bg = RBox(x, y, s * 0.44f, s * 0.22f, s * 0.05f, s * 0.1f, s * 0.02f, -15f);
                        return Union(Union(a, ag), Union(b, bg));
                    });
                case WeaponClass.Brella:
                    return UiSprites.Make("w_brella", n, n, (x, y) =>
                    {
                        // 傘面:大圓減去左移的圓 = 朝右的新月形盾面
                        float canopy = Intersect(UiSprites.Circle(x, y, s * 0.46f, s * 0.5f, s * 0.42f),
                            -UiSprites.Circle(x, y, s * 0.22f, s * 0.5f, s * 0.38f));
                        float shaft = UiSprites.Box(x, y, s * 0.46f, s * 0.5f, s * 0.34f, s * 0.03f, s * 0.015f);
                        float hook = Intersect(Ring(x, y, s * 0.16f, s * 0.42f, s * 0.08f, s * 0.025f), y - s * 0.42f);
                        return Union(Union(canopy, shaft), hook);
                    });
                case WeaponClass.Blaster:
                    return UiSprites.Make("w_blaster", n, n, (x, y) =>
                    {
                        float body = UiSprites.Box(x, y, s * 0.38f, s * 0.54f, s * 0.22f, s * 0.14f, s * 0.07f);
                        float barrel = UiSprites.Box(x, y, s * 0.7f, s * 0.56f, s * 0.14f, s * 0.08f, s * 0.03f);
                        float flare = UiSprites.Box(x, y, s * 0.88f, s * 0.56f, s * 0.05f, s * 0.14f, s * 0.03f);
                        float grip = RBox(x, y, s * 0.32f, s * 0.32f, s * 0.06f, s * 0.12f, s * 0.03f, -12f);
                        float tank = UiSprites.Circle(x, y, s * 0.3f, s * 0.74f, s * 0.08f);
                        return Union(Union(Union(body, barrel), Union(flare, grip)), tank);
                    });
                case WeaponClass.Brush:
                    return UiSprites.Make("w_brush", n, n, (x, y) =>
                    {
                        float handle = RBox(x, y, s * 0.34f, s * 0.34f, s * 0.035f, s * 0.28f, s * 0.02f, 45f);
                        float ferrule = RBox(x, y, s * 0.56f, s * 0.56f, s * 0.06f, s * 0.05f, s * 0.02f, 45f);
                        float bristle = UiSprites.Circle(x, y, s * 0.7f, s * 0.7f, s * 0.14f);
                        float tip = UiSprites.Triangle(x, y, new Vector2(s * 0.95f, s * 0.95f), new Vector2(s * 0.6f, s * 0.82f),
                            new Vector2(s * 0.82f, s * 0.6f));
                        return Union(Union(handle, ferrule), Union(bristle, tip));
                    });
                case WeaponClass.Stringer:
                    return UiSprites.Make("w_stringer", n, n, (x, y) =>
                    {
                        float bow = Intersect(Ring(x, y, s * 0.3f, s * 0.5f, s * 0.42f, s * 0.035f), s * 0.34f - x);
                        float str = UiSprites.Box(x, y, s * 0.31f, s * 0.5f, s * 0.012f, s * 0.41f, 0f);
                        float arrow = UiSprites.Box(x, y, s * 0.56f, s * 0.5f, s * 0.3f, s * 0.018f, 0f);
                        float head = UiSprites.Triangle(x, y, new Vector2(s * 0.95f, s * 0.5f), new Vector2(s * 0.82f, s * 0.58f),
                            new Vector2(s * 0.82f, s * 0.42f));
                        return Union(Union(bow, str), Union(arrow, head));
                    });
                case WeaponClass.Splatana:
                    return UiSprites.Make("w_splatana", n, n, (x, y) =>
                    {
                        float blade = RBox(x, y, s * 0.6f, s * 0.6f, s * 0.36f, s * 0.05f, s * 0.04f, 38f);
                        float guard = RBox(x, y, s * 0.3f, s * 0.36f, s * 0.03f, s * 0.11f, s * 0.015f, 38f);
                        float grip = RBox(x, y, s * 0.18f, s * 0.26f, s * 0.1f, s * 0.04f, s * 0.02f, 38f);
                        return Union(Union(blade, guard), grip);
                    });
                default:
                    // 射擊槍(Splattershot 輪廓:槍身 + 上方墨瓶 + 槍管 + 握把)
                    return UiSprites.Make("w_shooter", n, n, (x, y) =>
                    {
                        float body = UiSprites.Box(x, y, s * 0.46f, s * 0.54f, s * 0.26f, s * 0.1f, s * 0.05f);
                        float barrel = UiSprites.Box(x, y, s * 0.8f, s * 0.57f, s * 0.12f, s * 0.045f, s * 0.02f);
                        float nozzle = UiSprites.Box(x, y, s * 0.93f, s * 0.57f, s * 0.03f, s * 0.065f, s * 0.015f);
                        float grip = RBox(x, y, s * 0.36f, s * 0.33f, s * 0.06f, s * 0.14f, s * 0.03f, -15f);
                        float bottle = UiSprites.Box(x, y, s * 0.42f, s * 0.73f, s * 0.14f, s * 0.075f, s * 0.06f);
                        return Union(Union(Union(body, barrel), Union(nozzle, grip)), bottle);
                    });
            }
        }

        // 副武器(通用手榴彈輪廓)
        public static Sprite SubIcon()
        {
            const float s = IconSize;
            return UiSprites.Make("i_sub", IconSize, IconSize, (x, y) =>
            {
                float ball = UiSprites.Circle(x, y, s * 0.5f, s * 0.44f, s * 0.32f);
                float cap = UiSprites.Box(x, y, s * 0.5f, s * 0.8f, s * 0.1f, s * 0.07f, s * 0.03f);
                return Union(ball, cap);
            });
        }

        // 大招(五芒星)
        public static Sprite SpecialIcon()
        {
            return Star("i_special", IconSize, 5, 0.46f, 0.2f);
        }

        public static Sprite Star(string key, int size, int points, float outer, float inner)
        {
            float s = size;
            return UiSprites.Make(key, size, size, (x, y) =>
            {
                float d = UiSprites.Circle(x, y, s * 0.5f, s * 0.5f, s * inner);
                for (int i = 0; i < points; i++)
                {
                    float a = Mathf.PI * 0.5f + i * Mathf.PI * 2f / points;
                    float half = Mathf.PI / points;
                    var tip = new Vector2(s * 0.5f + Mathf.Cos(a) * s * outer, s * 0.5f + Mathf.Sin(a) * s * outer);
                    var left = new Vector2(s * 0.5f + Mathf.Cos(a - half) * s * inner, s * 0.5f + Mathf.Sin(a - half) * s * inner);
                    var right = new Vector2(s * 0.5f + Mathf.Cos(a + half) * s * inner, s * 0.5f + Mathf.Sin(a + half) * s * inner);
                    d = Mathf.Min(d, UiSprites.Triangle(x, y, tip, left, right));
                }
                return d;
            });
        }

        // ── 模式圖示 ──

        public static Sprite ZoneIcon()
        {
            const float s = IconSize;
            return UiSprites.Make("m_zone", IconSize, IconSize, (x, y) =>
            {
                float outer = UiSprites.Box(x, y, s * 0.5f, s * 0.5f, s * 0.38f, s * 0.38f, s * 0.04f);
                float inner = UiSprites.Box(x, y, s * 0.5f, s * 0.5f, s * 0.26f, s * 0.26f, s * 0.02f);
                float core = UiSprites.Box(x, y, s * 0.5f, s * 0.5f, s * 0.12f, s * 0.12f, s * 0.02f);
                return Union(Intersect(outer, -inner), core);
            });
        }

        public static Sprite TowerIcon()
        {
            const float s = IconSize;
            return UiSprites.Make("m_tower", IconSize, IconSize, (x, y) =>
            {
                float bodyShape = UiSprites.Triangle(x, y, new Vector2(s * 0.5f, s * 0.95f), new Vector2(s * 0.24f, s * 0.2f),
                    new Vector2(s * 0.76f, s * 0.2f));
                float body = Intersect(bodyShape, y - s * 0.26f);
                float bands = Mathf.Abs(Mathf.Repeat(y, s * 0.16f) - s * 0.08f) - s * 0.045f;
                float baseBox = UiSprites.Box(x, y, s * 0.5f, s * 0.14f, s * 0.34f, s * 0.07f, s * 0.03f);
                return Union(Intersect(body, bands), baseBox);
            });
        }

        public static Sprite RainmakerIcon()
        {
            const float s = IconSize;
            return UiSprites.Make("m_rainmaker", IconSize, IconSize, (x, y) =>
            {
                float bx = (x - s * 0.44f) / 1.45f;
                float body = Mathf.Sqrt(bx * bx + (y - s * 0.5f) * (y - s * 0.5f)) - s * 0.2f;
                float tail = UiSprites.Triangle(x, y, new Vector2(s * 0.7f, s * 0.5f), new Vector2(s * 0.95f, s * 0.72f),
                    new Vector2(s * 0.95f, s * 0.28f));
                float eye = UiSprites.Circle(x, y, s * 0.26f, s * 0.55f, s * 0.04f);
                return Intersect(Union(body, tail), -eye);
            });
        }

        public static Sprite ClamIcon()
        {
            const float s = IconSize;
            return UiSprites.Make("m_clam", IconSize, IconSize, (x, y) =>
            {
                float shell = Intersect(UiSprites.Circle(x, y, s * 0.5f, s * 0.36f, s * 0.4f), s * 0.36f - y);
                float ridges = Mathf.Abs(Mathf.Repeat(Mathf.Atan2(y - s * 0.36f, x - s * 0.5f) * s * 0.3f, s * 0.14f) - s * 0.07f)
                               - s * 0.05f;
                float hinge = UiSprites.Box(x, y, s * 0.5f, s * 0.3f, s * 0.2f, s * 0.05f, s * 0.03f);
                return Union(Intersect(shell, ridges), hinge);
            });
        }

        // 右箭頭(選項左右切換;左邊用 localScale.x = -1)
        public static Sprite Chevron()
        {
            const int n = 64;
            const float s = n;
            return UiSprites.Make("chevron", n, n, (x, y) =>
                UiSprites.Triangle(x, y, new Vector2(s * 0.8f, s * 0.5f), new Vector2(s * 0.28f, s * 0.9f),
                    new Vector2(s * 0.28f, s * 0.1f)) - s * 0.02f);
        }

        // 斜紋(背景裝飾,Tiled)
        public static Sprite Stripes()
        {
            const int n = 64;
            return UiSprites.Make("stripes", n, n, (x, y) =>
            {
                float t = Mathf.Repeat(x + y, n * 0.5f);
                return Mathf.Abs(t - n * 0.25f) - n * 0.09f;
            });
        }

        // 半色調圓點(S3 的網點紋理,背景裝飾,Tiled)
        public static Sprite Halftone()
        {
            const int n = 32;
            return UiSprites.Make("halftone", n, n, (x, y) => UiSprites.Circle(x, y, n * 0.5f, n * 0.5f, n * 0.22f));
        }

        // 重量(壺鈴)
        public static Sprite WeightIcon()
        {
            const float s = IconSize;
            return UiSprites.Make("i_weight", IconSize, IconSize, (x, y) =>
            {
                float bell = UiSprites.Circle(x, y, s * 0.5f, s * 0.38f, s * 0.3f);
                float handle = Intersect(Ring(x, y, s * 0.5f, s * 0.66f, s * 0.2f, s * 0.05f), s * 0.6f - y);
                return Union(bell, handle);
            });
        }

        // 場地預覽的峽谷剪影(兩側岩壁往中間收成 V 形谷地,稜線帶一點起伏);寬 256 高 128
        public static Sprite GorgeSilhouette()
        {
            const int w = 256;
            const int h = 128;
            return UiSprites.Make("gorge", w, h, (x, y) =>
            {
                float u = x / w;
                float ridge = h * (0.3f + 0.55f * Mathf.Abs(u - 0.5f) * 2f + 0.05f * Mathf.Sin(u * 23f) + 0.03f * Mathf.Sin(u * 57f));
                return y - ridge;
            });
        }

        // 鎖(即將推出)
        public static Sprite LockIcon()
        {
            const float s = IconSize;
            return UiSprites.Make("i_lock", IconSize, IconSize, (x, y) =>
            {
                float body = UiSprites.Box(x, y, s * 0.5f, s * 0.36f, s * 0.28f, s * 0.22f, s * 0.05f);
                float shackle = Intersect(Ring(x, y, s * 0.5f, s * 0.58f, s * 0.18f, s * 0.05f), s * 0.52f - y);
                float hole = UiSprites.Circle(x, y, s * 0.5f, s * 0.38f, s * 0.05f);
                return Intersect(Union(body, shackle), -hole);
            });
        }
    }
}

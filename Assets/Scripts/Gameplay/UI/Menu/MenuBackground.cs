using UnityEngine;
using UnityEngine.UI;

namespace SplatoonC.Gameplay.UI.Menu
{
    // 主選單背景:深紫黑底 + 緩慢漂移旋轉的大墨漬(黃綠 / 紫兩色)+ 斜紋與網點(S3 的塗鴉紙質感)。
    // 純 UI,不需要 3D 場景;動畫用 unscaled 時間,零配置。
    public sealed class MenuBackground : MonoBehaviour
    {
        private struct Blob
        {
            public RectTransform Rect;
            public Vector2 Home;
            public float Speed;
            public float Phase;
            public float Spin;
            public float Scale;
        }

        private Blob[] _blobs;
        private RectTransform _stripes;

        public static MenuBackground Create(Transform parent)
        {
            RectTransform root = Ui.Stretch("Background", parent);
            var background = root.gameObject.AddComponent<MenuBackground>();
            background.Build(root);
            return background;
        }

        private void Build(RectTransform root)
        {
            var baseImage = root.gameObject.AddComponent<Image>();
            baseImage.color = MenuTheme.Background;
            baseImage.raycastTarget = false;

            // 墨漬:兩塊鮮明的主色大墨漬壓在對角(右上黃綠、左下紫,大半在畫面外),中間只放低調的紫色墨漬——
            // 黃綠低透明度疊在暗底上會變成髒橄欖色,所以黃綠只用高飽和的小面積
            Vector2[] anchors =
            {
                new Vector2(1.02f, 1.05f), new Vector2(-0.04f, -0.08f), new Vector2(0.62f, 0.72f), new Vector2(0.3f, 0.3f),
                new Vector2(0.86f, 0.28f), new Vector2(0.08f, 0.8f), new Vector2(0.5f, 0.05f),
            };
            float[] sizes = { 980f, 1100f, 620f, 520f, 700f, 420f, 460f };
            Color[] colors =
            {
                Ui.WithAlpha(MenuTheme.Accent, 0.9f), Ui.WithAlpha(MenuTheme.Secondary, 0.62f),
                Ui.WithAlpha(MenuTheme.Secondary, 0.2f), Ui.WithAlpha(MenuTheme.Secondary, 0.16f),
                Ui.WithAlpha(MenuTheme.Secondary, 0.24f), Ui.WithAlpha(MenuTheme.Secondary, 0.18f),
                Ui.WithAlpha(new Color(0.95f, 0.3f, 0.65f), 0.16f),
            };
            _blobs = new Blob[anchors.Length];
            for (int i = 0; i < anchors.Length; i++)
            {
                Image image = Ui.Image("Splat" + i, root, anchors[i], Vector2.zero, new Vector2(sizes[i], sizes[i]),
                    UiSprites.Splat(256, 20 + i), colors[i]);
                image.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                _blobs[i] = new Blob
                {
                    Rect = image.rectTransform,
                    Home = Vector2.zero,
                    Speed = 0.07f + 0.03f * i,
                    Phase = i * 1.7f,
                    Spin = (i % 2 == 0 ? 1f : -1f) * (2f + i),
                    Scale = 1f,
                };
            }

            // 斜紋帶(微亮,Tiled)
            Image stripes = Ui.Image("Stripes", root, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(3200f, 420f),
                MenuSprites.Stripes(), new Color(1f, 1f, 1f, 0.028f));
            stripes.type = Image.Type.Tiled;
            stripes.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            stripes.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 14f);
            _stripes = stripes.rectTransform;

            // 右下網點
            Image dots = Ui.Image("Halftone", root, new Vector2(1f, 0f), Vector2.zero, new Vector2(900f, 520f),
                MenuSprites.Halftone(), new Color(1f, 1f, 1f, 0.04f));
            dots.type = Image.Type.Tiled;
        }

        private void Update()
        {
            if (_blobs == null)
            {
                return;
            }
            float t = Time.unscaledTime;
            for (int i = 0; i < _blobs.Length; i++)
            {
                Blob blob = _blobs[i];
                float a = t * blob.Speed + blob.Phase;
                blob.Rect.anchoredPosition = blob.Home + new Vector2(Mathf.Sin(a) * 40f, Mathf.Cos(a * 0.8f) * 30f);
                blob.Rect.localRotation = Quaternion.Euler(0f, 0f, t * blob.Spin);
                float pulse = blob.Scale * (1f + 0.04f * Mathf.Sin(a * 1.9f));
                blob.Rect.localScale = new Vector3(pulse, pulse, 1f);
            }
            _stripes.anchoredPosition = new Vector2(Mathf.Repeat(t * 18f, 64f), 0f);
        }
    }
}

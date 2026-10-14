using UnityEngine;

namespace SplatoonC.Gameplay.Painting
{
    // 把隊伍色套到墨彈、墨滴、炸彈、拋物線等「墨做的東西」上:共用一個 MaterialPropertyBlock,
    // 不複製材質(SRP Batcher 仍可合批),也不每次配置。
    public static class TeamTint
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();

        public static void Apply(Renderer renderer, Color color)
        {
            if (renderer == null)
            {
                return;
            }
            renderer.GetPropertyBlock(Block);
            Block.SetColor(BaseColorId, color);
            renderer.SetPropertyBlock(Block);
        }

        public static void Apply(Renderer renderer, Color color, float alpha)
        {
            color.a = alpha;
            Apply(renderer, color);
        }

        // 池化物件的熱路徑:呼叫端快取 Renderer 陣列,顏色沒變就不重設(每發射擊零配置)
        public static void ApplyCached(Renderer[] renderers, ref Color applied, Color color)
        {
            if (renderers == null || applied == color)
            {
                return;
            }
            applied = color;
            for (int i = 0; i < renderers.Length; i++)
            {
                Apply(renderers[i], color);
            }
        }

        // 同物件與子物件的所有 Renderer(低頻物件用;會配置陣列)
        public static void ApplyAll(Component root, Color color)
        {
            if (root == null)
            {
                return;
            }
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Apply(renderers[i], color);
            }
        }
    }
}

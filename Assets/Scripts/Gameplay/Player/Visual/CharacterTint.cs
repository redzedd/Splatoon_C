using System.Collections.Generic;
using UnityEngine;

namespace SplatoonC.Gameplay.Player
{
    // 角色的隊伍色:觸手/頭髮、墨水槽裡的墨、烏賊身體、槍上的墨罐——凡是用到「隊伍色材質」的子網格,
    // 都以 MaterialPropertyBlock(每個 renderer × 每個子網格)覆寫 _BaseColor,不複製材質。
    // 自發光(_EmissionColor,材質已開 _EMISSION)用來做墨水微光與大招集滿/發動時的脈動光。
    // 顏色或光量沒變就不重設(每幀零配置、零 SetPropertyBlock)。
    public sealed class CharacterTint
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private struct Slot
        {
            public Renderer Renderer;
            public int MaterialIndex;
            public float BaseGlow;
        }

        private readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        private Slot[] _slots = new Slot[0];
        private Color _appliedColor = new Color(-1f, -1f, -1f, -1f);
        private float _appliedGlow = -1f;

        public int SlotCount => _slots.Length;
        public Color AppliedColor => _appliedColor;

        // 掃描 root 底下所有 renderer(含停用中的烏賊模型),記下用到隊伍色材質的子網格(只在綁定時配置)
        public void Bind(Transform root, Material[] teamMaterials, float[] baseGlow)
        {
            var slots = new List<Slot>();
            if (root != null && teamMaterials != null)
            {
                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                for (int r = 0; r < renderers.Length; r++)
                {
                    Material[] shared = renderers[r].sharedMaterials;
                    for (int m = 0; m < shared.Length; m++)
                    {
                        int kind = IndexOf(teamMaterials, shared[m]);
                        if (kind < 0)
                        {
                            continue;
                        }
                        slots.Add(new Slot
                        {
                            Renderer = renderers[r],
                            MaterialIndex = m,
                            BaseGlow = baseGlow != null && kind < baseGlow.Length ? baseGlow[kind] : 0f,
                        });
                    }
                }
            }
            _slots = slots.ToArray();
            _appliedColor = new Color(-1f, -1f, -1f, -1f);
            _appliedGlow = -1f;
        }

        private static int IndexOf(Material[] materials, Material material)
        {
            if (material == null)
            {
                return -1;
            }
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == material)
                {
                    return i;
                }
            }
            return -1;
        }

        // 強制下一次 Apply 重設(配色表換色時)
        public void Invalidate()
        {
            _appliedColor = new Color(-1f, -1f, -1f, -1f);
            _appliedGlow = -1f;
        }

        // glow:額外自發光倍率(0 = 只有基礎微光)
        public void Apply(Color team, float glow)
        {
            if (team == _appliedColor && Mathf.Abs(glow - _appliedGlow) < 0.004f)
            {
                return;
            }
            _appliedColor = team;
            _appliedGlow = glow;
            Vector4 linear = team.linear;
            for (int i = 0; i < _slots.Length; i++)
            {
                Slot slot = _slots[i];
                if (slot.Renderer == null)
                {
                    continue;
                }
                slot.Renderer.GetPropertyBlock(_block, slot.MaterialIndex);
                _block.SetColor(BaseColorId, team);
                // 自發光以線性值傳(SetVector 不做色彩空間轉換,HDR 倍率才不會被 gamma 扭曲)
                float emission = slot.BaseGlow + glow;
                _block.SetVector(EmissionColorId, new Vector4(linear.x * emission, linear.y * emission,
                    linear.z * emission, 1f));
                slot.Renderer.SetPropertyBlock(_block, slot.MaterialIndex);
            }
        }

        // 測試/除錯:讀回某個 renderer 的第 materialIndex 個子網格目前被覆寫的 _BaseColor
        public static bool TryReadBaseColor(Renderer renderer, int materialIndex, out Color color)
        {
            color = Color.clear;
            if (renderer == null)
            {
                return false;
            }
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block, materialIndex);
            if (block.isEmpty)
            {
                return false;
            }
            color = block.GetColor(BaseColorId);
            return true;
        }
    }
}

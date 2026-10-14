using System.Collections.Generic;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 大招期間拿在手上的模型(Trizooka 發射器、Wave Breaker 裝置):掛在 CharacterVisual.WeaponSocket 下(V1 規則),
    // 同時把插槽底下原本的主武器模型藏起來(forceRenderingOff,不碰 Renderer.enabled——那是潛墨隱形在切的),結束時還原。
    // 雙槍的左手那把(WeaponSocketLeft)一起藏;顯示期間 CharacterVisual.HeldSpecialActive = true(滾筒/雙持姿勢讓位)。
    // 模型只建一次、之後切 SetActive;名稱以 "Ink" 開頭的部位套施放者隊伍色(MaterialPropertyBlock)。
    // 變烏賊時整個人形模型停用 → 插槽底下的模型跟著隱藏;沒有 CharacterVisual 的舊角色掛在 Visual 下的 fallback 位置。
    public sealed class SpecialHeldModel
    {
        private const string InkPartPrefix = "Ink";

        private readonly List<Renderer> _hidden = new List<Renderer>(8);
        private readonly List<bool> _hiddenWasOff = new List<bool>(8);
        private GameObject _instance;
        private CharacterVisual _visual;
        private Renderer[] _inkRenderers;
        private Color _tinted = Color.clear;

        public GameObject Instance => _instance;
        public bool Shown => _instance != null && _instance.activeSelf;

        // 顯示(第一次呼叫時生成)。localPosition/Euler:相對插槽(+Z 槍管方向、+Y 拇指側)
        public void Show(Inkling owner, GameObject prefab, Vector3 localPosition, Vector3 localEuler)
        {
            if (owner == null || prefab == null)
            {
                return;
            }
            Transform socket = FindSocket(owner, out bool fallback);
            if (socket == null)
            {
                return;
            }
            if (_instance == null || _instance.transform.parent != socket)
            {
                if (_instance != null)
                {
                    Object.Destroy(_instance);
                }
                _instance = Object.Instantiate(prefab, socket, false);
                _instance.name = prefab.name;
                SetLayer(_instance.transform, socket.gameObject.layer);
                var ink = new List<Renderer>();
                foreach (Renderer r in _instance.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.name.StartsWith(InkPartPrefix, System.StringComparison.Ordinal))
                    {
                        ink.Add(r);
                    }
                }
                _inkRenderers = ink.ToArray();
                _tinted = Color.clear;
            }
            _instance.transform.localPosition = fallback ? new Vector3(0.3f, 1.45f, 0.1f) + localPosition : localPosition;
            _instance.transform.localRotation = Quaternion.Euler(localEuler);
            _instance.SetActive(true);
            TeamTint.ApplyCached(_inkRenderers, ref _tinted, TeamPalette.ColorOf(owner.Team));
            HideSocketSiblings(socket);
            _visual = fallback ? null : owner.GetComponent<CharacterVisual>();
            if (_visual != null)
            {
                _visual.HeldSpecialActive = true;
                HideSocketSiblings(_visual.WeaponSocketLeft);
            }
        }

        public void Hide()
        {
            if (_instance != null && _instance.activeSelf)
            {
                _instance.SetActive(false);
            }
            if (_visual != null)
            {
                _visual.HeldSpecialActive = false;
                _visual = null;
            }
            for (int i = 0; i < _hidden.Count; i++)
            {
                if (_hidden[i] != null)
                {
                    _hidden[i].forceRenderingOff = _hiddenWasOff[i];
                }
            }
            _hidden.Clear();
            _hiddenWasOff.Clear();
        }

        public void Destroy()
        {
            Hide();
            if (_instance != null)
            {
                Object.Destroy(_instance);
                _instance = null;
            }
        }

        private void HideSocketSiblings(Transform socket)
        {
            if (socket == null)
            {
                return;
            }
            // 只在發動時配置一次(GetComponentsInChildren);主武器槍模、內建槍身都在插槽底下
            foreach (Renderer r in socket.GetComponentsInChildren<Renderer>(true))
            {
                if (r.transform.IsChildOf(_instance.transform) || _hidden.Contains(r))
                {
                    continue;
                }
                _hidden.Add(r);
                _hiddenWasOff.Add(r.forceRenderingOff);
                r.forceRenderingOff = true;
            }
        }

        private static Transform FindSocket(Inkling owner, out bool fallback)
        {
            fallback = false;
            var visual = owner.GetComponent<CharacterVisual>();
            if (visual != null && visual.IsReady && visual.WeaponSocket != null)
            {
                return visual.WeaponSocket;
            }
            fallback = true;
            Transform root = owner.transform.Find("Visual");
            return root != null ? root : owner.transform;
        }

        private static void SetLayer(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++)
            {
                SetLayer(root.GetChild(i), layer);
            }
        }
    }
}

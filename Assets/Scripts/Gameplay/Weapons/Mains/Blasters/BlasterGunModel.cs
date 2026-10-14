using System.Collections.Generic;
using SplatoonC.Core;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 爆破槍的手上槍模(程序化 prefab,BlasterSetup 產生):掛在 CharacterVisual.WeaponSocket(右手掌心握把點;+Z 槍管、+Y 上方),
    // 內建 Splattershot 槍身用 forceRenderingOff 藏起來、卸下時還原(不動 enabled——SquidController 潛墨隱形切的是 enabled)。
    // 槍模在人形模型底下:變烏賊 / 被擊倒時整個人形停用,槍模自動跟著隱藏。
    // 子物件名稱以 "Ink" 開頭的是墨水部位(墨槽、槍口),依擁有者隊伍套色(隊伍變了才重設,零配置)。
    // 純 C# 物件,由 BlasterWeapon 持有並每幀呼叫 Tick。
    public sealed class BlasterGunModel
    {
        private const string InkPartPrefix = "Ink";
        private const string MuzzleName = "Muzzle";

        private readonly Inkling _owner;
        private Transform _root;
        private Renderer _builtInGun;
        private Renderer[] _renderers;
        private Renderer[] _inkRenderers;
        private Team _tintedTeam;
        private bool _tinted;

        public GameObject SourcePrefab { get; }
        public Transform Root => _root;
        // 槍模的槍口(子物件 Muzzle;沒有就用槍模原點):出膛特效位置
        public Transform Muzzle { get; private set; }
        public int InkPartCount => _inkRenderers != null ? _inkRenderers.Length : 0;
        public bool BuiltInGunHidden => _builtInGun != null && _builtInGun.forceRenderingOff;

        // 槍模目前有畫出來(人形、活著、沒被停用)
        public bool Visible
        {
            get
            {
                if (_root == null || !_root.gameObject.activeInHierarchy || _renderers == null)
                {
                    return false;
                }
                for (int i = 0; i < _renderers.Length; i++)
                {
                    if (_renderers[i] != null && _renderers[i].enabled && !_renderers[i].forceRenderingOff)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        private BlasterGunModel(GameObject prefab, Inkling owner)
        {
            SourcePrefab = prefab;
            _owner = owner;
        }

        // 生成並掛上右手插槽;角色沒有 V1 外觀(CharacterVisual 未就緒)時回傳 null(武器照常運作,只是沒槍模)
        public static BlasterGunModel Attach(Component character, GameObject prefab, Inkling owner)
        {
            if (character == null || prefab == null)
            {
                return null;
            }
            var visual = character.GetComponent<CharacterVisual>();
            if (visual == null || !visual.IsReady || visual.WeaponSocket == null)
            {
                return null;
            }
            Transform socket = visual.WeaponSocket;
            Transform visualRoot = character.transform.Find("Visual");
            var model = new BlasterGunModel(prefab, owner);
            GameObject instance = Object.Instantiate(prefab, socket, false);
            instance.name = prefab.name;
            Transform t = instance.transform;
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            // 以 Visual 的單位(公尺)生成:插槽在骨架底下,若骨架帶縮放就補回來,槍模尺寸永遠是設計的公尺數
            float socketScale = Mathf.Abs(socket.lossyScale.x);
            float visualScale = visualRoot != null ? Mathf.Abs(visualRoot.lossyScale.x) : 1f;
            float k = socketScale > 1e-4f ? visualScale / socketScale : 1f;
            t.localScale = prefab.transform.localScale * k;
            SetLayerRecursively(t, socket.gameObject.layer);
            model._root = t;
            model.Muzzle = FindDeep(t, MuzzleName) ?? t;
            model._renderers = instance.GetComponentsInChildren<Renderer>(true);
            var ink = new List<Renderer>();
            for (int i = 0; i < model._renderers.Length; i++)
            {
                if (model._renderers[i].name.StartsWith(InkPartPrefix, System.StringComparison.Ordinal))
                {
                    ink.Add(model._renderers[i]);
                }
            }
            model._inkRenderers = ink.ToArray();

            Transform builtIn = visual.WeaponModel;
            model._builtInGun = builtIn != null ? builtIn.GetComponent<Renderer>() : null;
            if (model._builtInGun != null)
            {
                model._builtInGun.forceRenderingOff = true;
            }
            // V1 規約:掛上新網格後重掃要染色的子網格(只在換裝時)
            visual.RefreshTintTargets();
            model.Tick();
            return model;
        }

        // 每幀:隊伍變了才重新套色
        public void Tick()
        {
            Team team = _owner != null ? _owner.Team : Team.Alpha;
            if (_tinted && team == _tintedTeam)
            {
                return;
            }
            _tinted = true;
            _tintedTeam = team;
            Color color = TeamPalette.ColorOf(team);
            for (int i = 0; i < _inkRenderers.Length; i++)
            {
                if (_inkRenderers[i] != null)
                {
                    TeamTint.Apply(_inkRenderers[i], color);
                }
            }
        }

        // 卸下:內建槍身恢復、槍模銷毀
        public void Detach()
        {
            if (_builtInGun != null)
            {
                _builtInGun.forceRenderingOff = false;
            }
            if (_root != null)
            {
                // 先停用再銷毀:Destroy 延到幀尾,同一幀的染色重掃不該再綁到它
                _root.gameObject.SetActive(false);
                Object.Destroy(_root.gameObject);
            }
            _root = null;
        }

        private static void SetLayerRecursively(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++)
            {
                SetLayerRecursively(root.GetChild(i), layer);
            }
        }

        private static Transform FindDeep(Transform parent, string childName)
        {
            if (parent == null)
            {
                return null;
            }
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name == childName)
                {
                    return child;
                }
                Transform found = FindDeep(child, childName);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }
    }
}

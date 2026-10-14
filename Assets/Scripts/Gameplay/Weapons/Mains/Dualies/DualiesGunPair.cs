using System.Collections.Generic;
using SplatoonC.Core;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 雙槍的兩把槍模(程序化 prefab,DualiesSetup 產生):右手掛 CharacterVisual.WeaponSocket、左手掛左手插槽
    //(EnsureWeaponSocketLeft,右手插槽的鏡射)。內建 Splattershot 槍身用 forceRenderingOff 藏起來、卸下時還原。
    // 槍模在人形模型底下:變烏賊時整個人形停用,槍模自動跟著隱藏,不必登記到 SquidController。
    // 子物件名稱以 "Ink" 開頭的是墨水部位(墨槽、槍口),依擁有者隊伍套色(隊伍變了才重設,零配置)。
    // 純 C# 物件,由 DualiesWeapon 持有並每幀呼叫 Tick。
    public sealed class DualiesGunPair
    {
        private const string InkPartPrefix = "Ink";
        private const string MuzzleName = "Muzzle";

        private readonly Inkling _owner;
        private Transform _right;
        private Transform _left;
        private Renderer _builtInGun;
        private Renderer[] _inkRenderers;
        private Team _tintedTeam;
        private bool _tinted;

        public GameObject SourcePrefab { get; }
        public Transform Right => _right;
        public Transform Left => _left;
        // 槍口(槍模子物件 Muzzle;沒有就用槍模原點)
        public Transform RightMuzzle { get; private set; }
        public Transform LeftMuzzle { get; private set; }
        public int InkPartCount => _inkRenderers != null ? _inkRenderers.Length : 0;
        public bool BuiltInGunHidden => _builtInGun != null && _builtInGun.forceRenderingOff;

        private DualiesGunPair(GameObject prefab, Inkling owner)
        {
            SourcePrefab = prefab;
            _owner = owner;
        }

        // 生成兩把並掛上插槽;角色沒有 V1 外觀(CharacterVisual 未就緒)時回傳 null(武器照常運作,只是沒槍模)
        public static DualiesGunPair Attach(Component character, GameObject prefab, Inkling owner)
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
            Transform leftSocket = visual.EnsureWeaponSocketLeft();
            if (leftSocket == null)
            {
                return null;
            }
            var pair = new DualiesGunPair(prefab, owner);
            Transform visualRoot = character.transform.Find("Visual");
            pair._right = Spawn(prefab, visual.WeaponSocket, visualRoot, "SplatDualies_R");
            pair._left = Spawn(prefab, leftSocket, visualRoot, "SplatDualies_L");
            pair.RightMuzzle = FindDeep(pair._right, MuzzleName) ?? pair._right;
            pair.LeftMuzzle = FindDeep(pair._left, MuzzleName) ?? pair._left;

            Transform builtIn = visual.WeaponModel;
            pair._builtInGun = builtIn != null ? builtIn.GetComponent<Renderer>() : null;
            if (pair._builtInGun != null)
            {
                pair._builtInGun.forceRenderingOff = true;
            }
            var ink = new List<Renderer>();
            CollectInk(pair._right, ink);
            CollectInk(pair._left, ink);
            pair._inkRenderers = ink.ToArray();
            // V1 規約:掛上新網格後重掃要染色的子網格(只在換裝時)
            visual.RefreshTintTargets();
            pair.Tick();
            return pair;
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

        // 兩把槍模目前都有畫出來(人形、活著、沒被停用)
        public bool Visible => IsShown(_right) && IsShown(_left);

        private static bool IsShown(Transform gun)
        {
            if (gun == null || !gun.gameObject.activeInHierarchy)
            {
                return false;
            }
            Renderer r = gun.GetComponentInChildren<Renderer>();
            return r != null && r.enabled && !r.forceRenderingOff;
        }

        // 卸下:內建槍身恢復、槍模銷毀
        public void Detach()
        {
            if (_builtInGun != null)
            {
                _builtInGun.forceRenderingOff = false;
            }
            if (_right != null)
            {
                Object.Destroy(_right.gameObject);
            }
            if (_left != null)
            {
                Object.Destroy(_left.gameObject);
            }
            _right = null;
            _left = null;
        }

        // 以 Visual 的單位(公尺)生成:插槽在骨架底下,若骨架帶縮放就補回來,槍模尺寸永遠是設計的公尺數
        private static Transform Spawn(GameObject prefab, Transform socket, Transform visualRoot, string name)
        {
            GameObject instance = Object.Instantiate(prefab, socket, false);
            instance.name = name;
            Transform t = instance.transform;
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            float socketScale = Mathf.Abs(socket.lossyScale.x);
            float visualScale = visualRoot != null ? Mathf.Abs(visualRoot.lossyScale.x) : 1f;
            float k = socketScale > 1e-4f ? visualScale / socketScale : 1f;
            t.localScale = prefab.transform.localScale * k;
            SetLayerRecursively(t, socket.gameObject.layer);
            return t;
        }

        private static void CollectInk(Transform root, List<Renderer> into)
        {
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name.StartsWith(InkPartPrefix, System.StringComparison.Ordinal))
                {
                    into.Add(r);
                }
            }
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

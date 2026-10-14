using System.Collections.Generic;
using SplatoonC.Core;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 射擊槍變體的槍模(程序化 prefab,ShooterVariantsSetup 產生):掛在角色 Visual 下、對齊內建 GunBarrel 的位置。
    // 內建槍身改用 forceRenderingOff 藏起來,不動它的 enabled——SquidController 潛墨隱形時切的是 Visual 底下
    // 快取的 Renderer.enabled(槍模是執行期加上的,不在那份快取裡),所以槍模每幀跟著 GunBarrel.enabled 顯示/隱藏,
    // 才不會潛在墨裡還露出一把槍。子物件名稱以 "Ink" 開頭的是墨水部位(墨槽窗、墨管),套隊伍色。
    [DisallowMultipleComponent]
    public sealed class ShooterGunModel : MonoBehaviour
    {
        private const string BuiltInBarrelName = "GunBarrel";
        private const string InkPartPrefix = "Ink";

        private Renderer _builtInBarrel;
        private Renderer[] _renderers;
        private Renderer[] _inkRenderers;
        private Inkling _owner;
        private Team _tintedTeam;
        private bool _tinted;
        private bool _visible = true;

        public GameObject SourcePrefab { get; private set; }
        public bool Visible => _visible;
        public int InkPartCount => _inkRenderers != null ? _inkRenderers.Length : 0;

        // 生成並對齊;角色沒有 GunBarrel 時掛在 Visual(或根)原點
        public static ShooterGunModel Attach(Transform character, GameObject prefab, Inkling owner)
        {
            if (character == null || prefab == null)
            {
                return null;
            }
            Transform barrel = FindRecursive(character, BuiltInBarrelName);
            Transform parent = barrel != null ? barrel.parent : FindRecursive(character, "Visual");
            if (parent == null)
            {
                parent = character;
            }
            GameObject instance = Instantiate(prefab, parent, false);
            instance.name = prefab.name;
            if (barrel != null)
            {
                instance.transform.localPosition = barrel.localPosition;
                instance.transform.localRotation = barrel.localRotation;
            }
            SetLayerRecursively(instance.transform, parent.gameObject.layer);
            var model = instance.GetComponent<ShooterGunModel>();
            if (model == null)
            {
                model = instance.AddComponent<ShooterGunModel>();
            }
            model.Initialize(barrel != null ? barrel.GetComponent<Renderer>() : null, prefab, owner);
            return model;
        }

        private void Initialize(Renderer builtInBarrel, GameObject prefab, Inkling owner)
        {
            _builtInBarrel = builtInBarrel;
            SourcePrefab = prefab;
            _owner = owner;
            _renderers = GetComponentsInChildren<Renderer>(true);
            var ink = new List<Renderer>();
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i].name.StartsWith(InkPartPrefix, System.StringComparison.Ordinal))
                {
                    ink.Add(_renderers[i]);
                }
            }
            _inkRenderers = ink.ToArray();
            if (_builtInBarrel != null)
            {
                _builtInBarrel.forceRenderingOff = true;
            }
            SyncVisibility(true);
            TintIfTeamChanged();
        }

        // 換回內建槍身(換裝成沒有槍模的設定、或主武器元件被移除)
        public void Detach()
        {
            if (_builtInBarrel != null)
            {
                _builtInBarrel.forceRenderingOff = false;
            }
            Destroy(gameObject);
        }

        private void LateUpdate()
        {
            SyncVisibility(false);
            TintIfTeamChanged();
        }

        // 角色外觀(V1)變烏賊時整個 InklingModel 停用,掛在 WeaponSocket 下的槍模跟著停用、LateUpdate 不再跑:
        // 停用當下就把顯示狀態記成隱藏,重新啟用時再依內建槍身同步一次,Visible 才與畫面一致
        private void OnDisable()
        {
            if (_renderers == null)
            {
                return;
            }
            _visible = false;
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] != null)
                {
                    _renderers[i].enabled = false;
                }
            }
        }

        private void OnEnable()
        {
            if (_renderers != null)
            {
                SyncVisibility(true);
            }
        }

        private void SyncVisibility(bool force)
        {
            bool show = _builtInBarrel == null || _builtInBarrel.enabled;
            if (!force && show == _visible)
            {
                return;
            }
            _visible = show;
            for (int i = 0; i < _renderers.Length; i++)
            {
                _renderers[i].enabled = show;
            }
        }

        // 隊伍可能在換裝之後才由比賽流程設定:隊伍變了才重設(零配置)
        private void TintIfTeamChanged()
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
                TeamTint.Apply(_inkRenderers[i], color);
            }
        }

        private static Transform FindRecursive(Transform parent, string childName)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name == childName)
                {
                    return child;
                }
                Transform found = FindRecursive(child, childName);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        private static void SetLayerRecursively(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++)
            {
                SetLayerRecursively(root.GetChild(i), layer);
            }
        }
    }
}

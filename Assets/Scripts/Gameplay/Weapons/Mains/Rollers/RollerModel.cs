using SplatoonC.Core;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 手上的滾筒模型(程序化 prefab,RollerSetup 產生)。座標系 = 角色右手 WeaponSocket:原點 = 右手握把點,
    // +Z = 握把往滾筒的方向,滾筒軸 = 局部 X、軸心在 +Z × HandleLength。姿勢由 CharacterVisual 依 RollerWeapon 的動作狀態擺。
    // 掛上時把角色內建槍身(CharacterVisual.WeaponModel)以 forceRenderingOff 藏起來(不動 enabled——SquidController
    // 潛墨隱形切的是 Renderer.enabled);掛在人形模型底下,變烏賊/被擊倒時跟著人形模型一起消失。
    // 墨水部位(名稱 "Ink" 開頭,材質 = 角色的 TankInk 隊伍色材質)由 CharacterVisual.RefreshTintTargets 接手染色與發光;
    // 沒有 CharacterVisual 的舊角色退回 TeamTint。
    [DisallowMultipleComponent]
    public sealed class RollerModel : MonoBehaviour
    {
        private const string InkPartPrefix = "Ink";
        private const string BuiltInGunName = "GunBarrel";

        [SerializeField, Tooltip("滾筒(推滾時繞局部 X 轉)")]
        private Transform _drum;

        [SerializeField, Tooltip("滾筒半徑(公尺)")]
        private float _drumRadius = 0.2f;

        [SerializeField, Tooltip("滾筒寬度(公尺)")]
        private float _drumWidth = 1.4f;

        [SerializeField, Tooltip("握把點到滾筒軸心的距離(公尺;姿勢 IK 用)")]
        private float _handleLength = 1.05f;

        [SerializeField, Tooltip("左手握點:沿握把離右手的距離(公尺)")]
        private float _supportDistance = 0.26f;

        private Renderer _builtInGun;
        private CharacterVisual _visual;
        private Renderer[] _renderers;
        private Renderer[] _inkRenderers;
        private Inkling _owner;
        private bool _tinted;
        private Team _tintedTeam;
        private Quaternion _drumRest = Quaternion.identity;
        private float _spinSpeed;
        private float _spinAngle;

        public RollerModel SourcePrefab { get; private set; }
        public float DrumRadius => _drumRadius;
        public float DrumWidth => _drumWidth;
        public float HandleLength => _handleLength;
        public float SupportDistance => _supportDistance;
        public Transform Drum => _drum;
        public int InkPartCount => _inkRenderers != null ? _inkRenderers.Length : 0;
        public bool BuiltInGunHidden => _builtInGun != null && _builtInGun.forceRenderingOff;
        public float SpinAngle => _spinAngle;

        // 模型實際看得到(所在物件啟用中且有 renderer 開著)
        public bool Visible
        {
            get
            {
                if (!gameObject.activeInHierarchy || _renderers == null)
                {
                    return false;
                }
                for (int i = 0; i < _renderers.Length; i++)
                {
                    if (_renderers[i] != null && _renderers[i].enabled)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        // 滾筒軸心(世界座標)
        public Vector3 DrumCenter => transform.TransformPoint(new Vector3(0f, 0f, _handleLength));

        // 生成並掛到角色右手的 WeaponSocket(角色外觀還沒就緒時退回內建槍身的父物件);回傳 null = 無法掛上
        public static RollerModel Attach(Transform character, RollerModel prefab, Inkling owner)
        {
            if (character == null || prefab == null)
            {
                return null;
            }
            var visual = character.GetComponent<CharacterVisual>();
            Transform socket = visual != null && visual.IsReady ? visual.WeaponSocket : null;
            Transform builtIn = visual != null && visual.WeaponModel != null
                ? visual.WeaponModel
                : FindRecursive(character, BuiltInGunName);
            Transform parent = socket != null ? socket : builtIn != null ? builtIn.parent : character;
            RollerModel model = Instantiate(prefab, parent, false);
            model.name = prefab.name;
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;
            SetLayerRecursively(model.transform, parent.gameObject.layer);
            model.Initialize(visual, builtIn != null ? builtIn.GetComponent<Renderer>() : null, prefab, owner);
            return model;
        }

        private void Initialize(CharacterVisual visual, Renderer builtInGun, RollerModel prefab, Inkling owner)
        {
            _visual = visual;
            _builtInGun = builtInGun;
            _owner = owner;
            SourcePrefab = prefab;
            _renderers = GetComponentsInChildren<Renderer>(true);
            int ink = 0;
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (IsInkPart(_renderers[i]))
                {
                    ink++;
                }
            }
            _inkRenderers = new Renderer[ink];
            ink = 0;
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (IsInkPart(_renderers[i]))
                {
                    _inkRenderers[ink++] = _renderers[i];
                }
            }
            if (_drum != null)
            {
                _drumRest = _drum.localRotation;
            }
            if (_builtInGun != null)
            {
                _builtInGun.forceRenderingOff = true;
            }
            if (_visual != null)
            {
                // 滾筒墨水部位用角色的隊伍色材質:交給角色外觀的 MaterialPropertyBlock(含大招發光)
                _visual.RefreshTintTargets();
            }
            else
            {
                TintIfTeamChanged();
            }
        }

        private static bool IsInkPart(Renderer renderer)
        {
            return renderer != null && renderer.name.StartsWith(InkPartPrefix, System.StringComparison.Ordinal);
        }

        // 換回內建槍身並移除(換裝成其他主武器、或滾筒元件被移除)
        public void Detach()
        {
            if (_builtInGun != null)
            {
                _builtInGun.forceRenderingOff = false;
            }
            // 不改父物件(角色本身可能正在被銷毀);染色表裡殘留的 renderer 銷毀後自動跳過
            Destroy(gameObject);
            if (_visual != null)
            {
                _visual.RefreshTintTargets();
            }
        }

        // 推滾速度(公尺/秒,正 = 往前滾):滾筒依 v / r 轉動
        public void SetSpin(float metresPerSecond)
        {
            _spinSpeed = metresPerSecond;
        }

        private void LateUpdate()
        {
            if (_drum != null && Mathf.Abs(_spinSpeed) > 1e-3f && _drumRadius > 1e-3f)
            {
                _spinAngle = Mathf.Repeat(_spinAngle + _spinSpeed / _drumRadius * Mathf.Rad2Deg * Time.deltaTime, 360f);
                _drum.localRotation = _drumRest * Quaternion.AngleAxis(_spinAngle, Vector3.right);
            }
            if (_visual == null)
            {
                TintIfTeamChanged();
            }
        }

        // 沒有角色外觀元件時的退路:隊伍變了才重設(零配置)
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

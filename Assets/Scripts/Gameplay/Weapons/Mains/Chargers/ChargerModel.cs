using SplatoonC.Core;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 手上的狙擊槍模型(程序化 prefab,ChargerSetup 產生)。座標系 = 角色右手 WeaponSocket:原點 = 右手握把點,
    // +Z = 槍管方向、+Y = 槍的上方;槍托往 −Z 伸到肩上,槍口在 +Z × MuzzleDistance(MuzzleTip)。
    // 姿勢由 CharacterVisual 依 ILongGunPose(ChargerWeapon)擺:瞄準時槍托靠肩、左手托護木。
    // 掛上時把角色內建槍身(CharacterVisual.WeaponModel)以 forceRenderingOff 藏起來(不動 enabled——SquidController
    // 潛墨隱形切的是 Renderer.enabled);掛在人形模型底下,變烏賊/被擊倒時跟著人形模型一起消失。
    // 墨水部位(名稱 "Ink" 開頭,材質 = 角色的 TankInk 隊伍色材質)由 CharacterVisual.RefreshTintTargets 接手染色與發光;
    // 蓄力計(ChargeGauge,槍身兩側的發光墨條,Unlit 材質、本元件自己染隊伍色)隨蓄力從後往前填滿,
    // 滿蓄時變亮並脈動——兩隊都看得到的蓄力提示。
    [DisallowMultipleComponent]
    public sealed class ChargerModel : MonoBehaviour
    {
        private const string InkPartPrefix = "Ink";
        private const string BuiltInGunName = "GunBarrel";

        [SerializeField, Tooltip("槍口(雷射起點;+Z 端)")]
        private Transform _muzzleTip;

        [SerializeField, Tooltip("蓄力計(沿 +Z 縮放 0 → 1 = 蓄力比例;原點在後端)")]
        private Transform _chargeGauge;

        [SerializeField, Tooltip("握把點到槍口的距離(公尺)")]
        private float _muzzleDistance = 1.0f;

        private Renderer _builtInGun;
        private CharacterVisual _visual;
        private Renderer[] _renderers;
        private Renderer[] _inkRenderers;
        private Inkling _owner;
        private bool _tinted;
        private Team _tintedTeam;
        private Vector3 _gaugeRestScale = Vector3.one;
        private float _gaugeFill = -1f;
        private float _pulse;
        private Renderer[] _gaugeRenderers;
        private Color _gaugeColor = Color.clear;
        private bool _gaugeFull;

        public ChargerModel SourcePrefab { get; private set; }
        public Transform MuzzleTip => _muzzleTip != null ? _muzzleTip : transform;
        public Transform ChargeGauge => _chargeGauge;
        public float MuzzleDistance => _muzzleDistance;
        public int InkPartCount => _inkRenderers != null ? _inkRenderers.Length : 0;
        public bool BuiltInGunHidden => _builtInGun != null && _builtInGun.forceRenderingOff;
        public float GaugeFill => Mathf.Max(0f, _gaugeFill);

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

        // 生成並掛到角色右手的 WeaponSocket(角色外觀還沒就緒時退回內建槍身的父物件);回傳 null = 無法掛上
        public static ChargerModel Attach(Transform character, ChargerModel prefab, Inkling owner)
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
            ChargerModel model = Instantiate(prefab, parent, false);
            model.name = prefab.name;
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;
            SetLayerRecursively(model.transform, parent.gameObject.layer);
            model.Initialize(visual, builtIn != null ? builtIn.GetComponent<Renderer>() : null, prefab, owner);
            return model;
        }

        private void Initialize(CharacterVisual visual, Renderer builtInGun, ChargerModel prefab, Inkling owner)
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
            if (_chargeGauge != null)
            {
                _gaugeRestScale = _chargeGauge.localScale;
                _gaugeRenderers = _chargeGauge.GetComponentsInChildren<Renderer>(true);
            }
            SetCharge(0f, false);
            if (_builtInGun != null)
            {
                _builtInGun.forceRenderingOff = true;
            }
            if (_visual != null)
            {
                // 墨水部位用角色的隊伍色材質:交給角色外觀的 MaterialPropertyBlock(含大招發光)
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

        // 換回內建槍身並移除(換裝成其他主武器、或狙擊槍元件被移除)
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

        // 蓄力計:fill 0 → 1 從槍身後端往槍口填;滿蓄時粗細脈動(每幀呼叫,值沒變不動 transform)
        public void SetCharge(float fill, bool full)
        {
            if (_chargeGauge == null)
            {
                return;
            }
            fill = Mathf.Clamp01(fill);
            float pulse = 1f;
            if (full)
            {
                _pulse = Mathf.Repeat(_pulse + Time.deltaTime * 6f, Mathf.PI * 2f);
                pulse = 1.18f + 0.18f * Mathf.Sin(_pulse);
            }
            else
            {
                _pulse = 0f;
            }
            _gaugeFull = full;
            if (Mathf.Abs(fill - _gaugeFill) < 1e-4f && !full)
            {
                return;
            }
            _gaugeFill = fill;
            bool show = fill > 0.001f;
            if (_chargeGauge.gameObject.activeSelf != show)
            {
                _chargeGauge.gameObject.SetActive(show);
            }
            _chargeGauge.localScale = new Vector3(_gaugeRestScale.x * pulse, _gaugeRestScale.y * pulse,
                _gaugeRestScale.z * Mathf.Max(0.001f, fill));
        }

        private void LateUpdate()
        {
            if (_visual == null)
            {
                TintIfTeamChanged();
            }
            TintGauge();
        }

        // 蓄力計:隊伍色,滿蓄時往白色提亮(顏色沒變不重設,零配置)
        private void TintGauge()
        {
            if (_gaugeRenderers == null || _gaugeRenderers.Length == 0)
            {
                return;
            }
            Color team = TeamPalette.ColorOf(_owner != null ? _owner.Team : Team.Alpha);
            Color color = _gaugeFull ? Color.Lerp(team, Color.white, 0.45f) : team;
            TeamTint.ApplyCached(_gaugeRenderers, ref _gaugeColor, color);
        }

        // 蓄力計目前的顏色(測試讀)
        public Color GaugeColor => _gaugeColor;

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

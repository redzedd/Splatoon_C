using UnityEngine;

namespace SplatoonC.Gameplay.Match
{
    // 模式目標共用資產(Resources/Modes/ObjectiveAssets.asset,由 ModeLayoutSetup 接線):
    // 區域瞬間重塗的 ink map shader、區域邊框/光幕/穿牆虛線材質。放在 Resources 也確保 standalone build 帶上 shader。
    [CreateAssetMenu(fileName = "ObjectiveAssets", menuName = "SplatoonC/模式目標資產")]
    public sealed class ObjectiveAssets : ScriptableObject
    {
        public const string ResourcePath = "Modes/ObjectiveAssets";

        [SerializeField, Tooltip("區域重塗 shader(SplatoonC/InkZoneFill):把多邊形內的地面整片寫進 ink map")]
        private Shader _zoneFillShader;

        [SerializeField, Tooltip("區域地面邊框材質(SplatoonC/MarkOverlay,ZTest LEqual)")]
        private Material _zoneBorderMaterial;

        [SerializeField, Tooltip("區域光幕材質(邊框往上的漸層光牆,遠看更明顯)")]
        private Material _zoneCurtainMaterial;

        [SerializeField, Tooltip("區域穿牆虛線材質(ZTest Greater:被擋住的邊框以流動虛線顯示)")]
        private Material _zoneXRayMaterial;

        public Shader ZoneFillShader => _zoneFillShader;
        public Material ZoneBorderMaterial => _zoneBorderMaterial;
        public Material ZoneCurtainMaterial => _zoneCurtainMaterial;
        public Material ZoneXRayMaterial => _zoneXRayMaterial;

        private static ObjectiveAssets _cached;

        public static ObjectiveAssets Load()
        {
            if (_cached == null)
            {
                _cached = Resources.Load<ObjectiveAssets>(ResourcePath);
            }
            return _cached;
        }
    }
}

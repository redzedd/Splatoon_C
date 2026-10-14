using System;
using System.Collections.Generic;
using SplatoonC.Core.Match.Ranked;
using UnityEngine;

namespace SplatoonC.Gameplay.Match
{
    // 場地的「模式目標錨點」資料(Resources/ModeLayouts/<場景名>.asset,由 ModeLayoutSetup 產生):
    // 塗地場景本身不放任何模式物件,蠻頹對戰開賽時 ObjectiveModeController 依這裡的資料在執行期生成。
    // 目前:真格區域的區域多邊形。之後的塔樓路線 / 魚虎台座 / 蛤蜊籃在這裡加欄位。
    [CreateAssetMenu(fileName = "ModeLayout", menuName = "SplatoonC/模式目標配置")]
    public sealed class ModeLayout : ScriptableObject
    {
        public const string ResourceFolder = "ModeLayouts";

        // 真格區域的一個區域:俯視多邊形 + 高度範圍(只有落在範圍內、從上方看得到的地面格才算區域)
        [Serializable]
        public struct ZoneArea
        {
            [Tooltip("名稱(除錯/HUD)")]
            public string Name;

            [Tooltip("俯視多邊形頂點(世界座標 XZ,公尺)")]
            public Vector2[] Polygon;

            [Tooltip("區域地面的最低高度(公尺)")]
            public float MinY;

            [Tooltip("區域地面的最高高度(公尺);超出的平台(塔頂、斜坡上段)不算區域")]
            public float MaxY;
        }

        [SerializeField, Tooltip("對應的場景名稱")]
        private string _sceneName;

        [SerializeField, Tooltip("真格區域:1 或 2 個區域(S3;面積多為 140~200 m²)")]
        private List<ZoneArea> _splatZones = new List<ZoneArea>();

        public string SceneName => _sceneName;
        public IReadOnlyList<ZoneArea> SplatZones => _splatZones;

        // 這張場地支援哪些模式(塗地永遠支援)
        public bool Supports(MatchMode mode)
        {
            switch (mode)
            {
                case MatchMode.TurfWar:
                    return true;
                case MatchMode.SplatZones:
                    return _splatZones != null && _splatZones.Count > 0;
                default:
                    return false;
            }
        }

        public static ModeLayout Load(string sceneName)
        {
            return string.IsNullOrEmpty(sceneName) ? null : Resources.Load<ModeLayout>(ResourceFolder + "/" + sceneName);
        }

        // 某場地是否支援某模式(沒有配置檔的場地只支援塗地)
        public static bool StageSupports(string sceneName, MatchMode mode)
        {
            if (mode == MatchMode.TurfWar)
            {
                return true;
            }
            ModeLayout layout = Load(sceneName);
            return layout != null && layout.Supports(mode);
        }

        // 編輯器產生器寫入
        public void Setup(string sceneName, List<ZoneArea> splatZones)
        {
            _sceneName = sceneName;
            _splatZones = splatZones ?? new List<ZoneArea>();
        }
    }
}

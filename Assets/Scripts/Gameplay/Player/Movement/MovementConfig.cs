using SplatoonC.Core.Locomotion;
using UnityEngine;

namespace SplatoonC.Gameplay.Player
{
    // 移動單元(A4)的設定:Squid Roll / Wall Roll / Squid Surge / Super Jump 的 S3 數值、落點標記與護甲光暈的資產、音效 id。
    // 資產在 Resources/Movement/MovementConfig(MovementSetup 建立、每次重跑寫回程式碼預設值);
    // 執行期由 MovementConfig.Current 取用,資產不存在時退回程式碼預設值(舊場景/選單預覽也能跑)。
    [CreateAssetMenu(fileName = "MovementConfig", menuName = "SplatoonC/移動設定(翻滾・衝牆・超級跳)")]
    public sealed class MovementConfig : ScriptableObject
    {
        public const string ResourcePath = "Movement/MovementConfig";

        [Header("烏賊動作")]
        [SerializeField, Tooltip("翻滾 / 牆滾 / 衝牆(S3 0 AP)")]
        private SquidMoveParams _squidMoves = SquidMoveParams.Splatoon3;

        [Header("超級跳")]
        [SerializeField, Tooltip("蓄力 / 飛行 / 弧線(S3 0 AP)")]
        private SuperJumpParams _superJump = SuperJumpParams.Splatoon3;

        [SerializeField, Tooltip("落點標記 prefab(SuperJumpMarker:地面圈 + 往下箭頭(墨水流乾)+ 名字;所有人都看得到)")]
        private SuperJumpMarker _markerPrefab;

        [SerializeField, Tooltip("落點離目標(隊友、信標、防護罩)至少多遠(公尺):不跟對方的碰撞體重疊")]
        private float _landingClearance = 1.1f;

        [Header("護甲光暈")]
        [SerializeField, Tooltip("護甲外殼材質(半透明、隊伍色由 MaterialPropertyBlock 套;潛伏中也看得到——S3 護甲會露餡)")]
        private Material _armorShellMaterial;

        [SerializeField, Tooltip("護甲外殼網格(留空用內建球)")]
        private Mesh _armorShellMesh;

        [Header("音效 id")]
        [SerializeField, Tooltip("翻滾 / 牆滾")]
        private string _rollSound = "squid_roll";

        [SerializeField, Tooltip("衝牆開始蓄力")]
        private string _surgeChargeSound = "squid_surge_charge";

        [SerializeField, Tooltip("衝牆蓄滿(眼睛閃光)")]
        private string _surgeReadySound = "squid_surge_ready";

        [SerializeField, Tooltip("衝牆上衝")]
        private string _surgeSound = "squid_surge";

        [SerializeField, Tooltip("超級跳開始蓄力")]
        private string _superJumpChargeSound = "super_jump_charge";

        [SerializeField, Tooltip("超級跳起飛")]
        private string _superJumpLaunchSound = "super_jump_launch";

        [SerializeField, Tooltip("落點標記出現(在落點播,附近的敵人聽得到)")]
        private string _superJumpMarkerSound = "super_jump_marker";

        [SerializeField, Tooltip("超級跳著地")]
        private string _superJumpLandSound = "super_jump_land";

        private static MovementConfig _current;
        private static bool _loaded;

        public SquidMoveParams SquidMoves => _squidMoves;
        public SuperJumpParams SuperJump => _superJump;
        public SuperJumpMarker MarkerPrefab => _markerPrefab;
        public float LandingClearance => _landingClearance;
        public Material ArmorShellMaterial => _armorShellMaterial;
        public Mesh ArmorShellMesh => _armorShellMesh;
        public string RollSound => _rollSound;
        public string SurgeChargeSound => _surgeChargeSound;
        public string SurgeReadySound => _surgeReadySound;
        public string SurgeSound => _surgeSound;
        public string SuperJumpChargeSound => _superJumpChargeSound;
        public string SuperJumpLaunchSound => _superJumpLaunchSound;
        public string SuperJumpMarkerSound => _superJumpMarkerSound;
        public string SuperJumpLandSound => _superJumpLandSound;

        // 執行期取用(第一次讀 Resources;沒有資產 → 程式碼預設值的暫存實例)
        public static MovementConfig Current
        {
            get
            {
                if (_current == null || !_loaded)
                {
                    _loaded = true;
                    _current = Resources.Load<MovementConfig>(ResourcePath);
                    if (_current == null)
                    {
                        _current = CreateInstance<MovementConfig>();
                        _current.hideFlags = HideFlags.DontSave;
                    }
                }
                return _current;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _current = null;
            _loaded = false;
        }

        // 編輯器設定(MovementSetup)呼叫:數值寫回 S3 預設(冪等);資產參照由設定腳本另外接
        public void ResetToS3()
        {
            _squidMoves = SquidMoveParams.Splatoon3;
            _superJump = SuperJumpParams.Splatoon3;
            _landingClearance = 1.1f;
            _rollSound = "squid_roll";
            _surgeChargeSound = "squid_surge_charge";
            _surgeReadySound = "squid_surge_ready";
            _surgeSound = "squid_surge";
            _superJumpChargeSound = "super_jump_charge";
            _superJumpLaunchSound = "super_jump_launch";
            _superJumpMarkerSound = "super_jump_marker";
            _superJumpLandSound = "super_jump_land";
        }
    }
}

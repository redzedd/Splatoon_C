using SplatoonC.Core.Combat;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 滾筒(Splat / Carbon / Dynamo Roller)主武器設定。數值是 S3 datamine 原值(S3RollerParams,Leanny v11.3.0):
    // 速度 u/F、時間 F、墨量 = 標準墨槽的分數、傷害 HP;RollerSetup 從 S3RollerParams 寫入,可在 Inspector 微調。
    // 資產:Assets/Resources/Weapons/Main_<id>.asset(執行期 Resources.Load("Weapons/Main_splat_roller"))。
    [CreateAssetMenu(fileName = "Main_splat_roller", menuName = "SplatoonC/滾筒設定")]
    public sealed class RollerConfig : MainWeaponConfig
    {
        [Header("識別")]
        [SerializeField, Tooltip("S3 英文名稱(測試與除錯訊息用)")]
        private string _weaponName = "Splat Roller";

        [SerializeField, Tooltip("輕量級(Carbon Roller;WeaponSpeedType Fast)")]
        private bool _lightWeight;

        [SerializeField, Tooltip("重量級(Dynamo Roller;WeaponSpeedType Slow)")]
        private bool _heavyWeight;

        [Header("S3 參數")]
        [SerializeField, Tooltip("推滾(WeaponRollParam + BodyParam)")]
        private RollerRollParams _roll;

        [SerializeField, Tooltip("地面點按 = 橫甩(WeaponWideSwingParam + WideSwingUnitGroupParam)")]
        private RollerSwingParams _horizontal;

        [SerializeField, Tooltip("空中點按 = 直甩(WeaponVerticalSwingParam + VerticalSwingUnitGroupParam)")]
        private RollerSwingParams _vertical;

        [Header("[EST] 幾何與時序(S3 未公開)")]
        [SerializeField, Tooltip("橫甩墨團基準點:離地高度(公尺)")]
        private float _horizontalSpawnHeight = 1.3f;

        [SerializeField, Tooltip("橫甩墨團基準點:腳前距離(公尺)")]
        private float _horizontalSpawnForward = 0.6f;

        [SerializeField, Tooltip("直甩墨團基準點:離地高度(公尺)")]
        private float _verticalSpawnHeight = 1.5f;

        [SerializeField, Tooltip("直甩墨團基準點:腳前距離(公尺)")]
        private float _verticalSpawnForward = 0.5f;

        [SerializeField, Tooltip("推滾時滾筒軸心在腳前的距離(公尺;接觸判定與塗墨中心)")]
        private float _rollForward = 1.08f;

        [SerializeField, Tooltip("甩出後仍按住:幾幀後滾筒落地開始推滾")]
        private int _rollStartDelayFrames = 6;

        [SerializeField, Tooltip("推滾接觸同一目標再次吃傷害的最短間隔(幀)")]
        private int _contactRehitFrames = 20;

        [Header("外觀與音效")]
        [SerializeField, Tooltip("手上的滾筒模型 prefab(Assets/Prefabs/Weapons/<Name>.prefab)")]
        private RollerModel _modelPrefab;

        [SerializeField, Tooltip("甩墨墨團 prefab(InkBullet;留空用 CombatAssets 的通用墨彈)")]
        private InkBullet _globPrefab;

        [SerializeField, Tooltip("舉起(蓄勢)音效 id")]
        private string _windupSound = "roller_windup";

        [SerializeField, Tooltip("推滾循環音效 id")]
        private string _rollLoopSound = "roller_roll_loop";

        [SerializeField, Tooltip("推滾撞到敵人的音效 id")]
        private string _contactSound = "roller_hit";

        [SerializeField, Tooltip("推滾塗墨硬度(0~1;邊緣銳利度)")]
        private float _rollPaintHardness = 0.8f;

        [SerializeField, Tooltip("甩墨落點塗墨硬度")]
        private float _globPaintHardness = 0.7f;

        [Header("bot")]
        [SerializeField, Tooltip("目標在此距離內:按住(甩完直接推滾輾過去);外面到一擊必殺距離:點按甩墨(公尺)")]
        private float _botRollCommitDistance = 2.4f;

        public string WeaponName => _weaponName;
        public bool LightWeight => _lightWeight;
        public bool HeavyWeight => _heavyWeight;
        public RollerRollParams Roll => _roll;
        public RollerSwingParams Horizontal => _horizontal;
        public RollerSwingParams Vertical => _vertical;
        public float HorizontalSpawnHeight => _horizontalSpawnHeight;
        public float HorizontalSpawnForward => _horizontalSpawnForward;
        public float VerticalSpawnHeight => _verticalSpawnHeight;
        public float VerticalSpawnForward => _verticalSpawnForward;
        public float RollForward => _rollForward;
        public float RollStartDelaySeconds => _rollStartDelayFrames / 60f;
        public float ContactRehitSeconds => _contactRehitFrames / 60f;
        public RollerModel ModelPrefab => _modelPrefab;
        public InkBullet GlobPrefab => _globPrefab;
        public string WindupSound => _windupSound;
        public string RollLoopSound => _rollLoopSound;
        public string ContactSound => _contactSound;
        public float RollPaintHardness => _rollPaintHardness;
        public float GlobPaintHardness => _globPaintHardness;
        public float BotRollCommitDistance => _botRollCommitDistance;

        public RollerSwingParams Swing(bool vertical)
        {
            return vertical ? _vertical : _horizontal;
        }

        public override WeaponClass Class => WeaponClass.Roller;
        public override System.Type BehaviourType => typeof(RollerWeapon);

        // bot 交戰距離 = 橫甩一擊必殺距離(§3.5:Splat 7.2 / Carbon 4.7 / Dynamo 12.9)
        public override float EffectiveRange => Mathf.Max(1f, _horizontal.LethalDistance);

        // 提前量:橫甩主墨團初速(Splat 1.05 u/F = 63 m/s)
        public override float ProjectileSpeed =>
            _horizontal.Units != null && _horizontal.Units.Length > 0 ? _horizontal.Units[0].SpawnSpeed * 60f : 60f;

        // 設定資產上的「攻擊中走速」= 推滾速度(執行期 RollerWeapon 依甩動/推滾/衝刺覆寫)
        public override float MoveSpeedWhileFiring => _roll.SpeedNormal * 60f;

        // 甩墨的攻擊類別:Dynamo = RollerSplash_Heavy,其餘 RollerSplash;推滾接觸固定 RollerContact(RollerWeapon)
        public override SplatoonC.Core.Combat.AttackClass AttackClass =>
            _overrideAttackClass
                ? _attackClass
                : (_heavyWeight ? SplatoonC.Core.Combat.AttackClass.DynamoFlick : SplatoonC.Core.Combat.AttackClass.RollerFlick);

        // 目前設定組成的 S3 參數(測試比對、bot 預估用)
        public S3RollerParams ToParams()
        {
            return new S3RollerParams
            {
                Name = _weaponName,
                LightWeight = _lightWeight,
                HeavyWeight = _heavyWeight,
                Roll = _roll,
                Horizontal = _horizontal,
                Vertical = _vertical,
                HorizontalSpawnHeight = _horizontalSpawnHeight,
                HorizontalSpawnForward = _horizontalSpawnForward,
                VerticalSpawnHeight = _verticalSpawnHeight,
                VerticalSpawnForward = _verticalSpawnForward,
                RollForward = _rollForward,
                RollStartDelayFrames = _rollStartDelayFrames,
                ContactRehitFrames = _contactRehitFrames,
            };
        }

        // 編輯器產生器:寫入 S3 參數、外觀與音效
        public void Setup(in S3RollerParams p, RollerModel modelPrefab, InkBullet globPrefab, string flickSound,
            string windupSound, string rollLoopSound, string contactSound, float botRollCommitDistance)
        {
            _weaponName = p.Name;
            _lightWeight = p.LightWeight;
            _heavyWeight = p.HeavyWeight;
            _roll = p.Roll;
            _horizontal = p.Horizontal;
            _vertical = p.Vertical;
            _horizontalSpawnHeight = p.HorizontalSpawnHeight;
            _horizontalSpawnForward = p.HorizontalSpawnForward;
            _verticalSpawnHeight = p.VerticalSpawnHeight;
            _verticalSpawnForward = p.VerticalSpawnForward;
            _rollForward = p.RollForward;
            _rollStartDelayFrames = p.RollStartDelayFrames;
            _contactRehitFrames = p.ContactRehitFrames;
            _modelPrefab = modelPrefab;
            _globPrefab = globPrefab;
            _fireSound = flickSound;
            _windupSound = windupSound;
            _rollLoopSound = rollLoopSound;
            _contactSound = contactSound;
            _botRollCommitDistance = botRollCommitDistance;
            _moveSpeedWhileFiring = p.Roll.SpeedNormal * 60f;
            _overrideAttackClass = false;
        }
    }
}

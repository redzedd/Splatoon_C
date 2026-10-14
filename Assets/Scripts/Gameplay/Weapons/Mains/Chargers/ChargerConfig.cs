using SplatoonC.Core.Combat;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 狙擊槍(Splat Charger / E-liter 4K)主武器設定。數值是 S3 datamine 原值(S3ChargerParams,Leanny v11.3.0
    // ChargerNormal / ChargerLong):蓄力幀、傷害、射程、彈速、耗墨、塗墨半徑、儲存蓄力;ChargerSetup 從程式碼預設寫入。
    // 資產:Assets/Resources/Weapons/Main_<id>.asset(執行期 Resources.Load("Weapons/Main_splat_charger"))。
    [CreateAssetMenu(fileName = "Main_splat_charger", menuName = "SplatoonC/狙擊槍設定")]
    public sealed class ChargerConfig : MainWeaponConfig
    {
        [Header("S3 參數")]
        [SerializeField, Tooltip("蓄力 / 傷害 / 彈道 / 塗墨 / 儲存蓄力(S3ChargerParams)")]
        private S3ChargerParams _s3;

        [SerializeField, Tooltip("重量級(E-liter 4K;WeaponSpeedType Slow)")]
        private bool _heavyWeight;

        [SerializeField, Tooltip("部分蓄力的攻擊類別(對物件倍率;Splat = Charger、E-liter = Charger_Long)")]
        private AttackClass _partialAttackClass = AttackClass.Charger;

        [SerializeField, Tooltip("滿蓄的攻擊類別(Splat = ChargerFull、E-liter = ChargerFull_Long)")]
        private AttackClass _fullAttackClass = AttackClass.ChargerFull;

        [Header("外觀")]
        [SerializeField, Tooltip("手上的狙擊槍模型 prefab(Assets/Prefabs/Weapons/<Name>.prefab)")]
        private ChargerModel _modelPrefab;

        [SerializeField, Tooltip("子彈 prefab(InkBullet;細長墨彈 + 長拖尾)")]
        private InkBullet _bulletPrefab;

        [SerializeField, Tooltip("雷射瞄準線材質(URP Unlit 半透明;隊伍色由 MaterialPropertyBlock 套上)")]
        private Material _laserMaterial;

        [SerializeField, Tooltip("雷射線寬:槍口 / 末端(公尺)")]
        private Vector2 _laserWidth = new Vector2(0.03f, 0.07f);

        [SerializeField, Tooltip("墨彈拖尾秒數(S3 狙擊彈是一條細長的墨線)")]
        private float _bulletTrailSeconds = 0.045f;

        [SerializeField, Tooltip("沿線墨滴 / 終點塗墨硬度(0~1)")]
        private float _paintHardness = 0.75f;

        [SerializeField, Tooltip("右手握把點相對右肩(槍座標:右、上、前;公尺)——槍托靠在肩上")]
        private Vector3 _gripOffset = new Vector3(0.035f, -0.075f, 0.2f);

        [SerializeField, Tooltip("左手扶槍點相對握把(槍座標;公尺)——托在護木下")]
        private Vector3 _supportOffset = new Vector3(-0.015f, -0.05f, 0.3f);

        [SerializeField, Tooltip("蓄力拉近的 FOV(度;本機玩家,滿蓄時到這個值,0 = 不拉近)[EST] S3 無鏡版不拉近,這裡只輕微拉近")]
        private float _chargeZoomFov = 50f;

        [Header("音效")]
        [SerializeField, Tooltip("部分蓄力開槍音效 id")]
        private string _partialFireSound = "charger_fire_partial";

        [SerializeField, Tooltip("蓄力循環音效 id(音量隨蓄力上升)")]
        private string _chargeLoopSound = "charger_charge_loop";

        [SerializeField, Tooltip("蓄滿提示音效 id")]
        private string _fullChargeSound = "charger_full";

        [SerializeField, Tooltip("命中角色時射手聽到的高音命中聲 id(S3 狙擊 / 爆破槍專屬)")]
        private string _hitSound = "charger_hit";

        [Header("bot")]
        [SerializeField, Tooltip("目標在此距離內:蓄到最小蓄力以上就放(近身不等滿蓄)(公尺)")]
        private float _botCloseRange = 7f;

        [SerializeField, Tooltip("滿蓄後雷射一直沒掃到敵人時,最多再憋幾秒就在準心大致對上時放(秒)")]
        private float _botMaxHoldFullSeconds = 0.9f;

        [SerializeField, Tooltip("偏好高處的程度(挑塗地/移動目標時每 3 m 高低差的加分;0 = 不在意)")]
        private float _botHighGroundBias = 0.5f;

        public ref readonly S3ChargerParams S3 => ref _s3;
        public string WeaponName => _s3.Name;
        public bool HeavyWeight => _heavyWeight;
        public AttackClass PartialAttackClass => _partialAttackClass;
        public AttackClass FullAttackClass => _fullAttackClass;
        public ChargerModel ModelPrefab => _modelPrefab;
        public InkBullet BulletPrefab => _bulletPrefab;
        public Material LaserMaterial => _laserMaterial;
        public Vector2 LaserWidth => _laserWidth;
        public float BulletTrailSeconds => _bulletTrailSeconds;
        public float PaintHardness => _paintHardness;
        public Vector3 GripOffset => _gripOffset;
        public Vector3 SupportOffset => _supportOffset;
        public float ChargeZoomFov => _chargeZoomFov;
        public string PartialFireSound => _partialFireSound;
        public string ChargeLoopSound => _chargeLoopSound;
        public string FullChargeSound => _fullChargeSound;
        public string HitSound => _hitSound;
        public float BotCloseRange => _botCloseRange;
        public float BotMaxHoldFullSeconds => _botMaxHoldFullSeconds;
        public float BotHighGroundBias => _botHighGroundBias;

        public override WeaponClass Class => WeaponClass.Charger;
        public override System.Type BehaviourType => typeof(ChargerWeapon);

        // 交戰距離 = 滿蓄射程(含槍口;Splat 25.75 / E-liter 30.76,§3.5)
        public override float EffectiveRange => _s3.ReachFromPlayer(_s3.FullChargeFrames);

        // 提前量:滿蓄彈速(Splat 4.8 u/F = 288 m/s、E-liter 9.6 u/F = 576 m/s)
        public override float ProjectileSpeed => _s3.SpeedFull * 60f;

        // 設定資產上的「攻擊中走速」= 滿蓄走速(執行期 ChargerWeapon 依蓄力比例覆寫)
        public override float MoveSpeedWhileFiring => _s3.MoveSpeedFullCharge * 60f;

        // 主攻擊類別 = 部分蓄力那一列(滿蓄另用 FullAttackClass)
        public override AttackClass AttackClass => _overrideAttackClass ? _attackClass : _partialAttackClass;

        // 編輯器產生器:寫入 S3 參數、外觀與音效
        public void Setup(in S3ChargerParams s3, bool heavyWeight, AttackClass partialClass, AttackClass fullClass,
            ChargerModel modelPrefab, InkBullet bulletPrefab, Material laserMaterial, string fireSound,
            Vector3 gripOffset, Vector3 supportOffset, float chargeZoomFov, float botCloseRange)
        {
            _s3 = s3;
            _heavyWeight = heavyWeight;
            _partialAttackClass = partialClass;
            _fullAttackClass = fullClass;
            _modelPrefab = modelPrefab;
            _bulletPrefab = bulletPrefab;
            _laserMaterial = laserMaterial;
            _fireSound = fireSound;
            _gripOffset = gripOffset;
            _supportOffset = supportOffset;
            _chargeZoomFov = chargeZoomFov;
            _botCloseRange = botCloseRange;
            _moveSpeedWhileFiring = s3.MoveSpeedFullCharge * 60f;
            _overrideAttackClass = false;
        }
    }
}

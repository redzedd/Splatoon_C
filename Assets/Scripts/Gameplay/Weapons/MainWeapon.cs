using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 主武器設定基底:每個武器種類一個子類(射擊槍 = WeaponConfig、滾筒、狙擊…),
    // 並指明它的執行期行為元件型別,WeaponLoadout 依此在角色身上換裝。
    public abstract class MainWeaponConfig : ScriptableObject
    {
        [SerializeField, Tooltip("開火(攻擊中)時的走速(公尺/秒;S3 Splattershot 0.072 u/F = 4.32)")]
        protected float _moveSpeedWhileFiring = 4.32f;

        [SerializeField, Tooltip("攻擊音效 id(Assets/Audio/audio_manifest.json;變體組名會隨機挑)")]
        protected string _fireSound = "shoot_shooter";

        [SerializeField, Tooltip("覆寫攻擊類別(對物件傷害倍率);關閉 = 依武器種類推得(.52 Gal、Pro、Luna 等要打開指定)")]
        protected bool _overrideAttackClass;

        [SerializeField, Tooltip("攻擊類別(SplatoonC.Core.Combat.DamageRates 的列;只有勾選覆寫時使用)")]
        protected SplatoonC.Core.Combat.AttackClass _attackClass = SplatoonC.Core.Combat.AttackClass.Shooter;

        public string FireSound => _fireSound;

        public abstract WeaponClass Class { get; }

        // 主攻擊的攻擊類別(DamageInfo.AttackClass):物件受擊時查 DamageRates。
        // 預設依武器種類;同種類內有差異的(.52 Gal = HeavyShooter、Luna = LunaBlaster、E-liter…)用覆寫欄位或子類覆寫。
        public virtual SplatoonC.Core.Combat.AttackClass AttackClass =>
            _overrideAttackClass ? _attackClass : DefaultAttackClass(Class);

        public static SplatoonC.Core.Combat.AttackClass DefaultAttackClass(WeaponClass weaponClass)
        {
            switch (weaponClass)
            {
                case WeaponClass.Roller: return SplatoonC.Core.Combat.AttackClass.RollerFlick;
                case WeaponClass.Charger: return SplatoonC.Core.Combat.AttackClass.Charger;
                case WeaponClass.Slosher: return SplatoonC.Core.Combat.AttackClass.Slosher;
                case WeaponClass.Splatling: return SplatoonC.Core.Combat.AttackClass.Splatling;
                case WeaponClass.Dualies: return SplatoonC.Core.Combat.AttackClass.Dualies;
                case WeaponClass.Brella: return SplatoonC.Core.Combat.AttackClass.BrellaShot;
                case WeaponClass.Blaster: return SplatoonC.Core.Combat.AttackClass.Blaster;
                case WeaponClass.Brush: return SplatoonC.Core.Combat.AttackClass.BrushFlick;
                case WeaponClass.Stringer: return SplatoonC.Core.Combat.AttackClass.Stringer;
                case WeaponClass.Splatana: return SplatoonC.Core.Combat.AttackClass.SplatanaWave;
                default: return SplatoonC.Core.Combat.AttackClass.Shooter;
            }
        }

        // 對應的 MainWeapon 子類(必須有無參數建構)
        public abstract System.Type BehaviourType { get; }

        // 有效射程(公尺):bot 交戰距離、準星鎖定判定
        public abstract float EffectiveRange { get; }

        // 主要投射物初速(公尺/秒):bot 提前量;近戰武器回傳大值
        public virtual float ProjectileSpeed => 60f;

        public virtual float MoveSpeedWhileFiring => _moveSpeedWhileFiring;

        // 墨槽容量倍率(S3:標準 1.0;Splattershot Jr. 1.1)。所有耗墨(主/副)都以標準墨槽為單位,
        // 容量大就佔得少;WeaponLoadout 換裝時套到 PlayerInkTank。
        public virtual float InkTankScale => 1f;
    }

    // 主武器執行期行為基底:統一取得輸入、視線、墨槽、烏賊狀態,並統一「此刻能不能用主武器」的規則
    //(烏賊態、按住副武器、大招佔用時都不能開火)。子類只寫該種類的攻擊方式。
    public abstract class MainWeapon : MonoBehaviour
    {
        protected Inkling Owner { get; private set; }
        protected PlayerInputRouter Input { get; private set; }
        protected PlayerInkTank Tank { get; private set; }
        protected SquidController Squid { get; private set; }
        protected CharacterController Controller { get; private set; }
        protected PlayerSpecialGauge SpecialGauge { get; private set; }

        public abstract MainWeaponConfig BaseConfig { get; }

        // 換裝:設定資產與必要的 prefab 參照(WeaponLoadout 呼叫)
        public abstract void Configure(MainWeaponConfig config);

        // 攻擊中(開火/蓄力/揮動):走速改用 MoveSpeedWhileFiring、加速度加倍、墨量鎖
        public virtual bool IsAttacking => false;

        // 此刻不能變烏賊(SquidController 讀):例如雙槍翻滾後的硬直(S3 UnrelaxFrameNoSquid)、開火後的 PostDelay。
        // 預設 false = 不影響烏賊化(既有武器行為不變)
        public virtual bool BlocksSquid => false;

        // 此刻不能丟副武器(ThrowController 讀;視同按鍵沒按,硬直過後仍按著就開始瞄準):例如爆破槍出膛後的
        // PostDelayFrame_Blaster(S3 22F / 26F)。預設 false = 不影響副武器(既有武器行為不變)
        public virtual bool BlocksSubWeapon => false;

        // 實際出手次數(每射出一發/每揮出一下 +1):角色動畫輪詢它做後座力,零配置、換裝後不必重新訂閱。
        // 子類在真正出手時呼叫 NotifyAttack();沒呼叫的武器只是少了後座力,不影響其他行為。
        public int AttackCount { get; private set; }

        protected void NotifyAttack()
        {
            AttackCount++;
        }

        // ── bot 用法(子類依武器種類覆寫)──

        // 交戰時這一幀要不要按住攻擊。預設(連射類):看得到、反應過、在射程內、準心對上就按住。
        // 蓄力類覆寫成「按住蓄力 → 滿了且對準才放開」。
        public virtual bool BotAttackIntent(in BotCombatContext context)
        {
            return context.TargetVisible && context.Reacted && context.Distance <= EffectiveRange * 1.1f
                   && context.AimError < context.FireTolerance;
        }

        // 交戰時視線要對準的點(世界座標;BotBrain 算好提前量之後詢問)。預設 = context.AimPoint(目標胸口 + 提前量,原行為)。
        // 範圍攻擊類覆寫:爆破槍近距離瞄腳邊、目標躲到掩體後瞄掩體上方讓彈在目標頭上空爆(此時 BotAttackIntent 可在
        // TargetVisible = false 時仍回傳 true)
        public virtual Vector3 BotAimPoint(in BotAimContext context)
        {
            return context.AimPoint;
        }

        // 交戰時想維持的距離(公尺)
        public virtual float BotPreferredDistance => EffectiveRange * 0.72f;

        // 看到敵人後進入交戰(接近/開火)的距離(公尺)。預設 = 射程 × 1.35;
        // 近戰型(滾筒:一擊必殺距離短)覆寫得比射程遠很多,才會主動接近/潛游繞過去,而不是被遠程武器單方面點掉
        public virtual float BotEngageRange => EffectiveRange * 1.35f;

        // 交戰中腳下是自家墨時,距離目標超過此值就潛游接近(公尺)。預設 = 射程 × 1.2;
        // 伏擊型(滾筒)設得比射程短:潛到一擊必殺距離內才浮出來甩,不在射程外現身挨打
        public virtual float BotSwimApproachDistance => EffectiveRange * 1.2f;

        // 塗地時瞄準前方多遠的地面(公尺);近戰/滾筒類覆寫成貼地
        public virtual float BotPaintAimDistance => Mathf.Clamp(EffectiveRange * 0.65f, 3f, 9f);

        // 塗地時要不要按住攻擊(預設:有墨就按)
        public virtual bool BotPaintIntent(float inkRatio)
        {
            return inkRatio > 0.05f;
        }

        // 回墨時附近沒有自家墨:往腳邊塗一點再潛(BotBrain 看著腳下)。預設 = 有墨就按住;
        // 蓄力類覆寫成「按一下 → 蓄到能出手就放」,否則一直按住蓄力永遠不會出墨
        public virtual bool BotFeetPaintIntent(float inkRatio)
        {
            return inkRatio > 0.02f;
        }

        // 撤退中回頭補槍(只在沒潛游時問)。預設 = 準心誤差在容許值 1.5 倍內就按住;蓄力類覆寫成蓄力 → 放開
        public virtual bool BotRetreatAttackIntent(in BotCombatContext context)
        {
            return context.AimError < context.FireTolerance * 1.5f;
        }

        // 偏好高處的程度(BotDirector 挑塗地/移動目標時,每 3 m 高低差的加分;0 = 不在意)。
        // 狙擊類設正值:bot 往高台、長視線的位置走(S3 狙擊手站高點)
        public virtual float BotHighGroundBias => 0f;

        // 潛伏中仍然露出位置(S3:按住攻擊潛墨儲存蓄力時頭上有白圈,Ninja Squid 也藏不住)。
        // BotBrain 感知敵人時當成「看得到」;預設 false
        public virtual bool RevealsSubmerged => false;

        // 人形起跳高度倍率(PlayerLocomotion 乘進 jumpHeightMultiplier)。預設 1;
        // 蓄力類覆寫(S3 狙擊槍滿蓄起跳初速 0.07 vs 0.11 u/F → 高度 × 0.405)
        public virtual float JumpHeightFactor => 1f;

        // 攻擊中的走速(公尺/秒)。預設讀設定資產;動作分段的武器(滾筒:甩動 / 推滾 / 衝刺)覆寫成當下的值
        public virtual float MoveSpeedWhileFiring => BaseConfig != null ? BaseConfig.MoveSpeedWhileFiring : 4.32f;

        // 攻擊中走速可否超過跑速(滾筒推滾 6.48–9.6 m/s、筆刷奔跑)。預設 false:PlayerLocomotion 把攻擊走速夾在跑速以內
        public virtual bool AttackMoveSpeedCanExceedRun => false;
        public float EffectiveRange => BaseConfig != null ? BaseConfig.EffectiveRange : 10f;
        public float ProjectileSpeed => BaseConfig != null ? BaseConfig.ProjectileSpeed : 60f;
        public WeaponClass Class => BaseConfig != null ? BaseConfig.Class : WeaponClass.Shooter;

        protected virtual void Awake()
        {
            CacheReferences();
        }

        protected void CacheReferences()
        {
            Owner = GetComponent<Inkling>();
            Input = GetComponent<PlayerInputRouter>();
            Tank = GetComponent<PlayerInkTank>();
            Squid = GetComponent<SquidController>();
            Controller = GetComponent<CharacterController>();
            SpecialGauge = GetComponent<PlayerSpecialGauge>();
        }

        // 這一幀玩家「想攻擊且被允許攻擊」
        protected bool WantsAttack
        {
            get
            {
                if (Input == null || !Input.AttackHeld)
                {
                    return false;
                }
                return CanUseMainWeapon;
            }
        }

        protected bool CanUseMainWeapon
        {
            get
            {
                if (Squid != null && Squid.IsSquid)
                {
                    return false;
                }
                if (Input != null && Input.SubWeaponHeld)
                {
                    return false;
                }
                SpecialWeapon special = SpecialGauge != null ? SpecialGauge.ActiveSpecial : null;
                return special == null || !special.BlocksMainWeapon;
            }
        }

        // 找角色身上名為 name 的子物件(Muzzle、CameraPivot…),深度優先
        protected Transform FindChild(string childName)
        {
            return FindRecursive(transform, childName);
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
    }
}

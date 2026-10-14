using System;

namespace SplatoonC.Core.Combat
{
    // HUD 狀態圖示列的旗標(本機玩家頭上/畫面上的「被標記」「減速」「強化」)
    [Flags]
    public enum StatusFlags : byte
    {
        None = 0,
        // 被敵方標記(Point Sensor / Ink Mine / Angle Shooter / Wave Breaker / 毒霧曝光)
        Marked = 1,
        // 減速類負面效果(毒霧)
        Slowed = 2,
        // 增益(能量站)
        Buffed = 4,
    }

    // 一組暫時 AP(各能力 0~57)。以欄位而非陣列存,整個 struct 可直接複製、零配置。
    public struct AbilityPoints
    {
        private byte _runSpeed;
        private byte _swimSpeed;
        private byte _inkRecovery;
        private byte _inkResistance;
        private byte _specialCharge;
        private byte _specialSaver;
        private byte _quickRespawn;
        private byte _quickSuperJump;
        private byte _intensifyAction;

        public int this[Ability ability]
        {
            get
            {
                switch (ability)
                {
                    case Ability.RunSpeedUp: return _runSpeed;
                    case Ability.SwimSpeedUp: return _swimSpeed;
                    case Ability.InkRecoveryUp: return _inkRecovery;
                    case Ability.InkResistanceUp: return _inkResistance;
                    case Ability.SpecialChargeUp: return _specialCharge;
                    case Ability.SpecialSaver: return _specialSaver;
                    case Ability.QuickRespawn: return _quickRespawn;
                    case Ability.QuickSuperJump: return _quickSuperJump;
                    case Ability.IntensifyAction: return _intensifyAction;
                    default: return 0;
                }
            }
            set
            {
                byte v = (byte)Math.Max(0, Math.Min((int)AbilityCurve.MaxAp, value));
                switch (ability)
                {
                    case Ability.RunSpeedUp: _runSpeed = v; break;
                    case Ability.SwimSpeedUp: _swimSpeed = v; break;
                    case Ability.InkRecoveryUp: _inkRecovery = v; break;
                    case Ability.InkResistanceUp: _inkResistance = v; break;
                    case Ability.SpecialChargeUp: _specialCharge = v; break;
                    case Ability.SpecialSaver: _specialSaver = v; break;
                    case Ability.QuickRespawn: _quickRespawn = v; break;
                    case Ability.QuickSuperJump: _quickSuperJump = v; break;
                    case Ability.IntensifyAction: _intensifyAction = v; break;
                }
            }
        }

        public bool Any
        {
            get
            {
                return (_runSpeed | _swimSpeed | _inkRecovery | _inkResistance | _specialCharge | _specialSaver
                        | _quickRespawn | _quickSuperJump | _intensifyAction) != 0;
            }
        }
    }

    // 一個效果來源對角色的修正。預設值(default)= 完全不影響:倍率欄位以「倍率 − 1」存,
    // 所以 default(ModifierSpec) 的各倍率都是 1,不會因為忘了初始化把角色速度變 0。
    // 用 With* 串接建立(回傳修改後的副本),例:ModifierSpec.None.WithRunSpeed(0.5f).WithInkRefill(0f)。
    public struct ModifierSpec
    {
        private float _runSpeedDelta;
        private float _swimSpeedDelta;
        private float _inkRefillDelta;
        private float _inkCostDelta;
        private float _jumpHeightDelta;
        private float _respawnTimeDelta;
        private float _specialChargeDelta;
        private AbilityPoints _abilities;

        // 人形/乾地烏賊/敵墨中的移動速度倍率(毒霧 [EST] 0.5)
        public float RunSpeed => 1f + _runSpeedDelta;
        // 自家墨中游速倍率(地面游與爬牆)
        public float SwimSpeed => 1f + _swimSpeedDelta;
        // 回墨速率倍率(0 = 停止回墨)
        public float InkRefill => 1f + _inkRefillDelta;
        // 耗墨倍率(Zipcaster 主武器 ×0.67)
        public float InkCost => 1f + _inkCostDelta;
        // 起跳高度倍率
        public float JumpHeight => 1f + _jumpHeightDelta;
        // 重生等待倍率(套在整段等待上)
        public float RespawnTime => 1f + _respawnTimeDelta;
        // 大招充能倍率
        public float SpecialCharge => 1f + _specialChargeDelta;

        // 持續扣墨(每秒扣掉墨槽的比例;不會扣到 0 以下)
        public float InkDrainPerSecond { get; private set; }
        // 這個來源最多扣掉多少墨槽比例(≤0 = 不限;毒霧整場約 40%)
        public float InkDrainLimit { get; private set; }
        // 停止回血(Ink Storm 雲下)
        public bool NoHealthRegen { get; private set; }
        // 人形也以潛墨速率回血(Crab Tank 滾球模式;S3 v6.1.0 Ink Storm 雲下的隊友)
        public bool SwimRegen { get; private set; }
        // 踩敵墨不扣血(Crab Tank:坦克壓著敵墨也不會陷進去)
        public bool NoSlipDamage { get; private set; }
        // 死亡後仍保留(預設死亡清除;S3 能量站增益死亡失效)
        public bool Persistent { get; private set; }
        // HUD 狀態圖示
        public StatusFlags Status { get; private set; }

        public AbilityPoints Abilities => _abilities;

        public static ModifierSpec None => default;

        public ModifierSpec WithRunSpeed(float multiplier)
        {
            ModifierSpec s = this;
            s._runSpeedDelta = multiplier - 1f;
            return s;
        }

        public ModifierSpec WithSwimSpeed(float multiplier)
        {
            ModifierSpec s = this;
            s._swimSpeedDelta = multiplier - 1f;
            return s;
        }

        public ModifierSpec WithInkRefill(float multiplier)
        {
            ModifierSpec s = this;
            s._inkRefillDelta = multiplier - 1f;
            return s;
        }

        public ModifierSpec WithInkCost(float multiplier)
        {
            ModifierSpec s = this;
            s._inkCostDelta = multiplier - 1f;
            return s;
        }

        public ModifierSpec WithJumpHeight(float multiplier)
        {
            ModifierSpec s = this;
            s._jumpHeightDelta = multiplier - 1f;
            return s;
        }

        public ModifierSpec WithRespawnTime(float multiplier)
        {
            ModifierSpec s = this;
            s._respawnTimeDelta = multiplier - 1f;
            return s;
        }

        public ModifierSpec WithSpecialCharge(float multiplier)
        {
            ModifierSpec s = this;
            s._specialChargeDelta = multiplier - 1f;
            return s;
        }

        public ModifierSpec WithInkDrain(float fractionPerSecond, float limit = 0f)
        {
            ModifierSpec s = this;
            s.InkDrainPerSecond = Math.Max(0f, fractionPerSecond);
            s.InkDrainLimit = limit;
            return s;
        }

        public ModifierSpec WithoutHealthRegen()
        {
            ModifierSpec s = this;
            s.NoHealthRegen = true;
            return s;
        }

        public ModifierSpec WithSwimRegen()
        {
            ModifierSpec s = this;
            s.SwimRegen = true;
            return s;
        }

        public ModifierSpec WithoutSlipDamage()
        {
            ModifierSpec s = this;
            s.NoSlipDamage = true;
            return s;
        }

        public ModifierSpec WithAbility(Ability ability, int ap)
        {
            ModifierSpec s = this;
            s._abilities[ability] = ap;
            return s;
        }

        public ModifierSpec WithStatus(StatusFlags status)
        {
            ModifierSpec s = this;
            s.Status = status;
            return s;
        }

        public ModifierSpec AsPersistent()
        {
            ModifierSpec s = this;
            s.Persistent = true;
            return s;
        }

        // ── S3 效果預設(數值出處見各行註解;後續武器單元直接套用)──

        // 能量站飲用增益持續 1020F(17 s;SPU 另計)
        public const float TacticoolerSeconds = 1020f / 60f;

        // 能量站(Tacticooler,§5.2 / research/04 §2.15):SSU、RSU 29 AP;Intensify、QSJ、QR、Special Saver、墨抗 57 AP。
        // 死亡失效(非 Persistent);飲用者會發光(另由大招單元處理)。
        public static ModifierSpec Tacticooler => None
            .WithAbility(Ability.RunSpeedUp, 29)
            .WithAbility(Ability.SwimSpeedUp, 29)
            .WithAbility(Ability.IntensifyAction, 57)
            .WithAbility(Ability.QuickSuperJump, 57)
            .WithAbility(Ability.QuickRespawn, 57)
            .WithAbility(Ability.SpecialSaver, 57)
            .WithAbility(Ability.InkResistanceUp, 57)
            .WithStatus(StatusFlags.Buffed);

        // 毒霧(Toxic Mist,§4.3 / research/03 §2.12):霧中跑/游變慢([EST] ×0.5,S3 未公開)、停止回墨、
        // 持續扣墨(S3 分段加速扣,整團霧約 40% 上限 → 連續近似 8%/s、上限 40% [EST])。離開霧立刻恢復。
        public static ModifierSpec ToxicMist => None
            .WithRunSpeed(0.5f)
            .WithSwimSpeed(0.5f)
            .WithInkRefill(0f)
            .WithInkDrain(0.08f, 0.4f)
            .WithStatus(StatusFlags.Slowed);

        // Zipcaster(§5.2):主武器耗墨 ×0.67、不回墨、每秒扣 15%(大招期間不能用副武器,耗墨倍率全域套用等價)
        public static ModifierSpec ZipcasterInk => None
            .WithInkCost(0.67f)
            .WithInkRefill(0f)
            .WithInkDrain(0.15f);
    }
}

using System;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Combat;
using UnityEngine;

namespace SplatoonC.Gameplay.Player
{
    // 角色身上暫時效果的唯一持有者:毒霧減速/扣墨、能量站增益(AP)、Zipcaster 墨量規則、Ink Storm 雲下不回血…
    // 效果來源以「物件」為鍵 Add(來源, spec, 秒數)/ Remove(來源);同來源再 Add = 刷新。秒數 ≤ 0 = 直到移除。
    // 消費端(PlayerLocomotion、PlayerInkTank、InklingHealth、PlayerSpecialGauge、InklingLifecycle)讀下面的換算值;
    // 沒有任何效果時全部回傳 1 / 原值,行為與加入本元件前完全相同(常駐裝備 GearSwimSpeedAp 例外:潛游一律套用)。
    // 由 Inkling.Awake 自動掛上(不改 prefab);被擊倒時清掉非 Persistent 效果與身上的標記(S3:能量站增益死亡失效)。
    [DisallowMultipleComponent]
    public sealed class InklingModifiers : MonoBehaviour
    {
        private readonly ModifierStack _stack = new ModifierStack();
        private Inkling _inkling;
        private InklingLifecycle _life;
        private WeaponLoadout _loadout;
        private Action<DamageInfo> _onDied;
        private int _syncedFrame = -1;

        // 效果增減、到期、死亡清除時觸發(HUD 可訂閱;每幀讀值者不需要)
        public event Action Changed;

        public int Count
        {
            get
            {
                Sync();
                return _stack.Count;
            }
        }

        public int Version
        {
            get
            {
                Sync();
                return _stack.Version;
            }
        }

        // 取得(沒有就掛上)——消費端第一次使用時呼叫,不依賴 Awake 順序
        public static InklingModifiers For(Component component)
        {
            if (component == null)
            {
                return null;
            }
            var existing = component.GetComponent<InklingModifiers>();
            return existing != null ? existing : component.gameObject.AddComponent<InklingModifiers>();
        }

        private void Awake()
        {
            _inkling = GetComponent<Inkling>();
            _life = GetComponent<InklingLifecycle>();
            _loadout = GetComponent<WeaponLoadout>();
            _onDied = OnDied;
        }

        private void OnEnable()
        {
            if (_life != null)
            {
                _life.Died += _onDied;
            }
        }

        private void OnDisable()
        {
            if (_life != null)
            {
                _life.Died -= _onDied;
            }
        }

        private void Update()
        {
            Sync();
        }

        // 同一幀只清一次到期效果;讀值端先呼叫,不受腳本執行順序影響
        private void Sync()
        {
            int frame = Time.frameCount;
            if (frame == _syncedFrame)
            {
                return;
            }
            _syncedFrame = frame;
            if (_stack.Tick(Time.time) > 0)
            {
                Changed?.Invoke();
            }
        }

        // ── 增減 ──

        public void Add(object source, in ModifierSpec spec, float seconds)
        {
            _stack.Add(source, spec, Time.time, seconds);
            Changed?.Invoke();
        }

        public bool Remove(object source)
        {
            if (!_stack.Remove(source))
            {
                return false;
            }
            Changed?.Invoke();
            return true;
        }

        public bool Has(object source)
        {
            Sync();
            return _stack.Contains(source);
        }

        // 剩餘秒數(沒有 = 0,永久 = +∞)
        public float Remaining(object source)
        {
            Sync();
            return _stack.Remaining(source, Time.time);
        }

        public void ClearAll()
        {
            if (_stack.Count == 0)
            {
                return;
            }
            _stack.Clear();
            Changed?.Invoke();
        }

        // 被擊倒:非 Persistent 效果全部失效,身上的標記也清掉(§4.4 標記在死亡時結束)
        private void OnDied(DamageInfo info)
        {
            int removed = _stack.ClearNonPersistent();
            Marking.Clear(_inkling);
            if (removed > 0)
            {
                Changed?.Invoke();
            }
        }

        // ── 聚合原值 ──

        public float RunSpeedMultiplier { get { Sync(); return _stack.RunSpeed; } }
        public float SwimSpeedMultiplier { get { Sync(); return _stack.SwimSpeed; } }
        public float InkRefillMultiplier { get { Sync(); return _stack.InkRefill; } }
        public float InkCostMultiplier { get { Sync(); return _stack.InkCost; } }
        public float JumpHeightMultiplier { get { Sync(); return _stack.JumpHeight; } }
        public float RespawnTimeMultiplier { get { Sync(); return _stack.RespawnTime; } }
        public float SpecialChargeMultiplier { get { Sync(); return _stack.SpecialCharge; } }
        public float InkDrainPerSecond { get { Sync(); return _stack.InkDrainPerSecond; } }
        public bool NoHealthRegen { get { Sync(); return _stack.NoHealthRegen; } }
        public bool SwimRegen { get { Sync(); return _stack.SwimRegen; } }
        public bool NoSlipDamage { get { Sync(); return _stack.NoSlipDamage; } }
        public StatusFlags Status { get { Sync(); return _stack.Status; } }

        public int AbilityPoints(Ability ability)
        {
            Sync();
            return _stack.AbilityPoints(ability);
        }

        // 重量級別索引(0 輕、1 中、2 重):RSU/SSU 曲線依級別不同(57 AP 時三級同速)
        private int WeightIndex => _loadout != null && _loadout.Kit != null ? (int)_loadout.Kit.Weight : 1;

        // 常駐裝備的游泳速度提升 AP(PlayerLocomotion 從設定寫入;與暫時效果的 AP 相加,總和上限 57)
        public int GearSwimSpeedAp { get; set; }

        // 裝備帶來的游速倍率(相對本重量級 0 AP 游速;不含暫時效果)
        public float GearSwimRatio => AbilityCurve.SwimSpeedRatio(GearSwimSpeedAp, WeightIndex);

        // ── 給各系統的 S3 換算 ──

        // 移動速度倍率(乘在 PlayerLocomotion 既有的烏賊/重量/開火/投擲倍率之後):
        //   游泳(自家墨地面):SSU 游速曲線 × SwimSpeed×
        //   其他:RunSpeed× 再乘 ——人形敵墨中:墨抗曲線(RSU 無效);投擲姿勢:固定不吃 RSU;開火:MoveVelRt_Shot;走路:RSU 跑速曲線
        public float MoveSpeedFactor(bool swimming, bool humanoid, bool firing, bool throwStance, bool onEnemyInk)
        {
            Sync();
            if (swimming)
            {
                // 裝備 AP 常駐,沒有暫時效果時也要套
                float ap = Mathf.Min(AbilityCurve.MaxAp, _stack.AbilityPoints(Ability.SwimSpeedUp) + GearSwimSpeedAp);
                return _stack.SwimSpeed * AbilityCurve.SwimSpeedRatio(ap, WeightIndex);
            }
            if (_stack.Count == 0)
            {
                return 1f;
            }
            float factor = _stack.RunSpeed;
            if (!humanoid)
            {
                // 乾地烏賊:S3 固定速度,裝備不影響
                return factor;
            }
            if (onEnemyInk)
            {
                float inkRes = _stack.AbilityPoints(Ability.InkResistanceUp);
                return factor * (firing ? AbilityCurve.EnemyInkShotWalkRatio(inkRes) : AbilityCurve.EnemyInkWalkRatio(inkRes));
            }
            if (throwStance)
            {
                return factor;
            }
            float rsu = _stack.AbilityPoints(Ability.RunSpeedUp);
            return factor * (firing ? AbilityCurve.ShotMoveRatio(rsu) : AbilityCurve.RunSpeedRatio(rsu, WeightIndex));
        }

        // 爬牆(牆上游)速度倍率:只吃 SwimSpeed×(SSU 據社群實測不影響牆上游速)
        public float ClimbSpeedFactor
        {
            get
            {
                Sync();
                return _stack.SwimSpeed;
            }
        }

        // 起跳高度倍率(含敵墨):敵墨中 = 墨抗曲線 (初速/0.11)²,沒有墨抗時沿用設定值;再乘 JumpHeight×
        public float JumpHeightFactor(bool onEnemyInk, float enemyInkBaseMultiplier)
        {
            Sync();
            float multiplier = 1f;
            if (onEnemyInk)
            {
                float inkRes = _stack.AbilityPoints(Ability.InkResistanceUp);
                multiplier = inkRes > 0f
                    ? Mathf.Max(enemyInkBaseMultiplier, AbilityCurve.EnemyInkJumpHeightRatio(inkRes))
                    : enemyInkBaseMultiplier;
            }
            return multiplier * _stack.JumpHeight;
        }

        // 回墨速率倍率:IRU 曲線(潛墨 180F → 117F、非潛墨 600F → 220F)× InkRefill×
        public float InkRefillFactor(bool submerged)
        {
            Sync();
            if (_stack.Count == 0)
            {
                return 1f;
            }
            return _stack.InkRefill * AbilityCurve.InkRecoveryRatio(_stack.AbilityPoints(Ability.InkRecoveryUp), submerged);
        }

        // 本幀要持續扣掉的墨量(墨槽比例;各來源扣到自己的上限為止)
        public float ConsumeInkDrain(float deltaTime)
        {
            Sync();
            return _stack.ConsumeDrain(deltaTime);
        }

        // 大招充能倍率:SCU 曲線(×1.0 → ×1.3)× SpecialCharge×
        public float SpecialChargeFactor
        {
            get
            {
                Sync();
                if (_stack.Count == 0)
                {
                    return 1f;
                }
                return _stack.SpecialCharge
                       * AbilityCurve.SpecialChargeRate.Evaluate(_stack.AbilityPoints(Ability.SpecialChargeUp));
            }
        }

        // 被擊倒時大招保留比例:Special Saver 曲線(0 AP 0.5 → 57 AP 1.0),不低於設定值
        public float SpecialKeepOnDeath(float baseKeep)
        {
            Sync();
            float ap = _stack.AbilityPoints(Ability.SpecialSaver);
            return ap > 0f ? Mathf.Max(baseKeep, AbilityCurve.SpecialKeep.Evaluate(ap)) : baseKeep;
        }

        // 重生等待秒數:被敵人擊倒時扣掉 Quick Respawn 縮短的自身鏡頭 + 擊殺鏡頭(57 AP:450F → 210F);
        // 掉落等環境死亡不適用 QR(S3)。能量站給的 QR 不看「上次死後有沒有擊倒人」的條件 [EST 依 Inkipedia 描述]。
        public float RespawnDelay(float baseSeconds, bool enemyCaused)
        {
            Sync();
            if (_stack.Count == 0)
            {
                return baseSeconds;
            }
            float seconds = baseSeconds;
            if (enemyCaused)
            {
                seconds -= AbilityCurve.QuickRespawnSavedSeconds(_stack.AbilityPoints(Ability.QuickRespawn));
            }
            return Mathf.Max(0.5f, seconds * _stack.RespawnTime);
        }

        // 血量規則:墨抗調整敵墨持續傷害(上限 40 → 20、每幀 0.3 → 0.1);NoSlipDamage 不扣敵墨血;
        // SwimRegen 人形也以潛墨速率回血;NoHealthRegen 停止回血(優先)
        public VitalsSettings AdjustVitals(in VitalsSettings baseSettings)
        {
            Sync();
            VitalsSettings settings = baseSettings;
            if (_stack.Count == 0)
            {
                return settings;
            }
            float inkRes = _stack.AbilityPoints(Ability.InkResistanceUp);
            if (inkRes > 0f)
            {
                settings.SlipDamageCap = baseSettings.SlipDamageCap
                                         * AbilityCurve.EnemyInkDamageCap.Evaluate(inkRes) / AbilityCurve.EnemyInkDamageCap.Low;
                settings.SlipDamagePerSecond = baseSettings.SlipDamagePerSecond
                                               * AbilityCurve.EnemyInkDamagePerSecond(inkRes)
                                               / AbilityCurve.EnemyInkDamagePerSecond(0f);
            }
            if (_stack.NoSlipDamage)
            {
                settings.SlipDamagePerSecond = 0f;
            }
            if (_stack.SwimRegen)
            {
                // 人形也照潛墨的回血速率(S3 Crab Tank 滾球:「イカセンプクと同じく体力を急速に回復」)
                settings.RegenExposed = Mathf.Max(settings.RegenExposed, settings.RegenSubmerged);
            }
            if (_stack.NoHealthRegen)
            {
                settings.RegenSubmerged = 0f;
                settings.RegenExposed = 0f;
            }
            return settings;
        }

        // ── 之後的移動單元(超級跳、衝牆、翻滾、跳射)讀的值 ──

        // 超級跳蓄力 / 飛行秒數(QSJ:80F → 20F、138F → 96.6F)
        public float SuperJumpChargeSeconds => AbilityCurve.SuperJumpChargeFrames.Evaluate(AbilityPoints(Ability.QuickSuperJump)) / 60f;
        public float SuperJumpFlightSeconds => AbilityCurve.SuperJumpFlightFrames.Evaluate(AbilityPoints(Ability.QuickSuperJump)) / 60f;

        // 衝牆蓄力秒數(Intensify Action:45F → 5F)
        public float SquidSurgeChargeSeconds => AbilityCurve.SquidSurgeChargeFrames.Evaluate(AbilityPoints(Ability.IntensifyAction)) / 60f;

        // 連續翻滾速度係數(0.85 → 1.0)
        public float RollChainSpeedFactor => AbilityCurve.RollChainFactor.Evaluate(AbilityPoints(Ability.IntensifyAction));

        // 跳射額外散布移除比例(0 → 1)
        public float JumpSpreadReduction => AbilityCurve.JumpSpreadReduction.Evaluate(AbilityPoints(Ability.IntensifyAction));
    }
}

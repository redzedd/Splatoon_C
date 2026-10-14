using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 場上裝置的基底(Sprinkler、Splash Wall、Beakon、Ink Mine、Big Bubbler、Wave Breaker、Tacticooler…):
    // 一個 SubProjectile(由 ThrowController/大招從 SharedPool 取出、Launch)兼 IDamageable。
    //
    // 生命週期:子類在 Launch 裡放好位置,落地/展開時呼叫 Deploy(owner, team) → 滿血、上隊伍色、註冊進 DamageableRegistry
    // (範圍傷害找得到)、可選訂閱擁有者死亡。被打:只吃敵方(含中立)攻擊,傷害 × DamageRates.Get(攻擊類別, TargetClass),
    // 取整到 0.1;受擊閃白;歸零 → OnBroken(子類)→ 碎裂特效 + 音效 → 下一幀回池(延後回池:避免在別人迭代
    // 註冊表時把自己移掉)。子類也可呼叫 Drain(自然扣血,Splash Wall 每幀 1/400)或 Expire(被新的擠掉)。
    //
    // 碰撞體:根物件或子物件上要有 Collider,放 Target 圖層(GameLayers.Target):InkBullet 用對人半徑打得到、
    // 爆炸視線預設不被它擋(要擋敵方攻擊的覆寫 BlocksAttacksFrom,例:Splash Wall)。
    // 注意:專案 Physics.autoSyncTransforms 關閉,剛放置/移動的碰撞體要到下一個物理步才進射線查詢
    // (同一幀要查詢就先呼叫 Physics.SyncTransforms)。
    // 子類覆寫 Awake / LateUpdate / OnDisable / Expire 時要呼叫 base。
    public abstract class DeployableObject : SubProjectile, IDamageable
    {
        private const float FlashSeconds = 0.08f;

        private Collider _collider;
        private Renderer[] _renderers;
        private Team _team = Team.None;
        private float _hp;
        private bool _deployed;
        private bool _broken;
        private bool _pendingDespawn;
        private bool _registered;
        private float _flashUntil;
        private bool _flashApplied;
        private InklingLifecycle _ownerLifecycle;
        private System.Action<DamageInfo> _onOwnerDied;

        // ── 子類提供 ──

        // 耐久上限(Sprinkler 120、Splash Wall 800、Beakon 120、Wave Breaker 480…)
        public abstract float MaxHp { get; }

        // 受擊物件類別(查 DamageRates 的欄)
        public abstract TargetClass TargetClass { get; }

        // 擁有者被擊倒時是否一起消失(S3:Sprinkler 會;Beakon、Ink Mine 不會)
        protected virtual bool DespawnWhenOwnerDies => false;

        // 被打壞時的音效 id
        protected virtual string BrokenSound => "device_destroyed";

        // 打不壞的裝置(S3 Tacticooler 飲料機、Ultra Stamp 槌面擋彈):不登記進 DamageableRegistry(範圍傷害不找它)、
        // 直擊照樣停在它身上但不扣血、不閃白、攻擊者不出命中標記
        protected virtual bool Indestructible => false;

        // 是否擋下這一隊的攻擊(爆炸視線;子彈本來就會打在它身上)。Splash Wall 覆寫成「敵隊 → true」
        public virtual bool BlocksAttacksFrom(Team attackerTeam)
        {
            return false;
        }

        // 受擊回呼(已套倍率的實際扣血量)
        protected virtual void OnDamaged(float dealt, in DamageInfo info)
        {
        }

        // 被打壞(HP 歸零)回呼:子類停止運作;預設碎裂特效由基底處理
        protected virtual void OnBroken(in DamageInfo info)
        {
        }

        // ── 狀態 ──

        public Team Team => _team;
        public float Hp => _hp;
        public float Normalized => MaxHp > 0f ? _hp / MaxHp : 0f;
        public bool IsDeployed => _deployed;
        public bool IsBroken => _broken;
        public bool IsAlive => _deployed && !_broken && _hp > 0f && isActiveAndEnabled;
        public bool IsFlashing => Time.time < _flashUntil;

        // 累計被打壞的裝置數(測試用)
        public static int BrokenCount { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetCounters()
        {
            BrokenCount = 0;
        }

        protected virtual void Awake()
        {
            _collider = GetComponentInChildren<Collider>(true);
            _renderers = GetComponentsInChildren<Renderer>(true);
            _onOwnerDied = OnOwnerDied;
        }

        // 落地/展開:滿血、上色、註冊。可重複呼叫(池化重用)
        protected void Deploy(Inkling owner, Team team)
        {
            Owner = owner;
            _team = team;
            _hp = MaxHp;
            _broken = false;
            _pendingDespawn = false;
            _deployed = true;
            _flashUntil = 0f;
            _flashApplied = false;
            ApplyTint(TeamPalette.ColorOf(team));
            if (!_registered && !Indestructible)
            {
                DamageableRegistry.Active.Add(this);
                _registered = true;
            }
            UnsubscribeOwner();
            if (DespawnWhenOwnerDies && owner != null)
            {
                _ownerLifecycle = owner.GetComponent<InklingLifecycle>();
                if (_ownerLifecycle != null)
                {
                    _ownerLifecycle.Died += _onOwnerDied;
                }
            }
        }

        // 範圍傷害量距離用的最近點(預設:第一個 Collider);有多個受擊部位的裝置(Big Bubbler 地面裝置 + 頂端無人機)覆寫
        public virtual Vector3 ClosestPoint(Vector3 from)
        {
            if (_collider != null && _collider.enabled && _collider.gameObject.activeInHierarchy)
            {
                return _collider.ClosestPoint(from);
            }
            return transform.position;
        }

        public void ApplyDamage(in DamageInfo info)
        {
            if (!IsAlive || info.Amount <= 0f || Indestructible)
            {
                return;
            }
            // 只吃敵方(與中立)攻擊;友軍攻擊在攻擊端就被 DamageRules 擋掉,這裡再防一次
            if (info.Team != Team.None && info.Team == _team)
            {
                return;
            }
            float dealt = DamageRates.Apply(info.Amount, info.AttackClass, TargetClass);
            if (dealt <= 0f)
            {
                return;
            }
            dealt = Mathf.Min(dealt, _hp);
            _hp = VitalsModel.Floor01(_hp - dealt);
            _flashUntil = Time.time + FlashSeconds;
            OnDamaged(dealt, info);
            // 命中回饋(準星命中標記、命中音效);打壞物件不算擊倒
            info.Attacker?.OnDamageDealt(this, dealt, false);
            if (_hp <= 0f)
            {
                Break(info);
            }
        }

        // 自然扣血(不歸屬任何人;Splash Wall 每幀 1/400)。扣到 0 也算壞掉,但不播碎裂音
        protected void Drain(float amount)
        {
            if (!IsAlive || amount <= 0f)
            {
                return;
            }
            _hp = Mathf.Max(0f, _hp - amount);
            if (_hp <= 0f)
            {
                _broken = true;
                _pendingDespawn = true;
            }
        }

        private void Break(in DamageInfo info)
        {
            if (_broken)
            {
                return;
            }
            _broken = true;
            BrokenCount++;
            OnBroken(info);
            Vector3 center = _collider != null ? _collider.bounds.center : transform.position;
            if (InkSplashFxPool.Instance != null)
            {
                Color color = TeamPalette.ColorOf(_team);
                for (int i = 0; i < 4; i++)
                {
                    Vector3 offset = Random.insideUnitSphere * 0.4f;
                    offset.y = Mathf.Abs(offset.y);
                    InkSplashFxPool.Instance.Spawn(center + offset, Vector3.up, color);
                }
            }
            string sound = BrokenSound;
            if (!string.IsNullOrEmpty(sound))
            {
                GameAudio.Play(sound, center, 1f, 0.05f);
            }
            _pendingDespawn = true;
        }

        private void OnOwnerDied(DamageInfo info)
        {
            if (_deployed && !_broken)
            {
                _broken = true;
                _pendingDespawn = true;
            }
        }

        protected virtual void LateUpdate()
        {
            if (_pendingDespawn)
            {
                Expire();
                return;
            }
            if (!_deployed)
            {
                return;
            }
            // 受擊閃白:只在狀態切換時重設顏色(不每幀寫 MaterialPropertyBlock)
            bool flashing = Time.time < _flashUntil;
            if (flashing != _flashApplied)
            {
                _flashApplied = flashing;
                Color team = TeamPalette.ColorOf(_team);
                ApplyTint(flashing ? Color.Lerp(team, Color.white, 0.75f) : team);
            }
        }

        // 被擠掉/壽命到/被打壞:清理後回池。子類覆寫時記得呼叫 base.Expire()
        public override void Expire()
        {
            Cleanup();
            Despawn();
        }

        protected virtual void OnDisable()
        {
            Cleanup();
        }

        private void Cleanup()
        {
            if (_registered)
            {
                DamageableRegistry.Active.Remove(this);
                _registered = false;
            }
            UnsubscribeOwner();
            _deployed = false;
            _pendingDespawn = false;
        }

        private void UnsubscribeOwner()
        {
            if (_ownerLifecycle != null)
            {
                _ownerLifecycle.Died -= _onOwnerDied;
                _ownerLifecycle = null;
            }
        }

        private void ApplyTint(Color color)
        {
            if (_renderers == null)
            {
                return;
            }
            for (int i = 0; i < _renderers.Length; i++)
            {
                TeamTint.Apply(_renderers[i], color);
            }
        }
    }
}

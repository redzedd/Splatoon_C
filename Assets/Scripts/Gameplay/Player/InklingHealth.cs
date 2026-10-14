using System;
using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Combat;
using UnityEngine;

namespace SplatoonC.Gameplay.Player
{
    // 角色血量:IDamageable 的角色實作。敵墨持續傷害、回血、護甲都在 VitalsModel(純邏輯)。
    // 擊倒只發事件,後續(爆墨、隱藏、重生)由 InklingLifecycle 處理。
    [RequireComponent(typeof(Inkling))]
    public sealed class InklingHealth : MonoBehaviour, IDamageable
    {
        // 助攻判定窗:擊倒前這段時間內有打到的其他人算助攻
        public const float AssistWindowSeconds = 5f;
        private const int HistorySize = 8;

        [SerializeField, Tooltip("血量規則(Splatoon 3 預設:100 HP、1 秒後回血、潛墨 100/s、敵墨 18/s 上限 40)")]
        private VitalsSettings _settings = VitalsSettings.Splatoon3;

        [SerializeField, Tooltip("受傷在敵方地圖上曝光的累積傷害門檻")]
        private float _revealDamage = 18f;

        private Inkling _inkling;
        private SquidController _squid;
        private CharacterController _controller;
        private VitalsModel _model = VitalsModel.Create();

        // 近期受傷紀錄(環狀):助攻與擊殺鏡頭用
        private readonly IDamageDealer[] _historyDealer = new IDamageDealer[HistorySize];
        private readonly float[] _historyTime = new float[HistorySize];
        private int _historyNext;

        public Team Team => _inkling != null ? _inkling.Team : Team.None;
        public bool IsAlive => !_model.IsDead && isActiveAndEnabled;
        public float Hp => _model.Hp(_settings);
        public float MaxHp => _settings.MaxHp;
        public float Damage => _model.Damage;
        public float Normalized => _settings.MaxHp > 0f ? Hp / _settings.MaxHp : 0f;
        public bool HasArmor => _model.HasArmor(Time.time);
        // 護甲剩餘耐久與到期時間(翻滾/衝牆的護甲只在「身上是自己給的那份」時才清除,不誤清出生護甲)
        public float Armor => _model.Armor;
        public float ArmorUntil => _model.ArmorUntil;
        public bool IsRevealed => _model.Damage >= _revealDamage;
        public float LastDamageTime => _model.LastDamageTime;
        public bool Invulnerable { get; set; }

        // 最後一擊的攻擊者(擊殺鏡頭、「被 X 擊倒」)
        public IDamageDealer LastAttacker { get; private set; }

        // 受到一次攻擊(已扣除護甲後實際傷害, 資訊)——受傷回饋、HUD 訂閱
        public event Action<float, DamageInfo> Damaged;

        // 被擊倒(致命一擊資訊)
        public event Action<DamageInfo> Splatted;

        private void Awake()
        {
            _inkling = GetComponent<Inkling>();
            _squid = GetComponent<SquidController>();
            _controller = GetComponent<CharacterController>();
        }

        private void OnEnable()
        {
            DamageableRegistry.Active.Add(this);
        }

        private void OnDisable()
        {
            DamageableRegistry.Active.Remove(this);
        }

        private void Update()
        {
            if (_model.IsDead)
            {
                return;
            }
            bool onEnemyInk = _squid != null && _squid.OnEnemyInk && (_controller == null || _controller.isGrounded);
            bool submerged = _squid != null && _squid.IsSquid && _squid.IsInOwnInk;
            // 暫時效果:Ink Storm 雲下不回血、能量站墨抗降低敵墨持續傷害(無效果時原樣)
            VitalsSettings settings = _inkling != null ? _inkling.Modifiers.AdjustVitals(_settings) : _settings;
            _model.Tick(Time.time, Time.deltaTime, submerged, onEnemyInk, settings);
        }

        // 膠囊最近點(範圍傷害判定):以 CharacterController 的膠囊為準
        public Vector3 ClosestPoint(Vector3 from)
        {
            if (_controller == null)
            {
                return transform.position;
            }
            Vector3 center = transform.TransformPoint(_controller.center);
            float half = Mathf.Max(0f, _controller.height * 0.5f - _controller.radius);
            return CapsuleMath.ClosestPoint(center - Vector3.up * half, center + Vector3.up * half,
                _controller.radius, from);
        }

        public void ApplyDamage(in DamageInfo info)
        {
            if (_model.IsDead || Invulnerable || !isActiveAndEnabled)
            {
                return;
            }
            if (info.Team != Team.None && info.Team == Team)
            {
                return;
            }
            bool splatted = _model.ApplyHit(info.Amount, Time.time, _settings, out float dealt);
            if (info.Attacker != null)
            {
                RecordHistory(info.Attacker);
                LastAttacker = info.Attacker;
            }
            if (dealt > 0f || splatted)
            {
                Damaged?.Invoke(dealt, info);
            }
            info.Attacker?.OnDamageDealt(this, dealt, splatted);
            if (splatted)
            {
                Splatted?.Invoke(info);
            }
        }

        private void RecordHistory(IDamageDealer dealer)
        {
            _historyDealer[_historyNext] = dealer;
            _historyTime[_historyNext] = Time.time;
            _historyNext = (_historyNext + 1) % HistorySize;
        }

        // 助攻名單:窗內打過我、但不是最後一擊的人(去重)。回傳寫入數量。
        public int CollectAssists(IDamageDealer killer, IDamageDealer[] buffer)
        {
            int count = 0;
            float since = Time.time - AssistWindowSeconds;
            for (int i = 0; i < HistorySize; i++)
            {
                IDamageDealer dealer = _historyDealer[i];
                if (dealer == null || dealer == killer || _historyTime[i] < since)
                {
                    continue;
                }
                bool duplicate = false;
                for (int k = 0; k < count; k++)
                {
                    if (buffer[k] == dealer)
                    {
                        duplicate = true;
                        break;
                    }
                }
                if (!duplicate && count < buffer.Length)
                {
                    buffer[count++] = dealer;
                }
            }
            return count;
        }

        public void GrantArmor(float amount, float seconds)
        {
            _model.GrantArmor(amount, Time.time + seconds);
        }

        public void ClearArmor()
        {
            _model.ClearArmor();
        }

        public void ResetForRespawn()
        {
            _model.ResetFull();
            LastAttacker = null;
            Array.Clear(_historyDealer, 0, _historyDealer.Length);
        }
    }
}

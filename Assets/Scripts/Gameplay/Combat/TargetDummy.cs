using SplatoonC.Core;
using SplatoonC.Core.Combat;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 試射場假人:血量 + 頭頂累計傷害數字的資料來源。死亡 → 隱形、關 collider、計時重生;
    // 閒置一段時間沒被打 → 回滿血並清空數字(Splatoon 試射場行為)。
    // 根物件同時掛 CapsuleCollider(直擊用 hit.collider.GetComponent<IDamageable>() 找得到)。
    [RequireComponent(typeof(CapsuleCollider))]
    public sealed class TargetDummy : MonoBehaviour, IDamageable
    {
        [SerializeField, Tooltip("假人設定資產(Assets/Data/TargetDummyConfig)")]
        private TargetDummyConfig _config;

        [SerializeField, Tooltip("傷害數字錨點(頭頂);留空用根物件本身")]
        private Transform _popupAnchor;

        private CapsuleCollider _collider;
        private Renderer[] _renderers;
        private Health _health;
        private DamageAccumulator _accumulator;
        private float _killedAt;
        private float _respawnAt;

        public TargetDummyConfig Config => _config;
        public bool IsAlive => !_health.IsDead;

        // 中立目標:任何隊伍都打得到
        public Team Team => Team.None;
        public float CurrentHp => _health.Current;
        public float LastDamage { get; private set; }
        public int KillCount { get; private set; }
        // 頭頂數字的版本號:內容變動才 +1,HUD 只在版本變動時重建字串(不每幀配置)
        public int PopupVersion { get; private set; }
        public float PopupTotal => _accumulator.Total;
        public bool PopupIsKill { get; private set; }
        public Transform PopupAnchor => _popupAnchor != null ? _popupAnchor : transform;
        public float BodyRadius => _collider != null ? _collider.radius : 0.4f;

        public bool PopupVisible
        {
            get
            {
                if (IsAlive)
                {
                    return _accumulator.HasHits;
                }
                float hold = _config != null ? _config.KillPopupDuration : 1.2f;
                return Time.time < _killedAt + hold;
            }
        }

        private void Awake()
        {
            _collider = GetComponent<CapsuleCollider>();
            _renderers = GetComponentsInChildren<Renderer>(true);
            if (_config == null)
            {
                Debug.LogError("TargetDummy:缺少假人設定資產,改用 100 血預設", this);
            }
            _health = Health.Create(_config != null ? _config.MaxHp : 100f);
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
            if (!IsAlive)
            {
                if (Time.time >= _respawnAt)
                {
                    Respawn();
                }
                return;
            }
            float idle = _config != null ? _config.IdleResetDelay : 3f;
            if (_accumulator.HasExpired(Time.time, idle))
            {
                _health.ResetFull();
                _accumulator.Reset();
                PopupIsKill = false;
                PopupVersion++;
            }
        }

        // 解析解(CapsuleMath),不靠 Collider.ClosestPoint:死亡關 collider 後距離判定仍一致。假設膠囊直立、未縮放。
        public Vector3 ClosestPoint(Vector3 from)
        {
            if (_collider == null)
            {
                return transform.position;
            }
            Vector3 center = transform.TransformPoint(_collider.center);
            float halfSegment = Mathf.Max(0f, _collider.height * 0.5f - _collider.radius);
            return CapsuleMath.ClosestPoint(
                center - Vector3.up * halfSegment, center + Vector3.up * halfSegment, _collider.radius, from);
        }

        public void ApplyDamage(in DamageInfo info)
        {
            float amount = info.Amount;
            if (!IsAlive || amount <= 0f)
            {
                return;
            }
            LastDamage = amount;
            _accumulator.Add(amount, Time.time);
            bool killed = _health.ApplyDamage(amount);
            PopupIsKill = killed;
            PopupVersion++;
            info.Attacker?.OnDamageDealt(this, amount, killed);
            if (killed)
            {
                Die();
            }
        }

        private void Die()
        {
            KillCount++;
            _killedAt = Time.time;
            _respawnAt = _killedAt + (_config != null ? _config.RespawnDelay : 3f);
            SetVisible(false);
            _collider.enabled = false;
        }

        private void Respawn()
        {
            _health.ResetFull();
            _accumulator.Reset();
            PopupIsKill = false;
            PopupVersion++;
            SetVisible(true);
            _collider.enabled = true;
        }

        private void SetVisible(bool visible)
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                _renderers[i].enabled = visible;
            }
        }
    }
}

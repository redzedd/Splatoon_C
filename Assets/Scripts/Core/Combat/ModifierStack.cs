using System;
using UnityEngine;

namespace SplatoonC.Core.Combat
{
    // 角色身上所有暫時效果的純邏輯堆疊(InklingModifiers 持有一份)。
    // - 以「來源物件」為鍵:同一來源再加 = 刷新(換新 spec、重設到期時間,已扣的墨量額度保留),不會疊兩層。
    // - seconds ≤ 0 = 直到 Remove 為止。
    // - 聚合規則:倍率相乘;每秒扣墨相加(各來源各自的上限);AP 取各來源最大值(S3 能量站與裝備取高者、同類增益不疊),
    //   上限 57;NoHealthRegen / SwimRegen / NoSlipDamage / 狀態旗標取 OR。
    // 全部固定陣列 + 快取聚合值,穩態零配置。
    public sealed class ModifierStack
    {
        private struct Entry
        {
            public object Source;
            public ModifierSpec Spec;
            public float ExpiresAt;
            public float Drained;
        }

        private Entry[] _entries = new Entry[8];
        private int _count;
        private bool _dirty = true;

        private float _runSpeed = 1f;
        private float _swimSpeed = 1f;
        private float _inkRefill = 1f;
        private float _inkCost = 1f;
        private float _jumpHeight = 1f;
        private float _respawnTime = 1f;
        private float _specialCharge = 1f;
        private bool _noHealthRegen;
        private bool _swimRegen;
        private bool _noSlipDamage;
        private StatusFlags _status;
        private AbilityPoints _abilities;

        public int Count => _count;

        // 每次內容變動(新增、刷新、移除、到期)+1:HUD/測試判斷「有沒有變」用
        public int Version { get; private set; }

        public float RunSpeed { get { Recompute(); return _runSpeed; } }
        public float SwimSpeed { get { Recompute(); return _swimSpeed; } }
        public float InkRefill { get { Recompute(); return _inkRefill; } }
        public float InkCost { get { Recompute(); return _inkCost; } }
        public float JumpHeight { get { Recompute(); return _jumpHeight; } }
        public float RespawnTime { get { Recompute(); return _respawnTime; } }
        public float SpecialCharge { get { Recompute(); return _specialCharge; } }
        public bool NoHealthRegen { get { Recompute(); return _noHealthRegen; } }
        public bool SwimRegen { get { Recompute(); return _swimRegen; } }
        public bool NoSlipDamage { get { Recompute(); return _noSlipDamage; } }
        public StatusFlags Status { get { Recompute(); return _status; } }

        public int AbilityPoints(Ability ability)
        {
            Recompute();
            return _abilities[ability];
        }

        // 目前仍有扣墨額度的來源合計每秒扣墨(HUD/bot 參考)
        public float InkDrainPerSecond
        {
            get
            {
                float total = 0f;
                for (int i = 0; i < _count; i++)
                {
                    ModifierSpec spec = _entries[i].Spec;
                    if (spec.InkDrainPerSecond > 0f
                        && (spec.InkDrainLimit <= 0f || _entries[i].Drained < spec.InkDrainLimit))
                    {
                        total += spec.InkDrainPerSecond;
                    }
                }
                return total;
            }
        }

        public void Add(object source, in ModifierSpec spec, float now, float seconds)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            float expiresAt = seconds > 0f ? now + seconds : float.PositiveInfinity;
            int index = IndexOf(source);
            if (index >= 0)
            {
                // 刷新:換 spec、重設到期;已扣額度保留(同一團毒霧進進出出不會重置 40% 上限)
                _entries[index].Spec = spec;
                _entries[index].ExpiresAt = expiresAt;
            }
            else
            {
                if (_count == _entries.Length)
                {
                    Array.Resize(ref _entries, _entries.Length * 2);
                }
                _entries[_count++] = new Entry { Source = source, Spec = spec, ExpiresAt = expiresAt, Drained = 0f };
            }
            MarkChanged();
        }

        public bool Remove(object source)
        {
            int index = IndexOf(source);
            if (index < 0)
            {
                return false;
            }
            RemoveAt(index);
            MarkChanged();
            return true;
        }

        public bool Contains(object source)
        {
            return IndexOf(source) >= 0;
        }

        // 剩餘秒數(不存在 = 0;永久 = +∞)
        public float Remaining(object source, float now)
        {
            int index = IndexOf(source);
            if (index < 0)
            {
                return 0f;
            }
            float expiresAt = _entries[index].ExpiresAt;
            return float.IsPositiveInfinity(expiresAt) ? float.PositiveInfinity : Mathf.Max(0f, expiresAt - now);
        }

        // 移除到期的效果;回傳移除數
        public int Tick(float now)
        {
            int removed = 0;
            for (int i = _count - 1; i >= 0; i--)
            {
                if (now >= _entries[i].ExpiresAt)
                {
                    RemoveAt(i);
                    removed++;
                }
            }
            if (removed > 0)
            {
                MarkChanged();
            }
            return removed;
        }

        // 本步要扣的墨量(墨槽比例):各來源 rate × dt,扣到各自上限為止。額度以「要求量」計(墨槽已空也照算)。
        public float ConsumeDrain(float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return 0f;
            }
            float total = 0f;
            for (int i = 0; i < _count; i++)
            {
                ModifierSpec spec = _entries[i].Spec;
                if (spec.InkDrainPerSecond <= 0f)
                {
                    continue;
                }
                float amount = spec.InkDrainPerSecond * deltaTime;
                if (spec.InkDrainLimit > 0f)
                {
                    amount = Mathf.Min(amount, Mathf.Max(0f, spec.InkDrainLimit - _entries[i].Drained));
                }
                _entries[i].Drained += amount;
                total += amount;
            }
            return total;
        }

        public void Clear()
        {
            if (_count == 0)
            {
                return;
            }
            Array.Clear(_entries, 0, _count);
            _count = 0;
            MarkChanged();
        }

        // 死亡:清掉非 Persistent 的效果;回傳清除數
        public int ClearNonPersistent()
        {
            int removed = 0;
            for (int i = _count - 1; i >= 0; i--)
            {
                if (!_entries[i].Spec.Persistent)
                {
                    RemoveAt(i);
                    removed++;
                }
            }
            if (removed > 0)
            {
                MarkChanged();
            }
            return removed;
        }

        private int IndexOf(object source)
        {
            for (int i = 0; i < _count; i++)
            {
                if (ReferenceEquals(_entries[i].Source, source))
                {
                    return i;
                }
            }
            return -1;
        }

        private void RemoveAt(int index)
        {
            _count--;
            _entries[index] = _entries[_count];
            _entries[_count] = default;
        }

        private void MarkChanged()
        {
            _dirty = true;
            Version++;
        }

        private void Recompute()
        {
            if (!_dirty)
            {
                return;
            }
            _dirty = false;
            _runSpeed = 1f;
            _swimSpeed = 1f;
            _inkRefill = 1f;
            _inkCost = 1f;
            _jumpHeight = 1f;
            _respawnTime = 1f;
            _specialCharge = 1f;
            _noHealthRegen = false;
            _swimRegen = false;
            _noSlipDamage = false;
            _status = StatusFlags.None;
            _abilities = default;
            for (int i = 0; i < _count; i++)
            {
                ModifierSpec spec = _entries[i].Spec;
                _runSpeed *= Mathf.Max(0f, spec.RunSpeed);
                _swimSpeed *= Mathf.Max(0f, spec.SwimSpeed);
                _inkRefill *= Mathf.Max(0f, spec.InkRefill);
                _inkCost *= Mathf.Max(0f, spec.InkCost);
                _jumpHeight *= Mathf.Max(0f, spec.JumpHeight);
                _respawnTime *= Mathf.Max(0f, spec.RespawnTime);
                _specialCharge *= Mathf.Max(0f, spec.SpecialCharge);
                _noHealthRegen |= spec.NoHealthRegen;
                _swimRegen |= spec.SwimRegen;
                _noSlipDamage |= spec.NoSlipDamage;
                _status |= spec.Status;
                AbilityPoints ap = spec.Abilities;
                if (!ap.Any)
                {
                    continue;
                }
                for (int a = 0; a < AbilityCurve.Count; a++)
                {
                    var ability = (Ability)a;
                    if (ap[ability] > _abilities[ability])
                    {
                        _abilities[ability] = ap[ability];
                    }
                }
            }
        }
    }
}

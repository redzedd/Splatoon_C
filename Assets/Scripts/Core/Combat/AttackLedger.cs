namespace SplatoonC.Core.Combat
{
    // 「一次攻擊」的命中帳本(§3.1 命中去重):同一次揮動/潑灑/齊射的所有子彈共用一個 attackId,
    // 同一目標在同一次攻擊中:
    //   cap ≤ 0 → 只吃第一發(滾筒甩、潑桶、刮水刀墨波…)
    //   cap > 0 → 各發累加,但總和不超過 cap(傘:11 顆彈丸加總,每次開火對每個目標上限 81)
    // attackId = 0 表示「不去重」(射擊槍的每一發都是獨立攻擊)。
    //
    // 固定容量環形緩衝(零配置);帳目只需要活到那次攻擊的子彈全部落地(幾秒),滿了覆蓋最舊的。
    // 目標以 object 參照比對(IDamageable 實例),純邏輯可在 EditMode 測。
    public sealed class AttackLedger
    {
        private readonly int[] _attackIds;
        private readonly object[] _targets;
        private readonly float[] _totals;
        private int _next;
        private int _lastIssuedId;

        public AttackLedger(int capacity = 256)
        {
            if (capacity < 1)
            {
                capacity = 1;
            }
            _attackIds = new int[capacity];
            _targets = new object[capacity];
            _totals = new float[capacity];
        }

        public int Capacity => _attackIds.Length;

        // 發一個新的攻擊編號(永不為 0;溢位後從 1 重來)
        public int NewAttackId()
        {
            _lastIssuedId++;
            if (_lastIssuedId <= 0)
            {
                _lastIssuedId = 1;
            }
            return _lastIssuedId;
        }

        // 登記一發對 target 的傷害,回傳「實際允許」的傷害(0 = 本次攻擊已打過/已達上限,不要結算)。
        public float Register(int attackId, object target, float damage, float cap = 0f)
        {
            if (attackId == 0 || target == null)
            {
                return damage;
            }
            int index = Find(attackId, target);
            if (index >= 0)
            {
                if (cap <= 0f)
                {
                    return 0f;
                }
                float room = cap - _totals[index];
                float allowed = damage < room ? damage : room;
                if (allowed <= 0f)
                {
                    return 0f;
                }
                _totals[index] += allowed;
                return allowed;
            }
            float first = cap > 0f && damage > cap ? cap : damage;
            _attackIds[_next] = attackId;
            _targets[_next] = target;
            _totals[_next] = first;
            _next = (_next + 1) % _attackIds.Length;
            return first;
        }

        // 這次攻擊是否已經打過 target(爆破槍:直擊的目標不再吃同一發的爆炸)
        public bool HasHit(int attackId, object target)
        {
            return attackId != 0 && target != null && Find(attackId, target) >= 0;
        }

        // 這次攻擊對 target 累計造成的傷害
        public float TotalFor(int attackId, object target)
        {
            int index = attackId != 0 && target != null ? Find(attackId, target) : -1;
            return index >= 0 ? _totals[index] : 0f;
        }

        public void Clear()
        {
            for (int i = 0; i < _attackIds.Length; i++)
            {
                _attackIds[i] = 0;
                _targets[i] = null;
                _totals[i] = 0f;
            }
            _next = 0;
        }

        private int Find(int attackId, object target)
        {
            for (int i = 0; i < _attackIds.Length; i++)
            {
                if (_attackIds[i] == attackId && ReferenceEquals(_targets[i], target))
                {
                    return i;
                }
            }
            return -1;
        }
    }
}

using UnityEngine;

namespace SplatoonC.Core.Combat
{
    // 墨量純邏輯:0~1 正規化。射擊整發消耗(不足不部分扣),回墨由呼叫端依狀態給速率。
    public struct InkTank
    {
        private float _current;

        public float Normalized => _current;

        public static InkTank CreateFull()
        {
            return new InkTank { _current = 1f };
        }

        public bool TryConsume(float costPerShot)
        {
            // epsilon 容忍浮點累積誤差(0.05 連扣 19 次後剩 0.0499999... 仍應能射最後一發)
            if (_current < costPerShot - 1e-5f)
            {
                return false;
            }
            _current = Mathf.Max(0f, _current - costPerShot);
            return true;
        }

        public void Refill(float ratePerSecond, float deltaTime)
        {
            _current = Mathf.Min(1f, _current + ratePerSecond * deltaTime);
        }
    }
}

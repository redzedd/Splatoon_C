using SplatoonC.Gameplay.Painting;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 手持模型上的蓄力指示燈(Curling Bomb 的手持 prefab):讀持有者 ThrowController.SubCharge01,
    // 燈色由暗綠漸亮、蓄滿後快閃——玩家不看 HUD 也知道蓄了多少(S3 蓄力時手上的炸彈會發光)。
    // 顏色量化成 16 階,變了才寫 MaterialPropertyBlock(每幀零配置)。
    public sealed class SubChargeIndicator : MonoBehaviour
    {
        [SerializeField, Tooltip("指示燈 renderer")]
        private Renderer _light;

        [SerializeField, Tooltip("未蓄力的燈色")]
        private Color _idle = new Color(0.1f, 0.35f, 0.12f);

        [SerializeField, Tooltip("蓄滿的燈色")]
        private Color _full = new Color(0.35f, 1f, 0.4f);

        private ThrowController _thrower;
        private int _appliedStep = -1;

        public float DisplayedCharge { get; private set; }

        private void OnEnable()
        {
            if (_thrower == null)
            {
                _thrower = GetComponentInParent<ThrowController>();
            }
        }

        private void LateUpdate()
        {
            if (_light == null)
            {
                return;
            }
            float charge = _thrower != null ? _thrower.SubCharge01 : 0f;
            DisplayedCharge = charge;
            bool blinkOff = charge >= 1f && Mathf.Repeat(Time.time, 0.16f) >= 0.08f;
            int step = blinkOff ? -2 : Mathf.RoundToInt(charge * 16f);
            if (step == _appliedStep)
            {
                return;
            }
            _appliedStep = step;
            Color color = blinkOff ? _idle : Color.Lerp(_idle, _full, step / 16f);
            TeamTint.Apply(_light, color);
        }
    }
}

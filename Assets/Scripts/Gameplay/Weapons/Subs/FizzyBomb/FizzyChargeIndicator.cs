using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.Painting;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 碳酸炸彈手持模型上的段數指示(S3:蓄力時罐子被搖、每升一段「喀」一聲,最後一段更響、罐子發光):
    // 讀持有者 ThrowController.SubChargeFrames → FizzyBombConfig.LevelAt;燈色 藍 → 黃 → 紅(第 3 段快閃),
    // 每升一段播 LevelSound(第 3 段的響聲由 ThrowController 的 ChargedSound 播);連打折抵時罐子左右搖晃。
    // 只有段數或閃爍狀態改變才寫 MaterialPropertyBlock(每幀零配置)。
    public sealed class FizzyChargeIndicator : MonoBehaviour
    {
        [SerializeField, Tooltip("指示燈 renderer")]
        private Renderer _light;

        [SerializeField, Tooltip("搖晃的罐身(留空 = 不搖)")]
        private Transform _body;

        private ThrowController _thrower;
        private int _shownLevel;
        private int _appliedState = -1;
        private float _lastMash;
        private float _shake;
        private Quaternion _bodyRest = Quaternion.identity;

        // 目前顯示的段數(0 = 沒在蓄力;測試用)
        public int DisplayedLevel => _shownLevel;
        // 升段音累計(測試用)
        public int LevelCues { get; private set; }

        private void Awake()
        {
            if (_body != null)
            {
                _bodyRest = _body.localRotation;
            }
        }

        private void OnEnable()
        {
            if (_thrower == null)
            {
                _thrower = GetComponentInParent<ThrowController>();
            }
            _shownLevel = 0;
            _appliedState = -1;
            _lastMash = 0f;
        }

        private void LateUpdate()
        {
            var config = _thrower != null ? _thrower.SubConfig as FizzyBombConfig : null;
            bool charging = config != null && _thrower.IsChargingSub;
            int level = charging ? config.LevelAt(_thrower.SubChargeFrames) : 0;
            if (level > _shownLevel && _shownLevel > 0 && level < 3)
            {
                LevelCues++;
                GameAudio.PlayFrom(IsLocal, config.LevelSound, transform.position, 0.8f, 0f);
            }
            else if (level > _shownLevel && _shownLevel > 0)
            {
                LevelCues++;
            }
            _shownLevel = level;

            // 連打折抵 → 搖晃
            float mash = _thrower != null ? _thrower.SubMashFrames : 0f;
            if (mash > _lastMash + 1e-3f)
            {
                _shake = 1f;
            }
            _lastMash = mash;
            _shake = Mathf.MoveTowards(_shake, 0f, Time.deltaTime * 4f);
            if (_body != null)
            {
                _body.localRotation = _bodyRest * Quaternion.Euler(Mathf.Sin(Time.time * 55f) * 18f * _shake, 0f, 0f);
            }

            if (_light == null)
            {
                return;
            }
            bool blinkOff = level >= 3 && Mathf.Repeat(Time.time, 0.16f) >= 0.08f;
            int state = blinkOff ? -2 : level;
            if (state == _appliedState)
            {
                return;
            }
            _appliedState = state;
            TeamTint.Apply(_light, blinkOff ? new Color(0.2f, 0.2f, 0.25f) : ColorOf(level));
        }

        private bool IsLocal
        {
            get
            {
                var owner = _thrower != null ? _thrower.GetComponent<SplatoonC.Gameplay.Player.Inkling>() : null;
                return owner != null && owner.IsLocalPlayer;
            }
        }

        private static Color ColorOf(int level)
        {
            switch (level)
            {
                case 3:
                    return new Color(1f, 0.3f, 0.25f);
                case 2:
                    return new Color(1f, 0.8f, 0.2f);
                case 1:
                    return new Color(0.45f, 0.85f, 1f);
                default:
                    return new Color(0.25f, 0.3f, 0.35f);
            }
        }
    }
}

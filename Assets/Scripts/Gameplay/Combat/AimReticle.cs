using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 準星 = 墨彈真正的落點(拋物線武器不能用畫面中心當準星,否則永遠瞄不準)。
    // 每幀以與 InkProjectile 相同的積分模型預測落點,再投影成螢幕座標移動準星。
    public sealed class AimReticle : MonoBehaviour
    {
        [SerializeField, Tooltip("射擊來源(Player 上的 InkShooter)")]
        private InkShooter _shooter;

        [SerializeField, Tooltip("準星 RectTransform(HudCanvas/Crosshair)")]
        private RectTransform _reticle;

        [SerializeField, Tooltip("預測步長(秒);越小越準、成本越高(28m/s 下 0.03s ≈ 0.84m/步)")]
        private float _stepTime = 0.03f;

        [SerializeField, Tooltip("最大預測步數(0.03×60 ≈ 1.8 秒彈道)")]
        private int _maxSteps = 60;

        [SerializeField, Tooltip("落點上抬(公尺),避免準星被地面吃掉")]
        private float _reticleLift = 0.05f;

        [SerializeField, Tooltip("準星跟隨彈道落點;關閉則固定畫面中心(Splatoon 式,彈道須夠平直)")]
        private bool _followLandingPoint;

        private Camera _camera;

        // 最近一次預測的落點(AutoTest 用來與實跑落點比對)
        public Vector3 PredictedLanding { get; private set; }

        private void Awake()
        {
            _camera = Camera.main;
            if (_shooter == null || _reticle == null)
            {
                Debug.LogError("AimReticle:缺少 InkShooter 或準星引用,準星不會跟隨落點", this);
            }
        }

        private void LateUpdate()
        {
            if (_shooter == null || _reticle == null)
            {
                return;
            }
            if (_camera == null)
            {
                _camera = Camera.main;
                if (_camera == null)
                {
                    return;
                }
            }
            if (!_shooter.TryComputeAim(out Vector3 origin, out Vector3 direction))
            {
                return;
            }

            WeaponConfig config = _shooter.Config;
            Vector3 velocity = direction * config.MuzzleSpeed;
            Vector3 point = origin;
            Vector3 landing = origin + direction * 10f;
            bool found = false;

            for (int i = 0; i < _maxSteps; i++)
            {
                velocity.y += config.ProjectileGravity * _stepTime;
                Vector3 next = point + velocity * _stepTime;
                Vector3 delta = next - point;
                float distance = delta.magnitude;
                if (distance > 0.0001f && Physics.Raycast(point, delta / distance, out RaycastHit hit,
                        distance, config.HitMask, QueryTriggerInteraction.Ignore))
                {
                    landing = hit.point + hit.normal * _reticleLift;
                    found = true;
                    break;
                }
                point = next;
            }
            if (!found)
            {
                landing = point;
            }
            PredictedLanding = landing;

            if (!_followLandingPoint)
            {
                // 固定畫面中心(彈道夠平直時中心即準心);PredictedLanding 仍供測試/未來輔助瞄準用
                _reticle.anchoredPosition = Vector2.zero;
                return;
            }

            Vector3 screen = _camera.WorldToScreenPoint(landing);
            if (screen.z <= 0f)
            {
                return;
            }
            // Crosshair 錨定畫面中心,故位置以中心為原點
            _reticle.anchoredPosition = new Vector2(
                screen.x - Screen.width * 0.5f, screen.y - Screen.height * 0.5f);
        }
    }
}

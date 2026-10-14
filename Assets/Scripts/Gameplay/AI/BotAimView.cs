using SplatoonC.Core.AI;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.AI
{
    // bot 的視線(IAimView):沒有相機,腦袋給「想看的角度」,這裡以限速轉向 + 手抖雜訊逼近。
    // 視點放在槍口高度——InkShooter 的準心射線從這裡出發,彈道補償與玩家同一套。
    public sealed class BotAimView : MonoBehaviour, IAimView
    {
        [SerializeField, Tooltip("視點高度(公尺,自角色腳底)")]
        private float _eyeHeight = 1.35f;

        private float _yaw;
        private float _pitch;
        private float _targetYaw;
        private float _targetPitch;
        private float _noiseSeed;

        // 轉向速度(度/秒)與手抖幅度(度)——由 BotBrain 依難度設定
        public float TurnSpeed { get; set; } = 420f;
        public float AimJitterDeg { get; set; } = 1.5f;

        public float CurrentYaw => _yaw;
        public float CurrentPitch => _pitch;

        public Vector3 AimOrigin => transform.position + Vector3.up * _eyeHeight;

        public Vector3 AimForward
        {
            get
            {
                // 手抖:低頻雜訊,不是每幀亂跳(看起來像人在修正準心)
                float t = Time.time * 1.7f + _noiseSeed;
                float jy = (Mathf.PerlinNoise(t, 0.3f) - 0.5f) * 2f * AimJitterDeg;
                float jp = (Mathf.PerlinNoise(0.7f, t) - 0.5f) * 2f * AimJitterDeg;
                return Quaternion.Euler(_pitch + jp, _yaw + jy, 0f) * Vector3.forward;
            }
        }

        public float Yaw => _yaw;

        public float EyeHeight => _eyeHeight;

        private void Awake()
        {
            _yaw = transform.eulerAngles.y;
            _targetYaw = _yaw;
            _pitch = 8f;
            _targetPitch = _pitch;
            _noiseSeed = Random.value * 100f;
        }

        public void SetTarget(float yawDeg, float pitchDeg)
        {
            _targetYaw = yawDeg;
            _targetPitch = Mathf.Clamp(pitchDeg, -70f, 70f);
        }

        public void LookAt(Vector3 worldPoint)
        {
            BotMath.AnglesTo(AimOrigin, worldPoint, out float yaw, out float pitch);
            SetTarget(yaw, pitch);
        }

        // 重生/瞬移:直接對齊,不轉過去
        public void Snap(float yawDeg, float pitchDeg)
        {
            _yaw = _targetYaw = yawDeg;
            _pitch = _targetPitch = pitchDeg;
        }

        // 目前視線與「想看的方向」差幾度
        public float ErrorToTarget => BotMath.AngularError(_yaw, _pitch, _targetYaw, _targetPitch);

        private void Update()
        {
            float dt = Time.deltaTime;
            _yaw = Mathf.Repeat(BotMath.TurnTowards(_yaw, _targetYaw, TurnSpeed, dt), 360f);
            _pitch = Mathf.MoveTowards(_pitch, _targetPitch, TurnSpeed * 0.6f * dt);
        }
    }
}

using SplatoonC.Core.Combat;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 動態假人:從起點沿 _travelOffset 定速來回,面向行進方向;死亡期間照走。
    // 用 transform 移動靜態 collider:射線看到的是上一物理步的位置,2 m/s 誤差 ≤4cm,可接受。
    [RequireComponent(typeof(TargetDummy))]
    public sealed class DummyPatrol : MonoBehaviour
    {
        [SerializeField, Tooltip("來回終點相對起點的位移(公尺,世界座標)")]
        private Vector3 _travelOffset = new Vector3(5f, 0f, 0f);

        [SerializeField, Tooltip("速度(公尺/秒);0 = 用假人設定資產的 PatrolSpeed")]
        private float _speedOverride;

        private TargetDummy _dummy;
        private Vector3 _origin;
        private float _startTime;

        private void Awake()
        {
            _dummy = GetComponent<TargetDummy>();
        }

        private void Start()
        {
            _origin = transform.position;
            _startTime = Time.time;
        }

        private void Update()
        {
            float speed = _speedOverride > 0f
                ? _speedOverride
                : (_dummy.Config != null ? _dummy.Config.PatrolSpeed : 2f);
            Vector3 position = PingPongMover.Position(
                _origin, _origin + _travelOffset, speed, Time.time - _startTime, out int direction);
            transform.position = position;
            Vector3 facing = _travelOffset * direction;
            facing.y = 0f;
            if (facing.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(facing);
            }
        }
    }
}

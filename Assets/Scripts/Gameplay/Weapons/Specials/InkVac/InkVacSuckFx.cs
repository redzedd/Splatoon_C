using SplatoonC.Core;
using SplatoonC.Gameplay.Painting;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 被 Ink Vac 吸進去的東西(敵方墨彈、炸彈、爆炸):一團墨從被吸的位置繞著錐軸旋進噴嘴(0.22 秒),
    // 顏色 = 被吸的那一隊(吸敵方的墨 → 敵方色)。池化。
    public sealed class InkVacSuckFx : MonoBehaviour
    {
        private const float Duration = 0.22f;

        private Renderer[] _renderers;
        private Color _applied = Color.clear;
        private TrailRenderer _trail;
        private Transform _target;
        private Vector3 _targetOffset;
        private Vector3 _from;
        private float _startedAt;
        private float _scale;

        private void Awake()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            _trail = GetComponentInChildren<TrailRenderer>(true);
        }

        // from:被吸的位置;target + localOffset:噴嘴口(跟著噴嘴走);size:依吸入量(大東西大團)
        public void Begin(Vector3 from, Transform target, Vector3 localOffset, Team inkTeam, float size)
        {
            _from = from;
            _target = target;
            _targetOffset = localOffset;
            _startedAt = Time.time;
            _scale = Mathf.Clamp(size, 0.25f, 1.2f);
            transform.position = from;
            transform.localScale = Vector3.one * _scale;
            TeamTint.ApplyCached(_renderers, ref _applied, TeamPalette.ColorOf(inkTeam));
            if (_trail != null)
            {
                _trail.Clear();
            }
        }

        private void LateUpdate()
        {
            float t = (Time.time - _startedAt) / Duration;
            Vector3 to = _target != null ? _target.TransformPoint(_targetOffset) : _from;
            if (t >= 1f || _target == null || !_target.gameObject.activeInHierarchy)
            {
                SharedPool.Despawn(this);
                return;
            }
            // 加速旋進:位置 ease-in,繞連線轉一圈半、半徑收斂
            float e = t * t;
            Vector3 line = to - _from;
            Vector3 side = Vector3.Cross(line, Vector3.up);
            if (side.sqrMagnitude < 1e-4f)
            {
                side = Vector3.right;
            }
            side.Normalize();
            Vector3 up = Vector3.Cross(side, line.normalized);
            float angle = t * Mathf.PI * 3f;
            float swirl = (1f - e) * Mathf.Min(0.6f, line.magnitude * 0.08f);
            transform.position = _from + line * e + (side * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * swirl;
            transform.localScale = Vector3.one * (_scale * Mathf.Lerp(1f, 0.35f, e));
        }
    }
}

using System.Collections.Generic;
using SplatoonC.Core;
using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // Ink Vac 反擊彈落地後的倒數爆炸(research/04 §2.8、JP wiki キューインキ):
    //   落點立刻顯示爆炸範圍(地上的圓圈,所有人看得到)+ 一團越脹越大的墨;50F(SpawnBlastWaitFrame)後爆炸:
    //   半徑內 220 無衰減(DamageOffsetY 0.5:爆心抬高 0.5)、有視線判定、塗同半徑、擊退 700/9。
    //   倒數期間登記成 bot 預兆(IBotFleeHazard):敵方 bot 在圈內就往外跑(DESIGN_REFERENCE §5.3「50F 內離開圓圈」)。
    public sealed class InkVacBlast : MonoBehaviour, IBotHazard, IBotFleeHazard
    {
        private const float CenterLift = 0.5f;
        private const float FleeMargin = 1f;

        [SerializeField, Tooltip("地上的範圍圈(LineRenderer,本地座標半徑 1 的圓;執行期依半徑縮放)")]
        private LineRenderer _ring;

        [SerializeField, Tooltip("倒數中脹大的墨團")]
        private Transform _blob;

        private InkVacConfig _config;
        private Inkling _owner;
        private Team _team;
        private float _radius;
        private float _spawnedAt;
        private float _detonateAt;
        private bool _armed;
        private Renderer[] _renderers;
        private Color _applied = Color.clear;
        private bool _warned;

        private static readonly List<InkVacBlast> Live = new List<InkVacBlast>();

        // ── 狀態(測試、bot)──
        public Vector3 Center => transform.position;
        public float Radius => _radius;
        public Team Team => _team;
        public bool Armed => _armed;
        public float SpawnedAt => _spawnedAt;
        public float DetonateAt => _detonateAt;
        public ExplosionResult LastResult { get; private set; }

        public static int SpawnCount { get; private set; }
        public static int DetonateCount { get; private set; }
        public static InkVacBlast LastSpawned { get; private set; }
        public static float LastDetonatedAt { get; private set; }
        public static int LiveCount => Live.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetCounts()
        {
            SpawnCount = 0;
            DetonateCount = 0;
            LastSpawned = null;
            LastDetonatedAt = 0f;
            Live.Clear();
        }

        private void Awake()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
        }

        public void Arm(Vector3 point, float radius, InkVacConfig config, Inkling owner, Team team)
        {
            _config = config;
            _owner = owner;
            _team = team;
            _radius = radius;
            _spawnedAt = Time.time;
            _detonateAt = Time.time + config.BlastWaitFrames / 60f;
            _armed = true;
            _warned = false;
            transform.SetPositionAndRotation(point, Quaternion.identity);
            TeamTint.ApplyCached(_renderers, ref _applied, TeamPalette.ColorOf(team));
            if (_ring != null)
            {
                // 圈線物件轉 90°(本地 XY 平面 = 地面),縮放本地 X/Y
                _ring.transform.localScale = new Vector3(radius, radius, 1f);
                _ring.enabled = true;
            }
            SetBlob(0f);
            Live.Add(this);
            BotHazards.Register(this);
            SpawnCount++;
            LastSpawned = this;
            GameAudio.Play(config.WarnSound, point + Vector3.up, 0.9f, 0f);
        }

        private void Update()
        {
            if (!_armed)
            {
                return;
            }
            float t = Mathf.Clamp01((Time.time - _spawnedAt) / Mathf.Max(0.01f, _detonateAt - _spawnedAt));
            SetBlob(t);
            if (!_warned && t > 0.6f)
            {
                _warned = true;
                GameAudio.Play(_config.WarnSound, transform.position + Vector3.up, 1f, 0.05f);
            }
            if (Time.time >= _detonateAt)
            {
                Detonate();
            }
        }

        private void SetBlob(float t)
        {
            if (_blob != null)
            {
                // 墨團由 0.6 m 脹到 1.6 m,最後 20% 抖動(快爆了)
                float size = Mathf.Lerp(0.6f, 1.6f, t) * (t > 0.8f ? 1f + 0.08f * Mathf.Sin(Time.time * 60f) : 1f);
                _blob.localScale = Vector3.one * size;
                _blob.localPosition = new Vector3(0f, size * 0.35f, 0f);
            }
            if (_ring != null)
            {
                // 圈線越來越粗(預告)
                _ring.widthMultiplier = Mathf.Lerp(0.12f, 0.3f, t);
            }
        }

        private void Detonate()
        {
            _armed = false;
            Live.Remove(this);
            BotHazards.Unregister(this);
            DetonateCount++;
            LastDetonatedAt = Time.time;
            string sound = GameAudio.Has(_config.ExplodeSound) ? _config.ExplodeSound : "bomb_explode";
            ExplosionSpec spec = _config.BuildBlastSpec(_radius, sound);
            LastResult = Explosion.Detonate(spec, transform.position + Vector3.up * CenterLift, _team, _owner);
            if (InkSplashFxPool.Instance != null)
            {
                Color color = TeamPalette.ColorOf(_team);
                for (int i = 0; i < 8; i++)
                {
                    float angle = i * Mathf.PI * 0.25f;
                    Vector3 dir = new Vector3(Mathf.Cos(angle), 0.8f, Mathf.Sin(angle)).normalized;
                    InkSplashFxPool.Instance.Spawn(transform.position + dir * 1.2f, dir, color);
                }
            }
            if (_ring != null)
            {
                _ring.enabled = false;
            }
            _owner = null;
            SharedPool.Despawn(this);
        }

        private void OnDisable()
        {
            if (_armed)
            {
                _armed = false;
                Live.Remove(this);
                BotHazards.Unregister(this);
            }
        }

        // ── bot 預兆 ──

        public bool WantsJump(Team team, Vector3 position, float leadSeconds)
        {
            return false;
        }

        public bool Threatens(Team team, Vector3 position, out Vector3 center, out float radius)
        {
            center = transform.position;
            radius = _radius + FleeMargin;
            if (!_armed || team == _team)
            {
                return false;
            }
            Vector3 flat = position - center;
            flat.y = 0f;
            return flat.sqrMagnitude < radius * radius && Mathf.Abs(position.y - center.y) < _radius;
        }

        // point 是否在任何一個敵方(對 team 而言)倒數中的爆炸圈內(測試)
        public static bool AnyThreatens(Team team, Vector3 point)
        {
            for (int i = 0; i < Live.Count; i++)
            {
                if (Live[i] != null && Live[i].Threatens(team, point, out _, out _))
                {
                    return true;
                }
            }
            return false;
        }
    }
}

using System.Collections.Generic;
using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // Squid Beakon 的場上裝置(DeployableObject:HP 120、TargetClass.Beakon、擁有者死亡不消失)。
    // 生命週期:ThrowController 放開副武器 → Launch(腳前 PlaceForward 的地面;在空中放就往下掉)→ 著地 Deploy、
    // 塗一小圈墨、登記到 Active(超級跳目標、bot 雷達)。被超級跳用掉(OnJumpLanded)→ 次數 −1,歸零或跳過來的人
    // 副武器也是 Beakon → 消失。被打壞 → 碎裂(基底處理)。
    // 外觀:三腳架 + 桿 + 旋轉的隊伍色碟(Ink*)+ 兩顆剩餘次數燈(Light*;亮 = 還能用)。
    // 子物件命名:Ink* = 隊伍色;Light* = 次數燈;其他 = 材質原色。
    public sealed class SquidBeakon : DeployableObject
    {
        // 場上已部署、還活著的信標(超級跳目標、雷達查詢;零配置遍歷)
        public static readonly List<SquidBeakon> Active = new List<SquidBeakon>();

        // 累計(測試用)
        public static int PlacedCount { get; private set; }
        public static int ConsumedCount { get; private set; }

        [SerializeField, Tooltip("旋轉的碟(S3 信標頂端的雷達碟持續轉動)")]
        private Transform _dish;

        [SerializeField, Tooltip("剩餘次數燈(依序:第 1、2 次;亮 = 還能用)")]
        private Renderer[] _useLights;

        [SerializeField, Tooltip("碟的轉速(度/秒)")]
        private float _spinSpeed = 120f;

        private SquidBeakonConfig _config;
        private Renderer[] _neutralRenderers;
        private bool _falling;
        private Vector3 _velocity;
        private float _launchTime;
        private int _usesLeft;
        private bool _flashState;
        private int _lightsShown = -1;
        private float _blinkPhase;

        public override float MaxHp => _config != null ? _config.MaxHp : 120f;
        public override TargetClass TargetClass => TargetClass.Beakon;
        public SquidBeakonConfig Config => _config;
        public int UsesLeft => _usesLeft;
        public bool IsFalling => _falling;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetStatics()
        {
            Active.Clear();
            PlacedCount = 0;
            ConsumedCount = 0;
        }

        protected override void Awake()
        {
            base.Awake();
            var neutral = new List<Renderer>();
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                if (!r.name.StartsWith("Ink", System.StringComparison.Ordinal))
                {
                    neutral.Add(r);
                }
            }
            _neutralRenderers = neutral.ToArray();
        }

        // ThrowController 放開副武器時呼叫:位置改成擁有者腳前 PlaceForward 的點(S3 放在腳下,不是丟出去)
        public override void Launch(Vector3 position, Vector3 velocity, SubWeaponConfig config, Inkling owner)
        {
            _config = config as SquidBeakonConfig;
            Owner = owner;
            _usesLeft = _config != null ? _config.Uses : 2;
            _lightsShown = -1;
            Vector3 start = position;
            if (owner != null)
            {
                Vector3 forward = owner.AimView != null
                    ? Quaternion.Euler(0f, owner.AimView.Yaw, 0f) * Vector3.forward
                    : owner.transform.forward;
                forward.y = 0f;
                forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
                float ahead = _config != null ? _config.PlaceForward : 0.8f;
                start = owner.transform.position + forward * ahead + Vector3.up * 0.6f;
                transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            }
            transform.position = start;
            _velocity = Vector3.zero;
            _falling = true;
            _launchTime = Time.time;
            gameObject.SetActive(true);
            // 腳下(1.5 m 內)有地面 → 當場放好(S3 放在腳下,不是丟出去);空中放才往下掉
            int mask = _config != null ? (int)_config.HitMask : CombatMasks.Terrain;
            if (Physics.Raycast(start, Vector3.down, out RaycastHit hit, 1.5f, mask, QueryTriggerInteraction.Ignore))
            {
                Land(hit.point, hit.normal);
            }
        }

        private void Update()
        {
            if (_falling)
            {
                StepFall(Time.deltaTime);
                if (_falling && Time.time - _launchTime > (_config != null ? _config.Lifetime : 8f))
                {
                    // 掉出場外
                    _falling = false;
                    Expire();
                }
                return;
            }
            if (!IsAlive)
            {
                return;
            }
            if (_dish != null)
            {
                _dish.Rotate(0f, _spinSpeed * Time.deltaTime, 0f, Space.Self);
            }
            UpdateLights();
        }

        // 往下掉(重力),碰到地形就放下
        private void StepFall(float deltaTime)
        {
            float gravity = _config != null ? _config.FallGravity : -20f;
            _velocity.y += gravity * deltaTime;
            Vector3 position = transform.position;
            float step = Mathf.Max(0.02f, -_velocity.y * deltaTime);
            int mask = _config != null ? (int)_config.HitMask : CombatMasks.Terrain;
            if (Physics.Raycast(position + Vector3.up * 0.05f, Vector3.down, out RaycastHit hit, 0.65f + step, mask,
                    QueryTriggerInteraction.Ignore))
            {
                Land(hit.point, hit.normal);
                return;
            }
            transform.position = position + _velocity * deltaTime;
        }

        private void Land(Vector3 point, Vector3 normal)
        {
            _falling = false;
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            transform.SetPositionAndRotation(point, Quaternion.LookRotation(
                forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward, Vector3.up));
            Inkling owner = Owner;
            Team team = owner != null ? owner.Team : Team.Alpha;
            Deploy(owner, team);
            _flashState = false;
            RetintNeutral();
            if (!Active.Contains(this))
            {
                Active.Add(this);
            }
            PlacedCount++;
            // 腳下塗一小圈(副武器的墨照常充大招)
            float paint = _config != null ? _config.PaintRadius : 1f;
            if (paint > 0f && owner != null)
            {
                InkPainter.PaintSphere(point + normal * 0.05f, paint, _config != null ? _config.PaintHardness : 0.7f,
                    owner.InkSource(true));
            }
            if (InkSplashFxPool.Instance != null)
            {
                InkSplashFxPool.Instance.Spawn(point + normal * 0.05f, normal, TeamPalette.ColorOf(team));
            }
            GameAudio.PlayFrom(owner != null && owner.IsLocalPlayer, _config != null ? _config.DeploySound : "beakon_place",
                point, 0.9f, 0.05f);
            UpdateLights();
        }

        // ── 超級跳 ──

        // 這位隊友現在能不能跳過來
        public bool CanBeJumpedBy(Inkling jumper)
        {
            return jumper != null && IsAlive && !_falling && Team == jumper.Team && _usesLeft > 0;
        }

        // 超級跳落地(S3:次數在著地時才算)。跳過來的人副武器也是 Beakon → 一次用掉整個
        public void OnJumpLanded(Inkling jumper)
        {
            if (!IsAlive)
            {
                return;
            }
            bool jumperHasBeakon = false;
            var loadout = jumper != null ? jumper.GetComponent<WeaponLoadout>() : null;
            if (loadout != null && loadout.Kit != null && loadout.Kit.Sub is SquidBeakonConfig)
            {
                jumperHasBeakon = true;
            }
            _usesLeft = jumperHasBeakon ? 0 : _usesLeft - 1;
            ConsumedCount++;
            GameAudio.Play(_config != null ? _config.UseSound : "beakon_use", transform.position, 0.9f, 0.05f);
            if (_usesLeft <= 0)
            {
                if (InkSplashFxPool.Instance != null)
                {
                    InkSplashFxPool.Instance.Spawn(transform.position + Vector3.up * 0.6f, Vector3.up,
                        TeamPalette.ColorOf(Team));
                }
                Expire();
                return;
            }
            UpdateLights();
        }

        // ── 雷達 ──

        // team 方的信標是否偵測到 position(S3:半徑 15 u、上下 ±15 u 的圓柱)
        public static bool InRadar(Vector3 position, Team team)
        {
            for (int i = 0; i < Active.Count; i++)
            {
                SquidBeakon beakon = Active[i];
                if (beakon == null || !beakon.IsAlive || beakon.Team != team)
                {
                    continue;
                }
                float radius = beakon._config != null ? beakon._config.RadarRadius : 15f;
                float height = beakon._config != null ? beakon._config.RadarHeight : 15f;
                Vector3 delta = position - beakon.transform.position;
                if (Mathf.Abs(delta.y) <= height && delta.x * delta.x + delta.z * delta.z <= radius * radius)
                {
                    return true;
                }
            }
            return false;
        }

        // ── 生命週期 ──

        protected override void OnBroken(in DamageInfo info)
        {
            Active.Remove(this);
        }

        public override void Expire()
        {
            Active.Remove(this);
            _falling = false;
            base.Expire();
        }

        protected override void OnDisable()
        {
            Active.Remove(this);
            base.OnDisable();
        }

        protected override void LateUpdate()
        {
            base.LateUpdate();
            if (!IsDeployed)
            {
                return;
            }
            // 基底只在受擊閃白切換時重上整個物件的隊伍色 → 跟著把原色部位改回來
            if (IsFlashing != _flashState)
            {
                _flashState = IsFlashing;
                RetintNeutral();
                _lightsShown = -1;
                UpdateLights();
            }
        }

        private void RetintNeutral()
        {
            if (_neutralRenderers == null)
            {
                return;
            }
            for (int i = 0; i < _neutralRenderers.Length; i++)
            {
                if (_neutralRenderers[i] != null)
                {
                    _neutralRenderers[i].SetPropertyBlock(null);
                }
            }
        }

        // 次數燈:還能用的次數亮(隊伍色偏白,緩慢閃爍);用掉的暗
        private void UpdateLights()
        {
            if (_useLights == null)
            {
                return;
            }
            _blinkPhase += Time.deltaTime;
            bool blinkOn = Mathf.Repeat(_blinkPhase, 1.2f) < 0.9f;
            int shown = blinkOn ? _usesLeft : -_usesLeft - 1;
            if (shown == _lightsShown)
            {
                return;
            }
            _lightsShown = shown;
            Color lit = Color.Lerp(TeamPalette.ColorOf(Team), Color.white, blinkOn ? 0.45f : 0.1f);
            Color off = new Color(0.12f, 0.12f, 0.14f, 1f);
            for (int i = 0; i < _useLights.Length; i++)
            {
                if (_useLights[i] != null)
                {
                    TeamTint.Apply(_useLights[i], i < _usesLeft ? lit : off);
                }
            }
        }
    }
}

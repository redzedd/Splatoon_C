using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 池化偵測器(S3 PointSensor;參數見 PointSensorConfig / PointSensorModel)。以 60 Hz 固定步模擬(SimulatedBomb):
    //   飛行:S3 通用彈道(與預覽同一個 S3ThrownFlight),碰到任何東西(地形、敵方/中立目標、敵方屏障/球罩)就爆開;
    //         敵方變身中的 Torpedo 穿過(S3 GuideHitCollisionType …TorpedoOff)。
    //   感應:爆開點為中心 r 6.0 的感應球停留 150F,每幀把球內的敵方角色標記(SubMarking → Marking,穿牆),
    //         標記秒數 = (480 − 命中後經過幀)/ 60:命中當下在裡面的 8 秒、晚進來的較短、範圍消失後不再標記。
    //   外觀:本體翻滾飛行;爆開後隊伍色半透明感應球展開 + 脈動,最後 0.4 秒淡出。
    public sealed class PointSensor : SimulatedBomb
    {
        [SerializeField, Tooltip("感應球(半徑 1 的球,執行期縮放到感應半徑;放在根物件下,平時關閉)")]
        private Renderer _dome;

        [SerializeField, Tooltip("地面感應圈(外半徑 1 的平面環;放在根物件下,平時關閉)")]
        private Renderer _ring;

        private PointSensorConfig _config;
        private PointSensorParams _p;
        private S3ThrownFlight _flight;
        private bool _sensing;
        private int _burstFrame;
        private float _burstTime;
        private Vector3 _center;
        private Vector3 _groundPoint;
        private Vector3 _groundNormal = Vector3.up;
        private bool _hasGround;
        private int _markedThisBurst;
        private Vector3 _spinAxis = Vector3.right;
        private Color _appliedDome = Color.clear;
        private Color _appliedRing = Color.clear;
        private Renderer[] _domeArray;
        private Renderer[] _ringArray;

        // 測試/除錯
        public static int LaunchCount { get; private set; }
        public static int BurstCount { get; private set; }
        public static float LastLaunchTime { get; private set; }
        public static float LastBurstTime { get; private set; }
        public static int LastBurstFrame { get; private set; }
        public static Vector3 LastBurstCenter { get; private set; }
        public static BombContact LastContact { get; private set; }
        public static IDamageable LastContactTarget { get; private set; }
        // 最近一顆感應期間標記的次數(每幀每人一次)
        public static int LastMarkApplications { get; private set; }

        public bool IsSensing => _sensing;
        public Vector3 Center => _center;
        public int SensingFrames => _sensing ? Frame - _burstFrame : -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetCounters()
        {
            LaunchCount = 0;
            BurstCount = 0;
            LastContactTarget = null;
            LastLaunchTime = float.NegativeInfinity;
            LastBurstTime = float.NegativeInfinity;
        }

        protected override void Awake()
        {
            base.Awake();
            _domeArray = _dome != null ? new[] { _dome } : new Renderer[0];
            _ringArray = _ring != null ? new[] { _ring } : new Renderer[0];
            ShowField(false);
        }

        public override void Launch(Vector3 position, Vector3 velocity, SubWeaponConfig config, Inkling owner)
        {
            _config = config as PointSensorConfig;
            if (_config == null)
            {
                Debug.LogError($"PointSensor:設定型別不符({config})", this);
                Despawn();
                return;
            }
            Begin(position, owner);
            _p = _config.S3;
            _flight.Init(position, velocity * SimFrames.Dt);
            _sensing = false;
            _markedThisBurst = 0;
            _spinAxis = Random.onUnitSphere;
            Model.gameObject.SetActive(true);
            ShowField(false);
            SetLightColor(new Color(0.4f, 1f, 0.55f));
            LaunchCount++;
            LastLaunchTime = Time.time;
        }

        protected override bool Step()
        {
            if (_sensing)
            {
                return StepSensing();
            }
            if (Frame > SimFrames.ToFrames(_config.Lifetime))
            {
                Release();
                return false;
            }
            Vector3 from = _flight.Position;
            Vector3 stepVelocity = _flight.Velocity;
            BombContact contact = _flight.Step(_p.Throw, _p.CollisionRadius, BombTeam, true, out RaycastHit hit,
                out IDamageable target);
            SimPosition = _flight.Position;
            if (contact == BombContact.None)
            {
                return true;
            }
            // S3 …TorpedoOff:敵方 Torpedo 穿過——這一步照常走完(StepAir:位置 += 步進前速度),
            // 不停在 Torpedo 表面(否則下一步從受擊球裡出發又撞上,會卡在原地)
            if (contact == BombContact.Detonator && hit.collider != null
                && hit.collider.GetComponentInParent<Torpedo>() != null)
            {
                _flight.Position = from + stepVelocity;
                SimPosition = _flight.Position;
                return true;
            }
            BeginSensing(contact, hit, target);
            return true;
        }

        private void BeginSensing(BombContact contact, in RaycastHit hit, IDamageable target)
        {
            _sensing = true;
            _burstFrame = Frame;
            _burstTime = Time.time;
            // 地形:爆心在接觸點沿法線外一點;目標/屏障:接觸時的球心
            _center = contact == BombContact.Terrain ? hit.point + hit.normal * 0.1f : SimPosition;
            SimPosition = _center;
            PrevPosition = _center;
            _hasGround = Physics.Raycast(_center + Vector3.up * 0.3f, Vector3.down, out RaycastHit ground,
                _p.Radius + 0.3f, CombatMasks.Terrain, QueryTriggerInteraction.Ignore);
            _groundPoint = _hasGround ? ground.point : _center;
            _groundNormal = _hasGround ? ground.normal : Vector3.up;
            Model.gameObject.SetActive(false);
            ShowField(true);
            BurstCount++;
            LastBurstFrame = Frame;
            LastBurstTime = Time.time;
            LastBurstCenter = _center;
            LastContact = contact;
            LastContactTarget = target;
            GameAudio.Play(_config.BurstSound, _center, 0.9f, 0.03f);
            if (InkSplashFxPool.Instance != null)
            {
                InkSplashFxPool.Instance.Spawn(_center, contact == BombContact.Terrain ? hit.normal : Vector3.up,
                    TeamPalette.ColorOf(BombTeam));
            }
            // 命中這一幀就開始標記
            MarkInside();
        }

        private bool StepSensing()
        {
            if (Frame - _burstFrame >= _p.LingerFrames)
            {
                LastMarkApplications = _markedThisBurst;
                Release();
                return false;
            }
            MarkInside();
            return true;
        }

        // S3:範圍內的敵人標記 480F(從命中起算)
        private void MarkInside()
        {
            float seconds = PointSensorModel.MarkSecondsAt(_p, Frame - _burstFrame);
            _markedThisBurst += SubMarking.MarkSphere(_center, _p.Radius, BombTeam, seconds);
        }

        protected override void OnReleased()
        {
            ShowField(false);
            Model.gameObject.SetActive(true);
            _config = null;
        }

        private void ShowField(bool on)
        {
            if (_dome != null)
            {
                _dome.enabled = on;
            }
            if (_ring != null)
            {
                _ring.enabled = on;
            }
        }

        // ── 外觀 ──

        protected override void UpdateVisual()
        {
            if (!_sensing)
            {
                transform.Rotate(_spinAxis, 540f * Time.deltaTime, Space.World);
                SetLights(Blink(Time.time, 0.2f));
                return;
            }
            transform.position = _center;
            float elapsed = Time.time - _burstTime;
            float total = _p.LingerFrames / 60f;
            float grow = Mathf.Clamp01(elapsed / Mathf.Max(0.02f, _config.ExpandSeconds));
            float eased = 1f - (1f - grow) * (1f - grow);
            float fade = Mathf.Clamp01((total - elapsed) / 0.4f);
            float pulse = 0.75f + 0.25f * Mathf.Sin(elapsed * 9f);
            Color color = TeamPalette.ColorOf(BombTeam);
            if (_dome != null)
            {
                Transform dome = _dome.transform;
                dome.position = _center;
                dome.rotation = Quaternion.identity;
                dome.localScale = Vector3.one * (_p.Radius * 2f * eased);
                Color c = color;
                c.a = _config.DomeAlpha * pulse * fade;
                TeamTint.ApplyCached(_domeArray, ref _appliedDome, Quantize(c));
            }
            if (_ring != null)
            {
                Transform ring = _ring.transform;
                ring.position = _groundPoint + _groundNormal * 0.04f;
                ring.rotation = Quaternion.FromToRotation(Vector3.up, _groundNormal);
                // 雷達波:每 0.6 秒從中心掃到外緣
                float sweep = Mathf.Repeat(elapsed, 0.6f) / 0.6f;
                float radius = Mathf.Lerp(0.5f, _p.Radius, sweep) * eased;
                ring.localScale = new Vector3(radius, 1f, radius);
                Color c = color;
                c.a = 0.6f * (1f - sweep) * fade;
                TeamTint.ApplyCached(_ringArray, ref _appliedRing, Quantize(c));
            }
        }

        // 透明度量化成 32 階:顏色沒變就不重寫 MaterialPropertyBlock
        private static Color Quantize(Color c)
        {
            c.a = Mathf.Round(c.a * 32f) / 32f;
            return c;
        }
    }
}

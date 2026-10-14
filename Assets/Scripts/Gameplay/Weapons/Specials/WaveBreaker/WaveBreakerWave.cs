using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // Wave Breaker 的一道衝擊波(SharedPool;WaveBreakerDevice 在第 90 / 240 / 390F 各放一道):
    //   以裝置為中心,160F 內半徑 0 → 20 等速擴張(7.5 m/s),沿地形走(穿牆、穿 Big Bubbler,不塗墨)。
    //   判定 = 圓柱側面與地形的交線:這一幀被波前(半寬 0.35 [EST])掃過、腳貼著地(離地 ≤ 0.5 m [EST])或在牆上游
    //   (烏賊爬牆)的敵人吃 45 + 標記 480F,每道波每人一次;高度要在裝置 +2.8 / −5.2 之內。跳起來就躲得過。
    //   非角色目標(假人、場上裝置)一律視為貼地;Big Bubbler 的屏障面不吃波(發射座 = 弱點照吃)。
    //   外觀:沿地形平貼地面的隊伍色環(LineRenderer,每點往下打射線貼地)+ 環上一圈往上淡出的半透明波牆
    //   (高 0.8 m [EST],第三人稱視角也一眼看得出「一道要跳過去的牆」),大家都看得到 → 預兆。
    //   也是 bot 的預兆(IBotHazard):波前快到時叫敵方 bot 起跳。
    //   感應模式(EmitSensor):設置當下的「範圍預告光」—— 細環在 57F 內依設置感應半徑擴到 12.3 後消失,不傷人、不是預兆。
    public sealed class WaveBreakerWave : MonoBehaviour, IBotHazard
    {
        private const int Segments = 72;
        private const int MaxHits = 32;
        private const float BodyRadius = 0.4f;
        private const float RingLift = 0.12f;
        // 波牆外觀:高度(公尺)[EST:S3 的波約到膝上,判定只在腳下]與底部不透明度(往上由貼圖淡出)
        private const float BandHeight = 0.8f;
        private const float BandAlpha = 0.75f;

        [SerializeField, Tooltip("環(LineRenderer,loop、世界座標)")]
        private LineRenderer _ring;

        [SerializeField, Tooltip("波牆(MeshFilter;頂點每幀沿環的貼地點重建,材質是往上淡出的半透明隊伍色)")]
        private MeshFilter _band;

        private readonly Vector3[] _points = new Vector3[Segments];
        private readonly IDamageable[] _hit = new IDamageable[MaxHits];
        private int _hitCount;
        // 波前經過時人在空中的角色(診斷:跳過波的次數;落地時仍在波前帶內照樣會被打到)
        private readonly IDamageable[] _airborne = new IDamageable[MaxHits];
        private int _airborneCount;
        private WaveBreakerConfig _config;
        private Inkling _owner;
        private Team _team;
        private Vector3 _center;
        private float _startedAt;
        private float _radius;
        private float _previousRadius;
        private bool _running;
        private float _speed;
        private bool _sensor;
        // 波牆網格(Awake 建一次,頂點陣列重用 → 每幀零配置)
        private Mesh _bandMesh;
        private MeshRenderer _bandRenderer;
        private Vector3[] _bandVertices;
        private float _bandAlphaShown = -1f;

        public float Radius => _radius;
        public Vector3 Center => _center;
        public bool Running => _running;
        public bool IsSensorPulse => _sensor;
        public int HitCount => _hitCount;
        public Team Team => _team;

        // 累計(測試用):波打中角色/被跳過的次數
        public static int PlayerHits { get; private set; }
        public static int DodgedByJump { get; private set; }
        public static int EmitCount { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetCounts()
        {
            PlayerHits = 0;
            DodgedByJump = 0;
            EmitCount = 0;
        }

        private void Awake()
        {
            if (_band == null)
            {
                return;
            }
            _bandRenderer = _band.GetComponent<MeshRenderer>();
            // Segments + 1 欄(最後一欄與第一欄重合,貼圖 u 連續)× 上下兩列;三角形固定,頂點每幀改寫
            int columns = Segments + 1;
            _bandVertices = new Vector3[columns * 2];
            var uv = new Vector2[columns * 2];
            var triangles = new int[Segments * 6];
            for (int i = 0; i < columns; i++)
            {
                float u = i / (float)Segments;
                uv[i * 2] = new Vector2(u, 0f);
                uv[i * 2 + 1] = new Vector2(u, 1f);
            }
            for (int i = 0; i < Segments; i++)
            {
                int v = i * 2;
                int t = i * 6;
                triangles[t] = v;
                triangles[t + 1] = v + 1;
                triangles[t + 2] = v + 2;
                triangles[t + 3] = v + 2;
                triangles[t + 4] = v + 1;
                triangles[t + 5] = v + 3;
            }
            _bandMesh = new Mesh { name = "WaveBreakerBand" };
            _bandMesh.MarkDynamic();
            _bandMesh.vertices = _bandVertices;
            _bandMesh.uv = uv;
            _bandMesh.triangles = triangles;
            _band.sharedMesh = _bandMesh;
            if (_bandRenderer != null)
            {
                _bandRenderer.enabled = false;
            }
        }

        private void OnDestroy()
        {
            if (_bandMesh != null)
            {
                Destroy(_bandMesh);
            }
        }

        public void Emit(Vector3 center, WaveBreakerConfig config, Inkling owner, Team team)
        {
            _center = center;
            _config = config;
            _owner = owner;
            _team = team;
            _startedAt = Time.time;
            _radius = 0f;
            _previousRadius = 0f;
            _hitCount = 0;
            _airborneCount = 0;
            _running = true;
            _sensor = false;
            _speed = WaveBreakerModel.WaveSpeed(config.WaveMaxRadius, config.WaveExpandFrames);
            EmitCount++;
            ShowRing(0.95f);
            UpdateRing();
            BotHazards.Register(this);
        }

        // 設置感應的範圍預告光(不傷人、不標記——標記由裝置自己判定;不是 bot 預兆)
        public void EmitSensor(Vector3 center, WaveBreakerConfig config, Team team)
        {
            _center = center;
            _config = config;
            _owner = null;
            _team = team;
            _startedAt = Time.time;
            _radius = 0f;
            _previousRadius = 0f;
            _hitCount = 0;
            _airborneCount = 0;
            _running = true;
            _sensor = true;
            ShowRing(0.6f);
            UpdateRing();
        }

        private void ShowRing(float alpha)
        {
            if (_ring == null)
            {
                return;
            }
            _ring.loop = true;
            _ring.useWorldSpace = true;
            _ring.positionCount = Segments;
            // 環平貼地面(線寬沿地面展開,不面向鏡頭):TransformZ 對齊、根物件的 +Z 朝上
            _ring.alignment = LineAlignment.TransformZ;
            transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
            TeamTint.Apply(_ring, Color.Lerp(TeamPalette.ColorOf(_team), Color.white, _sensor ? 0.45f : 0.2f), alpha);
            _ring.enabled = true;
            if (_bandRenderer != null)
            {
                // 波牆只給真的衝擊波(感應預告光只有細環);世界座標頂點 → 波牆物件放在世界原點、不旋轉
                _band.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                _bandAlphaShown = -1f;
                _bandRenderer.enabled = !_sensor;
            }
        }

        private void Update()
        {
            if (!_running || _config == null)
            {
                return;
            }
            float frames = (Time.time - _startedAt) * 60f;
            if (_sensor)
            {
                _radius = WaveBreakerModel.SensorRadius(Mathf.Max(4f, frames), _config.WaveMaxRadius);
                UpdateRing();
                if (frames >= WaveBreakerModel.SensorEndFrame)
                {
                    Release();
                }
                return;
            }
            _previousRadius = _radius;
            _radius = WaveBreakerModel.WaveRadius(frames, _config.WaveMaxRadius, _config.WaveExpandFrames);
            Sweep(_previousRadius, _radius);
            UpdateRing();
            if (frames >= _config.WaveExpandFrames)
            {
                Release();
            }
        }

        // 這一幀波前從 previous 掃到 radius
        private void Sweep(float previous, float radius)
        {
            float halfBand = _config.WaveHalfBand;
            // 角色:腳底高度差在範圍內、被波前掃到、貼地或在牆上游
            var all = Inkling.All;
            for (int i = 0; i < all.Count; i++)
            {
                Inkling inkling = all[i];
                if (inkling == null || inkling.Team == _team)
                {
                    continue;
                }
                Vector3 feet = inkling.transform.position;
                if (!WaveBreakerModel.InVerticalRange(feet.y - _center.y))
                {
                    continue;
                }
                float distance = Flat(feet - _center);
                if (!WaveBreakerModel.FrontSweeps(previous, radius, distance, BodyRadius, halfBand))
                {
                    continue;
                }
                var health = inkling.GetComponent<InklingHealth>();
                if (health == null || !health.IsAlive || AlreadyHit(health))
                {
                    continue;
                }
                if (!Grounded(inkling))
                {
                    // 人在空中:這一幀躲過(波前帶還罩著他時落地一樣會被打到)
                    if (!Contains(_airborne, _airborneCount, health) && _airborneCount < MaxHits)
                    {
                        _airborne[_airborneCount++] = health;
                        DodgedByJump++;
                    }
                    continue;
                }
                Remember(health);
                health.ApplyDamage(new DamageInfo(_config.WaveDamage, feet, _team, _owner, DamageKind.Special,
                    AttackClass.WaveBreaker));
                Marking.Apply(inkling, _team, _config.WaveMarkSeconds);
                PlayerHits++;
            }
            // 其他目標(假人、場上裝置):視為貼地;屏障面不吃波
            var targets = DamageableRegistry.Active;
            for (int i = targets.Count - 1; i >= 0; i--)
            {
                if (i >= targets.Count)
                {
                    continue;
                }
                IDamageable target = targets[i];
                if (target == null || target is InklingHealth || target is BarrierSphere || !target.IsAlive
                    || !DamageRules.CanDamage(_team, target) || AlreadyHit(target))
                {
                    continue;
                }
                Vector3 point = target.ClosestPoint(_center);
                if (!WaveBreakerModel.InVerticalRange(point.y - _center.y))
                {
                    continue;
                }
                if (!WaveBreakerModel.FrontSweeps(previous, radius, Flat(point - _center), 0f, halfBand))
                {
                    continue;
                }
                Remember(target);
                target.ApplyDamage(new DamageInfo(_config.WaveDamage, point, _team, _owner, DamageKind.Special,
                    AttackClass.WaveBreaker));
            }
        }

        // 腳貼地(離地 ≤ WaveHitHeight)或正在牆上游(S3:人形只是碰到牆不算)
        private bool Grounded(Inkling inkling)
        {
            var locomotion = inkling.GetComponent<PlayerLocomotion>();
            if (locomotion != null && locomotion.IsClimbing)
            {
                return true;
            }
            var controller = inkling.GetComponent<CharacterController>();
            if (controller != null && controller.enabled && controller.isGrounded)
            {
                return true;
            }
            Vector3 feet = inkling.transform.position + Vector3.up * 0.05f;
            return Physics.Raycast(feet, Vector3.down, _config.WaveHitHeight + 0.05f, CombatMasks.Terrain,
                QueryTriggerInteraction.Ignore);
        }

        private bool AlreadyHit(IDamageable target)
        {
            return Contains(_hit, _hitCount, target);
        }

        private static bool Contains(IDamageable[] list, int count, IDamageable target)
        {
            for (int i = 0; i < count; i++)
            {
                if (ReferenceEquals(list[i], target))
                {
                    return true;
                }
            }
            return false;
        }

        private void Remember(IDamageable target)
        {
            if (_hitCount < MaxHits)
            {
                _hit[_hitCount++] = target;
            }
        }

        // 環沿地形:每個點從裝置高度 +2.8 往下找地面(到 −5.2 為止),找不到就留在裝置高度
        private void UpdateRing()
        {
            if (_ring == null)
            {
                return;
            }
            float top = _center.y + WaveBreakerModel.DamageYMax + 0.3f;
            float reach = WaveBreakerModel.DamageYMax - WaveBreakerModel.DamageYMin + 0.6f;
            float radius = Mathf.Max(0.05f, _radius);
            for (int i = 0; i < Segments; i++)
            {
                float angle = i * Mathf.PI * 2f / Segments;
                float x = _center.x + Mathf.Cos(angle) * radius;
                float z = _center.z + Mathf.Sin(angle) * radius;
                float y = _center.y;
                if (Physics.Raycast(new Vector3(x, top, z), Vector3.down, out RaycastHit hit, reach, CombatMasks.Terrain,
                        QueryTriggerInteraction.Ignore))
                {
                    y = hit.point.y;
                }
                _points[i] = new Vector3(x, y + RingLift, z);
            }
            _ring.SetPositions(_points);
            if (_sensor)
            {
                // 預告光:細環,後半段變細消失
                float age = (Time.time - _startedAt) * 60f / WaveBreakerModel.SensorEndFrame;
                _ring.widthMultiplier = Mathf.Lerp(0.3f, 0.05f, Mathf.Clamp01((age - 0.4f) / 0.6f));
                return;
            }
            // 擴張到最後 20% 時淡出
            float fade = _config.WaveMaxRadius > 0f ? Mathf.Clamp01((1f - _radius / _config.WaveMaxRadius) / 0.2f) : 1f;
            _ring.widthMultiplier = Mathf.Lerp(0.15f, 0.5f, fade);
            UpdateBand(fade);
        }

        // 波牆:環的每個貼地點往上立 BandHeight(波牆物件在世界原點 → 頂點直接用世界座標)
        private void UpdateBand(float fade)
        {
            if (_bandRenderer == null || !_bandRenderer.enabled)
            {
                return;
            }
            for (int i = 0; i <= Segments; i++)
            {
                Vector3 ground = _points[i % Segments];
                ground.y -= RingLift - 0.02f;
                _bandVertices[i * 2] = ground;
                ground.y += BandHeight;
                _bandVertices[i * 2 + 1] = ground;
            }
            _bandMesh.vertices = _bandVertices;
            float extent = _radius * 2f + 2f;
            _bandMesh.bounds = new Bounds(_center, new Vector3(extent, 20f, extent));
            float alpha = BandAlpha * fade;
            if (Mathf.Abs(alpha - _bandAlphaShown) > 0.02f)
            {
                _bandAlphaShown = alpha;
                TeamTint.Apply(_bandRenderer, Color.Lerp(TeamPalette.ColorOf(_team), Color.white, 0.15f), alpha);
            }
        }

        // IBotHazard:敵方站在範圍內、波前再 lead 秒內掃到腳下 → 該跳了
        public bool WantsJump(Team team, Vector3 position, float leadSeconds)
        {
            if (!_running || _sensor || team == _team || !WaveBreakerModel.InVerticalRange(position.y - _center.y))
            {
                return false;
            }
            float arrive = Flat(position - _center) - BodyRadius - _config.WaveHalfBand;
            if (arrive <= _radius)
            {
                return false;
            }
            float seconds = WaveBreakerModel.SecondsUntilFront(_radius, arrive, _speed, _config.WaveMaxRadius);
            return seconds >= 0f && seconds <= leadSeconds;
        }

        private static float Flat(Vector3 v)
        {
            return Mathf.Sqrt(v.x * v.x + v.z * v.z);
        }

        private void Release()
        {
            if (!_running)
            {
                return;
            }
            _running = false;
            _sensor = false;
            BotHazards.Unregister(this);
            for (int i = 0; i < _hitCount; i++)
            {
                _hit[i] = null;
            }
            for (int i = 0; i < _airborneCount; i++)
            {
                _airborne[i] = null;
            }
            _hitCount = 0;
            _airborneCount = 0;
            _owner = null;
            if (_ring != null)
            {
                _ring.enabled = false;
            }
            if (_bandRenderer != null)
            {
                _bandRenderer.enabled = false;
            }
            SharedPool.Despawn(this);
        }

        private void OnDisable()
        {
            if (_running)
            {
                _running = false;
                BotHazards.Unregister(this);
            }
        }
    }
}

using System;
using SplatoonC.Core;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Match;
using SplatoonC.Gameplay.Painting;
using UnityEngine;

namespace SplatoonC.Gameplay.Player
{
    // 擊倒 → 重生流程:爆成擊倒者的墨色(半徑 5m + 10 滴)、隱藏並停用操作、大招保留一半、
    // 7.5 秒後回到己方基地出生點(Splatoon 3:可操作前 450F),滿血滿墨並給 30 HP 出生護甲 235F。
    [RequireComponent(typeof(Inkling), typeof(InklingHealth))]
    public sealed class InklingLifecycle : MonoBehaviour
    {
        [SerializeField, Tooltip("擊倒到重生的秒數(S3:爆炸 30F + 自身鏡頭 90F + 擊殺鏡頭 270F + 重生動畫 60F = 7.5s)")]
        private float _respawnDelay = 7.5f;

        [SerializeField, Tooltip("擊倒爆墨半徑(公尺,S3 DieBlastParam 5.0)")]
        private float _splatPaintRadius = 5f;

        [SerializeField, Tooltip("擊倒爆墨四散的墨滴數(S3:10)")]
        private int _splatDroplets = 10;

        [SerializeField, Tooltip("四散墨滴的塗墨半徑(公尺,S3:1.0)")]
        private float _splatDropletRadius = 1f;

        [SerializeField, Tooltip("墨滴飛散距離(公尺)")]
        private float _splatDropletReach = 4.5f;

        [SerializeField, Range(0f, 1f), Tooltip("被擊倒後大招能量保留比例(S3 0 AP:0.5)")]
        private float _specialKeepOnDeath = 0.5f;

        [SerializeField, Tooltip("出生護甲耐久(HP)")]
        private float _spawnArmor = 30f;

        [SerializeField, Tooltip("出生護甲持續秒數(S3:235F)")]
        private float _spawnArmorSeconds = 235f / 60f;

        [SerializeField, Tooltip("掉出場外(y 低於此值)直接判定死亡")]
        private float _killPlaneY = -8f;

        [SerializeField, Tooltip("視覺根(整個隱藏);留空自動找子物件 Visual")]
        private Transform _visualRoot;

        private Inkling _inkling;
        private InklingHealth _health;
        private CharacterController _controller;
        private PlayerInputRouter _input;
        private PlayerInkTank _tank;
        private PlayerSpecialGauge _special;
        private Behaviour[] _controlBehaviours;
        private Vector3 _fallbackSpawnPosition;
        private Quaternion _fallbackSpawnRotation;
        private readonly IDamageDealer[] _assistBuffer = new IDamageDealer[4];

        public bool IsDead { get; private set; }
        public float DiedAt { get; private set; }
        public float RespawnAt { get; private set; }
        public Vector3 DeathPosition { get; private set; }
        public IDamageDealer Killer { get; private set; }

        // 出生槽位(TeamBase 的第幾個出生點);由比賽管理指定
        public int SpawnSlot { get; set; }

        public float RespawnRemaining => IsDead ? Mathf.Max(0f, RespawnAt - Time.time) : 0f;

        // 自己的出生點(重生位置;A4 超級跳「跳回出生點」的落點):自家 TeamBase 的出生槽,沒有基地(試射場)用場景初始位置
        public Vector3 SpawnPosition
        {
            get
            {
                TeamBase teamBase = _inkling != null ? TeamBase.Of(_inkling.Team) : null;
                Transform spawn = teamBase != null ? teamBase.GetSpawn(SpawnSlot) : null;
                return spawn != null ? spawn.position : _fallbackSpawnPosition;
            }
        }

        public float SpawnYaw
        {
            get
            {
                TeamBase teamBase = _inkling != null ? TeamBase.Of(_inkling.Team) : null;
                Transform spawn = teamBase != null ? teamBase.GetSpawn(SpawnSlot) : null;
                return (spawn != null ? spawn.rotation : _fallbackSpawnRotation).eulerAngles.y;
            }
        }

        // 被擊倒(致命資訊)/重生完成
        public event Action<DamageInfo> Died;
        public event Action Respawned;

        // 暫停重生倒數(比賽結束後不再重生)
        public bool RespawnSuspended { get; set; }

        private void Awake()
        {
            _inkling = GetComponent<Inkling>();
            _health = GetComponent<InklingHealth>();
            _controller = GetComponent<CharacterController>();
            _input = GetComponent<PlayerInputRouter>();
            _tank = GetComponent<PlayerInkTank>();
            _special = GetComponent<PlayerSpecialGauge>();
            if (_visualRoot == null)
            {
                _visualRoot = transform.Find("Visual");
            }
            // 死亡期間停用:移動、烏賊、投擲、墨量、大招(順序無關);主武器換裝會換元件,在 SetAlive 時才取
            _controlBehaviours = new Behaviour[]
            {
                GetComponent<PlayerLocomotion>(),
                GetComponent<SquidController>(),
                GetComponent<ThrowController>(),
                _tank,
                _special,
            };
            _fallbackSpawnPosition = transform.position;
            _fallbackSpawnRotation = transform.rotation;
        }

        private void OnEnable()
        {
            _health.Splatted += OnSplatted;
        }

        private void OnDisable()
        {
            _health.Splatted -= OnSplatted;
        }

        private void Update()
        {
            if (!IsDead)
            {
                if (transform.position.y < _killPlaneY)
                {
                    // 掉出場外:沒有擊倒者、不爆墨(S3 掉落重生較短,4.5s)
                    Die(new DamageInfo(0f, transform.position, Team.None, null, DamageKind.Environment), 4.5f, false);
                }
                return;
            }
            if (!RespawnSuspended && Time.time >= RespawnAt)
            {
                Respawn();
            }
        }

        private void OnSplatted(DamageInfo info)
        {
            Die(info, _respawnDelay, true);
        }

        private void Die(in DamageInfo info, float delay, bool burst)
        {
            if (IsDead)
            {
                return;
            }
            IsDead = true;
            DiedAt = Time.time;
            // 暫時效果(能量站的 Quick Respawn 等)在清除前先算進這次的重生時間與大招保留(InklingModifiers 於 Died 時清除)
            InklingModifiers modifiers = _inkling.Modifiers;
            RespawnAt = Time.time + modifiers.RespawnDelay(delay, burst && info.Attacker != null);
            DeathPosition = transform.position;
            Killer = info.Attacker;
            _inkling.RegisterDeath();

            // 助攻:窗內打過我的其他敵人
            if (Killer != null)
            {
                int assists = _health.CollectAssists(Killer, _assistBuffer);
                for (int i = 0; i < assists; i++)
                {
                    if (_assistBuffer[i] is Inkling assister)
                    {
                        assister.RegisterAssist();
                    }
                }
            }

            if (burst)
            {
                SplatBurst(info);
            }
            if (_special != null)
            {
                _special.ApplyDeathPenalty(modifiers.SpecialKeepOnDeath(_specialKeepOnDeath));
            }
            SetAlive(false);
            Died?.Invoke(info);
        }

        // 爆成擊倒者的墨色:大圓 + 四散墨滴(射線落點),塗墨點數記在擊倒者身上
        private void SplatBurst(in DamageInfo info)
        {
            Team team = info.Team != Team.None ? info.Team : TeamUtil.Opponent(_inkling.Team);
            var painter = info.Attacker as IInkPainter;
            var source = new InkSource(team, painter, true);
            Vector3 center = transform.position + Vector3.up * 0.1f;
            InkPainter.PaintSphere(center, _splatPaintRadius, 0.55f, source);
            for (int i = 0; i < _splatDroplets; i++)
            {
                float angle = (i + UnityEngine.Random.value * 0.6f) * Mathf.PI * 2f / Mathf.Max(1, _splatDroplets);
                Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 origin = center + Vector3.up * 1.5f + dir * (_splatPaintRadius * 0.6f
                    + UnityEngine.Random.value * _splatDropletReach);
                if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 6f, ~GameLayers.InklingMask,
                        QueryTriggerInteraction.Ignore))
                {
                    InkPainter.PaintSphere(hit.point, _splatDropletRadius, 0.6f, source);
                }
            }
            if (InkSplashFxPool.Instance != null)
            {
                for (int i = 0; i < 4; i++)
                {
                    Vector3 offset = UnityEngine.Random.insideUnitSphere * 0.6f;
                    offset.y = Mathf.Abs(offset.y) + 0.4f;
                    InkSplashFxPool.Instance.Spawn(transform.position + offset, Vector3.up, TeamPalette.ColorOf(team));
                }
            }
        }

        private void Respawn()
        {
            Transform spawn = null;
            TeamBase teamBase = TeamBase.Of(_inkling.Team);
            if (teamBase != null)
            {
                spawn = teamBase.GetSpawn(SpawnSlot);
            }
            PlaceAt(spawn != null ? spawn.position : _fallbackSpawnPosition,
                spawn != null ? spawn.rotation : _fallbackSpawnRotation);
            _health.ResetForRespawn();
            _health.GrantArmor(_spawnArmor, _spawnArmorSeconds);
            if (_tank != null)
            {
                _tank.RefillFull();
            }
            IsDead = false;
            Killer = null;
            SetAlive(true);
            Respawned?.Invoke();
        }

        // 瞬移到出生點並面向出生方向(實作統一在 Inkling.Teleport:清速度/爬牆/烏賊狀態、鏡頭與 bot 視線對齊)。
        // 陣亡中呼叫時 CharacterController 維持關閉,由 SetAlive(true) 再開。
        public void PlaceAt(Vector3 position, Quaternion rotation)
        {
            _inkling.Teleport(position, rotation.eulerAngles.y);
            if (_visualRoot != null)
            {
                _visualRoot.rotation = Quaternion.Euler(0f, rotation.eulerAngles.y, 0f);
            }
        }

        // 比賽開始 / 除錯:立即復活並放到出生點(不走倒數)
        public void ForceRespawnNow()
        {
            if (!IsDead)
            {
                IsDead = true;
            }
            Respawn();
        }

        private void SetAlive(bool alive)
        {
            if (_controller != null)
            {
                _controller.enabled = alive;
            }
            if (_visualRoot != null)
            {
                _visualRoot.gameObject.SetActive(alive);
            }
            for (int i = 0; i < _controlBehaviours.Length; i++)
            {
                if (_controlBehaviours[i] != null)
                {
                    _controlBehaviours[i].enabled = alive;
                }
            }
            var main = GetComponent<MainWeapon>();
            if (main != null)
            {
                main.enabled = alive;
            }
            var special = GetComponent<SpecialWeapon>();
            if (special != null)
            {
                special.enabled = alive;
            }
        }
    }
}

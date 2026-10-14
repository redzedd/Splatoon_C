using System.Collections.Generic;
using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // Crab Tank 的車體(research/04 §2.12、JP wiki カニタンク):駕駛身上跟著走的裝甲(DeployableObject,
    // TargetClass.CrabTank,耐久 500、受擊倍率走 DamageRates)。碰撞體在 Target 圖層(與駕駛的 CharacterController 互相忽略):
    //   步行:前方裝甲箱(寬 2.2、地面到 1.44 m、駕駛胸口前 0.12 m 起往前 1 m)+ 後方低箱(到 0.84 m)——
    //     正面的子彈先打到裝甲,駕駛的頭(1.44 m 以上)、背後、側面露出(S3「上方、背後、側面打得到駕駛」);
    //   滾球:半徑 1.15 的球包住駕駛,全身受保護(子彈停在球上、爆炸視線被球擋)。
    //   敵方炸彈碰到車體立刻爆炸(BlocksAttacksFrom 敵方 → BombRules.Detonates;S3「ボム即爆」)。
    //   耐久低於一半:部分外裝甲掉落(外觀);歸零 → 通知大招(駕駛彈出)。時間快到時裝甲閃紅。
    // 外觀:步行 = 蟹殼 + 左右兩門機槍 + 中間砲管 + 六隻腳(走路時擺動);滾球 = 帶甲片的球(依位移滾動)。
    public sealed class CrabTankBody : DeployableObject
    {
        [SerializeField, Tooltip("步行模式的碰撞體(前方裝甲、後方低箱)")]
        private Collider[] _walkColliders;

        [SerializeField, Tooltip("滾球模式的碰撞體(球)")]
        private Collider _ballCollider;

        [SerializeField, Tooltip("步行外觀根")]
        private Transform _walkVisual;

        [SerializeField, Tooltip("滾球外觀根(滾動的球)")]
        private Transform _ballVisual;

        [SerializeField, Tooltip("六隻腳(走路擺動)")]
        private Transform[] _legs;

        [SerializeField, Tooltip("耐久低於一半時掉落的外裝甲")]
        private GameObject[] _looseArmor;

        [SerializeField, Tooltip("左 / 右機槍口、中間砲口(發射特效位置)")]
        private Transform[] _muzzles;

        private CrabTankConfig _config;
        private CrabTankSpecial _special;
        private CharacterController _ownerController;
        private Renderer[] _inkRenderers;
        private Renderer[] _neutralRenderers;
        private Collider[] _allColliders;
        private bool _ball;
        private float _summonedAt;
        private bool _armored;
        private float _yaw;
        private Vector3 _lastPosition;
        private float _gait;
        private Quaternion _ballRotation = Quaternion.identity;
        private bool _looseShown = true;
        private int _tintState = -1;
        private bool _flashState;

        public override float MaxHp => _config != null ? _config.ArmorHp : CrabTankModel.ArmorHp;
        public override TargetClass TargetClass => TargetClass.CrabTank;
        protected override string BrokenSound => _config != null ? _config.BreakSound : "device_destroyed";

        // 敵方的爆炸視線被車體擋住(駕駛躲在裝甲後面)、敵方炸彈碰到就爆
        public override bool BlocksAttacksFrom(Team attackerTeam)
        {
            return _armored && attackerTeam != Team;
        }

        // ── 狀態(測試)──
        public bool Armored => _armored;
        public bool BallMode => _ball;
        public float DamageTaken { get; private set; }
        public bool LooseArmorShown => _looseShown;
        public Collider BallCollider => _ballCollider;

        public static int SummonCount { get; private set; }
        public static int BreakCount { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetCounts()
        {
            SummonCount = 0;
            BreakCount = 0;
        }

        protected override void Awake()
        {
            base.Awake();
            var ink = new List<Renderer>();
            var neutral = new List<Renderer>();
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                if (r.name.StartsWith("Ink", System.StringComparison.Ordinal))
                {
                    ink.Add(r);
                }
                else
                {
                    neutral.Add(r);
                }
            }
            _inkRenderers = ink.ToArray();
            _neutralRenderers = neutral.ToArray();
            _allColliders = GetComponentsInChildren<Collider>(true);
        }

        public override void Launch(Vector3 position, Vector3 velocity, SubWeaponConfig config, Inkling owner)
        {
            Debug.LogError("CrabTankBody:請用 Summon(由 CrabTankSpecial 召喚)", this);
            Expire();
        }

        // 召喚:跟著 owner(裝甲 45F 後才生效);yaw = 車身朝向
        public void Summon(Inkling owner, CrabTankConfig config, CrabTankSpecial special, float yaw)
        {
            _config = config;
            _special = special;
            Owner = owner;
            _ball = false;
            _armored = false;
            _yaw = yaw;
            _summonedAt = Time.time;
            DamageTaken = 0f;
            _ballRotation = Quaternion.identity;
            _ownerController = owner != null ? owner.GetComponent<CharacterController>() : null;
            IgnoreOwner(true);
            SetColliders(false, false);
            transform.SetPositionAndRotation(owner != null ? owner.transform.position : transform.position,
                Quaternion.Euler(0f, yaw, 0f));
            _lastPosition = transform.position;
            SetLoose(true);
            ShowMode(false);
            Team team = owner != null ? owner.Team : Team.Alpha;
            TeamTint.ApplyAll(this, TeamPalette.ColorOf(team));
            ResetNeutral();
            _tintState = -1;
            SummonCount++;
            if (InkSplashFxPool.Instance != null)
            {
                InkSplashFxPool.Instance.Spawn(transform.position + Vector3.up * 0.3f, Vector3.up, TeamPalette.ColorOf(team));
            }
        }

        // 45F:裝甲生效(滿血、登記成可受傷目標)
        public void Arm()
        {
            if (_armored)
            {
                return;
            }
            Deploy(Owner, Owner != null ? Owner.Team : Team.Alpha);
            ResetNeutral();
            _armored = true;
            SetColliders(!_ball, _ball);
            Physics.SyncTransforms();
        }

        public void SetBall(bool ball)
        {
            if (_ball == ball)
            {
                return;
            }
            _ball = ball;
            ShowMode(ball);
            if (_armored)
            {
                SetColliders(!ball, ball);
                Physics.SyncTransforms();
            }
        }

        // 每幀由大招更新位置與朝向(LateUpdate 前);warn = 快結束閃紅
        public void Follow(Vector3 position, float yaw, bool warn)
        {
            Vector3 delta = position - _lastPosition;
            _lastPosition = position;
            _yaw = yaw;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            if (_ball)
            {
                // 滾動:繞「移動方向 × 上」轉,角度 = 距離 / 半徑
                Vector3 flat = new Vector3(delta.x, 0f, delta.z);
                float distance = flat.magnitude;
                if (distance > 1e-4f && _ballVisual != null)
                {
                    Vector3 axis = Vector3.Cross(Vector3.up, flat / distance);
                    _ballRotation = Quaternion.AngleAxis(distance / 1.1f * Mathf.Rad2Deg, axis) * _ballRotation;
                    _ballVisual.rotation = _ballRotation;
                }
            }
            else if (_legs != null)
            {
                // 腳:依水平位移擺動(兩組交錯)
                float step = new Vector2(delta.x, delta.z).magnitude;
                _gait += step * 6f;
                for (int i = 0; i < _legs.Length; i++)
                {
                    if (_legs[i] == null)
                    {
                        continue;
                    }
                    float swing = Mathf.Sin(_gait + (i % 2 == 0 ? 0f : Mathf.PI)) * 18f * Mathf.Clamp01(step * 20f);
                    _legs[i].localRotation = Quaternion.Euler(swing, 0f, 0f);
                }
            }
            // 召喚:45F 內從地下長出來(縮放 0.2 → 1)
            float summon = Mathf.Clamp01((Time.time - _summonedAt) / Mathf.Max(0.01f, _config.ArmorDelaySeconds));
            float scale = _armored ? 1f : Mathf.Lerp(0.2f, 1f, summon * summon * (3f - 2f * summon));
            if (Mathf.Abs(transform.localScale.x - scale) > 1e-3f)
            {
                transform.localScale = Vector3.one * scale;
            }
            ApplyWarn(warn);
        }

        public Transform Muzzle(int index)
        {
            return _muzzles != null && index >= 0 && index < _muzzles.Length ? _muzzles[index] : transform;
        }

        // 範圍傷害量距離:啟用中的碰撞體取最近
        public override Vector3 ClosestPoint(Vector3 from)
        {
            Vector3 best = transform.position;
            float bestSq = float.MaxValue;
            for (int i = 0; i < _allColliders.Length; i++)
            {
                Collider c = _allColliders[i];
                if (c == null || !c.enabled)
                {
                    continue;
                }
                Vector3 point = c.ClosestPoint(from);
                float sq = (point - from).sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = point;
                }
            }
            return best;
        }

        protected override void OnDamaged(float dealt, in DamageInfo info)
        {
            DamageTaken += dealt;
            if (Normalized < 0.5f && _looseShown)
            {
                // S3:耐久低於一半,部分外裝甲掉落
                SetLoose(false);
                if (InkSplashFxPool.Instance != null)
                {
                    InkSplashFxPool.Instance.Spawn(transform.position + Vector3.up * 1f, Vector3.up, TeamPalette.ColorOf(Team));
                }
            }
        }

        protected override void OnBroken(in DamageInfo info)
        {
            BreakCount++;
            _armored = false;
            SetColliders(false, false);
            if (_special != null)
            {
                _special.OnTankBroken();
            }
        }

        // 收起(時間到 / 被打爆 / 駕駛倒下)
        public void Dismiss()
        {
            IgnoreOwner(false);
            _armored = false;
            _special = null;
            if (IsDeployed || gameObject.activeSelf)
            {
                Expire();
            }
        }

        protected override void OnDisable()
        {
            IgnoreOwner(false);
            _armored = false;
            base.OnDisable();
        }

        // ── 外觀 / 碰撞 ──

        private void ShowMode(bool ball)
        {
            if (_walkVisual != null && _walkVisual.gameObject.activeSelf == ball)
            {
                _walkVisual.gameObject.SetActive(!ball);
            }
            if (_ballVisual != null && _ballVisual.gameObject.activeSelf != ball)
            {
                _ballVisual.gameObject.SetActive(ball);
            }
        }

        private void SetColliders(bool walk, bool ball)
        {
            if (_walkColliders != null)
            {
                for (int i = 0; i < _walkColliders.Length; i++)
                {
                    if (_walkColliders[i] != null)
                    {
                        _walkColliders[i].enabled = walk;
                    }
                }
            }
            if (_ballCollider != null)
            {
                _ballCollider.enabled = ball;
            }
        }

        private void SetLoose(bool shown)
        {
            _looseShown = shown;
            if (_looseArmor == null)
            {
                return;
            }
            for (int i = 0; i < _looseArmor.Length; i++)
            {
                if (_looseArmor[i] != null && _looseArmor[i].activeSelf != shown)
                {
                    _looseArmor[i].SetActive(shown);
                }
            }
        }

        private void IgnoreOwner(bool ignore)
        {
            if (_ownerController == null)
            {
                return;
            }
            for (int i = 0; i < _allColliders.Length; i++)
            {
                if (_allColliders[i] != null)
                {
                    Physics.IgnoreCollision(_ownerController, _allColliders[i], ignore);
                }
            }
            if (!ignore)
            {
                _ownerController = null;
            }
        }

        // 快結束閃紅(隊伍色 ↔ 紅,每 0.2 秒切換;只在狀態改變時寫 MaterialPropertyBlock)
        private void ApplyWarn(bool warn)
        {
            int state = warn && Mathf.Repeat(Time.time * 5f, 1f) < 0.5f ? 1 : 0;
            if (state == _tintState || IsFlashing)
            {
                return;
            }
            _tintState = state;
            Color team = TeamPalette.ColorOf(Owner != null ? Owner.Team : Team);
            Color color = state == 1 ? new Color(1f, 0.12f, 0.1f) : team;
            for (int i = 0; i < _inkRenderers.Length; i++)
            {
                if (_inkRenderers[i] != null)
                {
                    TeamTint.Apply(_inkRenderers[i], color);
                }
            }
        }

        private void ResetNeutral()
        {
            for (int i = 0; i < _neutralRenderers.Length; i++)
            {
                if (_neutralRenderers[i] != null)
                {
                    _neutralRenderers[i].SetPropertyBlock(null);
                }
            }
            _tintState = -1;
        }

        // 基底只在受擊閃白切換時重上整個物件的顏色 → 跟著把原色部位改回來
        protected override void LateUpdate()
        {
            base.LateUpdate();
            if (IsDeployed && IsFlashing != _flashState)
            {
                _flashState = IsFlashing;
                ResetNeutral();
            }
        }
    }
}

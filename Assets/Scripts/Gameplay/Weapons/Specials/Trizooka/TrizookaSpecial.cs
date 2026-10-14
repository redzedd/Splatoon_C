using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // Trizooka 的執行期行為(S3 SpUltraShot,research/04 §2.1、JP wiki ウルトラショット):
    //   發動 → 扛起發射器(主/副武器不能用、可以潛游)→ 23F 後才能開始射;按住攻擊 → 15F 前隙出膛 → 40F 後隙,
    //   按住就每 55F 連射;前隙內的按鍵會記住(IsReqShotInStartDelay)。射完 3 發或 330F 到即結束(持續型:能量 = 計時器)。
    //   每發 3 顆墨團每 4F 一顆(InkBullet + 螺旋),共用一個 attackId:直擊 220 貫穿角色、對同一目標同一發只算一次。
    //   瞄準:沿準心射線 29 m 處歸零(真實彈道解仰角),所以中距離彈道比準心高(S3 特性)。
    //   走速:人形 0.07 u/F、射擊前後 0.04 u/F(不吃跑速能力)、游速 ×0.948——以 InklingModifiers 套上,結束/死亡移除。
    //   外觀:手上換成發射器模型(SpecialHeldModel)、上身照開火姿勢瞄準(CharacterVisual.AimOverride)、每發後座力;
    //   扛起音效對敵人較大聲(S3 v2.0.0)。
    public sealed class TrizookaSpecial : SpecialWeapon
    {
        // S3 中量級人形跑速 0.096 u/F(換算發射器走速的倍率用)
        private const float BaseRunSpeed = 5.76f;

        private TrizookaConfig _config;
        private InkBulletSpec _globSpec;
        private readonly SpecialHeldModel _model = new SpecialHeldModel();
        private CharacterVisual _visual;
        private WeaponLoadout _loadout;

        private float _activatedAt = float.NegativeInfinity;
        private float _readyAt;
        private float _shotAt = -1f;
        private float _slowUntil;
        private bool _requested;
        private float _requestedAt;
        private bool _wasHeld;
        private float _heldSince;
        private int _shotsFired;
        private int _moveState = -1;
        private float _hideModelAt = -1f;

        // 發射中的一發(3 顆墨團依序出膛;大招結束後剩下的照樣射完)
        private int _volleyRemaining;
        private int _volleyIndex;
        private float _nextGlobAt;
        private Vector3 _volleyOrigin;
        private Vector3 _volleyDirection;
        private int _volleyAttackId;

        // bot
        private float _botStrafeSign = 1f;
        private float _botStrafeFlipAt;

        public override SpecialWeaponConfig BaseConfig => _config;
        public TrizookaConfig Config => _config;

        public override bool BlocksMainWeapon => true;
        public override bool BlocksSubWeapon => true;
        public override bool BlocksSquid => false;

        // HUD:剩幾發(快取字串,每幀讀不配置)
        private static readonly string[] ShotHints = { "剩 0 發", "剩 1 發", "剩 2 發", "剩 3 發", "剩 4 發", "剩 5 發" };
        public override string HudHint => ShotHints[Mathf.Clamp(ShotsRemaining, 0, ShotHints.Length - 1)];

        // ── 狀態(HUD、測試)──
        public int ShotsFired => _shotsFired;
        public int ShotsRemaining => _config != null ? Mathf.Max(0, _config.Shots - _shotsFired) : 0;
        public int GlobsFired { get; private set; }
        public float ActivatedAt => _activatedAt;
        public float LastShotTime { get; private set; } = float.NegativeInfinity;
        public bool InShotMotion => IsActive && (_shotAt >= 0f || Time.time < _slowUntil);
        public InkBullet LastGlob { get; private set; }
        public Vector3 LastMuzzle => _volleyOrigin;
        public Vector3 LastDirection => _volleyDirection;
        public bool ModelShown => _model.Shown;

        public override void Configure(SpecialWeaponConfig config)
        {
            _config = config as TrizookaConfig;
            if (_config == null)
            {
                Debug.LogError($"TrizookaSpecial:設定型別不符({config})", this);
                return;
            }
            if (_config.GlobPrefab == null || _config.LauncherPrefab == null)
            {
                Debug.LogError("TrizookaSpecial:設定缺少墨團/發射器 prefab(先跑 Tools/SplatoonC/M7 Setup)", this);
            }
            string explode = GameAudio.Has(_config.ExplodeSound) ? _config.ExplodeSound : "bomb_explode";
            _globSpec = _config.BuildGlobSpec(explode);
            if (_config.GlobPrefab != null)
            {
                SharedPool.Prewarm(_config.GlobPrefab, _config.GlobsPerShot * 2);
            }
        }

        protected override void Awake()
        {
            base.Awake();
            _visual = GetComponent<CharacterVisual>();
            _loadout = GetComponent<WeaponLoadout>();
        }

        public override void OnActivated()
        {
            if (_config == null)
            {
                EndSpecial();
                return;
            }
            float now = Time.time;
            _activatedAt = now;
            _readyAt = now + _config.StartDelayFrames / 60f;
            _shotAt = -1f;
            _slowUntil = 0f;
            _requested = false;
            _wasHeld = false;
            _heldSince = now;
            _shotsFired = 0;
            _hideModelAt = -1f;
            _moveState = -1;
            _model.Show(Owner, _config.LauncherPrefab, Vector3.zero, Vector3.zero);
            if (_visual != null)
            {
                _visual.AimOverride = true;
            }
            ApplyMoveSpeed(false);
            // S3 v2.0.0:扛起的聲音對敵人較大聲、對自己/隊友較小
            Inkling local = Inkling.Local;
            bool enemyHears = local == null || Owner == null || local.Team != Owner.Team;
            GameAudio.Play(_config.ActivateSound, transform.position + Vector3.up, enemyHears ? 1f : 0.5f, 0f);
        }

        public override void OnEnded()
        {
            RemoveMoveSpeed();
            if (_visual != null)
            {
                _visual.AimOverride = false;
            }
            _shotAt = -1f;
            _requested = false;
            // 發射器在最後一發的後座力後才收起(死亡/停用時立刻收)
            if (isActiveAndEnabled)
            {
                _hideModelAt = Time.time + 0.3f;
            }
            else
            {
                _model.Hide();
            }
        }

        private void OnDisable()
        {
            RemoveMoveSpeed();
            if (_visual != null)
            {
                _visual.AimOverride = false;
            }
            _model.Hide();
            _volleyRemaining = 0;
        }

        private void OnDestroy()
        {
            _model.Destroy();
        }

        private void Update()
        {
            float now = Time.time;
            // 已出膛的一發:剩下的墨團每 4F 一顆(大招結束後照樣射完)
            while (_volleyRemaining > 0 && now >= _nextGlobAt)
            {
                FireGlob();
            }
            if (_hideModelAt > 0f && now >= _hideModelAt && !IsActive)
            {
                _hideModelAt = -1f;
                _model.Hide();
            }
            if (!IsActive || _config == null || Input == null)
            {
                return;
            }

            // 發動前隙/後隙中「按下」會記住(IsReqShotInStartDelay、S3 v5.0.0);「按住」= 每次後隙結束就接著射(間隔剛好 55F);
            // 按住出膛後就放開不會多射一發
            bool held = Input.AttackHeld;
            if (held && !_wasHeld)
            {
                _requested = true;
                _requestedAt = now;
                _heldSince = now;
            }
            _wasHeld = held;
            if (_shotAt < 0f)
            {
                if ((_requested || held) && now >= _readyAt && _shotsFired < _config.Shots)
                {
                    float since = _requested ? _requestedAt : _heldSince;
                    _requested = false;
                    // 前隙從「可以射的那一刻」與「按下的那一刻」較晚者起算(不受幀率量化影響)
                    _shotAt = Mathf.Max(_readyAt, since) + _config.ShotDelayFrames / 60f;
                }
            }
            else if (now >= _shotAt)
            {
                float firedAt = _shotAt;
                _shotAt = -1f;
                BeginVolley(firedAt);
                _shotsFired++;
                _readyAt = firedAt + _config.PostShotFrames / 60f;
                _slowUntil = _readyAt;
                if (_shotsFired >= _config.Shots)
                {
                    // 第 3 發出膛即結束(能量歸 0);剩下的兩顆墨團由上面的迴圈射完
                    EndSpecial();
                    return;
                }
            }
            ApplyMoveSpeed(_shotAt >= 0f || now < _slowUntil);
        }

        // ── 發射 ──

        private void BeginVolley(float firedAt)
        {
            ComputeAim(out _volleyOrigin, out _volleyDirection);
            _volleyAttackId = CombatLedger.NewAttackId();
            _volleyRemaining = Mathf.Max(1, _config.GlobsPerShot);
            _volleyIndex = 0;
            _nextGlobAt = firedAt;
            LastShotTime = firedAt;
            if (_visual != null)
            {
                _visual.NotifyExternalShot(1.8f);
            }
            GameAudio.PlayFrom(Owner != null && Owner.IsLocalPlayer, _config.FireSound, _volleyOrigin, 1f, 0.03f);
            if (InkSplashFxPool.Instance != null)
            {
                InkSplashFxPool.Instance.Spawn(_volleyOrigin, _volleyDirection, TeamPalette.ColorOf(TeamOf()));
            }
            while (_volleyRemaining > 0 && Time.time >= _nextGlobAt)
            {
                FireGlob();
            }
        }

        private void FireGlob()
        {
            int index = _volleyIndex;
            _volleyIndex++;
            _volleyRemaining--;
            _nextGlobAt += _config.GlobIntervalFrames / 60f;
            InkBullet glob = InkBullet.Fire(_globSpec, _volleyOrigin, _volleyDirection, TeamOf(), Owner,
                _volleyAttackId, Vector3.zero, _config.GlobPrefab);
            if (glob != null)
            {
                glob.OrbitPhaseDeg = TrizookaModel.OrbitAngleDeg(index, 0);
                GlobsFired++;
                LastGlob = glob;
            }
        }

        private Team TeamOf()
        {
            return Owner != null ? Owner.Team : Team.Alpha;
        }

        // 管口在右肩;方向 = 從管口指向「準心射線上 29 m 處」的歸零仰角(真實彈道求解)
        public void ComputeAim(out Vector3 origin, out Vector3 direction)
        {
            IAimView view = Owner != null ? Owner.AimView : null;
            float yaw = view != null ? view.Yaw : transform.eulerAngles.y;
            Quaternion facing = Quaternion.Euler(0f, yaw, 0f);
            Vector3 offset = _config.MuzzleOffset;
            origin = transform.position + facing * offset;
            Vector3 aimPoint = view != null
                ? view.AimOrigin + view.AimForward * _config.ZeroDistance
                : origin + facing * Vector3.forward * _config.ZeroDistance;
            Vector3 to = aimPoint - origin;
            Vector3 flat = new Vector3(to.x, 0f, to.z);
            float horizontal = flat.magnitude;
            if (horizontal < 0.01f)
            {
                direction = to.sqrMagnitude > 1e-6f ? to.normalized : facing * Vector3.forward;
                return;
            }
            float elevation = TrizookaModel.SolveElevation(TrizookaModel.Motion, horizontal, to.y);
            flat /= horizontal;
            direction = flat * Mathf.Cos(elevation) + Vector3.up * Mathf.Sin(elevation);
        }

        // ── 走速(InklingModifiers;這個元件本身當來源鍵)──

        private void ApplyMoveSpeed(bool inShot)
        {
            int state = inShot ? 1 : 0;
            if (state == _moveState || Owner == null)
            {
                return;
            }
            _moveState = state;
            float target = (inShot ? _config.MoveSpeedInShot : _config.MoveSpeed) * 60f;
            WeightClass weight = _loadout != null && _loadout.Kit != null ? _loadout.Kit.Weight : WeightClass.Mid;
            float baseSpeed = BaseRunSpeed * WeightClassUtil.RunMultiplier(weight);
            ModifierSpec spec = ModifierSpec.None
                .WithRunSpeed(target / Mathf.Max(0.01f, baseSpeed))
                .WithSwimSpeed(_config.SwimSpeedRatio);
            Owner.Modifiers.Add(this, spec, 0f);
        }

        private void RemoveMoveSpeed()
        {
            _moveState = -1;
            if (Owner != null)
            {
                Owner.Modifiers.Remove(this);
            }
        }

        // ── bot ──

        // 看得到敵人且在直擊射程(29 m)附近才開;沒有目標時偶爾開來打敵方前線
        public override bool BotWantsActivate(IBotControl bot)
        {
            if (bot.HasTarget && bot.TargetVisible && bot.TargetDistance <= 30f)
            {
                return true;
            }
            return !bot.HasTarget && Random.value < 0.08f;
        }

        // 瞄準目標(預判移動;中距離彈道偏高 → 瞄腳邊),對準才按;邊打邊慢慢橫移
        public override void BotDrive(IBotControl bot)
        {
            Vector3 aim;
            bool haveTarget = bot.HasTarget;
            if (haveTarget)
            {
                float distance = bot.TargetDistance;
                float lead = distance / 67.5f;
                float height = distance > 11f && distance < 24f ? 0.1f : 0.8f;
                aim = bot.TargetPosition + bot.TargetVelocity * lead + Vector3.up * height;
            }
            else
            {
                aim = bot.Position + bot.EnemyBaseDirection * 24f;
            }
            bot.LookAt(aim);
            float tolerance = haveTarget && bot.TargetVisible ? 4f : 7f;
            bot.HoldAttack(bot.AimError < tolerance && Time.time - _activatedAt > 0.3f);
            if (Time.time >= _botStrafeFlipAt)
            {
                _botStrafeFlipAt = Time.time + Random.Range(0.8f, 1.6f);
                _botStrafeSign = Random.value < 0.5f ? -1f : 1f;
            }
            Vector3 toAim = aim - bot.Position;
            toAim.y = 0f;
            Vector3 right = toAim.sqrMagnitude > 0.01f ? Vector3.Cross(Vector3.up, toAim.normalized) : Vector3.right;
            bot.Move(right * _botStrafeSign, 0.6f);
        }
    }
}

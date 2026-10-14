using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // Big Bubbler 的執行期行為(research/04 §2.2、JP wiki グレートバリア):
    //   按下時先檢查能不能設置——空中、懸崖邊(腳底四周有一點踩空)、低天花板下 → 不發動、不耗能量,畫面顯示「Can't Use!」。
    //   發動 → 腳下放裝置(BigBubblerDevice),能量立刻進入鎖定倒數 1001F(設置動作開始就算,死亡不中斷,屏障破掉即歸 0);
    //   設置前隙 50F 內不能行動(主/副武器、烏賊、移動),之後恢復正常,屏障自己撐到耐久歸零。一人同時只有一個。
    public sealed class BigBubblerSpecial : SpecialWeapon
    {
        private const float CeilingProbe = 12f;
        private static readonly Vector3[] EdgeDirections =
        {
            Vector3.forward, Vector3.back, Vector3.left, Vector3.right,
        };

        private BigBubblerConfig _config;
        private CharacterController _controller;
        private BigBubblerDevice _device;
        private float _deployEndsAt;

        public override SpecialWeaponConfig BaseConfig => _config;
        public BigBubblerConfig Config => _config;

        // 設置動作期間什麼都不能做
        public override bool BlocksMainWeapon => true;
        public override bool BlocksSubWeapon => true;
        public override bool BlocksSquid => true;

        public BigBubblerDevice Device => _device != null && _device.IsDeployed ? _device : null;
        public string LastRefusal { get; private set; }

        public override void Configure(SpecialWeaponConfig config)
        {
            _config = config as BigBubblerConfig;
            if (_config == null)
            {
                Debug.LogError($"BigBubblerSpecial:設定型別不符({config})", this);
                return;
            }
            if (_config.DevicePrefab == null)
            {
                Debug.LogError("BigBubblerSpecial:設定缺少裝置 prefab(先跑 Tools/SplatoonC/M7 Setup)", this);
                return;
            }
            SharedPool.Prewarm(_config.DevicePrefab, 1);
        }

        protected override void Awake()
        {
            base.Awake();
            _controller = GetComponent<CharacterController>();
        }

        // S3「Can't Use!」:空中、懸崖邊、低天花板
        public override bool CanActivateNow
        {
            get
            {
                LastRefusal = Refusal();
                return LastRefusal == null;
            }
        }

        private string Refusal()
        {
            if (_config == null || _config.DevicePrefab == null)
            {
                return "沒有設定";
            }
            if (_controller == null || !_controller.enabled || !_controller.isGrounded)
            {
                return "空中";
            }
            Vector3 feet = transform.position;
            for (int i = 0; i < EdgeDirections.Length; i++)
            {
                Vector3 probe = feet + EdgeDirections[i] * _config.EdgeProbeRadius + Vector3.up * 0.3f;
                if (!Physics.Raycast(probe, Vector3.down, 0.8f, CombatMasks.Terrain, QueryTriggerInteraction.Ignore))
                {
                    return "懸崖邊";
                }
            }
            if (CeilingAbove(feet) < _config.MinCeiling)
            {
                return "天花板太低";
            }
            return null;
        }

        // 腳底到天花板的距離(沒有天花板回傳大值)
        private static float CeilingAbove(Vector3 feet)
        {
            Vector3 origin = feet + Vector3.up * 0.5f;
            return Physics.Raycast(origin, Vector3.up, out RaycastHit hit, CeilingProbe, CombatMasks.Terrain,
                QueryTriggerInteraction.Ignore)
                ? hit.distance + 0.5f
                : CeilingProbe + 0.5f;
        }

        public override void OnActivationRefused()
        {
            if (Owner != null && Owner.IsLocalPlayer)
            {
                SpecialPromptHud.Show("Can't Use!");
            }
        }

        public override void OnActivated()
        {
            if (_config == null || _config.DevicePrefab == null)
            {
                EndSpecial();
                return;
            }
            // 一人同時只有一個(鎖定期間本來就不能再發動;保險起見舊的收掉)
            if (_device != null && _device.IsDeployed && _device.Owner == Owner)
            {
                _device.Expire();
            }
            float yaw = Owner != null && Owner.AimView != null ? Owner.AimView.Yaw : transform.eulerAngles.y;
            Vector3 ground = PlacementPoint(yaw);
            // 鎖定從設置動作開始算(research/04 §1.4 表),行為繼續到前隙結束
            int token = LockGauge(_config.LockSeconds, true);
            _device = SharedPool.Spawn(_config.DevicePrefab);
            _device.Place(ground, yaw, _config, Owner, Gauge, token, CeilingAbove(ground));
            _deployEndsAt = Time.time + _config.FrontLagFrames / 60f;
            if (Owner != null)
            {
                // 設置動作中站定不動(不能跳)
                Owner.Modifiers.Add(this, ModifierSpec.None.WithRunSpeed(0f).WithSwimSpeed(0f).WithJumpHeight(0f), 0f);
            }
        }

        // 發射座放在腳前 1.1 m 的地面(發射座與無人機是實心的弱點碰撞體,放在腳底會把人擠開);前面是牆/懸空就放腳底
        private Vector3 PlacementPoint(float yaw)
        {
            Vector3 feet = transform.position;
            Vector3 forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            Vector3 ahead = feet + forward * 1.1f;
            if (!Physics.Linecast(feet + Vector3.up * 0.5f, ahead + Vector3.up * 0.5f, CombatMasks.Terrain,
                    QueryTriggerInteraction.Ignore)
                && Physics.Raycast(ahead + Vector3.up * 0.4f, Vector3.down, out RaycastHit front, 0.9f, CombatMasks.Terrain,
                    QueryTriggerInteraction.Ignore))
            {
                return front.point;
            }
            return Physics.Raycast(feet + Vector3.up * 0.3f, Vector3.down, out RaycastHit hit, 1.5f, CombatMasks.Terrain,
                QueryTriggerInteraction.Ignore)
                ? hit.point
                : feet;
        }

        public override void OnEnded()
        {
            if (Owner != null)
            {
                Owner.Modifiers.Remove(this);
            }
        }

        private void OnDisable()
        {
            if (Owner != null)
            {
                Owner.Modifiers.Remove(this);
            }
        }

        private void Update()
        {
            if (IsActive && Time.time >= _deployEndsAt)
            {
                EndSpecial();
            }
        }

        // ── bot:交戰中(看得到敵人、距離不遠)且站在地上才開;設置動作期間站著不動 ──

        public override bool BotWantsActivate(IBotControl bot)
        {
            if (!bot.HasTarget || !bot.TargetVisible || bot.TargetDistance > 20f)
            {
                return false;
            }
            return CanActivateNow;
        }

        public override void BotDrive(IBotControl bot)
        {
            if (bot.HasTarget)
            {
                bot.LookAt(bot.TargetPosition + Vector3.up * 0.9f);
            }
        }
    }
}

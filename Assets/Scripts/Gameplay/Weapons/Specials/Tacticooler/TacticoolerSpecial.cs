using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // Tacticooler 的執行期行為(research/04 §2.15、JP wiki エナジースタンド):
    //   發動 → 手上舉著罐子(不限時、主武器被佔用、不能潛游);按住副武器鍵 = 拋物線預覽(ThrowOverride),放開 →
    //   13F 投擲前隙後罐子離手(原點/初速用放開當下算好的,與預覽一致)。離手的瞬間能量改為鎖定倒數:
    //   S3 從「飲料機展開」起算 600F——這裡預先模擬同一條拋物線求落地時間,鎖定秒數 = 飛行 + 展開 + 600F,
    //   倒數結束時刻與 S3 相同(以時間計、死亡不中斷)。飲料機消失 → 能量立刻歸 0。
    //   一人同時一台:新的一台立起時舊的消失。舉著時被擊倒 = 一般死亡懲罰(還沒丟出不算鎖定)。
    public sealed class TacticoolerSpecial : SpecialWeapon
    {
        private const float PredictRadius = 0.35f;

        private TacticoolerConfig _config;
        private readonly SpecialHeldModel _model = new SpecialHeldModel();
        private TacticoolerDevice _device;
        private bool _throwPending;
        private float _throwAt;
        private Vector3 _throwOrigin;
        private Vector3 _throwVelocity;
        private float _botReleaseAt;

        public override SpecialWeaponConfig BaseConfig => _config;
        public TacticoolerConfig Config => _config;

        public override bool BlocksMainWeapon => true;
        // 舉著時副武器鍵是「丟罐子」(ThrowOverride);放開後的 13F 前隙內不能再丟副武器
        public override bool BlocksSubWeapon => _throwPending;
        public override bool BlocksSquid => true;

        // 舉著時放開副武器鍵 = 丟(前隙中不再接)
        public override ThrowableConfig ThrowOverride => IsActive && !_throwPending && _config != null
                                                         && _config.DevicePrefab != null
            ? _config
            : null;

        public override string HudHint => "右鍵 丟出";

        // ── 狀態(測試、bot)──
        public TacticoolerDevice Device => _device != null && (_device.Flying || _device.IsDeployed) ? _device : null;
        public int ThrowCount { get; private set; }
        public bool ModelShown => _model.Shown;
        public bool ThrowPending => _throwPending;
        public float LastReleaseTime { get; private set; } = float.NegativeInfinity;
        public float LastLaunchTime { get; private set; } = float.NegativeInfinity;
        public float LastPredictedFlight { get; private set; }
        public float LastLockSeconds { get; private set; }

        public override void Configure(SpecialWeaponConfig config)
        {
            _config = config as TacticoolerConfig;
            if (_config == null)
            {
                Debug.LogError($"TacticoolerSpecial:設定型別不符({config})", this);
                return;
            }
            if (_config.DevicePrefab == null || _config.AuraPrefab == null)
            {
                Debug.LogError("TacticoolerSpecial:設定缺少飲料機/發光 prefab(先跑 Tools/SplatoonC/M7 Setup)", this);
                return;
            }
            SharedPool.Prewarm(_config.DevicePrefab, 1);
            SharedPool.Prewarm(_config.AuraPrefab, 4);
        }

        public override void OnActivated()
        {
            _botReleaseAt = 0f;
            _throwPending = false;
            if (_config == null)
            {
                EndSpecial();
                return;
            }
            _model.Show(Owner, _config.HeldPrefab, Vector3.zero, Vector3.zero);
            GameAudio.PlayFrom(Owner != null && Owner.IsLocalPlayer, _config.ActivateSound, transform.position + Vector3.up,
                0.9f, 0f);
        }

        public override void OnEnded()
        {
            _throwPending = false;
            _model.Hide();
        }

        private void OnDisable()
        {
            _throwPending = false;
            _model.Hide();
        }

        private void OnDestroy()
        {
            _model.Destroy();
        }

        // 放開副武器鍵:記下原點/初速,13F 前隙後才真的離手
        public override void OnThrow(Vector3 origin, Vector3 velocity)
        {
            if (!IsActive || _throwPending || _config == null || _config.DevicePrefab == null)
            {
                return;
            }
            _throwPending = true;
            _throwOrigin = origin;
            _throwVelocity = velocity;
            LastReleaseTime = Time.time;
            _throwAt = Time.time + _config.ThrowLagFrames / 60f;
        }

        private void Update()
        {
            if (!_throwPending || !IsActive || Time.time < _throwAt)
            {
                return;
            }
            _throwPending = false;
            Launch(_throwOrigin, _throwVelocity);
        }

        private void Launch(Vector3 origin, Vector3 velocity)
        {
            // 鎖定從飲料機展開起算 600F:先用同一條拋物線預測落地時間
            float flight = PredictFlight(origin, velocity, _config.Gravity);
            float lockSeconds = flight + _config.UnfoldSeconds + _config.LockSeconds;
            LastPredictedFlight = flight;
            LastLockSeconds = lockSeconds;
            int token = LockGauge(lockSeconds, false);
            _device = SharedPool.Spawn(_config.DevicePrefab);
            _device.Throw(origin, velocity, _config, Owner, Gauge, token);
            ThrowCount++;
            LastLaunchTime = Time.time;
            GameAudio.PlayFrom(Owner != null && Owner.IsLocalPlayer,
                GameAudio.Has(_config.ThrowSound) ? _config.ThrowSound : "bomb_throw", origin, 1f, 0.04f);
        }

        // 與 TacticoolerDevice 飛行同一套步進(60 Hz、單一重力、半徑 0.35 的 sphere cast),求著地秒數(沒落地 = 1 秒)
        public static float PredictFlight(Vector3 origin, Vector3 velocity, float gravity)
        {
            Vector3 position = origin;
            Vector3 v = velocity;
            const float dt = 1f / 60f;
            for (int i = 1; i <= 360; i++)
            {
                Vector3 previous = position;
                SimpleBallistics.Step(ref position, ref v, gravity, dt);
                Vector3 delta = position - previous;
                float distance = delta.magnitude;
                if (distance > 1e-5f && Physics.SphereCast(previous, PredictRadius, delta / distance, out RaycastHit hit,
                        distance, CombatMasks.Terrain, QueryTriggerInteraction.Ignore) && hit.normal.y >= 0.6f)
                {
                    return (i - 1 + hit.distance / distance) * dt;
                }
            }
            return 1f;
        }

        // ── bot:身邊有沒喝到的隊友或交戰前開;朝隊友的中心(沒有就前方 5 m)按住副武器 0.35 秒後放開 ──

        public override bool BotWantsActivate(IBotControl bot)
        {
            if (Owner == null)
            {
                return false;
            }
            int thirsty = CountThirstyTeammates(Owner, 14f, out _);
            if (thirsty == 0)
            {
                return false;
            }
            // 身邊有兩個以上沒喝的隊友、或準備交戰就開;不然偶爾自己開(每秒 15%)
            return thirsty >= 2 || (bot.HasTarget && bot.TargetDistance < 25f) || Random.value < 0.15f;
        }

        public override void BotDrive(IBotControl bot)
        {
            Vector3 aim;
            if (Owner != null && CountThirstyTeammates(Owner, 14f, out Vector3 centroid) >= 2)
            {
                aim = Vector3.Lerp(bot.Position, centroid, 0.6f);
            }
            else
            {
                aim = bot.Position + bot.EnemyBaseDirection * 5f;
            }
            if ((aim - bot.Position).sqrMagnitude < 9f)
            {
                aim = bot.Position + bot.EnemyBaseDirection * 3.5f;
            }
            bot.LookAt(aim + Vector3.up * 0.2f);
            if (_botReleaseAt <= 0f)
            {
                _botReleaseAt = Time.time + 0.35f;
            }
            bot.HoldSub(Time.time < _botReleaseAt);
            if (Time.time >= _botReleaseAt + 0.6f && !_throwPending)
            {
                // 沒丟出去(被打斷):重新按一次
                _botReleaseAt = 0f;
            }
        }

        // 附近(含自己)還沒有增益的隊友數與他們的中心
        private static int CountThirstyTeammates(Inkling owner, float radius, out Vector3 centroid)
        {
            centroid = Vector3.zero;
            int count = 0;
            var all = Inkling.All;
            Vector3 here = owner.transform.position;
            for (int i = 0; i < all.Count; i++)
            {
                Inkling mate = all[i];
                if (mate == null || mate.Team != owner.Team || !mate.isActiveAndEnabled)
                {
                    continue;
                }
                var life = mate.GetComponent<InklingLifecycle>();
                if (life != null && life.IsDead)
                {
                    continue;
                }
                if ((mate.transform.position - here).sqrMagnitude > radius * radius
                    || mate.Modifiers.Has(TacticoolerDevice.BuffSource))
                {
                    continue;
                }
                centroid += mate.transform.position;
                count++;
            }
            if (count > 0)
            {
                centroid /= count;
            }
            return count;
        }
    }
}

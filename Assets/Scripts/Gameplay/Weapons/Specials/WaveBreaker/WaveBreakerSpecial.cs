using SplatoonC.Gameplay.Audio;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // Wave Breaker 的執行期行為(research/04 §2.7、JP wiki ホップソナー):
    //   發動 → 手上舉著裝置(時間不限、不能用主武器、不能潛游);按住副武器鍵 = 拋物線預覽,放開往前丟(ThrowOverride,
    //   ThrowController 算初速、不繼承慣性)。丟出的瞬間能量改為鎖定倒數 ~390F(死亡不中斷),玩家立刻恢復行動;
    //   裝置被打壞 → 能量歸 0。舉著時被擊倒 = 一般死亡懲罰(還沒丟出不算鎖定)。
    public sealed class WaveBreakerSpecial : SpecialWeapon
    {
        private WaveBreakerConfig _config;
        private readonly SpecialHeldModel _model = new SpecialHeldModel();
        private WaveBreakerDevice _device;
        private float _botReleaseAt;

        public override SpecialWeaponConfig BaseConfig => _config;
        public WaveBreakerConfig Config => _config;

        public override bool BlocksMainWeapon => true;
        public override bool BlocksSubWeapon => false;
        public override bool BlocksSquid => true;

        // 舉著時放開副武器鍵 = 丟裝置
        public override ThrowableConfig ThrowOverride => IsActive && _config != null && _config.DevicePrefab != null
            ? _config
            : null;

        // HUD:舉著時提示丟出的按鍵(副武器 = 滑鼠右鍵 / 手把 R)
        public override string HudHint => "右鍵 丟出";

        public WaveBreakerDevice Device => _device != null && (_device.Flying || _device.IsDeployed) ? _device : null;
        public int ThrowCount { get; private set; }
        public bool ModelShown => _model.Shown;

        public override void Configure(SpecialWeaponConfig config)
        {
            _config = config as WaveBreakerConfig;
            if (_config == null)
            {
                Debug.LogError($"WaveBreakerSpecial:設定型別不符({config})", this);
                return;
            }
            if (_config.DevicePrefab == null || _config.WavePrefab == null)
            {
                Debug.LogError("WaveBreakerSpecial:設定缺少裝置/衝擊波 prefab(先跑 Tools/SplatoonC/M7 Setup)", this);
                return;
            }
            SharedPool.Prewarm(_config.DevicePrefab, 1);
            SharedPool.Prewarm(_config.WavePrefab, 2);
        }

        public override void OnActivated()
        {
            _botReleaseAt = 0f;
            if (_config != null)
            {
                _model.Show(Owner, _config.HeldPrefab, Vector3.zero, Vector3.zero);
            }
        }

        public override void OnEnded()
        {
            _model.Hide();
        }

        private void OnDisable()
        {
            _model.Hide();
        }

        private void OnDestroy()
        {
            _model.Destroy();
        }

        public override void OnThrow(Vector3 origin, Vector3 velocity)
        {
            if (!IsActive || _config == null || _config.DevicePrefab == null)
            {
                return;
            }
            // 丟出的瞬間開始鎖定倒數(行為同時結束,玩家恢復行動)
            int token = LockGauge(_config.LockSeconds, false);
            _device = SharedPool.Spawn(_config.DevicePrefab);
            _device.Throw(origin, velocity, _config, Owner, Gauge, token);
            ThrowCount++;
            GameAudio.PlayFrom(Owner != null && Owner.IsLocalPlayer,
                GameAudio.Has(_config.ThrowSound) ? _config.ThrowSound : "bomb_throw", origin, 1f, 0.04f);
        }

        // ── bot:敵人在附近(16 m 內)就開;朝敵人腳下(沒有就前方 7 m)按住副武器 0.4 秒後放開 ──

        public override bool BotWantsActivate(IBotControl bot)
        {
            return bot.HasTarget && bot.TargetDistance <= 16f;
        }

        public override void BotDrive(IBotControl bot)
        {
            Vector3 aim = bot.HasTarget
                ? Vector3.Lerp(bot.Position, bot.TargetPosition, 0.8f)
                : bot.Position + bot.EnemyBaseDirection * 7f;
            bot.LookAt(aim + Vector3.up * 0.3f);
            if (_botReleaseAt <= 0f)
            {
                _botReleaseAt = Time.time + 0.4f;
            }
            bot.HoldSub(Time.time < _botReleaseAt);
            if (Time.time >= _botReleaseAt + 0.3f)
            {
                // 沒丟出去(例如被打斷):重新按一次
                _botReleaseAt = 0f;
            }
        }
    }
}

using SplatoonC.Gameplay.Audio;
using UnityEngine;
using UnityEngine.Pool;

namespace SplatoonC.Gameplay.Combat
{
    // 三重龍捲風的執行期行為:啟動期間接管副武器鍵(ThrowOverride),放開就丟一個信標;
    // 丟滿 MaxDevices 個或能量耗盡即結束。信標/龍捲風的池由本行為持有(每個施放者各一組,數量很小)。
    // S3:握著信標時不能用主武器、不能潛入墨中。
    public sealed class TripleInkstrikeSpecial : SpecialWeapon
    {
        private TripleInkstrikeConfig _config;
        private ObjectPool<InkstrikeDevice> _devicePool;
        private ObjectPool<Tornado> _tornadoPool;
        private Transform _poolRoot;
        private int _thrown;

        public override SpecialWeaponConfig BaseConfig => _config;
        public TripleInkstrikeConfig Config => _config;

        public int DevicesThrown => _thrown;
        public int DevicesRemaining => _config != null ? Mathf.Max(0, _config.MaxDevices - _thrown) : 0;
        public bool CanThrowDevice => IsActive && _config != null && _thrown < _config.MaxDevices && _devicePool != null;

        public int ActiveDevices => _devicePool != null ? _devicePool.CountActive : 0;
        public int ActiveTornadoes => _tornadoPool != null ? _tornadoPool.CountActive : 0;

        public override bool BlocksMainWeapon => true;
        public override bool BlocksSubWeapon => false;
        public override bool BlocksSquid => true;
        public override ThrowableConfig ThrowOverride => CanThrowDevice ? _config : null;

        public override void Configure(SpecialWeaponConfig config)
        {
            _config = config as TripleInkstrikeConfig;
            if (_config == null)
            {
                Debug.LogError($"TripleInkstrikeSpecial:設定型別不符({config})", this);
                return;
            }
            if (_config.DevicePrefab == null || _config.TornadoPrefab == null)
            {
                Debug.LogError("TripleInkstrikeSpecial:設定缺少信標/龍捲風 prefab(先跑 Tools/SplatoonC/M7 Setup)", this);
                return;
            }
            if (_poolRoot == null)
            {
                _poolRoot = new GameObject("InkstrikePool_" + name).transform;
            }
            _devicePool = CreatePool(_config.DevicePrefab, _poolRoot, _config.MaxDevices);
            _tornadoPool = CreatePool(_config.TornadoPrefab, _poolRoot, _config.MaxDevices);
        }

        private static ObjectPool<T> CreatePool<T>(T prefab, Transform root, int prewarm) where T : Component
        {
            var pool = new ObjectPool<T>(
                () => Instantiate(prefab, root),
                item => item.gameObject.SetActive(true),
                item => item.gameObject.SetActive(false),
                item => Destroy(item.gameObject),
                collectionCheck: true,
                defaultCapacity: prewarm);
            var warm = new T[prewarm];
            for (int i = 0; i < prewarm; i++)
            {
                warm[i] = pool.Get();
            }
            for (int i = 0; i < prewarm; i++)
            {
                pool.Release(warm[i]);
            }
            return pool;
        }

        private void OnDestroy()
        {
            if (_poolRoot != null)
            {
                Destroy(_poolRoot.gameObject);
            }
        }

        public override void OnActivated()
        {
            _thrown = 0;
        }

        private float _botReleaseAt;

        // bot:朝敵人(沒有就朝敵方前線 12m)按住副武器鍵 0.5 秒後放開,三個丟完為止
        public override void BotDrive(IBotControl bot)
        {
            Vector3 aim = bot.HasTarget
                ? bot.TargetPosition
                : bot.Position + bot.EnemyBaseDirection * 12f;
            bot.LookAt(aim + Vector3.up * 0.3f);
            if (_botReleaseAt <= 0f)
            {
                _botReleaseAt = Time.time + 0.5f;
            }
            bool holding = Time.time < _botReleaseAt;
            bot.HoldSub(holding);
            if (Time.time >= _botReleaseAt + 0.25f)
            {
                _botReleaseAt = 0f;
            }
        }

        public override void OnThrow(Vector3 origin, Vector3 velocity)
        {
            if (!CanThrowDevice)
            {
                return;
            }
            InkstrikeDevice device = _devicePool.Get();
            device.Launch(origin, velocity, _config, _devicePool, _tornadoPool, Owner);
            GameAudio.PlayFrom(Owner != null && Owner.IsLocalPlayer, "inkstrike_launch", origin);
            _thrown++;
            if (_thrown >= _config.MaxDevices)
            {
                EndSpecial();
            }
        }
    }
}

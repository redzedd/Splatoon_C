using System.Collections;
using System.Collections.Generic;
using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Core.Match;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // Big Bubbler(D1)驗收,試射場西半部。全部走真實路徑(按 R 發動、InkBullet / InkProjectile / Explosion / SplatBomb):
    //   (a) 空中、低天花板、懸崖邊按下 → 不發動、不耗能量、畫面「Can't Use!」
    //   (b) 地上發動:腳下裝置、能量鎖定倒數 1001F;前隙 50F 內不能動、不能開火;無人機第 15F 點火、30F 升到頂;50F 起才擋
    //   (c) 耐久每秒 −92.1;屏障半徑 = 2.255 + 5.245 × RadiusRatioCurve(耐久比例)、頂端 = 無人機;設置時腳下塗 4.5
    //   (d) 罩外敵彈停在球面(罩內假人 0 傷、屏障 36 × 0.64 = 23.0);友軍子彈穿過;罩內往外打不擋;舊式 InkProjectile 也擋;
    //       打發射座(弱點)×1 = 36
    //   (e) 罩外爆炸炸不到罩內(屏障吃 180 × 0.64 × Bomb 2.0 = 230.4);罩內爆炸照炸;敵方炸彈碰到球面立刻引爆
    //   (f) 鎖定:倒數中不充能、被擊倒不中斷不減半;屏障被打破 → 能量立刻歸 0、恢復充能
    //   (g) A1 炸彈(飛行的 Suction Bomb 走 BombRules.CastPath、滑行的 Curling Bomb 走 TouchDetonator)碰到敵方球罩也立刻引爆,
    //       爆心在球面外、罩內假人 0 傷
    [AutoTest(Scene = AutoTestAttribute.TrainingScene, Timeout = 150f, Order = 231)]
    public sealed class BigBubblerAutoTest : AutoTestBase
    {
        private readonly List<InkBulletImpact> _impacts = new List<InkBulletImpact>();
        private readonly List<Object> _spawned = new List<Object>();
        private int _errors;
        private string _firstError;

        // 量到的最長一幀(幀數):以幀量化的時間檢查加上這個餘裕,機器忙時不假紅
        private float _maxFrame = 1f;
        private float Slack => _maxFrame;

        private GameObject _playerGo;
        private Inkling _player;
        private ScriptedIntent _intent;
        private PlayerSpecialGauge _gauge;
        private InklingHealth _health;
        private BigBubblerSpecial _special;
        private BigBubblerConfig _config;
        private TargetDummy _template;
        private Inkling _enemy;

        public static void Run()
        {
            Launch<BigBubblerAutoTest>();
        }

        private void OnEnable()
        {
            InkBullet.Impacted += OnImpact;
            Application.logMessageReceived += OnLog;
        }

        private void OnDisable()
        {
            InkBullet.Impacted -= OnImpact;
            Application.logMessageReceived -= OnLog;
        }

        private void Update()
        {
            _maxFrame = Mathf.Max(_maxFrame, Time.deltaTime * 60f);
        }

        private void OnImpact(in InkBulletImpact impact)
        {
            _impacts.Add(impact);
        }

        private void OnLog(string message, string stack, LogType type)
        {
            // 自己的 FAIL 行也是 LogError,不算
            if ((type == LogType.Exception || type == LogType.Error)
                && !message.StartsWith("[AUTOTEST]", System.StringComparison.Ordinal))
            {
                _errors++;
                if (_firstError == null)
                {
                    _firstError = message;
                }
            }
        }

        private IEnumerator Start()
        {
            _playerGo = FindPlayer();
            _player = _playerGo != null ? _playerGo.GetComponent<Inkling>() : null;
            _config = Resources.Load<BigBubblerConfig>("Weapons/Special_big_bubbler");
            GameObject dummyGo = GameObject.Find("Dummy_Near");
            _template = dummyGo != null ? dummyGo.GetComponent<TargetDummy>() : null;
            if (_player == null || _config == null || _config.DevicePrefab == null || _template == null)
            {
                FailSetup("找不到 Player / Dummy_Near / Resources/Weapons/Special_big_bubbler(先跑 M7 Setup)");
                yield break;
            }
            _intent = ScriptedIntent.Drive(_player);
            _gauge = _player.GetComponent<PlayerSpecialGauge>();
            _health = _player.GetComponent<InklingHealth>();
            if (!SpecialTestKit.EquipSpecial(_player, _config))
            {
                FailSetup("換裝 Big Bubbler 失敗");
                yield break;
            }
            _special = _player.GetComponent<BigBubblerSpecial>();
            _enemy = SpecialTestKit.CloneEnemy(_playerGo, new Vector3(-22f, 0.1f, 22f), "BubblerEnemy", out _);
            _spawned.Add(_enemy);
            _health.Invulnerable = true;

            yield return CantUse();
            yield return DeployAndBlock();
            yield return OtherBombsOnShell();

            Check("全程無例外/錯誤 log", _errors == 0, _errors == 0 ? "0" : $"{_errors} 筆,第一筆:{_firstError}");
            foreach (Object obj in _spawned)
            {
                if (obj != null)
                {
                    Destroy(obj is Component c ? c.gameObject : obj);
                }
            }
            _health.Invulnerable = false;
            _player.GetComponent<PlayerInputRouter>().ClearOverrideSource();
            _player.Teleport(new Vector3(0f, 0.1f, 0f), 0f);
            Finish();
        }

        // ── (a) Can't Use! ──
        private IEnumerator CantUse()
        {
            _player.Teleport(new Vector3(-14f, 0.1f, 14f), 90f);
            yield return new WaitForSeconds(0.4f);
            _gauge.SetPointsForTest(100f);
            _intent.PressJump();
            yield return new WaitForSeconds(0.2f);
            int refusedBefore = _gauge.RefusedCount;
            _intent.PressSpecial();
            yield return null;
            yield return null;
            Check("空中按下:不發動、不耗能量、顯示「Can't Use!」",
                !_gauge.IsActive && _gauge.IsFull && _gauge.RefusedCount == refusedBefore + 1
                && SpecialPromptHud.LastMessage == "Can't Use!" && _special.LastRefusal == "空中",
                $"啟動={_gauge.IsActive} 滿={_gauge.IsFull} 拒絕={_gauge.RefusedCount - refusedBefore}({_special.LastRefusal})" +
                $" 提示={SpecialPromptHud.LastMessage}");
            yield return new WaitForSeconds(1.2f);

            // 低天花板:頭上 2.8 m 蓋一塊板
            GameObject roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
            roof.name = "BubblerLowRoof";
            roof.transform.position = new Vector3(-8f, 2.9f, 14f);
            roof.transform.localScale = new Vector3(4f, 0.2f, 4f);
            _spawned.Add(roof);
            _player.Teleport(new Vector3(-8f, 0.1f, 14f), 90f);
            Physics.SyncTransforms();
            yield return new WaitForSeconds(0.4f);
            _intent.PressSpecial();
            yield return null;
            yield return null;
            string ceiling = _special.LastRefusal;
            bool ceilingRefused = !_gauge.IsActive && _gauge.IsFull;

            // 懸崖邊:站在場地邊緣(x = −24.85),腳下四周有一點踩空
            _player.Teleport(new Vector3(-24.85f, 0.1f, 14f), 90f);
            yield return new WaitForSeconds(0.4f);
            _intent.PressSpecial();
            yield return null;
            yield return null;
            string edge = _special.LastRefusal;
            bool edgeRefused = !_gauge.IsActive && _gauge.IsFull;
            Check("低天花板、懸崖邊也不能發動", ceilingRefused && ceiling == "天花板太低" && edgeRefused && edge == "懸崖邊",
                $"天花板:{ceiling}({ceilingRefused})、邊緣:{edge}({edgeRefused})");
            Destroy(roof);
            yield return new WaitForSeconds(0.2f);
        }

        // ── (b)–(f) ──
        private IEnumerator DeployAndBlock()
        {
            Vector3 p = new Vector3(-14f, 0f, -12f);
            _player.Teleport(p + Vector3.up * 0.1f, 90f);
            yield return new WaitForSeconds(0.5f);
            _gauge.SetPointsForTest(100f);
            _intent.PressSpecial();
            yield return WaitUntil(() => _gauge.IsActive, 1f);
            BigBubblerDevice device = _special.Device;
            if (device == null)
            {
                Check("發動後腳下有裝置", false, "沒有裝置");
                yield break;
            }
            float t0 = Time.time;
            Check("發動:腳下設置裝置、能量鎖定倒數 1001F、主武器被佔用",
                _gauge.IsLocked && _gauge.IsActive && Mathf.Abs(_gauge.LockRemainingSeconds - 1001f / 60f) < 0.1f
                && _gauge.ActiveSpecial == _special && _special.BlocksMainWeapon
                && Flat(device.transform.position - _player.transform.position) < 1.3f,
                $"鎖定={_gauge.IsLocked} 剩 {_gauge.LockRemainingSeconds:0.00}s 行為中={_gauge.IsActive}");

            // 前隙 50F:推搖桿(往側邊,發射座在腳前)也不動;第 15F 點火前無人機停在發射座頂
            _intent.Move = new Vector2(1f, 0f);
            Vector3 start = _player.transform.position;
            float droneEarly = -1f;
            float droneTop = -1f;
            float topFrame = 0f;
            bool blockingEarly = false;
            while (_gauge.IsActive && Time.time - t0 < 2f)
            {
                float frames = device.ElapsedFrames;
                if (frames >= 3f && frames < 14.5f)
                {
                    droneEarly = device.DroneHeight;
                }
                if (frames >= 46f && droneTop < 0f)
                {
                    droneTop = device.DroneHeight;
                    topFrame = frames;
                }
                if (frames >= 16f && frames < 49.5f)
                {
                    blockingEarly |= device.Surface.IsAlive;
                }
                yield return null;
            }
            float lagFrames = (Time.time - t0) * 60f;
            float moved = Flat(_player.transform.position - start);
            float expectedTop = _config.CenterHeight + ExpectedRadius(device);
            Check("前隙 50F:站定不動,之後恢復行動", !_gauge.IsActive && Mathf.Abs(lagFrames - 50f) <= 1f + Slack && moved < 0.1f,
                $"前隙 {lagFrames:0.0}F 期間位移 {moved:0.00} m");
            Check("無人機第 15F 點火前停在發射座頂、30F 內升到屏障頂(1 + 半徑);展開完成(50F)前不擋",
                Mathf.Abs(droneEarly - 1.2f) < 0.01f && Mathf.Abs(droneTop - expectedTop) < 0.35f && !blockingEarly,
                $"10F 高 {droneEarly:0.00}、{topFrame:0}F 高 {droneTop:0.00}(頂 {expectedTop:0.00})、擋={blockingEarly}");
            yield return new WaitForSeconds(0.3f);
            Vector3 walked = _player.transform.position;
            yield return new WaitForSeconds(0.3f);
            _intent.Move = Vector2.zero;
            Check("前隙後可以走動", Flat(_player.transform.position - walked) > 1f, "");
            _player.Teleport(p + new Vector3(-2f, 0.1f, -2f), 90f);

            // (c) 耐久與半徑、腳下塗墨
            var ground = GameObject.Find("Ground").GetComponent<PaintableSurface>();
            Vector3 b = device.transform.position;
            bool paintIn = ground.SampleOwnership(b + new Vector3(4f, 0f, 0f)) == (byte)Team.Alpha
                           && ground.SampleOwnership(b + new Vector3(0f, 0f, -4.1f)) == (byte)Team.Alpha;
            bool paintOut = ground.SampleOwnership(b + new Vector3(5.2f, 0f, 0f)) != (byte)Team.Alpha;
            float hp0 = device.Hp;
            float s0 = Time.time;
            yield return new WaitForSeconds(1f);
            float decay = (hp0 - device.Hp) / (Time.time - s0);
            float radius = device.Radius;
            float expected = ExpectedRadius(device);
            Check("耐久每秒 −92.1(1536 / 92.1 = 1001F 自然消失)", Mathf.Abs(decay - 92.1f) < 1.5f,
                $"{decay:0.0}/s,耐久 {device.Hp:0.0}/{device.MaxHp:0}");
            Check("屏障半徑 = 2.255 + 5.245 × RadiusRatioCurve(耐久比例);頂端 = 無人機;≤ 7.5",
                Mathf.Abs(radius - expected) < 0.05f && radius <= 7.5f && radius >= 2.25f
                && Mathf.Abs(device.DroneHeight - (_config.CenterHeight + radius)) < 0.05f && device.BarrierUp,
                $"半徑 {radius:0.00}(期望 {expected:0.00})、無人機 {device.DroneHeight:0.00}");
            Check("設置時腳下塗 4.5 u", paintIn && paintOut, $"4 m 內塗到={paintIn} 5.2 m 外沒塗={paintOut}");

            yield return Bullets(device, p);
            yield return Explosions(device, p);
            yield return LockRules(device);
        }

        private float ExpectedRadius(BigBubblerDevice device)
        {
            float ratio = device.Hp / device.MaxHp;
            return _config.MinRadius + (device.MaxRadiusLimit - _config.MinRadius)
                   * HermiteCurve.BubblerRadiusRatio.Evaluate(ratio);
        }

        // ── (d) 子彈 ──
        private IEnumerator Bullets(BigBubblerDevice device, Vector3 p)
        {
            Vector3 c = device.BarrierCenter;
            TargetDummy inside = SpawnDummy(p + new Vector3(0f, 0f, 2f));
            TargetDummy outside = SpawnDummy(p + new Vector3(0f, 0f, -12f));
            yield return new WaitForSeconds(0.2f);
            InkBulletSpec spec = InkBulletSpec.Splattershot;
            spec.DripSpacing = 0f;

            // 罩外敵彈 → 停在球面
            float barrierBefore = device.BarrierDamageTaken;
            int start = _impacts.Count;
            Vector3 from = new Vector3(p.x, 1f, p.z + 11f);
            InkBullet.Fire(spec, from, Vector3.back, Team.Bravo, _enemy);
            yield return WaitUntil(() => _impacts.Count > start, 1f);
            InkBulletImpact hit = _impacts.Count > start ? _impacts[start] : default;
            float barrierDealt = device.BarrierDamageTaken - barrierBefore;
            bool onShell = Mathf.Abs(Vector3.Distance(hit.Point, c) - device.Radius) < 0.1f;
            Check("罩外敵方子彈停在球面:罩內假人 0 傷、屏障扣 36 × 0.64 = 23.0",
                hit.End == InkBulletEnd.Target && ReferenceEquals(hit.Target, device.Surface) && onShell
                && inside.PopupTotal == 0f && Mathf.Abs(barrierDealt - 23f) < 0.01f,
                $"{hit.End} 在球面={onShell} 假人 {inside.PopupTotal:0.0} 屏障 −{barrierDealt:0.0}");

            // 友軍子彈穿過
            start = _impacts.Count;
            InkBullet.Fire(spec, from, Vector3.back, Team.Alpha, _player);
            yield return WaitUntil(() => _impacts.Count > start, 1f);
            hit = _impacts.Count > start ? _impacts[start] : default;
            Check("友軍子彈穿過屏障打到罩內假人", hit.End == InkBulletEnd.Target && ReferenceEquals(hit.Target, inside)
                                            && inside.PopupTotal > 0f, $"{hit.End} 假人 {inside.PopupTotal:0.0}");

            // 罩內往外打不擋
            start = _impacts.Count;
            InkBullet.Fire(spec, new Vector3(p.x, 1f, p.z - 2f), Vector3.back, Team.Bravo, _enemy);
            yield return WaitUntil(() => _impacts.Count > start, 1f);
            hit = _impacts.Count > start ? _impacts[start] : default;
            Check("罩內的敵人往外打不擋", hit.End == InkBulletEnd.Target && ReferenceEquals(hit.Target, outside),
                $"{hit.End} 目標={(hit.Target != null ? hit.Target.GetType().Name : "無")}");

            // 舊式 InkProjectile(Splattershot 主武器的彈)從罩外打進來也擋
            var weapon = _player.GetComponent<WeaponLoadout>().Kit.Main as WeaponConfig;
            float insideBefore = inside.PopupTotal;
            barrierBefore = device.BarrierDamageTaken;
            if (weapon != null && weapon.ProjectilePrefab != null)
            {
                InkProjectile projectile = Instantiate(weapon.ProjectilePrefab);
                projectile.Launch(from, Vector3.back * weapon.MuzzleSpeed, weapon, null, null, 0, _enemy);
                yield return new WaitForSeconds(0.4f);
            }
            float projectileDealt = device.BarrierDamageTaken - barrierBefore;
            Check("Splattershot 的 InkProjectile 從罩外打進來同樣停在球面",
                weapon != null && projectileDealt > 20f && Mathf.Approximately(inside.PopupTotal, insideBefore),
                $"屏障 −{projectileDealt:0.0}、罩內假人 {insideBefore:0.0} → {inside.PopupTotal:0.0}");

            // 打地面發射座(弱點)×1
            float weakBefore = device.WeakPointDamageTaken;
            start = _impacts.Count;
            InkBullet.Fire(spec, new Vector3(p.x + 3f, 0.6f, p.z), Vector3.left, Team.Bravo, _enemy);
            yield return WaitUntil(() => _impacts.Count > start, 1f);
            float weak = device.WeakPointDamageTaken - weakBefore;
            Check("打發射座(弱點)×1:36", Mathf.Abs(weak - 36f) < 0.01f, $"弱點 −{weak:0.0}");
            Destroy(inside.gameObject);
            Destroy(outside.gameObject);
            yield return new WaitForSeconds(0.1f);
        }

        // ── (e) 爆炸與炸彈 ──
        private IEnumerator Explosions(BigBubblerDevice device, Vector3 p)
        {
            Vector3 c = device.BarrierCenter;
            float r = device.Radius;
            Vector3 dir = Vector3.forward;
            TargetDummy shielded = SpawnDummy(new Vector3(c.x, 0f, c.z) + dir * (r - 1.2f));
            yield return new WaitForSeconds(0.2f);
            float barrierBefore = device.BarrierDamageTaken;
            Vector3 outsideCenter = c + dir * (r + 1.2f);
            ExplosionSpec spec = ExplosionSpec.SplatBomb;
            spec.PaintRadius = 0f;
            spec.PaintRayCount = 0;
            spec.DropCount = 0;
            ExplosionResult result = Explosion.Detonate(spec, outsideCenter, Team.Bravo, _enemy);
            float barrierDealt = device.BarrierDamageTaken - barrierBefore;
            Check("罩外爆炸炸不到罩內(假人 2.4 m 內 0 傷);屏障吃 180 × 0.64 × 2.0 = 230.4",
                shielded.PopupTotal == 0f && result.BlockedByCover >= 1 && Mathf.Abs(barrierDealt - 230.4f) < 0.05f,
                $"假人 {shielded.PopupTotal:0.0} 被擋 {result.BlockedByCover} 屏障 −{barrierDealt:0.0}");

            Vector3 insideCenter = c + new Vector3(-1.5f, -0.6f, 0.5f);
            float weakBefore = device.WeakPointDamageTaken;
            barrierBefore = device.BarrierDamageTaken;
            Explosion.Detonate(spec, insideCenter, Team.Bravo, _enemy);
            Check("罩內爆炸照炸罩內的人;屏障本身不吃(罩內炸不到球面)、發射座吃 Bomb × 0.5",
                shielded.PopupTotal > 0f && device.BarrierDamageTaken == barrierBefore
                && device.WeakPointDamageTaken > weakBefore,
                $"假人 {shielded.PopupTotal:0.0} 屏障 −{device.BarrierDamageTaken - barrierBefore:0.0} 弱點 −{device.WeakPointDamageTaken - weakBefore:0.0}");
            Destroy(shielded.gameObject);

            // 敵方炸彈碰到球面立刻引爆(沒等引信)
            var bombConfig = _player.GetComponent<WeaponLoadout>().Kit.Sub as SplatBombConfig;
            if (bombConfig != null && bombConfig.Prefab != null)
            {
                int before = SplatBomb.ExplosionCount;
                SubProjectile bomb = SharedPool.Spawn(bombConfig.Prefab);
                Vector3 launch = c + dir * (device.Radius + 4f) + Vector3.up * 0.5f;
                bomb.Launch(launch, new Vector3(0f, 1f, -10f), bombConfig, _enemy);
                float thrownAt = Time.time;
                yield return WaitUntil(() => SplatBomb.ExplosionCount > before, 2f);
                float seconds = Time.time - thrownAt;
                float centerDistance = Vector3.Distance(SplatBomb.LastExplosionCenter, c);
                Check("敵方炸彈碰到屏障立刻引爆(爆心在球面外)",
                    SplatBomb.ExplosionCount > before && seconds < 0.8f && centerDistance >= device.Radius - 0.05f,
                    $"{seconds:0.00}s 後爆炸,爆心離球心 {centerDistance:0.00}(半徑 {device.Radius:0.00})");
            }
            else
            {
                Check("敵方炸彈碰到屏障立刻引爆", false, "目前的副武器不是斯普拉炸彈");
            }
            yield return new WaitForSeconds(0.2f);
        }

        // ── (f) 鎖定規則與打破 ──
        private IEnumerator LockRules(BigBubblerDevice device)
        {
            float elapsed = device.ElapsedFrames / 60f;
            float expected = 100f * (1f - (device.ElapsedFrames / 60f) / (1001f / 60f));
            float points = _gauge.Points;
            Check("鎖定倒數:能量 = 100 × (1 − 經過 / 1001F)", _gauge.IsLocked && Mathf.Abs(points - expected) < 1f,
                $"{points:0.0}(期望 {expected:0.0},經過 {elapsed:0.00}s)");
            _player.OnTurfInked(30f * TurfPoints.SquareMetresPerPoint, true);
            float afterCharge = _gauge.Points;
            Check("鎖定中塗地不充能", afterCharge <= points + 0.01f, $"{points:0.00} → {afterCharge:0.00}");

            var life = _player.GetComponent<InklingLifecycle>();
            _health.Invulnerable = false;
            float beforeDeath = _gauge.Points;
            _health.ApplyDamage(new DamageInfo(300f, _player.transform.position + Vector3.up, Team.Bravo, _enemy,
                DamageKind.Main));
            yield return null;
            float afterDeath = _gauge.Points;
            yield return new WaitForSeconds(1f);
            float laterDead = _gauge.Points;
            Check("被擊倒不中斷、不減半(鎖定型),陣亡期間照樣倒數",
                life.IsDead && _gauge.IsLocked && afterDeath > beforeDeath * 0.9f && laterDead < afterDeath - 4f
                && device.IsAlive,
                $"死前 {beforeDeath:0.0} → 死後 {afterDeath:0.0} → 1 秒後 {laterDead:0.0};屏障仍在={device.IsAlive}");
            life.ForceRespawnNow();
            _health.Invulnerable = true;
            yield return new WaitForSeconds(0.2f);

            int pops = BigBubblerDevice.PopCount;
            device.ApplyDamage(new DamageInfo(3000f, device.transform.position + Vector3.up * 0.6f, Team.Bravo, _enemy,
                DamageKind.Main, AttackClass.Shooter));
            yield return null;
            yield return null;
            bool popped = BigBubblerDevice.PopCount == pops + 1 && device.Surface != null && !device.Surface.IsAlive;
            Check("打破屏障 → 能量立刻歸 0、解除鎖定", popped && !_gauge.IsLocked && _gauge.Points == 0f,
                $"破掉={popped} 鎖定={_gauge.IsLocked} 能量={_gauge.Points:0.0}");
            _player.OnTurfInked(20f * TurfPoints.SquareMetresPerPoint, true);
            Check("解除鎖定後恢復充能", _gauge.Points > 0f, $"{_gauge.Points:0.00}");
        }

        // ── (g) A1 炸彈碰到敵方球罩 ──
        // 上面的裝置已被打破:另外立一顆獨立的我方球罩(只有 BarrierSphere,沒有裝置),敵人對它丟 A1 的炸彈。
        // 吸盤炸彈黏住後要 120F 才爆、冰壺炸彈引信 210F:很快就爆 = 碰到球面引爆,不是引信到。
        private IEnumerator OtherBombsOnShell()
        {
            var suction = Resources.Load<SuctionBombConfig>("Weapons/Sub_suction_bomb");
            var curling = Resources.Load<CurlingBombConfig>("Weapons/Sub_curling_bomb");
            if (suction == null || suction.Prefab == null || curling == null || curling.Prefab == null)
            {
                Check("A1 炸彈碰到敵方球罩立刻引爆", false, "找不到 Resources/Weapons/Sub_suction_bomb / Sub_curling_bomb");
                yield break;
            }
            var holder = new GameObject("BubblerShellProbe");
            _spawned.Add(holder);
            Vector3 ground = SpecialTestKit.Ground(new Vector3(-14f, 0f, -12f));
            const float radius = 4f;
            var shell = new BarrierSphere(holder.transform, null)
            {
                Center = ground + Vector3.up,
                Radius = radius,
                Blocking = true,
            };
            shell.Enable(_player.Team);
            try
            {
                // 玩家(我方)站到球罩後方,不擋炸彈的路
                _intent.Move = Vector2.zero;
                _player.Teleport(ground + Vector3.back * (radius + 3f) + Vector3.up * 0.1f, 0f);
                TargetDummy shielded = SpawnDummy(ground);
                yield return new WaitForSeconds(0.2f);
                Vector3 c = shell.Center;

                int before = SuctionBomb.ExplosionCount;
                SubProjectile bomb = SharedPool.Spawn(suction.Prefab);
                bomb.Launch(c + Vector3.forward * (radius + 4f) + Vector3.up * 0.5f, new Vector3(0f, 2f, -14f), suction,
                    _enemy);
                float thrownAt = Time.time;
                yield return WaitUntil(() => SuctionBomb.ExplosionCount > before, 3f);
                float seconds = Time.time - thrownAt;
                float centerDistance = Vector3.Distance(SuctionBomb.LastExplosionCenter, c);
                Check("敵方吸盤炸彈(飛行)碰到球罩立刻引爆:爆心在球面外、罩內假人 0 傷",
                    SuctionBomb.ExplosionCount > before && seconds < 1f && centerDistance > radius
                    && shielded.PopupTotal == 0f,
                    $"{seconds:0.00}s 後爆炸,爆心離球心 {centerDistance:0.00}(半徑 {radius:0.00}),假人 {shielded.PopupTotal:0.0}");
                yield return new WaitForSeconds(0.2f);

                before = CurlingBomb.ExplosionCount;
                int touches = CurlingBomb.DetonatorHitCount;
                bomb = SharedPool.Spawn(curling.Prefab);
                bomb.Launch(ground + Vector3.forward * (radius + 5f) + Vector3.up * 0.3f, new Vector3(0f, 1f, -12f),
                    curling, _enemy);
                thrownAt = Time.time;
                yield return WaitUntil(() => CurlingBomb.ExplosionCount > before, 4f);
                seconds = Time.time - thrownAt;
                centerDistance = Vector3.Distance(CurlingBomb.LastExplosionCenter, c);
                Check("敵方冰壺炸彈(滑行)碰到球罩立刻引爆:爆心在球面外、罩內假人 0 傷",
                    CurlingBomb.ExplosionCount > before && CurlingBomb.DetonatorHitCount > touches && seconds < 3f
                    && centerDistance > radius && shielded.PopupTotal == 0f,
                    $"{seconds:0.00}s 後爆炸,碰引爆物={CurlingBomb.DetonatorHitCount - touches},爆心離球心 {centerDistance:0.00},"
                    + $"假人 {shielded.PopupTotal:0.0}");
            }
            finally
            {
                // 靜態清單不留假球罩給後面的測試
                shell.Disable();
            }
        }

        private TargetDummy SpawnDummy(Vector3 position)
        {
            TargetDummy dummy = Instantiate(_template, SpecialTestKit.Ground(position), Quaternion.identity);
            _spawned.Add(dummy);
            return dummy;
        }

        private static float Flat(Vector3 v)
        {
            return new Vector2(v.x, v.z).magnitude;
        }
    }
}

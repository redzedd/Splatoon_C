using System.Collections;
using System.Collections.Generic;
using SplatoonC.Core.Combat;
using SplatoonC.Core.Match;
using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 射擊槍變體驗收的共用流程(每把變體一個 [AutoTest] 子類,各自在全新 play session 跑):
    // 執行期武器組合換裝 → 實測 射速 / 散布 / 擊倒發數與 TTK / 有效射程(全傷最遠距離)/ 開火走速 /
    // 單發塗地(與 Splattershot 同條件比)/ 單發耗墨 / 一槽發數與連射秒數 / 墨量鎖 / 墨槽容量 / 槍模與墨彈外觀 /
    // 潛墨隱形 / bot 掛勾。期望值全取自 S3 原始參數(S3ShooterParams);量測值全來自真實遊戲路徑
    //(意圖 → InkShooter → 墨彈 → 假人 / 地面),不是讀設定回來比。
    // 場地:試射場 x ≤ −4 的空地(x=−24~−4、z=−24~0),不碰靶場與爬牆區。
    public abstract class ShooterVariantAutoTest : AutoTestBase
    {
        // 瞄準固定在槍口 → 目標:與相機無關,射程/傷害量測可重現
        private sealed class TestAim : IAimView
        {
            public Vector3 Origin;
            public Vector3 Forward = Vector3.forward;

            public Vector3 AimOrigin => Origin;
            public Vector3 AimForward => Forward;
            public float Yaw => Mathf.Atan2(Forward.x, Forward.z) * Mathf.Rad2Deg;
        }

        private struct HitRecord
        {
            public float Time;
            public IDamageable Target;
            public float Amount;
            public bool Splatted;
        }

        private const float DummyCenterHeight = 1f;

        private readonly List<HitRecord> _hits = new List<HitRecord>();
        private readonly List<string> _sounds = new List<string>();
        private readonly List<GameObject> _spawned = new List<GameObject>();

        private GameObject _player;
        private Inkling _inkling;
        private WeaponLoadout _loadout;
        private PlayerInkTank _tank;
        private Transform _visual;
        private Transform _muzzle;
        private ScriptedIntent _intent;
        private TestAim _aim;
        private GameObject _dummyTemplate;
        private PaintableSurface _ground;
        private WeaponConfig _config;
        private WeaponKit _baseKit;
        private WeaponKit _variantKit;
        private float _maxDeltaTime;
        private CharacterController _controller;
        // 單發塗地(m²,無重疊):一槽塗地估計要用
        private float _paintPerShotBaseline;
        private float _paintPerShotVariant;

        // Resources/Weapons/Main_<id>
        protected abstract string ConfigId { get; }

        // S3 原始參數(期望值來源)
        protected abstract S3ShooterParams S3 { get; }

        private InkShooter Shooter => _loadout != null ? _loadout.Main as InkShooter : null;

        private void OnEnable()
        {
            GameAudio.Played += OnSoundPlayed;
        }

        private void OnDisable()
        {
            GameAudio.Played -= OnSoundPlayed;
            if (_inkling != null)
            {
                _inkling.DamageDealt -= OnDamageDealt;
            }
        }

        private void OnSoundPlayed(string id)
        {
            _sounds.Add(id);
        }

        private void OnDamageDealt(IDamageable target, float amount, bool splatted)
        {
            _hits.Add(new HitRecord { Time = Time.time, Target = target, Amount = amount, Splatted = splatted });
        }

        protected IEnumerator Start()
        {
            _player = FindPlayer();
            _inkling = _player != null ? _player.GetComponent<Inkling>() : null;
            _loadout = _player != null ? _player.GetComponent<WeaponLoadout>() : null;
            _tank = _player != null ? _player.GetComponent<PlayerInkTank>() : null;
            _visual = _player != null ? _player.transform.Find("Visual") : null;
            _controller = _player != null ? _player.GetComponent<CharacterController>() : null;
            _dummyTemplate = GameObject.Find("Dummy_Near");
            _config = Resources.Load<WeaponConfig>("Weapons/Main_" + ConfigId);
            foreach (PaintableSurface surface in PaintableSurface.Active)
            {
                if (surface.name == "Ground")
                {
                    _ground = surface;
                }
            }
            if (_inkling == null || _loadout == null || _tank == null || _visual == null || _controller == null
                || _dummyTemplate == null || _ground == null)
            {
                FailSetup("找不到 Player(Inkling/WeaponLoadout/PlayerInkTank/Visual)、Dummy_Near 或 Ground");
                yield break;
            }
            if (_config == null)
            {
                FailSetup($"找不到 Resources/Weapons/Main_{ConfigId}(先跑 Tools/SplatoonC/M7 Setup)");
                yield break;
            }
            _baseKit = _loadout.Kit;
            if (_baseKit == null || !_baseKit.IsValid)
            {
                FailSetup("Player 沒有預設武器組合(先跑 M7 Setup)");
                yield break;
            }
            _inkling.DamageDealt += OnDamageDealt;
            _intent = ScriptedIntent.Drive(_player.transform);
            _aim = new TestAim();
            _inkling.AimView = _aim;

            yield return EquipAndInspect();
            if (Shooter == null || Shooter.Config != _config || _muzzle == null)
            {
                Cleanup();
                Finish();
                yield break;
            }
            CheckBotHooks();
            yield return MeasureCadenceAndSpread();
            yield return MeasureTtk();
            yield return MeasureEffectiveRange();
            yield return MeasureBotStyleEngagement();
            yield return MeasureFiringMoveSpeed();
            yield return MeasurePaintPerShot();
            yield return MeasureInkTank();
            yield return MeasureSubmergedVisibility();

            Debug.Log($"[AUTOTEST] INFO {S3.Name} 量測期間最大幀時間 {_maxDeltaTime * 1000f:0.0} ms");
            Cleanup();
            Finish();
        }

        // ── 換裝、外觀 ──

        private IEnumerator EquipAndInspect()
        {
            _variantKit = ScriptableObject.CreateInstance<WeaponKit>();
            _variantKit.Setup(ConfigId, S3.Name, S3.Name, "AutoTest 執行期組合", _config, _baseKit.Sub,
                _baseKit.Special, 200f, S3.LightWeight ? WeightClass.Light : WeightClass.Mid,
                "射程", 50, "威力", 50, "射速", 50);
            yield return MoveTo(new Vector3(-12f, 0.1f, -20f));
            _loadout.Equip(_variantKit);
            yield return null;
            yield return null;

            InkShooter shooter = Shooter;
            Check($"{S3.Name} 換裝:同一個 InkShooter 換上變體設定",
                shooter != null && shooter.Config == _config && _loadout.Kit == _variantKit,
                $"主武器={(shooter != null ? shooter.Config.name : "非 InkShooter")}");
            if (shooter == null || shooter.Config != _config)
            {
                yield break;
            }
            _muzzle = FindRecursive(_player.transform, "Muzzle");
            if (_muzzle == null)
            {
                Check($"{S3.Name} 前置:角色有 Muzzle", false, "找不到 Muzzle 子物件");
                yield break;
            }

            Renderer barrel = BuiltInBarrel();
            ShooterGunModel model = shooter.GunModel;
            Check($"{S3.Name} 外觀:程序化槍模取代內建槍身、墨水部位套隊伍色",
                model != null && model.SourcePrefab == _config.GunModelPrefab && model.InkPartCount > 0
                && model.Visible && barrel != null && barrel.forceRenderingOff,
                $"槍模={(model != null ? model.name : "無")} 墨水部位={(model != null ? model.InkPartCount : 0)} " +
                $"內建槍身隱藏={(barrel != null && barrel.forceRenderingOff)}");

            Check($"{S3.Name} 墨槽容量 ×{S3.InkTankScale:0.0}(副武器耗墨也以標準墨槽計)",
                Mathf.Abs(_tank.CapacityScale - S3.InkTankScale) < 1e-4f,
                $"CapacityScale={_tank.CapacityScale:0.00}");
        }

        // ── bot 掛勾(BotBrain 交戰時呼叫的同一組方法)──

        private void CheckBotHooks()
        {
            InkShooter shooter = Shooter;
            float range = S3.EffectiveRange;
            var inRange = new BotCombatContext
            {
                Distance = range * 0.9f, TargetVisible = true, Reacted = true, AimError = 0.5f, FireTolerance = 4f,
            };
            var outOfRange = inRange;
            outOfRange.Distance = range * 1.25f;
            Check($"{S3.Name} bot:有效射程 {range:0.00} m(S3)內開火、射程外不開火;交戰距離 ≈ 0.72 × 射程",
                Mathf.Abs(shooter.EffectiveRange - range) < 0.02f && shooter.BotAttackIntent(inRange)
                && !shooter.BotAttackIntent(outOfRange) && Mathf.Abs(shooter.BotPreferredDistance - range * 0.72f) < 0.05f,
                $"EffectiveRange={shooter.EffectiveRange:0.00} 交戰距離={shooter.BotPreferredDistance:0.00} " +
                $"初速(提前量)={shooter.ProjectileSpeed:0.0} m/s");
        }

        // ── 射速、散布、音效 ──

        private IEnumerator MeasureCadenceAndSpread()
        {
            InkShooter shooter = Shooter;
            _tank.InfiniteInk = true;
            shooter.SpreadMultiplier = 1f;
            yield return MoveTo(new Vector3(-12f, 0.1f, -20f));
            AimAlong(Vector3.forward);
            yield return Frames(4);

            // 連射到偏向 b 必定爬到上限(N-ZAP 要 48 發)再多幾發
            int wantShots = Mathf.CeilToInt((S3.BiasMax - S3.BiasMin) / S3.BiasKf) + 6;
            int soundsBefore = _sounds.Count;
            int startShots = shooter.ShotsFired;
            var times = new List<float>();
            float maxAbsYaw = 0f;
            float biasAfterFirst = -1f;
            _intent.Attack = true;
            float deadline = Time.time + wantShots * S3.FireIntervalSeconds + 2f;
            int seen = startShots;
            while (shooter.ShotsFired - startShots < wantShots && Time.time < deadline)
            {
                AimAlong(Vector3.forward);
                yield return null;
                TrackDelta();
                if (shooter.ShotsFired != seen)
                {
                    // 同一幀可能出好幾發(低幀率補射):時間記同一幀
                    for (int i = seen; i < shooter.ShotsFired; i++)
                    {
                        times.Add(shooter.LastShotTime);
                    }
                    if (seen == startShots)
                    {
                        biasAfterFirst = shooter.ShotsFired - startShots == 1 ? shooter.SpreadBias : -1f;
                    }
                    seen = shooter.ShotsFired;
                    maxAbsYaw = Mathf.Max(maxAbsYaw, Mathf.Abs(shooter.LastYawOffsetDeg));
                }
            }
            _intent.Attack = false;
            float biasEnd = shooter.SpreadBias;
            int shots = times.Count;
            float interval = shots > 1 ? (times[shots - 1] - times[0]) / (shots - 1) : 0f;
            float expected = S3.FireIntervalSeconds;
            Check($"{S3.Name} 射速:每 {S3.RepeatFrame}F 一發({60f / S3.RepeatFrame:0.0} 發/秒)",
                shots >= 10 && Mathf.Abs(interval - expected) < expected * 0.03f,
                $"{shots} 發平均間隔 {interval * 1000f:0.0} ms(期望 {expected * 1000f:0.0} ms)");

            // 散布:首發 b = Min(+Kf 後 = Min+Kf);連射爬到 Max;所有偏角 ≤ 地面最大偏角,且真的有散開
            Check($"{S3.Name} 散布:偏角 ≤ {S3.StandSwerve}°、連射偏向 b 由 {S3.BiasMin} 爬到 {S3.BiasMax}",
                Mathf.Abs(biasAfterFirst - (S3.BiasMin + S3.BiasKf)) < 1e-3f
                && Mathf.Abs(biasEnd - S3.BiasMax) < 0.02f
                && maxAbsYaw <= S3.StandSwerve + 1e-3f && maxAbsYaw >= S3.StandSwerve * 0.3f,
                $"首發後 b={biasAfterFirst:0.000} 連射 {shots} 發後 b={biasEnd:0.000} 最大偏角 {maxAbsYaw:0.00}°");

            bool heard = false;
            for (int i = soundsBefore; i < _sounds.Count; i++)
            {
                heard |= _sounds[i].StartsWith("shoot_shooter", System.StringComparison.Ordinal);
            }
            Check($"{S3.Name} 射擊音效({_config.FireSound},未合成前退回 shoot_shooter)", heard,
                $"期間發聲 {_sounds.Count - soundsBefore} 次");

            if (_config.UsesInkBullet)
            {
                // S3 墨彈:場上飛行中的是自己的 InkBullet,外觀 = 通用墨彈 × 變體縮放(彈徑)
                CombatAssets assets = CombatAssets.Instance;
                Transform prefabBody = assets != null && assets.InkBulletPrefab != null
                                       && assets.InkBulletPrefab.transform.childCount > 0
                    ? assets.InkBulletPrefab.transform.GetChild(0)
                    : null;
                float scale = _config.Bullet.EffectiveVisualScale;
                Vector3 wanted = prefabBody != null ? prefabBody.localScale * scale : Vector3.zero;
                _intent.Attack = true;
                yield return Frames(3);
                _intent.Attack = false;
                int flying = 0;
                bool scaled = false;
                foreach (InkBullet b in FindObjectsByType<InkBullet>(FindObjectsSortMode.None))
                {
                    if (!b.IsFlying || b.Owner != _inkling || b.transform.childCount == 0)
                    {
                        continue;
                    }
                    flying++;
                    scaled |= (b.transform.GetChild(0).localScale - wanted).sqrMagnitude < 1e-6f;
                }
                Check($"{S3.Name} 墨彈外觀:S3 墨彈 InkBullet(通用墨彈 × {scale:0.00})", prefabBody != null && flying > 0 && scaled,
                    $"飛行中 {flying} 發;期望彈身 {wanted.x:0.00} × {wanted.z:0.00}");
            }
            else
            {
                // 舊的兩段式彈:場上飛行中的彈用的是變體的彈 prefab(彈徑)
                Vector3 prefabScale = _config.ProjectilePrefab.transform.localScale;
                _intent.Attack = true;
                yield return Frames(3);
                _intent.Attack = false;
                bool bulletMatches = false;
                foreach (InkProjectile p in FindObjectsByType<InkProjectile>())
                {
                    if (p.gameObject.activeInHierarchy && (p.transform.localScale - prefabScale).sqrMagnitude < 1e-6f)
                    {
                        bulletMatches = true;
                    }
                }
                Check($"{S3.Name} 墨彈外觀:用變體彈 prefab({_config.ProjectilePrefab.name})", bulletMatches,
                    $"彈徑 {prefabScale.x:0.00} × 長 {prefabScale.z:0.00}");
            }
            yield return new WaitForSeconds(0.6f);
        }

        // ── 擊倒發數與 TTK(近距 5 m,全傷;散布關閉 = S3 TTK 的定義:每發都中)──

        private IEnumerator MeasureTtk()
        {
            InkShooter shooter = Shooter;
            shooter.SpreadMultiplier = 0f;
            _tank.InfiniteInk = true;
            yield return MoveTo(new Vector3(-8f, 0.1f, -20f));
            AimAlong(Vector3.forward);
            yield return Frames(3);
            TargetDummy dummy = SpawnDummy(MuzzleFlat() + Vector3.forward * 5f);
            yield return Frames(2);

            _hits.Clear();
            float deadline = Time.time + 3f;
            _intent.Attack = true;
            while (dummy.IsAlive && Time.time < deadline)
            {
                AimAt(DummyCenter(dummy));
                yield return null;
                TrackDelta();
            }
            _intent.Attack = false;

            int hits = 0;
            float first = -1f;
            float kill = -1f;
            bool allFull = true;
            foreach (HitRecord h in _hits)
            {
                if (!ReferenceEquals(h.Target, dummy))
                {
                    continue;
                }
                hits++;
                allFull &= Mathf.Abs(h.Amount - S3.DamageMax) < 0.01f;
                if (first < 0f)
                {
                    first = h.Time;
                }
                if (h.Splatted)
                {
                    kill = h.Time;
                }
            }
            int expectedShots = S3.ShotsToSplat();
            Check($"{S3.Name} 擊倒發數:{S3.DamageMax} × {expectedShots} 發(近距全傷)",
                kill > 0f && hits == expectedShots && allFull,
                $"命中 {hits} 發、每發全傷={allFull}、擊倒={kill > 0f}");
            float ttk = kill - first;
            float expectedTtk = S3.TtkFrames() / 60f;
            float tolerance = Mathf.Max(0.02f, _maxDeltaTime * 1.5f);
            Check($"{S3.Name} TTK(首發命中後):{S3.TtkFrames()}F = {expectedTtk:0.000} s",
                kill > 0f && Mathf.Abs(ttk - expectedTtk) <= tolerance,
                $"實測 {ttk:0.000} s(容許 ±{tolerance:0.000})");
            Despawn(dummy);
            yield return new WaitForSeconds(0.3f);
        }

        // ── 有效射程:假人放在 S3 有效射程 −1.0 ~ +1.0 m(S3 墨彈;同雙槍)/ +1.5 m(舊的兩段式彈,滑行較遠)(每 0.5 m),
        //    單發量傷害,全傷最遠的距離 ──

        private IEnumerator MeasureEffectiveRange()
        {
            InkShooter shooter = Shooter;
            shooter.SpreadMultiplier = 0f;
            _tank.InfiniteInk = true;
            float range = S3.EffectiveRange;
            float lastFull = -1f;
            bool contiguous = true;
            float farDamage = -1f;
            string log = "";
            // S3 墨彈過了有效射程就開始下墜:射程 + 1.5 m 已打不到胸口高度,最遠量到 + 1.0 m
            int lastStep = _config.UsesInkBullet ? 2 : 3;
            for (int step = -2; step <= lastStep; step++)
            {
                float distance = range + step * 0.5f;
                yield return MoveTo(new Vector3(-12f, 0.1f, -22f));
                AimAlong(Vector3.forward);
                yield return Frames(3);
                TargetDummy dummy = SpawnDummy(MuzzleFlat() + Vector3.forward * distance);
                yield return Frames(2);
                AimAt(DummyCenter(dummy));
                yield return Frames(2);
                float damage = -1f;
                for (int attempt = 0; attempt < 2 && damage < 0f; attempt++)
                {
                    yield return new WaitForSeconds(S3.FireIntervalSeconds + 0.05f);
                    _hits.Clear();
                    AimAt(DummyCenter(dummy));
                    _intent.Attack = true;
                    yield return null;
                    _intent.Attack = false;
                    float deadline = Time.time + 0.8f;
                    while (Time.time < deadline && damage < 0f)
                    {
                        foreach (HitRecord h in _hits)
                        {
                            if (ReferenceEquals(h.Target, dummy))
                            {
                                damage = h.Amount;
                            }
                        }
                        yield return null;
                    }
                }
                bool full = Mathf.Abs(damage - S3.DamageMax) < 0.01f;
                if (full && contiguous)
                {
                    lastFull = distance;
                }
                else if (!full)
                {
                    contiguous = false;
                }
                if (step == lastStep)
                {
                    farDamage = damage;
                }
                log += $" {distance:0.0}m→{(damage < 0f ? "未中" : damage.ToString("0.0"))}";
                Despawn(dummy);
            }
            Check($"{S3.Name} 有效射程(全傷最遠距離)≈ S3 {range:0.00} m",
                lastFull > 0f && Mathf.Abs(lastFull - range) <= 0.5f + 1e-3f,
                $"全傷最遠 {lastFull:0.00} m;逐點:{log}");
            Check($"{S3.Name} 射程外傷害衰減({S3.DamageMax} → 下限 {S3.DamageMin})",
                farDamage >= S3.DamageMin - 0.01f && farDamage < S3.DamageMax - 0.5f,
                $"{range + lastStep * 0.5f:0.0} m 處 {farDamage:0.0}");
        }

        // ── 以 bot 掛勾驅動開火(模擬 BotBrain 交戰):射程內看得到就按住,要真的打中 ──

        private IEnumerator MeasureBotStyleEngagement()
        {
            InkShooter shooter = Shooter;
            shooter.SpreadMultiplier = 1f;
            _tank.InfiniteInk = true;
            yield return MoveTo(new Vector3(-16f, 0.1f, -22f));
            AimAlong(Vector3.forward);
            yield return Frames(3);
            TargetDummy dummy = SpawnDummy(MuzzleFlat() + Vector3.forward * (S3.EffectiveRange * 0.8f));
            yield return Frames(2);
            _hits.Clear();
            int startShots = shooter.ShotsFired;
            int held = 0;
            float end = Time.time + 1.5f;
            while (Time.time < end && dummy.IsAlive)
            {
                Vector3 center = DummyCenter(dummy);
                AimAt(center);
                var context = new BotCombatContext
                {
                    Distance = Vector3.Distance(_muzzle.position, center),
                    TargetVisible = true,
                    Reacted = true,
                    AimError = 0f,
                    FireTolerance = 4f,
                };
                _intent.Attack = shooter.BotAttackIntent(context);
                held += _intent.Attack ? 1 : 0;
                yield return null;
            }
            _intent.Attack = false;
            int hits = 0;
            foreach (HitRecord h in _hits)
            {
                hits += ReferenceEquals(h.Target, dummy) ? 1 : 0;
            }
            Check($"{S3.Name} bot 掛勾驅動開火:{S3.EffectiveRange * 0.8f:0.0} m 目標按住開火並命中(含 S3 散布)",
                held > 0 && shooter.ShotsFired > startShots && hits > 0,
                $"按住 {held} 幀、出膛 {shooter.ShotsFired - startShots} 發、命中 {hits} 發");
            Despawn(dummy);
            yield return new WaitForSeconds(0.3f);
        }

        // ── 開火走速(S3 MoveSpeed;輕量級也不加成)──

        private IEnumerator MeasureFiringMoveSpeed()
        {
            _tank.InfiniteInk = true;
            Shooter.SpreadMultiplier = 1f;
            yield return MoveTo(new Vector3(-20f, 0.1f, -16f));
            AimAlong(Vector3.forward);
            yield return Frames(3);
            _intent.Attack = true;
            _intent.Move = new Vector2(0f, 1f);
            float t0 = Time.time;
            while (Time.time - t0 < 0.5f)
            {
                AimAlong(Vector3.forward);
                yield return null;
            }
            Vector3 p0 = _player.transform.position;
            float s0 = Time.time;
            while (Time.time - s0 < 0.8f)
            {
                AimAlong(Vector3.forward);
                yield return null;
            }
            Vector3 p1 = _player.transform.position;
            float elapsed = Time.time - s0;
            _intent.Attack = false;
            _intent.Move = Vector2.zero;
            p1.y = p0.y;
            float speed = Vector3.Distance(p0, p1) / Mathf.Max(elapsed, 1e-3f);
            float expected = S3.MoveSpeedWhileFiring;
            Check($"{S3.Name} 開火走速 {S3.MoveSpeed} u/F = {expected:0.00} m/s" + (S3.LightWeight ? "(輕量級不加成)" : ""),
                Mathf.Abs(speed - expected) < expected * 0.05f,
                $"實測 {speed:0.00} m/s");
            yield return new WaitForSeconds(0.4f);
        }

        // ── 單發塗地:4 條互不重疊的射道各一發(平射,散布關閉),與 Splattershot 同條件比 ──

        private IEnumerator MeasurePaintPerShot()
        {
            float baseline = 0f;
            yield return PaintPerShot(_baseKit, a => baseline = a);
            float variant = 0f;
            yield return PaintPerShot(_variantKit, a => variant = a);

            _paintPerShotBaseline = baseline;
            _paintPerShotVariant = variant;
            float measuredRatio = baseline > 0f ? variant / baseline : 0f;
            float s3Ratio = S3.EstimatedPaintAreaPerShot / S3ShooterParams.Splattershot.EstimatedPaintAreaPerShot;
            Check($"{S3.Name} 單發塗地(相對 Splattershot ≈ S3 估計 ×{s3Ratio:0.00})",
                variant > 0f && Mathf.Abs(measuredRatio - s3Ratio) <= s3Ratio * 0.3f,
                $"變體 {TurfPoints.FromArea(variant):0.0}p({variant:0.0} m²)/ Splattershot " +
                $"{TurfPoints.FromArea(baseline):0.0}p = ×{measuredRatio:0.00}");
        }

        private IEnumerator PaintPerShot(WeaponKit kit, System.Action<float> report)
        {
            _loadout.Equip(kit);
            yield return null;
            InkShooter shooter = Shooter;
            shooter.SpreadMultiplier = 0f;
            _tank.InfiniteInk = true;
            _ground.ClearInk();
            float area = 0f;
            int shots = 0;
            for (int lane = 0; lane < 4; lane++)
            {
                yield return MoveTo(new Vector3(-22f + lane * 5f, 0.1f, -23f));
                AimAlong(Vector3.forward);
                // 等射擊冷卻(兩把槍中最慢的 .52 Gal 是 9F)
                yield return new WaitForSeconds(0.2f);
                AimAlong(Vector3.forward);
                float before = _inkling.TurfInkedArea;
                int shotsBefore = shooter.ShotsFired;
                _intent.Attack = true;
                yield return null;
                _intent.Attack = false;
                // 墨彈約 0.25 s 落地、墨滴再掉 0.3 s
                yield return new WaitForSeconds(1f);
                area += _inkling.TurfInkedArea - before;
                shots += shooter.ShotsFired - shotsBefore;
            }
            report(shots > 0 ? area / shots : 0f);
        }

        // ── 墨槽:副武器耗墨換算、單發耗墨、一槽發數與連射秒數、墨量鎖、一槽塗地(掃射)──

        private IEnumerator MeasureInkTank()
        {
            if (_loadout.Kit != _variantKit)
            {
                _loadout.Equip(_variantKit);
                yield return null;
            }
            InkShooter shooter = Shooter;
            shooter.SpreadMultiplier = 1f;
            _tank.InfiniteInk = false;

            // 副武器耗墨(Splat Bomb 70% 標準墨槽)在大墨槽裡佔比較小:Jr. 70 / 110 = 63.6%
            _tank.RefillFull();
            float before = _tank.Normalized;
            _tank.TryConsume(0.7f);
            float spentNormalized = before - _tank.Normalized;
            Check($"{S3.Name} 墨槽 {S3.InkTankScale * 100f:0}%:70% 的副武器只佔 {70f / S3.InkTankScale:0.0}%",
                Mathf.Abs(spentNormalized - 0.7f / S3.InkTankScale) < 1e-3f,
                $"扣 {spentNormalized * 100f:0.00}%");

            // 掃射區先清掉(前面的量測可能已經塗過)
            _ground.ClearInk();
            yield return MoveTo(new Vector3(-14f, 0.1f, -22f));
            AimAlong(Vector3.forward);
            _tank.RefillFull();
            yield return Frames(3);

            float capacity = _tank.CapacityScale;
            float perShotNormalized = S3.InkConsume / capacity;
            float turfBefore = _inkling.TurfInkedArea;
            int startShots = shooter.ShotsFired;
            float full = _tank.Normalized;
            float tenShotLevel = -1f;
            float firstShotTime = -1f;
            float sweepStart = Time.time;
            _intent.Attack = true;
            float deadline = Time.time + S3.FullTankFireSeconds + 6f;
            while (Time.time < deadline)
            {
                // 慢速左右掃射,塗一片扇形
                float yaw = Mathf.Sin((Time.time - sweepStart) * 0.9f) * 35f;
                AimAlong(Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, -0.12f, 1f));
                yield return null;
                TrackDelta();
                int fired = shooter.ShotsFired - startShots;
                if (fired > 0 && firstShotTime < 0f)
                {
                    firstShotTime = shooter.LastShotTime;
                }
                if (fired == 10 && tenShotLevel < 0f)
                {
                    tenShotLevel = _tank.Normalized;
                }
                // 剩墨不夠下一發:這一幀就放開,之後沒有乾扣扳機,墨量鎖從最後一發起算
                if (_tank.Normalized < perShotNormalized - 1e-5f)
                {
                    break;
                }
            }
            _intent.Attack = false;
            int shots = shooter.ShotsFired - startShots;
            float lastShotTime = shooter.LastShotTime;
            float released = _tank.Normalized;

            float perShot = tenShotLevel >= 0f ? (full - tenShotLevel) * capacity / 10f : -1f;
            Check($"{S3.Name} 單發耗墨 {S3.InkConsume * 100f:0.00}%(標準墨槽)",
                Mathf.Abs(perShot - S3.InkConsume) < S3.InkConsume * 0.01f,
                $"實測 {perShot * 100f:0.000}%");
            Check($"{S3.Name} 一槽發數 {S3.ShotsPerTank}(連射期間每發上鎖、不回墨)",
                Mathf.Abs(shots - S3.ShotsPerTank) <= 1,
                $"實測 {shots} 發");
            float fireSeconds = lastShotTime - firstShotTime;
            float expectedSeconds = (S3.ShotsPerTank - 1) * S3.FireIntervalSeconds;
            Check($"{S3.Name} 一槽連射秒數 ≈ {S3.FullTankFireSeconds:0.00} s(S3)",
                Mathf.Abs(fireSeconds - expectedSeconds) <= S3.FireIntervalSeconds + 0.05f,
                $"首發到末發 {fireSeconds:0.00} s(期望 {expectedSeconds:0.00} s)");

            // 墨量鎖:最後一發後 InkRecoverStop 幀內不回墨,之後開始回
            float unlockedAt = -1f;
            float lockEnd = Time.time + S3.InkLockSeconds + 1f;
            while (Time.time < lockEnd)
            {
                yield return null;
                if (_tank.Normalized > released + 1e-5f)
                {
                    unlockedAt = Time.time;
                    break;
                }
            }
            float lockMeasured = unlockedAt - lastShotTime;
            float lockTolerance = Mathf.Max(0.034f, _maxDeltaTime * 2f);
            Check($"{S3.Name} 墨量鎖 {S3.InkRecoverStop}F = {S3.InkLockSeconds:0.000} s",
                unlockedAt > 0f && Mathf.Abs(lockMeasured - S3.InkLockSeconds) <= lockTolerance,
                $"最後一發後 {lockMeasured:0.000} s 開始回墨(容許 ±{lockTolerance:0.000})");

            yield return new WaitForSeconds(0.8f);
            float perTankArea = _inkling.TurfInkedArea - turfBefore;
            Debug.Log($"[AUTOTEST] INFO {S3.Name} 一槽定點扇形掃射塗地 {TurfPoints.FromArea(perTankArea):0}p" +
                      $"({perTankArea:0} m²,{shots} 發;彈落點大量重疊,只供參考)");

            // 一槽塗地量(無重疊):實測單發塗地 × 實測一槽發數,對 Splattershot(單發 × 108 發)——
            // S3 特性:Jr. 一槽塗地約 Splattershot 的 2.7 倍、Pro 約一半
            S3ShooterParams reference = S3ShooterParams.Splattershot;
            float variantTank = _paintPerShotVariant * shots;
            float baselineTank = _paintPerShotBaseline * reference.ShotsPerTank;
            float tankRatio = baselineTank > 0f ? variantTank / baselineTank : 0f;
            float s3TankRatio = S3.EstimatedPaintAreaPerShot * S3.ShotsPerTank
                                / (reference.EstimatedPaintAreaPerShot * reference.ShotsPerTank);
            Check($"{S3.Name} 一槽塗地量(無重疊)相對 Splattershot ≈ S3 估計 ×{s3TankRatio:0.00}",
                perTankArea > 0f && Mathf.Abs(tankRatio - s3TankRatio) <= s3TankRatio * 0.3f,
                $"{TurfPoints.FromArea(variantTank):0}p / Splattershot {TurfPoints.FromArea(baselineTank):0}p = ×{tankRatio:0.00}");
        }

        // ── 潛墨隱形:槍模跟著角色一起消失、浮出再出現 ──

        private IEnumerator MeasureSubmergedVisibility()
        {
            _tank.InfiniteInk = true;
            yield return MoveTo(new Vector3(-18f, 0.1f, -6f));
            InkPainter.PaintSphere(new Vector3(-18f, 0f, -6f), 3f, 1f, _inkling.InkSource(false));
            yield return Frames(3);
            _intent.Squid = true;
            yield return new WaitForSeconds(0.8f);
            ShooterGunModel model = Shooter != null ? Shooter.GunModel : null;
            bool hidden = model != null && !model.Visible && !AnyRendererEnabled(model);
            _intent.Squid = false;
            yield return new WaitForSeconds(0.8f);
            bool shown = model != null && model.Visible && AnyRendererEnabled(model);
            Check($"{S3.Name} 潛墨時槍模一起隱形、浮出後出現", hidden && shown,
                $"潛墨隱藏={hidden} 浮出顯示={shown}");
        }

        // ── 收尾:換回 Splattershot,檢查外觀與墨槽容量還原 ──

        private void Cleanup()
        {
            if (_intent != null)
            {
                _intent.Attack = false;
                _intent.Move = Vector2.zero;
                _intent.Squid = false;
            }
            if (_loadout != null && _baseKit != null && _loadout.Kit != _baseKit)
            {
                _loadout.Equip(_baseKit);
                InkShooter shooter = Shooter;
                Renderer barrel = BuiltInBarrel();
                Check($"{S3.Name} 換回 Splattershot:內建槍身恢復、墨槽容量回到 100%",
                    shooter != null && shooter.GunModel == null && barrel != null && !barrel.forceRenderingOff
                    && Mathf.Abs(_tank.CapacityScale - 1f) < 1e-4f,
                    $"槍模={(shooter != null && shooter.GunModel != null)} 容量={_tank.CapacityScale:0.00}");
            }
            if (Shooter != null)
            {
                Shooter.SpreadMultiplier = 1f;
            }
            foreach (GameObject go in _spawned)
            {
                if (go != null)
                {
                    Destroy(go);
                }
            }
            _spawned.Clear();
            if (_inkling != null)
            {
                _inkling.AimView = null;
            }
            if (_tank != null)
            {
                _tank.InfiniteInk = false;
                _tank.RefillFull();
            }
            var router = _player != null ? _player.GetComponent<PlayerInputRouter>() : null;
            if (router != null)
            {
                router.ClearOverrideSource();
            }
            if (_player != null)
            {
                Teleport(_player.transform, new Vector3(0f, 0.1f, 0f));
            }
        }

        // ── 小工具 ──

        // 瞬移後等落地站穩:空中開火會套用跳射散布(b 固定 0.4、偏角放大),會污染地面量測
        private IEnumerator MoveTo(Vector3 position)
        {
            Teleport(_player.transform, position);
            int grounded = 0;
            float deadline = Time.time + 1.5f;
            while (grounded < 3 && Time.time < deadline)
            {
                yield return null;
                TrackDelta();
                grounded = _controller.isGrounded ? grounded + 1 : 0;
            }
        }

        // 瞄準:視線從槍口出發,朝 direction;視覺根直接轉到該方向(槍口位置才對)
        private void AimAlong(Vector3 direction)
        {
            Vector3 flat = new Vector3(direction.x, 0f, direction.z);
            if (flat.sqrMagnitude > 1e-6f)
            {
                _visual.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
            }
            _aim.Forward = direction.normalized;
            _aim.Origin = _muzzle != null ? _muzzle.position : _player.transform.position + Vector3.up * 1.05f;
        }

        private void AimAt(Vector3 point)
        {
            Vector3 flat = point - _player.transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude > 1e-6f)
            {
                _visual.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
            }
            Vector3 origin = _muzzle != null ? _muzzle.position : _player.transform.position + Vector3.up * 1.05f;
            _aim.Origin = origin;
            _aim.Forward = (point - origin).normalized;
        }

        // 槍口在地面的投影(假人距離從槍口量:S3 射程也是從槍口起算)
        private Vector3 MuzzleFlat()
        {
            Vector3 p = _muzzle != null ? _muzzle.position : _player.transform.position;
            p.y = 0f;
            return p;
        }

        private static Vector3 DummyCenter(TargetDummy dummy)
        {
            return dummy.transform.position + Vector3.up * DummyCenterHeight;
        }

        private TargetDummy SpawnDummy(Vector3 groundPosition)
        {
            GameObject go = Instantiate(_dummyTemplate, groundPosition, Quaternion.identity);
            go.name = "ShooterVariantDummy";
            _spawned.Add(go);
            return go.GetComponent<TargetDummy>();
        }

        private void Despawn(TargetDummy dummy)
        {
            if (dummy != null)
            {
                _spawned.Remove(dummy.gameObject);
                Destroy(dummy.gameObject);
            }
        }

        private Renderer BuiltInBarrel()
        {
            Transform barrel = FindRecursive(_player.transform, "GunBarrel");
            return barrel != null ? barrel.GetComponent<Renderer>() : null;
        }

        private static bool AnyRendererEnabled(ShooterGunModel model)
        {
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                if (r.enabled)
                {
                    return true;
                }
            }
            return false;
        }

        private void TrackDelta()
        {
            _maxDeltaTime = Mathf.Max(_maxDeltaTime, Time.deltaTime);
        }

        private IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return null;
                TrackDelta();
            }
        }

        private static Transform FindRecursive(Transform parent, string childName)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name == childName)
                {
                    return child;
                }
                Transform found = FindRecursive(child, childName);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }
    }
}

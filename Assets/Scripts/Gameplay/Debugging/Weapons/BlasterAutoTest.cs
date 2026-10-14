using System.Collections;
using System.Collections.Generic;
using SplatoonC.Core;
using SplatoonC.Core.Animation;
using SplatoonC.Core.Combat;
using SplatoonC.Core.Match;
using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 爆破槍(Blaster / Luna Blaster)驗收(試射場):執行期換裝 → 實測
    //   換裝外觀(槍模掛右手插槽、內建槍身隱藏、墨水部位隊伍色、持槍姿勢提示)/ bot 掛勾 /
    //   第一發預備 14F(點一下也出膛)、烏賊態出來 24F / 按住 50F 一發 / 出膛後 22F 不能潛、不能丟副武器(預備中也不能)/
    //   第 13F 空爆(≈ 9.9 m)/ 空爆爆風 70 → 50 線性(1.025 → 3.385 m)、外圈外無傷 / 直擊 125、直擊目標不吃爆風、旁人吃爆風 /
    //   撞地形爆風半徑 × 0.4234 / 被敵方 Ink Vac 吸走不爆炸不塗墨(D2)/ 爆風兩發擊倒、TTK 50F / 每發 10%、一槽 10 發、墨量鎖 57F / 開火走速 2.7 m/s /
    //   地面 0° 散布、跳射 ≤ 10° / 塗墨(腳前、沿路墨滴、空爆大圓、撞地小圓、牆)/ 預備蓄勢與後座力姿勢、潛墨隱藏槍模 /
    //   bot 掛勾驅動(瞄腳邊直擊、掩體後空爆)/ Luna(11F 空爆 ≈ 7.4 m、內圈 1.7、40F、7.5% → 13 發、26F、3.0 m/s)/ 每幀零配置。
    // 期望值全取自 S3 原始參數(S3BlasterParams);量測值來自真實遊戲路徑(意圖 → BlasterWeapon → InkBullet / Explosion → 假人 / 地面)。
    // 場地:試射場 x ≤ −4 的空地;牆面塗墨用 ClimbWall_Low(8, 1, −6;轉 90°,牆面朝 −x)。
    [AutoTest(Scene = AutoTestAttribute.TrainingScene, Timeout = 360f, Order = 230)]
    public sealed class BlasterAutoTest : AutoTestBase
    {
        public const string BlasterPath = "Weapons/Main_blaster";
        public const string LunaPath = "Weapons/Main_luna_blaster";

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

        private const float F = 1f / 60f;

        private readonly List<HitRecord> _hits = new List<HitRecord>();
        private readonly List<string> _sounds = new List<string>();
        private readonly List<GameObject> _spawned = new List<GameObject>();

        private GameObject _player;
        private Inkling _inkling;
        private WeaponLoadout _loadout;
        private PlayerInkTank _tank;
        private SquidController _squid;
        private CharacterController _controller;
        private CharacterVisual _visualComponent;
        private ThrowController _thrower;
        private Transform _visual;
        private Transform _muzzle;
        private ScriptedIntent _intent;
        private TestAim _aim;
        private GameObject _dummyTemplate;
        private PaintableSurface _ground;
        private BlasterConfig _blaster;
        private BlasterConfig _luna;
        private WeaponKit _baseKit;
        private float _maxDeltaTime;
        private int _exceptions;
        private string _firstException;

        private static S3BlasterParams B => S3BlasterParams.Blaster;
        private static S3BlasterParams L => S3BlasterParams.Luna;

        private BlasterWeapon Weapon => _loadout != null ? _loadout.Main as BlasterWeapon : null;

        public static void Run()
        {
            Launch<BlasterAutoTest>();
        }

        private void OnEnable()
        {
            GameAudio.Played += OnSoundPlayed;
            Application.logMessageReceived += OnLog;
        }

        private void OnDisable()
        {
            GameAudio.Played -= OnSoundPlayed;
            Application.logMessageReceived -= OnLog;
            CharacterVisual.MeasureAllocations = false;
            if (_inkling != null)
            {
                _inkling.DamageDealt -= OnDamageDealt;
            }
        }

        private void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Exception)
            {
                _exceptions++;
                _firstException ??= message;
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

        private IEnumerator Start()
        {
            _player = FindPlayer();
            _inkling = _player != null ? _player.GetComponent<Inkling>() : null;
            _loadout = _player != null ? _player.GetComponent<WeaponLoadout>() : null;
            _tank = _player != null ? _player.GetComponent<PlayerInkTank>() : null;
            _squid = _player != null ? _player.GetComponent<SquidController>() : null;
            _controller = _player != null ? _player.GetComponent<CharacterController>() : null;
            _visualComponent = _player != null ? _player.GetComponent<CharacterVisual>() : null;
            _thrower = _player != null ? _player.GetComponent<ThrowController>() : null;
            _visual = _player != null ? _player.transform.Find("Visual") : null;
            _muzzle = _visual != null ? _visual.Find("Muzzle") : null;
            _dummyTemplate = GameObject.Find("Dummy_Near");
            _blaster = Resources.Load<BlasterConfig>(BlasterPath);
            _luna = Resources.Load<BlasterConfig>(LunaPath);
            foreach (PaintableSurface surface in PaintableSurface.Active)
            {
                if (surface.name == "Ground")
                {
                    _ground = surface;
                }
            }
            if (_inkling == null || _loadout == null || _tank == null || _squid == null || _controller == null
                || _visual == null || _muzzle == null || _dummyTemplate == null || _ground == null || _thrower == null)
            {
                FailSetup("找不到 Player(Inkling/WeaponLoadout/PlayerInkTank/SquidController/ThrowController/Visual/Muzzle)、"
                          + "Dummy_Near 或 Ground");
                yield break;
            }
            if (_blaster == null || _luna == null)
            {
                FailSetup($"找不到 Resources/{BlasterPath} 或 {LunaPath}(先跑 Tools/SplatoonC/M7 Setup)");
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

            yield return EquipAndInspect(_blaster, B, false);
            if (Weapon == null)
            {
                Cleanup();
                Finish();
                yield break;
            }
            CharacterVisual.ResetCostCounters();
            CharacterVisual.MeasureAllocations = true;

            CheckBotHooks();
            yield return MeasureFirstShotDelay();
            yield return MeasureCadence(B, 4);
            yield return MeasurePostDelay(B);
            yield return MeasureAirBurst(B, false);
            yield return MeasureAirBurstSplash();
            yield return MeasureDirectHit();
            yield return MeasureTerrainHit();
            yield return MeasureVacuumAbsorb();
            yield return MeasureInkTank(B);
            yield return MeasureFiringMoveSpeed(B);
            yield return MeasureJumpSpread();
            yield return MeasurePaint();
            yield return MeasureWallPaint();
            yield return MeasureStanceAndVisibility();
            yield return MeasureBotEngagement();
            yield return MeasureCoverShot();

            // ── Luna Blaster ──
            yield return EquipAndInspect(_luna, L, true);
            if (Weapon != null)
            {
                yield return MeasureAirBurst(L, true);
                yield return MeasureLunaSplash();
                yield return MeasureCadence(L, 3);
                yield return MeasurePostDelay(L);
                yield return MeasureInkTank(L);
                yield return MeasureFiringMoveSpeed(L);
            }

            CharacterVisual.MeasureAllocations = false;
            Check("爆破槍持槍 / 蓄勢 / 後座力姿勢每幀零配置(CharacterVisual)",
                CharacterVisual.AllocatedBytes == 0 && CharacterVisual.TotalSamples > 100,
                $"配置 {CharacterVisual.AllocatedBytes} B / {CharacterVisual.TotalSamples} 幀");
            Check("爆破槍全程無例外", _exceptions == 0, $"例外 {_exceptions} 次 {_firstException}");
            Debug.Log($"[AUTOTEST] INFO 爆破槍量測期間最大幀時間 {_maxDeltaTime * 1000f:0.0} ms");
            Cleanup();
            Finish();
        }

        // ── 換裝、外觀 ──

        private IEnumerator EquipAndInspect(BlasterConfig config, S3BlasterParams s3, bool luna)
        {
            var kit = ScriptableObject.CreateInstance<WeaponKit>();
            kit.Setup(luna ? "luna_blaster" : "blaster", luna ? "新星爆破槍" : "爆破槍", s3.Name, "AutoTest 執行期組合", config,
                _baseKit.Sub, _baseKit.Special, luna ? 170f : 190f, luna ? WeightClass.Light : WeightClass.Mid,
                "射程", luna ? 19 : 27, "衝擊", 70, "射速", luna ? 30 : 20);
            yield return MoveTo(new Vector3(-12f, 0.1f, -20f));
            _loadout.Equip(kit);
            yield return Frames(2);

            BlasterWeapon weapon = Weapon;
            AttackClass wantClass = luna ? AttackClass.LunaBlaster : AttackClass.Blaster;
            Check($"{s3.Name} 換裝:主武器換成 BlasterWeapon(Class = Blaster、AttackClass = {wantClass})",
                weapon != null && weapon.Config == config && config.Class == WeaponClass.Blaster
                && config.AttackClass == wantClass && config.Bullet.AttackClass == wantClass
                && config.Explosion.AttackClass == wantClass,
                $"主武器={(_loadout.Main != null ? _loadout.Main.GetType().Name : "無")} 攻擊類別={config.AttackClass}");
            if (weapon == null)
            {
                yield break;
            }
            BlasterGunModel gun = weapon.Gun;
            bool socketed = gun != null && gun.Root != null && _visualComponent != null
                            && gun.Root.parent == _visualComponent.WeaponSocket;
            Check($"{s3.Name} 外觀:槍模掛右手 WeaponSocket、內建槍身隱藏、墨水部位套隊伍色、持槍姿勢提示接手",
                socketed && gun.BuiltInGunHidden && gun.InkPartCount >= 2 && gun.Visible && InkTinted(gun.Root)
                && _visualComponent.GunStanceActive,
                $"槍模={(gun != null)} 插槽={socketed} 墨水部位={(gun != null ? gun.InkPartCount : 0)} " +
                $"內建隱藏={(gun != null && gun.BuiltInGunHidden)} 姿勢提示={_visualComponent.GunStanceActive}");
            if (gun != null)
            {
                float length = GunLength(gun.Root);
                float want = luna ? 0.39f : 0.415f;
                Check($"{s3.Name} 槍模尺寸:全長約 {want:0.00} m(比 Splattershot 0.31 m 大一號)",
                    Mathf.Abs(length - want) < 0.05f, $"世界長度 {length:0.000} m");
            }
            Check($"{s3.Name} 設定:有效射程 {s3.EffectiveRange:0.00}、空爆 {s3.BurstFrame}F、直擊 {s3.DirectDamage}、" +
                  $"爆風 {s3.NearDamage}({s3.NearRadius})→ {s3.FarDamage}({s3.FarRadius})線性、撞地 ×{s3.TerrainRadiusRate}、" +
                  $"擊退 {s3.KnockbackAccel} / {s3.KnockbackDistance}、開火走速 {s3.MoveSpeedWhileFiring:0.0} m/s",
                Mathf.Abs(weapon.EffectiveRange - s3.EffectiveRange) < 0.01f && config.BurstFrame == s3.BurstFrame
                && Mathf.Abs(config.DirectDamage - s3.DirectDamage) < 1e-3f && config.Explosion.DamageLinear
                && Mathf.Abs(config.Explosion.NearRadius - s3.NearRadius) < 1e-4f
                && Mathf.Abs(config.Explosion.FarRadius - s3.FarRadius) < 1e-4f
                && Mathf.Abs(config.Bullet.TerrainHitExplosionScale - s3.TerrainRadiusRate) < 1e-4f
                && Mathf.Abs(config.Explosion.KnockbackAccel - s3.KnockbackAccel) < 1e-3f
                && Mathf.Abs(config.Explosion.KnockbackRange - s3.KnockbackDistance) < 1e-3f
                && Mathf.Abs(weapon.MoveSpeedWhileFiring - s3.MoveSpeedWhileFiring) < 1e-3f,
                $"射程 {weapon.EffectiveRange:0.00} 空爆 {config.BurstFrame}F 走速 {weapon.MoveSpeedWhileFiring:0.00}");
        }

        // ── bot 掛勾(BotBrain 交戰時呼叫的同一組方法)──

        private void CheckBotHooks()
        {
            BlasterWeapon weapon = Weapon;
            float burst = B.BurstDistance;
            var inRange = new BotCombatContext
            {
                Distance = burst, TargetVisible = true, Reacted = true, AimError = 0.5f, FireTolerance = 4f,
            };
            var outOfRange = inRange;
            outOfRange.Distance = burst + B.FarRadius + 0.5f;
            var hidden = inRange;
            hidden.TargetVisible = false;
            // 看得到:近距離瞄腳邊、空爆距離瞄胸口
            Vector3 feet = new Vector3(3f, 0f, 5f);
            var near = new BotAimContext
            {
                Eye = Vector3.up, AimPoint = feet + Vector3.up * 0.9f, TargetFeet = feet, TargetVisible = true, Distance = 3f,
            };
            var far = near;
            far.Distance = burst;
            float nearHeight = weapon.BotAimPoint(near).y - feet.y;
            float farHeight = weapon.BotAimPoint(far).y - feet.y;
            // 橫移中的目標:BotBrain 只算飛行時間的提前量,武器再補「預備 14F」期間目標走的距離
            var moving = near;
            moving.TargetVelocity = new Vector3(5f, 0f, 0f);
            float windupLead = weapon.BotAimPoint(moving).x - feet.x;
            Check($"bot 瞄準:橫移目標補預備 {B.FirstShotFrames}F 的提前量(5 m/s × {B.FirstShotSeconds:0.000} s)",
                !weapon.IsWindingUp && Mathf.Abs(windupLead - 5f * B.FirstShotSeconds) < 0.05f,
                $"提前 {windupLead:0.00} m(期望 {5f * B.FirstShotSeconds:0.00} m)");
            Check($"bot:空爆 + 外圈八成內開火、太遠不開、看不到且沒算出掩體空爆不開;交戰距離 0.85 × 空爆 {burst:0.00} m",
                weapon.BotAttackIntent(inRange) && !weapon.BotAttackIntent(outOfRange) && !weapon.BotAttackIntent(hidden)
                && Mathf.Abs(weapon.BotPreferredDistance - burst * 0.85f) < 0.05f,
                $"交戰距離 {weapon.BotPreferredDistance:0.00} 提前量彈速 {weapon.ProjectileSpeed:0.0} m/s");
            Check($"bot 瞄準:近距離瞄腳邊(小腿 {BlasterAim.FeetAimHeight} m)、空爆距離瞄胸口({BlasterAim.ChestAimHeight} m);" +
                  "塗地瞄空爆點更遠處、墨不夠一發不按",
                Mathf.Abs(nearHeight - BlasterAim.FeetAimHeight) < 1e-3f && Mathf.Abs(farHeight - BlasterAim.ChestAimHeight) < 1e-3f
                && weapon.BotPaintAimDistance > burst && weapon.BotPaintIntent(0.5f) && !weapon.BotPaintIntent(0.09f),
                $"近 {nearHeight:0.00} m 遠 {farHeight:0.00} m 塗地瞄 {weapon.BotPaintAimDistance:0.0} m");
        }

        // ── 第一發延遲:人形 14F(點一下也出膛)、剛從烏賊態出來 24F ──

        private IEnumerator MeasureFirstShotDelay()
        {
            BlasterWeapon weapon = Weapon;
            _tank.InfiniteInk = true;
            yield return MoveTo(new Vector3(-16f, 0.1f, -20f));
            AimAlong(Vector3.forward);
            yield return new WaitForSeconds(0.6f);
            float tolerance = Mathf.Max(1.2f * F, _maxDeltaTime * 1.2f);

            // 點一下(只按一幀):照樣出膛
            int before = weapon.ShotsFired;
            _intent.Attack = true;
            yield return null;
            TrackDelta();
            _intent.Attack = false;
            bool pendingAfterRelease = weapon.IsWindingUp;
            yield return WaitUntil(() => weapon.ShotsFired > before, 1f);
            float tapDelay = weapon.LastShotTime - weapon.TriggerStartTime;
            Check($"點一下扳機也出膛(按下 = 確定一發);人形預備 {B.FirstShotFrames}F",
                pendingAfterRelease && weapon.ShotsFired == before + 1
                && Mathf.Abs(tapDelay - B.FirstShotSeconds) <= tolerance,
                $"放開後仍預備={pendingAfterRelease} 按下到出膛 {tapDelay * 60f:0.0}F(容許 ±{tolerance * 60f:0.0}F)");
            yield return new WaitForSeconds(1f);

            // 烏賊態 → 放開烏賊同時按開火
            InkPainter.PaintSphere(_player.transform.position, 3f, 1f, _inkling.InkSource(false));
            yield return new WaitForSeconds(0.2f);
            _intent.Squid = true;
            yield return new WaitForSeconds(0.6f);
            bool wasSquid = _squid.IsSquid;
            before = weapon.ShotsFired;
            _intent.Squid = false;
            _intent.Attack = true;
            yield return WaitUntil(() => weapon.ShotsFired > before, 1.5f);
            float squidDelay = weapon.LastShotTime - weapon.LastSquidTime;
            _intent.Attack = false;
            Check($"第一發延遲(烏賊態出來){B.SquidFirstShotFrames}F(9 + 15 − 0)",
                wasSquid && weapon.ShotsFired > before && Mathf.Abs(squidDelay - B.SquidFirstShotSeconds) <= tolerance,
                $"烏賊={wasSquid} 解除烏賊到出膛 {squidDelay * 60f:0.0}F");
            yield return new WaitForSeconds(1f);
            _ground.ClearInk();
        }

        // ── 按住連射:每 RepeatFrame 一發;地面 0° 散布 ──

        private IEnumerator MeasureCadence(S3BlasterParams s3, int wantShots)
        {
            BlasterWeapon weapon = Weapon;
            _tank.InfiniteInk = true;
            weapon.SpreadMultiplier = 1f;
            yield return MoveTo(new Vector3(-12f, 0.1f, -20f));
            AimAlong(Vector3.forward);
            yield return new WaitForSeconds(0.9f);
            int start = weapon.ShotsFired;
            int soundsBefore = _sounds.Count;
            var actual = new List<float>();
            var scheduled = new List<float>();
            float maxYaw = 0f;
            int seen = start;
            _intent.Attack = true;
            float deadline = Time.time + s3.FirstShotSeconds + wantShots * s3.FireIntervalSeconds + 1f;
            while (weapon.ShotsFired - start < wantShots && Time.time < deadline)
            {
                AimAlong(Vector3.forward);
                yield return null;
                TrackDelta();
                if (weapon.ShotsFired != seen)
                {
                    seen = weapon.ShotsFired;
                    actual.Add(weapon.LastShotTime);
                    scheduled.Add(weapon.LastScheduledShotTime);
                    maxYaw = Mathf.Max(maxYaw, Mathf.Abs(weapon.LastYawOffsetDeg));
                }
            }
            _intent.Attack = false;
            int n = actual.Count;
            float interval = n > 1 ? (actual[n - 1] - actual[0]) / (n - 1) : 0f;
            float scheduledInterval = n > 1 ? (scheduled[n - 1] - scheduled[0]) / (n - 1) : 0f;
            float expected = s3.FireIntervalSeconds;
            Check($"{s3.Name} 按住連射:每 {s3.RepeatFrame}F 一發({60f / s3.RepeatFrame:0.0} 發/秒)",
                n >= wantShots && Mathf.Abs(interval - expected) < expected * 0.03f
                && Mathf.Abs(scheduledInterval - expected) < 1e-3f,
                $"{n} 發平均間隔 {interval * 60f:0.0}F(排定 {scheduledInterval * 60f:0.00}F,期望 {s3.RepeatFrame}F)");
            Check($"{s3.Name} 地面散布 0°(完全不散)", n >= wantShots && maxYaw < 1e-4f, $"最大偏角 {maxYaw:0.0000}°");
            bool heard = false;
            for (int i = soundsBefore; i < _sounds.Count; i++)
            {
                heard |= _sounds[i].StartsWith("shoot_", System.StringComparison.Ordinal);
            }
            Check($"{s3.Name} 射擊音效(未合成前退回 shoot_shooter)", heard, $"期間發聲 {_sounds.Count - soundsBefore} 次");
            yield return new WaitForSeconds(1f);
        }

        // ── 出膛後 PostDelay 不能變烏賊 / 丟副武器;預備中也不能 ──

        private IEnumerator MeasurePostDelay(S3BlasterParams s3)
        {
            BlasterWeapon weapon = Weapon;
            _tank.InfiniteInk = true;
            yield return MoveTo(new Vector3(-18f, 0.1f, -14f));
            AimAlong(new Vector3(0f, 0.3f, 1f));
            yield return new WaitForSeconds(1f);
            float tolerance = Mathf.Max(1.5f * F, _maxDeltaTime * 1.3f);

            // (a) 出膛那一幀起按住烏賊:要等 PostDelay
            int before = weapon.ShotsFired;
            _intent.Attack = true;
            yield return null;
            _intent.Attack = false;
            yield return WaitUntil(() => weapon.ShotsFired > before, 1f);
            _intent.Squid = true;
            float shot = weapon.LastShotTime;
            yield return WaitUntil(() => _squid.IsSquid, 1.5f);
            float squidDelay = Time.time - shot;
            bool becameSquid = _squid.IsSquid;
            _intent.Squid = false;
            Check($"{s3.Name} 出膛後 {s3.PostDelayFrames}F 才能變烏賊(PostDelayFrame_Blaster)",
                becameSquid && Mathf.Abs(squidDelay - s3.PostDelaySeconds) <= tolerance,
                $"出膛到變烏賊 {squidDelay * 60f:0.0}F(容許 ±{tolerance * 60f:0.0}F)");
            yield return new WaitForSeconds(1f);

            // (b) 出膛那一幀起按住副武器:要等 PostDelay 才開始瞄準
            before = weapon.ShotsFired;
            _intent.Attack = true;
            yield return null;
            _intent.Attack = false;
            yield return WaitUntil(() => weapon.ShotsFired > before, 1f);
            shot = weapon.LastShotTime;
            _intent.Sub = true;
            yield return WaitUntil(() => _thrower.IsAiming, 1.5f);
            float subDelay = Time.time - shot;
            bool aiming = _thrower.IsAiming;
            _intent.Sub = false;
            // 放開會丟出副武器:等它離手再清墨
            Check($"{s3.Name} 出膛後 {s3.PostDelayFrames}F 才能丟副武器(按住的話硬直過後開始瞄準)",
                aiming && Mathf.Abs(subDelay - s3.PostDelaySeconds) <= tolerance,
                $"出膛到開始瞄準 {subDelay * 60f:0.0}F");
            yield return new WaitForSeconds(1.2f);

            // (c) 預備中也不能變烏賊:按下 3F 後按住烏賊 → 要到 預備 + PostDelay 之後
            before = weapon.ShotsFired;
            _intent.Attack = true;
            yield return null;
            _intent.Attack = false;
            float commit = weapon.TriggerStartTime;
            yield return Frames(3);
            _intent.Squid = true;
            bool squidDuringWindup = false;
            while (weapon.ShotsFired == before && Time.time - commit < 1f)
            {
                yield return null;
                squidDuringWindup |= _squid.IsSquid;
            }
            yield return WaitUntil(() => _squid.IsSquid, 1.5f);
            float total = Time.time - commit;
            _intent.Squid = false;
            float want = s3.FirstShotSeconds + s3.PostDelaySeconds;
            Check($"{s3.Name} 預備中({s3.FirstShotFrames}F)也不能變烏賊:按下到能潛入 = {s3.FirstShotFrames} + {s3.PostDelayFrames}F",
                !squidDuringWindup && Mathf.Abs(total - want) <= tolerance * 2f,
                $"預備中變了烏賊={squidDuringWindup} 按下到變烏賊 {total * 60f:0.0}F(期望 {want * 60f:0}F)");
            yield return new WaitForSeconds(0.8f);

            // (d) 一般操作:按住等出膛、出膛後還多按 8F 才放開改按烏賊 → 不會被下一發綁住(下一發要到
            //     出膛 + RepeatFrame − 預備 = {Repeat − 14}F 才開始預備),出膛後 PostDelay 就潛入,也不會多出一發
            before = weapon.ShotsFired;
            _intent.Attack = true;
            yield return WaitUntil(() => weapon.ShotsFired > before, 1f);
            shot = weapon.LastShotTime;
            yield return WaitUntil(() => Time.time - shot >= 8f * F, 0.5f);
            _intent.Attack = false;
            _intent.Squid = true;
            yield return WaitUntil(() => _squid.IsSquid, 2f);
            float heldDelay = Time.time - shot;
            bool heldSquid = _squid.IsSquid;
            yield return new WaitForSeconds(s3.FireIntervalSeconds);
            int extra = weapon.ShotsFired - before - 1;
            _intent.Squid = false;
            Check($"{s3.Name} 按住射完、出膛後 8F 放開改潛:不會多確定下一發,出膛後 {s3.PostDelayFrames}F 就潛入" +
                  $"(下一發最早 {s3.RepeatFrame - s3.FirstShotFrames}F 後才開始預備)",
                heldSquid && extra == 0 && Mathf.Abs(heldDelay - s3.PostDelaySeconds) <= tolerance,
                $"出膛到變烏賊 {heldDelay * 60f:0.0}F、多出 {extra} 發");
            yield return new WaitForSeconds(0.8f);
            _ground.ClearInk();
        }

        // ── 空爆:第 BurstFrame 幀在空中爆(落點 = 同初速的三狀態彈道模擬)──

        private IEnumerator MeasureAirBurst(S3BlasterParams s3, bool luna)
        {
            BlasterWeapon weapon = Weapon;
            _tank.InfiniteInk = true;
            yield return MoveTo(new Vector3(-12f, 0.1f, -22f));
            AimAlong(Vector3.forward);
            yield return new WaitForSeconds(0.9f);
            yield return FireOnce(weapon);
            int bursts = weapon.BurstCount;
            yield return WaitUntil(() => weapon.BurstCount > bursts, 1.5f);
            InkBulletImpact burst = weapon.LastBurst;
            BulletMotion motion = s3.Motion;
            BulletState sim = BulletBallistics.Launch(weapon.LastShotOrigin, weapon.LastShotDirection, motion);
            for (int i = 0; i < s3.BurstFrame; i++)
            {
                BulletBallistics.Step(ref sim, motion);
            }
            Vector3 flat = burst.Point - weapon.LastShotOrigin;
            flat.y = 0f;
            float distance = flat.magnitude;
            Check($"{s3.Name} 第 {s3.BurstFrame}F 空爆(≈ S3 {s3.BurstDistance:0.00} m;三狀態步進 = 模擬落點)",
                burst.End == InkBulletEnd.AirBurst && burst.Frame == s3.BurstFrame && burst.Exploded
                && Vector3.Distance(burst.Point, sim.Position) < 0.05f && Mathf.Abs(distance - s3.BurstDistance) < 0.15f,
                $"{burst.End} 第 {burst.Frame}F 水平 {distance:0.00} m、與模擬差 {Vector3.Distance(burst.Point, sim.Position):0.000} m");
            yield return new WaitForSeconds(luna ? 0.7f : 0.9f);
            _ground.ClearInk();
        }

        // ── 空爆爆風:70(≤ 1.025)→ 50(3.385)線性,外圈外無傷;兩發擊倒、TTK 50F ──

        private IEnumerator MeasureAirBurstSplash()
        {
            BlasterWeapon weapon = Weapon;
            _tank.InfiniteInk = true;
            Vector3 stand = new Vector3(-14f, 0.1f, -24f);
            yield return MoveTo(stand);
            AimAlong(Vector3.forward);
            yield return new WaitForSeconds(0.9f);
            // 校準:先打一發看空爆點
            yield return FireOnce(weapon);
            int bursts = weapon.BurstCount;
            yield return WaitUntil(() => weapon.BurstCount > bursts, 1.5f);
            Vector3 p = weapon.LastBurst.Point;
            yield return new WaitForSeconds(0.9f);

            float body = BodyRadius();
            Vector3 right = Vector3.right;
            TargetDummy nearDummy = SpawnDummy(new Vector3(p.x + 0.8f + body, 0f, p.z));
            TargetDummy midDummy = SpawnDummy(new Vector3(p.x - 2.4f - body, 0f, p.z));
            TargetDummy farDummy = SpawnDummy(new Vector3(p.x, 0f, p.z + 3.6f + body));
            yield return Frames(3);
            _hits.Clear();
            yield return MoveTo(stand);
            AimAlong(Vector3.forward);
            yield return new WaitForSeconds(0.9f);

            // 按住兩發:第一發各吃一次爆風,第二發把兩個假人都擊倒
            int start = weapon.ShotsFired;
            _intent.Attack = true;
            float deadline = Time.time + 3f;
            float firstBurstAt = -1f;
            Vector3 firstBurst = Vector3.zero;
            int seenBursts = weapon.BurstCount;
            while (Time.time < deadline && (nearDummy.IsAlive || midDummy.IsAlive))
            {
                AimAlong(Vector3.forward);
                yield return null;
                TrackDelta();
                if (weapon.BurstCount != seenBursts && firstBurstAt < 0f)
                {
                    firstBurstAt = Time.time;
                    firstBurst = weapon.LastBurst.Point;
                }
                seenBursts = weapon.BurstCount;
                if (weapon.ShotsFired - start >= 2 && Time.time - weapon.LastShotTime > 0.4f)
                {
                    break;
                }
            }
            _intent.Attack = false;
            yield return new WaitForSeconds(0.3f);

            float nearExpected = Expected(firstBurst, nearDummy, false);
            float midExpected = Expected(firstBurst, midDummy, false);
            float nearFirst = FirstHit(nearDummy, out float nearFirstTime, out float nearKill);
            float midFirst = FirstHit(midDummy, out _, out float midKill);
            int farHits = HitCount(farDummy);
            float nearDistance = Vector3.Distance(firstBurst, nearDummy.ClosestPoint(firstBurst));
            float midDistance = Vector3.Distance(firstBurst, midDummy.ClosestPoint(firstBurst));
            Check($"空爆爆風線性:{B.NearDamage}(≤ {B.NearRadius} m)→ {B.FarDamage}({B.FarRadius} m);外圈外無傷",
                Mathf.Abs(nearFirst - nearExpected) < 0.11f && Mathf.Abs(nearExpected - 70f) < 1e-3f
                && Mathf.Abs(midFirst - midExpected) < 0.11f && midExpected > 50f && midExpected < 70f && farHits == 0,
                $"{nearDistance:0.00} m → {nearFirst:0.0}(期望 {nearExpected:0.0});{midDistance:0.00} m → {midFirst:0.0}" +
                $"(期望 {midExpected:0.0});{3.6f:0.0} m 外圈外命中 {farHits} 次");
            float ttk = nearKill - nearFirstTime;
            float tolerance = Mathf.Max(0.034f, _maxDeltaTime * 1.5f);
            Check($"爆風兩發擊倒、TTK {B.SplashTtkFrames()}F({B.FireIntervalSeconds:0.000} s)",
                nearKill > 0f && midKill > 0f && Mathf.Abs(ttk - B.SplashTtkFrames() / 60f) <= tolerance,
                $"近假人擊倒={nearKill > 0f} 中假人擊倒={midKill > 0f} 首發到擊倒 {ttk:0.000} s(容許 ±{tolerance:0.000})");
            Despawn(nearDummy);
            Despawn(midDummy);
            Despawn(farDummy);
            yield return new WaitForSeconds(0.3f);
            _ground.ClearInk();
        }

        // ── 直擊:125(一擊必殺、不衰減)、直擊目標不吃同一發的爆風、旁人吃爆風 ──

        private IEnumerator MeasureDirectHit()
        {
            BlasterWeapon weapon = Weapon;
            _tank.InfiniteInk = true;
            yield return MoveTo(new Vector3(-8f, 0.1f, -22f));
            AimAlong(Vector3.forward);
            yield return new WaitForSeconds(0.9f);
            float body = BodyRadius();
            TargetDummy direct = SpawnDummy(MuzzleFlat() + Vector3.forward * 5f);
            TargetDummy bystander = SpawnDummy(MuzzleFlat() + Vector3.forward * 5f + Vector3.right * (1.3f + body));
            yield return Frames(3);
            _hits.Clear();
            int directHits = weapon.DirectHits;
            int soundsBefore = _sounds.Count;
            AimAt(DummyCenter(direct));
            yield return FireOnce(weapon);
            int bursts = weapon.BurstCount;
            yield return WaitUntil(() => weapon.BurstCount > bursts, 1.5f);
            yield return Frames(2);
            InkBulletImpact hit = weapon.LastBurst;
            float directDamage = FirstHit(direct, out _, out float directKill);
            int directCount = HitCount(direct);
            float byExpected = Expected(hit.Point, bystander, false);
            float byDamage = FirstHit(bystander, out _, out _);
            bool heardBurst = false;
            for (int i = soundsBefore; i < _sounds.Count; i++)
            {
                heardBurst |= _sounds[i] == "blaster_burst" || _sounds[i] == "bomb_explode";
            }
            Check($"直擊 {B.DirectDamage}(一擊必殺、不衰減);直擊目標只吃一次(不疊爆風);旁人吃爆風;爆炸音效",
                hit.End == InkBulletEnd.Target && ReferenceEquals(hit.Target, direct) && hit.Exploded
                && Mathf.Abs(directDamage - B.DirectDamage) < 1e-3f && directKill > 0f && directCount == 1
                && weapon.DirectHits == directHits + 1 && Mathf.Abs(byDamage - byExpected) < 0.11f && byExpected >= 50f
                && heardBurst,
                $"{hit.End} 第 {hit.Frame}F 直擊 {directDamage:0.0}×{directCount} 擊倒={directKill > 0f};旁人 {byDamage:0.0}" +
                $"(期望 {byExpected:0.0});爆炸音={heardBurst}");
            Despawn(direct);
            Despawn(bystander);
            yield return new WaitForSeconds(0.3f);
            _ground.ClearInk();
        }

        // ── 撞地形:爆風半徑 × 0.4234(內圈 0.434、外圈 1.433)──

        private IEnumerator MeasureTerrainHit()
        {
            BlasterWeapon weapon = Weapon;
            _tank.InfiniteInk = true;
            yield return MoveTo(new Vector3(-18f, 0.1f, -22f));
            AimAlong(Vector3.forward);
            yield return new WaitForSeconds(0.9f);
            float body = BodyRadius();
            Vector3 ground = MuzzleFlat() + Vector3.forward * 5f;
            TargetDummy inside = SpawnDummy(ground + Vector3.right * (1.0f + body));
            TargetDummy outside = SpawnDummy(ground - Vector3.right * (1.9f + body));
            yield return Frames(3);
            _hits.Clear();
            AimAt(ground);
            yield return FireOnce(weapon);
            int bursts = weapon.BurstCount;
            yield return WaitUntil(() => weapon.BurstCount > bursts, 1.5f);
            yield return Frames(2);
            InkBulletImpact hit = weapon.LastBurst;
            // 撞地形的爆心 = 命中點沿法線抬 0.05 m(InkBullet;事件回報的是命中點本身)
            Vector3 center = hit.Point + hit.Normal * 0.05f;
            float insideExpected = Expected(center, inside, true);
            float insideDamage = FirstHit(inside, out _, out _);
            int outsideHits = HitCount(outside);
            float outsideDistance = Vector3.Distance(center, outside.ClosestPoint(center));
            float wouldBe = B.ExplosionDamage(outsideDistance, false);
            Check($"撞地形:爆風半徑 × {B.TerrainRadiusRate}(外圈 {B.FarRadius * B.TerrainRadiusRate:0.00} m)",
                hit.End == InkBulletEnd.Terrain && hit.Exploded && Mathf.Abs(insideDamage - insideExpected) < 0.11f
                && insideExpected >= 50f && outsideHits == 0 && wouldBe > 50f,
                $"{hit.End} 內側 {insideDamage:0.0}(期望 {insideExpected:0.0});{outsideDistance:0.00} m 外側命中 {outsideHits} 次" +
                $"(空爆的話會吃 {wouldBe:0.0})");
            Despawn(inside);
            Despawn(outside);
            yield return new WaitForSeconds(0.3f);
            _ground.ClearInk();
        }

        // ── 跨單元(D2 Ink Vac):彈被敵方吸引漩渦吸走 → 不爆炸、不塗墨、不算一次爆發 ──

        private IEnumerator MeasureVacuumAbsorb()
        {
            BlasterWeapon weapon = Weapon;
            _tank.InfiniteInk = true;
            yield return MoveTo(new Vector3(-18f, 0.1f, -22f));
            Vector3 forward = Vector3.forward;
            AimAlong(forward);
            yield return new WaitForSeconds(0.9f);
            _ground.ClearInk();
            // 敵隊的吸引漩渦(不需要真的 Ink Vac 使用者):錐從前方 8 m 朝玩家伸 3 m → 彈在 5 m 左右被吸走(9F 直進段內)
            var vacuum = new InkVacuum(null)
            {
                Apex = _muzzle.position + forward * 8f,
                Axis = -forward,
                Length = 3f,
            };
            vacuum.Enable(TeamUtil.Opponent(_inkling.Team));
            int bursts = weapon.BurstCount;
            int absorbed = weapon.AbsorbedCount;
            AimAlong(forward);
            yield return FireOnce(weapon);
            yield return WaitUntil(() => vacuum.AbsorbedHits > 0, 1f);
            // 等過原本會空爆的第 13F,確認沒有補爆
            yield return new WaitForSeconds(0.4f);
            vacuum.Disable();
            // 吸入點兩側 2.8 m:錯誤地當成空爆時,正下方地面大圓(r 3.48)會塗到;沿路墨滴(r 1.77)與腳前那片(r 2.66、腳前)塗不到
            Vector3 at = vacuum.LastAbsorbPoint;
            Vector3 side = Vector3.Cross(Vector3.up, forward).normalized * 2.8f;
            bool sideInk = Owned(at + side) || Owned(at - side);
            Check("跨單元(D2 Ink Vac):彈被敵方吸引漩渦吸走 → 不爆炸、不塗墨(不算一次爆發)",
                vacuum.AbsorbedHits == 1 && weapon.AbsorbedCount == absorbed + 1 && weapon.BurstCount == bursts && !sideInk,
                $"吸走 {vacuum.AbsorbedHits} 發(容量 +{vacuum.Absorbed:0}、距槍口 {Vector3.Distance(at, _muzzle.position):0.0} m);" +
                $"爆發 +{weapon.BurstCount - bursts};記為吸走 +{weapon.AbsorbedCount - absorbed};兩側 2.8 m 塗到={sideInk}");
            yield return new WaitForSeconds(0.2f);
            _ground.ClearInk();
        }

        // ── 墨槽:每發耗墨、一槽發數、墨不夠乾扣扳機、墨量鎖 ──

        private IEnumerator MeasureInkTank(S3BlasterParams s3)
        {
            BlasterWeapon weapon = Weapon;
            yield return MoveTo(new Vector3(-20f, 0.1f, -22f));
            AimAlong(new Vector3(0f, 0.25f, 1f));
            _tank.InfiniteInk = false;
            _tank.RefillFull();
            yield return new WaitForSeconds(0.9f);
            int start = weapon.ShotsFired;
            int dryStart = weapon.DryFires;
            float full = _tank.Normalized;
            float afterFirst = -1f;
            _intent.Attack = true;
            float deadline = Time.time + s3.FirstShotSeconds + (s3.ShotsPerTank + 2) * s3.FireIntervalSeconds + 1f;
            while (Time.time < deadline && weapon.DryFires == dryStart)
            {
                AimAlong(new Vector3(0f, 0.25f, 1f));
                yield return null;
                TrackDelta();
                if (afterFirst < 0f && weapon.ShotsFired - start == 1)
                {
                    afterFirst = _tank.Normalized;
                }
            }
            _intent.Attack = false;
            int shots = weapon.ShotsFired - start;
            float lastShot = weapon.LastShotTime;
            float perShot = afterFirst >= 0f ? full - afterFirst : -1f;
            Check($"{s3.Name} 每發耗墨 {s3.InkConsume * 100f:0.0}%", Mathf.Abs(perShot - s3.InkConsume) < 1e-3f,
                $"實測 {perShot * 100f:0.00}%");
            Check($"{s3.Name} 一槽 {s3.ShotsPerTank} 發,再按是乾扣扳機(不出彈)",
                shots == s3.ShotsPerTank && weapon.DryFires > dryStart,
                $"出膛 {shots} 發、乾扣 {weapon.DryFires - dryStart} 次、剩墨 {_tank.Normalized * 100f:0.0}%");
            // 墨量鎖從最後一次扣扳機起算(乾扣也上鎖;迴圈在乾扣那一幀結束,此刻 = 乾扣的時刻)
            float lockFrom = Time.time;
            float released = _tank.Normalized;
            float unlockedAt = -1f;
            float lockEnd = Time.time + s3.InkLockSeconds + 1f;
            while (Time.time < lockEnd)
            {
                yield return null;
                if (_tank.Normalized > released + 1e-5f)
                {
                    unlockedAt = Time.time;
                    break;
                }
            }
            float lockMeasured = unlockedAt - lockFrom;
            float tolerance = Mathf.Max(0.04f, _maxDeltaTime * 2f);
            Check($"{s3.Name} 墨量鎖 {s3.InkRecoverStop}F = {s3.InkLockSeconds:0.000} s(最後一次扣扳機起算)",
                unlockedAt > 0f && Mathf.Abs(lockMeasured - s3.InkLockSeconds) <= tolerance,
                $"最後一次扣扳機後 {lockMeasured:0.000} s 開始回墨(容許 ±{tolerance:0.000};最後一發出膛於 " +
                $"{lockFrom - lastShot:0.00} s 前)");
            _tank.RefillFull();
            _tank.InfiniteInk = true;
            yield return new WaitForSeconds(0.3f);
            _ground.ClearInk();
        }

        // ── 開火走速(預備、按住、硬直期間)──

        private IEnumerator MeasureFiringMoveSpeed(S3BlasterParams s3)
        {
            _tank.InfiniteInk = true;
            yield return MoveTo(new Vector3(-22f, 0.1f, -20f));
            AimAlong(new Vector3(0f, 0.2f, 1f));
            yield return new WaitForSeconds(0.9f);
            _intent.Attack = true;
            _intent.Move = new Vector2(0f, 1f);
            float t0 = Time.time;
            while (Time.time - t0 < 0.5f)
            {
                AimAlong(new Vector3(0f, 0.2f, 1f));
                yield return null;
            }
            Vector3 p0 = _player.transform.position;
            float s0 = Time.time;
            while (Time.time - s0 < 1f)
            {
                AimAlong(new Vector3(0f, 0.2f, 1f));
                yield return null;
            }
            Vector3 p1 = _player.transform.position;
            float elapsed = Time.time - s0;
            _intent.Attack = false;
            _intent.Move = Vector2.zero;
            p1.y = p0.y;
            float speed = Vector3.Distance(p0, p1) / Mathf.Max(elapsed, 1e-3f);
            Check($"{s3.Name} 開火走速 {s3.MoveSpeed} u/F = {s3.MoveSpeedWhileFiring:0.00} m/s",
                Mathf.Abs(speed - s3.MoveSpeedWhileFiring) < s3.MoveSpeedWhileFiring * 0.05f, $"實測 {speed:0.00} m/s");
            yield return new WaitForSeconds(1f);
            _ground.ClearInk();
        }

        // ── 跳射散布:空中偏向 b 固定 0.5、偏角上限 10°(起跳 25F 後收、70F 回到 0°);地面恆 0°(上一段)──
        // 每一跳在離地後點一下扳機 → 預備 14F 後在空中出膛(偏角上限仍是 10°);|偏角| ≤ 10° 為確定上限,
        // b 0.5 → 偏角 = 10° × 均勻亂數,每發 > 0.5° 的機率 95%,6 發中少於 3 發的機率 < 1e-4。
        private IEnumerator MeasureJumpSpread()
        {
            BlasterWeapon weapon = Weapon;
            _tank.InfiniteInk = true;
            weapon.SpreadMultiplier = 1f;
            yield return MoveTo(new Vector3(-12f, 0.1f, -14f));
            AimAlong(Vector3.forward);
            yield return new WaitForSeconds(0.9f);
            int airShots = 0;
            int over = 0;
            float maxAir = 0f;
            float maxRatio = 0f;
            for (int jump = 0; jump < 8 && airShots < 6; jump++)
            {
                yield return WaitUntil(() => _controller.isGrounded, 2f);
                yield return new WaitForSeconds(0.5f);
                _intent.PressJump();
                float takeoff = -1f;
                float deadline = Time.time + 1f;
                while (takeoff < 0f && Time.time < deadline)
                {
                    AimAlong(Vector3.forward);
                    yield return null;
                    TrackDelta();
                    if (!_controller.isGrounded)
                    {
                        takeoff = Time.time;
                    }
                }
                int before = weapon.ShotsFired;
                _intent.Attack = true;
                yield return null;
                _intent.Attack = false;
                yield return WaitUntil(() => weapon.ShotsFired > before, 1f);
                if (weapon.ShotsFired > before && weapon.LastShotAirborne && takeoff > 0f)
                {
                    float yaw = Mathf.Abs(weapon.LastYawOffsetDeg);
                    float frames = (weapon.LastShotTime - takeoff) * 60f;
                    float shrink = Mathf.Clamp01((frames - 1.5f - B.JumpBiasDecreaseStartFrame)
                                                 / (B.JumpBiasEndFrame - B.JumpBiasDecreaseStartFrame));
                    float bound = Mathf.Lerp(B.JumpSwerve, B.StandSwerve, shrink);
                    airShots++;
                    maxAir = Mathf.Max(maxAir, yaw);
                    over += yaw > 0.5f ? 1 : 0;
                    maxRatio = Mathf.Max(maxRatio, bound > 1e-4f ? yaw / bound : yaw * 1e4f);
                }
                yield return WaitUntil(() => _controller.isGrounded, 2f);
            }
            Check($"跳射散布:空中偏角上限 {B.JumpSwerve}°(b 固定 {B.JumpBiasMax}),多數 > 0.5°(地面恆 0°)",
                airShots >= 5 && maxAir <= B.JumpSwerve + 1e-3f && over >= 3 && maxRatio <= 1f + 1e-3f,
                $"空中 {airShots} 發、最大偏角 {maxAir:0.00}°、> 0.5° 的 {over} 發、|偏角| / 當下上限 最大 {maxRatio:0.00}");
            yield return new WaitForSeconds(1f);
            _ground.ClearInk();
        }

        // ── 塗墨:腳前、沿路墨滴、空爆正下方大圓、撞地形小圓 ──

        private IEnumerator MeasurePaint()
        {
            BlasterWeapon weapon = Weapon;
            _tank.InfiniteInk = true;
            weapon.SpreadMultiplier = 0f;
            _ground.ClearInk();
            yield return MoveTo(new Vector3(-16f, 0.1f, -24f));
            AimAlong(Vector3.forward);
            yield return new WaitForSeconds(0.9f);
            _ground.ClearInk();
            float turfBefore = _inkling.TurfInkedArea;
            int nearestBefore = weapon.NearestPaintCount;
            yield return FireOnce(weapon);
            int bursts = weapon.BurstCount;
            yield return WaitUntil(() => weapon.BurstCount > bursts, 1.5f);
            yield return Frames(2);
            // 沿路墨滴有落下動畫(InkBulletDrip):等最後一滴著地
            yield return WaitUntil(() => InkBulletDrip.ActiveCount == 0, 2f);
            float airTurf = _inkling.TurfInkedArea - turfBefore;
            Vector3 feet = weapon.LastNearestPaintPoint;
            Vector3 origin = MuzzleFlat();
            int dropletsOwned = 0;
            int dropletsWanted = B.DropletCount(B.BurstDistance);
            for (int k = 1; k <= dropletsWanted; k++)
            {
                dropletsOwned += Owned(origin + Vector3.forward * (k * B.SpawnBetweenLength)) ? 1 : 0;
            }
            bool gapsFilled = Owned(origin + Vector3.forward * 2.25f) && Owned(origin + Vector3.forward * 5.25f);
            Vector3 drop = weapon.LastDropPaintPoint;
            float dropRadius = weapon.LastDropPaintRadius;
            bool dropInside = Owned(drop + Vector3.right * dropRadius * 0.8f) && Owned(drop - Vector3.right * dropRadius * 0.8f);
            bool dropOutside = !Owned(drop + Vector3.right * dropRadius * 1.25f) && !Owned(drop - Vector3.right * dropRadius * 1.25f);
            Check($"塗墨:腳前墨滴(每發,半徑 {B.NearestPaintRadius:0.00})、沿路每 {B.SpawnBetweenLength} m 一滴({dropletsWanted} 滴連成一線)",
                weapon.NearestPaintCount == nearestBefore + 1 && Owned(feet) && Owned(feet + Vector3.right * B.NearestPaintRadius * 0.7f)
                && dropletsOwned == dropletsWanted && gapsFilled,
                $"腳前={Owned(feet)} 墨滴 {dropletsOwned}/{dropletsWanted} 滴間連續={gapsFilled}");
            Check($"塗墨:空爆的墨落到正下方地面(半徑 {B.BurstDropPaintRadius} [EST])",
                weapon.LastBurst.End == InkBulletEnd.AirBurst && Mathf.Abs(dropRadius - B.BurstDropPaintRadius) < 1e-3f
                && dropInside && dropOutside,
                $"{weapon.LastBurst.End} 大圓 r={dropRadius:0.00}:0.8r 內有墨={dropInside} 1.25r 外無墨={dropOutside}");

            // 撞地形:小一號(SplashDropPaintShotColHitRadius 2.5)
            _ground.ClearInk();
            yield return Frames(2);
            float turfBefore2 = _inkling.TurfInkedArea;
            Vector3 ground = MuzzleFlat() + Vector3.forward * 5f;
            AimAt(ground);
            yield return new WaitForSeconds(0.9f);
            AimAt(ground);
            yield return FireOnce(weapon);
            bursts = weapon.BurstCount;
            yield return WaitUntil(() => weapon.BurstCount > bursts, 1.5f);
            yield return Frames(2);
            yield return WaitUntil(() => InkBulletDrip.ActiveCount == 0, 2f);
            float terrainTurf = _inkling.TurfInkedArea - turfBefore2;
            Vector3 drop2 = weapon.LastDropPaintPoint;
            float r2 = weapon.LastDropPaintRadius;
            bool inside2 = Owned(drop2 + Vector3.right * r2 * 0.8f) && Owned(drop2 - Vector3.right * r2 * 0.8f);
            bool outside2 = !Owned(drop2 + Vector3.right * r2 * 1.3f) && !Owned(drop2 - Vector3.right * r2 * 1.3f);
            Check($"塗墨:撞地形的爆炸塗一圈較小的(半徑 {B.TerrainDropPaintRadius},S3 ShotColHit)",
                weapon.LastBurst.End == InkBulletEnd.Terrain && Mathf.Abs(r2 - B.TerrainDropPaintRadius) < 1e-3f
                && inside2 && outside2 && airTurf > terrainTurf,
                $"{weapon.LastBurst.End} r={r2:0.00} 內={inside2} 外={outside2};單發塗地 空爆 {TurfPoints.FromArea(airTurf):0.0}p / " +
                $"撞地 {TurfPoints.FromArea(terrainTurf):0.0}p");
            Debug.Log($"[AUTOTEST] INFO Blaster 單發塗地:空爆 {airTurf:0.0} m²、撞地 {terrainTurf:0.0} m²;" +
                      $"一槽 10 發(空爆)≈ {TurfPoints.FromArea(airTurf * B.ShotsPerTank):0}p");
            weapon.SpreadMultiplier = 1f;
            _ground.ClearInk();
            yield return new WaitForSeconds(0.3f);
        }

        // ── 牆面:打到可塗牆的爆炸把牆塗上(ClimbWall_Low 轉了 90°:牆面朝 −x,x = 7.5、z −8 ~ −4、高 0 ~ 2)──

        private IEnumerator MeasureWallPaint()
        {
            BlasterWeapon weapon = Weapon;
            GameObject wallGo = GameObject.Find("ClimbWall_Low");
            var wall = wallGo != null ? wallGo.GetComponent<PaintableSurface>() : null;
            if (wall == null)
            {
                Check("牆面塗墨(ClimbWall_Low)", false, "找不到 ClimbWall_Low 的 PaintableSurface");
                yield break;
            }
            wall.ClearInk();
            _tank.InfiniteInk = true;
            yield return MoveTo(new Vector3(2f, 0.1f, -5.5f));
            AimAlong(Vector3.right);
            yield return new WaitForSeconds(0.9f);
            AimAlong(Vector3.right);
            yield return FireOnce(weapon);
            int bursts = weapon.BurstCount;
            yield return WaitUntil(() => weapon.BurstCount > bursts, 1.5f);
            yield return Frames(2);
            InkBulletImpact hit = weapon.LastBurst;
            byte owner = wall.SampleOwnership(hit.Point - hit.Normal * 0.05f, hit.Normal);
            byte above = wall.SampleOwnership(hit.Point - hit.Normal * 0.05f + Vector3.up * 0.6f, hit.Normal);
            Check("牆面:爆破槍打到牆 → 撞地形爆炸,牆面塗上(球形塗墨)",
                hit.End == InkBulletEnd.Terrain && Mathf.Abs(hit.Normal.y) < 0.3f && owner == (byte)_inkling.Team
                && above == (byte)_inkling.Team,
                $"{hit.End} 法線 {hit.Normal} 命中點={owner} 上 0.6 m={above}(隊伍 {(byte)_inkling.Team})");
            wall.ClearInk();
            _ground.ClearInk();
        }

        // ── 姿勢:預備時蓄勢、出膛後座力大於射擊槍一發;潛墨時槍模隱藏 ──

        private IEnumerator MeasureStanceAndVisibility()
        {
            BlasterWeapon weapon = Weapon;
            _tank.InfiniteInk = true;
            yield return MoveTo(new Vector3(-10f, 0.1f, -10f));
            AimAlong(new Vector3(0f, 0.2f, 1f));
            yield return new WaitForSeconds(1.2f);
            int before = weapon.ShotsFired;
            _intent.Attack = true;
            yield return null;
            _intent.Attack = false;
            float maxWindup = 0f;
            float aimWeight = 0f;
            while (weapon.ShotsFired == before && Time.time - weapon.TriggerStartTime < 1f)
            {
                AimAlong(new Vector3(0f, 0.2f, 1f));
                yield return null;
                maxWindup = Mathf.Max(maxWindup, _visualComponent.GunStanceWindup);
                aimWeight = Mathf.Max(aimWeight, _visualComponent.AimWeight);
            }
            float peak = 0f;
            float end = Time.time + 0.3f;
            while (Time.time < end)
            {
                AimAlong(new Vector3(0f, 0.2f, 1f));
                yield return null;
                peak = Mathf.Max(peak, _visualComponent.Recoil);
            }
            float reference = ShooterRecoilPeak();
            Check("姿勢:預備中舉槍蓄勢(槍往後收)、出膛後座力 ≥ 射擊槍一發的 1.6 倍",
                maxWindup > 0.5f && aimWeight > 0.9f && peak >= reference * 1.6f,
                $"蓄勢最大 {maxWindup:0.00} 舉槍 {aimWeight:0.00} 後座力峰值 {peak:0.00}(射擊槍一發 {reference:0.00})");

            InkPainter.PaintSphere(_player.transform.position, 3f, 1f, _inkling.InkSource(false));
            yield return new WaitForSeconds(0.9f);
            _intent.Squid = true;
            yield return new WaitForSeconds(0.8f);
            BlasterGunModel gun = weapon.Gun;
            bool hidden = gun != null && !gun.Visible && _squid.IsSquid;
            _intent.Squid = false;
            yield return new WaitForSeconds(0.8f);
            bool shown = gun != null && gun.Visible;
            Check("潛墨時槍模跟著人形一起消失、浮出後出現", hidden && shown, $"潛墨隱藏={hidden} 浮出顯示={shown}");
            _ground.ClearInk();
        }

        // 射擊槍一發的後座力峰值(CharacterVisual 同一組彈簧,速度 26)
        private static float ShooterRecoilPeak()
        {
            float x = 0f;
            float v = 26f;
            float peak = 0f;
            for (int i = 0; i < 30; i++)
            {
                SpringMotion.Step(ref x, ref v, 0f, 520f, 34f, F);
                peak = Mathf.Max(peak, x);
            }
            return peak;
        }

        // ── bot 掛勾驅動(模擬 BotBrain 交戰):瞄準點由武器決定(近距離瞄腳邊)、依 BotAttackIntent 按扳機,要打得中 ──

        private IEnumerator MeasureBotEngagement()
        {
            BlasterWeapon weapon = Weapon;
            _tank.InfiniteInk = true;
            yield return MoveTo(new Vector3(-16f, 0.1f, -20f));
            AimAlong(Vector3.forward);
            yield return new WaitForSeconds(0.9f);
            TargetDummy dummy = SpawnDummy(MuzzleFlat() + Vector3.forward * 4f);
            yield return Frames(3);
            _hits.Clear();
            int start = weapon.ShotsFired;
            int directStart = weapon.DirectHits;
            int held = 0;
            float end = Time.time + 2.2f;
            while (Time.time < end)
            {
                Vector3 feet = dummy.transform.position;
                float distance = Vector3.Distance(_muzzle.position, feet + Vector3.up * 0.9f);
                var aimContext = new BotAimContext
                {
                    Eye = _muzzle.position, AimPoint = feet + Vector3.up * 0.9f, TargetFeet = feet, TargetVisible = true,
                    Distance = distance,
                };
                AimAt(weapon.BotAimPoint(aimContext));
                var context = new BotCombatContext
                {
                    Distance = distance, TargetVisible = true, Reacted = true, AimError = 0f, FireTolerance = 4f,
                };
                _intent.Attack = weapon.BotAttackIntent(context);
                held += _intent.Attack ? 1 : 0;
                yield return null;
                TrackDelta();
            }
            _intent.Attack = false;
            yield return new WaitForSeconds(0.4f);
            int hits = HitCount(dummy);
            Check("bot 掛勾驅動:射程內按住開火、瞄腳邊(瞄準線先進入小腿的膨脹膠囊,才降到撞地高度)直擊 125",
                held > 0 && weapon.ShotsFired > start && hits > 0 && weapon.DirectHits > directStart,
                $"按住 {held} 幀、出膛 {weapon.ShotsFired - start} 發、命中 {hits} 次、直擊 {weapon.DirectHits - directStart} 次");
            Despawn(dummy);
            yield return new WaitForSeconds(0.3f);
            _ground.ClearInk();
        }

        // ── bot 掩體空爆:目標躲在 1.5 m 高的掩體後(看不到),算出越過掩體的瞄準點照樣開火,爆風炸到目標 ──

        private IEnumerator MeasureCoverShot()
        {
            BlasterWeapon weapon = Weapon;
            _tank.InfiniteInk = true;
            yield return MoveTo(new Vector3(-12f, 0.1f, -24f));
            AimAlong(Vector3.forward);
            yield return new WaitForSeconds(0.9f);
            Vector3 origin = MuzzleFlat();
            var cover = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cover.name = "BlasterTest_Cover";
            cover.transform.position = origin + Vector3.forward * 5f + Vector3.up * 0.75f;
            cover.transform.localScale = new Vector3(3f, 1.5f, 0.4f);
            _spawned.Add(cover);
            TargetDummy dummy = SpawnDummy(origin + Vector3.forward * 10.5f);
            Physics.SyncTransforms();
            yield return Frames(3);
            _hits.Clear();
            Vector3 feet = dummy.transform.position;
            bool hiddenFromChest = Physics.Linecast(_muzzle.position, feet + Vector3.up * 0.9f, CombatMasks.Terrain,
                QueryTriggerInteraction.Ignore);
            var aimContext = new BotAimContext
            {
                Eye = _muzzle.position, AimPoint = feet + Vector3.up * 0.9f, TargetFeet = feet, TargetVisible = false,
                Distance = Vector3.Distance(_muzzle.position, feet), SecondsSinceSeen = 0.5f,
            };
            Vector3 aimPoint = weapon.BotAimPoint(aimContext);
            bool planned = weapon.CoverShotPlanned;
            var context = new BotCombatContext
            {
                Distance = aimContext.Distance, TargetVisible = false, Reacted = true, AimError = 0f, FireTolerance = 4f,
            };
            bool wantsFire = weapon.BotAttackIntent(context);
            int start = weapon.ShotsFired;
            int bursts = weapon.BurstCount;
            if (planned && wantsFire)
            {
                AimAt(aimPoint);
                yield return FireOnce(weapon, () => AimAt(aimPoint));
                yield return WaitUntil(() => weapon.BurstCount > bursts, 1.5f);
                yield return Frames(2);
            }
            InkBulletImpact burst = weapon.LastBurst;
            float damage = FirstHit(dummy, out _, out _);
            Check("bot 掩體空爆:目標躲在掩體後 → 瞄掩體上方、彈越過掩體在目標上方空爆,爆風炸到(50~70)",
                hiddenFromChest && planned && wantsFire && weapon.ShotsFired > start && burst.End == InkBulletEnd.AirBurst
                && damage >= 50f && damage <= 70f,
                $"胸口被擋={hiddenFromChest} 算出瞄準點={planned}(高 {aimPoint.y - feet.y:0.0} m)開火={wantsFire} " +
                $"{burst.End} 爆風 {damage:0.0}");
            // 空曠地(沒有掩體擋)看不到 → 記憶過期(> 2 秒)不再開火
            aimContext.SecondsSinceSeen = 3f;
            weapon.BotAimPoint(aimContext);
            Check("bot 掩體空爆:看不到超過 2 秒就放棄", !weapon.BotAttackIntent(context) && !weapon.CoverShotPlanned,
                $"計畫={weapon.CoverShotPlanned}");
            Despawn(dummy);
            _spawned.Remove(cover);
            Destroy(cover);
            yield return new WaitForSeconds(0.5f);
            _ground.ClearInk();
        }

        // ── Luna:內圈 1.7 m 仍是 70(Blaster 在同距離只剩 ~65)、外圈 3.9 ──

        private IEnumerator MeasureLunaSplash()
        {
            BlasterWeapon weapon = Weapon;
            _tank.InfiniteInk = true;
            Vector3 stand = new Vector3(-20f, 0.1f, -24f);
            yield return MoveTo(stand);
            AimAlong(Vector3.forward);
            yield return new WaitForSeconds(0.9f);
            yield return FireOnce(weapon);
            int bursts = weapon.BurstCount;
            yield return WaitUntil(() => weapon.BurstCount > bursts, 1.5f);
            Vector3 p = weapon.LastBurst.Point;
            yield return new WaitForSeconds(0.9f);
            float body = BodyRadius();
            TargetDummy inner = SpawnDummy(new Vector3(p.x + 1.5f + body, 0f, p.z));
            TargetDummy outer = SpawnDummy(new Vector3(p.x - 3.6f - body, 0f, p.z));
            yield return Frames(3);
            _hits.Clear();
            yield return MoveTo(stand);
            AimAlong(Vector3.forward);
            yield return new WaitForSeconds(0.9f);
            yield return FireOnce(weapon);
            bursts = weapon.BurstCount;
            yield return WaitUntil(() => weapon.BurstCount > bursts, 1.5f);
            yield return Frames(2);
            Vector3 center = weapon.LastBurst.Point;
            float innerDamage = FirstHit(inner, out _, out _);
            float outerDamage = FirstHit(outer, out _, out _);
            float innerExpected = Expected(center, inner, false);
            float outerExpected = Expected(center, outer, false);
            float innerDistance = Vector3.Distance(center, inner.ClosestPoint(center));
            Check($"Luna 爆風:內圈 {L.NearRadius} m 內 {L.NearDamage}(Blaster 同距離 {B.ExplosionDamage(innerDistance, false):0.0})、" +
                  $"外圈 {L.FarRadius} m 線性到 {L.FarDamage}",
                Mathf.Abs(innerDamage - innerExpected) < 0.11f && Mathf.Abs(innerExpected - 70f) < 1e-3f
                && Mathf.Abs(outerDamage - outerExpected) < 0.11f && outerExpected > 50f && outerExpected < 60f,
                $"{innerDistance:0.00} m → {innerDamage:0.0};外側 → {outerDamage:0.0}(期望 {outerExpected:0.0})");
            Despawn(inner);
            Despawn(outer);
            yield return new WaitForSeconds(0.3f);
            _ground.ClearInk();
        }

        // ── 收尾:換回 Splattershot,內建槍身恢復 ──

        private void Cleanup()
        {
            if (_intent != null)
            {
                _intent.Attack = false;
                _intent.Move = Vector2.zero;
                _intent.Squid = false;
                _intent.Sub = false;
            }
            if (_loadout != null && _baseKit != null && _loadout.Kit != _baseKit)
            {
                _loadout.Equip(_baseKit);
                Renderer barrel = _visualComponent != null && _visualComponent.WeaponModel != null
                    ? _visualComponent.WeaponModel.GetComponent<Renderer>()
                    : null;
                Check("換回 Splattershot:內建槍身恢復、爆破槍槍模移除",
                    _loadout.Main is InkShooter && barrel != null && !barrel.forceRenderingOff,
                    $"主武器={(_loadout.Main != null ? _loadout.Main.GetType().Name : "無")} " +
                    $"內建隱藏={(barrel != null && barrel.forceRenderingOff)}");
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
            if (_ground != null)
            {
                _ground.ClearInk();
            }
            if (_player != null)
            {
                Teleport(_player.transform, new Vector3(0f, 0.1f, 0f));
            }
        }

        // ── 小工具 ──

        // 點一下扳機、等出膛(預備 14F);aimEachFrame 可在等待中持續瞄準
        private IEnumerator FireOnce(BlasterWeapon weapon, System.Action aimEachFrame = null)
        {
            int before = weapon.ShotsFired;
            _intent.Attack = true;
            yield return null;
            TrackDelta();
            _intent.Attack = false;
            float deadline = Time.time + 1.5f;
            while (weapon.ShotsFired == before && Time.time < deadline)
            {
                aimEachFrame?.Invoke();
                yield return null;
                TrackDelta();
            }
        }

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

        // 瞄準:視覺根轉到該方向(槍口位置跟著轉),視線從角色槍口出發
        private void AimAlong(Vector3 direction)
        {
            Vector3 flat = new Vector3(direction.x, 0f, direction.z);
            if (flat.sqrMagnitude > 1e-6f)
            {
                _visual.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
            }
            _aim.Origin = _muzzle.position;
            _aim.Forward = direction.normalized;
        }

        private void AimAt(Vector3 point)
        {
            Vector3 flat = point - _player.transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude > 1e-6f)
            {
                _visual.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
            }
            _aim.Origin = _muzzle.position;
            _aim.Forward = (point - _muzzle.position).normalized;
        }

        // 槍口在地面的投影(距離從槍口量)
        private Vector3 MuzzleFlat()
        {
            Vector3 p = _muzzle.position;
            p.y = 0f;
            return p;
        }

        private static Vector3 DummyCenter(TargetDummy dummy)
        {
            return dummy.transform.position + Vector3.up;
        }

        private float BodyRadius()
        {
            var dummy = _dummyTemplate.GetComponent<TargetDummy>();
            return dummy != null ? dummy.BodyRadius : 0.4f;
        }

        private TargetDummy SpawnDummy(Vector3 groundPosition)
        {
            GameObject go = Instantiate(_dummyTemplate, groundPosition, Quaternion.identity);
            go.name = "BlasterDummy";
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

        // 爆心 center 對假人最近點的期望爆風(terrain = 撞地形,半徑 × 0.4234)
        private float Expected(Vector3 center, TargetDummy dummy, bool terrain)
        {
            BlasterWeapon weapon = Weapon;
            ExplosionSpec spec = weapon.Config.Explosion;
            float scale = terrain ? weapon.Config.Bullet.TerrainHitExplosionScale : 1f;
            return spec.Evaluate(Vector3.Distance(center, dummy.ClosestPoint(center)), scale);
        }

        // 第一筆命中的傷害與時間,以及擊倒時間(−1 = 沒有)
        private float FirstHit(TargetDummy dummy, out float firstTime, out float killTime)
        {
            float amount = -1f;
            firstTime = -1f;
            killTime = -1f;
            foreach (HitRecord h in _hits)
            {
                if (!ReferenceEquals(h.Target, dummy))
                {
                    continue;
                }
                if (firstTime < 0f)
                {
                    firstTime = h.Time;
                    amount = h.Amount;
                }
                if (h.Splatted && killTime < 0f)
                {
                    killTime = h.Time;
                }
            }
            return amount;
        }

        private int HitCount(TargetDummy dummy)
        {
            int hits = 0;
            foreach (HitRecord h in _hits)
            {
                hits += ReferenceEquals(h.Target, dummy) ? 1 : 0;
            }
            return hits;
        }

        private bool Owned(Vector3 point)
        {
            return _ground.SampleOwnership(new Vector3(point.x, 0f, point.z), Vector3.up) == (byte)_inkling.Team;
        }

        private bool InkTinted(Transform gun)
        {
            var block = new MaterialPropertyBlock();
            foreach (Renderer r in gun.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.name.StartsWith("Ink", System.StringComparison.Ordinal))
                {
                    continue;
                }
                r.GetPropertyBlock(block);
                Color c = block.GetColor("_BaseColor");
                Color team = TeamPalette.ColorOf(_inkling.Team);
                return Mathf.Abs(c.r - team.r) + Mathf.Abs(c.g - team.g) + Mathf.Abs(c.b - team.b) < 0.05f;
            }
            return false;
        }

        // 槍管方向(槍模 +Z)的世界長度:各零件網格包圍盒的 8 個角換到槍模座標取 z 範圍
        private static float GunLength(Transform gun)
        {
            float minZ = float.MaxValue;
            float maxZ = float.MinValue;
            foreach (MeshFilter filter in gun.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null)
                {
                    continue;
                }
                Bounds b = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3(
                        (i & 1) == 0 ? b.min.x : b.max.x,
                        (i & 2) == 0 ? b.min.y : b.max.y,
                        (i & 4) == 0 ? b.min.z : b.max.z);
                    Vector3 world = filter.transform.TransformPoint(corner);
                    float z = gun.InverseTransformPoint(world).z;
                    minZ = Mathf.Min(minZ, z);
                    maxZ = Mathf.Max(maxZ, z);
                }
            }
            return maxZ > minZ ? (maxZ - minZ) * Mathf.Abs(gun.lossyScale.z) : 0f;
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
    }
}

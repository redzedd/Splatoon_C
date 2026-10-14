using System.Collections;
using System.Collections.Generic;
using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Core.Match;
using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.CameraRig;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 雙槍(Splat Dualies)驗收(試射場):執行期換裝 → 實測
    //   換裝外觀(兩把槍模、左手插槽、內建槍身隱藏)/ bot 掛勾 / 射速 5F 與左右輪流、兩把槍平行、S3 散布(地面 / 跳射)/
    //   第一發延遲 3F(人形)、13F(烏賊)/ 4 發擊倒與 TTK 15F / 有效射程 10.89 與衰減 / 單發耗墨 0.72%、一槽 138 發、墨量鎖 20F /
    //   開火走速 4.8 m/s / 閃避翻滾(4.0 + 1.0 m、7%、20F 後開火、硬直定身與不能變烏賊、墨量鎖 70F、落點塗 r1.8)/
    //   連 2 滾、第 3 滾擋下、硬直後重置 / 砲台模式(4F、不散、判定 0.335、TTK 12F、移動解除)/ 空中翻滾急墜 /
    //   墨不夠不滾(一般跳)/ 單發塗地(對 Splattershot)/ bot 掛勾驅動(被打就翻滾、命中)/ 潛墨隱形 / 每幀零配置。
    // 期望值全取自 S3 原始參數(S3DualiesParams);量測值來自真實遊戲路徑(意圖 → DualiesWeapon / PlayerLocomotion → 墨彈 → 假人 / 地面)。
    // 場地:試射場 x ≤ −4 的空地(x=−24~−4、z=−24~0)。
    [AutoTest(Scene = AutoTestAttribute.TrainingScene, Timeout = 300f, Order = 220)]
    public sealed class DualiesAutoTest : AutoTestBase
    {
        public const string ConfigPath = "Weapons/Main_splat_dualies";

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
        private SquidController _squid;
        private CharacterController _controller;
        private CharacterVisual _visualComponent;
        private Transform _visual;
        private ScriptedIntent _intent;
        private TestAim _aim;
        private GameObject _dummyTemplate;
        private PaintableSurface _ground;
        private DualiesConfig _config;
        private WeaponKit _baseKit;
        private WeaponKit _kit;
        private float _maxDeltaTime;
        private float _paintPerShotBaseline;
        private int _exceptions;
        private string _firstException;

        private static S3DualiesParams S3 => S3DualiesParams.SplatDualies;

        private DualiesWeapon Weapon => _loadout != null ? _loadout.Main as DualiesWeapon : null;

        public static void Run()
        {
            Launch<DualiesAutoTest>();
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
            _visual = _player != null ? _player.transform.Find("Visual") : null;
            _dummyTemplate = GameObject.Find("Dummy_Near");
            _config = Resources.Load<DualiesConfig>(ConfigPath);
            foreach (PaintableSurface surface in PaintableSurface.Active)
            {
                if (surface.name == "Ground")
                {
                    _ground = surface;
                }
            }
            if (_inkling == null || _loadout == null || _tank == null || _squid == null || _controller == null
                || _visual == null || _dummyTemplate == null || _ground == null)
            {
                FailSetup("找不到 Player(Inkling/WeaponLoadout/PlayerInkTank/SquidController/Visual)、Dummy_Near 或 Ground");
                yield break;
            }
            if (_config == null)
            {
                FailSetup($"找不到 Resources/{ConfigPath}(先跑 Tools/SplatoonC/M7 Setup)");
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

            // 單發塗地的基準(Splattershot,換裝前先量)
            yield return MeasurePaintPerShotBaseline();
            yield return EquipAndInspect();
            if (Weapon == null)
            {
                Cleanup();
                Finish();
                yield break;
            }
            CharacterVisual.ResetCostCounters();
            CharacterVisual.MeasureAllocations = true;

            CheckBotHooks();
            yield return MeasureCadenceAlternationSpread();
            yield return MeasureJumpSpread();
            yield return MeasureFirstShotDelay();
            yield return MeasureTtk();
            yield return MeasureEffectiveRange();
            yield return MeasureFiringMoveSpeed();
            yield return MeasureDodgeRoll();
            yield return MeasureRollRefillLockAndPaint();
            yield return MeasureRollChain();
            yield return MeasureTurret();
            yield return MeasureTurretTtk();
            yield return MeasureTurretRadius();
            yield return MeasureAirRoll();
            yield return MeasureRollWithoutInk();
            yield return MeasureInkTank();
            yield return MeasurePaintPerShot();
            yield return MeasureBotStyleEngagement();
            yield return MeasureSubmergedVisibility();

            CharacterVisual.MeasureAllocations = false;
            Check("雙持/翻滾/砲台姿勢每幀零配置(CharacterVisual)",
                CharacterVisual.AllocatedBytes == 0 && CharacterVisual.TotalSamples > 100,
                $"配置 {CharacterVisual.AllocatedBytes} B / {CharacterVisual.TotalSamples} 幀");
            Check("雙槍全程無例外", _exceptions == 0, $"例外 {_exceptions} 次 {_firstException}");
            Debug.Log($"[AUTOTEST] INFO Splat Dualies 量測期間最大幀時間 {_maxDeltaTime * 1000f:0.0} ms");
            Cleanup();
            Finish();
        }

        // ── 換裝、外觀 ──

        private IEnumerator EquipAndInspect()
        {
            _kit = ScriptableObject.CreateInstance<WeaponKit>();
            _kit.Setup("splat_dualies", "斯普拉雙槍", "Splat Dualies", "AutoTest 執行期組合", _config, _baseKit.Sub,
                _baseKit.Special, 200f, WeightClass.Mid, "射程", 50, "威力", 29, "機動", 60);
            yield return MoveTo(new Vector3(-12f, 0.1f, -20f));
            _loadout.Equip(_kit);
            yield return Frames(2);

            DualiesWeapon weapon = Weapon;
            Check("換裝:主武器換成 DualiesWeapon(Class = Dualies、AttackClass = Dualies)",
                weapon != null && weapon.Config == _config && _loadout.Kit == _kit
                && _config.Class == WeaponClass.Dualies && _config.AttackClass == AttackClass.Dualies,
                $"主武器={(_loadout.Main != null ? _loadout.Main.GetType().Name : "無")}");
            if (weapon == null)
            {
                yield break;
            }
            DualiesGunPair guns = weapon.Guns;
            Transform leftSocket = _visualComponent != null ? _visualComponent.WeaponSocketLeft : null;
            bool socketOk = leftSocket != null && leftSocket.parent != null && leftSocket.parent.name == "Hand.L";
            Check("外觀:兩把槍模(右手 WeaponSocket、左手鏡射插槽 WeaponSocketLeft)、內建槍身隱藏、墨水部位套隊伍色",
                guns != null && socketOk && guns.Right.parent == _visualComponent.WeaponSocket
                && guns.Left.parent == leftSocket && guns.BuiltInGunHidden && guns.InkPartCount >= 4 && guns.Visible
                && InkTinted(guns.Right),
                $"槍模={(guns != null)} 左插槽={(leftSocket != null ? leftSocket.parent.name : "無")} " +
                $"墨水部位={(guns != null ? guns.InkPartCount : 0)} 內建隱藏={(guns != null && guns.BuiltInGunHidden)}");
            if (guns != null)
            {
                float length = GunLength(guns.Right);
                Check("槍模尺寸:單把約 0.27 m(比 Splattershot 小)", length > 0.2f && length < 0.34f,
                    $"世界長度 {length:0.000} m");
            }
            Check("本機玩家有雙準星 HUD", weapon.Reticle != null, $"Reticle={(weapon.Reticle != null)}");
            Check("開火走速設定 0.08 u/F = 4.8 m/s、有效射程 10.89",
                Mathf.Abs(weapon.MoveSpeedWhileFiring - S3.MoveSpeedWhileFiring) < 1e-3f
                && Mathf.Abs(weapon.EffectiveRange - S3.EffectiveRange) < 0.02f,
                $"走速 {weapon.MoveSpeedWhileFiring:0.00} 射程 {weapon.EffectiveRange:0.00}");
        }

        // ── bot 掛勾(BotBrain 交戰時呼叫的同一組方法)──

        private void CheckBotHooks()
        {
            DualiesWeapon weapon = Weapon;
            float range = S3.EffectiveRange;
            var inRange = new BotCombatContext
            {
                Distance = range * 0.9f, TargetVisible = true, Reacted = true, AimError = 0.5f, FireTolerance = 4f,
            };
            var outOfRange = inRange;
            outOfRange.Distance = range * 1.3f;
            outOfRange.TargetVisible = true;
            Check($"bot:有效射程 {range:0.00} m 內開火、射程外不開火;交戰距離 ≈ 0.72 × 射程",
                weapon.BotAttackIntent(inRange) && !weapon.BotAttackIntent(outOfRange)
                && Mathf.Abs(weapon.BotPreferredDistance - range * 0.72f) < 0.05f && weapon.ProjectileSpeed > 60f,
                $"交戰距離 {weapon.BotPreferredDistance:0.00} 平均彈速(提前量){weapon.ProjectileSpeed:0.0} m/s");
        }

        // ── 射速 5F、左右輪流、兩把槍平行、S3 散布、音效 ──

        private IEnumerator MeasureCadenceAlternationSpread()
        {
            DualiesWeapon weapon = Weapon;
            _tank.InfiniteInk = true;
            weapon.SpreadMultiplier = 1f;
            yield return MoveTo(new Vector3(-12f, 0.1f, -20f));
            AimAlong(Vector3.forward);
            yield return Frames(4);

            int wantShots = Mathf.CeilToInt((S3.BiasMax - S3.BiasMin) / S3.BiasKf) + 8;
            int soundsBefore = _sounds.Count;
            int startShots = weapon.ShotsFired;
            int bulletsBefore = InkBullet.FiredCount;
            var times = new List<float>();
            float maxAbsYaw = 0f;
            float biasAfterFirst = -1f;
            bool alternates = true;
            int lastHand = -1;
            float leftLateral = 0f;
            float rightLateral = 0f;
            int leftCount = 0;
            int rightCount = 0;
            float maxParallelError = 0f;
            _intent.Attack = true;
            float deadline = Time.time + wantShots * S3.FireIntervalSeconds + 2f;
            int seen = startShots;
            while (weapon.ShotsFired - startShots < wantShots && Time.time < deadline)
            {
                AimAlong(Vector3.forward);
                yield return null;
                TrackDelta();
                if (weapon.ShotsFired == seen)
                {
                    continue;
                }
                for (int i = seen; i < weapon.ShotsFired; i++)
                {
                    times.Add(weapon.LastShotTime);
                }
                if (seen == startShots)
                {
                    biasAfterFirst = weapon.ShotsFired - startShots == 1 ? weapon.SpreadBias : -1f;
                }
                if (weapon.ShotsFired - seen == 1)
                {
                    int hand = weapon.LastHand;
                    alternates &= lastHand < 0 || hand != lastHand;
                    lastHand = hand;
                    Vector3 local = Quaternion.Inverse(_visual.rotation) * (weapon.LastShotOrigin - _player.transform.position);
                    if (hand == 0)
                    {
                        leftLateral += local.x;
                        leftCount++;
                    }
                    else
                    {
                        rightLateral += local.x;
                        rightCount++;
                    }
                    // 兩把槍平行:扣掉散布偏角後的水平方向與準心方向夾角
                    Vector3 dir = Quaternion.AngleAxis(-weapon.LastYawOffsetDeg, Vector3.up) * weapon.LastShotDirection;
                    dir.y = 0f;
                    maxParallelError = Mathf.Max(maxParallelError, Vector3.Angle(dir, Vector3.forward));
                }
                seen = weapon.ShotsFired;
                maxAbsYaw = Mathf.Max(maxAbsYaw, Mathf.Abs(weapon.LastYawOffsetDeg));
            }
            _intent.Attack = false;
            float biasEnd = weapon.SpreadBias;
            int shots = times.Count;
            float interval = shots > 1 ? (times[shots - 1] - times[0]) / (shots - 1) : 0f;
            float expected = S3.FireIntervalSeconds;
            Check($"射速:每 {S3.RepeatFrame}F 一發(12 發/秒)",
                shots >= 20 && Mathf.Abs(interval - expected) < expected * 0.03f,
                $"{shots} 發平均間隔 {interval * 1000f:0.0} ms(期望 {expected * 1000f:0.0} ms)");
            leftLateral = leftCount > 0 ? leftLateral / leftCount : 0f;
            rightLateral = rightCount > 0 ? rightLateral / rightCount : 0f;
            Check("兩把槍左右輪流出膛、各自從自己那一側(±半個槍距)平行射向自己的準星",
                alternates && leftCount > 5 && rightCount > 5 && Mathf.Abs(leftLateral + _config.MuzzleHalfSpacing) < 0.03f
                && Mathf.Abs(rightLateral - _config.MuzzleHalfSpacing) < 0.03f && maxParallelError < 1f,
                $"輪流={alternates} 左 {leftCount} 發 x={leftLateral:0.00} 右 {rightCount} 發 x={rightLateral:0.00} " +
                $"平行誤差 {maxParallelError:0.00}°");
            Check("墨彈走 S3 三狀態 InkBullet(每發一顆)", InkBullet.FiredCount - bulletsBefore >= shots,
                $"InkBullet 發射 {InkBullet.FiredCount - bulletsBefore} 顆 / 出膛 {shots} 發");
            Check($"散布:偏角 ≤ {S3.StandSwerve}°、連射偏向 b 由 {S3.BiasMin} 爬到 {S3.BiasMax}",
                Mathf.Abs(biasAfterFirst - (S3.BiasMin + S3.BiasKf)) < 1e-3f && Mathf.Abs(biasEnd - S3.BiasMax) < 0.02f
                && maxAbsYaw <= S3.StandSwerve + 1e-3f && maxAbsYaw >= S3.StandSwerve * 0.3f,
                $"首發後 b={biasAfterFirst:0.000} 連射 {shots} 發後 b={biasEnd:0.000} 最大偏角 {maxAbsYaw:0.00}°");
            bool heard = false;
            for (int i = soundsBefore; i < _sounds.Count; i++)
            {
                heard |= _sounds[i].StartsWith("shoot_", System.StringComparison.Ordinal);
            }
            Check($"射擊音效({_config.FireSound},未合成前退回 shoot_shooter)", heard,
                $"期間發聲 {_sounds.Count - soundsBefore} 次");
            yield return new WaitForSeconds(0.5f);
        }

        // ── 跳射散布:空中偏向 b 固定 0.4、偏角上限 7.5°(起跳 25F 後開始收、70F 回到地面 2.0°)──
        // 地面偏角永遠 ≤ 2.0°(上一段實測),所以空中有好幾發超過 2.0° 就證明走的是跳射散布(統計:b 0.4、偏角 ≥ 5° 時
        // 每發超過 2.0° 的機率 0.5~0.63,兩跳約 30 發,少於 3 發的機率 < 1e-4);收回的時間律則是確定性的上限:
        // 每一發 |偏角| ≤ 當下上限 lerp(7.5, 2.0, (起跳後幀數 − 25) / 45)(起跳時刻以測試看到離地的那一幀,容許 1.5F 的更新順序差)。
        private IEnumerator MeasureJumpSpread()
        {
            DualiesWeapon weapon = Weapon;
            _tank.InfiniteInk = true;
            weapon.SpreadMultiplier = 1f;
            yield return MoveTo(new Vector3(-12f, 0.1f, -16f));
            AimAlong(Vector3.forward);
            yield return Frames(4);
            int rolls0 = weapon.RollCount;
            int airShots = 0;
            int overStand = 0;
            int shrinkShots = 0;
            int lateShots = 0;
            float maxAir = 0f;
            float maxRatio = 0f;
            float maxLate = 0f;
            _intent.Attack = true;
            _intent.Move = Vector2.zero;
            for (int jump = 0; jump < 5 && airShots < 24; jump++)
            {
                yield return WaitUntil(() => _controller.isGrounded, 2f);
                yield return Frames(2);
                // 沒推方向:開火中按跳是一般跳(不是翻滾)
                _intent.PressJump();
                float takeoff = -1f;
                int seen = weapon.ShotsFired;
                float deadline = Time.time + 2.5f;
                while (Time.time < deadline)
                {
                    AimAlong(Vector3.forward);
                    yield return null;
                    TrackDelta();
                    if (takeoff < 0f && !_controller.isGrounded)
                    {
                        takeoff = Time.time;
                    }
                    if (weapon.ShotsFired != seen)
                    {
                        if (weapon.ShotsFired - seen == 1 && weapon.LastShotAirborne && takeoff >= 0f)
                        {
                            float yaw = Mathf.Abs(weapon.LastYawOffsetDeg);
                            float frames = (weapon.LastShotTime - takeoff) * 60f;
                            float shrink = Mathf.Clamp01((frames - 1.5f - S3.JumpBiasDecreaseStartFrame)
                                                         / (S3.JumpBiasEndFrame - S3.JumpBiasDecreaseStartFrame));
                            float bound = Mathf.Lerp(S3.JumpSwerve, S3.StandSwerve, shrink);
                            airShots++;
                            maxAir = Mathf.Max(maxAir, yaw);
                            overStand += yaw > S3.StandSwerve + 1e-3f ? 1 : 0;
                            maxRatio = Mathf.Max(maxRatio, yaw / bound);
                            if (frames > S3.JumpBiasDecreaseStartFrame + 2f)
                            {
                                shrinkShots++;
                            }
                            if (frames > S3.JumpBiasEndFrame + 2f)
                            {
                                lateShots++;
                                maxLate = Mathf.Max(maxLate, yaw);
                            }
                        }
                        seen = weapon.ShotsFired;
                    }
                    if (takeoff >= 0f && _controller.isGrounded)
                    {
                        break;
                    }
                }
            }
            _intent.Attack = false;
            Check($"跳射散布:空中偏角上限 {S3.JumpSwerve}°(b 固定 {S3.JumpBiasMax}),好幾發超過地面上限 {S3.StandSwerve}°;"
                  + "開火中不推方向按跳是一般跳",
                airShots >= 12 && maxAir <= S3.JumpSwerve + 1e-3f && overStand >= 3 && weapon.RollCount == rolls0,
                $"空中 {airShots} 發、最大偏角 {maxAir:0.00}°、超過 {S3.StandSwerve}° 的 {overStand} 發;"
                + $"翻滾 {weapon.RollCount - rolls0} 次");
            Check($"跳射偏角起跳後 {S3.JumpBiasDecreaseStartFrame}F 開始收、{S3.JumpBiasEndFrame}F 回到 {S3.StandSwerve}°"
                  + "(每發 ≤ 當下上限)",
                shrinkShots >= 6 && maxRatio <= 1f + 1e-3f && maxLate <= S3.StandSwerve + 1e-3f,
                $"收回段 {shrinkShots} 發、|偏角| / 當下上限 最大 {maxRatio:0.00};{S3.JumpBiasEndFrame}F 後 {lateShots} 發"
                + $"最大 {maxLate:0.00}°");
            yield return WaitUntil(() => _controller.isGrounded, 2f);
            yield return new WaitForSeconds(0.3f);
        }

        // ── 第一發延遲:人形 3F、剛從烏賊態出來 13F ──

        private IEnumerator MeasureFirstShotDelay()
        {
            DualiesWeapon weapon = Weapon;
            _tank.InfiniteInk = true;
            yield return MoveTo(new Vector3(-16f, 0.1f, -20f));
            AimAlong(Vector3.forward);
            yield return new WaitForSeconds(0.3f);
            int before = weapon.ShotsFired;
            _intent.Attack = true;
            yield return WaitUntil(() => weapon.ShotsFired > before, 1f);
            float humanoidDelay = weapon.LastShotTime - weapon.TriggerStartTime;
            _intent.Attack = false;
            float tolerance = Mathf.Max(1.2f / 60f, _maxDeltaTime * 1.2f);
            Check($"第一發延遲(人形){S3.FirstShotFrames}F",
                weapon.ShotsFired > before && Mathf.Abs(humanoidDelay - S3.FirstShotFrames / 60f) <= tolerance,
                $"實測 {humanoidDelay * 60f:0.0}F(容許 ±{tolerance * 60f:0.0}F)");

            // 烏賊態 → 放開烏賊同時按開火
            InkPainter.PaintSphere(_player.transform.position, 3f, 1f, _inkling.InkSource(false));
            yield return new WaitForSeconds(0.2f);
            _intent.Squid = true;
            yield return new WaitForSeconds(0.6f);
            bool wasSquid = _squid.IsSquid;
            before = weapon.ShotsFired;
            _intent.Squid = false;
            _intent.Attack = true;
            yield return WaitUntil(() => weapon.ShotsFired > before, 1f);
            float squidDelay = weapon.LastShotTime - weapon.LastSquidTime;
            _intent.Attack = false;
            Check($"第一發延遲(烏賊態出來){S3.SquidFirstShotFrames}F(9 + 4 − 0)",
                wasSquid && weapon.ShotsFired > before
                && Mathf.Abs(squidDelay - S3.SquidFirstShotFrames / 60f) <= tolerance,
                $"烏賊={wasSquid} 解除烏賊到第一發 {squidDelay * 60f:0.0}F");
            yield return new WaitForSeconds(0.3f);
        }

        // ── 4 發擊倒、TTK 15F(近距全傷;散布關閉 = S3 TTK 的定義)──

        private IEnumerator MeasureTtk()
        {
            DualiesWeapon weapon = Weapon;
            weapon.SpreadMultiplier = 0f;
            _tank.InfiniteInk = true;
            yield return MoveTo(new Vector3(-8f, 0.1f, -20f));
            AimAlong(Vector3.forward);
            yield return Frames(3);
            TargetDummy dummy = SpawnDummy(MuzzleFlat(false) + Vector3.forward * 5f);
            yield return Frames(2);
            _hits.Clear();
            float deadline = Time.time + 3f;
            _intent.Attack = true;
            while (dummy.IsAlive && Time.time < deadline)
            {
                AimAt(DummyCenter(dummy), false);
                yield return null;
                TrackDelta();
            }
            _intent.Attack = false;
            Tally(dummy, out int hits, out bool allFull, out float first, out float kill);
            Check($"擊倒發數:{S3.DamageMax} × {S3.ShotsToSplat()} 發(近距全傷,兩把槍都打得到)",
                kill > 0f && hits == S3.ShotsToSplat() && allFull, $"命中 {hits} 發、每發全傷={allFull}、擊倒={kill > 0f}");
            float expected = S3.TtkFrames() / 60f;
            float tolerance = Mathf.Max(0.02f, _maxDeltaTime * 1.5f);
            Check($"TTK(首發命中後):{S3.TtkFrames()}F = {expected:0.000} s",
                kill > 0f && Mathf.Abs(kill - first - expected) <= tolerance,
                $"實測 {kill - first:0.000} s(容許 ±{tolerance:0.000})");
            Despawn(dummy);
            weapon.SpreadMultiplier = 1f;
            yield return new WaitForSeconds(0.3f);
        }

        // ── 有效射程:假人放在 S3 有效射程 −1.0 ~ +1.0 m(每 0.5 m),單發量傷害,全傷最遠的距離 ──

        private IEnumerator MeasureEffectiveRange()
        {
            DualiesWeapon weapon = Weapon;
            weapon.SpreadMultiplier = 0f;
            _tank.InfiniteInk = true;
            float range = S3.EffectiveRange;
            float lastFull = -1f;
            bool contiguous = true;
            float farDamage = -1f;
            string log = "";
            for (int step = -2; step <= 2; step++)
            {
                float distance = range + step * 0.5f;
                yield return MoveTo(new Vector3(-12f, 0.1f, -22f));
                AimAlong(Vector3.forward);
                yield return Frames(3);
                TargetDummy dummy = SpawnDummy(MuzzleFlat(false) + Vector3.forward * distance);
                yield return Frames(2);
                float damage = -1f;
                for (int attempt = 0; attempt < 2 && damage < 0f; attempt++)
                {
                    yield return new WaitForSeconds(0.2f);
                    _hits.Clear();
                    AimAt(DummyCenter(dummy), false);
                    int before = weapon.ShotsFired;
                    _intent.Attack = true;
                    yield return WaitUntil(() => weapon.ShotsFired > before, 0.5f);
                    _intent.Attack = false;
                    float deadline = Time.time + 0.6f;
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
                if (step == 2)
                {
                    farDamage = damage;
                }
                log += $" {distance:0.0}m→{(damage < 0f ? "未中" : damage.ToString("0.0"))}";
                Despawn(dummy);
            }
            Check($"有效射程(全傷最遠距離)≈ S3 {range:0.00} m",
                lastFull > 0f && Mathf.Abs(lastFull - range) <= 0.5f + 1e-3f, $"全傷最遠 {lastFull:0.00} m;逐點:{log}");
            Check($"射程外依飛行幀數衰減({S3.DamageMax} → 下限 {S3.DamageMin},第 {S3.ReduceStartFrame}→{S3.ReduceEndFrame}F)",
                farDamage >= S3.DamageMin - 0.01f && farDamage < S3.DamageMax - 0.5f, $"{range + 1f:0.0} m 處 {farDamage:0.0}");
            weapon.SpreadMultiplier = 1f;
        }

        // ── 開火走速 0.08 u/F = 4.8 m/s ──

        private IEnumerator MeasureFiringMoveSpeed()
        {
            _tank.InfiniteInk = true;
            yield return MoveTo(new Vector3(-20f, 0.1f, -18f));
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
            Check($"開火走速 {S3.MoveSpeed} u/F = {S3.MoveSpeedWhileFiring:0.00} m/s", Mathf.Abs(speed - S3.MoveSpeedWhileFiring)
                < S3.MoveSpeedWhileFiring * 0.05f, $"實測 {speed:0.00} m/s");
            yield return new WaitForSeconds(0.4f);
        }

        // ── 閃避翻滾:距離、耗墨、開火恢復、硬直定身、不能變烏賊 ──

        private IEnumerator MeasureDodgeRoll()
        {
            DualiesWeapon weapon = Weapon;
            DodgeRollParams roll = S3.Roll;
            _tank.InfiniteInk = false;
            _tank.RefillFull();
            yield return MoveTo(new Vector3(-22f, 0.1f, -14f));
            // 瞄準前上方:墨彈飛到遠處,不干擾翻滾量測
            AimAlong(new Vector3(0f, 0.8f, 1f));
            yield return Frames(3);
            Vector3 p0 = _player.transform.position;
            float ink0 = _tank.Normalized;
            int rolls0 = weapon.RollCount;
            int shots0 = weapon.ShotsFired;
            _intent.Attack = true;
            _intent.Move = new Vector2(1f, 0f);
            _intent.PressJump();
            yield return WaitUntil(() => weapon.RollCount > rolls0, 0.5f);
            bool started = weapon.RollCount == rolls0 + 1 && weapon.IsRolling;
            float rollStart = weapon.RollStartTime;
            float inkAfter = _tank.Normalized;
            bool groundedStart = !weapon.IsAirRoll;

            float peakY = p0.y;
            float firstShotAt = -1f;
            float lockCheckStart = -1f;
            Vector3 lockStartPos = Vector3.zero;
            float lockDrift = 0f;
            bool squidDuringLock = false;
            bool blocksDuringLock = true;
            bool turretSeen = false;
            float slideEndDistance = -1f;
            while (weapon.IsRolling && Time.time - rollStart < 2f)
            {
                AimAlong(new Vector3(0f, 0.8f, 1f));
                yield return null;
                TrackDelta();
                peakY = Mathf.Max(peakY, _player.transform.position.y);
                float frames = weapon.RollFrames;
                if (firstShotAt < 0f && weapon.ShotsFired > shots0)
                {
                    firstShotAt = weapon.LastShotTime;
                }
                turretSeen |= weapon.IsTurret;
                if (frames >= roll.StartupFrames + roll.MoveFrames + roll.SlideFrames + 1f && slideEndDistance < 0f)
                {
                    Vector3 d = _player.transform.position - p0;
                    d.y = 0f;
                    slideEndDistance = d.magnitude;
                    lockCheckStart = frames;
                    lockStartPos = _player.transform.position;
                    // 硬直中推著方向鍵 + 按烏賊鍵:都不能動
                    _intent.Squid = true;
                }
                else if (lockCheckStart > 0f && frames >= 0f && frames < DodgeRollModel.TotalFrames(roll) - 1f)
                {
                    Vector3 drift = _player.transform.position - lockStartPos;
                    drift.y = 0f;
                    lockDrift = Mathf.Max(lockDrift, drift.magnitude);
                    squidDuringLock |= _squid.IsSquid;
                    blocksDuringLock &= weapon.BlocksSquid;
                }
            }
            float lockEnd = Time.time;
            // 硬直結束:可以變烏賊了(按著烏賊鍵 → 不再出新的一發,PostDelay 後變身)
            yield return WaitUntil(() => _squid.IsSquid, 0.5f);
            bool squidAfter = _squid.IsSquid;
            float squidAfterDelay = Time.time - lockEnd;
            _intent.Squid = false;
            _intent.Attack = false;
            _intent.Move = Vector2.zero;

            Check("閃避翻滾:開火中推方向 + 跳 → 地面翻滾(不起跳)",
                started && groundedStart && peakY - p0.y < 0.25f, $"開始={started} 地面={groundedStart} 最高 {peakY - p0.y:0.00} m");
            Check($"翻滾耗墨 {roll.InkConsume * 100f:0}%(每滾)", Mathf.Abs(ink0 - inkAfter - roll.InkConsume) < 1e-3f,
                $"扣 {(ink0 - inkAfter) * 100f:0.00}%");
            Check($"翻滾距離 {roll.Distance:0.0} + 滑行 {roll.SlideDistance:0.0} = {DodgeRollModel.TotalDistance(roll):0.0} m",
                Mathf.Abs(slideEndDistance - DodgeRollModel.TotalDistance(roll)) < 0.15f, $"實測 {slideEndDistance:0.00} m");
            float fireResume = firstShotAt - rollStart;
            float expectedResume = DodgeRollModel.FireResumeFrame(roll) / 60f;
            float tolerance = Mathf.Max(1.2f / 60f, _maxDeltaTime * 1.2f);
            Check($"翻滾中不能開火:起手 {roll.StartupFrames}F + 翻滾 {roll.MoveFrames}F + {roll.NoFireFrames}F 後才開火",
                firstShotAt > 0f && Mathf.Abs(fireResume - expectedResume) <= tolerance,
                $"翻滾開始到第一發 {fireResume * 60f:0.0}F(期望 {DodgeRollModel.FireResumeFrame(roll)}F)");
            Check("翻滾停下立刻進砲台模式", turretSeen, $"砲台={turretSeen}");
            Check($"硬直 {roll.UnrelaxFrames}F:推方向鍵也不動、不能變烏賊",
                lockCheckStart > 0f && lockDrift < 0.05f && !squidDuringLock && blocksDuringLock,
                $"硬直中位移 {lockDrift:0.000} m、變烏賊={squidDuringLock}");
            Check($"時間軸 {DodgeRollModel.TotalFrames(roll)}F 走完後可以變烏賊",
                squidAfter && squidAfterDelay < 0.25f, $"硬直結束後 {squidAfterDelay:0.00} s 變烏賊={squidAfter}");
            yield return new WaitForSeconds(0.4f);
        }

        // ── 翻滾後墨量鎖 70F、落點塗一灘(半寬 1.8)、翻滾音效 ──

        private IEnumerator MeasureRollRefillLockAndPaint()
        {
            DualiesWeapon weapon = Weapon;
            DodgeRollParams roll = S3.Roll;
            _tank.InfiniteInk = false;
            _tank.RefillFull();
            _ground.ClearInk();
            yield return MoveTo(new Vector3(-22f, 0.1f, -4f));
            AimAlong(Vector3.forward);
            yield return new WaitForSeconds(0.2f);
            int soundsBefore = _sounds.Count;
            int rolls0 = weapon.RollCount;
            int paints0 = weapon.RollPaintCount;
            int shots0 = weapon.ShotsFired;
            _intent.Attack = true;
            _intent.Move = new Vector2(1f, 0f);
            _intent.PressJump();
            yield return WaitUntil(() => weapon.RollCount > rolls0, 0.5f);
            float rollStart = weapon.RollStartTime;
            // 滾出去就放開扳機:沒有任何一發墨彈(第一發要 20F 後),墨量鎖只剩翻滾的 70F
            yield return Frames(2);
            _intent.Attack = false;
            float level = _tank.Normalized;
            float unlockedAt = -1f;
            float deadline = Time.time + roll.InkRecoverStopFrames / 60f + 1f;
            while (Time.time < deadline)
            {
                yield return null;
                TrackDelta();
                if (_tank.Normalized > level + 1e-5f)
                {
                    unlockedAt = Time.time;
                    break;
                }
            }
            _intent.Move = Vector2.zero;
            float lockMeasured = unlockedAt - rollStart;
            float expected = roll.InkRecoverStopFrames / 60f;
            float tolerance = Mathf.Max(2.2f / 60f, _maxDeltaTime * 2f);
            Check($"翻滾後墨量鎖 {roll.InkRecoverStopFrames}F = {expected:0.000} s",
                unlockedAt > 0f && weapon.ShotsFired == shots0 && Mathf.Abs(lockMeasured - expected) <= tolerance,
                $"翻滾開始後 {lockMeasured:0.000} s 開始回墨(容許 ±{tolerance:0.000};期間出膛 {weapon.ShotsFired - shots0} 發)");

            Vector3 landing = weapon.LastRollPaintPoint;
            Vector3 dir = weapon.LastRollDirection;
            Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
            bool center = Owned(landing);
            bool inside = Owned(landing + side * (roll.PaintWidthHalf - 0.35f))
                          && Owned(landing - side * (roll.PaintWidthHalf - 0.35f));
            bool outside = !Owned(landing + side * (roll.PaintWidthHalf + 0.5f))
                           && !Owned(landing - side * (roll.PaintWidthHalf + 0.5f));
            Check($"翻滾落點塗一灘(半寬 {roll.PaintWidthHalf} m;沒開火也有)",
                weapon.RollPaintCount - paints0 == 2 && center && inside && outside,
                $"塗墨次數 {weapon.RollPaintCount - paints0} 中心={center} 內側 {roll.PaintWidthHalf - 0.35f:0.00} m={inside} " +
                $"外側 {roll.PaintWidthHalf + 0.5f:0.00} m 未塗={outside}");
            bool heard = false;
            for (int i = soundsBefore; i < _sounds.Count; i++)
            {
                heard |= _sounds[i] == _config.RollSound || _sounds[i] == "squid_dive";
            }
            Check($"翻滾音效({_config.RollSound},未合成前退回 squid_dive)", heard, $"發聲 {_sounds.Count - soundsBefore} 次");
            yield return new WaitForSeconds(0.3f);
        }

        // ── 連續翻滾:位移結束後可接第 2 滾、第 3 滾擋下(也不起跳)、硬直走完次數重置 ──

        private IEnumerator MeasureRollChain()
        {
            DualiesWeapon weapon = Weapon;
            DodgeRollParams roll = S3.Roll;
            _tank.InfiniteInk = true;
            yield return MoveTo(new Vector3(-23f, 0.1f, -9f));
            AimAlong(new Vector3(0f, 0.8f, 1f));
            yield return Frames(3);
            int rolls0 = weapon.RollCount;
            _intent.Attack = true;
            _intent.Move = new Vector2(1f, 0f);
            _intent.PressJump();
            yield return WaitUntil(() => weapon.RollCount > rolls0, 0.5f);

            // 位移中(還沒到 16F)按跳:不能接
            yield return WaitUntil(() => weapon.RollFrames >= roll.StartupFrames + 4f, 0.5f);
            _intent.PressJump();
            yield return Frames(2);
            bool earlyBlocked = weapon.RollCount == rolls0 + 1;

            // 位移結束後按跳 + 方向:第 2 滾
            yield return WaitUntil(() => weapon.RollFrames >= DodgeRollModel.MoveEndFrame(roll) + 2f, 0.5f);
            _intent.PressJump();
            yield return Frames(2);
            bool second = weapon.RollCount == rolls0 + 2 && weapon.RollsInChain == 2;

            // 第 2 滾停下後再按:次數用完,擋下且不起跳
            yield return WaitUntil(() => weapon.RollFrames >= DodgeRollModel.MoveEndFrame(roll) + 4f, 0.5f);
            float y0 = _player.transform.position.y;
            _intent.PressJump();
            float peak = y0;
            for (int i = 0; i < 12; i++)
            {
                yield return null;
                peak = Mathf.Max(peak, _player.transform.position.y);
            }
            bool thirdBlocked = weapon.RollCount == rolls0 + 2 && peak - y0 < 0.1f;

            // 硬直走完 → 次數重置,又可以滾
            yield return WaitUntil(() => !weapon.IsRolling, 1f);
            bool refreshed = weapon.RollsInChain == 0 && weapon.RollsRemaining == roll.MaxRolls;
            _intent.PressJump();
            yield return Frames(3);
            bool again = weapon.RollCount == rolls0 + 3 && weapon.RollsInChain == 1;
            yield return WaitUntil(() => !weapon.IsRolling, 1.5f);
            _intent.Attack = false;
            _intent.Move = Vector2.zero;
            Check($"連續翻滾最多 {roll.MaxRolls} 次:位移中不能接、停下後可接第 2 滾、第 3 滾擋下且不起跳",
                earlyBlocked && second && thirdBlocked,
                $"太早={earlyBlocked} 第2滾={second} 第3滾擋下={thirdBlocked}(跳高 {peak - y0:0.00} m)");
            Check($"硬直 {DodgeRollModel.TotalFrames(roll)}F 走完後翻滾次數重置", refreshed && again,
                $"重置={refreshed} 再滾={again}");
            yield return new WaitForSeconds(0.3f);
        }

        // ── 砲台模式:4F、不散布、雙槍併攏、硬直後仍維持、移動解除 ──

        private IEnumerator MeasureTurret()
        {
            DualiesWeapon weapon = Weapon;
            _tank.InfiniteInk = true;
            weapon.SpreadMultiplier = 1f;
            yield return MoveTo(new Vector3(-22f, 0.1f, -20f));
            AimAlong(Vector3.forward);
            // 雙準星的投影用本機相機:相機轉到與測試視線同方向(+Z、略俯),準星才落在畫面內
            ThirdPersonCameraRig rig = Camera.main != null ? Camera.main.GetComponent<ThirdPersonCameraRig>() : null;
            if (rig != null)
            {
                rig.SetAngles(0f, 4f);
            }
            yield return Frames(3);
            // 先一般連射一下(S3:開火中才能翻滾),量兩個準星的畫面間距
            _intent.Attack = true;
            float warm = Time.time + 0.25f;
            while (Time.time < warm)
            {
                AimAlong(Vector3.forward);
                yield return null;
            }
            DualiesReticle reticle = weapon.Reticle;
            float separationNormal = reticle != null && reticle.Shown ? reticle.ScreenSeparation : -1f;
            float separationTurret = float.MaxValue;
            float turretSince = -1f;
            int rolls0 = weapon.RollCount;
            _intent.Move = new Vector2(1f, 0f);
            _intent.PressJump();
            yield return WaitUntil(() => weapon.RollCount > rolls0, 0.5f);
            _intent.Move = Vector2.zero;
            yield return WaitUntil(() => weapon.IsTurret, 0.6f);
            var times = new List<float>();
            float maxYaw = 0f;
            float maxLateral = 0f;
            bool allTurret = true;
            bool radiusOk = true;
            int seen = weapon.ShotsFired;
            float end = Time.time + 1.4f;
            while (Time.time < end)
            {
                AimAlong(Vector3.forward);
                yield return null;
                TrackDelta();
                if (weapon.IsTurret && turretSince < 0f)
                {
                    turretSince = Time.time;
                }
                if (reticle != null && reticle.Shown && turretSince > 0f && Time.time - turretSince > 0.2f)
                {
                    separationTurret = Mathf.Min(separationTurret, reticle.ScreenSeparation);
                }
                if (weapon.ShotsFired == seen)
                {
                    continue;
                }
                for (int i = seen; i < weapon.ShotsFired; i++)
                {
                    times.Add(weapon.LastShotTime);
                }
                seen = weapon.ShotsFired;
                maxYaw = Mathf.Max(maxYaw, Mathf.Abs(weapon.LastYawOffsetDeg));
                Vector3 local = Quaternion.Inverse(_visual.rotation) * (weapon.LastShotOrigin - _player.transform.position);
                maxLateral = Mathf.Max(maxLateral, Mathf.Abs(local.x));
                allTurret &= weapon.LastShotTurret;
                radiusOk &= Mathf.Abs(weapon.LastBulletRadius - S3.TurretRadiusForPlayer) < 1e-4f;
            }
            bool stillTurret = weapon.IsTurret && !weapon.IsRolling;
            int shots = times.Count;
            float interval = shots > 1 ? (times[shots - 1] - times[0]) / (shots - 1) : 0f;
            float expected = S3.TurretFireIntervalSeconds;
            Check($"砲台模式射速:每 {S3.TurretRepeatFrame}F 一發(LapOver_RepeatFrame;15 發/秒)",
                shots >= 12 && allTurret && Mathf.Abs(interval - expected) < expected * 0.04f,
                $"{shots} 發平均間隔 {interval * 1000f:0.0} ms(期望 {expected * 1000f:0.0} ms)");
            Check($"砲台模式不散布(LapOver_DegSwerve {S3.TurretSwerve}°)、雙槍併攏、判定 {S3.TurretRadiusForPlayer}",
                maxYaw < 1e-4f && maxLateral <= _config.TurretMuzzleHalfSpacing + 0.02f && radiusOk,
                $"最大偏角 {maxYaw:0.000}° 最大槍口側距 {maxLateral:0.00} m 判定 0.335={radiusOk}");
            Check("砲台模式在硬直結束後仍維持(按著扳機、不移動)", stillTurret, $"砲台={weapon.IsTurret}");

            // 移動 → 解除,射速回到 5F
            _intent.Move = new Vector2(0f, 1f);
            yield return Frames(4);
            bool released = !weapon.IsTurret;
            times.Clear();
            seen = weapon.ShotsFired;
            end = Time.time + 0.8f;
            while (Time.time < end)
            {
                AimAlong(Vector3.forward);
                yield return null;
                for (int i = seen; i < weapon.ShotsFired; i++)
                {
                    times.Add(weapon.LastShotTime);
                }
                seen = weapon.ShotsFired;
            }
            float separationAfter = reticle != null && reticle.Shown ? reticle.ScreenSeparation : -1f;
            _intent.Attack = false;
            _intent.Move = Vector2.zero;
            float normalInterval = times.Count > 1 ? (times[times.Count - 1] - times[0]) / (times.Count - 1) : 0f;
            Check("開始移動就解除砲台模式、射速回到 5F", released && Mathf.Abs(normalInterval - S3.FireIntervalSeconds)
                < S3.FireIntervalSeconds * 0.05f, $"解除={released} 間隔 {normalInterval * 1000f:0.0} ms");
            Check("雙準星:一般模式左右兩個、砲台模式合併成一個、解除後再分開",
                separationNormal > 3f && separationTurret < separationNormal * 0.1f && separationAfter > separationNormal * 0.8f,
                $"畫面間距 一般 {separationNormal:0.0} → 砲台 {separationTurret:0.0} → 解除 {separationAfter:0.0}(參考解析度像素)");
            yield return new WaitForSeconds(0.3f);
        }

        // ── 砲台模式 TTK 12F ──

        private IEnumerator MeasureTurretTtk()
        {
            DualiesWeapon weapon = Weapon;
            _tank.InfiniteInk = true;
            weapon.SpreadMultiplier = 0f;
            yield return MoveTo(new Vector3(-22f, 0.1f, -22f));
            AimAlong(Vector3.forward);
            yield return Frames(3);
            // 滾完的位置正前方 5 m 放假人(往 +x 滾 5 m)
            Vector3 landing = _player.transform.position + Vector3.right * DodgeRollModel.TotalDistance(S3.Roll);
            Vector3 muzzleOffset = MuzzleFlat(true) - new Vector3(_player.transform.position.x, 0f, _player.transform.position.z);
            TargetDummy dummy = SpawnDummy(new Vector3(landing.x, 0f, landing.z) + muzzleOffset + Vector3.forward * 5f);
            yield return Frames(2);
            _hits.Clear();
            int rolls0 = weapon.RollCount;
            _intent.Attack = true;
            _intent.Move = new Vector2(1f, 0f);
            _intent.PressJump();
            yield return WaitUntil(() => weapon.RollCount > rolls0, 0.5f);
            _intent.Move = Vector2.zero;
            float deadline = Time.time + 3f;
            bool turretAtFirstHit = false;
            while (dummy.IsAlive && Time.time < deadline)
            {
                AimAt(DummyCenter(dummy), true);
                yield return null;
                TrackDelta();
                if (_hits.Count == 1)
                {
                    turretAtFirstHit = weapon.IsTurret;
                }
            }
            _intent.Attack = false;
            Tally(dummy, out int hits, out bool allFull, out float first, out float kill);
            float expected = S3.TurretTtkFrames() / 60f;
            float tolerance = Mathf.Max(0.02f, _maxDeltaTime * 1.5f);
            Check($"砲台模式 TTK:{S3.ShotsToSplat()} 發 × {S3.TurretRepeatFrame}F → {S3.TurretTtkFrames()}F = {expected:0.000} s",
                turretAtFirstHit && kill > 0f && hits == S3.ShotsToSplat() && allFull
                && Mathf.Abs(kill - first - expected) <= tolerance,
                $"砲台={turretAtFirstHit} 命中 {hits} 發 全傷={allFull} 實測 {kill - first:0.000} s(容許 ±{tolerance:0.000})");
            Despawn(dummy);
            weapon.SpreadMultiplier = 1f;
            yield return WaitUntil(() => !weapon.IsRolling, 1f);
            yield return new WaitForSeconds(0.3f);
        }

        // ── 判定半徑:設定裡的一般 0.31 / 砲台 0.335 彈,擦邊 0.725 m(假人半徑 0.4)——砲台打得到、一般打不到 ──

        private IEnumerator MeasureTurretRadius()
        {
            TargetDummy dummy = SpawnDummy(new Vector3(-14f, 0f, -2f));
            yield return Frames(2);
            float radius = dummy.BodyRadius;
            float graze = radius + 0.5f * (S3.RadiusForPlayer + S3.TurretRadiusForPlayer);
            Vector3 origin = new Vector3(-14f - 5f, 1f, -2f + graze);
            _hits.Clear();
            int attackNormal = CombatLedger.NewAttackId();
            InkBullet.Fire(_config.Bullet, origin, Vector3.right, _inkling.Team, _inkling, attackNormal);
            yield return new WaitForSeconds(0.4f);
            bool normalHit = HitCount(dummy) > 0;
            _hits.Clear();
            int attackTurret = CombatLedger.NewAttackId();
            InkBullet.Fire(_config.TurretBullet, origin, Vector3.right, _inkling.Team, _inkling, attackTurret);
            yield return new WaitForSeconds(0.4f);
            bool turretHit = HitCount(dummy) > 0;
            Check($"對人判定:一般 {S3.RadiusForPlayer} 擦不到、砲台 {S3.TurretRadiusForPlayer} 擦得到(離假人軸 {graze:0.000} m)",
                !normalHit && turretHit && Mathf.Abs(_config.Bullet.RadiusForPlayer - S3.RadiusForPlayer) < 1e-4f,
                $"一般命中={normalHit} 砲台命中={turretHit}");
            Despawn(dummy);
        }

        // ── 空中翻滾:往下急墜、水平距離很短 ──

        private IEnumerator MeasureAirRoll()
        {
            DualiesWeapon weapon = Weapon;
            _tank.InfiniteInk = true;
            yield return MoveTo(new Vector3(-20f, 0.1f, -10f));
            AimAlong(new Vector3(0f, 0.8f, 1f));
            yield return Frames(3);
            float groundY = _player.transform.position.y;
            _intent.PressJump();
            float peak = groundY;
            float apexDeadline = Time.time + 1f;
            while (Time.time < apexDeadline)
            {
                yield return null;
                peak = Mathf.Max(peak, _player.transform.position.y);
                if (_controller.velocity.y <= 0.5f && _player.transform.position.y > groundY + 0.8f)
                {
                    break;
                }
            }
            float height = _player.transform.position.y - groundY;
            Vector3 p0 = _player.transform.position;
            int rolls0 = weapon.RollCount;
            _intent.Attack = true;
            _intent.Move = new Vector2(1f, 0f);
            _intent.PressJump();
            yield return WaitUntil(() => weapon.RollCount > rolls0, 0.5f);
            bool air = weapon.IsAirRoll;
            float start = weapon.RollStartTime;
            yield return WaitUntil(() => _controller.isGrounded, 1f);
            float fall = Time.time - start;
            // 落地後放開方向鍵(翻滾中本來就不吃搖桿):硬直結束那一幀若掉幀,推著方向鍵會多走一段,污染水平距離
            _intent.Move = Vector2.zero;
            yield return WaitUntil(() => !weapon.IsRolling, 1.5f);
            Vector3 p1 = _player.transform.position;
            _intent.Attack = false;
            _intent.Move = Vector2.zero;
            Vector3 flat = p1 - p0;
            flat.y = 0f;
            // 一般下落要 √(2h/g);急墜 30 m/s 只要起手 4F + h/30
            float normalFall = Mathf.Sqrt(2f * Mathf.Max(height, 0.1f) / 25f);
            Check("空中翻滾:往下急墜、過不了溝(水平距離遠小於地面翻滾)",
                air && height > 0.8f && fall < normalFall * 0.7f && flat.magnitude < 2.2f,
                $"空中={air} 起滾高度 {height:0.00} m 落地 {fall:0.00} s(一般下落 {normalFall:0.00} s)水平 {flat.magnitude:0.00} m");
            yield return new WaitForSeconds(0.3f);
        }

        // ── 墨不夠 7%:不翻滾、照常起跳,並上墨量鎖 ──

        private IEnumerator MeasureRollWithoutInk()
        {
            DualiesWeapon weapon = Weapon;
            yield return MoveTo(new Vector3(-16f, 0.1f, -14f));
            AimAlong(new Vector3(0f, 0.8f, 1f));
            _tank.InfiniteInk = false;
            Drain(_tank);
            _tank.RefillFull();
            _tank.TryConsume(0.96f);
            yield return Frames(2);
            float y0 = _player.transform.position.y;
            int rolls0 = weapon.RollCount;
            _intent.Attack = true;
            _intent.Move = new Vector2(1f, 0f);
            _intent.PressJump();
            float peak = y0;
            bool locked = false;
            for (int i = 0; i < 20; i++)
            {
                yield return null;
                peak = Mathf.Max(peak, _player.transform.position.y);
                locked |= _tank.IsLocked;
            }
            _intent.Attack = false;
            _intent.Move = Vector2.zero;
            Check("墨不夠 7%:不翻滾、照常起跳(失敗也上墨量鎖)", weapon.RollCount == rolls0 && peak - y0 > 0.4f && locked,
                $"翻滾={weapon.RollCount - rolls0} 跳高 {peak - y0:0.00} m 上鎖={locked}");
            _tank.RefillFull();
            yield return WaitUntil(() => _controller.isGrounded, 2f);
            yield return new WaitForSeconds(0.3f);
        }

        // ── 墨槽:單發耗墨 0.72%、一槽 138 發、墨量鎖 20F ──

        private IEnumerator MeasureInkTank()
        {
            DualiesWeapon weapon = Weapon;
            float perShot = S3.InkConsume;
            // 一幀長到墨量鎖(20F)在兩發之間過期,PlayerInkTank 就會在那一幀回墨(S3 固定 60 Hz 不會發生)。
            // 批次驗證時其他 Unity 行程搶 CPU 偶有 0.3 s 的卡頓:量測期間出現這種幀就整槽重量(最多 3 次),證據列出卡頓
            float lapseFrame = S3.InkLockSeconds - S3.FireIntervalSeconds - 1f / 60f;
            int shots = 0;
            int attempts = 0;
            float full = 1f;
            float tenShotLevel = -1f;
            float firstShotTime = -1f;
            float attemptMaxDelta = 0f;
            while (attempts < 3)
            {
                attempts++;
                _ground.ClearInk();
                yield return MoveTo(new Vector3(-14f, 0.1f, -22f));
                AimAlong(Vector3.forward);
                _tank.InfiniteInk = false;
                _tank.RefillFull();
                yield return Frames(3);
                int startShots = weapon.ShotsFired;
                full = _tank.Normalized;
                tenShotLevel = -1f;
                firstShotTime = -1f;
                attemptMaxDelta = 0f;
                float sweepStart = Time.time;
                _intent.Attack = true;
                float deadline = Time.time + S3.FullTankFireSeconds + 6f;
                while (Time.time < deadline)
                {
                    float yaw = Mathf.Sin((Time.time - sweepStart) * 0.9f) * 35f;
                    AimAlong(Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, -0.12f, 1f));
                    yield return null;
                    TrackDelta();
                    int fired = weapon.ShotsFired - startShots;
                    if (fired > 0)
                    {
                        attemptMaxDelta = Mathf.Max(attemptMaxDelta, Time.deltaTime);
                    }
                    if (fired > 0 && firstShotTime < 0f)
                    {
                        firstShotTime = weapon.LastShotTime;
                    }
                    if (fired == 10 && tenShotLevel < 0f)
                    {
                        tenShotLevel = _tank.Normalized;
                    }
                    if (_tank.Normalized < perShot - 1e-5f)
                    {
                        break;
                    }
                }
                shots = weapon.ShotsFired - startShots;
                if (attemptMaxDelta < lapseFrame)
                {
                    break;
                }
                _intent.Attack = false;
                Debug.Log($"[AUTOTEST] INFO 一槽量測第 {attempts} 次出現 {attemptMaxDelta * 1000f:0} ms 卡頓(墨量鎖在兩發間過期),重量");
                yield return new WaitForSeconds(0.5f);
            }
            _intent.Attack = false;
            float lastShotTime = weapon.LastShotTime;
            float released = _tank.Normalized;
            float measured = tenShotLevel >= 0f ? (full - tenShotLevel) / 10f : -1f;
            Check($"單發耗墨 {S3.InkConsume * 100f:0.00}%", Mathf.Abs(measured - S3.InkConsume) < S3.InkConsume * 0.01f,
                $"實測 {measured * 100f:0.000}%");
            Check($"一槽 {S3.ShotsPerTank} 發(連射期間每發上鎖、不回墨)", Mathf.Abs(shots - S3.ShotsPerTank) <= 1,
                $"實測 {shots} 發(第 {attempts} 次量測,最長幀 {attemptMaxDelta * 1000f:0} ms)");
            float fireSeconds = lastShotTime - firstShotTime;
            float expectedSeconds = (S3.ShotsPerTank - 1) * S3.FireIntervalSeconds;
            Check($"一槽連射秒數 ≈ {S3.FullTankFireSeconds:0.00} s", Mathf.Abs(fireSeconds - expectedSeconds)
                <= S3.FireIntervalSeconds + 0.05f, $"首發到末發 {fireSeconds:0.00} s(期望 {expectedSeconds:0.00} s)");
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
            float tolerance = Mathf.Max(0.034f, _maxDeltaTime * 2f);
            Check($"墨量鎖 {S3.InkRecoverStop}F = {S3.InkLockSeconds:0.000} s",
                unlockedAt > 0f && Mathf.Abs(lockMeasured - S3.InkLockSeconds) <= tolerance,
                $"最後一發後 {lockMeasured:0.000} s 開始回墨(容許 ±{tolerance:0.000})");
            _tank.RefillFull();
            yield return new WaitForSeconds(0.3f);
        }

        // ── 單發塗地(平射,散布關閉),與 Splattershot 同條件比 ──

        private IEnumerator MeasurePaintPerShotBaseline()
        {
            float area = 0f;
            yield return PaintPerShot(a => area = a);
            _paintPerShotBaseline = area;
        }

        private IEnumerator MeasurePaintPerShot()
        {
            float area = 0f;
            yield return PaintPerShot(a => area = a);
            // S3 估計(不計重疊):落點橢圓(平均半寬 × 平均拉長)+ 墨滴;雙槍 ≈ Splattershot 的 0.77
            float dualies = EstimatedPaintArea(0.5f * (S3.WidthHalfNear + S3.WidthHalfFar),
                0.5f * (S3.DepthScaleMax + S3.DepthScaleMin), S3.SpawnNum, S3.DropletWidthHalf, 0f, 0f);
            S3ShooterParams shot = S3ShooterParams.Splattershot;
            float reference = shot.EstimatedPaintAreaPerShot;
            float s3Ratio = dualies / reference;
            float ratio = _paintPerShotBaseline > 0f ? area / _paintPerShotBaseline : 0f;
            Check($"單發塗地(相對 Splattershot ≈ S3 估計 ×{s3Ratio:0.00})",
                area > 0f && Mathf.Abs(ratio - s3Ratio) <= s3Ratio * 0.25f,
                $"雙槍 {TurfPoints.FromArea(area):0.0}p({area:0.0} m²)/ Splattershot {TurfPoints.FromArea(_paintPerShotBaseline):0.0}p" +
                $" = ×{ratio:0.00}");
            // 一槽塗地:單發 × 一槽發數(雙槍 138 發 vs 108 發)
            float tankRatio = _paintPerShotBaseline > 0f
                ? area * S3.ShotsPerTank / (_paintPerShotBaseline * shot.ShotsPerTank)
                : 0f;
            Debug.Log($"[AUTOTEST] INFO Splat Dualies 一槽塗地(無重疊)相對 Splattershot ×{tankRatio:0.00}" +
                      $"(S3 估計 ×{s3Ratio * S3.ShotsPerTank / shot.ShotsPerTank:0.00})");
        }

        private static float EstimatedPaintArea(float width, float depth, float spawnNum, float droplet, float feetRate,
            float feet)
        {
            return Mathf.PI * width * width * depth + spawnNum * Mathf.PI * droplet * droplet * 1.1f
                                                    + feetRate * Mathf.PI * feet * feet;
        }

        // 連續 7 發各量一次(每發前清掉地面,互不重疊;7 = 雙槍墨滴樣式數,每種相位剛好各一發,
        // 落點超過射程的樣式就沒有墨滴——平均才是 S3 的「每發」);換裝前量 = Splattershot(InkShooter),換裝後 = 雙槍
        private IEnumerator PaintPerShot(System.Action<float> report)
        {
            const int Shots = 7;
            _tank.InfiniteInk = true;
            var shooter = _loadout.Main as InkShooter;
            var dualies = _loadout.Main as DualiesWeapon;
            if (shooter != null)
            {
                shooter.SpreadMultiplier = 0f;
            }
            if (dualies != null)
            {
                dualies.SpreadMultiplier = 0f;
            }
            float area = 0f;
            int shots = 0;
            for (int i = 0; i < Shots; i++)
            {
                _ground.ClearInk();
                yield return MoveTo(new Vector3(-16f, 0.1f, -23f));
                AimAlong(Vector3.forward);
                yield return new WaitForSeconds(0.15f);
                AimAlong(Vector3.forward);
                float before = _inkling.TurfInkedArea;
                int shotsBefore = shooter != null ? shooter.ShotsFired : dualies != null ? dualies.ShotsFired : 0;
                _intent.Attack = true;
                // 雙槍第一發要 3F:按到出第一發為止
                float until = Time.time + 0.5f;
                while (Time.time < until)
                {
                    yield return null;
                    int now = shooter != null ? shooter.ShotsFired : dualies != null ? dualies.ShotsFired : 0;
                    if (now > shotsBefore)
                    {
                        break;
                    }
                }
                _intent.Attack = false;
                // 墨彈約 0.2 s 落地、沿路墨滴即時落點
                yield return new WaitForSeconds(0.8f);
                area += _inkling.TurfInkedArea - before;
                int after = shooter != null ? shooter.ShotsFired : dualies != null ? dualies.ShotsFired : 0;
                shots += after - shotsBefore;
            }
            _ground.ClearInk();
            if (shooter != null)
            {
                shooter.SpreadMultiplier = 1f;
            }
            if (dualies != null)
            {
                dualies.SpreadMultiplier = 1f;
            }
            report(shots > 0 ? area / shots : 0f);
        }

        // ── bot 掛勾驅動(模擬 BotBrain 交戰):射程內按住開火、邊橫移;被打 → 沿橫移方向翻滾;要真的打中 ──

        private IEnumerator MeasureBotStyleEngagement()
        {
            DualiesWeapon weapon = Weapon;
            weapon.SpreadMultiplier = 1f;
            _tank.InfiniteInk = true;
            yield return MoveTo(new Vector3(-16f, 0.1f, -22f));
            AimAlong(Vector3.forward);
            yield return Frames(3);
            TargetDummy dummy = SpawnDummy(MuzzleFlat(false) + Vector3.forward * (S3.EffectiveRange * 0.7f));
            yield return Frames(2);
            _hits.Clear();
            int startShots = weapon.ShotsFired;
            int rolls0 = weapon.RollCount;
            int held = 0;
            bool hurtApplied = false;
            float start = Time.time;
            float end = Time.time + 2.2f;
            var health = _player.GetComponent<InklingHealth>();
            // 假人被擊倒後會隱形重生,迴圈照跑到時間到(翻滾要在被打之後才會發生)
            while (Time.time < end)
            {
                Vector3 center = DummyCenter(dummy);
                AimAt(center, weapon.IsTurret);
                var context = new BotCombatContext
                {
                    Distance = Vector3.Distance(weapon.MuzzleCenter(false), center),
                    TargetVisible = true,
                    Reacted = true,
                    AimError = 0f,
                    FireTolerance = 4f,
                };
                _intent.Attack = weapon.BotAttackIntent(context);
                // BotBrain 的橫移
                _intent.Move = new Vector2(Mathf.Sin((Time.time - start) * 3f) >= 0f ? 0.8f : -0.8f, 0f);
                held += _intent.Attack ? 1 : 0;
                if (!hurtApplied && Time.time - start > 0.15f && health != null)
                {
                    // 被敵人打到(BotBrain 交戰時:被打就翻滾閃躲)
                    health.ApplyDamage(new DamageInfo(12f, _player.transform.position + Vector3.up, Team.Bravo, null,
                        DamageKind.Main));
                    hurtApplied = true;
                }
                yield return null;
                TrackDelta();
            }
            _intent.Attack = false;
            _intent.Move = Vector2.zero;
            int hits = HitCount(dummy);
            Check("bot 掛勾驅動:射程內按住開火並命中、被打就沿橫移方向翻滾(與玩家同一條翻滾路徑)",
                held > 0 && weapon.ShotsFired > startShots && hits > 0 && weapon.RollCount > rolls0,
                $"按住 {held} 幀、出膛 {weapon.ShotsFired - startShots} 發、命中 {hits} 發、翻滾 {weapon.RollCount - rolls0} 次");
            Despawn(dummy);
            yield return WaitUntil(() => !weapon.IsRolling, 1.5f);
            yield return new WaitForSeconds(0.3f);
        }

        // ── 潛墨:兩把槍模跟著人形一起消失、浮出再出現 ──

        private IEnumerator MeasureSubmergedVisibility()
        {
            _tank.InfiniteInk = true;
            yield return MoveTo(new Vector3(-18f, 0.1f, -6f));
            InkPainter.PaintSphere(new Vector3(-18f, 0f, -6f), 3f, 1f, _inkling.InkSource(false));
            yield return Frames(3);
            _intent.Squid = true;
            yield return new WaitForSeconds(0.8f);
            DualiesGunPair guns = Weapon != null ? Weapon.Guns : null;
            bool hidden = guns != null && !guns.Visible;
            bool reticleHidden = Weapon != null && Weapon.Reticle != null && !Weapon.Reticle.Shown;
            _intent.Squid = false;
            yield return new WaitForSeconds(0.8f);
            bool shown = guns != null && guns.Visible;
            Check("潛墨時兩把槍模與雙準星一起隱藏、浮出後出現", hidden && shown && reticleHidden,
                $"潛墨隱藏={hidden} 準星隱藏={reticleHidden} 浮出顯示={shown}");
        }

        // ── 收尾:換回 Splattershot,內建槍身恢復 ──

        private void Cleanup()
        {
            if (_intent != null)
            {
                _intent.Attack = false;
                _intent.Move = Vector2.zero;
                _intent.Squid = false;
            }
            DualiesGunPair guns = Weapon != null ? Weapon.Guns : null;
            Transform right = guns != null ? guns.Right : null;
            if (_loadout != null && _baseKit != null && _loadout.Kit != _baseKit)
            {
                _loadout.Equip(_baseKit);
                Renderer barrel = _visualComponent != null && _visualComponent.WeaponModel != null
                    ? _visualComponent.WeaponModel.GetComponent<Renderer>()
                    : null;
                Check("換回 Splattershot:內建槍身恢復、雙槍槍模移除",
                    _loadout.Main is InkShooter && barrel != null && !barrel.forceRenderingOff,
                    $"主武器={(_loadout.Main != null ? _loadout.Main.GetType().Name : "無")} " +
                    $"內建隱藏={(barrel != null && barrel.forceRenderingOff)} 右槍模待銷毀={(right != null)}");
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

        // 瞄準:視線從兩把槍口中點出發,朝 direction;視覺根直接轉到該方向(槍口位置才對)
        private void AimAlong(Vector3 direction)
        {
            Vector3 flat = new Vector3(direction.x, 0f, direction.z);
            if (flat.sqrMagnitude > 1e-6f)
            {
                _visual.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
            }
            _aim.Forward = direction.normalized;
            DualiesWeapon weapon = Weapon;
            _aim.Origin = weapon != null ? weapon.MuzzleCenter(weapon.IsTurret)
                : _player.transform.position + Vector3.up * 1.05f;
        }

        private void AimAt(Vector3 point, bool turret)
        {
            Vector3 flat = point - _player.transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude > 1e-6f)
            {
                _visual.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
            }
            DualiesWeapon weapon = Weapon;
            Vector3 origin = weapon != null ? weapon.MuzzleCenter(turret) : _player.transform.position + Vector3.up * 1.05f;
            _aim.Origin = origin;
            _aim.Forward = (point - origin).normalized;
        }

        // 兩把槍口中點在地面的投影(射程從槍口量)
        private Vector3 MuzzleFlat(bool turret)
        {
            DualiesWeapon weapon = Weapon;
            Vector3 p = weapon != null ? weapon.MuzzleCenter(turret) : _player.transform.position;
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
            go.name = "DualiesDummy";
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

        private void Tally(TargetDummy dummy, out int hits, out bool allFull, out float first, out float kill)
        {
            hits = 0;
            allFull = true;
            first = -1f;
            kill = -1f;
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

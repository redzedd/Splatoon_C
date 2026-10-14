using System.Collections;
using System.Collections.Generic;
using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // Tacticooler(D2)驗收,試射場西南角。全部走真實路徑(按 R 發動、按住/放開副武器鍵丟出、飲料機落地供應):
    //   (a) 發動:舉著罐子、主武器被佔用、不能潛游、放開副武器鍵會丟;舉著不限時(能量不動)
    //   (b) 放開 → 13F 後罐子才離手;離手時能量開始鎖定、行為結束;鎖定倒數結束 = 展開完成起 600F
    //   (c) 飲料機:擋雙方的墨(敵方/我方子彈停在機身、敵方爆炸炸不到另一側的隊友)、打不壞(不在可受傷清單)
    //   (d) 供應:展開(30F)前拿不到;水平 7(+ 身體半徑)內、往上 3 內才拿得到;烏賊態拿不到;敵人拿不到;
    //       一台 4 罐、一人一罐;增益 = 1020F 的 RSU/SSU 29、Intensify/QSJ/QR/Special Saver/墨抗 57,飲用者發光
    //   (e) 增益實測:被敵人擊倒的重生等待 7.5 → 3.5 秒(QR 57 省 4 秒)、大招不減(Special Saver 57)、死亡後增益消失
    //   (f) 丟的人被擊倒:鎖定照樣倒數、不減半;新的一台立在 1.8 u 內 → 舊的消失、能量立刻歸 0
    //   (g) 飲料機 900F 後消失
    [AutoTest(Scene = AutoTestAttribute.TrainingScene, Timeout = 150f, Order = 240)]
    public sealed class TacticoolerAutoTest : AutoTestBase
    {
        private readonly List<Object> _spawned = new List<Object>();
        private int _errors;
        private string _firstError;
        private float _maxFrame = 1f;

        private GameObject _playerGo;
        private Inkling _player;
        private ScriptedIntent _intent;
        private PlayerSpecialGauge _gauge;
        private InklingHealth _health;
        private ThrowController _thrower;
        private SquidController _squid;
        private TacticoolerSpecial _special;
        private TacticoolerConfig _config;
        private SpecialTestAim _aim;

        private float Slack => Mathf.Max(2f, _maxFrame) / 60f;

        public static void Run()
        {
            Launch<TacticoolerAutoTest>();
        }

        private void OnEnable()
        {
            Application.logMessageReceived += OnLog;
        }

        private void OnDisable()
        {
            Application.logMessageReceived -= OnLog;
        }

        private void OnLog(string message, string stack, LogType type)
        {
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

        private void Update()
        {
            _maxFrame = Mathf.Max(_maxFrame, Time.deltaTime * 60f);
        }

        private IEnumerator Start()
        {
            _playerGo = FindPlayer();
            _player = _playerGo != null ? _playerGo.GetComponent<Inkling>() : null;
            _config = Resources.Load<TacticoolerConfig>("Weapons/Special_tacticooler");
            if (_player == null || _config == null || _config.DevicePrefab == null || _config.AuraPrefab == null)
            {
                FailSetup("找不到 Player / Resources/Weapons/Special_tacticooler(先跑 M7 Setup)");
                yield break;
            }
            _intent = ScriptedIntent.Drive(_player);
            _gauge = _player.GetComponent<PlayerSpecialGauge>();
            _health = _player.GetComponent<InklingHealth>();
            _thrower = _player.GetComponent<ThrowController>();
            _squid = _player.GetComponent<SquidController>();
            IAimView originalView = _player.AimView;
            _aim = new SpecialTestAim();
            _player.AimView = _aim;
            if (!SpecialTestKit.EquipSpecial(_player, _config))
            {
                FailSetup("換裝 Tacticooler 失敗");
                yield break;
            }
            _special = _player.GetComponent<TacticoolerSpecial>();
            _health.Invulnerable = true;

            yield return HoldAndThrow();
            if (_special.Device != null && _special.Device.IsDeployed)
            {
                yield return ServeAndBlock(_special.Device);
            }
            else
            {
                Check("飲料機落地", false, "沒有落地的飲料機");
            }

            Check("全程無例外/錯誤 log", _errors == 0, _errors == 0 ? "0" : $"{_errors} 筆,第一筆:{_firstError}");
            foreach (Object obj in _spawned)
            {
                if (obj != null)
                {
                    Destroy(obj is Component c ? c.gameObject : obj);
                }
            }
            _health.Invulnerable = false;
            _player.AimView = originalView;
            _player.GetComponent<PlayerInputRouter>().ClearOverrideSource();
            _player.Teleport(new Vector3(0f, 0.1f, 0f), 0f);
            Finish();
        }

        private Inkling Clone(Vector3 position, string name, Team team, out ScriptedIntent intent)
        {
            Inkling clone = SpecialTestKit.CloneEnemy(_playerGo, position, name, out intent);
            clone.Configure(team, name, false);
            clone.GetComponent<InklingHealth>().Invulnerable = true;
            _spawned.Add(clone);
            return clone;
        }

        private IEnumerator HoldAndThrow()
        {
            Vector3 p = new Vector3(-20f, 0f, -18f);
            _player.Teleport(p + Vector3.up * 0.1f, 90f);
            _aim.From(_player.transform, p + new Vector3(9f, 0f, 0f));
            yield return new WaitForSeconds(0.4f);
            _gauge.SetPointsForTest(100f);
            _intent.PressSpecial();
            yield return WaitUntil(() => _gauge.IsActive, 1f);
            _intent.Squid = true;
            yield return new WaitForSeconds(0.8f);
            bool squidBlocked = !_squid.IsSquid;
            _intent.Squid = false;
            yield return new WaitForSeconds(0.2f);
            Check("發動:舉著罐子、主武器被佔用、不能潛游、放開副武器鍵會丟;舉著不限時(能量不動)",
                _gauge.IsActive && _special.ModelShown && _special.BlocksMainWeapon && squidBlocked
                && _thrower.WillThrowDevice && Mathf.Approximately(_gauge.Points, 100f) && !_gauge.IsLocked,
                $"啟動={_gauge.IsActive} 模型={_special.ModelShown} 烏賊被擋={squidBlocked} 會丟={_thrower.WillThrowDevice} " +
                $"能量={_gauge.Points:0.0}");

            _intent.Sub = true;
            yield return new WaitForSeconds(0.3f);
            _intent.Sub = false;
            yield return WaitUntil(() => _special.ThrowCount > 0, 1f);
            float lag = (_special.LastLaunchTime - _special.LastReleaseTime) * 60f;
            bool lockedAtLaunch = _gauge.IsLocked && !_gauge.IsActive;
            Check("放開副武器鍵 13F 後罐子離手;離手時行為結束、能量開始鎖定",
                _special.ThrowCount == 1 && lag >= 13f - 0.01f && lag <= 13f + Mathf.Max(2f, _maxFrame) && lockedAtLaunch,
                $"放開 → 離手 {lag:0.0}F(S3 13F);鎖定={lockedAtLaunch} 行為啟動={_gauge.IsActive}");

            TacticoolerDevice device = _special.Device;
            yield return WaitUntil(() => device != null && device.IsDeployed, 3f);
            if (device == null || !device.IsDeployed)
            {
                yield break;
            }
            float flight = device.PlacedAt - device.ThrownAt;
            float lockRemaining = _gauge.LockRemainingSeconds;
            float expected = _config.UnfoldSeconds + _config.LockSeconds;
            Vector3 flat = device.transform.position - p;
            flat.y = 0f;
            Check("飛行與鎖定:0.4 u/F 往前拋出數公尺;落地時鎖定剩 展開 0.5 + 600F(= 預測飛行時間吻合)",
                Mathf.Abs(lockRemaining - expected) <= 3f / 60f + Slack && flat.magnitude > 5f && flat.magnitude < 13f
                && Mathf.Abs(flight - _special.LastPredictedFlight) <= Slack,
                $"飛 {flight * 60f:0.0}F(預測 {_special.LastPredictedFlight * 60f:0.0}F)、{flat.magnitude:0.00} m;" +
                $"鎖定剩 {lockRemaining:0.000} s(應 {expected:0.000})");
        }

        private IEnumerator ServeAndBlock(TacticoolerDevice device)
        {
            Vector3 s = device.transform.position;
            // 丟的人先走遠(別佔掉罐子)
            _player.Teleport(s + new Vector3(-16f, 0.1f, 0f));
            float placedAt = device.PlacedAt;
            // (d-1) 展開前拿不到
            Inkling early = Clone(s + new Vector3(2f, 0.1f, 2f), "CoolerEarly", Team.Alpha, out _);
            Inkling enemy = Clone(s + new Vector3(-2f, 0.1f, 2.5f), "CoolerEnemy", Team.Bravo, out _);
            yield return new WaitForSeconds(0.25f);
            bool beforeUnfold = !early.Modifiers.Has(TacticoolerDevice.BuffSource) && device.DrinksLeft == 4;
            yield return WaitUntil(() => early.Modifiers.Has(TacticoolerDevice.BuffSource), 1.5f);
            float drankAt = (Time.time - placedAt) * 60f;
            Check("展開完(30F)才供應:之前在範圍內也拿不到",
                beforeUnfold && early.Modifiers.Has(TacticoolerDevice.BuffSource)
                && drankAt >= _config.UnfoldSeconds * 60f - 0.5f && drankAt <= _config.UnfoldSeconds * 60f + 2f + _maxFrame,
                $"展開前拿不到={beforeUnfold};拿到在落地後 {drankAt:0.0}F(展開 {_config.UnfoldSeconds * 60f:0}F)");

            // (d-2) 增益內容
            InklingModifiers m = early.Modifiers;
            float remaining = m.Remaining(TacticoolerDevice.BuffSource);
            bool abilities = m.AbilityPoints(Ability.RunSpeedUp) == 29 && m.AbilityPoints(Ability.SwimSpeedUp) == 29
                             && m.AbilityPoints(Ability.IntensifyAction) == 57 && m.AbilityPoints(Ability.QuickSuperJump) == 57
                             && m.AbilityPoints(Ability.QuickRespawn) == 57 && m.AbilityPoints(Ability.SpecialSaver) == 57
                             && m.AbilityPoints(Ability.InkResistanceUp) == 57;
            Check("增益:1020F、RSU/SSU 29、Intensify/QSJ/QR/Special Saver/墨抗 57;飲用者發光",
                abilities && remaining > 1020f / 60f - 0.5f && remaining <= 1020f / 60f + 0.01f
                && TacticoolerAura.IsGlowing(early) && (m.Status & StatusFlags.Buffed) != 0,
                $"剩 {remaining:0.00} s;RSU {m.AbilityPoints(Ability.RunSpeedUp)} SSU {m.AbilityPoints(Ability.SwimSpeedUp)} " +
                $"QR {m.AbilityPoints(Ability.QuickRespawn)} SpS {m.AbilityPoints(Ability.SpecialSaver)} " +
                $"墨抗 {m.AbilityPoints(Ability.InkResistanceUp)};發光={TacticoolerAura.IsGlowing(early)}");

            // (d-3) 範圍:水平 7.6 拿不到、6.9 拿得到;烏賊態拿不到;敵人拿不到
            Inkling far = Clone(s + new Vector3(7.6f, 0.1f, 0f), "CoolerFar", Team.Alpha, out _);
            // 烏賊那位先在範圍外變烏賊,再搬進範圍(生成當幀是人形,會先拿到)
            Inkling squid = Clone(s + new Vector3(0f, 0.1f, -14f), "CoolerSquid", Team.Alpha, out ScriptedIntent squidIntent);
            squidIntent.Squid = true;
            yield return new WaitForSeconds(0.3f);
            squid.Teleport(s + new Vector3(0f, 0.1f, -3f));
            yield return new WaitForSeconds(0.5f);
            bool farNo = !far.Modifiers.Has(TacticoolerDevice.BuffSource);
            bool squidNo = !squid.Modifiers.Has(TacticoolerDevice.BuffSource) && squid.GetComponent<SquidController>().IsSquid;
            far.Teleport(s + new Vector3(6.9f, 0.1f, 0f));
            squidIntent.Squid = false;
            yield return new WaitForSeconds(0.4f);
            bool farYes = far.Modifiers.Has(TacticoolerDevice.BuffSource);
            bool squidYes = squid.Modifiers.Has(TacticoolerDevice.BuffSource);
            bool enemyNo = !enemy.Modifiers.Has(TacticoolerDevice.BuffSource);
            Check("供應範圍:水平 7.6 m 拿不到、6.9 m 拿得到;烏賊態拿不到、變回人形就拿到;敵人拿不到",
                farNo && farYes && squidNo && squidYes && enemyNo && device.DrinksLeft == 1,
                $"7.6 m={!farNo} 6.9 m={farYes} 烏賊={!squidNo} 人形={squidYes} 敵人={!enemyNo} 剩 {device.DrinksLeft} 罐");

            // (d-4) 往上 3:站在 3.4 m 高台拿不到、2.8 m 拿得到;4 罐喝完後第 5 人拿不到;喝過的人回來不能再拿
            GameObject high = Platform(s + new Vector3(0f, 0f, 4.5f), 3.4f);
            Inkling tall = Clone(s + new Vector3(0f, 3.5f, 4.5f), "CoolerHigh", Team.Alpha, out _);
            yield return new WaitForSeconds(0.5f);
            bool highNo = !tall.Modifiers.Has(TacticoolerDevice.BuffSource) && tall.transform.position.y > s.y + 3.1f;
            high.transform.localScale = new Vector3(2f, 2.8f, 2f);
            high.transform.position = s + new Vector3(0f, 1.4f, 4.5f);
            Physics.SyncTransforms();
            tall.Teleport(s + new Vector3(0f, 2.9f, 4.5f));
            yield return new WaitForSeconds(0.5f);
            bool highYes = tall.Modifiers.Has(TacticoolerDevice.BuffSource);
            Inkling fifth = Clone(s + new Vector3(-2.5f, 0.1f, -1f), "CoolerFifth", Team.Alpha, out _);
            early.Modifiers.Remove(TacticoolerDevice.BuffSource);
            yield return new WaitForSeconds(0.4f);
            Check("往上 3 m:3.4 m 高台拿不到、2.8 m 拿得到;4 罐喝完第 5 人拿不到;喝過的人回來也不能再拿",
                highNo && highYes && device.DrinksLeft == 0 && device.DrinkerCount == 4
                && !fifth.Modifiers.Has(TacticoolerDevice.BuffSource) && !early.Modifiers.Has(TacticoolerDevice.BuffSource),
                $"3.4 m={!highNo} 2.8 m={highYes} 剩 {device.DrinksLeft} 罐、喝過 {device.DrinkerCount} 人、第 5 人={fifth.Modifiers.Has(TacticoolerDevice.BuffSource)} " +
                $"重喝={early.Modifiers.Has(TacticoolerDevice.BuffSource)}");

            // (c) 擋雙方的墨:敵方與我方子彈都停在機身;敵方爆炸炸不到另一側的隊友;打不壞
            yield return BlockInk(device, s, enemy, far);

            // (e) 增益實測:被敵人擊倒 → 重生等待 3.5 秒、大招不減、增益消失
            yield return BuffOnDeath(far, squid, enemy);

            // (f) 丟的人被擊倒:鎖定照樣倒數、不減半
            _health.Invulnerable = false;
            float before = _gauge.Points;
            float lockLeft = _gauge.LockRemainingSeconds;
            _health.ApplyDamage(new DamageInfo(200f, _player.transform.position + Vector3.up, Team.Bravo, enemy,
                DamageKind.Main));
            yield return new WaitForSeconds(1f);
            float after = _gauge.Points;
            float expectedAfter = lockLeft > 0f ? before * Mathf.Max(0f, lockLeft - 1f) / lockLeft : 0f;
            var life = _player.GetComponent<InklingLifecycle>();
            Check("丟的人被擊倒:鎖定照樣倒數、不減半",
                life.IsDead && _gauge.IsLocked && Mathf.Abs(after - expectedAfter) < 2.5f,
                $"死亡={life.IsDead};能量 {before:0.0} → 1 秒後 {after:0.0}(照倒數應 {expectedAfter:0.0},減半會是 {before * 0.5f:0.0})");
            _health.Invulnerable = true;

            // (f) 新的一台立在 1.8 u 內 → 舊的消失、丟的人能量立刻歸 0
            TacticoolerDevice second = SharedPool.Spawn(_config.DevicePrefab);
            second.Throw(s + new Vector3(1f, 2.5f, -1f), Vector3.down * 2f, _config, early, null, 0);
            yield return WaitUntil(() => second.IsDeployed, 2f);
            yield return null;
            bool firstGone = !device.IsDeployed;
            Check("新的一台立在 1.8 u 內 → 舊的消失、丟的人能量鎖定立刻歸 0",
                second.IsDeployed && firstGone && !_gauge.IsLocked && _gauge.Points < 0.01f,
                $"新={second.IsDeployed} 舊還在={!firstGone} 鎖定={_gauge.IsLocked} 能量={_gauge.Points:0.00}");

            // (g) 900F 後消失
            float secondPlaced = second.PlacedAt;
            yield return WaitUntil(() => !second.IsDeployed, 17f);
            float life2 = (Time.time - secondPlaced) * 60f;
            Check("飲料機存在 900F(15 s)後消失", !second.IsDeployed && Mathf.Abs(life2 - 900f) <= 2f + _maxFrame,
                $"存在 {life2:0.0}F");
            yield return WaitUntil(() => !life.IsDead, 8f);
        }

        private GameObject Platform(Vector3 foot, float height)
        {
            GameObject platform = GameObject.CreatePrimitive(PrimitiveType.Cube);
            platform.name = "CoolerPlatform";
            platform.transform.position = foot + Vector3.up * (height * 0.5f);
            platform.transform.localScale = new Vector3(2f, height, 2f);
            _spawned.Add(platform);
            Physics.SyncTransforms();
            return platform;
        }

        private IEnumerator BlockInk(TacticoolerDevice device, Vector3 s, Inkling enemy, Inkling ally)
        {
            var ends = new List<InkBulletImpact>();
            InkBulletImpactHandler handler = (in InkBulletImpact impact) => ends.Add(impact);
            InkBullet.Impacted += handler;
            Vector3 aimPoint = s + Vector3.up * 1f;
            Vector3 enemyFrom = s + new Vector3(-6f, 1f, 0.2f);
            Vector3 allyFrom = s + new Vector3(6f, 1f, -0.2f);
            InkBullet enemyShot = InkBullet.Fire(InkBulletSpec.Splattershot, enemyFrom, aimPoint - enemyFrom, Team.Bravo, enemy);
            InkBullet allyShot = InkBullet.Fire(InkBulletSpec.Splattershot, allyFrom, aimPoint - allyFrom, Team.Alpha, ally);
            yield return new WaitForSeconds(0.4f);
            InkBullet.Impacted -= handler;
            bool enemyStopped = false;
            bool allyStopped = false;
            foreach (InkBulletImpact impact in ends)
            {
                bool atStand = impact.End == InkBulletEnd.Terrain
                               && new Vector2(impact.Point.x - s.x, impact.Point.z - s.z).magnitude < 1.2f;
                if (impact.Bullet == enemyShot && impact.Team == Team.Bravo)
                {
                    enemyStopped |= atStand;
                }
                if (impact.Bullet == allyShot && impact.Team == Team.Alpha)
                {
                    allyStopped |= atStand;
                }
            }
            // 敵方爆炸在機身一側,隊友在另一側:視線被機身擋住
            ally.Teleport(s + new Vector3(1.5f, 0.1f, 0f));
            var allyHealth = ally.GetComponent<InklingHealth>();
            allyHealth.Invulnerable = false;
            allyHealth.ResetForRespawn();
            yield return new WaitForSeconds(0.1f);
            ExplosionSpec blast = ExplosionSpec.SplatBomb;
            blast.PaintRadius = 0f;
            blast.PaintRayCount = 0;
            ExplosionResult result = Explosion.Detonate(blast, s + new Vector3(-1.4f, 0.5f, 0f), Team.Bravo, enemy);
            bool allyUnhurt = allyHealth.Hp >= allyHealth.MaxHp - 0.01f;
            allyHealth.Invulnerable = true;
            bool indestructible = device.IsDeployed && !DamageableRegistry.Active.Contains(device);
            Check("飲料機擋雙方的墨:敵方與我方子彈都停在機身;敵方爆炸炸不到另一側的隊友;打不壞(不在可受傷清單)",
                enemyStopped && allyStopped && allyUnhurt && result.BlockedByCover >= 1 && indestructible,
                $"敵彈停={enemyStopped} 我彈停={allyStopped} 隊友 HP {allyHealth.Hp:0.0} 被擋 {result.BlockedByCover} 個;" +
                $"不在可受傷清單={indestructible}");
        }

        private IEnumerator BuffOnDeath(Inkling buffed, Inkling plain, Inkling enemy)
        {
            // plain(squid 那位)也喝過 → 先移除它的增益當對照
            plain.Modifiers.Remove(TacticoolerDevice.BuffSource);
            var buffedGauge = buffed.GetComponent<PlayerSpecialGauge>();
            var plainGauge = plain.GetComponent<PlayerSpecialGauge>();
            buffedGauge.SetPointsForTest(80f);
            plainGauge.SetPointsForTest(80f);
            var buffedLife = buffed.GetComponent<InklingLifecycle>();
            var plainLife = plain.GetComponent<InklingLifecycle>();
            var buffedHealth = buffed.GetComponent<InklingHealth>();
            var plainHealth = plain.GetComponent<InklingHealth>();
            buffedHealth.Invulnerable = false;
            plainHealth.Invulnerable = false;
            buffedHealth.ApplyDamage(new DamageInfo(200f, buffed.transform.position + Vector3.up, Team.Bravo, enemy,
                DamageKind.Main));
            plainHealth.ApplyDamage(new DamageInfo(200f, plain.transform.position + Vector3.up, Team.Bravo, enemy,
                DamageKind.Main));
            yield return null;
            float buffedDelay = buffedLife.RespawnAt - buffedLife.DiedAt;
            float plainDelay = plainLife.RespawnAt - plainLife.DiedAt;
            float buffedKeep = buffedGauge.Points;
            float plainKeep = plainGauge.Points;
            bool cleared = !buffed.Modifiers.Has(TacticoolerDevice.BuffSource);
            yield return new WaitForSeconds(0.2f);
            bool auraGone = !TacticoolerAura.IsGlowing(buffed);
            Check("增益實測:被敵人擊倒重生等待 7.5 → 3.5 s(QR 57 省 4 s)、大招不減(Special Saver 57)、死亡後增益與發光消失",
                buffedLife.IsDead && Mathf.Abs(buffedDelay - 3.5f) < 0.05f && Mathf.Abs(plainDelay - 7.5f) < 0.05f
                && Mathf.Abs(buffedKeep - 80f) < 0.01f && Mathf.Abs(plainKeep - 40f) < 0.01f && cleared && auraGone,
                $"重生等待 有增益 {buffedDelay:0.00} s / 沒有 {plainDelay:0.00} s;大招 80 → {buffedKeep:0.0} / {plainKeep:0.0};" +
                $"增益清除={cleared} 發光消失={auraGone}");
        }
    }
}

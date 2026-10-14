using System.Collections;
using System.Collections.Generic;
using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.AI;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // Ink Vac(D2)驗收,試射場西半部。全部走真實路徑(按 R 發動、按住/放開攻擊鍵、真的墨彈與爆炸):
    //   (a) 發動 17F 後可吸;沒按攻擊可以潛游;從烏賊按攻擊 → 變回人形、18F 後開始吸;錐長 +0.3334 u/F、45F 到 15 u
    //   (b) 吸走敵方墨彈(不傷人、容量 += 該發傷害 36)與錐內的敵方爆炸(整個爆炸被吸走、容量 += 180);背後的照樣打到
    //   (c) 漩渦裡的敵人不受傷、移動 ×0.5、每秒扣 13.2% 墨,容量每幀 +1.5;敵方裝置每幀 2 傷
    //   (d) 走速隨吸入量 0.09 → 0.07 u/F(半滿 0.08 = 4.8 m/s)
    //   (e) 吸滿 1100 → 發射模式,20F 內不能射;放開發射:初速 0.7 u/F(吸滿)、直擊 220;落地後 50F 爆炸 220,
    //       半徑 11(吸滿)內的敵人吃 220、外面的沒事;bot(BotBrain)在圈內 → 50F 內跑出圈(BotHazards)
    //   (f) 沒吸:能量 360F 用完 → 自動轉發射模式;發射模式走速 0.072 u/F;150F 沒放 → 自動發射,爆炸半徑 6;
    //       空檔中:漩渦對敵方 Big Bubbler 屏障面每幀 2 傷、被實心牆截短
    [AutoTest(Scene = AutoTestAttribute.TrainingScene, Timeout = 150f, Order = 241)]
    public sealed class InkVacAutoTest : AutoTestBase
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
        private SquidController _squid;
        private InkVacSpecial _special;
        private InkVacConfig _config;
        private SpecialTestAim _aim;
        private int _playerHits;

        private float FrameSlack => Mathf.Max(1.5f, _maxFrame) + 0.5f;

        public static void Run()
        {
            Launch<InkVacAutoTest>();
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
            _config = Resources.Load<InkVacConfig>("Weapons/Special_ink_vac");
            if (_player == null || _config == null || _config.ShotPrefab == null || _config.BlastPrefab == null)
            {
                FailSetup("找不到 Player / Resources/Weapons/Special_ink_vac(先跑 M7 Setup)");
                yield break;
            }
            _intent = ScriptedIntent.Drive(_player);
            _gauge = _player.GetComponent<PlayerSpecialGauge>();
            _health = _player.GetComponent<InklingHealth>();
            _squid = _player.GetComponent<SquidController>();
            IAimView originalView = _player.AimView;
            _aim = new SpecialTestAim();
            _player.AimView = _aim;
            if (!SpecialTestKit.EquipSpecial(_player, _config))
            {
                FailSetup("換裝 Ink Vac 失敗");
                yield break;
            }
            _special = _player.GetComponent<InkVacSpecial>();
            _health.Damaged += (dealt, info) => _playerHits++;

            yield return SuctionAndFire();
            yield return AutoFireEmpty();

            Check("全程無例外/錯誤 log", _errors == 0, _errors == 0 ? "0" : $"{_errors} 筆,第一筆:{_firstError}");
            foreach (Object obj in _spawned)
            {
                if (obj != null)
                {
                    Destroy(obj is Component c ? c.gameObject : obj);
                }
            }
            _intent.Attack = false;
            _intent.Move = Vector2.zero;
            _health.Invulnerable = false;
            _player.AimView = originalView;
            _player.GetComponent<PlayerInputRouter>().ClearOverrideSource();
            _player.Teleport(new Vector3(0f, 0.1f, 0f), 0f);
            Finish();
        }

        private Inkling Enemy(Vector3 position, string name, bool invulnerable)
        {
            Inkling clone = SpecialTestKit.CloneEnemy(_playerGo, position, name, out _);
            clone.GetComponent<InklingHealth>().Invulnerable = invulnerable;
            _spawned.Add(clone);
            return clone;
        }

        private IEnumerator SuctionAndFire()
        {
            Vector3 p = new Vector3(-20f, 0f, 14f);
            _player.Teleport(p + Vector3.up * 0.1f, 90f);
            _aim.From(_player.transform, p + new Vector3(12f, 1.1f, 0f));
            _health.Invulnerable = true;
            yield return new WaitForSeconds(0.4f);
            _gauge.SetPointsForTest(100f);
            _intent.PressSpecial();
            _intent.Attack = true;
            yield return WaitUntil(() => _gauge.IsActive, 1f);
            float activatedAt = _special.ActivatedAt;

            // (a) 發動時就按著攻擊:17F 前隙 + 12F 吸引前隙 = 29F 才開始吸
            yield return WaitUntil(() => _special.VortexLength > 0f, 1.5f);
            float firstLag = (_special.SuctionStartedAt - activatedAt) * 60f;
            _intent.Attack = false;
            yield return new WaitForSeconds(0.2f);
            bool stopped = _special.VortexLength <= 0f;
            // 沒按攻擊可以潛游;從烏賊按攻擊 → 變回人形、18F 後開始吸
            _intent.Squid = true;
            yield return new WaitForSeconds(0.25f);
            bool swam = _squid.IsSquid;
            _intent.Attack = true;
            float pressedAt = Time.time;
            yield return WaitUntil(() => _special.VortexLength > 0f, 1f);
            float lag = (_special.SuctionStartedAt - pressedAt) * 60f;
            bool surfaced = !_squid.IsSquid;
            _intent.Squid = false;
            Check("發動 17F + 人形 12F 後才開始吸;放開停止;沒按攻擊可以潛游;從烏賊按攻擊 → 變回人形、18F 後開始吸",
                Mathf.Abs(firstLag - 29f) <= FrameSlack && stopped && swam && surfaced && lag >= 18f - 0.01f
                && lag <= 18f + FrameSlack,
                $"發動 → 開始吸 {firstLag:0.0}F(17 + 12);放開停止={stopped};潛游={swam} 變回人形={surfaced};" +
                $"烏賊按下 → 開始吸 {lag:0.0}F(S3 18F)");
            float startedAt = _special.SuctionStartedAt;
            yield return new WaitForSeconds(0.5f);
            float half = _special.VortexLength;
            float expectedHalf = InkVacModel.LengthAt((Time.time - startedAt) * 60f);
            yield return new WaitForSeconds(0.4f);
            float full = _special.VortexLength;
            Check("錐長每幀 +0.3334 u、45F 長到 15 u",
                Mathf.Abs(half - expectedHalf) < 0.35f && half > 8f && Mathf.Abs(full - 15f) < 0.01f,
                $"開始吸後 {(Time.time - 0.4f - startedAt) * 60f:0}F 長 {half:0.00}(應 {expectedHalf:0.00});之後 {full:0.00}");

            // (b) 吸走敵方墨彈;背後的照樣打到
            // 射手站在錐外(站在錐裡會被當成「吸到敵人」每幀 +1.5),子彈從錐軸上的 10 m 處打過來
            Inkling e = Enemy(p + new Vector3(10f, 0.1f, 6f), "VacShooter", true);
            _health.Invulnerable = false;
            _health.ResetForRespawn();
            float before = _special.Absorbed;
            int hitsBefore = _playerHits;
            int absorbed = 0;
            var shots = new List<InkBullet>();
            InkBulletImpactHandler handler = (in InkBulletImpact impact) =>
            {
                if (impact.End == InkBulletEnd.Absorbed && shots.Contains(impact.Bullet))
                {
                    absorbed++;
                }
            };
            InkBullet.Impacted += handler;
            Vector3 chest = _player.transform.position + Vector3.up * 1f;
            for (int i = 0; i < 5; i++)
            {
                Vector3 from = p + new Vector3(10f, 1.1f, (i - 2) * 0.2f);
                shots.Add(InkBullet.Fire(InkBulletSpec.Splattershot, from, chest - from, Team.Bravo, e));
            }
            yield return new WaitForSeconds(0.3f);
            InkBullet.Impacted -= handler;
            float gained = _special.Absorbed - before;
            bool frontUnhurt = _playerHits == hitsBefore;
            Vector3 back = _player.transform.position + new Vector3(-6f, 1f, 0f);
            InkBullet.Fire(InkBulletSpec.Splattershot, back, chest - back, Team.Bravo, e);
            yield return new WaitForSeconds(0.3f);
            bool backHit = _playerHits > hitsBefore;
            Check("吸走正面的敵方墨彈(不傷人、容量 += 36 × 5);背後打來的照樣打到",
                absorbed == 5 && Mathf.Abs(gained - 180f) < 0.6f && frontUnhurt && backHit,
                $"吸走 {absorbed}/5 發、容量 +{gained:0.0}(應 180);正面沒受傷={frontUnhurt} 背後打到={backHit}");
            _health.ResetForRespawn();

            // (b) 錐內的敵方爆炸被整個吸走;錐外(背後)的照樣炸到
            before = _special.Absorbed;
            hitsBefore = _playerHits;
            ExplosionResult inside = Explosion.Detonate(ExplosionSpec.SplatBomb, p + new Vector3(5f, 1f, 0f), Team.Bravo, e);
            gained = _special.Absorbed - before;
            bool unhurt = _playerHits == hitsBefore;
            ExplosionSpec small = ExplosionSpec.SplatBomb;
            small.PaintRadius = 0f;
            small.PaintRayCount = 0;
            small.KnockbackAccel = 0f;
            small.NearDamage = 20f;
            small.FarDamage = 20f;
            ExplosionResult behind = Explosion.Detonate(small, _player.transform.position + new Vector3(-2.5f, 0.5f, 0f),
                Team.Bravo, e);
            bool behindHit = _playerHits > hitsBefore && !behind.Absorbed;
            Check("錐內的敵方爆炸整個被吸走(容量 += 180、使用者不受傷);背後的爆炸照樣炸到",
                inside.Absorbed && Mathf.Abs(gained - 180f) < 0.01f && unhurt && behindHit,
                $"吸走={inside.Absorbed} 容量 +{gained:0.0};沒受傷={unhurt};背後炸到={behindHit}");
            _health.ResetForRespawn();
            _health.Invulnerable = true;

            // (c) 漩渦裡的敵人:不受傷、×0.5、每秒扣 13.2% 墨;容量每幀 +1.5;敵方裝置每幀 2 傷
            e.Teleport(p + new Vector3(6f, 0.1f, 0.5f));
            var eHealth = e.GetComponent<InklingHealth>();
            eHealth.Invulnerable = false;
            var eTank = e.GetComponent<PlayerInkTank>();
            eTank.RefillFull();
            TestDeployable device = TestDeployable.Create("VacDevice", p + new Vector3(5f, 1f, -0.6f), Vector3.one * 0.6f);
            device.Setup(e, Team.Bravo, 1000f, TargetClass.Sprinkler, false);
            _spawned.Add(device);
            yield return new WaitForSeconds(0.1f);
            float tank0 = eTank.Normalized;
            float fill0 = _special.Absorbed;
            // 裝置的傷害也從 t0 起算(前面 0.1 s 等待期間已結算的 tick 不算進這 1 秒;否則多算 1~2 個 0.1 s tick)
            float deviceHp0 = device.Hp;
            float t0 = Time.time;
            yield return new WaitForSeconds(1f);
            float seconds = Time.time - t0;
            float drain = (tank0 - eTank.Normalized) / seconds;
            float fillRate = (_special.Absorbed - fill0) / (seconds * 60f);
            float slow = e.Modifiers.RunSpeedMultiplier;
            float deviceDamage = deviceHp0 - device.Hp;
            bool eUnhurt = eHealth.Hp >= eHealth.MaxHp - 0.01f;
            e.Teleport(p + new Vector3(6f, 0.1f, 9f));
            yield return new WaitForSeconds(0.2f);
            bool recovered = Mathf.Approximately(e.Modifiers.RunSpeedMultiplier, 1f);
            // 測試裝置收掉(之後的反擊彈會打在它身上)
            _spawned.Remove(device);
            Destroy(device.gameObject);
            Check("漩渦裡的敵人不受傷、移動 ×0.5、每秒扣 13.2% 墨;容量每幀 +1.5;離開就恢復;敵方裝置每幀 2 傷",
                eUnhurt && Mathf.Approximately(slow, 0.5f) && Mathf.Abs(drain - 0.132f) < 0.02f
                && Mathf.Abs(fillRate - 1.5f) < 0.12f && recovered && Mathf.Abs(deviceDamage / seconds - 120f) < 20f,
                $"敵人 HP {eHealth.Hp:0.0};移動 ×{slow:0.00};扣墨 {drain * 100f:0.0}%/s;容量 +{fillRate:0.00}/F;" +
                $"離開後 ×{e.Modifiers.RunSpeedMultiplier:0.00};裝置 {deviceDamage / seconds:0.0}/s(應 120)");
            eHealth.Invulnerable = true;

            // (d) 走速隨吸入量(量化到 5%):橫移 1 秒
            float fill = _special.Fill01;
            float expectedSpeed = InkVacModel.MoveSpeed(Mathf.Round(fill * 20f) / 20f) * 60f;
            _intent.Move = new Vector2(1f, 0f);
            yield return new WaitForSeconds(0.5f);
            Vector3 m0 = _player.transform.position;
            float mt0 = Time.time;
            yield return new WaitForSeconds(0.8f);
            Vector3 m1 = _player.transform.position;
            float speed = new Vector2(m1.x - m0.x, m1.z - m0.z).magnitude / (Time.time - mt0);
            _intent.Move = Vector2.zero;
            Check("走速隨吸入量 0.09 → 0.07 u/F(越吸越重)",
                Mathf.Abs(speed - expectedSpeed) < expectedSpeed * 0.05f && fill > 0.3f,
                $"吸入 {fill * 100f:0}%:橫移 {speed:0.00} m/s(應 {expectedSpeed:0.00})");

            // (e) 吸滿 → 發射模式、20F 內不能射
            _player.Teleport(p + Vector3.up * 0.1f, 90f);
            int guard = 0;
            while (!_special.InFireMode && guard++ < 10)
            {
                Explosion.Detonate(ExplosionSpec.SplatBomb, p + new Vector3(4f, 1f, 0f), Team.Bravo, e);
                yield return null;
            }
            float enteredAt = _special.FireModeEnteredAt;
            bool capacityFull = _special.Fill01 >= 1f;
            yield return new WaitForSeconds(10f / 60f);
            bool blockedEarly = !_special.CanFireNow && _special.ShotsFired == 0;
            yield return WaitUntil(() => _special.CanFireNow, 1f);
            float ready = (_special.AimStartedAt - enteredAt) * 60f;
            Check("吸滿 1100 → 發射模式;轉換 20F 內不能射",
                capacityFull && blockedEarly && ready >= 20f - 0.01f && ready <= 20f + FrameSlack,
                $"吸滿={capacityFull}(容量 {_special.Absorbed:0});10F 時不能射={blockedEarly};可以射在 {ready:0.0}F");

            // 發射:瞄向前方 16 m 的地面;直擊路徑上擺一個敵人
            Vector3 aimPoint = p + new Vector3(16f, 0f, 0f);
            _aim.From(_player.transform, aimPoint);
            yield return null;
            Vector3 dir = _aim.Forward;
            Vector3 muzzle = _special.MuzzlePoint(dir);
            // 敵人站在彈道正下方 6 m 的地面(反擊彈另有 0.08 u/F 上抬,胸口高度剛好)
            Vector3 victimAt = SpecialTestKit.Ground(muzzle + dir * 6f) + Vector3.up * 0.1f;
            Inkling victim = Enemy(victimAt, "VacVictim", false);
            var victimHealth = victim.GetComponent<InklingHealth>();
            float directDamage = 0f;
            victimHealth.Damaged += (dealt, info) =>
            {
                if (info.AttackClass == AttackClass.InkVac && directDamage == 0f)
                {
                    directDamage = info.Amount;
                }
            };
            yield return new WaitForSeconds(0.2f);
            var shotLog = new System.Text.StringBuilder();
            InkBulletImpactHandler shotHandler = (in InkBulletImpact impact) =>
            {
                if (impact.Bullet == _special.LastShot && shotLog.Length < 400)
                {
                    shotLog.Append($"[{impact.End} F{impact.Frame} {impact.Point} {(impact.Target != null ? impact.Target.GetType().Name : "-")}]");
                }
            };
            InkBullet.Impacted += shotHandler;
            _intent.Attack = false;
            yield return WaitUntil(() => _special.ShotsFired > 0, 1f);
            InkBullet shot = _special.LastShot;
            Vector3 v = shot != null ? shot.State.Velocity : Vector3.zero;
            float spawnSpeed = (v - Vector3.up * InkVacModel.ShotSpeedY).magnitude;
            bool ended = !_gauge.IsActive;
            yield return WaitUntil(() => _special.LastBlast != null && _special.LastBlast.Armed, 3f);
            InkBullet.Impacted -= shotHandler;
            InkVacBlast blast = _special.LastBlast;
            if (blast == null)
            {
                Check("反擊彈落地", false, "沒有落地爆炸");
                yield break;
            }
            Check("放開攻擊發射:初速 0.7 u/F(吸滿)、直擊 220;發射即結束大招",
                Mathf.Abs(spawnSpeed - 0.7f) < 0.005f && Mathf.Approximately(directDamage, 220f) && ended
                && !victimHealth.IsAlive,
                $"初速 {spawnSpeed:0.000} u/F;直擊 {directDamage:0.0};大招結束={ended};敵人倒下={!victimHealth.IsAlive};" +
                $"槍口 {_special.LastMuzzle} 方向 {_special.LastShotDirection} 敵人 {victim.transform.position}(放置 {victimAt});" +
                $"彈 {shotLog}");

            // 爆炸圈:半徑 11 內吃 220、外面沒事;bot 在圈內 50F 內跑出去
            Vector3 c = blast.Center;
            Inkling inRange = Enemy(c + new Vector3(0f, 0.1f, 10f), "VacInRange", false);
            Inkling outRange = Enemy(c + new Vector3(0f, 0.1f, -12.5f), "VacOutRange", false);
            Inkling bot = Enemy(c + new Vector3(9.2f, 0.1f, 3f), "VacBot", false);
            bot.gameObject.AddComponent<BotBrain>().Configure(BotBrain.Difficulty.Hard);
            int fleesBefore = BotHazards.FleesTriggered;
            var inHealth = inRange.GetComponent<InklingHealth>();
            var outHealth = outRange.GetComponent<InklingHealth>();
            float outVacDamage = 0f;
            outHealth.Damaged += (dealt, info) =>
            {
                if (info.AttackClass == AttackClass.InkVac)
                {
                    outVacDamage += info.Amount;
                }
            };
            var botHealth = bot.GetComponent<InklingHealth>();
            yield return WaitUntil(() => !blast.Armed, 2f);
            float waited = (InkVacBlast.LastDetonatedAt - blast.SpawnedAt) * 60f;
            Vector3 botFlat = bot.transform.position - c;
            botFlat.y = 0f;
            Check("落地 50F 後爆炸 220:半徑 11(吸滿)內的敵人倒下、外面的沒事",
                Mathf.Abs(blast.Radius - 11f) < 0.01f && Mathf.Abs(waited - 50f) <= FrameSlack && !inHealth.IsAlive
                && outVacDamage == 0f,
                $"半徑 {blast.Radius:0.00};落地 → 爆炸 {waited:0.0}F;10 m 的倒下={!inHealth.IsAlive};12.5 m 的吃到 {outVacDamage:0.0}");
            Check("bot 在爆炸圈內 → 50F 內跑出圈外(BotHazards 逃離)",
                BotHazards.FleesTriggered > fleesBefore && botFlat.magnitude > blast.Radius && botHealth.IsAlive,
                $"逃離觸發 {BotHazards.FleesTriggered - fleesBefore} 次;爆炸時離圈心 {botFlat.magnitude:0.00} m;活著={botHealth.IsAlive}");
            Destroy(bot.GetComponent<BotBrain>());
        }

        // (f) 沒吸:360F 自動轉發射模式、發射模式走速、150F 自動發射、爆炸半徑 6
        private IEnumerator AutoFireEmpty()
        {
            yield return WaitUntil(() => !_gauge.IsActive, 2f);
            Vector3 p = new Vector3(-22f, 0f, -14f);
            _player.Teleport(p + Vector3.up * 0.1f, 90f);
            _aim.From(_player.transform, p + new Vector3(30f, 1.2f, 0f));
            _intent.Attack = false;
            yield return new WaitForSeconds(0.3f);
            _gauge.SetPointsForTest(100f);
            _intent.PressSpecial();
            yield return WaitUntil(() => _gauge.IsActive, 1f);
            float activatedAt = _special.ActivatedAt;
            _intent.Move = new Vector2(0f, 1f);
            yield return new WaitForSeconds(0.6f);
            Vector3 m0 = _player.transform.position;
            float mt0 = Time.time;
            yield return new WaitForSeconds(0.8f);
            float walk = Vector3.Distance(Flat(m0), Flat(_player.transform.position)) / (Time.time - mt0);
            _intent.Move = Vector2.zero;
            yield return new WaitForSeconds(3f - (Time.time - activatedAt));
            float gaugeAt3 = _gauge.Points;
            Check("吸引模式走速 0.09 u/F(沒吸);能量 = 360F 計時器(3 秒時剩一半)",
                Mathf.Abs(walk - 5.4f) < 0.27f && Mathf.Abs(gaugeAt3 - 50f) < 1.5f,
                $"走 {walk:0.00} m/s(應 5.40);3 秒時能量 {gaugeAt3:0.0}");

            // 轉發射模式前的空檔(3 → 6 秒):漩渦吃 Big Bubbler 屏障面、被實心牆截短(Inkipedia Ink Vac)
            _intent.Attack = true;
            yield return WaitUntil(() => _special.VortexLength >= 14.9f, 1.2f);
            InkVacuum vacuum = _special.Vacuum;
            Vector3 apex = vacuum.Apex;
            Vector3 axis = vacuum.Axis;
            float barrierDamage = 0f;
            var barrier = new BarrierSphere(_player, (in DamageInfo info) => barrierDamage += info.Amount);
            barrier.Center = apex + axis * 8f;
            barrier.Radius = 2f;
            barrier.Blocking = true;
            barrier.Enable(Team.Bravo);
            float b0 = Time.time;
            yield return new WaitForSeconds(0.5f);
            float barrierRate = barrierDamage / (Time.time - b0);
            barrier.Disable();
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "VacTestWall";
            wall.transform.SetPositionAndRotation(apex + axis * 5f, Quaternion.LookRotation(new Vector3(axis.x, 0f, axis.z)));
            wall.transform.localScale = new Vector3(6f, 4f, 0.4f);
            _spawned.Add(wall);
            Physics.SyncTransforms();
            yield return new WaitForSeconds(0.1f);
            float clipped = _special.VortexLength;
            float wallAt = Physics.Raycast(vacuum.Apex, vacuum.Axis, out RaycastHit wallHit, 15f, CombatMasks.Terrain,
                QueryTriggerInteraction.Ignore) ? wallHit.distance : -1f;
            _spawned.Remove(wall);
            Destroy(wall);
            yield return new WaitForSeconds(0.15f);
            float restored = _special.VortexLength;
            _intent.Attack = false;
            Check("漩渦:敵方 Big Bubbler 屏障面每幀 2 傷(120/s);被實心牆截短、牆拿掉就恢復",
                Mathf.Abs(barrierRate - 120f) < 20f && wallAt > 0f && Mathf.Abs(clipped - wallAt) < 0.1f
                && restored > clipped + 3f,
                $"屏障 {barrierRate:0.0}/s;牆在 {wallAt:0.00} m、錐長 {clipped:0.00} → 拿掉後 {restored:0.00}");

            yield return WaitUntil(() => _special.InFireMode, 4f);
            float entered = (_special.FireModeEnteredAt - activatedAt) * 60f;
            _intent.Move = new Vector2(0f, -1f);
            yield return new WaitForSeconds(0.5f);
            m0 = _player.transform.position;
            mt0 = Time.time;
            yield return new WaitForSeconds(0.8f);
            float fireWalk = Vector3.Distance(Flat(m0), Flat(_player.transform.position)) / (Time.time - mt0);
            _intent.Move = Vector2.zero;
            bool guide = false;
            float until = _special.FireModeEnteredAt + 3.5f;
            while (_special.ShotsFired < 2 && Time.time < until)
            {
                guide |= _special.GuideShown;
                yield return null;
            }
            float autoAt = (_special.LastShotTime - _special.FireModeEnteredAt) * 60f;
            Check("能量 360F 用完自動轉發射模式;發射模式走速 0.072 u/F;150F 沒放 → 自動發射(本機顯示彈道預覽)",
                Mathf.Abs(entered - 360f) <= FrameSlack && Mathf.Abs(fireWalk - 4.32f) < 0.25f
                && _special.LastShotAuto && Mathf.Abs(autoAt - 170f) <= FrameSlack && guide,
                $"轉發射模式 {entered:0.0}F(應 360);走 {fireWalk:0.00} m/s(應 4.32);自動發射 {autoAt:0.0}F(20 + 150);預覽={guide}");
            yield return WaitUntil(() => _special.LastBlast != null && _special.LastBlast.Armed, 4f);
            InkVacBlast blast = _special.LastBlast;
            Check("沒吸的反擊彈爆炸半徑 6", blast != null && Mathf.Abs(blast.Radius - 6f) < 0.01f,
                $"半徑 {(blast != null ? blast.Radius : 0f):0.00}");
            yield return new WaitForSeconds(1f);
        }

        private static Vector3 Flat(Vector3 v)
        {
            return new Vector3(v.x, 0f, v.z);
        }
    }
}

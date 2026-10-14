using System.Collections;
using System.Collections.Generic;
using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // Crab Tank(D2)驗收,試射場。全部走真實路徑(按 R 發動、攻擊/副武器/烏賊鍵、真的墨彈與爆炸):
    //   (a) 召喚 45F 後裝甲才生效;主武器被佔用、烏賊鍵不會變烏賊
    //   (b) 步行:橫移 0.075 u/F(4.5 m/s)、前後 0.015 u/F(0.9 m/s);車身以 70°/s 轉向準心
    //   (c) 裝甲:正面的子彈打在裝甲上(耐久 500、Shooter ×1、Trizooka ×1.8),駕駛沒事;背後與頭部打得到駕駛;
    //       正面的爆炸被車體擋住(駕駛沒事、裝甲吃傷害);耐久低於一半側甲掉落
    //   (d) 480F 到:戰車消失、25F 硬直後才結束(能量歸 0)
    //   (e) ZR 連射 12F → 4F(按住 90F 到最快)、近距離 32;R 砲每 75F、砲擊後 32F 不能連射;砲彈直擊 50 + 爆炸 30 = 80
    //   (f) ZL 滾球:0.1 u/F 全方向、駕駛藏起來且背後的子彈打在球上、接觸 40、爬塗得到的牆;放開 → 20F 不能射
    //   (g) 裝甲被打爆:駕駛彈出(沒倒下)、25F 後結束、能量歸 0
    [AutoTest(Scene = AutoTestAttribute.TrainingScene, Timeout = 150f, Order = 242)]
    public sealed class CrabTankAutoTest : AutoTestBase
    {
        private readonly List<Object> _spawned = new List<Object>();
        private readonly List<Inkling> _revive = new List<Inkling>();
        private int _errors;
        private string _firstError;
        private float _maxFrame = 1f;

        private GameObject _playerGo;
        private Inkling _player;
        private ScriptedIntent _intent;
        private PlayerSpecialGauge _gauge;
        private InklingHealth _health;
        private SquidController _squid;
        private CharacterVisual _visual;
        private CrabTankSpecial _special;
        private CrabTankConfig _config;
        private SpecialTestAim _aim;
        private Inkling _enemy;
        private int _riderHits;

        private float FrameSlack => Mathf.Max(1.5f, _maxFrame) + 0.5f;

        public static void Run()
        {
            Launch<CrabTankAutoTest>();
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
            for (int i = 0; i < _revive.Count; i++)
            {
                var health = _revive[i] != null ? _revive[i].GetComponent<InklingHealth>() : null;
                if (health != null && health.IsAlive && health.Damage > 0f)
                {
                    health.ResetForRespawn();
                }
            }
        }

        private IEnumerator Start()
        {
            _playerGo = FindPlayer();
            _player = _playerGo != null ? _playerGo.GetComponent<Inkling>() : null;
            _config = Resources.Load<CrabTankConfig>("Weapons/Special_crab_tank");
            if (_player == null || _config == null || _config.BodyPrefab == null || _config.BulletPrefab == null)
            {
                FailSetup("找不到 Player / Resources/Weapons/Special_crab_tank(先跑 M7 Setup)");
                yield break;
            }
            _intent = ScriptedIntent.Drive(_player);
            _gauge = _player.GetComponent<PlayerSpecialGauge>();
            _health = _player.GetComponent<InklingHealth>();
            _squid = _player.GetComponent<SquidController>();
            _visual = _player.GetComponent<CharacterVisual>();
            IAimView originalView = _player.AimView;
            _aim = new SpecialTestAim();
            _player.AimView = _aim;
            if (!SpecialTestKit.EquipSpecial(_player, _config))
            {
                FailSetup("換裝 Crab Tank 失敗");
                yield break;
            }
            _special = _player.GetComponent<CrabTankSpecial>();
            _health.Damaged += (dealt, info) => _riderHits++;
            _enemy = SpecialTestKit.CloneEnemy(_playerGo, new Vector3(-20f, 0.1f, 20f), "CrabShooter", out _);
            _enemy.GetComponent<InklingHealth>().Invulnerable = true;
            _spawned.Add(_enemy);

            yield return WalkAndArmor();
            yield return TurretAndCannon();
            yield return BallMode();
            yield return BreakTank();

            Check("全程無例外/錯誤 log", _errors == 0, _errors == 0 ? "0" : $"{_errors} 筆,第一筆:{_firstError}");
            _revive.Clear();
            foreach (Object obj in _spawned)
            {
                if (obj != null)
                {
                    Destroy(obj is Component c ? c.gameObject : obj);
                }
            }
            _intent.Attack = false;
            _intent.Squid = false;
            _intent.Sub = false;
            _intent.Move = Vector2.zero;
            _health.Invulnerable = false;
            _player.AimView = originalView;
            _player.GetComponent<PlayerInputRouter>().ClearOverrideSource();
            _player.Teleport(new Vector3(0f, 0.1f, 0f), 0f);
            Finish();
        }

        private IEnumerator Activate(Vector3 position, float yaw, Vector3 lookAt)
        {
            yield return WaitUntil(() => !_gauge.IsActive, 3f);
            _intent.Attack = false;
            _intent.Squid = false;
            _intent.Sub = false;
            _intent.Move = Vector2.zero;
            _player.Teleport(position + Vector3.up * 0.1f, yaw);
            _aim.From(_player.transform, lookAt);
            _health.ResetForRespawn();
            yield return new WaitForSeconds(0.3f);
            _gauge.SetPointsForTest(100f);
            _intent.PressSpecial();
            yield return WaitUntil(() => _gauge.IsActive, 1f);
        }

        private IEnumerator WalkAndArmor()
        {
            Vector3 p = new Vector3(-14f, 0f, 6f);
            yield return Activate(p, 90f, p + new Vector3(20f, 1.2f, 0f));
            float activatedAt = _special.ActivatedAt;
            _intent.Squid = true;
            yield return WaitUntil(() => _special.Armored, 1.5f);
            float armedAt = (Time.time - activatedAt) * 60f;
            bool squidBlocked = !_squid.IsSquid && !_special.BallMode;
            _intent.Squid = false;
            Check("召喚 45F 後裝甲才生效;主武器被佔用、召喚中按烏賊鍵不會變烏賊/滾球",
                Mathf.Abs(armedAt - 45f) <= FrameSlack && _special.BlocksMainWeapon && squidBlocked
                && Mathf.Approximately(_special.Body.Hp, 500f),
                $"裝甲生效 {armedAt:0.0}F;烏賊被擋={squidBlocked};耐久 {_special.Body.Hp:0}");

            // (b) 步行速度
            float lateral = 0f;
            float forward = 0f;
            yield return MeasureSpeed(new Vector2(1f, 0f), 0.7f, v => lateral = v);
            yield return MeasureSpeed(new Vector2(0f, 1f), 0.7f, v => forward = v);
            Check("步行:橫移 0.075 u/F(4.5 m/s)、前後 0.015 u/F(0.9 m/s)",
                Mathf.Abs(lateral - 4.5f) < 0.2f && Mathf.Abs(forward - 0.9f) < 0.08f,
                $"橫移 {lateral:0.00} m/s、前進 {forward:0.00} m/s");

            // 轉向:準心轉 90°,0.5 秒後車身轉了 35°
            _player.Teleport(p + Vector3.up * 0.1f, 90f);
            yield return new WaitForSeconds(0.1f);
            float yaw0 = _special.TankYaw;
            _aim.From(_player.transform, p + new Vector3(0f, 1.2f, -20f));
            yield return new WaitForSeconds(0.5f);
            float turned = Mathf.Abs(Mathf.DeltaAngle(yaw0, _special.TankYaw));
            float expectedTurn = _config.TurnDegPerSecond * 0.5f;
            Check("車身以 70°/s 轉向準心(比鏡頭慢)", Mathf.Abs(turned - expectedTurn) < 5f,
                $"0.5 秒轉 {turned:0.0}°(應 {expectedTurn:0.0}°)");
            _aim.From(_player.transform, p + new Vector3(20f, 1.2f, 0f));
            yield return WaitUntil(() => Mathf.Abs(Mathf.DeltaAngle(_special.TankYaw, 90f)) < 0.5f, 2f);

            // (c) 裝甲 vs 駕駛
            CrabTankBody body = _special.Body;
            Vector3 riderPos = _player.transform.position;
            _health.Invulnerable = false;
            float armor0 = body.Hp;
            int hits0 = _riderHits;
            yield return Shoot(riderPos + new Vector3(8f, 1f, 0f), riderPos + Vector3.up * 1f, InkBulletSpec.Splattershot);
            float frontArmor = armor0 - body.Hp;
            bool frontRiderSafe = _riderHits == hits0;
            _health.ResetForRespawn();
            hits0 = _riderHits;
            float armor1 = body.Hp;
            yield return Shoot(riderPos + new Vector3(-8f, 1f, 0f), riderPos + Vector3.up * 1f, InkBulletSpec.Splattershot);
            bool backHitsRider = _riderHits > hits0 && Mathf.Approximately(body.Hp, armor1);
            _health.ResetForRespawn();
            hits0 = _riderHits;
            yield return Shoot(riderPos + new Vector3(8f, 1.85f, 0f), riderPos + Vector3.up * 1.85f, InkBulletSpec.Splattershot);
            bool headHitsRider = _riderHits > hits0;
            _health.ResetForRespawn();
            InkBulletSpec heavy = InkBulletSpec.Splattershot;
            heavy.DamageMax = 50f;
            heavy.DamageMin = 50f;
            heavy.AttackClass = AttackClass.Trizooka;
            float armor2 = body.Hp;
            yield return Shoot(riderPos + new Vector3(8f, 0.8f, 0.3f), riderPos + new Vector3(0f, 0.8f, 0.3f), heavy);
            float trizookaDealt = armor2 - body.Hp;
            Check("裝甲:正面子彈打在裝甲(Shooter ×1 = 36、Trizooka ×1.8 = 90)駕駛沒事;背後與頭部打得到駕駛",
                Mathf.Abs(frontArmor - 36f) < 0.05f && frontRiderSafe && backHitsRider && headHitsRider
                && Mathf.Abs(trizookaDealt - 90f) < 0.05f,
                $"正面 → 裝甲 −{frontArmor:0.0}、駕駛安全={frontRiderSafe};背後打到駕駛={backHitsRider};頭部打到={headHitsRider};" +
                $"Trizooka 50 → 裝甲 −{trizookaDealt:0.0}");

            // 正面的敵方爆炸:車體擋住駕駛、自己吃傷害;背後的爆炸炸得到駕駛
            hits0 = _riderHits;
            float armor3 = body.Hp;
            ExplosionSpec blast = ExplosionSpec.SplatBomb;
            blast.PaintRadius = 0f;
            blast.PaintRayCount = 0;
            blast.KnockbackAccel = 0f;
            ExplosionResult front = Explosion.Detonate(blast, riderPos + new Vector3(2.4f, 0.4f, 0f), Team.Bravo, _enemy);
            bool riderSafe = _riderHits == hits0;
            float blastArmor = armor3 - body.Hp;
            _health.ResetForRespawn();
            blast.NearDamage = 10f;
            blast.FarDamage = 10f;
            ExplosionResult rear = Explosion.Detonate(blast, riderPos + new Vector3(-1.8f, 1.2f, 0f), Team.Bravo, _enemy);
            bool rearHits = _riderHits > hits0;
            _health.ResetForRespawn();
            Check("正面的敵方爆炸被車體擋住(駕駛沒事、裝甲吃傷害);背後的爆炸炸得到駕駛",
                riderSafe && blastArmor > 0f && front.BlockedByCover >= 1 && rearHits && rear.TargetsHit >= 1,
                $"駕駛安全={riderSafe} 裝甲 −{blastArmor:0.0} 擋住 {front.BlockedByCover};背後炸到={rearHits}");

            // 耐久低於一半側甲掉落
            body.ApplyDamage(new DamageInfo(body.Hp - 240f, body.transform.position + Vector3.up, Team.Bravo, _enemy,
                DamageKind.Main, AttackClass.Shooter));
            Check("耐久低於一半:側甲掉落", body.IsAlive && !body.LooseArmorShown && body.Hp < 250f,
                $"耐久 {body.Hp:0.0};側甲還在={body.LooseArmorShown}");
            _health.Invulnerable = true;

            // (d) 480F 到:戰車消失、25F 硬直後結束
            yield return WaitUntil(() => _special.InEndLag || !_gauge.IsActive, 9f);
            float endLagAt = (_special.EndLagStartedAt - activatedAt) * 60f;
            bool bodyGone = _special.Body == null;
            bool blocking = _gauge.IsActive && _special.BlocksMainWeapon;
            yield return WaitUntil(() => !_gauge.IsActive, 1f);
            float endedAt = (Time.time - activatedAt) * 60f;
            Check("480F 到:戰車消失、25F 硬直(不能攻擊)後才結束,能量歸 0",
                Mathf.Abs(endLagAt - 480f) <= FrameSlack && bodyGone && blocking
                && Mathf.Abs(endedAt - 505f) <= FrameSlack + 1f && _gauge.Points < 0.01f,
                $"戰車消失 {endLagAt:0.0}F;硬直中佔用={blocking};結束 {endedAt:0.0}F(480 + 25);能量 {_gauge.Points:0.0}");
        }

        private IEnumerator MeasureSpeed(Vector2 input, float seconds, System.Action<float> result)
        {
            Vector3 p = _player.transform.position;
            _intent.Move = input;
            yield return new WaitForSeconds(0.2f);
            Vector3 a = _player.transform.position;
            float t0 = Time.time;
            yield return new WaitForSeconds(seconds);
            Vector3 b = _player.transform.position;
            result(new Vector2(b.x - a.x, b.z - a.z).magnitude / (Time.time - t0));
            _intent.Move = Vector2.zero;
            yield return new WaitForSeconds(0.1f);
            _player.Teleport(p);
        }

        private IEnumerator Shoot(Vector3 from, Vector3 to, InkBulletSpec spec)
        {
            InkBullet.Fire(spec, from, to - from, Team.Bravo, _enemy);
            yield return new WaitForSeconds(0.25f);
        }

        private IEnumerator TurretAndCannon()
        {
            Vector3 p = new Vector3(-20f, 0f, -8f);
            yield return Activate(p, 90f, p + new Vector3(20f, 1f, 0f));
            yield return WaitUntil(() => _special.Armored, 1.5f);
            // 近距離(6 m)的敵人:連射每發 32
            Inkling target = SpecialTestKit.CloneEnemy(_playerGo, p + new Vector3(6f, 0.1f, 1.4f), "CrabTarget", out _);
            _spawned.Add(target);
            _revive.Add(target);
            var damages = new List<float>();
            target.GetComponent<InklingHealth>().Damaged += (dealt, info) =>
            {
                if (info.AttackClass == AttackClass.CrabTankCannon)
                {
                    damages.Add(info.Amount);
                }
            };
            _aim.From(_player.transform, target.transform.position + Vector3.up * 1f);
            yield return new WaitForSeconds(0.3f);
            int shots0 = _special.TurretShots;
            _intent.Attack = true;
            float heldAt = Time.time;
            yield return new WaitForSeconds(0.25f);
            float firstInterval = _special.LastTurretInterval;
            yield return new WaitForSeconds(1.9f);
            float lastInterval = _special.LastTurretInterval;
            float held = Time.time - heldAt;
            int shots = _special.TurretShots - shots0;
            _intent.Attack = false;
            int expected = ExpectedShots(held);
            bool all32 = damages.Count > 0;
            foreach (float d in damages)
            {
                all32 &= Mathf.Approximately(d, 32f);
            }
            Check("ZR 連射:間隔 12F → 4F(按住 90F 到最快)、近距離每發 32",
                firstInterval > 10.5f && Mathf.Abs(lastInterval - 4f) < 0.01f && Mathf.Abs(shots - expected) <= 2 && all32,
                $"起始間隔 {firstInterval:0.0}F、最後 {lastInterval:0.0}F;{held:0.00} 秒 {shots} 發(應約 {expected});" +
                $"命中 {damages.Count} 次都是 32={all32}");
            _revive.Remove(target);
            target.Teleport(p + new Vector3(0f, 0.1f, 18f));

            // R 砲:每 75F;砲擊後 32F 不能連射
            _aim.From(_player.transform, p + new Vector3(13f, 0f, 0f));
            yield return new WaitForSeconds(0.2f);
            var impacts = new List<InkBulletImpact>();
            InkBulletImpactHandler handler = (in InkBulletImpact impact) => impacts.Add(impact);
            InkBullet.Impacted += handler;
            _intent.Sub = true;
            yield return WaitUntil(() => _special.CannonShots > 0, 0.5f);
            float cannonAt = _special.LastCannonTime;
            _intent.Sub = false;
            _intent.Attack = true;
            int turretBefore = _special.TurretShots;
            yield return WaitUntil(() => _special.TurretShots > turretBefore, 1f);
            float resumed = (_special.LastTurretShotTime - cannonAt) * 60f;
            _intent.Attack = false;
            yield return new WaitForSeconds(0.1f);
            _intent.Sub = true;
            yield return null;
            _intent.Sub = false;
            bool tooSoon = _special.CannonShots == 1;
            yield return new WaitForSeconds(1.2f - (Time.time - cannonAt));
            _intent.Sub = true;
            yield return WaitUntil(() => _special.CannonShots > 1, 0.5f);
            float interval = (_special.LastCannonTime - cannonAt) * 60f;
            _intent.Sub = false;
            yield return new WaitForSeconds(0.5f);
            InkBullet.Impacted -= handler;
            Vector3 landing = Vector3.zero;
            foreach (InkBulletImpact impact in impacts)
            {
                if (impact.Exploded && impact.End == InkBulletEnd.Terrain)
                {
                    landing = impact.Point;
                }
            }
            Check("R 砲:砲擊後 32F 不能連射;冷卻中再按不會射;每 75F 一發",
                resumed >= 32f - 0.01f && resumed <= 32f + FrameSlack + 1f && tooSoon && interval >= 75f - 0.01f
                && interval <= 75f + 1f + FrameSlack && landing != Vector3.zero,
                $"砲擊 → 恢復連射 {resumed:0.0}F;冷卻中沒射={tooSoon};第二發在 {interval:0.0}F;落點 {landing}");

            // 砲彈直擊 50 + 爆炸 30:敵人站在落點
            yield return new WaitForSeconds(1.3f);
            target.Teleport(new Vector3(landing.x, 0.1f, landing.z));
            _revive.Add(target);
            damages.Clear();
            yield return new WaitForSeconds(0.2f);
            _intent.Sub = true;
            yield return new WaitForSeconds(0.1f);
            _intent.Sub = false;
            yield return new WaitForSeconds(0.8f);
            float total = 0f;
            bool has50 = false;
            bool has30 = false;
            foreach (float d in damages)
            {
                total += d;
                has50 |= Mathf.Approximately(d, 50f);
                has30 |= Mathf.Approximately(d, 30f);
            }
            Check("砲彈直擊 50(貫穿)+ 著地爆炸 30 = 80", has50 && has30 && Mathf.Approximately(total, 80f),
                $"命中 {damages.Count} 次、合計 {total:0.0}");
            _revive.Remove(target);
        }

        private int ExpectedShots(float seconds)
        {
            float t = 0f;
            int shots = 0;
            while (t <= seconds)
            {
                shots++;
                t += _config.TurretRepeatFrames(t * 60f) / 60f;
            }
            return shots;
        }

        private IEnumerator BallMode()
        {
            Vector3 p = new Vector3(12f, 0f, -9f);
            yield return Activate(p, 0f, p + new Vector3(0f, 1.2f, 20f));
            yield return WaitUntil(() => _special.Armored, 1.5f);
            _intent.Squid = true;
            yield return new WaitForSeconds(0.2f);
            bool hidden = _visual != null && _visual.BodyHiddenBySpecial;
            bool regen = _player.Modifiers.SwimRegen;
            // 背後的子彈打在球上
            CrabTankBody body = _special.Body;
            _health.Invulnerable = false;
            float armor0 = body.Hp;
            int hits0 = _riderHits;
            Vector3 rider = _player.transform.position;
            yield return Shoot(rider + new Vector3(0f, 1f, -8f), rider + Vector3.up * 1f, InkBulletSpec.Splattershot);
            bool protectedByBall = _riderHits == hits0 && body.Hp < armor0;
            _health.Invulnerable = true;
            // 前方 4 m 的敵人:滾過去撞
            Inkling target = SpecialTestKit.CloneEnemy(_playerGo, rider + new Vector3(0f, 0.1f, 3.5f), "CrabBallTarget", out _);
            _spawned.Add(target);
            _revive.Add(target);
            float contact = 0f;
            target.GetComponent<InklingHealth>().Damaged += (dealt, info) =>
            {
                if (info.AttackClass == AttackClass.CrabTankBody)
                {
                    contact = info.Amount;
                }
            };
            float speed = 0f;
            yield return MeasureSpeed(new Vector2(0f, 1f), 0.5f, v => speed = v);
            _revive.Remove(target);
            target.Teleport(p + new Vector3(-6f, 0.1f, 0f));
            Check("ZL 滾球:0.1 u/F(6 m/s)、駕駛藏起來、背後的子彈打在球上、接觸 40、回血同潛墨",
                _special.BallMode && hidden && Mathf.Abs(speed - 6f) < 0.35f && protectedByBall
                && Mathf.Approximately(contact, 40f) && regen,
                $"滾球={_special.BallMode} 人藏起來={hidden} 速度 {speed:0.00} m/s;球擋子彈={protectedByBall};接觸 {contact:0.0};" +
                $"潛墨回血={regen}");

            // 爬塗得到的牆(ClimbWall_High:轉 90°,牆面在 x = 12 朝 −x)
            _player.Teleport(new Vector3(8.8f, 0.1f, 0f), 90f);
            _aim.From(_player.transform, new Vector3(30f, 1.2f, 0f));
            _intent.Move = new Vector2(0f, 1f);
            float climbStart = _player.transform.position.y;
            bool climbed = false;
            bool everClimbing = false;
            float maxY = climbStart;
            float until = Time.time + 2f;
            while (Time.time < until)
            {
                everClimbing |= _special.Climbing;
                maxY = Mathf.Max(maxY, _player.transform.position.y);
                climbed |= _special.Climbing && _player.transform.position.y > climbStart + 1.2f;
                yield return null;
            }
            _intent.Move = Vector2.zero;
            Check("滾球爬塗得到的牆(直直往上)", climbed,
                $"爬牆={everClimbing} 最高 {maxY:0.00} m;結束位置 {_player.transform.position};滾球={_special.BallMode}");

            // 放開 ZL:車身轉向鏡頭、20F 不能射
            _player.Teleport(p + Vector3.up * 0.1f, 0f);
            _aim.From(_player.transform, p + new Vector3(20f, 1.2f, 20f));
            yield return new WaitForSeconds(0.1f);
            _intent.Attack = true;
            int shots0 = _special.TurretShots;
            _intent.Squid = false;
            float releasedAt = Time.time;
            yield return WaitUntil(() => _special.TurretShots > shots0, 1f);
            float firstShot = (_special.LastTurretShotTime - releasedAt) * 60f;
            float yawError = Mathf.Abs(Mathf.DeltaAngle(_special.TankYaw, 45f));
            _intent.Attack = false;
            Check("放開 ZL:車身直接轉向鏡頭、20F 後才能射",
                !_special.BallMode && firstShot >= 20f - 0.01f && firstShot <= 20f + FrameSlack + 1f && yawError < 3f,
                $"第一發在放開後 {firstShot:0.0}F;車身與鏡頭差 {yawError:0.0}°");
            yield return WaitUntil(() => !_gauge.IsActive, 9f);
        }

        private IEnumerator BreakTank()
        {
            Vector3 p = new Vector3(-20f, 0f, -18f);
            yield return Activate(p, 90f, p + new Vector3(20f, 1.2f, 0f));
            yield return WaitUntil(() => _special.Armored, 1.5f);
            _gauge.SetPointsForTest(100f);
            yield return new WaitForSeconds(0.2f);
            CrabTankBody body = _special.Body;
            float gaugeBefore = _gauge.Points;
            body.ApplyDamage(new DamageInfo(600f, body.transform.position + Vector3.up, Team.Bravo, _enemy, DamageKind.Main,
                AttackClass.Shooter));
            yield return null;
            bool ejected = _special.Ejected && _special.InEndLag && _health.IsAlive;
            float brokeAt = Time.time;
            yield return WaitUntil(() => !_gauge.IsActive, 1f);
            float lag = (Time.time - brokeAt) * 60f;
            Check("裝甲被打爆:駕駛彈出(沒倒下)、25F 後結束、能量歸 0",
                ejected && Mathf.Abs(lag - 25f) <= FrameSlack + 1f && _gauge.Points < 0.01f && gaugeBefore > 50f,
                $"彈出={ejected};打爆 → 結束 {lag:0.0}F;能量 {gaugeBefore:0.0} → {_gauge.Points:0.0}");
        }
    }
}

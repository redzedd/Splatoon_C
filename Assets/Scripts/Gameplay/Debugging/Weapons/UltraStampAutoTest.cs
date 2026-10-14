using System.Collections;
using System.Collections.Generic;
using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // Ultra Stamp(D2)驗收,試射場西半部。全部走真實路徑(按 R 發動、攻擊/副武器/跳躍鍵、真的墨彈與爆炸):
    //   (a) 發動 17F 後能揮;拿著時可以潛游、主武器被佔用
    //   (b) 點攻擊:30F 後敲到(從烏賊 35F);落點的敵人 100、落點 4 m 內的 40(被直擊的不再吃)、更遠的沒事;蓋印章(塗墨)
    //   (c) 剛敲完再點:兩下之間 20F
    //   (d) 按住連敲:每 13F 一下、前進 0.155 u/F(9.3 m/s);槌頭擋住正面的敵彈與爆炸、平常拿著擋不到;連敲中跳起原地不前進
    //   (e) 空中攻擊:8F 迴旋,水平 4.5 內的敵人 100
    //   (f) 450F 到就結束
    //   (g) 副武器鍵:42F 起手後丟出(丟出即結束、能量歸 0);路徑上的敵人直擊 220;落地爆炸 220(≤ 3.6)/ 60(≤ 8.0);平射約 25 m
    [AutoTest(Scene = AutoTestAttribute.TrainingScene, Timeout = 150f, Order = 243)]
    public sealed class UltraStampAutoTest : AutoTestBase
    {
        private readonly List<Object> _spawned = new List<Object>();
        private readonly List<Inkling> _revive = new List<Inkling>();
        private readonly Dictionary<Inkling, List<float>> _hits = new Dictionary<Inkling, List<float>>();
        private int _errors;
        private string _firstError;
        private float _maxFrame = 1f;

        private GameObject _playerGo;
        private Inkling _player;
        private ScriptedIntent _intent;
        private PlayerSpecialGauge _gauge;
        private InklingHealth _health;
        private SquidController _squid;
        private UltraStampSpecial _special;
        private UltraStampConfig _config;
        private SpecialTestAim _aim;
        private Inkling _shooter;
        private int _playerHits;

        private float FrameSlack => Mathf.Max(1.5f, _maxFrame) + 0.5f;

        public static void Run()
        {
            Launch<UltraStampAutoTest>();
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
            _config = Resources.Load<UltraStampConfig>("Weapons/Special_ultra_stamp");
            if (_player == null || _config == null || _config.HammerPrefab == null || _config.ThrowPrefab == null)
            {
                FailSetup("找不到 Player / Resources/Weapons/Special_ultra_stamp(先跑 M7 Setup)");
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
                FailSetup("換裝 Ultra Stamp 失敗");
                yield break;
            }
            _special = _player.GetComponent<UltraStampSpecial>();
            _health.Damaged += (dealt, info) => _playerHits++;
            _health.Invulnerable = true;
            _shooter = SpecialTestKit.CloneEnemy(_playerGo, new Vector3(-22f, 0.1f, 22f), "StampShooter", out _);
            _shooter.GetComponent<InklingHealth>().Invulnerable = true;
            _spawned.Add(_shooter);

            yield return StampSession();
            yield return ThrowSession();

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

        private Inkling Target(Vector3 position, string name)
        {
            Inkling clone = SpecialTestKit.CloneEnemy(_playerGo, SpecialTestKit.Ground(position) + Vector3.up * 0.1f, name, out _);
            _spawned.Add(clone);
            _revive.Add(clone);
            var list = new List<float>();
            _hits[clone] = list;
            clone.GetComponent<InklingHealth>().Damaged += (dealt, info) =>
            {
                if (info.AttackClass == AttackClass.UltraStampSwing || info.AttackClass == AttackClass.UltraStampThrow)
                {
                    list.Add(info.Amount);
                }
            };
            return clone;
        }

        private float Sum(Inkling target)
        {
            float total = 0f;
            foreach (float d in _hits[target])
            {
                total += d;
            }
            return total;
        }

        private IEnumerator StampSession()
        {
            Vector3 p = new Vector3(-18f, 0f, -4f);
            _player.Teleport(p + Vector3.up * 0.1f, 90f);
            _aim.From(_player.transform, p + new Vector3(20f, 1.2f, 0f));
            yield return new WaitForSeconds(0.3f);
            _gauge.SetPointsForTest(100f);
            _intent.PressSpecial();
            yield return WaitUntil(() => _gauge.IsActive, 1f);
            float activatedAt = _special.ActivatedAt;
            yield return WaitUntil(() => _special.Idle, 1f);
            float idleAt = (Time.time - activatedAt) * 60f;
            _intent.Squid = true;
            yield return new WaitForSeconds(0.2f);
            bool swam = _squid.IsSquid;
            // (b) 從烏賊按攻擊 → 35F 敲到
            _intent.Attack = true;
            yield return null;
            _intent.Attack = false;
            _intent.Squid = false;
            yield return WaitUntil(() => _special.Stamps > 0, 1.5f);
            float squidLag = (_special.LastImpactTime - _special.LastSwingStartedAt) * 60f;
            Check("發動 17F 後能揮;拿著時可以潛游、主武器被佔用;從烏賊按攻擊 35F 敲到",
                Mathf.Abs(idleAt - 17f) <= FrameSlack && swam && _special.BlocksMainWeapon && _special.ModelShown
                && Mathf.Abs(squidLag - 35f) < 0.01f,
                $"可揮 {idleAt:0.0}F;潛游={swam};模型={_special.ModelShown};烏賊 → 敲到 {squidLag:0.0}F");
            yield return new WaitForSeconds(0.6f);

            // (b) 人形點攻擊:30F 敲到;落點 100、4 m 內 40、更遠沒事;蓋印章
            _player.Teleport(p + Vector3.up * 0.1f, 90f);
            yield return new WaitForSeconds(0.1f);
            Vector3 impact = p + new Vector3(_config.ImpactDistance, 0f, 0f);
            Inkling direct = Target(impact, "StampDirect");
            Inkling side = Target(impact + new Vector3(0f, 0f, 3.1f), "StampSide");
            Inkling far = Target(impact + new Vector3(0f, 0f, -5.6f), "StampFar");
            yield return new WaitForSeconds(0.2f);
            int stamps0 = _special.Stamps;
            _intent.Attack = true;
            yield return null;
            _intent.Attack = false;
            yield return WaitUntil(() => _special.Stamps > stamps0, 1f);
            float humanLag = (_special.LastImpactTime - _special.LastSwingStartedAt) * 60f;
            Vector3 stampAt = _special.LastImpactPoint;
            yield return new WaitForSeconds(0.1f);
            byte owner = GroundOwner(stampAt);
            Check("點攻擊 30F 敲到:落點的敵人 100、4 m 內 40(不重複)、更遠的沒事;落點蓋上印章",
                Mathf.Abs(humanLag - 30f) < 0.01f && Mathf.Approximately(Sum(direct), 100f)
                && Mathf.Approximately(Sum(side), 40f) && Sum(far) == 0f && owner == (byte)_player.Team,
                $"敲到 {humanLag:0.0}F;落點 {Sum(direct):0}、旁邊 {Sum(side):0}、遠處 {Sum(far):0};印章墨={owner}");

            // (c) 剛敲完再點:兩下之間 20F
            float firstImpact = _special.LastImpactTime;
            _intent.Attack = true;
            yield return null;
            _intent.Attack = false;
            yield return WaitUntil(() => _special.LastImpactTime > firstImpact, 1f);
            float between = (_special.LastImpactTime - firstImpact) * 60f;
            Check("剛敲完再點:兩下之間 20F(振り間隔)", Mathf.Abs(between - 20f) <= FrameSlack + 0.01f,
                $"兩下相隔 {between:0.0}F");
            foreach (Inkling t in new[] { direct, side, far })
            {
                _revive.Remove(t);
                t.Teleport(p + new Vector3(0f, 0.1f, 20f));
            }
            yield return new WaitForSeconds(0.35f);

            // (d) 按住連敲:每 13F、前進 9.3 m/s;槌頭擋正面的敵彈與爆炸(使用者保持無敵:子彈照樣停在身上,以命中對象判斷)
            _player.Teleport(p + Vector3.up * 0.1f, 90f);
            yield return new WaitForSeconds(0.1f);
            int blocked = 0;
            int hitPlayer = 0;
            InkBulletImpactHandler handler = (in InkBulletImpact impactInfo) =>
            {
                if (impactInfo.Team != Team.Bravo)
                {
                    return;
                }
                if (impactInfo.Target is UltraStampGuard)
                {
                    blocked++;
                }
                else if (ReferenceEquals(impactInfo.Target, _health))
                {
                    hitPlayer++;
                }
            };
            InkBullet.Impacted += handler;
            // 平常拿著:正面的子彈打得到
            Vector3 chest = _player.transform.position + Vector3.up * 1f;
            Vector3 muzzle = chest + new Vector3(12f, 0f, 0f);
            InkBullet.Fire(InkBulletSpec.Splattershot, muzzle, chest - muzzle, Team.Bravo, _shooter);
            yield return new WaitForSeconds(0.3f);
            bool idleHit = hitPlayer > 0 && blocked == 0;
            blocked = 0;
            _intent.Attack = true;
            yield return WaitUntil(() => _special.Rushing, 1f);
            int stampsAtRush = _special.Stamps;
            Vector3 r0 = _player.transform.position;
            float rt0 = Time.time;
            // 連敲中每 0.1 秒從正前方 12 m 打一發
            int shots = 0;
            float end = Time.time + 1f;
            bool explosionBlocked = false;
            float lastImpactSeen = _special.LastImpactTime;
            bool detonated = false;
            while (Time.time < end)
            {
                if (Time.time - rt0 >= shots * 0.1f)
                {
                    shots++;
                    Vector3 c = _player.transform.position + Vector3.up * 1.1f;
                    Vector3 from = c + new Vector3(12f, 0f, 0f);
                    InkBullet.Fire(InkBulletSpec.Splattershot, from, c - from, Team.Bravo, _shooter);
                }
                // 剛敲下的那一幀在槌頭前方引爆敵方爆炸:槌頭擋住(使用者不在命中名單裡)
                if (!detonated && _special.LastImpactTime > lastImpactSeen && _special.GuardRaised)
                {
                    detonated = true;
                    ExplosionSpec blast = ExplosionSpec.SplatBomb;
                    blast.PaintRadius = 0f;
                    blast.PaintRayCount = 0;
                    blast.KnockbackAccel = 0f;
                    Vector3 front = _player.transform.position + new Vector3(_config.ImpactDistance + 1.4f, 0.8f, 0f);
                    ExplosionResult result = Explosion.Detonate(blast, front, Team.Bravo, _shooter);
                    explosionBlocked = result.BlockedByCover >= 1 && result.TargetsHit == 0;
                }
                lastImpactSeen = _special.LastImpactTime;
                yield return null;
            }
            InkBullet.Impacted -= handler;
            float rushSeconds = Time.time - rt0;
            Vector3 r1 = _player.transform.position;
            float rushSpeed = new Vector2(r1.x - r0.x, r1.z - r0.z).magnitude / rushSeconds;
            int rushStamps = _special.Stamps - stampsAtRush;
            float expectedStamps = rushSeconds * 60f / 13f;
            // 連敲中跳起:空中不前進
            _intent.PressJump();
            yield return new WaitForSeconds(0.1f);
            Vector3 a0 = _player.transform.position;
            yield return new WaitForSeconds(0.2f);
            Vector3 a1 = _player.transform.position;
            float airDrift = new Vector2(a1.x - a0.x, a1.z - a0.z).magnitude;
            _intent.Attack = false;
            Check("按住連敲:每 13F 一下、前進 0.155 u/F(9.3 m/s);連敲中跳起原地不前進",
                Mathf.Abs(rushSpeed - 9.3f) < 0.5f && Mathf.Abs(rushStamps - expectedStamps) <= 1.2f && airDrift < 0.2f,
                $"前進 {rushSpeed:0.00} m/s;{rushSeconds:0.00} 秒敲 {rushStamps} 下(應 {expectedStamps:0.0});跳起後 0.2 秒水平移動 {airDrift:0.00} m");
            Check("槌頭擋彈:連敲中正面的敵彈有被槌頭擋下、槌頭前方的敵方爆炸被擋住;平常拿著擋不到",
                blocked >= 1 && explosionBlocked && idleHit,
                $"連敲中 {shots} 發被擋 {blocked} 發;爆炸被擋={explosionBlocked};拿著時被打到={idleHit}");
            yield return new WaitForSeconds(0.4f);

            // (e) 空中迴旋:8F 後,水平 4.5 內的敵人 100
            _player.Teleport(p + Vector3.up * 0.1f, 90f);
            Inkling near = Target(p + new Vector3(0f, 0f, 3.5f), "StampAirNear");
            Inkling away = Target(p + new Vector3(0f, 0f, -6.5f), "StampAirFar");
            yield return new WaitForSeconds(0.2f);
            _intent.PressJump();
            yield return new WaitForSeconds(0.12f);
            int air0 = _special.AirSwings;
            _intent.Attack = true;
            yield return WaitUntil(() => _special.AirSwings > air0, 0.5f);
            float swingAt = _special.LastSwingStartedAt;
            _intent.Attack = false;
            yield return new WaitForSeconds(0.3f);
            float hitLag = (_special.LastImpactTime - swingAt) * 60f;
            Check("空中攻擊:8F 迴旋,水平 4.5 m 內的敵人 100、外面的沒事",
                Mathf.Abs(hitLag - 8f) <= FrameSlack && Mathf.Approximately(Sum(near), 100f) && Sum(away) == 0f,
                $"迴旋 → 打到 {hitLag:0.0}F;3.5 m 的 {Sum(near):0}、6.5 m 的 {Sum(away):0}");
            _revive.Remove(near);
            _revive.Remove(away);

            // (f) 450F 到就結束
            yield return WaitUntil(() => !_gauge.IsActive, 8f);
            float endedAt = (Time.time - activatedAt) * 60f;
            Check("450F(7.5 s)到就結束", Mathf.Abs(endedAt - 450f) <= FrameSlack + 1f && !_special.ModelShown,
                $"結束在 {endedAt:0.0}F;模型收起={!_special.ModelShown}");
        }

        private IEnumerator ThrowSession()
        {
            // 投擲道:z = 16(落點周圍沒有障礙物;Box_Far 在 z 7~9)
            Vector3 p = new Vector3(-22f, 0f, 16f);
            _player.Teleport(p + Vector3.up * 0.1f, 90f);
            _aim.Set(p + new Vector3(-0.3f, 1.6f, 0f), p + new Vector3(30f, 1.6f, 0f));
            yield return new WaitForSeconds(0.3f);
            _gauge.SetPointsForTest(100f);
            _intent.PressSpecial();
            yield return WaitUntil(() => _special.Idle, 1.5f);
            // 預測落點(與丟出的槌同一套彈道:平地 y = 0,判定半徑 0.45)
            Vector3 dir = _aim.Forward;
            Vector3 origin = _special.ThrowOrigin(dir);
            BulletState state = BulletBallistics.Launch(origin, dir, UltraStampModel.ThrowMotion,
                Vector3.up * UltraStampModel.ThrowSpeedY);
            Vector3 landing = origin;
            Vector3 descending = origin + dir * 8f;
            bool foundDescending = false;
            for (int i = 0; i < 400; i++)
            {
                BulletBallistics.Step(ref state, UltraStampModel.ThrowMotion);
                // 往下掉到胸口高度的那一點:路徑上的敵人放這裡(槌先往上飛,近處會從頭上過去)
                if (!foundDescending && state.Velocity.y < 0f && state.Position.y <= 1.3f)
                {
                    foundDescending = true;
                    descending = state.Position;
                }
                if (state.Position.y <= 0.45f)
                {
                    landing = state.Position;
                    break;
                }
            }
            landing.y = 0f;
            float predicted = new Vector2(landing.x - origin.x, landing.z - origin.z).magnitude;
            // 路徑上的敵人離落點至少 4 m(不然被直擊後又在爆炸裡);落點前後各擺近、遠爆風的敵人
            Vector3 pathPoint = descending;
            if (Vector3.Distance(new Vector3(pathPoint.x, 0f, pathPoint.z), landing) < 4f)
            {
                pathPoint = landing - new Vector3(dir.x, 0f, dir.z).normalized * 4.5f;
            }
            Inkling inPath = Target(new Vector3(pathPoint.x, 0f, pathPoint.z), "ThrowPath");
            Inkling nearBlast = Target(landing + new Vector3(0f, 0f, 2.6f), "ThrowNear");
            Inkling farBlast = Target(landing + new Vector3(0f, 0f, -6.5f), "ThrowFar");
            yield return new WaitForSeconds(0.2f);
            float gaugeBefore = _gauge.Points;
            _intent.Sub = true;
            yield return WaitUntil(() => _special.WindingUp, 0.5f);
            _intent.Sub = false;
            yield return WaitUntil(() => !_gauge.IsActive, 1.5f);
            // 丟出的那一刻讀能量(之後被炸倒的敵人爆墨會算給丟的人,能量又會漲)
            float gaugeAfter = _gauge.Points;
            float windup = (_special.LastThrowTime - _special.LastWindupStartedAt) * 60f;
            InkBullet thrown = _special.LastThrown;
            var impacts = new List<InkBulletImpact>();
            InkBulletImpactHandler handler = (in InkBulletImpact impact) =>
            {
                if (impact.Bullet == thrown && impact.Exploded)
                {
                    impacts.Add(impact);
                }
            };
            InkBullet.Impacted += handler;
            yield return new WaitForSeconds(1.5f);
            InkBullet.Impacted -= handler;
            Vector3 landed = impacts.Count > 0 ? impacts[0].Point : Vector3.zero;
            float range = new Vector2(landed.x - origin.x, landed.z - origin.z).magnitude;
            Check("副武器鍵:42F 起手後丟出、丟出即結束且能量歸 0;平射約 25 m(JP wiki 5.0 條)",
                Mathf.Abs(windup - 42f) <= FrameSlack && !_gauge.IsActive && gaugeAfter < 0.01f && gaugeBefore > 80f
                && impacts.Count == 1 && Mathf.Abs(range - predicted) < 1.2f && range > 21f && range < 29f,
                $"起手 {windup:0.0}F;能量 {gaugeBefore:0.0} → {gaugeAfter:0.0};落地 {range:0.0} m(預測 {predicted:0.0})");
            Check("丟出的槌:路徑上的敵人直擊 220(貫穿);落地爆炸 3.6 m 內 220、8 m 內 60",
                _hits[inPath].Contains(220f) && Mathf.Approximately(Sum(nearBlast), 220f)
                && Mathf.Approximately(Sum(farBlast), 60f),
                $"路徑上 {Sum(inPath):0}(直擊 {_hits[inPath].Contains(220f)});落點 2.6 m {Sum(nearBlast):0}、6.5 m {Sum(farBlast):0}");
        }

        private static byte GroundOwner(Vector3 point)
        {
            if (Physics.Raycast(point + Vector3.up * 1f, Vector3.down, out RaycastHit hit, 3f, CombatMasks.Terrain,
                    QueryTriggerInteraction.Ignore))
            {
                var surface = hit.collider.GetComponent<PaintableSurface>();
                return surface != null ? surface.SampleOwnership(hit.point, hit.normal) : (byte)0;
            }
            return 0;
        }
    }
}

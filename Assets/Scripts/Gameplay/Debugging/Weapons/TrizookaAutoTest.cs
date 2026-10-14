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
    // Trizooka(D1)驗收,試射場西半部。全部走真實路徑(按 R 發動、按住攻擊射擊、InkBullet 墨團、Explosion):
    //   (a) 發動:墨量回滿、主武器被佔用、手上換發射器;前隙 23F + 前搖 15F → 第一發在 38F,按住連射間隔 55F
    //   (b) 每發 3 顆墨團每 4F 一顆;墨團 10F 後離彈道軸 1.0 u(螺旋);第 3 發出膛即結束、能量歸 0
    //   (c) 不射:330F(5.5 s)到自動結束;走速 4.2 m/s、射擊前後 2.4 m/s;可以變烏賊;發動中塗地不充能
    //   (d) 直擊 220 貫穿兩個敵人;地形爆炸:假人吃到的傷害 = 第一顆炸到它的墨團的近 53(≤ 2.5)/ 遠 35(≤ 4.0),不重複;
    //       爆炸塗地;大招自己的墨不充能
    //   (e) 發動中被擊倒(S3 持續型):能量在死亡瞬間停止、剩下的留 50%
    [AutoTest(Scene = AutoTestAttribute.TrainingScene, Timeout = 150f, Order = 230)]
    public sealed class TrizookaAutoTest : AutoTestBase
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
        private PlayerInkTank _tank;
        private InklingHealth _health;
        private TrizookaSpecial _special;
        private SpecialTestAim _aim;
        private TrizookaConfig _config;

        public static void Run()
        {
            Launch<TrizookaAutoTest>();
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
            _config = Resources.Load<TrizookaConfig>("Weapons/Special_trizooka");
            GameObject dummyGo = GameObject.Find("Dummy_Near");
            if (_player == null || _config == null || _config.GlobPrefab == null || dummyGo == null)
            {
                FailSetup("找不到 Player / Dummy_Near / Resources/Weapons/Special_trizooka(先跑 M7 Setup)");
                yield break;
            }
            _intent = ScriptedIntent.Drive(_player);
            _gauge = _player.GetComponent<PlayerSpecialGauge>();
            _tank = _player.GetComponent<PlayerInkTank>();
            _health = _player.GetComponent<InklingHealth>();
            IAimView originalView = _player.AimView;
            _aim = new SpecialTestAim();
            _player.AimView = _aim;
            if (!SpecialTestKit.EquipSpecial(_player, _config))
            {
                FailSetup("換裝 Trizooka 失敗");
                yield break;
            }
            _special = _player.GetComponent<TrizookaSpecial>();
            _health.Invulnerable = true;
            _player.Teleport(new Vector3(-22f, 0.1f, -20f), 90f);
            yield return new WaitForSeconds(0.4f);

            yield return ActivationAndCadence();
            yield return DurationMovementAndCharge();
            yield return ShotMotionSpeed();
            yield return DirectPierce(dummyGo);
            yield return TerrainBlast(dummyGo.GetComponent<TargetDummy>());
            yield return DeathKeepsHalf();

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

        // ── (a)(b) 發動、節奏、墨團、結束 ──
        private IEnumerator ActivationAndCadence()
        {
            Vector3 feet = _player.transform.position;
            _aim.From(_player.transform, feet + new Vector3(20f, 0.8f, 0f));
            Drain(_tank);
            _gauge.SetPointsForTest(100f);
            yield return null;
            GameObject gun = _player.GetComponent<CharacterVisual>() != null
                ? _player.GetComponent<CharacterVisual>().WeaponModel?.gameObject
                : null;
            Renderer gunRenderer = gun != null ? gun.GetComponent<Renderer>() : null;
            // 發動前就按住攻擊(前隙中的按鍵會記住)
            _intent.Attack = true;
            _intent.PressSpecial();
            yield return WaitUntil(() => _gauge.IsActive, 1f);
            yield return null;
            float t0 = _special.ActivatedAt;
            bool gunHidden = gunRenderer == null || gunRenderer.forceRenderingOff;
            Check("發動:墨量回滿、能量計時中、手上換成發射器、主武器被佔用",
                _gauge.IsActive && _tank.Normalized > 0.99f && _special.ModelShown && gunHidden
                && _gauge.ActiveSpecial == _special && _special.BlocksMainWeapon,
                $"啟動={_gauge.IsActive} 墨量={_tank.Normalized:0.00} 發射器={_special.ModelShown} 原槍隱藏={gunHidden}");

            var globTimes = new List<float>();
            int firedBefore = InkBullet.FiredCount;
            float deadline = Time.time + 4f;
            float t1 = -1f;
            float t2 = -1f;
            float glob10Offset = -1f;
            InkBullet firstGlob = null;
            int firstGlobFrameSeen = -1;
            while (_special.ShotsFired < 3 && Time.time < deadline)
            {
                int fired = InkBullet.FiredCount;
                while (firedBefore < fired)
                {
                    firedBefore++;
                    if (globTimes.Count < 3)
                    {
                        globTimes.Add(Time.time);
                    }
                }
                if (_special.ShotsFired >= 1 && t1 < 0f)
                {
                    t1 = _special.LastShotTime;
                    firstGlob = _special.LastGlob;
                }
                if (_special.ShotsFired >= 2 && t2 < 0f)
                {
                    t2 = _special.LastShotTime;
                }
                // 第一顆墨團飛到第 10–14F:離彈道軸的距離 = 螺旋半徑(10F 長滿 1.0 u)
                if (firstGlob != null && firstGlob.IsFlying && glob10Offset < 0f && firstGlob.State.Frame >= 10)
                {
                    firstGlobFrameSeen = firstGlob.State.Frame;
                    glob10Offset = (firstGlob.Position - firstGlob.State.Position).magnitude;
                }
                yield return null;
            }
            bool ended = !_gauge.IsActive;
            float points = _gauge.Points;
            // 發射器在最後一發後 0.3 秒收起
            yield return new WaitForSeconds(0.45f);
            float firstFrames = (t1 - t0) * 60f;
            float repeatFrames = (t2 - t1) * 60f;
            Check("第一發在發動後 38F 出膛(發動前隙 23F + 前搖 15F)", Mathf.Abs(firstFrames - 38f) <= 1.5f,
                $"{firstFrames:0.0}F");
            Check("按住連射間隔 55F(RepeatFrame)", Mathf.Abs(repeatFrames - 55f) <= 1.5f, $"{repeatFrames:0.0}F");
            float spread = globTimes.Count >= 3 ? (globTimes[2] - globTimes[0]) * 60f : -1f;
            Check("每發 3 顆墨團,每 4F 一顆(第 1 → 3 顆 8F)", _special.GlobsFired == 9 && Mathf.Abs(spread - 8f) <= 0.5f + Slack,
                $"共 {_special.GlobsFired} 顆;第一發 3 顆跨 {spread:0.0}F");
            Check("螺旋:墨團 10F 後離彈道軸 1.0 u", Mathf.Abs(glob10Offset - 1f) < 0.02f,
                $"第 {firstGlobFrameSeen}F 偏移 {glob10Offset:0.000} u");
            Check("第 3 發出膛即結束、能量歸 0;發射器收起、原槍恢復",
                ended && points == 0f && !_special.ModelShown && (gunRenderer == null || !gunRenderer.forceRenderingOff),
                $"結束={ended} 能量={points:0.0} 發射器={_special.ModelShown}");
            _intent.Attack = false;
            yield return new WaitForSeconds(0.6f);
        }

        // ── (c) 330F 自動結束、走速、烏賊、發動中不充能 ──
        private IEnumerator DurationMovementAndCharge()
        {
            _player.Teleport(new Vector3(-22f, 0.1f, 10f), 90f);
            _aim.From(_player.transform, _player.transform.position + new Vector3(20f, 1f, 0f));
            yield return new WaitForSeconds(0.3f);
            _gauge.SetPointsForTest(100f);
            _intent.PressSpecial();
            yield return WaitUntil(() => _gauge.IsActive, 1f);
            float t0 = _special.ActivatedAt;

            // 走速:扛著 0.07 u/F = 4.2 m/s(不吃跑速能力)
            _intent.Move = new Vector2(0f, 1f);
            yield return new WaitForSeconds(0.4f);
            Vector3 p0 = _player.transform.position;
            float s0 = Time.time;
            yield return new WaitForSeconds(0.8f);
            float walk = Flat(_player.transform.position - p0) / (Time.time - s0);
            _intent.Move = Vector2.zero;
            Check("扛著走速 4.2 m/s(0.07 u/F)", Mathf.Abs(walk - 4.2f) < 0.25f, $"{walk:0.00} m/s");

            float before = _gauge.Points;
            _player.OnTurfInked(20f * TurfPoints.SquareMetresPerPoint, true);
            float after = _gauge.Points;
            Check("發動中塗地不充能", after <= before + 0.001f, $"{before:0.00} → {after:0.00}");

            _intent.Squid = true;
            yield return new WaitForSeconds(0.2f);
            bool squid = _player.GetComponent<SquidController>().IsSquid;
            _intent.Squid = false;
            Check("扛著可以變烏賊潛游(BlocksSquid = false)", squid && _gauge.IsActive, $"烏賊={squid}");

            yield return WaitUntil(() => !_gauge.IsActive, 7f);
            float frames = (Time.time - t0) * 60f;
            Check("不射:330F(5.5 s)後自動結束、能量歸 0", !_gauge.IsActive && Mathf.Abs(frames - 330f) <= 2f + Slack
                                                      && _gauge.Points == 0f && _special.ShotsFired == 0,
                $"{frames:0.0}F 能量={_gauge.Points:0.0} 發數={_special.ShotsFired}");
            yield return new WaitForSeconds(0.4f);
            float normal = MeasureNothing();
            Check("結束後移除走速限制", Mathf.Approximately(normal, 1f), $"RunSpeed×{normal:0.00}");
        }

        private float MeasureNothing()
        {
            return _player.Modifiers.RunSpeedMultiplier;
        }

        // ── (c') 射擊前後的走速 0.04 u/F = 2.4 m/s ──
        private IEnumerator ShotMotionSpeed()
        {
            _player.Teleport(new Vector3(-22f, 0.1f, 0f), 90f);
            _aim.From(_player.transform, _player.transform.position + new Vector3(24f, 3f, 0f));
            yield return new WaitForSeconds(0.3f);
            _gauge.SetPointsForTest(100f);
            _intent.PressSpecial();
            yield return WaitUntil(() => _gauge.IsActive, 1f);
            _intent.Move = new Vector2(0f, 1f);
            yield return new WaitForSeconds(0.5f);
            _intent.Attack = true;
            // 按下後 5F(前搖中)開始量,量到出膛後 35F(後隙中)
            yield return WaitUntil(() => _special.InShotMotion, 1f);
            yield return new WaitForSeconds(5f / 60f);
            Vector3 p0 = _player.transform.position;
            float s0 = Time.time;
            yield return new WaitForSeconds(40f / 60f);
            float speed = Flat(_player.transform.position - p0) / (Time.time - s0);
            bool inMotion = _special.InShotMotion;
            _intent.Attack = false;
            _intent.Move = Vector2.zero;
            Check("射擊前搖/後隙走速 2.4 m/s(0.04 u/F)", inMotion && Mathf.Abs(speed - 2.4f) < 0.3f,
                $"{speed:0.00} m/s(仍在射擊動作中={inMotion})");
            yield return WaitUntil(() => !_gauge.IsActive, 7f);
            yield return new WaitForSeconds(0.3f);
        }

        // ── (d) 直擊 220 貫穿兩個敵人 ──
        private IEnumerator DirectPierce(GameObject dummyTemplate)
        {
            _player.Teleport(new Vector3(-22f, 0.1f, 18f), 90f);
            Vector3 feet = new Vector3(-22f, 0f, 18f);
            Inkling e1 = SpecialTestKit.CloneEnemy(_playerGo, feet + new Vector3(10f, 0.1f, 0f), "TrizookaEnemyA", out _);
            Inkling e2 = SpecialTestKit.CloneEnemy(_playerGo, feet + new Vector3(13f, 0.1f, 0f), "TrizookaEnemyB", out _);
            _spawned.Add(e1);
            _spawned.Add(e2);
            var h1 = e1.GetComponent<InklingHealth>();
            var h2 = e2.GetComponent<InklingHealth>();
            var hits1 = new List<DamageInfo>();
            var hits2 = new List<DamageInfo>();
            System.Action<float, DamageInfo> on1 = (d, info) => hits1.Add(info);
            System.Action<float, DamageInfo> on2 = (d, info) => hits2.Add(info);
            h1.Damaged += on1;
            h2.Damaged += on2;
            yield return new WaitForSeconds(0.4f);
            _aim.From(_player.transform, e1.transform.position + Vector3.up * 0.9f);
            _gauge.SetPointsForTest(100f);
            _intent.PressSpecial();
            yield return WaitUntil(() => _gauge.IsActive, 1f);
            _intent.Attack = true;
            yield return WaitUntil(() => _special.ShotsFired >= 1, 2f);
            _intent.Attack = false;
            yield return new WaitForSeconds(1.2f);
            h1.Damaged -= on1;
            h2.Damaged -= on2;
            int pierce1 = CountPierce(h1);
            int pierce2 = CountPierce(h2);
            bool one1 = hits1.Count == 1 && Mathf.Approximately(hits1[0].Amount, 220f)
                        && hits1[0].AttackClass == AttackClass.Trizooka && hits1[0].Kind == DamageKind.Special;
            bool one2 = hits2.Count == 1 && Mathf.Approximately(hits2[0].Amount, 220f);
            Check("直擊 220 貫穿:前後兩個敵人各吃一次 220(墨團不在人身上停/爆)",
                one1 && one2 && pierce1 == 1 && pierce2 == 1,
                $"A {Describe(hits1)}(貫穿事件 {pierce1})、B {Describe(hits2)}(貫穿事件 {pierce2})");
            yield return WaitUntil(() => !_gauge.IsActive, 7f);
            yield return new WaitForSeconds(0.3f);
        }

        private int CountPierce(IDamageable target)
        {
            int count = 0;
            foreach (InkBulletImpact impact in _impacts)
            {
                if (impact.End == InkBulletEnd.Pierce && ReferenceEquals(impact.Target, target) && impact.Damage > 0f)
                {
                    count++;
                }
            }
            return count;
        }

        private static string Describe(List<DamageInfo> hits)
        {
            if (hits.Count == 0)
            {
                return "0 次";
            }
            var s = new System.Text.StringBuilder();
            s.Append(hits.Count).Append(" 次:");
            foreach (DamageInfo info in hits)
            {
                s.Append(info.Amount.ToString("0.0")).Append(' ');
            }
            return s.ToString();
        }

        // ── (d') 地形爆炸 53 / 35、同一發只算一次、塗地、不充能 ──
        private IEnumerator TerrainBlast(TargetDummy template)
        {
            _player.Teleport(new Vector3(-22f, 0.1f, -8f), 90f);
            // 瞄準 16 m 外的一面牆:墨團螺旋在垂直飛行方向的平面上 ±1 m,打在牆上的落點集中(打地面時會沿飛行方向散開好幾公尺)。
            // 假人站在牆前 0.75 m、準心橫向 2.6 m 與 3.8 m(墨團擦不到它們,只吃爆風)
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "TrizookaBlastWall";
            wall.transform.position = new Vector3(-6f, 2f, -8f);
            wall.transform.localScale = new Vector3(0.5f, 4f, 10f);
            _spawned.Add(wall);
            Physics.SyncTransforms();
            Vector3 aimPoint = new Vector3(-6.25f, 1.5f, -8f);
            TargetDummy near = Instantiate(template, new Vector3(-7f, 0f, -8f + 2.6f), Quaternion.identity);
            TargetDummy far = Instantiate(template, new Vector3(-7f, 0f, -8f - 3.8f), Quaternion.identity);
            _spawned.Add(near);
            _spawned.Add(far);
            yield return new WaitForSeconds(0.3f);
            _aim.From(_player.transform, aimPoint);
            _gauge.SetPointsForTest(100f);
            _intent.PressSpecial();
            yield return WaitUntil(() => _gauge.IsActive, 1f);
            int start = _impacts.Count;
            _intent.Attack = true;
            yield return WaitUntil(() => _special.ShotsFired >= 1, 2f);
            _intent.Attack = false;
            int attackId = -1;
            yield return new WaitForSeconds(1.2f);
            // 這一發的墨團落點(依時間順序)
            var blasts = new List<Vector3>();
            for (int i = start; i < _impacts.Count; i++)
            {
                InkBulletImpact impact = _impacts[i];
                if (attackId < 0)
                {
                    attackId = impact.AttackId;
                }
                if (impact.AttackId == attackId && impact.Exploded && impact.End == InkBulletEnd.Terrain
                    && impact.Point.x > -7f)
                {
                    blasts.Add(impact.Point + impact.Normal * 0.05f);
                }
            }
            float expectedNear = ExpectedBlast(near, blasts, out float nearDistance);
            float expectedFar = ExpectedBlast(far, blasts, out float farDistance);
            Check("地形爆炸:假人吃到第一顆炸到它的墨團的 53(≤ 2.5)/ 35(≤ 4.0),同一發不重複",
                blasts.Count == 3 && expectedNear > 0f && Mathf.Approximately(near.PopupTotal, expectedNear)
                && Mathf.Approximately(far.PopupTotal, expectedFar),
                $"落地爆炸 {blasts.Count} 顆;近假人 {near.PopupTotal:0.0}(期望 {expectedNear},距 {nearDistance:0.00} m)、" +
                $"遠假人 {far.PopupTotal:0.0}(期望 {expectedFar},距 {farDistance:0.00} m)");
            var ground = GameObject.Find("Ground").GetComponent<PaintableSurface>();
            Vector3 first = blasts.Count > 0 ? blasts[0] : aimPoint;
            Vector3 below = new Vector3(first.x - 0.3f, 0f, first.z);
            bool painted = ground.SampleOwnership(below) == (byte)Team.Alpha
                           && ground.SampleOwnership(below + new Vector3(-2.4f, 0f, 0f)) == (byte)Team.Alpha;
            Check("爆炸塗地(半徑 3.2);大招自己的墨不充能", painted && _gauge.Points <= 100f,
                $"落點塗到={painted}");
            yield return WaitUntil(() => !_gauge.IsActive, 7f);
            yield return new WaitForSeconds(0.3f);
            float chargedAfter = _gauge.Points;
            Check("結束後的爆炸塗地不充大招(能量維持 0)", chargedAfter == 0f, $"{chargedAfter:0.00}");
        }

        // 依時間順序第一個在 4.0 內的爆心決定傷害(近 53 ≤ 2.5、遠 35 ≤ 4.0)
        private static float ExpectedBlast(TargetDummy dummy, List<Vector3> blasts, out float distance)
        {
            distance = -1f;
            for (int i = 0; i < blasts.Count; i++)
            {
                float d = Vector3.Distance(dummy.ClosestPoint(blasts[i]), blasts[i]);
                if (d <= 4f)
                {
                    distance = d;
                    return d <= 2.5f ? 53f : 35f;
                }
            }
            return 0f;
        }

        // ── (e) 持續型:發動中被擊倒 → 能量停在死亡瞬間、再留 50% ──
        private IEnumerator DeathKeepsHalf()
        {
            _player.Teleport(new Vector3(-22f, 0.1f, 4f), 90f);
            yield return new WaitForSeconds(0.3f);
            _gauge.SetPointsForTest(100f);
            _intent.PressSpecial();
            yield return WaitUntil(() => _gauge.IsActive, 1f);
            yield return new WaitForSeconds(1f);
            float atDeath = _gauge.Points;
            _health.Invulnerable = false;
            _health.ApplyDamage(new DamageInfo(300f, _player.transform.position + Vector3.up, Team.Bravo, null,
                DamageKind.Main));
            yield return null;
            var life = _player.GetComponent<InklingLifecycle>();
            float kept = _gauge.Points;
            yield return new WaitForSeconds(0.5f);
            float later = _gauge.Points;
            Check("發動中被擊倒:能量停在死亡瞬間再留 50%(S3 持續型)",
                life.IsDead && !_gauge.IsActive && Mathf.Abs(kept - atDeath * 0.5f) < 1f && Mathf.Abs(later - kept) < 0.01f
                && !_special.ModelShown && Mathf.Approximately(_player.Modifiers.RunSpeedMultiplier, 1f),
                $"死亡時 {atDeath:0.0} → 保留 {kept:0.0}(0.5 秒後 {later:0.0})");
            life.ForceRespawnNow();
            _health.Invulnerable = true;
            yield return new WaitForSeconds(0.3f);
        }

        private static float Flat(Vector3 v)
        {
            return new Vector2(v.x, v.z).magnitude;
        }
    }
}

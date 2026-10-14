using System.Collections;
using System.Collections.Generic;
using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // Killer Wail 5.1(D1)驗收,試射場西半部。全部走真實路徑(按 R 發動、準心鎖定、喇叭光束、主武器):
    //   (a) 發動:身後 6 支喇叭;40F 內不能用主武器、之後照常開火(大招仍在)
    //   (b) 時間表:三組在 16 / 52 / 88F 鎖定、20F 預告線、36 / 72 / 108F 開始照射 180F;288F 能量歸 0
    //   (c) 每組可以鎖不同的人(準心 ±80°、離準心射線 ≤ 10 u 內最近的敵人);光束穿牆
    //   (d) 每 5F 3.5 傷:每支 36 次;對靜止目標第一組 36–71F 兩支共 16 次;6 支總傷 756(JP wiki 大假人)
    //   (e) 準心範圍內沒有敵人 → 沿準心照、之後以 0.3°/F(18°/s)單向掃
    //   (f) 持續型:發動中被擊倒 → 能量停在死亡瞬間留 50%;還沒照射的喇叭取消,已在照射的照完
    [AutoTest(Scene = AutoTestAttribute.TrainingScene, Timeout = 150f, Order = 232)]
    public sealed class KillerWailAutoTest : AutoTestBase
    {
        private readonly List<float> _ticksA = new List<float>();
        private readonly List<float> _ticksB = new List<float>();
        private readonly List<Object> _spawned = new List<Object>();
        private int _errors;
        private string _firstError;

        // 量到的最長一幀(幀數):以幀量化的時間檢查加上這個餘裕,機器忙時不假紅
        private float _maxFrame = 1f;
        private float Slack => _maxFrame;
        private bool _keepAlive;

        private GameObject _playerGo;
        private Inkling _player;
        private ScriptedIntent _intent;
        private PlayerSpecialGauge _gauge;
        private InklingHealth _health;
        private KillerWailSpecial _special;
        private KillerWailConfig _config;
        private SpecialTestAim _aim;
        private Inkling _enemyA;
        private Inkling _enemyB;
        private InklingHealth _healthA;
        private InklingHealth _healthB;

        public static void Run()
        {
            Launch<KillerWailAutoTest>();
        }

        private void OnEnable()
        {
            Application.logMessageReceived += OnLog;
        }

        private void OnDisable()
        {
            Application.logMessageReceived -= OnLog;
            if (_healthA != null)
            {
                _healthA.Damaged -= OnDamagedA;
            }
            if (_healthB != null)
            {
                _healthB.Damaged -= OnDamagedB;
            }
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

        private void OnDamagedA(float dealt, DamageInfo info)
        {
            if (info.AttackClass == AttackClass.KillerWail)
            {
                _ticksA.Add(Time.time);
            }
        }

        private void OnDamagedB(float dealt, DamageInfo info)
        {
            if (info.AttackClass == AttackClass.KillerWail)
            {
                _ticksB.Add(Time.time);
            }
        }

        // 靶子不死:每幀補滿(光束每 5F 最多 6 × 3.5,遠不到 100)
        private void Update()
        {
            _maxFrame = Mathf.Max(_maxFrame, Time.deltaTime * 60f);
            if (!_keepAlive)
            {
                return;
            }
            _healthA?.ResetForRespawn();
            _healthB?.ResetForRespawn();
        }

        private IEnumerator Start()
        {
            _playerGo = FindPlayer();
            _player = _playerGo != null ? _playerGo.GetComponent<Inkling>() : null;
            _config = Resources.Load<KillerWailConfig>("Weapons/Special_killer_wail");
            if (_player == null || _config == null || _config.SpeakerPrefab == null)
            {
                FailSetup("找不到 Player / Resources/Weapons/Special_killer_wail(先跑 M7 Setup)");
                yield break;
            }
            _intent = ScriptedIntent.Drive(_player);
            _gauge = _player.GetComponent<PlayerSpecialGauge>();
            _health = _player.GetComponent<InklingHealth>();
            IAimView originalView = _player.AimView;
            _aim = new SpecialTestAim();
            _player.AimView = _aim;
            if (!SpecialTestKit.EquipSpecial(_player, _config))
            {
                FailSetup("換裝 Killer Wail 失敗");
                yield break;
            }
            _special = _player.GetComponent<KillerWailSpecial>();
            _health.Invulnerable = true;

            Vector3 p = new Vector3(-18f, 0f, 0f);
            _player.Teleport(p + Vector3.up * 0.1f, 90f);
            _enemyA = SpecialTestKit.CloneEnemy(_playerGo, p + new Vector3(12f, 0.1f, -4f), "WailEnemyA", out _);
            _enemyB = SpecialTestKit.CloneEnemy(_playerGo, p + new Vector3(14f, 0.1f, 5f), "WailEnemyB", out _);
            _spawned.Add(_enemyA);
            _spawned.Add(_enemyB);
            _healthA = _enemyA.GetComponent<InklingHealth>();
            _healthB = _enemyB.GetComponent<InklingHealth>();
            _healthA.Damaged += OnDamagedA;
            _healthB.Damaged += OnDamagedB;
            // 擋在喇叭與 B 之間的牆(光束要穿牆)
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "WailWall";
            wall.transform.position = p + new Vector3(10f, 1.5f, 3.6f);
            wall.transform.localScale = new Vector3(0.4f, 3f, 3f);
            _spawned.Add(wall);
            Physics.SyncTransforms();
            yield return new WaitForSeconds(0.6f);

            yield return FullRun();
            yield return DeathCancels();
            yield return NoTargetSweep();

            _keepAlive = false;
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

        private Vector3 Chest(Inkling inkling)
        {
            return inkling.transform.position + Vector3.up * 0.9f;
        }

        // ── (a)–(d) ──
        private IEnumerator FullRun()
        {
            _keepAlive = true;
            _ticksA.Clear();
            _ticksB.Clear();
            _aim.From(_player.transform, Chest(_enemyA));
            _gauge.SetPointsForTest(100f);
            _intent.PressSpecial();
            yield return WaitUntil(() => _gauge.IsActive, 1f);
            float t0 = _special.ActivatedAt;
            int speakers = 0;
            for (int i = 0; i < 6; i++)
            {
                speakers += _special.Speaker(i) != null && _special.Speaker(i).isActiveAndEnabled ? 1 : 0;
            }
            yield return new WaitForSeconds(20f / 60f);
            bool blockedEarly = _special.BlocksMainWeapon;
            var shooter = _player.GetComponent<InkShooter>();
            // 第一組在 16F 鎖定 A 之後把準心移到 B(第二組 52F 鎖 B),第二組鎖定後再移回 A
            yield return WaitUntil(() => (Time.time - t0) * 60f >= 30f, 1f);
            _aim.From(_player.transform, Chest(_enemyB));
            yield return WaitUntil(() => (Time.time - t0) * 60f >= 44f, 1f);
            bool blockedLate = _special.BlocksMainWeapon;
            int shotsBefore = shooter != null ? shooter.ShotsFired : 0;
            _intent.Attack = true;
            yield return new WaitForSeconds(0.2f);
            _intent.Attack = false;
            int mainShots = shooter != null ? shooter.ShotsFired - shotsBefore : 0;
            Check("發動:身後 6 支喇叭;40F 內主武器被佔用、之後照常開火(大招仍在進行)",
                speakers == 6 && blockedEarly && !blockedLate && mainShots > 0 && _gauge.IsActive,
                $"喇叭 {speakers} 支、20F 佔用={blockedEarly}、44F 佔用={blockedLate}、主武器出膛 {mainShots} 發");
            yield return WaitUntil(() => (Time.time - t0) * 60f >= 66f, 1f);
            _aim.From(_player.transform, Chest(_enemyA));

            KillerWailSpeaker s0 = _special.Speaker(0);
            KillerWailSpeaker s2 = _special.Speaker(2);
            KillerWailSpeaker s4 = _special.Speaker(4);
            float fire0 = s0 != null ? (s0.FireAt - t0) * 60f : -1f;
            float fire1 = s2 != null ? (s2.FireAt - t0) * 60f : -1f;
            yield return WaitUntil(() => _special.PairsLocked >= 3, 1f);
            float fire2 = s4 != null ? (s4.FireAt - t0) * 60f : -1f;
            float end2 = s4 != null ? (s4.EndAt - t0) * 60f : -1f;
            Check("時間表:照射開始 36 / 72 / 108F(每組間隔 36F、預告線 20F),最後一組 288F 結束",
                Mathf.Abs(fire0 - 36f) < 0.5f && Mathf.Abs(fire1 - 72f) < 0.5f && Mathf.Abs(fire2 - 108f) < 0.5f
                && Mathf.Abs(end2 - 288f) < 0.5f,
                $"{fire0:0.0} / {fire1:0.0} / {fire2:0.0}F,結束 {end2:0.0}F");
            bool lineOfSightBlocked = s2 != null && Physics.Linecast(s2.Origin, Chest(_enemyB), CombatMasks.Terrain,
                QueryTriggerInteraction.Ignore);
            Check("每組各自鎖定:A → B → A(準心範圍內離射線最近的敵人)",
                _special.PairTarget(0) == _enemyA && _special.PairTarget(1) == _enemyB && _special.PairTarget(2) == _enemyA,
                $"{Name(_special.PairTarget(0))} / {Name(_special.PairTarget(1))} / {Name(_special.PairTarget(2))}");

            yield return WaitUntil(() => !_gauge.IsActive, 6f);
            float total = (Time.time - t0) * 60f;
            yield return new WaitForSeconds(0.2f);
            _keepAlive = false;
            Check("持續型:288F(4.8 s)能量歸 0", Mathf.Abs(total - 288f) <= 2f + Slack && _gauge.Points == 0f,
                $"{total:0.0}F 能量={_gauge.Points:0.0}");

            int firstPair = 0;
            foreach (float t in _ticksA)
            {
                float f = (t - t0) * 60f;
                if (f >= 35f && f < 71.9f)
                {
                    firstPair++;
                }
            }
            float firstTick = _ticksA.Count > 0 ? (_ticksA[0] - t0) * 60f : -1f;
            Check("每 5F 3.5 傷:第一組兩支對 A 在 36–71F 共 16 次(每支 8 次),第一次在 36F",
                Mathf.Abs(firstPair - 16) <= 2 && Mathf.Abs(firstTick - 36f) <= 0.5f + Slack,
                $"36–71F {firstPair} 次,第一次 {firstTick:0.0}F");
            int beamsFull = 0;
            for (int i = 0; i < 6; i++)
            {
                KillerWailSpeaker s = _special.Speaker(i);
                if (s != null && s.Ticks == 36)
                {
                    beamsFull++;
                }
            }
            float onTargets = (_ticksA.Count + _ticksB.Count) * 3.5f;
            Check("每支照射 180F = 36 次判定;靜止目標幾乎全中 → 6 支總傷 ≈ 756(JP wiki 大假人總傷 756)",
                beamsFull == 6 && _ticksA.Count >= 140 && _ticksA.Count <= 144 && _ticksB.Count >= 70
                && _ticksB.Count <= 72 && Mathf.Abs(onTargets - 756f) <= 21f,
                $"36 次的喇叭 {beamsFull}/6、A 被打 {_ticksA.Count} 次(滿 144)、B {_ticksB.Count} 次(滿 72)、合計 {onTargets:0.0}");
            Check("光束穿牆:喇叭與 B 之間有牆,B 照樣吃傷害", lineOfSightBlocked && _ticksB.Count > 0,
                $"牆擋視線={lineOfSightBlocked} B 被打 {_ticksB.Count} 次");
            yield return new WaitForSeconds(0.3f);
        }

        private static string Name(Inkling inkling)
        {
            return inkling != null ? inkling.DisplayName : "無";
        }

        // ── (f) 死亡:能量留 50%、還沒照射的喇叭取消 ──
        private IEnumerator DeathCancels()
        {
            _keepAlive = true;
            _aim.From(_player.transform, Chest(_enemyA));
            _gauge.SetPointsForTest(100f);
            _intent.PressSpecial();
            yield return WaitUntil(() => _gauge.IsActive, 1f);
            float t0 = _special.ActivatedAt;
            yield return WaitUntil(() => (Time.time - t0) * 60f >= 40f, 1f);
            KillerWailSpeaker firing = _special.Speaker(0);
            KillerWailSpeaker pending = _special.Speaker(4);
            float atDeath = _gauge.Points;
            _health.Invulnerable = false;
            _health.ApplyDamage(new DamageInfo(300f, _player.transform.position + Vector3.up, Team.Bravo, _enemyA,
                DamageKind.Main));
            yield return null;
            float kept = _gauge.Points;
            yield return new WaitForSeconds(0.3f);
            bool pendingGone = pending == null || pending.Phase == KillerWailSpeaker.State.Idle;
            bool stillFiring = firing != null && firing.Phase == KillerWailSpeaker.State.Firing;
            Check("發動中被擊倒:能量停在死亡瞬間留 50%;還沒照射的喇叭取消、已在照射的繼續",
                Mathf.Abs(kept - atDeath * 0.5f) < 1f && pendingGone && stillFiring && !_gauge.IsActive,
                $"死亡時 {atDeath:0.0} → {kept:0.0};第三組取消={pendingGone} 第一組照射中={stillFiring}");
            var life = _player.GetComponent<InklingLifecycle>();
            life.ForceRespawnNow();
            _health.Invulnerable = true;
            _player.Teleport(new Vector3(-18f, 0.1f, 0f), 90f);
            yield return WaitUntil(() => firing == null || firing.Phase == KillerWailSpeaker.State.Idle, 5f);
            _keepAlive = false;
            yield return new WaitForSeconds(0.3f);
        }

        // ── (e) 沒有目標:沿準心照、之後單向掃 18°/s ──
        private IEnumerator NoTargetSweep()
        {
            // 準心朝後方空地(A、B 都在 80° 之外)
            _aim.From(_player.transform, _player.transform.position + new Vector3(-20f, 2f, 0f));
            yield return new WaitForSeconds(0.2f);
            _gauge.SetPointsForTest(100f);
            _intent.PressSpecial();
            yield return WaitUntil(() => _gauge.IsActive, 1f);
            float t0 = _special.ActivatedAt;
            KillerWailSpeaker s0 = _special.Speaker(0);
            yield return WaitUntil(() => (Time.time - t0) * 60f >= 36f + 30f, 2f);
            float yaw1 = Yaw(s0.Direction);
            float at1 = Time.time;
            yield return new WaitForSeconds(1f);
            float yaw2 = Yaw(s0.Direction);
            float rate = Mathf.Abs(Mathf.DeltaAngle(yaw1, yaw2)) / (Time.time - at1);
            Check("準心範圍內沒有敵人:不鎖人,光束沿準心照、之後單向掃 0.3°/F(18°/s)",
                _special.PairTarget(0) == null && Mathf.Abs(rate - 18f) < 1.5f,
                $"鎖定={Name(_special.PairTarget(0))} 掃速 {rate:0.0}°/s");
            yield return WaitUntil(() => !_gauge.IsActive, 6f);
            yield return new WaitForSeconds(0.2f);
        }

        private static float Yaw(Vector3 direction)
        {
            return Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        }
    }
}

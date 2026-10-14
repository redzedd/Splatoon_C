using System.Collections;
using System.Collections.Generic;
using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // Wave Breaker(D1)驗收,試射場西半部。全部走真實路徑(按 R 發動、按住/放開副武器鍵丟出、裝置/衝擊波):
    //   (a) 發動:舉著裝置、主武器被佔用、放開副武器鍵會丟裝置;舉著不限時(能量不動)
    //   (b) 丟出:行為結束、能量鎖定倒數 390F(死亡不中斷、不減半);著地
    //   (c) 設置感應:半徑 12.3 內的敵人立刻被標記,第 102F 一律結束;範圍外/高出 2.8 以上的不標
    //   (d) 三波在設置後 90 / 240 / 390F 放出;每波 160F 擴到 20(80F 時 10 m);打中貼地的敵人 45 + 標記 480F;
    //       假人也吃;高出 2.8 的人打不到;跳起來躲過(bot 預兆 BotHazards 在波前到達前 0.22 s 叫跳);不跳的下一波照吃
    //   (e) 碰到裝置 30(同一人 0.5 秒一次)+ 擊退
    //   (f) 裝置耐久 480;被打壞 → 能量立刻歸 0;已放出的波照樣擴張
    [AutoTest(Scene = AutoTestAttribute.TrainingScene, Timeout = 150f, Order = 233)]
    public sealed class WaveBreakerAutoTest : AutoTestBase
    {
        private readonly List<Object> _spawned = new List<Object>();
        private readonly List<float> _waveHitsA = new List<float>();
        private readonly List<float> _waveHitsB = new List<float>();
        private readonly List<float> _hitsC = new List<float>();
        private readonly List<float> _contactD = new List<float>();
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
        private ThrowController _thrower;
        private WaveBreakerSpecial _special;
        private WaveBreakerConfig _config;
        private SpecialTestAim _aim;
        private Inkling _a;
        private Inkling _b;
        private Inkling _c;
        private Inkling _d;
        private ScriptedIntent _bIntent;
        private TargetDummy _template;
        private float _bDistance;

        public static void Run()
        {
            Launch<WaveBreakerAutoTest>();
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

        private void Update()
        {
            _maxFrame = Mathf.Max(_maxFrame, Time.deltaTime * 60f);
            if (!_keepAlive)
            {
                return;
            }
            Revive(_a);
            Revive(_b);
            Revive(_c);
            Revive(_d);
        }

        private static void Revive(Inkling inkling)
        {
            var health = inkling != null ? inkling.GetComponent<InklingHealth>() : null;
            if (health != null && health.IsAlive)
            {
                health.ResetForRespawn();
            }
        }

        private void Track(Inkling inkling, List<float> list, float amount)
        {
            inkling.GetComponent<InklingHealth>().Damaged += (dealt, info) =>
            {
                if (info.AttackClass == AttackClass.WaveBreaker && Mathf.Approximately(info.Amount, amount))
                {
                    list.Add(Time.time);
                }
            };
        }

        private IEnumerator Start()
        {
            _playerGo = FindPlayer();
            _player = _playerGo != null ? _playerGo.GetComponent<Inkling>() : null;
            _config = Resources.Load<WaveBreakerConfig>("Weapons/Special_wave_breaker");
            GameObject dummyGo = GameObject.Find("Dummy_Near");
            _template = dummyGo != null ? dummyGo.GetComponent<TargetDummy>() : null;
            if (_player == null || _config == null || _config.DevicePrefab == null || _template == null)
            {
                FailSetup("找不到 Player / Dummy_Near / Resources/Weapons/Special_wave_breaker(先跑 M7 Setup)");
                yield break;
            }
            _intent = ScriptedIntent.Drive(_player);
            _gauge = _player.GetComponent<PlayerSpecialGauge>();
            _health = _player.GetComponent<InklingHealth>();
            _thrower = _player.GetComponent<ThrowController>();
            IAimView originalView = _player.AimView;
            _aim = new SpecialTestAim();
            _player.AimView = _aim;
            if (!SpecialTestKit.EquipSpecial(_player, _config))
            {
                FailSetup("換裝 Wave Breaker 失敗");
                yield break;
            }
            _special = _player.GetComponent<WaveBreakerSpecial>();
            _health.Invulnerable = true;
            _a = SpecialTestKit.CloneEnemy(_playerGo, new Vector3(-22f, 0.1f, 22f), "WaveA", out _);
            _b = SpecialTestKit.CloneEnemy(_playerGo, new Vector3(-20f, 0.1f, 22f), "WaveB", out _bIntent);
            _c = SpecialTestKit.CloneEnemy(_playerGo, new Vector3(-18f, 0.1f, 22f), "WaveC", out _);
            _d = SpecialTestKit.CloneEnemy(_playerGo, new Vector3(-16f, 0.1f, 22f), "WaveD", out _);
            _spawned.Add(_a);
            _spawned.Add(_b);
            _spawned.Add(_c);
            _spawned.Add(_d);
            Track(_a, _waveHitsA, 45f);
            Track(_b, _waveHitsB, 45f);
            Track(_c, _hitsC, 45f);
            Track(_d, _contactD, 30f);

            yield return HoldThrowAndWaves();
            yield return BreakDevice();

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

        private IEnumerator HoldThrowAndWaves()
        {
            Vector3 p = new Vector3(-21f, 0f, -12f);
            _player.Teleport(p + Vector3.up * 0.1f, 90f);
            _aim.From(_player.transform, p + new Vector3(8f, 0f, 0f));
            yield return new WaitForSeconds(0.4f);
            _gauge.SetPointsForTest(100f);
            _intent.PressSpecial();
            yield return WaitUntil(() => _gauge.IsActive, 1f);
            yield return new WaitForSeconds(1f);
            Check("發動:舉著裝置、主武器被佔用、放開副武器鍵會丟裝置;舉著不限時(能量不動)",
                _gauge.IsActive && _special.ModelShown && _special.BlocksMainWeapon && _thrower.WillThrowDevice
                && Mathf.Approximately(_gauge.Points, 100f) && !_gauge.IsLocked,
                $"啟動={_gauge.IsActive} 模型={_special.ModelShown} 會丟裝置={_thrower.WillThrowDevice} 能量={_gauge.Points:0.0}");

            // 按住副武器鍵看落點 → 依預測落點擺好敵人 → 放開
            _intent.Sub = true;
            yield return new WaitForSeconds(0.3f);
            Vector3 land = _thrower.HasPredictedLanding ? _thrower.PredictedLanding : p + new Vector3(8f, 0f, 0f);
            land.y = 0f;
            _a.Teleport(land + new Vector3(0f, 0.1f, 10f));
            _b.Teleport(land + new Vector3(15f, 0.1f, 0f));
            GameObject platform = GameObject.CreatePrimitive(PrimitiveType.Cube);
            platform.name = "WavePlatform";
            platform.transform.position = land + new Vector3(-12f, 1.6f, 0f);
            platform.transform.localScale = new Vector3(3f, 3.2f, 3f);
            _spawned.Add(platform);
            Physics.SyncTransforms();
            _c.Teleport(land + new Vector3(-12f, 3.3f, 0f));
            TargetDummy dummy = Instantiate(_template, land + new Vector3(6f, 0f, 0f), Quaternion.identity);
            _spawned.Add(dummy);
            _keepAlive = true;
            yield return null;
            Vector3 origin = _player.transform.position;
            _intent.Sub = false;
            yield return WaitUntil(() => _special.Device != null, 0.5f);
            float thrownAt = Time.time;
            WaveBreakerDevice device = _special.Device;
            if (device == null)
            {
                Check("放開副武器鍵丟出裝置", false, "沒有裝置");
                yield break;
            }
            Check("丟出:行為結束、模型收起、能量鎖定倒數 390F",
                !_gauge.IsActive && !_special.ModelShown && _gauge.IsLocked
                && Mathf.Abs(_gauge.LockRemainingSeconds - 6.5f) < 0.05f && device.Flying,
                $"行為={_gauge.IsActive} 鎖定={_gauge.IsLocked} 剩 {_gauge.LockRemainingSeconds:0.00}s 飛行中={device.Flying}");

            yield return WaitUntil(() => device.Placed, 2f);
            float placedAt = device.PlacedAt;
            float flight = new Vector2(device.transform.position.x - origin.x, device.transform.position.z - origin.z).magnitude;
            float landError = new Vector2(device.transform.position.x - land.x, device.transform.position.z - land.z).magnitude;
            Check("著地設置(短拋;落點 ≈ 拋物線預覽)", device.Placed && flight > 5f && flight < 13f && landError < 1f,
                $"飛 {flight:0.0} m、離預覽落點 {landError:0.00} m、{(placedAt - thrownAt) * 60f:0}F");

            _bDistance = Flat(_b.transform.position - device.transform.position);

            // 陣亡不中斷、不減半
            yield return new WaitForSeconds(0.25f);
            var life = _player.GetComponent<InklingLifecycle>();
            _health.Invulnerable = false;
            float beforeDeath = _gauge.Points;
            _health.ApplyDamage(new DamageInfo(300f, _player.transform.position + Vector3.up, Team.Bravo, _a,
                DamageKind.Main));
            yield return null;
            float afterDeath = _gauge.Points;
            Check("丟出後被擊倒:鎖定照走、不減半", life.IsDead && _gauge.IsLocked && afterDeath > beforeDeath * 0.95f,
                $"{beforeDeath:0.0} → {afterDeath:0.0}");
            life.ForceRespawnNow();
            _health.Invulnerable = true;
            _player.Teleport(p + new Vector3(0f, 0.1f, -6f), 90f);

            // (c) 設置感應:第 20F 時 A(10 m)被標記、B(15 m)與 C(高 3.2)沒有;第 110F 標記已結束(在下面的迴圈裡看)
            Vector3 devicePos = device.transform.position;
            yield return WaitUntil(() => (Time.time - placedAt) * 60f >= 20f, 1f);
            bool aMarked = Marking.IsMarked(_a, Team.Alpha);
            bool bMarked = Marking.IsMarked(_b, Team.Alpha);
            bool cMarked = Marking.IsMarked(_c, Team.Alpha);
            bool aStill = true;
            bool checkedMarkEnd = false;
            int sensed = -1;

            // (d) 三波:時間、半徑、傷害、標記、跳過 —— 一路看到第三波掃過所有人(設置後 540F)
            var emits = new List<float>();
            int emitBase = WaveBreakerWave.EmitCount - device.WavesEmitted;
            int lastEmitSeen = device.WavesEmitted;
            float radiusAt80 = -1f;
            float bJumpSignalAt = -1f;
            int dodgedBefore = WaveBreakerWave.DodgedByJump;
            bool contactPlaced = false;
            float contactStart = -1f;
            float dStartX = 0f;
            float lockEndedAt = -1f;
            float deadline = Time.time + 11f;
            while (Time.time < deadline && (Time.time - placedAt) * 60f < 540f)
            {
                float frames = (Time.time - placedAt) * 60f;
                if (!checkedMarkEnd && frames >= 110f)
                {
                    checkedMarkEnd = true;
                    aStill = Marking.IsMarked(_a, Team.Alpha);
                    sensed = device.SensedCount;
                }
                while (lastEmitSeen < device.WavesEmitted)
                {
                    lastEmitSeen++;
                    emits.Add(frames);
                }
                WaveBreakerWave wave = device.LastWave;
                if (device.WavesEmitted == 1 && radiusAt80 < 0f && frames >= 90f + 80f && wave != null && wave.Running)
                {
                    radiusAt80 = wave.Radius - 20f / 160f * (frames - 170f);
                }
                // B 只在第一波用 bot 預兆起跳
                if (device.WavesEmitted == 1 && bJumpSignalAt < 0f
                    && BotHazards.ShouldJump(Team.Bravo, _b.transform.position, 0.22f))
                {
                    bJumpSignalAt = frames;
                    _bIntent.PressJump();
                }
                // (e) 第二波放出後把 D 放到裝置旁邊
                if (!contactPlaced && device.WavesEmitted >= 2 && frames >= 250f)
                {
                    contactPlaced = true;
                    contactStart = Time.time;
                    Vector3 side = devicePos + new Vector3(0.95f, 0.1f, 0f);
                    _d.Teleport(side);
                    dStartX = side.x;
                }
                if (lockEndedAt < 0f && !_gauge.IsLocked)
                {
                    lockEndedAt = Time.time;
                }
                yield return null;
            }
            Check("設置感應:12.3 內的敵人立刻被標記、第 102F 一律結束;範圍外與高出 2.8 以上的不標",
                aMarked && !bMarked && !cMarked && !aStill && sensed == 1,
                $"A={aMarked}(110F 時 {aStill}) B={bMarked} C={cMarked} 感應到 {sensed} 人");
            string emitText = string.Join(" / ", emits.ConvertAll(f => f.ToString("0.0")));
            Check("三波在設置後 90 / 240 / 390F 放出", emits.Count == 3 && Mathf.Abs(emits[0] - 90f) <= 0.5f + Slack
                                                  && Mathf.Abs(emits[1] - 240f) <= 0.5f + Slack
                                                  && Mathf.Abs(emits[2] - 390f) <= 0.5f + Slack
                                                  && WaveBreakerWave.EmitCount - emitBase == 3,
                emitText + "F");
            Check("每波 160F 擴到 20 u:放出後 80F 半徑 10 u(0.125 u/F)", Mathf.Abs(radiusAt80 - 10f) < 0.15f,
                $"{radiusAt80:0.00} u");
            // A 在約 10 m:第一波在波前(半徑 − 身體 0.4 − 判定帶 0.35)到達時打到 = 90 + (d − 0.75) / 0.125
            float distA = Flat(_a.transform.position - devicePos);
            float expectA = 90f + (distA - 0.75f) / 0.125f;
            float firstHitA = _waveHitsA.Count > 0 ? (_waveHitsA[0] - placedAt) * 60f : -1f;
            float markLeft = Marking.Remaining(_a, Team.Alpha);
            Check("貼地的敵人每波吃 45(三波三次);第一波在波前到達時打到;被標記 480F",
                _waveHitsA.Count == 3 && Mathf.Abs(firstHitA - expectA) <= 1f + Slack && markLeft > 4f,
                $"A 被打 {_waveHitsA.Count} 次,第一次 {firstHitA:0.0}F(距 {distA:0.00} m 期望 {expectA:0.0}F),標記剩 {markLeft:0.0}s");
            Check("假人(非角色目標)也吃 45", dummy.PopupTotal >= 45f && dummy.PopupTotal % 45f < 0.01f,
                $"假人 {dummy.PopupTotal:0.0}");
            Check("高出裝置 2.8 以上(站在 3.2 m 平台)的敵人打不到", _hitsC.Count == 0, $"C 被打 {_hitsC.Count} 次");
            float bFirstWaveArrive = 90f + (_bDistance - 0.75f) / 0.125f;
            Check("跳起來躲過:bot 預兆在波前到達前 0.22 s 叫跳 → B 第一波沒事;沒跳的後面兩波照吃",
                bJumpSignalAt > 0f && Mathf.Abs(bFirstWaveArrive - bJumpSignalAt - 13.2f) <= 1.5f + Slack
                && _waveHitsB.Count == 2 && (_waveHitsB[0] - placedAt) * 60f > 240f
                && WaveBreakerWave.DodgedByJump > dodgedBefore,
                $"叫跳 {bJumpSignalAt:0.0}F(波前 {bFirstWaveArrive:0.0}F)、B 被打 {_waveHitsB.Count} 次" +
                $"{(_waveHitsB.Count > 0 ? $"(第一次 {(_waveHitsB[0] - placedAt) * 60f:0}F)" : "")}");
            float pushed = Mathf.Abs(_d.transform.position.x - dStartX);
            float contactFrame = _contactD.Count > 0 ? (_contactD[0] - contactStart) * 60f : -1f;
            Check("碰到裝置 30 + 擊退(同一人 0.5 秒一次)", _contactD.Count >= 1 && contactFrame >= 0f && contactFrame < 2f + Slack
                                                  && pushed > 0.25f,
                $"D 接觸傷害 {_contactD.Count} 次(第一次 {contactFrame:0.0}F)、被推開 {pushed:0.00} m");
            float lockFrames = lockEndedAt > 0f ? (lockEndedAt - thrownAt) * 60f : -1f;
            Check("能量鎖定從丟出起 390F 歸 0(期間不能充能)", Mathf.Abs(lockFrames - 390f) <= 1.5f + Slack && _gauge.Points == 0f,
                $"{lockFrames:0.0}F");
            Check("放完第三波後裝置收起", !device.IsDeployed && !device.Flying, $"設置中={device.IsDeployed}");
            _keepAlive = false;
            yield return new WaitForSeconds(0.3f);
        }

        // ── (f) 耐久 480、被打壞 → 能量歸 0、已放出的波照樣擴張 ──
        private IEnumerator BreakDevice()
        {
            Vector3 p = new Vector3(-21f, 0f, 12f);
            _player.Teleport(p + Vector3.up * 0.1f, 90f);
            _aim.From(_player.transform, p + new Vector3(8f, 0f, 0f));
            yield return new WaitForSeconds(0.4f);
            _gauge.SetPointsForTest(100f);
            _intent.PressSpecial();
            yield return WaitUntil(() => _gauge.IsActive, 1f);
            _intent.Sub = true;
            yield return new WaitForSeconds(0.3f);
            _intent.Sub = false;
            yield return WaitUntil(() => _special.Device != null && _special.Device.Placed, 2f);
            WaveBreakerDevice device = _special.Device;
            if (device == null)
            {
                Check("第二次丟出", false, "沒有裝置");
                yield break;
            }
            float hp0 = device.Hp;
            device.ApplyDamage(new DamageInfo(100f, device.transform.position + Vector3.up, Team.Bravo, _a,
                DamageKind.Main, AttackClass.Shooter));
            float hp1 = device.Hp;
            Check("裝置耐久 480(射擊槍 100 → 剩 380)", Mathf.Approximately(hp0, 480f) && Mathf.Approximately(hp1, 380f),
                $"{hp0:0.0} → {hp1:0.0}");
            yield return WaitUntil(() => device.WavesEmitted >= 1, 3f);
            yield return new WaitForSeconds(0.2f);
            WaveBreakerWave wave = device.LastWave;
            bool locked = _gauge.IsLocked;
            device.ApplyDamage(new DamageInfo(1000f, device.transform.position + Vector3.up, Team.Bravo, _a,
                DamageKind.Main, AttackClass.Shooter));
            yield return null;
            yield return null;
            float radius = wave != null ? wave.Radius : -1f;
            yield return new WaitForSeconds(0.3f);
            bool expanding = wave != null && wave.Running && wave.Radius > radius;
            Check("裝置被打壞 → 能量立刻歸 0、解除鎖定;已放出的波照樣擴張",
                locked && !_gauge.IsLocked && _gauge.Points == 0f && !device.IsDeployed && expanding,
                $"打壞前鎖定={locked} 之後鎖定={_gauge.IsLocked} 能量={_gauge.Points:0.0} 波擴張中={expanding}");
            yield return new WaitForSeconds(0.5f);
        }

        private static float Flat(Vector3 v)
        {
            return new Vector2(v.x, v.z).magnitude;
        }
    }
}

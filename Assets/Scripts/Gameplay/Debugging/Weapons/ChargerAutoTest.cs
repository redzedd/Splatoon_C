using System.Collections;
using System.Collections.Generic;
using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.CameraRig;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 狙擊槍驗收的共用流程(每把狙擊槍一個 [AutoTest] 子類,各自在全新 play session 跑)。
    // 全部走真實遊戲路徑(意圖 → ChargerWeapon → InkBullet → 假人 / 角色 / 地面 / 墨槽),期望值取自 S3ChargerParams:
    //   換裝外觀(狙擊槍掛右手、內建槍身隱藏、墨槽隊伍色、長槍持槍姿勢)/ bot 掛勾
    //   滿蓄:蓄力幀、蓄力比例、蓄力中不回墨、雷射(隊伍色、長度 = 射程、兩隊可見)、蓄力準星、蓄力計、槍對準準星;
    //     滿蓄走速;開槍:耗墨、射程(含槍口 25.75 / 30.76)、飛行幀、墨量鎖 20F、沿線墨線寬、終點圓
    //   點一下:8F 出手、40 傷、最小射程 10.75、2.25%;互擊:開槍後馬上被擊倒,飛行中的那一發照樣塗墨線;
    //     部分蓄力傷害 = S3 曲線、停在目標上;滿蓄一擊
    //   貫穿:滿蓄打穿一直線的兩個敵人、部分蓄力停在第一個
    //   起跳:滿蓄起跳高度 × 0.405、空中蓄力 1/3;墨不足:1/3 速率蓄滿後照樣打出、連最小都不夠不出手
    //   儲存蓄力:潛墨保留(白圈、bot 看得到、雷射收起)、浮出 25F 雷射 / 31F 開槍、75F 過期、部分蓄力 / 潛伏中放開就沒了
    //   開槍後 15F 不能潛墨;雷射停在牆上、子彈撞牆墨線塗到牆腳;對物件倍率(部分 / 滿蓄兩個攻擊類別)
    //   bot 式:滿蓄 + 雷射掃到敵人才放、貼近部分蓄力就放、塗地蓄滿打遠處、腳邊點一下;蓄力拉近 FOV;潛墨隱藏
    // 場地:試射場 x ≤ −4 的空地(西南 z < 0、西北 z > 0 各分幾條射擊道,互不干擾)。
    public abstract class ChargerAutoTest : AutoTestBase
    {
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
            public IDamageable Target;
            public float Amount;
            public bool Splatted;
        }

        private readonly List<HitRecord> _hits = new List<HitRecord>();
        private readonly List<InkBulletImpact> _impacts = new List<InkBulletImpact>();
        private readonly List<GameObject> _spawned = new List<GameObject>();
        private int _errors;
        private string _firstError;

        private GameObject _player;
        private Inkling _inkling;
        private WeaponLoadout _loadout;
        private PlayerInkTank _tank;
        private Transform _visual;
        private CharacterController _controller;
        private SquidController _squid;
        private CharacterVisual _characterVisual;
        private ScriptedIntent _intent;
        private TestAim _aim;
        private IAimView _originalView;
        private GameObject _dummyTemplate;
        private PaintableSurface _ground;
        private ChargerConfig _config;
        private WeaponKit _baseKit;
        private WeaponKit _chargerKit;
        private float _maxDeltaTime;
        private float _aimHeight = 1.3f;
        private Vector3 _aimDirection = Vector3.forward;
        private bool _aimAtPoint;
        private Vector3 _aimPoint;
        private Inkling _enemyA;
        private Inkling _enemyB;
        private Inkling _enemyC;

        // Resources/Weapons/Main_<id>
        protected abstract string ConfigId { get; }

        // S3 原始參數(期望值來源)
        protected abstract S3ChargerParams S3 { get; }

        private ChargerWeapon Charger => _loadout != null ? _loadout.Main as ChargerWeapon : null;

        private float Tolerance => Mathf.Max(0.02f, _maxDeltaTime * 1.5f);

        private void OnEnable()
        {
            InkBullet.Impacted += OnImpact;
            Application.logMessageReceived += OnLog;
        }

        private void OnDisable()
        {
            InkBullet.Impacted -= OnImpact;
            Application.logMessageReceived -= OnLog;
            if (_inkling != null)
            {
                _inkling.DamageDealt -= OnDamageDealt;
            }
        }

        private void OnImpact(in InkBulletImpact impact)
        {
            _impacts.Add(impact);
        }

        private void OnDamageDealt(IDamageable target, float amount, bool splatted)
        {
            _hits.Add(new HitRecord { Target = target, Amount = amount, Splatted = splatted });
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

        // 瞄準每幀重設(移動中視點跟著角色走)
        private void LateUpdate()
        {
            if (_aim == null || _player == null)
            {
                return;
            }
            Vector3 origin = _player.transform.position + Vector3.up * _aimHeight;
            _aim.Origin = origin;
            if (_aimAtPoint)
            {
                Vector3 delta = _aimPoint - origin;
                if (delta.sqrMagnitude > 1e-6f)
                {
                    _aim.Forward = delta.normalized;
                }
            }
            else
            {
                _aim.Forward = _aimDirection.normalized;
            }
            Vector3 flat = new Vector3(_aim.Forward.x, 0f, _aim.Forward.z);
            if (flat.sqrMagnitude > 1e-6f && _visual != null)
            {
                _visual.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
            }
        }

        protected IEnumerator Start()
        {
            _player = FindPlayer();
            _inkling = _player != null ? _player.GetComponent<Inkling>() : null;
            _loadout = _player != null ? _player.GetComponent<WeaponLoadout>() : null;
            _tank = _player != null ? _player.GetComponent<PlayerInkTank>() : null;
            _visual = _player != null ? _player.transform.Find("Visual") : null;
            _controller = _player != null ? _player.GetComponent<CharacterController>() : null;
            _squid = _player != null ? _player.GetComponent<SquidController>() : null;
            _characterVisual = _player != null ? _player.GetComponent<CharacterVisual>() : null;
            _dummyTemplate = GameObject.Find("Dummy_Near");
            _config = Resources.Load<ChargerConfig>("Weapons/Main_" + ConfigId);
            foreach (PaintableSurface surface in PaintableSurface.Active)
            {
                if (surface.name == "Ground")
                {
                    _ground = surface;
                }
            }
            if (_inkling == null || _loadout == null || _tank == null || _visual == null || _controller == null
                || _squid == null || _dummyTemplate == null || _ground == null)
            {
                FailSetup("找不到 Player(Inkling/WeaponLoadout/PlayerInkTank/Visual/SquidController)、Dummy_Near 或 Ground");
                yield break;
            }
            if (_config == null || _config.ModelPrefab == null || _config.BulletPrefab == null || _config.LaserMaterial == null)
            {
                FailSetup($"找不到 Resources/Weapons/Main_{ConfigId} 或它的模型 / 子彈 / 雷射材質(先跑 Tools/SplatoonC/M7 Setup)");
                yield break;
            }
            _baseKit = _loadout.Kit;
            if (_baseKit == null || !_baseKit.IsValid)
            {
                FailSetup("Player 沒有預設武器組合(先跑 M7 Setup)");
                yield break;
            }
            // 敵方靶(複製本機玩家,Bravo、站著不動):在換上狙擊槍之前複製,不帶狙擊槍的模型 / 雷射
            _enemyA = SpecialTestKit.CloneEnemy(_player, new Vector3(-23f, 0.1f, 23f), "ChargerEnemyA", out _);
            _enemyB = SpecialTestKit.CloneEnemy(_player, new Vector3(-21f, 0.1f, 23f), "ChargerEnemyB", out _);
            _enemyC = SpecialTestKit.CloneEnemy(_player, new Vector3(-19f, 0.1f, 23f), "ChargerEnemyC", out _);
            _spawned.Add(_enemyA.gameObject);
            _spawned.Add(_enemyB.gameObject);
            _spawned.Add(_enemyC.gameObject);

            _inkling.DamageDealt += OnDamageDealt;
            _intent = ScriptedIntent.Drive(_player.transform);
            _originalView = _inkling.AimView;
            _aim = new TestAim();
            _inkling.AimView = _aim;

            yield return EquipAndInspect();
            if (Charger == null || Charger.Config != _config)
            {
                Cleanup();
                Finish();
                yield break;
            }
            CheckBotHooks();
            yield return MeasureFullCharge();
            yield return MeasureTapShot();
            yield return MeasureTradeTrail();
            yield return MeasureDummyDamage();
            yield return MeasurePierce();
            yield return MeasureJumpAndAirCharge();
            yield return MeasureLowInk();
            yield return MeasureKeepCharge();
            yield return MeasureNoSquidAfterShot();
            yield return MeasureLaserWall();
            yield return MeasureDeviceRates();
            yield return MeasureBotStyle();
            yield return MeasureZoomAndSquid();

            Check($"{S3.Name} 全程無例外 / 錯誤 log", _errors == 0,
                _errors == 0 ? "0" : $"{_errors} 筆,第一筆:{_firstError}");
            Debug.Log($"[AUTOTEST] INFO {S3.Name} 量測期間最大幀時間 {_maxDeltaTime * 1000f:0.0} ms");
            Cleanup();
            Finish();
        }

        // ── 換裝、外觀 ──

        private IEnumerator EquipAndInspect()
        {
            WeightClass weight = _config.HeavyWeight ? WeightClass.Heavy : WeightClass.Mid;
            _chargerKit = ScriptableObject.CreateInstance<WeaponKit>();
            _chargerKit.Setup(ConfigId, S3.Name, S3.Name, "AutoTest 執行期組合", _config, _baseKit.Sub, _baseKit.Special,
                200f, weight, "射程", 90, "威力", 90, "射速", 20);
            yield return MoveTo(new Vector3(-12f, 0.1f, -20f));
            _loadout.Equip(_chargerKit);
            yield return Frames(3);

            ChargerWeapon charger = Charger;
            Check($"{S3.Name} 換裝:主武器換成 ChargerWeapon", charger != null && charger.Config == _config,
                $"主武器={(_loadout.Main != null ? _loadout.Main.GetType().Name : "無")}");
            if (charger == null)
            {
                yield break;
            }
            ChargerModel model = charger.Model;
            Renderer gun = BuiltInGun();
            bool onSocket = model != null && _characterVisual != null && model.transform.parent == _characterVisual.WeaponSocket;
            Check($"{S3.Name} 外觀:程序化狙擊槍掛在右手 WeaponSocket、內建槍身隱藏",
                model != null && model.SourcePrefab == _config.ModelPrefab && onSocket && gun != null
                && gun.forceRenderingOff && model.Visible,
                $"模型={(model != null ? model.name : "無")} 掛點={(model != null ? model.transform.parent.name : "-")} " +
                $"內建槍身隱藏={(gun != null && gun.forceRenderingOff)}");

            Color team = TeamPalette.ColorOf(_inkling.Team);
            bool tinted = false;
            int inkParts = 0;
            if (model != null)
            {
                foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
                {
                    if (!r.name.StartsWith("Ink", System.StringComparison.Ordinal))
                    {
                        continue;
                    }
                    inkParts++;
                    if (CharacterTint.TryReadBaseColor(r, 0, out Color c) && ColorClose(c, team))
                    {
                        tinted = true;
                    }
                }
            }
            Check($"{S3.Name} 外觀:墨槽是隊伍色", tinted && inkParts >= 1, $"墨水部位 {inkParts} 個,隊伍色={tinted}");
            Check($"{S3.Name} 角色姿勢:長槍持槍(ILongGunPose)接手",
                _characterVisual != null && _characterVisual.LongGunStanceActive, $"LongGunStanceActive={_characterVisual?.LongGunStanceActive}");
            Check($"{S3.Name} 雷射與蓄力準星:已建立、沒蓄力時不顯示",
                charger.Laser != null && !charger.Laser.LaserVisible && charger.Reticle != null && !charger.Reticle.Shown,
                $"雷射={(charger.Laser != null ? charger.Laser.LaserVisible.ToString() : "無")} 準星={(charger.Reticle != null ? charger.Reticle.Shown.ToString() : "無")}");
            Check($"{S3.Name} 重量級別 {weight}(E-liter 重)", _loadout.Kit.Weight == weight && _config.HeavyWeight == (weight == WeightClass.Heavy),
                $"跑速倍率 {WeightClassUtil.RunMultiplier(weight):0.000}");
        }

        // ── bot 掛勾(BotBrain 交戰 / 塗地時呼叫的同一組方法;純查詢)──

        private void CheckBotHooks()
        {
            ChargerWeapon charger = Charger;
            float reach = S3.ReachFromPlayer(S3.FullChargeFrames);
            var visible = new BotCombatContext
            {
                Distance = reach * 0.8f, TargetVisible = true, Reacted = true, AimError = 1f, FireTolerance = 5f,
            };
            var hidden = visible;
            hidden.TargetVisible = false;
            var slow = visible;
            slow.Reacted = false;
            Check($"{S3.Name} bot:交戰距離 = 滿蓄射程 {reach:0.00} m(含槍口)、想維持 0.8 倍、塗地瞄遠處、偏好高處",
                Mathf.Abs(charger.EffectiveRange - reach) < 0.01f && Mathf.Abs(charger.BotPreferredDistance - reach * 0.8f) < 0.01f
                && charger.BotEngageRange > reach && charger.BotPaintAimDistance >= 12f && charger.BotHighGroundBias > 0f,
                $"EffectiveRange={charger.EffectiveRange:0.00} 交戰={charger.BotPreferredDistance:0.0} 進場={charger.BotEngageRange:0.0} " +
                $"塗地瞄 {charger.BotPaintAimDistance:0.0} m 高處偏好 {charger.BotHighGroundBias:0.0}");
            Check($"{S3.Name} bot:看到敵人(反應過)就開始蓄;看不到 / 還沒反應且沒在蓄就不按;塗地沒在蓄就起手",
                charger.BotAttackIntent(visible) && !charger.BotAttackIntent(hidden) && !charger.BotAttackIntent(slow)
                && charger.BotPaintIntent(1f) && !charger.BotPaintIntent(0.05f) && charger.BotFeetPaintIntent(1f)
                && !charger.RevealsSubmerged,
                $"交戰={charger.BotAttackIntent(visible)} 看不到={charger.BotAttackIntent(hidden)} 塗地(滿墨)={charger.BotPaintIntent(1f)}");
        }

        // ── 滿蓄:蓄力幀、比例、墨量鎖、雷射、準星、姿勢、走速;開槍:耗墨、射程、飛行幀、墨量鎖、墨線 ──

        private IEnumerator MeasureFullCharge()
        {
            ChargerWeapon charger = Charger;
            _tank.InfiniteInk = false;
            _tank.RefillFull();
            _tank.Drain(0.1f);
            yield return MoveTo(new Vector3(-20f, 0.1f, -23f));
            AimFlat(Vector3.forward, 1.3f);
            yield return Frames(3);
            _intent.Attack = true;
            float deadline = Time.time + S3.FullChargeSeconds * 2f + 1f;
            float midCharge = -1f;
            float midTarget = -1f;
            // 蓄力開始的下一幀記下墨量(按下那一幀 PlayerInkTank 可能比武器先跑、多回一幀的墨)
            float levelBefore = -1f;
            while (!charger.IsFullyCharged && Time.time < deadline)
            {
                yield return null;
                TrackDelta();
                if (levelBefore < 0f && charger.IsCharging && Time.time > charger.ChargeStartTime)
                {
                    levelBefore = _tank.Normalized;
                }
                if (midCharge < 0f && charger.IsCharging && Time.time - charger.ChargeStartTime >= S3.FullChargeSeconds * 0.5f)
                {
                    midCharge = charger.Charge01;
                    midTarget = (Time.time - charger.ChargeStartTime) / S3.FullChargeSeconds;
                }
            }
            float chargeTime = charger.FullChargeTime - charger.ChargeStartTime;
            Check($"{S3.Name} 滿蓄 {S3.FullChargeFrames}F = {S3.FullChargeSeconds:0.000} s",
                charger.IsFullyCharged && Mathf.Abs(chargeTime - S3.FullChargeSeconds) <= Tolerance,
                $"實測 {chargeTime:0.000} s(容許 ±{Tolerance:0.000})");
            Check($"{S3.Name} 蓄力比例隨時間線性(一半時間 ≈ 0.5)", midCharge > 0f && Mathf.Abs(midCharge - midTarget) < 0.05f,
                $"{midTarget:0.000} 時 Charge01 = {midCharge:0.000}");
            Check($"{S3.Name} 蓄力中不回墨(墨量鎖)", levelBefore > 0f && _tank.Normalized <= levelBefore + 1e-4f,
                $"蓄力前 {levelBefore:0.0000} → 滿蓄 {_tank.Normalized:0.0000}");
            yield return Frames(3);

            // 雷射:兩隊都看得到的世界物件、隊伍色、長度 = 滿蓄射程(從角色量,含槍口)
            ChargerLaser laser = charger.Laser;
            Vector3 feet = Flat(_player.transform.position);
            float laserReach = laser != null ? Vector3.Dot(Flat(laser.LaserTo) - feet, Vector3.forward) : -1f;
            float reach = S3.ReachFromPlayer(S3.FullChargeFrames);
            Color team = TeamPalette.ColorOf(_inkling.Team);
            Color expectedLine = Color.Lerp(team, Color.white, 0.35f);
            bool worldObject = laser != null && laser.transform.root == _player.transform && laser.Line.useWorldSpace;
            Check($"{S3.Name} 雷射:滿蓄時顯示、世界空間(兩隊可見)、長度 = 射程 {reach:0.00} m、隊伍色最亮",
                laser != null && laser.LaserVisible && worldObject && Mathf.Abs(laserReach - reach) < 0.2f
                && ColorClose(laser.LaserColor, expectedLine) && laser.LaserColor.a > 0.9f,
                $"雷射終點離角色 {laserReach:0.00} m、顏色 {laser?.LaserColor}(期望 {expectedLine})");
            bool fromTip = laser != null && charger.Model != null
                           && Vector3.Distance(laser.LaserFrom, charger.Model.MuzzleTip.position) < 0.01f;
            Check($"{S3.Name} 雷射從槍口出發", fromTip,
                $"起點 {laser?.LaserFrom} / 槍口 {charger.Model?.MuzzleTip.position}");
            ChargerReticle reticle = charger.Reticle;
            Check($"{S3.Name} 蓄力準星:滿蓄整圈填滿", reticle != null && reticle.Shown && reticle.Fill > 0.99f,
                $"顯示={reticle?.Shown} 填滿 {reticle?.Fill:0.00}");
            ChargerModel model = charger.Model;
            float angle = model != null ? Vector3.Angle(model.transform.forward, _aim.Forward) : 999f;
            float tipHeight = model != null ? model.MuzzleTip.position.y - _player.transform.position.y : 0f;
            Check($"{S3.Name} 蓄力姿勢:槍管對準準星、槍口在胸口高度、蓄力計填滿",
                model != null && angle < 16f && tipHeight > 0.8f && tipHeight < 1.8f && model.GaugeFill > 0.99f,
                $"槍管與視線夾角 {angle:0.0}°、槍口高 {tipHeight:0.00} m、蓄力計 {model?.GaugeFill:0.00}");

            // 滿蓄走速
            _intent.Move = new Vector2(0f, 1f);
            yield return new WaitForSeconds(0.35f);
            Vector3 p0 = _player.transform.position;
            float t0 = Time.time;
            yield return new WaitForSeconds(0.4f);
            float speed = Vector3.Distance(Flat(_player.transform.position), Flat(p0)) / Mathf.Max(1e-3f, Time.time - t0);
            _intent.Move = Vector2.zero;
            float expectedSpeed = S3.MoveSpeed(1f) * 60f;
            Check($"{S3.Name} 滿蓄走速 {S3.MoveSpeedFullCharge} u/F = {expectedSpeed:0.00} m/s",
                Mathf.Abs(speed - expectedSpeed) < 0.15f, $"實測 {speed:0.00} m/s");
            yield return new WaitForSeconds(0.25f);

            // 開槍
            bool heard = false;
            System.Action<string> listen = id => heard |= id.StartsWith("charger_", System.StringComparison.Ordinal)
                                                          || id.StartsWith("shoot_shooter", System.StringComparison.Ordinal);
            GameAudio.Played += listen;
            int shots = charger.ShotsFired;
            float beforeShot = _tank.Normalized;
            _intent.Attack = false;
            yield return WaitUntil(() => charger.ShotsFired > shots, 1f);
            GameAudio.Played -= listen;
            float spent = beforeShot - _tank.Normalized;
            Check($"{S3.Name} 滿蓄一發:{S3.DamageFull} 傷、耗墨 {S3.InkConsumeFull * 100f:0.##}%",
                charger.ShotsFired == shots + 1 && charger.LastShotFull && Mathf.Approximately(charger.LastShotDamage, S3.DamageFull)
                && Mathf.Abs(spent - S3.InkConsumeFull) < 1e-3f,
                $"傷害 {charger.LastShotDamage}、耗墨 {spent * 100f:0.00}%");
            Check($"{S3.Name} 開槍音效(charger_*;未合成前退回 shoot_shooter)", heard, GameAudio.LastPlayedId ?? "-");
            float shotTime = charger.LastShotTime;
            float level = _tank.Normalized;
            float unlocked = -1f;
            InkBulletImpact end = default;
            bool ended = false;
            deadline = Time.time + 1.5f;
            while (Time.time < deadline && (unlocked < 0f || !ended))
            {
                yield return null;
                TrackDelta();
                if (unlocked < 0f && _tank.Normalized > level + 1e-5f)
                {
                    unlocked = Time.time;
                }
                if (!ended)
                {
                    ended = TryFinalImpact(charger.LastShotAttackId, out end);
                }
            }
            float along = ended ? Vector3.Dot(Flat(end.Point) - Flat(charger.LastShotFeet), Vector3.forward) : -1f;
            int expectedFrames = Mathf.CeilToInt(charger.LastShotMaxDistance / S3.SpeedFull - 1e-4f);
            Check($"{S3.Name} 滿蓄射程 {reach:0.00} m(含槍口)、彈速 {S3.SpeedFull} u/F",
                ended && end.End == InkBulletEnd.MaxDistance && Mathf.Abs(along - reach) < 0.12f && end.Frame == expectedFrames,
                $"終點 {end.End} 離角色 {along:0.00} m、飛行 {end.Frame}F(期望 {expectedFrames}F = {charger.LastShotMaxDistance:0.00} / {S3.SpeedFull})");
            float lockTolerance = Mathf.Max(0.034f, _maxDeltaTime * 2f);
            Check($"{S3.Name} 開槍後墨量鎖 {S3.InkRecoverStop}F",
                unlocked > 0f && Mathf.Abs(unlocked - shotTime - S3.InkRecoverStop / 60f) <= lockTolerance,
                $"開槍後 {unlocked - shotTime:0.000} s 開始回墨(容許 ±{lockTolerance:0.000})");
            yield return new WaitForSeconds(0.2f);
            CheckTrail(charger, S3.FullChargeFrames, "滿蓄");
            _tank.RefillFull();
        }

        // 墨線:沿彈道正下方連續(≥ 90%)、寬 ≈ 2 × 半寬、終點圓半徑 ≈ 終點塗墨半徑
        private void CheckTrail(ChargerWeapon charger, float frames, string label)
        {
            Vector3 origin = charger.LastShotOrigin;
            Vector3 dir = charger.LastShotDirection;
            Vector3 flatDir = Flat(dir).normalized;
            Vector3 perp = Vector3.Cross(Vector3.up, flatDir);
            float maxDistance = charger.LastShotMaxDistance;
            float w = S3.TrailWidthHalf(frames);
            int samples = 0;
            int own = 0;
            for (float d = S3.TrailNearestSkip + w; d <= maxDistance - 0.5f; d += 0.5f)
            {
                samples++;
                if (Own(origin + dir * d))
                {
                    own++;
                }
            }
            float coverage = samples > 0 ? own / (float)samples : 0f;
            Vector3 mid = origin + dir * (maxDistance * 0.5f);
            float width = OwnWidth(mid, perp, 5f);
            Check($"{S3.Name} {label}墨線:彈道正下方連續、寬 ≈ 2 × {w:0.00} m",
                coverage >= 0.9f && width >= 1.55f * w && width <= 2.3f * w,
                $"覆蓋 {coverage:P0}({own}/{samples})、中段寬 {width:0.00} m、墨滴 {charger.LastTrailDrops} 顆");
            float r = S3.EndPaintRadius(frames);
            Vector3 endPoint = origin + dir * maxDistance;
            bool inside = Own(endPoint + perp * (r * 0.8f)) && Own(endPoint - perp * (r * 0.8f));
            bool outside = !Own(endPoint + perp * (r * 1.3f + 0.1f)) && !Own(endPoint - perp * (r * 1.3f + 0.1f));
            Check($"{S3.Name} {label}終點圓半徑 {r:0.000} m(射程盡頭往下落)", inside && outside,
                $"0.8r 內={inside}、1.3r 外乾淨={outside}");
        }

        // ── 點一下:8F 出手、最小蓄力 ──

        private IEnumerator MeasureTapShot()
        {
            ChargerWeapon charger = Charger;
            _tank.InfiniteInk = false;
            _tank.RefillFull();
            yield return MoveTo(new Vector3(-13f, 0.1f, -23f));
            AimFlat(Vector3.forward, 1.3f);
            yield return Frames(3);
            int shots = charger.ShotsFired;
            float before = _tank.Normalized;
            yield return Tap();
            yield return WaitUntil(() => charger.ShotsFired > shots, 1f);
            float spent = before - _tank.Normalized;
            float delay = charger.LastShotTime - charger.ChargeStartTime;
            Check($"{S3.Name} 點一下:蓄到 {S3.MinChargeFrames}F 那一刻出手(提早放開緩衝)",
                charger.ShotsFired == shots + 1 && Mathf.Abs(delay - S3.MinChargeSeconds) <= Tolerance
                && Mathf.Abs(charger.LastShotChargeFrames - S3.MinChargeFrames) < 0.6f,
                $"按下後 {delay:0.000} s 出手(期望 {S3.MinChargeSeconds:0.000})、蓄 {charger.LastShotChargeFrames:0.0}F");
            Check($"{S3.Name} 最小蓄力:{S3.DamageMin} 傷、耗墨 {S3.InkConsumeMin * 100f:0.##}%、不貫穿",
                Mathf.Abs(charger.LastShotDamage - S3.Damage(charger.LastShotChargeFrames)) < 0.05f
                && charger.LastShotDamage <= S3.DamageMin + 0.5f && !charger.LastShotFull
                && Mathf.Abs(spent - S3.InkConsume(charger.LastShotChargeFrames)) < 1e-3f,
                $"傷害 {charger.LastShotDamage}、耗墨 {spent * 100f:0.00}%");
            InkBulletImpact end = default;
            yield return WaitUntil(() => TryFinalImpact(charger.LastShotAttackId, out end), 1f);
            float along = Vector3.Dot(Flat(end.Point) - Flat(charger.LastShotFeet), Vector3.forward);
            float reach = S3.ReachFromPlayer(charger.LastShotChargeFrames);
            float speed = S3.Speed(charger.LastShotChargeFrames);
            int frames = Mathf.CeilToInt(charger.LastShotMaxDistance / speed - 1e-4f);
            Check($"{S3.Name} 最小射程 {S3.ReachFromPlayer(S3.MinChargeFrames):0.00} m(含槍口)、彈速 {S3.SpeedMin} u/F",
                end.End == InkBulletEnd.MaxDistance && Mathf.Abs(along - reach) < 0.12f && end.Frame == frames
                && Mathf.Abs(charger.LastShotSpeed - speed) < 1e-3f,
                $"終點離角色 {along:0.00} m(期望 {reach:0.00})、飛行 {end.Frame}F(期望 {frames})");
            yield return new WaitForSeconds(0.2f);
            CheckTrail(charger, charger.LastShotChargeFrames, "最小蓄力");
        }

        // ── 互擊:開槍後子彈還在飛就被擊倒(InklingLifecycle 停用主武器元件)→ 這一發照樣飛完、墨線照樣塗上 ──
        // 射擊道 x = −16(z −21 往 +z,半蓄力 ≈ 17 / 20 m):先確認這條道上原本沒有自家墨(其他量測不用這條道)
        private IEnumerator MeasureTradeTrail()
        {
            ChargerWeapon charger = Charger;
            _tank.InfiniteInk = true;
            yield return MoveTo(new Vector3(-16f, 0.1f, -21f));
            AimFlat(Vector3.forward, 1.3f);
            yield return Frames(3);
            Vector3 feet = Flat(_player.transform.position);
            int dirtyBefore = 0;
            for (float d = 2f; d <= 16f; d += 0.5f)
            {
                if (Own(feet + Vector3.forward * d))
                {
                    dirtyBefore++;
                }
            }
            int shots = charger.ShotsFired;
            _intent.Attack = true;
            yield return WaitUntil(() => charger.ChargeFrames >= S3.FullChargeFrames * 0.5f, S3.FullChargeSeconds + 1f);
            _intent.Attack = false;
            yield return WaitUntil(() => charger.ShotsFired > shots, 1f);
            // 開槍的同一幀(子彈剛出膛)停用主武器 = 擊倒;之後子彈結束時,這把狙擊槍的元件是停用狀態
            bool inFlight = charger.ShotsFired == shots + 1 && !TryFinalImpact(charger.LastShotAttackId, out _);
            charger.enabled = false;
            yield return WaitUntil(() => TryFinalImpact(charger.LastShotAttackId, out _), 1f);
            yield return new WaitForSeconds(0.2f);
            bool ended = TryFinalImpact(charger.LastShotAttackId, out InkBulletImpact end);
            bool disabledAtEnd = !charger.enabled;
            float frames = charger.LastShotChargeFrames;
            float w = S3.TrailWidthHalf(frames);
            int samples = 0;
            int own = 0;
            for (float d = S3.TrailNearestSkip + w; d <= charger.LastShotMaxDistance - 0.5f; d += 0.5f)
            {
                samples++;
                if (Own(charger.LastShotOrigin + charger.LastShotDirection * d))
                {
                    own++;
                }
            }
            float coverage = samples > 0 ? own / (float)samples : 0f;
            charger.enabled = true;
            yield return Frames(2);
            Check($"{S3.Name} 互擊:開槍後子彈還在飛就被擊倒(主武器停用)→ 子彈飛完、沿線墨線照樣塗上",
                dirtyBefore == 0 && inFlight && ended && end.End == InkBulletEnd.MaxDistance && disabledAtEnd
                && coverage >= 0.9f && charger.LastTrailDrops > 0,
                $"射擊道原本自家墨 {dirtyBefore} 點、出膛時仍在飛={inFlight}、終點 {end.End}、結束時武器停用={disabledAtEnd}、" +
                $"墨線覆蓋 {coverage:P0}({own}/{samples})、墨滴 {charger.LastTrailDrops} 顆");
        }

        // ── 部分蓄力打假人(= S3 曲線、停在假人身上、打不死)、滿蓄打假人一擊 ──

        private IEnumerator MeasureDummyDamage()
        {
            ChargerWeapon charger = Charger;
            _tank.InfiniteInk = true;
            yield return MoveTo(new Vector3(-7f, 0.1f, -23f));
            AimFlat(Vector3.forward, 1.3f);
            yield return Frames(2);
            TargetDummy dummy = SpawnDummy(Flat(_player.transform.position) + Vector3.forward * 9f);
            yield return Frames(2);
            int hitsBefore = _hits.Count;
            yield return ChargeTo(30f);
            yield return new WaitForSeconds(0.4f);
            float dealt = SumHits(dummy, hitsBefore, out bool killed);
            float expected = S3.Damage(charger.LastShotChargeFrames);
            bool stopped = TryFinalImpact(charger.LastShotAttackId, out InkBulletImpact end) && end.End == InkBulletEnd.Target
                           && ReferenceEquals(end.Target, dummy);
            Check($"{S3.Name} 部分蓄力傷害 = S3 曲線(40 → 80;蓄 {charger.LastShotChargeFrames:0.0}F → {expected})、打不死、子彈停在假人身上",
                Mathf.Abs(dealt - expected) < 0.05f && expected > S3.DamageMin && expected < S3.DamageMax && !killed && stopped,
                $"假人吃 {dealt:0.0}(期望 {expected:0.0})擊倒={killed}、終點={end.End}");
            bool paintedAtDummy = Own(DummyCenter(dummy) + Vector3.back * 0.6f);
            Check($"{S3.Name} 停在假人身上的一發:終點的墨落在假人腳下", paintedAtDummy, $"假人腳下有墨={paintedAtDummy}");
            Despawn(dummy);
            yield return new WaitForSeconds(0.3f);

            dummy = SpawnDummy(Flat(_player.transform.position) + Vector3.forward * 18f);
            yield return Frames(2);
            hitsBefore = _hits.Count;
            yield return ChargeTo(S3.FullChargeFrames);
            yield return new WaitForSeconds(0.4f);
            dealt = SumHits(dummy, hitsBefore, out killed);
            Check($"{S3.Name} 滿蓄 18 m 一擊({S3.DamageFull})", killed && dealt >= 100f,
                $"假人吃 {dealt:0.0} 擊倒={killed}");
            Despawn(dummy);
            yield return new WaitForSeconds(0.2f);
        }

        // ── 貫穿:滿蓄打穿一直線的兩個敵人;部分蓄力停在第一個 ──

        private IEnumerator MeasurePierce()
        {
            ChargerWeapon charger = Charger;
            _tank.InfiniteInk = true;
            yield return MoveTo(new Vector3(-21f, 0.1f, 1f));
            AimFlat(Vector3.forward, 1.3f);
            Vector3 feet = Flat(_player.transform.position);
            Teleport(_enemyA.transform, feet + Vector3.forward * 11f + Vector3.up * 0.1f);
            Teleport(_enemyB.transform, feet + Vector3.forward * 15f + Vector3.up * 0.1f);
            var healthA = _enemyA.GetComponent<InklingHealth>();
            var healthB = _enemyB.GetComponent<InklingHealth>();
            healthA.ResetForRespawn();
            healthB.ResetForRespawn();
            var hitsA = new List<DamageInfo>();
            var hitsB = new List<DamageInfo>();
            System.Action<float, DamageInfo> onA = (d, info) => hitsA.Add(info);
            System.Action<float, DamageInfo> onB = (d, info) => hitsB.Add(info);
            healthA.Damaged += onA;
            healthB.Damaged += onB;
            yield return new WaitForSeconds(0.3f);
            // 視線對準兩人連線上的胸口高度
            AimAt(_enemyA.transform.position + Vector3.up * 1.0f + Vector3.forward * 2f, 1.3f);
            yield return Frames(3);

            yield return ChargeTo(36f);
            yield return new WaitForSeconds(0.4f);
            bool partialOk = hitsA.Count == 1 && hitsB.Count == 0 && hitsA[0].AttackClass == _config.PartialAttackClass
                             && Mathf.Abs(hitsA[0].Amount - S3.Damage(charger.LastShotChargeFrames)) < 0.05f;
            Check($"{S3.Name} 部分蓄力不貫穿:只打到前面的敵人({_config.PartialAttackClass})", partialOk,
                $"A 吃 {Describe(hitsA)}、B 吃 {Describe(hitsB)}");
            hitsA.Clear();
            hitsB.Clear();
            healthA.ResetForRespawn();
            yield return new WaitForSeconds(0.2f);

            int pierceBefore = CountPierce();
            yield return ChargeTo(S3.FullChargeFrames);
            yield return new WaitForSeconds(0.5f);
            bool fullA = hitsA.Count == 1 && Mathf.Approximately(hitsA[0].Amount, S3.DamageFull)
                         && hitsA[0].AttackClass == _config.FullAttackClass;
            bool fullB = hitsB.Count == 1 && Mathf.Approximately(hitsB[0].Amount, S3.DamageFull);
            bool bothDown = !healthA.IsAlive && !healthB.IsAlive;
            Check($"{S3.Name} 滿蓄貫穿:一發打穿一直線的兩個敵人,各吃 {S3.DamageFull}({_config.FullAttackClass})",
                fullA && fullB && bothDown && CountPierce() - pierceBefore >= 2,
                $"A 吃 {Describe(hitsA)}、B 吃 {Describe(hitsB)}、都倒={bothDown}、貫穿事件 {CountPierce() - pierceBefore}");
            healthA.Damaged -= onA;
            healthB.Damaged -= onB;
            // 靶子送回角落(倒地後由生命週期處理重生,不影響之後的量測)
            Teleport(_enemyA.transform, new Vector3(-23f, 0.1f, 23f));
            Teleport(_enemyB.transform, new Vector3(-21f, 0.1f, 23f));
            yield return new WaitForSeconds(0.2f);
        }

        // ── 起跳:滿蓄起跳高度 × 0.405;空中蓄力 1/3 ──

        private IEnumerator MeasureJumpAndAirCharge()
        {
            ChargerWeapon charger = Charger;
            _tank.InfiniteInk = true;
            yield return MoveTo(new Vector3(-10f, 0.1f, -2f));
            AimFlat(Vector3.left, 1.3f);
            yield return Frames(3);
            float normal = -1f;
            yield return JumpApex(h => normal = h);
            yield return MoveTo(new Vector3(-10f, 0.1f, -2f));
            _intent.Attack = true;
            yield return WaitUntil(() => charger.IsFullyCharged, S3.FullChargeSeconds + 1f);
            float charged = -1f;
            yield return JumpApex(h => charged = h);
            float ratio = normal > 0f ? charged / normal : 0f;
            float expected = S3.JumpHeightFactor(1f);
            Check($"{S3.Name} 滿蓄起跳高度 × {expected:0.000}(初速 {S3.JumpVelocityFullCharge} vs {S3.JumpVelocityNormal} u/F)",
                normal > 0.5f && Mathf.Abs(ratio - expected) < 0.07f,
                $"一般 {normal:0.00} m、滿蓄 {charged:0.00} m → × {ratio:0.000}");
            _intent.Attack = false;
            yield return new WaitForSeconds(0.5f);

            // 空中蓄力:地上按住後起跳,滯空期間的蓄力速率
            yield return MoveTo(new Vector3(-10f, 0.1f, -2f));
            _intent.Attack = true;
            _intent.PressJump();
            float deadline = Time.time + 1f;
            while (_controller.isGrounded && Time.time < deadline)
            {
                yield return null;
            }
            yield return new WaitForSeconds(0.12f);
            float f0 = charger.ChargeFrames;
            float t0 = Time.time;
            yield return new WaitForSeconds(0.3f);
            bool stillAir = !_controller.isGrounded;
            float rate = (charger.ChargeFrames - f0) / Mathf.Max(1e-3f, (Time.time - t0) * 60f);
            Check($"{S3.Name} 空中蓄力速率 × 1/3", stillAir && Mathf.Abs(rate - S3.AirChargeRate) < 0.06f,
                $"滯空中每幀 {rate:0.000}(仍在空中={stillAir})");
            _intent.Attack = false;
            yield return new WaitForSeconds(0.8f);
        }

        private IEnumerator JumpApex(System.Action<float> report)
        {
            float ground = _player.transform.position.y;
            _intent.PressJump();
            float peak = ground;
            bool left = false;
            float deadline = Time.time + 2.5f;
            while (Time.time < deadline)
            {
                yield return null;
                TrackDelta();
                peak = Mathf.Max(peak, _player.transform.position.y);
                if (!_controller.isGrounded)
                {
                    left = true;
                }
                else if (left)
                {
                    break;
                }
            }
            yield return new WaitForSeconds(0.2f);
            report(peak - ground);
        }

        // ── 墨不足:1/3 速率蓄滿、照樣打出滿蓄;連最小蓄力都不夠 → 不出手 ──

        private IEnumerator MeasureLowInk()
        {
            ChargerWeapon charger = Charger;
            _tank.InfiniteInk = false;
            yield return MoveTo(new Vector3(-10f, 0.1f, 8f));
            AimFlat(Vector3.left, 1.3f);
            yield return Frames(2);
            const float level = 0.05f;
            SetTankLevel(level);
            float affordable = S3.AffordableFrames(level);
            float expected = (affordable + (S3.FullChargeFrames - affordable) / S3.LowInkChargeRate) / 60f;
            _intent.Attack = true;
            yield return WaitUntil(() => charger.IsFullyCharged, expected + 2f);
            float took = charger.FullChargeTime - charger.ChargeStartTime;
            Check($"{S3.Name} 墨只剩 5%:付得起的 {affordable:0.0}F 之後以 1/3 速率蓄滿",
                charger.IsFullyCharged && Mathf.Abs(took - expected) < 0.1f,
                $"滿蓄花 {took:0.00} s(期望 {expected:0.00} s)");
            int low = charger.LowInkShots;
            int shots = charger.ShotsFired;
            _intent.Attack = false;
            yield return WaitUntil(() => charger.ShotsFired > shots, 1f);
            Check($"{S3.Name} 墨不足的滿蓄照樣打出({S3.DamageFull} 傷)、剩下的墨用光",
                charger.ShotsFired == shots + 1 && charger.LowInkShots == low + 1 && charger.LastShotFull
                && _tank.Normalized < 0.002f,
                $"發數 +{charger.ShotsFired - shots}、墨不足發 +{charger.LowInkShots - low}、墨槽 {_tank.Normalized:0.000}");

            SetTankLevel(0.01f);
            int failed = charger.FailedShots;
            shots = charger.ShotsFired;
            _intent.Attack = true;
            yield return new WaitForSeconds(0.5f);
            _intent.Attack = false;
            yield return new WaitForSeconds(0.3f);
            Check($"{S3.Name} 墨不到最小蓄力 {S3.InkConsumeMin * 100f:0.##}%:不出手",
                charger.ShotsFired == shots && charger.FailedShots == failed + 1,
                $"發數 +{charger.ShotsFired - shots}、失敗 +{charger.FailedShots - failed}");
            _tank.RefillFull();
        }

        // ── 儲存蓄力 ──

        private IEnumerator MeasureKeepCharge()
        {
            ChargerWeapon charger = Charger;
            _tank.InfiniteInk = true;
            Vector3 spot = new Vector3(-10f, 0.1f, 20f);
            InkPainter.PaintSphere(new Vector3(spot.x, 0f, spot.z), 3.5f, 1f, _inkling.InkSource(false));
            yield return MoveTo(spot);
            AimFlat(Vector3.right, 1.3f);
            yield return Frames(3);

            // (a) 滿蓄 → 按住潛墨保留 → 浮出 25F 雷射、31F 開槍
            _intent.Attack = true;
            yield return WaitUntil(() => charger.IsFullyCharged, S3.FullChargeSeconds + 1f);
            int keeps = charger.KeepCount;
            _intent.Squid = true;
            yield return WaitUntil(() => charger.IsKeepingCharge, 0.5f);
            yield return new WaitForSeconds(0.25f);
            bool keeping = charger.IsKeepingCharge && _squid.IsSquid && charger.KeepCount == keeps + 1;
            bool ring = charger.Laser != null && charger.Laser.RingVisible && !charger.Laser.LaserVisible;
            Check($"{S3.Name} 儲存蓄力:滿蓄按住攻擊潛墨 → 保留(雷射收起、頭上白圈、bot 看得到)",
                keeping && ring && charger.RevealsSubmerged && charger.IsFullyCharged,
                $"保留={charger.IsKeepingCharge} 烏賊={_squid.IsSquid} 白圈={charger.Laser?.RingVisible} " +
                $"雷射={charger.Laser?.LaserVisible} 露位={charger.RevealsSubmerged}");
            _intent.Squid = false;
            yield return WaitUntil(() => charger.State == ChargerState.Surfacing, 0.5f);
            float surfaced = charger.SurfaceTime;
            _intent.Attack = false;
            int shots = charger.ShotsFired;
            float laserBack = -1f;
            float deadline = Time.time + 1.5f;
            while (charger.ShotsFired == shots && Time.time < deadline)
            {
                yield return null;
                TrackDelta();
                if (laserBack < 0f && charger.Laser != null && charger.Laser.LaserVisible)
                {
                    laserBack = Time.time;
                }
            }
            float fireDelay = charger.LastShotTime - surfaced;
            Check($"{S3.Name} 浮出後 {S3.KeepFireDelayFrames}F 才開槍(提早放開緩衝)、打出保留的滿蓄",
                charger.ShotsFired == shots + 1 && charger.LastShotFromKeep && charger.LastShotFull
                && Mathf.Abs(fireDelay - S3.KeepFireDelayFrames / 60f) <= Tolerance,
                $"浮出後 {fireDelay:0.000} s 出手(期望 {S3.KeepFireDelayFrames / 60f:0.000})、滿蓄={charger.LastShotFull}");
            Check($"{S3.Name} 浮出後 {S3.KeepLaserDelayFrames}F 雷射重新出現",
                laserBack > 0f && Mathf.Abs(laserBack - surfaced - S3.KeepLaserDelayFrames / 60f) <= Tolerance,
                $"浮出後 {laserBack - surfaced:0.000} s(期望 {S3.KeepLaserDelayFrames / 60f:0.000})");
            yield return new WaitForSeconds(0.4f);

            // (b) 保留超過 75F → 蓄力消失
            _intent.Attack = true;
            yield return WaitUntil(() => charger.IsFullyCharged, S3.FullChargeSeconds + 1f);
            int expired = charger.KeepExpiredCount;
            _intent.Squid = true;
            yield return WaitUntil(() => charger.IsKeepingCharge, 0.5f);
            float keepStart = charger.KeepStartTime;
            yield return WaitUntil(() => !charger.IsKeepingCharge, 2.5f);
            float kept = Time.time - keepStart;
            Check($"{S3.Name} 保留最多 {S3.KeepChargeFrames}F:時間到蓄力消失",
                charger.KeepExpiredCount == expired + 1 && charger.State == ChargerState.Idle
                && Mathf.Abs(kept - S3.KeepChargeFrames / 60f) <= Tolerance * 2f,
                $"保留 {kept:0.000} s(期望 {S3.KeepChargeFrames / 60f:0.000})、狀態 {charger.State}");
            _intent.Squid = false;
            yield return new WaitForSeconds(0.3f);
            Check($"{S3.Name} 過期後浮出仍按著:從 0 重新蓄", charger.IsCharging && charger.Charge01 < 0.45f,
                $"狀態 {charger.State}、蓄力 {charger.Charge01:0.00}");
            _intent.Attack = false;
            yield return new WaitForSeconds(0.4f);

            // (c) 部分蓄力潛墨 = 沒了;(d) 保留中放開攻擊 = 沒了(都不出手)
            shots = charger.ShotsFired;
            _intent.Attack = true;
            yield return new WaitForSeconds(S3.FullChargeSeconds * 0.4f);
            int cancels = charger.CancelCount;
            _intent.Squid = true;
            yield return new WaitForSeconds(0.2f);
            bool partialLost = charger.State == ChargerState.Idle && charger.CancelCount == cancels + 1;
            _intent.Squid = false;
            yield return WaitUntil(() => !_squid.IsSquid, 0.5f);
            yield return WaitUntil(() => charger.IsFullyCharged, S3.FullChargeSeconds + 1f);
            _intent.Squid = true;
            yield return WaitUntil(() => charger.IsKeepingCharge, 0.5f);
            _intent.Attack = false;
            yield return new WaitForSeconds(0.2f);
            bool releasedLost = charger.State == ChargerState.Idle;
            _intent.Squid = false;
            yield return new WaitForSeconds(0.3f);
            Check($"{S3.Name} 部分蓄力潛墨、保留中放開攻擊:蓄力消失且不出手",
                partialLost && releasedLost && charger.ShotsFired == shots,
                $"部分蓄力潛墨→取消={partialLost}、保留中放開→取消={releasedLost}、發數 +{charger.ShotsFired - shots}");
        }

        // ── 開槍後 15F 不能潛墨 ──

        private IEnumerator MeasureNoSquidAfterShot()
        {
            ChargerWeapon charger = Charger;
            _tank.InfiniteInk = true;
            Vector3 spot = new Vector3(-10f, 0.1f, 20f);
            InkPainter.PaintSphere(new Vector3(spot.x, 0f, spot.z), 3.5f, 1f, _inkling.InkSource(false));
            yield return MoveTo(spot);
            AimFlat(Vector3.right, 1.3f);
            yield return Frames(3);
            int shots = charger.ShotsFired;
            _intent.Attack = true;
            yield return null;
            _intent.Attack = false;
            yield return WaitUntil(() => charger.ShotsFired > shots, 1f);
            _intent.Squid = true;
            yield return WaitUntil(() => _squid.IsSquid, 1f);
            float delay = Time.time - charger.LastShotTime;
            float expected = S3.PostShotNoSquidFrames / 60f;
            Check($"{S3.Name} 開槍後 {S3.PostShotNoSquidFrames}F 才能潛墨",
                _squid.IsSquid && delay >= expected - Tolerance && delay <= expected + Tolerance * 2f,
                $"開槍後 {delay:0.000} s 變烏賊(期望 {expected:0.000})");
            _intent.Squid = false;
            yield return new WaitForSeconds(0.4f);
        }

        // ── 雷射停在牆上;子彈撞牆、墨線塗到牆腳 ──

        private IEnumerator MeasureLaserWall()
        {
            ChargerWeapon charger = Charger;
            _tank.InfiniteInk = true;
            yield return MoveTo(new Vector3(-14f, 0.1f, 2f));
            AimFlat(Vector3.forward, 1.3f);
            Vector3 feet = Flat(_player.transform.position);
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "ChargerTestWall";
            wall.transform.position = feet + Vector3.forward * 9f + Vector3.up * 1.5f;
            wall.transform.localScale = new Vector3(4f, 3f, 0.5f);
            _spawned.Add(wall);
            Physics.SyncTransforms();
            float face = feet.z + 9f - 0.25f;
            yield return Frames(3);
            _intent.Attack = true;
            yield return WaitUntil(() => charger.ChargeFrames >= S3.FullChargeFrames * 0.5f, 3f);
            yield return Frames(2);
            float laserEnd = charger.Laser != null ? charger.Laser.LaserTo.z : -999f;
            Check($"{S3.Name} 雷射停在牆上(射程 > 牆距)", charger.Laser != null && charger.Laser.LaserVisible
                                                       && Mathf.Abs(laserEnd - face) < 0.15f,
                $"雷射終點 z={laserEnd:0.00}、牆面 z={face:0.00}");
            _intent.Attack = false;
            int shots = charger.ShotsFired;
            yield return WaitUntil(() => charger.ShotsFired > shots, 1f);
            InkBulletImpact end = default;
            yield return WaitUntil(() => TryFinalImpact(charger.LastShotAttackId, out end), 1f);
            yield return new WaitForSeconds(0.2f);
            Vector3 beforeWall = new Vector3(charger.LastShotOrigin.x, 0f, face - 1.2f);
            Check($"{S3.Name} 子彈撞牆停下、墨線一路塗到牆前",
                end.End == InkBulletEnd.Terrain && Mathf.Abs(end.Point.z - face) < 0.15f && Own(beforeWall)
                && Mathf.Abs(charger.LastTrailEnd.z - face) < 0.2f,
                $"終點 {end.End} z={end.Point.z:0.00}、墨線終點 z={charger.LastTrailEnd.z:0.00}、牆前 1.2 m 有墨={Own(beforeWall)}");
            _spawned.Remove(wall);
            Destroy(wall);
            yield return new WaitForSeconds(0.2f);
        }

        // ── 對物件倍率:部分蓄力(Charger / Charger_Long)× 傘面、滿蓄(ChargerFull / _Long)× Big Bubbler 罩 ──

        private IEnumerator MeasureDeviceRates()
        {
            ChargerWeapon charger = Charger;
            _tank.InfiniteInk = true;
            yield return MoveTo(new Vector3(-7f, 0.1f, 3f));
            AimFlat(Vector3.forward, 1.1f);
            Vector3 feet = Flat(_player.transform.position);
            TestDeployable canopy = TestDeployable.Create("ChargerTestCanopy", feet + Vector3.forward * 8f + Vector3.up * 1.1f,
                new Vector3(2f, 2f, 0.4f));
            canopy.Setup(null, Team.Bravo, 10000f, TargetClass.BrellaCanopy, false);
            _spawned.Add(canopy.gameObject);
            yield return Frames(3);
            yield return ChargeTo(26f);
            yield return new WaitForSeconds(0.4f);
            float partialRate = DamageRates.Get(_config.PartialAttackClass, TargetClass.BrellaCanopy);
            float expected = S3.Damage(charger.LastShotChargeFrames) * partialRate;
            float dealt = 10000f - canopy.Hp;
            Check($"{S3.Name} 部分蓄力打傘面 × {partialRate}({_config.PartialAttackClass})",
                Mathf.Abs(dealt - expected) < 0.25f, $"扣 {dealt:0.0}(期望 {expected:0.0})");
            _spawned.Remove(canopy.gameObject);
            Destroy(canopy.gameObject);
            yield return Frames(2);

            TestDeployable shield = TestDeployable.Create("ChargerTestShield", feet + Vector3.forward * 8f + Vector3.up * 1.1f,
                new Vector3(2f, 2f, 0.4f));
            shield.Setup(null, Team.Bravo, 10000f, TargetClass.BubblerShield, false);
            _spawned.Add(shield.gameObject);
            yield return Frames(3);
            yield return ChargeTo(S3.FullChargeFrames);
            yield return new WaitForSeconds(0.4f);
            float fullRate = DamageRates.Get(_config.FullAttackClass, TargetClass.BubblerShield);
            expected = S3.DamageFull * fullRate;
            dealt = 10000f - shield.Hp;
            bool stopped = TryFinalImpact(charger.LastShotAttackId, out InkBulletImpact end) && end.End == InkBulletEnd.Target;
            Check($"{S3.Name} 滿蓄打 Big Bubbler 罩 × {fullRate}({_config.FullAttackClass});裝置擋下子彈(只貫穿角色)",
                Mathf.Abs(dealt - expected) < 0.25f && stopped, $"扣 {dealt:0.0}(期望 {expected:0.0})、終點 {end.End}");
            _spawned.Remove(shield.gameObject);
            Destroy(shield.gameObject);
            yield return new WaitForSeconds(0.2f);
        }

        // ── bot 式:用 bot 掛勾驅動(與 BotBrain 同一組呼叫)──

        private IEnumerator MeasureBotStyle()
        {
            ChargerWeapon charger = Charger;
            _tank.InfiniteInk = true;
            yield return MoveTo(new Vector3(-17f, 0.1f, 2f));
            AimFlat(Vector3.forward, 1.3f);
            Vector3 feet = Flat(_player.transform.position);
            float range = S3.ReachFromPlayer(S3.FullChargeFrames);
            Teleport(_enemyC.transform, feet + Vector3.forward * (range * 0.75f) + Vector3.up * 0.1f);
            var healthC = _enemyC.GetComponent<InklingHealth>();
            healthC.ResetForRespawn();
            var hitsC = new List<DamageInfo>();
            System.Action<float, DamageInfo> onC = (d, info) => hitsC.Add(info);
            healthC.Damaged += onC;
            yield return new WaitForSeconds(0.3f);

            // (a) 中距離:按住蓄到滿、雷射掃到敵人才放
            int shots = charger.ShotsFired;
            bool laserAtRelease = false;
            float deadline = Time.time + S3.FullChargeSeconds + 2.5f;
            while (charger.ShotsFired == shots && Time.time < deadline)
            {
                AimAt(_enemyC.transform.position + Vector3.up * 1.0f, 1.3f);
                var context = new BotCombatContext
                {
                    Distance = Vector3.Distance(_player.transform.position, _enemyC.transform.position),
                    TargetVisible = true,
                    Reacted = true,
                    AimError = 0.5f,
                    FireTolerance = 5f,
                };
                bool hold = charger.BotAttackIntent(context);
                if (!hold && _intent.Attack)
                {
                    laserAtRelease = charger.LaserOnEnemy;
                }
                _intent.Attack = hold;
                yield return null;
                TrackDelta();
            }
            _intent.Attack = false;
            yield return new WaitForSeconds(0.4f);
            Check($"{S3.Name} bot 式:{range * 0.75f:0.0} m 的敵人 → 蓄滿、雷射掃到才放、一擊",
                charger.ShotsFired == shots + 1 && charger.LastShotFull && laserAtRelease && hitsC.Count == 1
                && Mathf.Approximately(hitsC[0].Amount, S3.DamageFull) && !healthC.IsAlive,
                $"發數 +{charger.ShotsFired - shots}、滿蓄={charger.LastShotFull}、放槍時雷射在敵人身上={laserAtRelease}、" +
                $"敵人吃 {Describe(hitsC)}");
            healthC.Damaged -= onC;
            Teleport(_enemyC.transform, new Vector3(-19f, 0.1f, 23f));

            // (b) 貼近:蓄過最小蓄力就放(部分蓄力逼退)
            var near = new BotCombatContext
            {
                Distance = _config.BotCloseRange * 0.7f, TargetVisible = true, Reacted = true, AimError = 0.5f, FireTolerance = 5f,
            };
            shots = charger.ShotsFired;
            deadline = Time.time + 1.5f;
            while (charger.ShotsFired == shots && Time.time < deadline)
            {
                _intent.Attack = charger.BotAttackIntent(near);
                yield return null;
            }
            _intent.Attack = false;
            Check($"{S3.Name} bot 式:{near.Distance:0.0} m 貼近 → 部分蓄力就放",
                charger.ShotsFired == shots + 1 && !charger.LastShotFull && charger.LastShotChargeFrames < S3.FullChargeFrames * 0.5f,
                $"蓄 {charger.LastShotChargeFrames:0.0}F 放槍、滿蓄={charger.LastShotFull}");
            yield return new WaitForSeconds(0.3f);

            // (c) 塗地:蓄滿打向遠處地面
            yield return MoveTo(new Vector3(-12f, 0.1f, -8f));
            AimAt(Flat(_player.transform.position) + Vector3.left * charger.BotPaintAimDistance, 1.3f);
            _tank.InfiniteInk = false;
            _tank.RefillFull();
            shots = charger.ShotsFired;
            int fullBefore = charger.FullShots;
            // 兩輪「蓄滿 → 放 → 站著不動時等 1.2 s 再蓄」(bot 在這段時間沿墨線跑 / 潛游過去)
            float paintSeconds = (S3.FullChargeSeconds + ChargerWeapon.BotPaintRepositionSeconds) * 2f + 0.4f;
            deadline = Time.time + paintSeconds;
            float firstShot = -1f;
            float secondCharge = -1f;
            while (Time.time < deadline)
            {
                _intent.Attack = charger.BotPaintIntent(_tank.Normalized);
                yield return null;
                if (firstShot < 0f && charger.ShotsFired > shots)
                {
                    firstShot = charger.LastShotTime;
                }
                if (firstShot > 0f && secondCharge < 0f && charger.IsCharging)
                {
                    secondCharge = charger.ChargeStartTime;
                }
            }
            _intent.Attack = false;
            yield return new WaitForSeconds(0.3f);
            int paintShots = charger.ShotsFired - shots;
            Vector3 paintLine = Flat(_player.transform.position) + Vector3.left * (charger.BotPaintAimDistance * 0.6f);
            // 最後一發可能是停手那一刻放開的部分蓄力(bot 換模式時也一樣),只要求至少兩發滿蓄
            float pause = secondCharge - firstShot;
            Check($"{S3.Name} bot 塗地:蓄滿 → 放,打向 {charger.BotPaintAimDistance:0.0} m 外的地面、一路塗成墨線;" +
                  $"打完先移動 {ChargerWeapon.BotPaintRepositionSeconds} s 再蓄",
                charger.FullShots - fullBefore >= 2 && paintShots - (charger.FullShots - fullBefore) <= 1 && Own(paintLine)
                && pause >= ChargerWeapon.BotPaintRepositionSeconds - 0.05f,
                $"{paintSeconds:0.0} s 打了 {paintShots} 發(滿蓄 {charger.FullShots - fullBefore})、中段有墨={Own(paintLine)}、" +
                $"第一發後 {pause:0.00} s 才再蓄");

            // (d) 回墨前往腳邊點一下
            yield return MoveTo(new Vector3(-6f, 0.1f, -8f));
            AimAt(Flat(_player.transform.position) + Vector3.left * 1.2f, 1.3f);
            shots = charger.ShotsFired;
            deadline = Time.time + 0.6f;
            while (Time.time < deadline)
            {
                _intent.Attack = charger.BotFeetPaintIntent(_tank.Normalized);
                yield return null;
            }
            _intent.Attack = false;
            yield return new WaitForSeconds(0.3f);
            Check($"{S3.Name} bot 回墨前往腳邊點一下:最小蓄力就放、腳邊有墨",
                charger.ShotsFired > shots && charger.LastShotChargeFrames < S3.MinChargeFrames + 1f + _maxDeltaTime * 60f
                && Own(Flat(_player.transform.position) + Vector3.left * 1.2f),
                $"{charger.ShotsFired - shots} 發、最後一發蓄 {charger.LastShotChargeFrames:0.0}F");
            _tank.RefillFull();
        }

        // ── 蓄力拉近(本機相機)、潛墨隱藏槍與雷射 ──

        private IEnumerator MeasureZoomAndSquid()
        {
            ChargerWeapon charger = Charger;
            _tank.InfiniteInk = true;
            Vector3 spot = new Vector3(-10f, 0.1f, 20f);
            InkPainter.PaintSphere(new Vector3(spot.x, 0f, spot.z), 3.5f, 1f, _inkling.InkSource(false));
            yield return MoveTo(spot);
            Camera camera = Camera.main;
            var rig = camera != null ? camera.GetComponent<ThirdPersonCameraRig>() : null;
            if (rig != null && _config.ChargeZoomFov > 0f)
            {
                _inkling.AimView = rig;
                yield return new WaitForSeconds(0.5f);
                float before = camera.fieldOfView;
                _intent.Attack = true;
                yield return WaitUntil(() => charger.IsFullyCharged, S3.FullChargeSeconds + 1f);
                yield return new WaitForSeconds(0.6f);
                float zoomed = camera.fieldOfView;
                _intent.Attack = false;
                yield return new WaitForSeconds(0.9f);
                float after = camera.fieldOfView;
                Check($"{S3.Name} 蓄力輕微拉近(本機相機 → {_config.ChargeZoomFov}°)、開槍後還原",
                    zoomed <= _config.ChargeZoomFov + 1.5f && zoomed < before - 2f && after > zoomed + 2f,
                    $"FOV {before:0.0} → 滿蓄 {zoomed:0.0} → 開槍後 {after:0.0}");
                _inkling.AimView = _aim;
            }
            AimFlat(Vector3.right, 1.3f);
            yield return Frames(2);
            _intent.Attack = true;
            yield return new WaitForSeconds(0.3f);
            _intent.Attack = false;
            yield return new WaitForSeconds(0.3f);
            _intent.Squid = true;
            yield return new WaitForSeconds(0.6f);
            bool hidden = charger.Model != null && !charger.Model.Visible && !charger.Laser.LaserVisible
                          && !charger.Laser.RingVisible;
            _intent.Squid = false;
            yield return new WaitForSeconds(0.6f);
            bool shown = charger.Model != null && charger.Model.Visible;
            Check($"{S3.Name} 潛墨時狙擊槍一起隱形(沒保留蓄力時沒有白圈)、浮出後出現", hidden && shown,
                $"潛墨隱藏={hidden} 浮出顯示={shown}");
        }

        // ── 收尾:換回 Splattershot,內建槍身恢復、狙擊槍模型 / 雷射 / 準星移除 ──

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
                ChargerWeapon charger = Charger;
                ChargerLaser laser = charger != null ? charger.Laser : null;
                _loadout.Equip(_baseKit);
                Renderer gun = BuiltInGun();
                Check($"{S3.Name} 換回 Splattershot:內建槍身恢復、狙擊槍元件移除",
                    !(_loadout.Main is ChargerWeapon) && gun != null && !gun.forceRenderingOff,
                    $"主武器={(_loadout.Main != null ? _loadout.Main.GetType().Name : "無")} 槍身隱藏={(gun != null && gun.forceRenderingOff)} " +
                    $"雷射待銷毀={(laser != null)}");
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
                _inkling.AimView = _originalView;
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
            _aim = null;
            if (_player != null)
            {
                Teleport(_player.transform, new Vector3(0f, 0.1f, 0f));
            }
        }

        // ── 小工具 ──

        // 按住蓄到 frames(滿蓄 = 等 IsFullyCharged)再放開,等子彈飛完
        private IEnumerator ChargeTo(float frames)
        {
            ChargerWeapon charger = Charger;
            int shots = charger.ShotsFired;
            _intent.Attack = true;
            bool full = frames >= S3.FullChargeFrames;
            float deadline = Time.time + S3.FullChargeSeconds * 3f + 1f;
            while (Time.time < deadline && (full ? !charger.IsFullyCharged : charger.ChargeFrames < frames))
            {
                yield return null;
                TrackDelta();
            }
            _intent.Attack = false;
            yield return WaitUntil(() => charger.ShotsFired > shots, 1f);
            yield return WaitUntil(() => TryFinalImpact(charger.LastShotAttackId, out _), 1f);
        }

        private IEnumerator Tap()
        {
            _intent.Attack = true;
            yield return null;
            _intent.Attack = false;
        }

        // 瞬移後等落地站穩
        private IEnumerator MoveTo(Vector3 position)
        {
            _intent.Attack = false;
            _intent.Move = Vector2.zero;
            _intent.Squid = false;
            Teleport(_player.transform, position);
            int grounded = 0;
            float deadline = Time.time + 1.5f;
            while (grounded < 4 && Time.time < deadline)
            {
                yield return null;
                TrackDelta();
                grounded = _controller.isGrounded ? grounded + 1 : 0;
            }
        }

        private void AimFlat(Vector3 direction, float height)
        {
            _aimAtPoint = false;
            _aimDirection = direction;
            _aimHeight = height;
            LateUpdate();
        }

        private void AimAt(Vector3 point, float height)
        {
            _aimAtPoint = true;
            _aimPoint = point;
            _aimHeight = height;
            LateUpdate();
        }

        private void SetTankLevel(float level)
        {
            _tank.RefillFull();
            _tank.Drain(1f - level);
            // 量測期間不讓站立回墨干擾
            _tank.LockRefill(3f);
        }

        // 這一發的最後一筆事件(不是貫穿):射程盡頭 / 撞地形 / 停在目標
        private bool TryFinalImpact(int attackId, out InkBulletImpact impact)
        {
            for (int i = _impacts.Count - 1; i >= 0; i--)
            {
                if (_impacts[i].AttackId == attackId && _impacts[i].End != InkBulletEnd.Pierce)
                {
                    impact = _impacts[i];
                    return true;
                }
            }
            impact = default;
            return false;
        }

        private int CountPierce()
        {
            int count = 0;
            foreach (InkBulletImpact impact in _impacts)
            {
                if (impact.End == InkBulletEnd.Pierce && impact.Damage > 0f)
                {
                    count++;
                }
            }
            return count;
        }

        private float SumHits(IDamageable target, int from, out bool splatted)
        {
            float total = 0f;
            splatted = false;
            for (int i = from; i < _hits.Count; i++)
            {
                if (ReferenceEquals(_hits[i].Target, target))
                {
                    total += _hits[i].Amount;
                    splatted |= _hits[i].Splatted;
                }
            }
            return total;
        }

        private static string Describe(List<DamageInfo> hits)
        {
            if (hits.Count == 0)
            {
                return "0 次";
            }
            var text = new System.Text.StringBuilder();
            text.Append(hits.Count).Append(" 次:");
            for (int i = 0; i < hits.Count; i++)
            {
                text.Append(i > 0 ? ", " : string.Empty).Append(hits[i].Amount.ToString("0.0")).Append('(')
                    .Append(hits[i].AttackClass).Append(')');
            }
            return text.ToString();
        }

        // 地面這一點是不是自家墨
        private bool Own(Vector3 point)
        {
            return _ground.SampleOwnership(new Vector3(point.x, 0f, point.z), Vector3.up) == (byte)_inkling.Team;
        }

        // 從 center 沿 ±axis 量連續自家墨的總寬
        private float OwnWidth(Vector3 center, Vector3 axis, float max)
        {
            const float step = 0.05f;
            float left = 0f;
            float right = 0f;
            for (float x = 0f; x < max; x += step)
            {
                if (!Own(center - axis * x))
                {
                    break;
                }
                left = x;
            }
            for (float x = 0f; x < max; x += step)
            {
                if (!Own(center + axis * x))
                {
                    break;
                }
                right = x;
            }
            return left + right;
        }

        private static Vector3 Flat(Vector3 p)
        {
            p.y = 0f;
            return p;
        }

        private static Vector3 DummyCenter(TargetDummy dummy)
        {
            return dummy.transform.position + Vector3.up * 1f;
        }

        private TargetDummy SpawnDummy(Vector3 groundPosition)
        {
            GameObject go = Instantiate(_dummyTemplate, groundPosition, Quaternion.identity);
            go.name = "ChargerTestDummy";
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

        private Renderer BuiltInGun()
        {
            Transform gun = _characterVisual != null ? _characterVisual.WeaponModel : null;
            return gun != null ? gun.GetComponent<Renderer>() : null;
        }

        private static bool ColorClose(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) < 0.02f && Mathf.Abs(a.g - b.g) < 0.02f && Mathf.Abs(a.b - b.b) < 0.02f;
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

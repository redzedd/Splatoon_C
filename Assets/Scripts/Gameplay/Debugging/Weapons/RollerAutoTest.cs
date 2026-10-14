using System.Collections;
using System.Collections.Generic;
using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Core.Match;
using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 滾筒驗收的共用流程(每把滾筒一個 [AutoTest] 子類,各自在全新 play session 跑)。
    // 全部走真實遊戲路徑(意圖 → RollerWeapon → InkBullet / 推滾 → 假人 / 地面 / 墨槽),期望值取自 S3RollerParams:
    //   換裝外觀(滾筒掛右手、內建槍身隱藏、墨水部位隊伍色、角色滾筒姿勢)/ bot 掛勾
    //   橫甩:舉起 SwingFrame、墨團數、耗墨、墨量鎖、甩動循環;距離傷害(近距一擊、必殺邊緣、遠距衰減、同一甩只算最高一顆)
    //   直甩(空中):舉起幀、墨團數、耗墨/鎖、超過橫甩必殺距離仍一擊
    //   甩動走速;推滾:起步延遲、普通/衝刺速度、DashFrame、塗寬(普通/衝刺)、耗墨率、急轉掉速、墨量鎖 20F
    //   接觸傷害(靜止不傷、Splat/Dynamo 125 一擊、Carbon 70 + 自己被彈回 + 間隔後再撞擊倒)
    //   對物件倍率(甩墨 × 2.88 灑水器、推滾 × 100)、墨不足弱甩 / 空甩、bot 式開火與塗地、潛墨隱藏滾筒
    // 場地:試射場 x ≤ −4 的空地(x = −24 ~ −4、z = −24 ~ 0)。
    public abstract class RollerAutoTest : AutoTestBase
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
            public float Time;
            public IDamageable Target;
            public float Amount;
            public bool Splatted;
        }

        private readonly List<HitRecord> _hits = new List<HitRecord>();
        private readonly List<InkBulletImpact> _impacts = new List<InkBulletImpact>();
        private readonly List<GameObject> _spawned = new List<GameObject>();

        private GameObject _player;
        private Inkling _inkling;
        private WeaponLoadout _loadout;
        private PlayerInkTank _tank;
        private Transform _visual;
        private CharacterController _controller;
        private PlayerLocomotion _locomotion;
        private CharacterVisual _characterVisual;
        private ScriptedIntent _intent;
        private TestAim _aim;
        private GameObject _dummyTemplate;
        private PaintableSurface _ground;
        private RollerConfig _config;
        private WeaponKit _baseKit;
        private WeaponKit _rollerKit;
        private float _maxDeltaTime;
        private float _lastNearestTravel;
        private int _lastHitEvents;
        private float _aimHeight = 1.3f;
        private Vector3 _aimDirection = Vector3.forward;
        private bool _aimAtPoint;
        private Vector3 _aimPoint;

        // Resources/Weapons/Main_<id>
        protected abstract string ConfigId { get; }

        // S3 原始參數(期望值來源)
        protected abstract S3RollerParams S3 { get; }

        private RollerWeapon Roller => _loadout != null ? _loadout.Main as RollerWeapon : null;

        private float Tolerance => Mathf.Max(0.02f, _maxDeltaTime * 1.5f);

        private void OnEnable()
        {
            InkBullet.Impacted += OnImpact;
        }

        private void OnDisable()
        {
            InkBullet.Impacted -= OnImpact;
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
            _hits.Add(new HitRecord { Time = Time.time, Target = target, Amount = amount, Splatted = splatted });
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
            _locomotion = _player != null ? _player.GetComponent<PlayerLocomotion>() : null;
            _characterVisual = _player != null ? _player.GetComponent<CharacterVisual>() : null;
            _dummyTemplate = GameObject.Find("Dummy_Near");
            _config = Resources.Load<RollerConfig>("Weapons/Main_" + ConfigId);
            foreach (PaintableSurface surface in PaintableSurface.Active)
            {
                if (surface.name == "Ground")
                {
                    _ground = surface;
                }
            }
            if (_inkling == null || _loadout == null || _tank == null || _visual == null || _controller == null
                || _locomotion == null || _dummyTemplate == null || _ground == null)
            {
                FailSetup("找不到 Player(Inkling/WeaponLoadout/PlayerInkTank/Visual/PlayerLocomotion)、Dummy_Near 或 Ground");
                yield break;
            }
            if (_config == null)
            {
                FailSetup($"找不到 Resources/Weapons/Main_{ConfigId}(先跑 Tools/SplatoonC/M7 Setup)");
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

            yield return EquipAndInspect();
            if (Roller == null || Roller.Config != _config)
            {
                Cleanup();
                Finish();
                yield break;
            }
            CheckBotHooks();
            yield return MeasureHorizontalFlick();
            yield return MeasureRepressFlick();
            yield return MeasureFlickDamage();
            yield return MeasureVerticalFlick();
            yield return MeasureSwingMoveSpeed();
            yield return MeasureRolling();
            yield return MeasureContact();
            yield return MeasureDeviceRates();
            yield return MeasureDepletion();
            yield return MeasureBotStyle();
            yield return MeasureSquidHidesModel();

            Debug.Log($"[AUTOTEST] INFO {S3.Name} 量測期間最大幀時間 {_maxDeltaTime * 1000f:0.0} ms");
            Cleanup();
            Finish();
        }

        // ── 換裝、外觀 ──

        private IEnumerator EquipAndInspect()
        {
            WeightClass weight = S3.LightWeight ? WeightClass.Light : S3.HeavyWeight ? WeightClass.Heavy : WeightClass.Mid;
            _rollerKit = ScriptableObject.CreateInstance<WeaponKit>();
            _rollerKit.Setup(ConfigId, S3.Name, S3.Name, "AutoTest 執行期組合", _config, _baseKit.Sub, _baseKit.Special,
                200f, weight, "射程", 50, "塗墨", 50, "機動", 50);
            yield return MoveTo(new Vector3(-12f, 0.1f, -20f));
            _loadout.Equip(_rollerKit);
            yield return Frames(3);

            RollerWeapon roller = Roller;
            Check($"{S3.Name} 換裝:主武器換成 RollerWeapon", roller != null && roller.Config == _config,
                $"主武器={(_loadout.Main != null ? _loadout.Main.GetType().Name : "無")}");
            if (roller == null)
            {
                yield break;
            }
            RollerModel model = roller.Model;
            Renderer gun = BuiltInGun();
            bool onSocket = model != null && _characterVisual != null && model.transform.parent == _characterVisual.WeaponSocket;
            Check($"{S3.Name} 外觀:程序化滾筒掛在右手 WeaponSocket、內建槍身隱藏",
                model != null && model.SourcePrefab == _config.ModelPrefab && onSocket && gun != null
                && gun.forceRenderingOff && model.Visible,
                $"模型={(model != null ? model.name : "無")} 掛點={(model != null ? model.transform.parent.name : "-")} " +
                $"內建槍身隱藏={(gun != null && gun.forceRenderingOff)}");

            // 墨水部位由角色外觀的 MPB 染成隊伍色
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
            Check($"{S3.Name} 外觀:滾筒墨水部位是隊伍色", tinted && inkParts >= 2, $"墨水部位 {inkParts} 個,隊伍色={tinted}");

            Vector3 drum = model != null ? model.DrumCenter - _player.transform.position : Vector3.zero;
            Check($"{S3.Name} 角色姿勢:拿著時滾筒在身前低處(接手持槍姿勢)",
                _characterVisual != null && _characterVisual.RollerStanceActive && drum.y < 0.75f
                && new Vector2(drum.x, drum.z).magnitude > 0.35f,
                $"滾筒軸心相對腳底 {drum:F2}");
            float expectedRun = 5.76f * WeightClassUtil.RunMultiplier(weight);
            Check($"{S3.Name} 重量級別 {weight}(Carbon 輕、Dynamo 重)", _loadout.Kit.Weight == weight,
                $"跑速倍率 {WeightClassUtil.RunMultiplier(weight):0.000} → {expectedRun:0.00} m/s");
        }

        // ── bot 掛勾(BotBrain 交戰/塗地時呼叫的同一組方法)──

        private void CheckBotHooks()
        {
            RollerWeapon roller = Roller;
            float lethal = S3.HorizontalLethalRange;
            var inRange = new BotCombatContext
            {
                Distance = lethal * 0.9f, TargetVisible = true, Reacted = true, AimError = 1f, FireTolerance = 4f,
            };
            var far = inRange;
            far.Distance = lethal * 1.3f;
            var hidden = inRange;
            hidden.TargetVisible = false;
            var close = inRange;
            close.Distance = _config.BotRollCommitDistance * 0.8f;
            Check($"{S3.Name} bot:交戰距離 = 橫甩一擊必殺 {lethal:0.0} m、想維持 0.45 倍(伏擊)、塗地瞄腳前",
                Mathf.Abs(roller.EffectiveRange - lethal) < 0.01f
                && Mathf.Abs(roller.BotPreferredDistance - lethal * 0.45f) < 0.01f && roller.BotPaintAimDistance <= 2f,
                $"EffectiveRange={roller.EffectiveRange:0.00} 交戰={roller.BotPreferredDistance:0.00} " +
                $"塗地瞄 {roller.BotPaintAimDistance:0.0} m");
            Check($"{S3.Name} bot:必殺距離內點按甩墨、太遠/看不到不按、貼身按住推滾",
                roller.BotAttackIntent(inRange) && !roller.BotAttackIntent(far) && !roller.BotAttackIntent(hidden)
                && roller.BotAttackIntent(close) && roller.BotPaintIntent(1f) && !roller.BotPaintIntent(0.01f),
                $"近 {close.Distance:0.0} m 按住、{inRange.Distance:0.0} m 甩、{far.Distance:0.0} m 不按");
        }

        // ── 橫甩:舉起幀、墨團數、耗墨、墨量鎖、甩動循環、音效 ──

        private IEnumerator MeasureHorizontalFlick()
        {
            RollerWeapon roller = Roller;
            RollerSwingParams swing = S3.Horizontal;
            _tank.InfiniteInk = false;
            _tank.RefillFull();
            yield return MoveTo(new Vector3(-12f, 0.1f, -20f));
            AimFlat(Vector3.forward, _config.HorizontalSpawnHeight);
            yield return Frames(3);
            int flicks = roller.FlickCount;
            float before = _tank.Normalized;
            bool heard = false;
            System.Action<string> listen = id => heard |= id.StartsWith("roller_", System.StringComparison.Ordinal)
                                                          || id == "bomb_throw";
            GameAudio.Played += listen;
            yield return Tap();
            float peakDrum = -10f;
            float deadline = Time.time + 3f;
            while (roller.FlickCount == flicks && Time.time < deadline)
            {
                if (roller.Model != null)
                {
                    peakDrum = Mathf.Max(peakDrum, roller.Model.DrumCenter.y - _player.transform.position.y);
                }
                yield return null;
                TrackDelta();
            }
            GameAudio.Played -= listen;
            float spent = before - _tank.Normalized;
            float delay = roller.LastFlingTime - roller.LastSwingStartTime;
            Check($"{S3.Name} 橫甩:舉起 {swing.SwingFrame}F = {swing.SwingSeconds:0.000} s 後甩出",
                roller.FlickCount == flicks + 1 && !roller.LastFlingVertical
                && Mathf.Abs(delay - swing.SwingSeconds) <= Tolerance,
                $"實測 {delay:0.000} s(容許 ±{Tolerance:0.000})");
            Check($"{S3.Name} 橫甩:{swing.BulletCount(false)} 顆墨團(主扇形 ±{swing.Units[0].WideDegree}° + 中央)",
                roller.LastFlingBulletCount == swing.BulletCount(false), $"實際 {roller.LastFlingBulletCount} 顆");
            Check($"{S3.Name} 橫甩耗墨 {swing.InkConsume * 100f:0.##}%(一槽 {S3.FlicksPerTank} 甩)",
                Mathf.Abs(spent - swing.InkConsume) < 1e-4f, $"實測 {spent * 100f:0.000}%");
            Check($"{S3.Name} 舉起姿勢:橫甩把滾筒高舉過頭", peakDrum > 1.75f, $"舉起中滾筒軸心最高離地 {peakDrum:0.00} m");
            Check($"{S3.Name} 甩墨音效(roller_*;未合成前退回 bomb_throw)", heard, GameAudio.LastPlayedId ?? "-");

            // 墨量鎖:甩出後 InkRecoverStop 幀內不回墨;甩動循環結束回到 Idle
            float flingTime = roller.LastFlingTime;
            float level = _tank.Normalized;
            float unlocked = -1f;
            float idleAt = -1f;
            deadline = Time.time + swing.InkLockSeconds + 1.5f;
            while (Time.time < deadline && (unlocked < 0f || idleAt < 0f))
            {
                yield return null;
                TrackDelta();
                if (unlocked < 0f && _tank.Normalized > level + 1e-5f)
                {
                    unlocked = Time.time;
                }
                if (idleAt < 0f && roller.Action == RollerAction.Idle)
                {
                    idleAt = Time.time;
                }
            }
            float lockTolerance = Mathf.Max(0.034f, _maxDeltaTime * 2f);
            Check($"{S3.Name} 橫甩墨量鎖 {swing.InkRecoverStop}F = {swing.InkLockSeconds:0.000} s",
                unlocked > 0f && Mathf.Abs(unlocked - flingTime - swing.InkLockSeconds) <= lockTolerance,
                $"甩出後 {unlocked - flingTime:0.000} s 開始回墨(容許 ±{lockTolerance:0.000})");
            float cycle = swing.CycleFrames / 60f;
            Check($"{S3.Name} 甩動循環 {swing.CycleFrames}F 後才能甩下一下 [EST]",
                idleAt > 0f && Mathf.Abs(idleAt - roller.LastSwingStartTime - cycle) <= lockTolerance,
                $"按下後 {idleAt - roller.LastSwingStartTime:0.000} s 回到待機(期望 {cycle:0.000} s)");
            _tank.RefillFull();
        }

        // ── 連點:甩出後放開、收招中又按住 → 不推滾,等甩動循環結束再甩下一下;這一下一直按著才接推滾 ──

        private IEnumerator MeasureRepressFlick()
        {
            RollerWeapon roller = Roller;
            RollerSwingParams swing = S3.Horizontal;
            _tank.InfiniteInk = true;
            yield return MoveTo(new Vector3(-16f, 0.1f, -20f));
            AimFlat(Vector3.forward, _config.HorizontalSpawnHeight);
            yield return WaitUntil(() => roller.Action == RollerAction.Idle, 2f);
            yield return Frames(2);
            int swings = roller.SwingCount;
            int rolls = roller.RollStartCount;
            int flicks = roller.FlickCount;
            yield return Tap();
            float firstSwing = roller.LastSwingStartTime;
            yield return WaitUntil(() => roller.FlickCount > flicks, 2f);
            // 收招中(循環還沒結束)再按住不放
            yield return Frames(2);
            _intent.Attack = true;
            bool rolledEarly = false;
            float deadline = Time.time + swing.CycleFrames / 60f + 1f;
            while (roller.SwingCount < swings + 2 && Time.time < deadline)
            {
                rolledEarly |= roller.RollStartCount > rolls;
                yield return null;
                TrackDelta();
            }
            float gap = roller.LastSwingStartTime - firstSwing;
            float cycle = swing.CycleFrames / 60f;
            float tolerance = Mathf.Max(0.034f, _maxDeltaTime * 2f);
            Check($"{S3.Name} 連點:收招中再按 → 不推滾,{swing.CycleFrames}F 循環結束才甩下一下",
                roller.SwingCount == swings + 2 && !rolledEarly && Mathf.Abs(gap - cycle) <= tolerance,
                $"兩次舉起間隔 {gap:0.000} s(期望 {cycle:0.000})、之間推滾={rolledEarly}");
            // 第二下從按下一直按著:甩完接推滾
            yield return WaitUntil(() => roller.RollStartCount > rolls, swing.SwingSeconds + 1f);
            Check($"{S3.Name} 連點的第二下一直按著:甩完接推滾",
                roller.IsRolling && roller.RollStartTime > roller.LastFlingTime,
                $"推滾={roller.IsRolling}、甩出後 {roller.RollStartTime - roller.LastFlingTime:0.000} s 開始推滾");
            _intent.Attack = false;
            yield return WaitUntil(() => roller.Action == RollerAction.Idle, 2f);
            yield return new WaitForSeconds(0.2f);
        }

        // ── 橫甩距離傷害:近距一擊、必殺邊緣、遠距衰減;同一甩對同一目標只算最高一顆 ──

        private IEnumerator MeasureFlickDamage()
        {
            RollerSwingParams swing = S3.Horizontal;
            RollerFlingUnit main = swing.Units[0];
            float reach = _config.HorizontalSpawnForward + main.PlayerRadiusEnd + DummyRadius();
            // 近距:飛 1.5 m 就中 → 一擊。通常是中央墨團的最大傷害;偶爾最快的一顆是側邊墨團先到、
            // 單獨就打倒(Dynamo 側邊 120 → 100 @ 3.38 m),之後的墨團打不到已倒下的目標——數值由上面的「= S3 曲線最高值」核對
            yield return FlickAt(false, reach + 1.5f, (damage, killed, log) =>
                Check($"{S3.Name} 橫甩近距一擊(中央 {swing.Damage.Inside.MaxValue};同一甩多顆命中不疊加)",
                    killed && damage >= 100f && damage <= swing.Damage.Inside.MaxValue + 0.15f
                    && (Mathf.Abs(damage - swing.Damage.Inside.MaxValue) < 0.15f || _lastHitEvents == 1),
                    log));
            // 必殺邊緣:飛行距離 = 必殺距離 − 0.6 m → 仍 ≥ 100
            float lethal = S3.HorizontalLethalRange;
            yield return FlickAt(false, reach + lethal - 0.6f, (damage, killed, log) =>
                Check($"{S3.Name} 橫甩必殺距離 {lethal:0.0} m 內仍一擊(≥ 100)", killed && damage >= 100f, log));
            // 遠距:飛行距離 ≈ 必殺距離 + (最小距離 − 必殺距離) × 0.35 → 衰減、打不死(Dynamo 14.5 m 仍有一半以上墨團飛得到)
            float far = lethal + (swing.Damage.Inside.MinDistance - lethal) * 0.35f;
            yield return FlickAt(false, reach + far, (damage, killed, log) =>
                Check($"{S3.Name} 橫甩遠距衰減({far:0.0} m:< 100、≥ 最小 {swing.Damage.Inside.MinValue})",
                    !killed && damage < 100f && damage >= swing.Damage.Inside.MinValue - 0.15f, log));
        }

        // 在前方 distance(從腳底量)放假人,地面橫甩一下,回報該甩對假人的總傷害、是否擊倒、證據字串
        private IEnumerator FlickAt(bool vertical, float distance, System.Action<float, bool, string> report)
        {
            RollerWeapon roller = Roller;
            _tank.InfiniteInk = true;
            yield return MoveTo(new Vector3(-12f, 0.1f, -23f));
            AimFlat(Vector3.forward, _config.HorizontalSpawnHeight);
            yield return WaitSwingCycleEnded();
            yield return Frames(2);
            TargetDummy dummy = SpawnDummy(Flat(_player.transform.position) + Vector3.forward * distance);
            yield return Frames(2);
            int flicks = roller.FlickCount;
            int hitsBefore = _hits.Count;
            int impactsBefore = _impacts.Count;
            // 墨團初速/偏角的亂數固定種子:每次跑同一組扇形,結果可重現(甩出後還原,不影響之後的測試)
            Random.State saved = Random.state;
            Random.InitState(7919 + Mathf.RoundToInt(distance * 100f));
            yield return Tap();
            yield return WaitUntil(() => roller.FlickCount > flicks, 2f);
            Random.state = saved;
            yield return ReportFlick(false, dummy, distance, hitsBefore, impactsBefore, report);
        }

        // 空中直甩:假人先藏在地下;甩出那一幀依實際甩墨基準點,把假人(浮空,中心與基準點同高)擺到
        // 「飛行 travel 公尺後接觸」的位置(= travel + 列中那顆的對人半徑 + 假人半徑),量飛到該距離時的傷害
        private IEnumerator VerticalFlickAt(float travel, System.Action<float, bool, string> report)
        {
            RollerWeapon roller = Roller;
            _tank.InfiniteInk = true;
            yield return MoveTo(new Vector3(-12f, 0.1f, -23f));
            AimFlat(Vector3.forward, _config.VerticalSpawnHeight);
            yield return WaitSwingCycleEnded();
            yield return Frames(2);
            TargetDummy dummy = SpawnDummy(Flat(_player.transform.position) + Vector3.forward * 6f + Vector3.down * 8f);
            _intent.PressJump();
            float until = Time.time + 1f;
            while (_controller.isGrounded && Time.time < until)
            {
                yield return null;
            }
            yield return new WaitForSeconds(0.12f);
            int flicks = roller.FlickCount;
            int hitsBefore = _hits.Count;
            int impactsBefore = _impacts.Count;
            Random.State saved = Random.state;
            Random.InitState(104729);
            yield return Tap();
            yield return WaitUntil(() => roller.FlickCount > flicks, 2f);
            Random.state = saved;
            float radius = S3.Vertical.Units[1].PlayerRadiusEnd;
            float distance = travel + radius + DummyRadius();
            dummy.transform.position = roller.LastFlingOrigin + roller.LastFlingForward * distance - Vector3.up * 1f;
            Physics.SyncTransforms();
            yield return ReportFlick(true, dummy, distance, hitsBefore, impactsBefore, report);
        }

        // 上一下的甩動循環(S3 CycleFrames)還沒結束時按下會被忽略(RollerWeapon 待機但 _nextSwingReady 未到):
        // 量測前等循環結束再多留 3F,避免「上一步收尾 + 瞬移 + 放假人」剛好落在循環邊界(0.650 + 1F = 0.667 s)的競態
        private IEnumerator WaitSwingCycleEnded()
        {
            RollerWeapon roller = Roller;
            float cycle = Mathf.Max(S3.Horizontal.CycleFrames, S3.Vertical.CycleFrames) / 60f + 3f / 60f;
            yield return WaitUntil(() => roller.Action == RollerAction.Idle && Time.time - roller.LastSwingStartTime >= cycle,
                cycle + 1f);
        }

        // 甩出後等墨團飛完:回報該甩對假人的總傷害、是否擊倒、證據字串;
        // 另外核對:總傷害 = 命中各顆依 S3 曲線算出的最大值(中央/側邊曲線之間),不是加總
        private IEnumerator ReportFlick(bool vertical, TargetDummy dummy, float distance, int hitsBefore,
            int impactsBefore, System.Action<float, bool, string> report)
        {
            RollerWeapon roller = Roller;
            int attack = roller.LastFlingAttackId;
            yield return new WaitForSeconds(0.8f);
            float total = 0f;
            bool killed = false;
            int events = 0;
            for (int i = hitsBefore; i < _hits.Count; i++)
            {
                if (ReferenceEquals(_hits[i].Target, dummy))
                {
                    total += _hits[i].Amount;
                    killed |= _hits[i].Splatted;
                    events++;
                }
            }
            RollerFlingDamage rule = vertical ? S3.Vertical.Damage : S3.Horizontal.Damage;
            float maxInside = 0f;
            float maxOutside = 0f;
            int globs = 0;
            float nearest = float.MaxValue;
            for (int i = impactsBefore; i < _impacts.Count; i++)
            {
                InkBulletImpact impact = _impacts[i];
                if (impact.AttackId != attack || !ReferenceEquals(impact.Target, dummy))
                {
                    continue;
                }
                globs++;
                nearest = Mathf.Min(nearest, impact.ContactTravelled);
                maxInside = Mathf.Max(maxInside, rule.Evaluate(impact.ContactTravelled, impact.Frame, true, false));
                maxOutside = Mathf.Max(maxOutside, rule.Evaluate(impact.ContactTravelled, impact.Frame, false, false));
            }
            bool consistent = globs > 0 && total <= maxInside + 0.15f && total >= Mathf.Min(maxInside, maxOutside) - 0.15f
                              && total <= rule.Inside.MaxValue + 0.15f;
            _lastNearestTravel = globs > 0 ? nearest : 0f;
            _lastHitEvents = events;
            string log = $"假人 {distance:0.0} m:總傷 {total:0.0}({events} 次結算)擊倒={killed};命中墨團 {globs} 顆," +
                         $"最近飛行 {_lastNearestTravel:0.00} m,依 S3 曲線最高 中央 {maxInside:0.0} / 側邊 {maxOutside:0.0}";
            Check($"{S3.Name} {(vertical ? "直甩" : "橫甩")}傷害 = 命中墨團依 S3 距離曲線的最高值(不疊加)", consistent, log);
            report(total, killed, log);
            Despawn(dummy);
            yield return new WaitForSeconds(0.2f);
        }

        // ── 直甩(空中點按)──

        private IEnumerator MeasureVerticalFlick()
        {
            RollerWeapon roller = Roller;
            RollerSwingParams swing = S3.Vertical;
            _tank.InfiniteInk = false;
            _tank.RefillFull();
            yield return MoveTo(new Vector3(-20f, 0.1f, -20f));
            AimFlat(Vector3.forward, _config.VerticalSpawnHeight);
            yield return Frames(2);
            _intent.PressJump();
            float until = Time.time + 1f;
            while (_controller.isGrounded && Time.time < until)
            {
                yield return null;
            }
            yield return new WaitForSeconds(0.1f);
            int flicks = roller.FlickCount;
            float before = _tank.Normalized;
            yield return Tap();
            float sideReach = 0f;
            float deadline = Time.time + 3f;
            while (roller.FlickCount == flicks && Time.time < deadline)
            {
                if (roller.Model != null && roller.Action == RollerAction.Windup && roller.WindupProgress > 0.7f)
                {
                    Vector3 offset = roller.Model.DrumCenter - _player.transform.position;
                    sideReach = Mathf.Max(sideReach, Vector3.Dot(offset, _visual.right));
                }
                yield return null;
                TrackDelta();
            }
            float spent = before - _tank.Normalized;
            float delay = roller.LastFlingTime - roller.LastSwingStartTime;
            Check($"{S3.Name} 直甩(空中):舉起 {swing.SwingFrame}F = {swing.SwingSeconds:0.000} s、{swing.BulletCount(false)} 顆一列",
                roller.LastFlingVertical && roller.LastFlingBulletCount == swing.BulletCount(false)
                && Mathf.Abs(delay - swing.SwingSeconds) <= Tolerance,
                $"直甩={roller.LastFlingVertical} {roller.LastFlingBulletCount} 顆、實測 {delay:0.000} s");
            Check($"{S3.Name} 直甩耗墨 {swing.InkConsume * 100f:0.##}%", Mathf.Abs(spent - swing.InkConsume) < 1e-4f,
                $"實測 {spent * 100f:0.000}%");
            Check($"{S3.Name} 舉起姿勢:直甩把滾筒拉到身體右側", sideReach > 0.5f, $"滾筒軸心右移 {sideReach:0.00} m");
            float flingTime = roller.LastFlingTime;
            float level = _tank.Normalized;
            float unlocked = -1f;
            deadline = Time.time + swing.InkLockSeconds + 1f;
            while (Time.time < deadline && unlocked < 0f)
            {
                yield return null;
                TrackDelta();
                if (_tank.Normalized > level + 1e-5f)
                {
                    unlocked = Time.time;
                }
            }
            float lockTolerance = Mathf.Max(0.034f, _maxDeltaTime * 2f);
            Check($"{S3.Name} 直甩墨量鎖 {swing.InkRecoverStop}F = {swing.InkLockSeconds:0.000} s",
                unlocked > 0f && Mathf.Abs(unlocked - flingTime - swing.InkLockSeconds) <= lockTolerance,
                $"甩出後 {unlocked - flingTime:0.000} s 開始回墨");
            yield return WaitUntil(() => roller.Action == RollerAction.Idle && _controller.isGrounded, 2f);

            // 直甩的一擊必殺距離比橫甩長:飛行距離 = 橫甩必殺 + 30% 的差距(仍在直甩必殺內)→ 一擊
            float travel = S3.HorizontalLethalRange + (S3.VerticalLethalRange - S3.HorizontalLethalRange) * 0.3f;
            yield return VerticalFlickAt(travel, (damage, killed, log) =>
                Check($"{S3.Name} 直甩一擊距離較長:飛 {travel:0.0} m(> 橫甩必殺 {S3.HorizontalLethalRange:0.0})仍 ≥ 100",
                    damage >= 100f && killed && _lastNearestTravel > S3.HorizontalLethalRange, log));
            _tank.RefillFull();
        }

        // ── 甩動中的走速(SwingMoveSpeed)──

        private IEnumerator MeasureSwingMoveSpeed()
        {
            RollerWeapon roller = Roller;
            RollerSwingParams swing = S3.Horizontal;
            _tank.InfiniteInk = true;
            yield return MoveTo(new Vector3(-20f, 0.1f, -23f));
            AimFlat(Vector3.forward, _config.HorizontalSpawnHeight);
            _intent.Move = new Vector2(0f, 1f);
            yield return new WaitForSeconds(0.5f);
            yield return Tap();
            // 甩動循環(舉起 + 收招)全程都是甩動走速:從按下 0.12 s(跑速減到走速)量到循環結束前
            yield return new WaitForSeconds(0.12f);
            Vector3 p0 = _player.transform.position;
            float t0 = Time.time;
            float end = roller.LastSwingStartTime + swing.CycleFrames / 60f - 0.04f;
            while (Time.time < end && roller.Action != RollerAction.Idle)
            {
                yield return null;
                TrackDelta();
            }
            Vector3 p1 = _player.transform.position;
            float elapsed = Time.time - t0;
            _intent.Move = Vector2.zero;
            p1.y = p0.y;
            float speed = Vector3.Distance(p0, p1) / Mathf.Max(1e-3f, elapsed);
            float expected = swing.MoveSpeedPerSecond;
            Check($"{S3.Name} 甩動中走速 {swing.MoveSpeed} u/F = {expected:0.00} m/s",
                elapsed > 0.1f && Mathf.Abs(speed - expected) <= expected * 0.08f,
                $"實測 {speed:0.00} m/s({elapsed:0.00} s)");
            yield return new WaitForSeconds(0.3f);
        }

        // ── 推滾:起步延遲、普通/衝刺速度、DashFrame、塗寬、耗墨、急轉、墨量鎖 ──

        private IEnumerator MeasureRolling()
        {
            RollerWeapon roller = Roller;
            RollerRollParams roll = S3.Roll;
            _ground.ClearInk();
            _tank.InfiniteInk = true;
            _tank.RefillFull();
            Vector3 start = new Vector3(-9f, 0.1f, -22.5f);
            yield return MoveTo(start);
            // 先朝後方(−z)甩,墨團不落在要量塗寬的前方跑道;甩出後轉回 +z 推滾
            AimFlat(Vector3.back, _config.HorizontalSpawnHeight);
            yield return Frames(3);
            int flicks = roller.FlickCount;
            int rolls = roller.RollStartCount;
            _intent.Attack = true;
            yield return WaitUntil(() => roller.FlickCount > flicks, 2f);
            AimFlat(Vector3.forward, _config.HorizontalSpawnHeight);
            yield return WaitUntil(() => roller.RollStartCount > rolls, 1f);
            float startDelay = roller.RollStartTime - roller.LastFlingTime;
            Check($"{S3.Name} 按住:甩出後 {S3.RollStartDelayFrames}F 滾筒落地開始推滾 [EST]",
                roller.IsRolling && Mathf.Abs(startDelay - S3.RollStartDelayFrames / 60f) <= Tolerance,
                $"甩出後 {startDelay:0.000} s 推滾");

            // 站著推滾不動:不塗墨(先等甩出的墨團落地)
            yield return new WaitForSeconds(0.35f);
            float turf0 = _inkling.TurfInkedArea;
            int stamps0 = roller.RollStamps;
            yield return new WaitForSeconds(0.3f);
            Check($"{S3.Name} 推滾但沒移動:不塗墨", _inkling.TurfInkedArea - turf0 < 0.01f && roller.RollStamps == stamps0,
                $"0.3 s 塗 {_inkling.TurfInkedArea - turf0:0.00} m²、推滾印 {roller.RollStamps - stamps0} 次");

            var times = new List<float>();
            var positions = new List<Vector3>();
            float rollBegin = Time.time;
            _intent.Move = new Vector2(0f, 1f);
            float dashSpeedCap = roll.SpeedDash * 60f;
            bool inkMeasured = false;
            float inkRate = -1f;
            float inkT0 = -1f;
            float inkL0 = 0f;
            float drumMaxY = -10f;
            float drumMinY = 10f;
            float drumAhead = 0f;
            int drumSamples = 0;
            float twoHands = 1f;
            float deadline = Time.time + 6f;
            while (Time.time < deadline)
            {
                yield return null;
                TrackDelta();
                times.Add(Time.time);
                positions.Add(_player.transform.position);
                if (roller.Model != null && Time.time - rollBegin > 0.3f)
                {
                    Vector3 offset = roller.Model.DrumCenter - _player.transform.position;
                    drumMaxY = Mathf.Max(drumMaxY, offset.y);
                    drumMinY = Mathf.Min(drumMinY, offset.y);
                    drumAhead += Vector3.Dot(offset, _visual.forward);
                    drumSamples++;
                    twoHands = Mathf.Min(twoHands, _characterVisual != null ? _characterVisual.RollerTwoHandWeight : 0f);
                }
                if (roller.IsDashing && !inkMeasured)
                {
                    float sinceDash = Time.time - roller.DashStartTime;
                    if (inkT0 < 0f && sinceDash > 0.15f)
                    {
                        _tank.InfiniteInk = false;
                        _tank.RefillFull();
                        inkT0 = Time.time;
                        inkL0 = _tank.Normalized;
                    }
                    else if (inkT0 > 0f && Time.time - inkT0 >= 0.6f)
                    {
                        inkRate = (inkL0 - _tank.Normalized) / (Time.time - inkT0);
                        inkMeasured = true;
                        _tank.InfiniteInk = true;
                    }
                }
                if (inkMeasured && Time.time - roller.DashStartTime > 1.1f)
                {
                    break;
                }
            }
            float dashAt = roller.DashStartTime;
            float dashDelay = dashAt - rollBegin;
            float expectedDash = roll.DashFrame / 60f;
            Check($"{S3.Name} 連續推滾 {roll.DashFrame}F = {expectedDash:0.000} s 後衝刺",
                dashAt > 0f && dashDelay >= expectedDash - Tolerance && dashDelay <= expectedDash + 0.1f,
                $"開始移動後 {dashDelay:0.000} s 進入衝刺");

            // 普通速度:開始移動 0.12 s 後到衝刺前;衝刺速度:衝刺 0.2 s 後的 0.6 s
            float normalEnd = Mathf.Min(rollBegin + 1.2f, dashAt - 0.03f);
            float normal = AverageSpeed(times, positions, rollBegin + 0.12f, normalEnd);
            float dash = AverageSpeed(times, positions, dashAt + 0.2f, dashAt + 0.8f);
            float expectedNormal = roll.SpeedNormal * 60f;
            Check($"{S3.Name} 推滾速度 {roll.SpeedNormal} u/F = {expectedNormal:0.00} m/s(比跑速快也不夾)",
                normal > 0f && Mathf.Abs(normal - expectedNormal) <= expectedNormal * 0.06f,
                $"實測 {normal:0.00} m/s({normalEnd - rollBegin - 0.12f:0.00} s 區間)");
            Check($"{S3.Name} 衝刺速度 {roll.SpeedDash} u/F = {dashSpeedCap:0.00} m/s",
                Mathf.Abs(dash - dashSpeedCap) <= dashSpeedCap * 0.05f, $"實測 {dash:0.00} m/s");
            Check($"{S3.Name} 推滾耗墨隨速度(衝刺 {roll.InkPerFrame(roll.SpeedDash) * 6000f:0.0}%/s)",
                inkRate > 0f && Mathf.Abs(inkRate - roll.InkPerFrame(roll.SpeedDash) * 60f) <= 0.006f,
                $"實測 {inkRate * 100f:0.00}%/s");
            float drumAverage = drumSamples > 0 ? drumAhead / drumSamples : 0f;
            Check($"{S3.Name} 推滾姿勢:雙手推、滾筒壓在腳前 {S3.RollForward:0.00} m 的地面",
                drumSamples > 0 && drumMinY > 0.03f && drumMaxY < roller.Model.DrumRadius + 0.25f
                && Mathf.Abs(drumAverage - S3.RollForward) < 0.3f && twoHands > 0.8f,
                $"滾筒軸心離地 {drumMinY:0.00}–{drumMaxY:0.00} m、前距平均 {drumAverage:0.00} m、雙手權重 ≥ {twoHands:0.00}");

            // 急轉:衝刺中瞬間轉 90° → 掉回 SpeedDashTurnBreak、衝刺重算
            bool wasDashing = roller.IsDashing;
            AimFlat(Vector3.right, _config.HorizontalSpawnHeight);
            bool broke = false;
            for (int i = 0; i < 3 && !broke; i++)
            {
                yield return null;
                broke = !roller.IsDashing && roller.IsRolling;
            }
            float capAfter = roller.MoveSpeedWhileFiring;
            Check($"{S3.Name} 衝刺中急轉:掉回 {roll.SpeedDashTurnBreak * 60f:0.00} m/s、衝刺重算",
                wasDashing && broke && Mathf.Abs(capAfter - roll.SpeedDashTurnBreak * 60f) < 0.01f,
                $"轉前衝刺={wasDashing} 轉後衝刺={roller.IsDashing} 速度上限 {capAfter:0.00}");
            yield return new WaitForSeconds(0.15f);

            // 墨量鎖 20F:放開後最後一次耗墨起算
            _tank.InfiniteInk = false;
            yield return new WaitForSeconds(0.1f);
            _intent.Attack = false;
            _intent.Move = Vector2.zero;
            yield return null;
            float lastInk = roller.LastRollInkTime;
            float level = _tank.Normalized;
            float unlocked = -1f;
            deadline = Time.time + 1.5f;
            while (Time.time < deadline && unlocked < 0f)
            {
                yield return null;
                TrackDelta();
                if (_tank.Normalized > level + 1e-5f)
                {
                    unlocked = Time.time;
                }
            }
            float lockTolerance = Mathf.Max(0.034f, _maxDeltaTime * 2f);
            Check($"{S3.Name} 推滾墨量鎖 {roll.InkRecoverStop}F = {roll.InkLockSeconds:0.000} s",
                unlocked > 0f && Mathf.Abs(unlocked - lastInk - roll.InkLockSeconds) <= lockTolerance,
                $"最後一次推滾耗墨後 {unlocked - lastInk:0.000} s 開始回墨");

            // 塗寬:衝刺 0.35 s 時滾筒經過的那一排、以及衝刺前(普通速度)的一排
            float dashWidth = MeasureStripWidth(times, positions, dashAt + 0.35f, out Vector3 dashPoint);
            float expectedDashWidth = 2f * roll.PaintHalfWidth(roll.SpeedDash);
            Check($"{S3.Name} 衝刺推滾塗寬 {expectedDashWidth:0.0} m(2 × WidthHalfMax)",
                Mathf.Abs(dashWidth - expectedDashWidth) <= expectedDashWidth * 0.15f,
                $"實測 {dashWidth:0.00} m(z = {dashPoint.z:0.0})");
            if (roll.DashFrame >= 60)
            {
                float normalWidth = MeasureStripWidth(times, positions, rollBegin + 0.8f, out Vector3 normalPoint);
                float expectedNormalWidth = 2f * roll.PaintHalfWidth(roll.SpeedNormal);
                Check($"{S3.Name} 普通推滾塗寬 {expectedNormalWidth:0.0} m(隨速度)",
                    Mathf.Abs(normalWidth - expectedNormalWidth) <= expectedNormalWidth * 0.15f,
                    $"實測 {normalWidth:0.00} m(z = {normalPoint.z:0.0})");
            }
            _tank.InfiniteInk = true;
            _tank.RefillFull();
            yield return new WaitForSeconds(0.2f);
        }

        // ── 接觸傷害 ──

        private IEnumerator MeasureContact()
        {
            RollerWeapon roller = Roller;
            RollerRollParams roll = S3.Roll;
            _tank.InfiniteInk = true;
            yield return MoveTo(new Vector3(-16f, 0.1f, -23f));
            AimFlat(Vector3.forward, _config.HorizontalSpawnHeight);
            yield return Frames(2);
            int rolls = roller.RollStartCount;
            _intent.Attack = true;
            yield return WaitUntil(() => roller.RollStartCount > rolls, 2f);
            yield return new WaitForSeconds(0.5f);

            // 靜止的滾筒碰著假人:不傷人
            int contacts = roller.ContactHits;
            TargetDummy still = SpawnDummy(Flat(_player.transform.position) + Vector3.forward * (S3.RollForward + 0.5f));
            yield return new WaitForSeconds(0.4f);
            Check($"{S3.Name} 滾筒不動時碰到敵人不傷人", roller.ContactHits == contacts && still.IsAlive,
                $"接觸 {roller.ContactHits - contacts} 次");
            Despawn(still);
            yield return Frames(2);

            TargetDummy dummy = SpawnDummy(Flat(_player.transform.position) + Vector3.forward * 4.5f);
            int hitsBefore = _hits.Count;
            _intent.Move = new Vector2(0f, 1f);
            yield return WaitUntil(() => roller.ContactHits > contacts, 2.5f);
            float firstHit = Time.time;
            float bounce = 0f;
            for (int i = 0; i < 4; i++)
            {
                bounce = Mathf.Min(bounce, Vector3.Dot(_locomotion.ExternalVelocity, Vector3.forward));
                yield return null;
            }
            float first = FirstHitOn(dummy, hitsBefore, out bool splatted);
            bool lethal = roll.Damage >= 100f;
            Check($"{S3.Name} 推滾接觸傷害 {roll.Damage}" + (lethal ? "(一擊)" : "(打不死、自己被彈回)"),
                roller.ContactHits > contacts && Mathf.Abs(first - roll.Damage) < 0.01f && splatted == lethal
                && (lethal || bounce < -2.5f),
                $"傷害 {first:0.0} 擊倒={splatted} 自己的反彈速度 {bounce:0.00} m/s");
            if (!lethal)
            {
                // Carbon:間隔 ContactRehitFrames 後再撞一次 → 70 + 70 擊倒
                yield return WaitUntil(() => roller.ContactHits > contacts + 1 || !dummy.IsAlive, 3f);
                float second = Time.time;
                Check($"{S3.Name} 推滾再撞一次({S3.ContactRehitFrames}F 間隔)擊倒",
                    !dummy.IsAlive && second - firstHit >= S3.ContactRehitFrames / 60f - Tolerance,
                    $"第二次接觸在 {second - firstHit:0.000} s 後,假人存活={dummy.IsAlive}");
            }
            _intent.Move = Vector2.zero;
            _intent.Attack = false;
            Despawn(dummy);
            yield return new WaitForSeconds(0.3f);
        }

        // ── 對物件倍率:甩墨 RollerSplash(_Heavy)× 2.88 灑水器、推滾 RollerCore × 100 ──

        private IEnumerator MeasureDeviceRates()
        {
            RollerWeapon roller = Roller;
            _tank.InfiniteInk = true;
            yield return MoveTo(new Vector3(-6f, 0.1f, -22f));
            AimFlat(Vector3.forward, _config.HorizontalSpawnHeight);
            yield return Frames(2);
            Vector3 feet = Flat(_player.transform.position);
            TestDeployable device = TestDeployable.Create("RollerTestSprinkler",
                feet + Vector3.forward * (_config.HorizontalSpawnForward + 1.8f) + Vector3.up * 0.5f, Vector3.one);
            device.Setup(null, Team.Bravo, 10000f, TargetClass.Sprinkler, false);
            _spawned.Add(device.gameObject);
            yield return Frames(2);
            int flicks = roller.FlickCount;
            yield return Tap();
            yield return WaitUntil(() => roller.FlickCount > flicks, 2f);
            yield return new WaitForSeconds(0.6f);
            float rate = DamageRates.Get(_config.AttackClass, TargetClass.Sprinkler);
            float expected = S3.Horizontal.Damage.Inside.MaxValue * rate;
            float dealt = 10000f - device.Hp;
            Check($"{S3.Name} 甩墨打灑水器 × {rate}({_config.AttackClass})",
                Mathf.Abs(dealt - expected) < 0.6f, $"扣 {dealt:0.0}(期望 {expected:0.0})");
            _spawned.Remove(device.gameObject);
            Destroy(device.gameObject);
            yield return WaitUntil(() => roller.Action == RollerAction.Idle, 2f);

            // 推滾輾過:RollerContact 對灑水器 × 100(一碰就壞)
            int rolls = roller.RollStartCount;
            _intent.Attack = true;
            yield return WaitUntil(() => roller.RollStartCount > rolls, 2f);
            yield return new WaitForSeconds(0.3f);
            feet = Flat(_player.transform.position);
            TestDeployable second = TestDeployable.Create("RollerTestSprinkler2", feet + Vector3.forward * 4f + Vector3.up * 0.5f,
                Vector3.one);
            second.Setup(null, Team.Bravo, 5000f, TargetClass.Sprinkler, false);
            _spawned.Add(second.gameObject);
            _intent.Move = new Vector2(0f, 1f);
            yield return WaitUntil(() => second == null || second.IsBroken, 2.5f);
            bool broken = second == null || second.IsBroken;
            float contactRate = DamageRates.Get(AttackClass.RollerContact, TargetClass.Sprinkler);
            Check($"{S3.Name} 推滾輾過灑水器 × {contactRate}({S3.Roll.Damage} × {contactRate} ≥ 5000 HP,一碰就壞)",
                broken && contactRate >= 100f, $"壞了={broken}");
            _intent.Move = Vector2.zero;
            _intent.Attack = false;
            yield return new WaitForSeconds(0.3f);
        }

        // ── 墨不足:弱甩(耗墨 × 0.5、顆數變少)/ 連弱甩都不夠 = 空甩 ──

        private IEnumerator MeasureDepletion()
        {
            RollerWeapon roller = Roller;
            RollerSwingParams swing = S3.Horizontal;
            yield return MoveTo(new Vector3(-20f, 0.1f, -12f));
            AimFlat(Vector3.forward, _config.HorizontalSpawnHeight);
            yield return WaitUntil(() => roller.Action == RollerAction.Idle, 2f);
            _tank.InfiniteInk = false;
            SetTankLevel(swing.InkConsume * 0.8f);
            int flicks = roller.FlickCount;
            float before = _tank.Normalized;
            yield return Tap();
            yield return WaitUntil(() => roller.FlickCount > flicks, 2f);
            float spent = before - _tank.Normalized;
            Check($"{S3.Name} 墨不足一甩 → 弱甩:耗墨 × 0.5、{swing.BulletCount(true)} 顆",
                roller.LastFlingDepleted && roller.LastFlingBulletCount == swing.BulletCount(true)
                && Mathf.Abs(spent - swing.InkConsume * 0.5f) < 1e-4f,
                $"弱甩={roller.LastFlingDepleted} {roller.LastFlingBulletCount} 顆、耗墨 {spent * 100f:0.000}%");
            yield return WaitUntil(() => roller.Action == RollerAction.Idle, 2f);
            SetTankLevel(swing.InkConsume * 0.3f);
            int failed = roller.FailedFlickCount;
            flicks = roller.FlickCount;
            before = _tank.Normalized;
            yield return Tap();
            yield return new WaitForSeconds(swing.SwingSeconds + 0.2f);
            Check($"{S3.Name} 連弱甩都不夠:空甩(不出墨、不扣墨)",
                roller.FailedFlickCount == failed + 1 && roller.FlickCount == flicks
                && Mathf.Abs(before - _tank.Normalized) < 1e-4f,
                $"空甩 {roller.FailedFlickCount - failed} 次、墨 {before * 100f:0.00}% → {_tank.Normalized * 100f:0.00}%");
            _tank.RefillFull();
            yield return WaitUntil(() => roller.Action == RollerAction.Idle, 2f);
        }

        // ── bot 式:以 BotAttackIntent 決定按鍵,甩死必殺距離內的假人;BotPaintIntent 按住推滾塗地 ──

        private IEnumerator MeasureBotStyle()
        {
            RollerWeapon roller = Roller;
            _tank.InfiniteInk = true;
            yield return MoveTo(new Vector3(-14f, 0.1f, -22f));
            AimFlat(Vector3.forward, _config.HorizontalSpawnHeight);
            yield return Frames(2);
            float distance = Mathf.Max(_config.BotRollCommitDistance + 0.8f, roller.EffectiveRange * 0.8f);
            TargetDummy dummy = SpawnDummy(Flat(_player.transform.position) + Vector3.forward * distance);
            yield return Frames(2);
            int flicks = roller.FlickCount;
            int rolls = roller.RollStartCount;
            int kills = dummy.KillCount;
            float end = Time.time + 2.5f;
            int pressed = 0;
            while (Time.time < end && dummy.KillCount == kills)
            {
                AimAt(DummyCenter(dummy), _config.HorizontalSpawnHeight);
                var context = new BotCombatContext
                {
                    Distance = Vector3.Distance(_player.transform.position, dummy.transform.position),
                    TargetVisible = true,
                    Reacted = true,
                    AimError = 0f,
                    FireTolerance = 4f,
                };
                _intent.Attack = roller.BotAttackIntent(context);
                pressed += _intent.Attack ? 1 : 0;
                yield return null;
            }
            _intent.Attack = false;
            Check($"{S3.Name} bot 掛勾:{distance:0.0} m 的敵人點按甩墨擊倒(不推滾)",
                dummy.KillCount > kills && roller.FlickCount > flicks && roller.RollStartCount == rolls,
                $"按 {pressed} 幀、甩 {roller.FlickCount - flicks} 下、推滾 {roller.RollStartCount - rolls} 次");
            Despawn(dummy);
            yield return WaitUntil(() => roller.Action == RollerAction.Idle, 2f);

            // 塗地:看腳前 BotPaintAimDistance、依 BotPaintIntent 按住,邊走邊推滾
            _ground.ClearInk();
            yield return MoveTo(new Vector3(-22f, 0.1f, -21f));
            float pitch = Mathf.Atan2(-_config.HorizontalSpawnHeight, roller.BotPaintAimDistance);
            AimFlat(new Vector3(0f, Mathf.Sin(pitch), Mathf.Cos(pitch)), _config.HorizontalSpawnHeight);
            float turf = _inkling.TurfInkedArea;
            float rolled = roller.RolledDistance;
            rolls = roller.RollStartCount;
            _intent.Move = new Vector2(0f, 1f);
            // 甩完(Dynamo 舉起 45F)開始推滾後再滾 1.4 s(重量級推滾 5.28 m/s 也有 5 m 以上)
            end = Time.time + 4f;
            float rollBegan = -1f;
            while (Time.time < end)
            {
                _intent.Attack = roller.BotPaintIntent(_tank.Normalized);
                if (rollBegan < 0f && roller.RollStartCount > rolls)
                {
                    rollBegan = Time.time;
                }
                if (rollBegan > 0f && Time.time - rollBegan >= 1.4f)
                {
                    break;
                }
                yield return null;
            }
            _intent.Attack = false;
            _intent.Move = Vector2.zero;
            float painted = _inkling.TurfInkedArea - turf;
            Check($"{S3.Name} bot 塗地掛勾:按住推滾塗出一條路", roller.RolledDistance - rolled > 5f && painted > 15f,
                $"推滾 {roller.RolledDistance - rolled:0.0} m、塗 {TurfPoints.FromArea(painted):0}p({painted:0} m²)");
            yield return new WaitForSeconds(0.3f);
        }

        // ── 潛墨:滾筒跟人形模型一起消失、浮出再出現 ──

        private IEnumerator MeasureSquidHidesModel()
        {
            RollerWeapon roller = Roller;
            yield return MoveTo(new Vector3(-18f, 0.1f, -4f));
            InkPainter.PaintSphere(new Vector3(-18f, 0f, -4f), 3f, 1f, _inkling.InkSource(false));
            yield return Frames(3);
            _intent.Squid = true;
            yield return new WaitForSeconds(0.6f);
            bool hidden = roller.Model != null && !roller.Model.Visible;
            _intent.Squid = false;
            yield return new WaitForSeconds(0.6f);
            bool shown = roller.Model != null && roller.Model.Visible;
            Check($"{S3.Name} 潛墨時滾筒一起隱形、浮出後出現", hidden && shown, $"潛墨隱藏={hidden} 浮出顯示={shown}");
        }

        // ── 收尾:換回 Splattershot,內建槍身恢復、滾筒模型移除 ──

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
                RollerModel model = Roller != null ? Roller.Model : null;
                _loadout.Equip(_baseKit);
                Renderer gun = BuiltInGun();
                Check($"{S3.Name} 換回 Splattershot:內建槍身恢復、滾筒模型移除",
                    !(_loadout.Main is RollerWeapon) && gun != null && !gun.forceRenderingOff,
                    $"主武器={(_loadout.Main != null ? _loadout.Main.GetType().Name : "無")} 槍身隱藏={(gun != null && gun.forceRenderingOff)} " +
                    $"滾筒模型待銷毀={(model != null)}");
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
            _aim = null;
            if (_player != null)
            {
                Teleport(_player.transform, new Vector3(0f, 0.1f, 0f));
            }
        }

        // ── 小工具 ──

        private IEnumerator Tap()
        {
            _intent.Attack = true;
            yield return null;
            _intent.Attack = false;
        }

        // 瞬移後等落地站穩(空中按 = 直甩)
        private IEnumerator MoveTo(Vector3 position)
        {
            _intent.Attack = false;
            _intent.Move = Vector2.zero;
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

        private float FirstHitOn(IDamageable target, int from, out bool splatted)
        {
            splatted = false;
            for (int i = from; i < _hits.Count; i++)
            {
                if (ReferenceEquals(_hits[i].Target, target))
                {
                    splatted = _hits[i].Splatted;
                    return _hits[i].Amount;
                }
            }
            return -1f;
        }

        private static float AverageSpeed(List<float> times, List<Vector3> positions, float from, float to)
        {
            int a = -1;
            int b = -1;
            for (int i = 0; i < times.Count; i++)
            {
                if (a < 0 && times[i] >= from)
                {
                    a = i;
                }
                if (times[i] <= to)
                {
                    b = i;
                }
            }
            if (a < 0 || b <= a)
            {
                return -1f;
            }
            Vector3 delta = positions[b] - positions[a];
            delta.y = 0f;
            return delta.magnitude / Mathf.Max(1e-3f, times[b] - times[a]);
        }

        // 在 time 時滾筒軸心經過的那一排(垂直推滾方向 = x 軸)量連續己方墨的寬度
        private float MeasureStripWidth(List<float> times, List<Vector3> positions, float time, out Vector3 point)
        {
            point = positions.Count > 0 ? positions[positions.Count - 1] : Vector3.zero;
            for (int i = 0; i < times.Count; i++)
            {
                if (times[i] >= time)
                {
                    point = positions[i];
                    break;
                }
            }
            point += Vector3.forward * S3.RollForward;
            byte team = (byte)_inkling.Team;
            float center = point.x;
            float step = 0.05f;
            float left = 0f;
            float right = 0f;
            for (float x = 0f; x < 6f; x += step)
            {
                if (_ground.SampleOwnership(new Vector3(center - x, 0f, point.z), Vector3.up) != team)
                {
                    break;
                }
                left = x;
            }
            for (float x = 0f; x < 6f; x += step)
            {
                if (_ground.SampleOwnership(new Vector3(center + x, 0f, point.z), Vector3.up) != team)
                {
                    break;
                }
                right = x;
            }
            return left + right;
        }

        private float DummyRadius()
        {
            var dummy = _dummyTemplate.GetComponent<TargetDummy>();
            return dummy != null ? dummy.BodyRadius : 0.4f;
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
            go.name = "RollerTestDummy";
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

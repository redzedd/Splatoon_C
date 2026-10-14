using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SplatoonC.Core;
using SplatoonC.Gameplay.AI;
using SplatoonC.Gameplay.CameraRig;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Match;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging.Recording
{
    // 作品集影片用的錄影工具(只錄影、不改遊戲邏輯):在對戰場景(TurfWar_ScorchGorge)Play 中執行
    //   ShowcaseRecorder.Run("main_splattershot,move_swim,...") 或 Run("all")
    // 其他 7 個 bot 凍結;本機玩家由 ScriptedIntent 驅動、鏡頭每幀轉向目標;
    // 遊戲時間固定 1/60 秒一幀(Time.captureDeltaTime),每幀把 Game view(含 HUD)存成 PNG。
    public sealed class ShowcaseRecorder : MonoBehaviour
    {
        public static string OutRoot = "C:/Users/redze/Desktop/作品集/影片/_splatoon_frames";

        private enum Fire { Hold, Pulse, Roller, Charger }

        private sealed class MainShot
        {
            public string Name, Kit; public Fire Fire; public float Dist; public bool Strafe;
            public MainShot(string name, string kit, Fire fire, float dist, bool strafe) { Name = name; Kit = kit; Fire = fire; Dist = dist; Strafe = strafe; }
        }

        private static readonly MainShot[] Mains =
        {
            new MainShot("main_splattershot", "splattershot", Fire.Hold, 4.5f, true),
            new MainShot("main_splattershot_jr", "splattershot_jr", Fire.Hold, 4f, true),
            new MainShot("main_52_gal", "52_gal", Fire.Hold, 4.5f, true),
            new MainShot("main_n_zap_85", "n_zap_85", Fire.Hold, 4.5f, true),
            new MainShot("main_splattershot_pro", "splattershot_pro", Fire.Hold, 6f, true),
            new MainShot("main_splat_dualies", "splat_dualies", Fire.Hold, 4.5f, true),
            new MainShot("main_blaster", "blaster", Fire.Pulse, 4.5f, false),
            new MainShot("main_luna_blaster", "luna_blaster_neo", Fire.Pulse, 3.5f, false),
            new MainShot("main_splat_roller", "splat_roller", Fire.Roller, 6f, false),
            new MainShot("main_carbon_roller", "carbon_roller_deco", Fire.Roller, 6f, false),
            new MainShot("main_dynamo_roller", "starz_dynamo_roller", Fire.Roller, 7f, false),
            new MainShot("main_splat_charger", "splat_charger", Fire.Charger, 15f, false),
            new MainShot("main_e_liter_4k", "e_liter_4k", Fire.Charger, 18f, false),
        };

        // 副武器 → 帶這個副武器的正式組合;finish = 副武器打不死時用主武器補完
        private static readonly (string name, string kit, Fire fire, float dist, bool damaging)[] Subs =
        {
            ("sub_splat_bomb", "tentatek_splattershot", Fire.Hold, 7f, true),
            ("sub_suction_bomb", "splattershot", Fire.Hold, 7f, true),
            ("sub_burst_bomb", "carbon_roller_deco", Fire.Roller, 5f, true),
            ("sub_curling_bomb", "splat_roller", Fire.Roller, 7f, true),
            ("sub_autobomb", "blaster", Fire.Pulse, 7f, true),
            ("sub_torpedo", "custom_splattershot_jr", Fire.Hold, 8f, true),
            ("sub_fizzy_bomb", "luna_blaster_neo", Fire.Pulse, 6f, true),
            ("sub_angle_shooter", "splattershot_pro", Fire.Hold, 7f, true),
            ("sub_point_sensor", "starz_dynamo_roller", Fire.Roller, 9f, false),
            ("sub_squid_beakon", "e_liter_4k", Fire.Charger, 6f, false),
        };

        private static readonly string[] Specials =
        {
            "special_trizooka", "special_killer_wail", "special_crab_tank", "special_ultra_stamp", "special_ink_vac",
            "special_wave_breaker", "special_triple_inkstrike", "special_big_bubbler", "special_tacticooler",
        };

        private static readonly string[] Moves = { "move_swim", "move_climb", "move_roll", "move_surge", "move_superjump" };

        public static string LastStatus = "";

        public static void Run(string shots)
        {
            if (!Application.isPlaying) { Debug.LogError("[REC] 需在 Play mode"); return; }
            var go = new GameObject("ShowcaseRecorder");
            var rec = go.AddComponent<ShowcaseRecorder>();
            rec._queue = new List<string>();
            if (shots == "all" || string.IsNullOrEmpty(shots))
            {
                foreach (var m in Mains) rec._queue.Add(m.Name);
                foreach (var s in Subs) rec._queue.Add(s.name);
                rec._queue.AddRange(Specials);
                rec._queue.AddRange(Moves);
            }
            else
            {
                foreach (string s in shots.Split(',')) if (s.Trim().Length > 0) rec._queue.Add(s.Trim());
            }
        }

        private List<string> _queue;
        private MatchController _match;
        private Inkling _me;
        private ScriptedIntent _in;
        private ThirdPersonCameraRig _rig;
        private Inkling _target;
        private ScriptedIntent _tin;
        private readonly Dictionary<Inkling, ScriptedIntent> _botIntents = new Dictionary<Inkling, ScriptedIntent>();
        private readonly List<Inkling> _mates = new List<Inkling>();
        private WeaponCatalog _catalog;

        private bool _recording;
        private string _shotDir;
        private int _frame;
        private readonly List<Task> _writes = new List<Task>();

        private static readonly Vector3 Origin = new Vector3(-4f, 0.1f, -40f);

        private IEnumerator Start()
        {
            Time.captureDeltaTime = 1f / 60f;
            while (MatchController.Instance == null || MatchController.Instance.LocalPlayer == null) yield return null;
            _match = MatchController.Instance;
            _match.DebugSetRemaining(175f);
            yield return null;
            _me = _match.LocalPlayer;
            _in = ScriptedIntent.Drive(_me);
            _rig = Camera.main.GetComponent<ThirdPersonCameraRig>();
            _catalog = WeaponCatalog.Load();
            _me.GetComponent<InklingHealth>().Invulnerable = true;
            foreach (Inkling p in _match.Roster)
            {
                if (p == _me) continue;
                var brain = p.GetComponent<BotBrain>();
                if (brain != null) brain.enabled = false;
                _botIntents[p] = ScriptedIntent.Drive(p);
                if (p.Team == _me.Team) _mates.Add(p);
                else if (_target == null) _target = p;
            }
            _tin = _botIntents[_target];
            yield return new WaitForSeconds(0.5f);
            StartCoroutine(CaptureLoop());

            foreach (string shot in _queue)
            {
                LastStatus = "running " + shot;
                Debug.Log("[REC] start " + shot);
                IEnumerator routine = null;
                if (shot.StartsWith("main_")) { foreach (var m in Mains) if (m.Name == shot) routine = MainKill(m); }
                else if (shot.StartsWith("sub_")) { foreach (var s in Subs) if (s.name == shot) routine = SubShot(s.name, s.kit, s.fire, s.dist, s.damaging); }
                else if (shot.StartsWith("special_")) routine = SpecialShot(shot.Substring(8));
                else if (shot.StartsWith("move_")) routine = MoveShot(shot.Substring(5));
                if (routine == null) { Debug.LogError("[REC] unknown shot " + shot); continue; }
                yield return routine;
                StopRec();
                ResetIntents();
                yield return FlushWrites();
                Debug.Log("[REC] done " + shot + " frames=" + _frame);
            }
            Time.captureDeltaTime = 0f;
            LastStatus = "DONE";
            Debug.Log("[REC] DONE");
            Destroy(gameObject);
        }

        // ---------------------------------------------------------------- capture
        private IEnumerator CaptureLoop()
        {
            var eof = new WaitForEndOfFrame();
            while (true)
            {
                yield return eof;
                if (!_recording) continue;
                Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
                byte[] png = tex.EncodeToPNG();
                Destroy(tex);
                string path = Path.Combine(_shotDir, _frame.ToString("00000") + ".png");
                _frame++;
                _writes.Add(Task.Run(() => File.WriteAllBytes(path, png)));
            }
        }

        private void StartRec(string name)
        {
            _shotDir = Path.Combine(OutRoot, name);
            if (Directory.Exists(_shotDir)) foreach (string f in Directory.GetFiles(_shotDir, "*.png")) File.Delete(f);
            Directory.CreateDirectory(_shotDir);
            _frame = 0;
            _recording = true;
        }

        private void StopRec() { _recording = false; }

        private IEnumerator FlushWrites()
        {
            while (_writes.Exists(t => !t.IsCompleted)) yield return null;
            _writes.Clear();
        }

        // ---------------------------------------------------------------- helpers
        private void ResetIntents()
        {
            _in.Move = Vector2.zero; _in.Look = Vector2.zero; _in.Attack = false; _in.Squid = false; _in.Sub = false; _in.JumpHold = true;
            foreach (var kv in _botIntents)
            {
                ScriptedIntent b = kv.Value;
                b.Move = Vector2.zero; b.Attack = false; b.Squid = false; b.Sub = false; b.JumpHold = true;
            }
        }

        private static void ClearInk()
        {
            foreach (PaintableSurface s in PaintableSurface.Active) s.ClearInk();
        }

        private void Equip(string kitId)
        {
            WeaponKit kit = _catalog.Find(kitId);
            if (kit == null) { Debug.LogError("[REC] no kit " + kitId); return; }
            _me.GetComponent<WeaponLoadout>().Equip(kit);
            var tank = _me.GetComponent<PlayerInkTank>();
            tank.InfiniteInk = true;
            tank.RefillFull();
        }

        private static float Wrap(float a) { a %= 360f; if (a > 180f) a -= 360f; if (a < -180f) a += 360f; return a; }

        private Vector3 Chest(Inkling who)
        {
            var cc = who.GetComponent<CharacterController>();
            return cc != null ? cc.bounds.center + Vector3.up * 0.15f : who.transform.position + Vector3.up * 0.9f;
        }

        // 鏡頭(準心射線)平滑轉向某一點
        private void AimAt(Vector3 point, float smooth = 0.25f)
        {
            Vector3 d = point - _rig.AimOrigin;
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            float cy = Mathf.LerpAngle(_rig.Yaw, yaw, smooth);
            float cp = Mathf.Lerp(Wrap(_rig.transform.eulerAngles.x), pitch, smooth);
            _rig.SetAngles(cy, cp);
        }

        private void SetView(float yaw, float pitch) { _rig.SetAngles(yaw, pitch); }

        private IEnumerator PlaceTarget(Vector3 pos)
        {
            var life = _target.GetComponent<InklingLifecycle>();
            if (life.IsDead) { life.ForceRespawnNow(); yield return null; yield return null; }
            Vector3 toMe = _me.transform.position - pos; toMe.y = 0f;
            _target.Teleport(pos, Mathf.Atan2(toMe.x, toMe.z) * Mathf.Rad2Deg);
            yield return null;
            var hp = _target.GetComponent<InklingHealth>();
            hp.Invulnerable = false;
            hp.ClearArmor();
            BotAimView aim = _target.AimView as BotAimView;
            if (aim != null) aim.LookAt(Chest(_me));
        }

        private bool TargetDead => _target.GetComponent<InklingLifecycle>().IsDead;

        private IEnumerator Setup(string kit, float dist, float playerYaw = 0f)
        {
            ResetIntents();
            _match.DebugSetRemaining(170f);
            ClearInk();
            yield return Hold(2.5f);
            if (kit != null) Equip(kit);
            _me.Teleport(Origin, playerYaw);
            SetView(playerYaw, 8f);
            Vector3 fwd = Quaternion.Euler(0f, playerYaw, 0f) * Vector3.forward;
            yield return PlaceTarget(Origin + fwd * dist);
            for (int i = 0; i < 20; i++) { AimAt(Chest(_target), 1f); yield return null; }
        }

        private IEnumerator Hold(float seconds, Action each = null)
        {
            float t = 0f;
            while (t < seconds) { each?.Invoke(); t += Time.deltaTime; yield return null; }
        }

        // 主武器打到目標倒下(或逾時)
        private IEnumerator FireUntilKill(Fire fire, float timeout, bool strafe)
        {
            float t = 0f; int f = 0; bool releasing = false; int releaseFrames = 0;
            var charger = _me.GetComponentInChildren<ChargerWeapon>();
            while (!TargetDead && t < timeout)
            {
                AimAt(Chest(_target), 0.35f);
                if (strafe) _tin.Move = new Vector2(Mathf.Sin(t * 2.2f) * 0.55f, 0f);
                float dist = Vector3.Distance(_me.transform.position, _target.transform.position);
                switch (fire)
                {
                    case Fire.Hold: _in.Attack = true; break;
                    case Fire.Pulse: _in.Attack = (f % 34) < 4; break;
                    case Fire.Roller:
                        _in.Move = dist > 2.6f ? new Vector2(0f, 1f) : Vector2.zero;
                        _in.Attack = dist < 3.4f && (f % 40) < 4;
                        break;
                    case Fire.Charger:
                        if (releasing)
                        {
                            _in.Attack = false;
                            if (--releaseFrames <= 0) releasing = false;
                        }
                        else
                        {
                            _in.Attack = true;
                            if (charger != null && charger.IsFullyCharged) { releasing = true; releaseFrames = 18; _in.Attack = false; }
                        }
                        break;
                }
                f++; t += Time.deltaTime;
                yield return null;
            }
            _in.Attack = false; _in.Move = Vector2.zero; _tin.Move = Vector2.zero;
            if (!TargetDead) Debug.LogWarning("[REC] target survived");
        }

        private IEnumerator Aftermath(float seconds)
        {
            yield return Hold(seconds, () => { if (!TargetDead) AimAt(Chest(_target), 0.2f); });
        }

        // ---------------------------------------------------------------- shots
        private IEnumerator MainKill(MainShot m)
        {
            yield return Setup(m.Kit, m.Dist);
            StartRec(m.Name);
            yield return Hold(0.6f, () => AimAt(Chest(_target), 0.3f));
            yield return FireUntilKill(m.Fire, 9f, m.Strafe);
            yield return Aftermath(1.8f);
        }

        // 投擲:按住副武器鍵,依預測落點修正仰角,放開丟出
        private IEnumerator Throw(Vector3 groundTarget, int holdFrames, bool flat)
        {
            var thrower = _me.GetComponent<ThrowController>();
            Vector3 flatDir = groundTarget - _me.transform.position; flatDir.y = 0f;
            float yaw = Mathf.Atan2(flatDir.x, flatDir.z) * Mathf.Rad2Deg;
            float pitch = flat ? 2f : -10f;
            _in.Sub = true;
            for (int i = 0; i < holdFrames; i++)
            {
                if (!flat && thrower != null && thrower.HasPredictedLanding)
                {
                    Vector3 land = thrower.PredictedLanding - _me.transform.position; land.y = 0f;
                    float err = flatDir.magnitude - Vector3.Dot(land, flatDir.normalized);
                    pitch = Mathf.Clamp(pitch - err * 1.2f, -45f, 30f);
                }
                _rig.SetAngles(Mathf.LerpAngle(_rig.Yaw, yaw, 0.4f), Mathf.Lerp(Wrap(_rig.transform.eulerAngles.x), pitch, 0.4f));
                yield return null;
            }
            _in.Sub = false;
            yield return null;
        }

        private IEnumerator SubShot(string name, string kit, Fire fire, float dist, bool damaging)
        {
            yield return Setup(kit, dist);
            StartRec(name);
            yield return Hold(0.5f, () => AimAt(Chest(_target), 0.3f));
            Vector3 ground = _target.transform.position;
            if (name == "sub_squid_beakon")
            {
                // 信標:放在腳前方,鏡頭轉過去看
                yield return Throw(_me.transform.position + _me.transform.forward * 2.5f, 20, false);
                yield return Hold(2.5f);
                yield break;
            }
            bool flat = name == "sub_curling_bomb";
            yield return Throw(ground, flat ? 10 : 24, flat);
            if (!damaging)
            {
                yield return Hold(3f, () => AimAt(Chest(_target), 0.15f));
                yield break;
            }
            yield return Hold(3.2f, () => { if (!TargetDead) AimAt(Chest(_target), 0.2f); }, () => TargetDead);
            if (!TargetDead) yield return FireUntilKill(fire, 8f, false);
            yield return Aftermath(1.8f);
        }

        private IEnumerator Hold(float seconds, Action each, Func<bool> stop)
        {
            float t = 0f;
            while (t < seconds && !stop()) { each?.Invoke(); t += Time.deltaTime; yield return null; }
        }

        private IEnumerator SpecialShot(string id)
        {
            string kitId = id == "triple_inkstrike" ? "tentatek_splattershot" : "splattershot";
            float dist = id switch
            {
                "trizooka" => 12f, "killer_wail" => 7f, "crab_tank" => 9f, "ultra_stamp" => 7f, "ink_vac" => 8f,
                "wave_breaker" => 5f, "triple_inkstrike" => 9f, "big_bubbler" => 8f, _ => 8f,
            };
            yield return Setup(kitId, dist);
            if (id != "triple_inkstrike")
            {
                var cfg = Resources.Load<SpecialWeaponConfig>("Weapons/Special_" + id);
                if (cfg == null) { Debug.LogError("[REC] no special " + id); yield break; }
                SpecialTestKit.EquipSpecial(_me, cfg);
            }
            var gauge = _me.GetComponent<PlayerSpecialGauge>();
            gauge.SetPointsForTest(100f);
            yield return null;
            StartRec("special_" + id);
            yield return Hold(0.6f, () => AimAt(Chest(_target), 0.3f));
            _in.PressSpecial();
            yield return null;
            switch (id)
            {
                case "trizooka":
                    yield return Hold(0.45f, () => AimAt(Chest(_target), 0.3f));
                    _in.Attack = true;
                    yield return Hold(5f, () => AimAt(Chest(_target), 0.3f), () => TargetDead);
                    _in.Attack = false;
                    break;
                case "killer_wail":
                    yield return Hold(4.5f, () => { if (!TargetDead) AimAt(Chest(_target), 0.2f); });
                    break;
                case "crab_tank":
                    yield return Hold(0.7f, () => AimAt(Chest(_target), 0.3f));
                    _in.Attack = true;
                    yield return Hold(6f, () => AimAt(Chest(_target), 0.3f), () => TargetDead);
                    _in.Attack = false;
                    break;
                case "ultra_stamp":
                    yield return Hold(0.6f, () => AimAt(Chest(_target), 0.3f));
                    _in.Attack = true;
                    yield return Hold(6f, () => AimAt(Chest(_target), 0.3f), () => TargetDead);
                    _in.Attack = false;
                    break;
                case "ink_vac":
                    // 敵人朝我們射墨,我們吸進來再噴回去
                    _tin.Attack = true;
                    yield return Hold(0.4f, () => { AimAt(Chest(_target), 0.3f); TargetAimAtMe(); });
                    _in.Attack = true;
                    yield return Hold(3.2f, () => { AimAt(Chest(_target), 0.3f); TargetAimAtMe(); });
                    _tin.Attack = false;
                    _in.Attack = false;
                    yield return Hold(3f, () => { if (!TargetDead) AimAt(Chest(_target), 0.3f); }, () => TargetDead);
                    break;
                case "wave_breaker":
                    yield return Hold(0.4f, () => AimAt(Chest(_target), 0.3f));
                    yield return Throw(_target.transform.position, 20, false);
                    yield return Hold(3.5f, () => AimAt(Chest(_target), 0.2f), () => TargetDead);
                    if (!TargetDead) yield return FireUntilKill(Fire.Hold, 6f, false);
                    break;
                case "triple_inkstrike":
                    yield return Hold(0.4f, () => AimAt(Chest(_target), 0.3f));
                    for (int i = 0; i < 3; i++)
                    {
                        Vector3 spot = _target.transform.position + new Vector3((i - 1) * 3.5f, 0f, 0f);
                        yield return Throw(spot, 16, false);
                        yield return Hold(0.35f);
                    }
                    yield return Hold(7f, () => { if (!TargetDead) AimAt(Chest(_target), 0.15f); }, () => TargetDead);
                    break;
                case "big_bubbler":
                    _tin.Attack = true;
                    yield return Hold(4f, () => { AimAt(Chest(_target), 0.2f); TargetAimAtMe(); });
                    _tin.Attack = false;
                    break;
                case "tacticooler":
                    yield return Hold(0.4f);
                    yield return Throw(_me.transform.position + _me.transform.forward * 3f, 18, false);
                    yield return Hold(1.2f);
                    _in.Move = new Vector2(0f, 1f);
                    yield return Hold(1.2f);
                    _in.Move = Vector2.zero;
                    yield return Hold(1.5f);
                    break;
            }
            _in.Attack = false;
            yield return Aftermath(TargetDead ? 1.8f : 0.8f);
        }

        private void TargetAimAtMe()
        {
            BotAimView aim = _target.AimView as BotAimView;
            if (aim != null) aim.LookAt(Chest(_me));
        }

        // ---------------------------------------------------------------- movement
        private void PaintLine(Vector3 a, Vector3 b, float radius)
        {
            InkSource src = _me.InkSource(false);
            float len = Vector3.Distance(a, b);
            for (float s = 0f; s <= len; s += radius * 0.6f)
                InkPainter.PaintSphere(Vector3.Lerp(a, b, len > 0 ? s / len : 0f), radius, 0.8f, src);
        }

        private void PaintRect(Vector3 min, Vector3 max, float radius)
        {
            InkSource src = _me.InkSource(false);
            for (float x = min.x; x <= max.x + 0.01f; x += radius * 0.7f)
                for (float y = min.y; y <= max.y + 0.01f; y += radius * 0.7f)
                    for (float z = min.z; z <= max.z + 0.01f; z += radius * 0.7f)
                        InkPainter.PaintSphere(new Vector3(x, y, z), radius, 0.8f, src);
        }

        private IEnumerator MoveShot(string id)
        {
            ResetIntents();
            _match.DebugSetRemaining(170f);
            ClearInk();
            yield return Hold(2.5f);
            Equip("splattershot");
            // 目標移到遠處,不入鏡
            yield return PlaceTarget(new Vector3(18f, 0.1f, 40f));
            _target.GetComponent<InklingHealth>().Invulnerable = true;
            var moves = _me.GetComponent<SquidMoves>();
            var loco = _me.GetComponent<PlayerLocomotion>();
            switch (id)
            {
                case "swim":
                {
                    PaintLine(new Vector3(-4f, 0f, -50f), new Vector3(-4f, 0f, -17f), 2.2f);
                    _me.Teleport(new Vector3(-4f, 0.1f, -46f), 0f);
                    SetView(0f, 14f);
                    yield return Hold(0.3f);
                    StartRec("move_swim");
                    yield return Hold(0.5f);
                    _in.Squid = true;
                    _in.Move = new Vector2(0f, 1f);
                    yield return Hold(3.0f, () => SetView(0f, 14f), () => _me.transform.position.z > -19.5f);
                    _in.PressJump();
                    yield return Hold(0.15f);
                    _in.Squid = false;
                    yield return Hold(0.6f);
                    _in.Move = Vector2.zero;
                    yield return Hold(1.0f);
                    break;
                }
                case "climb":
                {
                    // 地面 → 中央平台(2m 牆) → 高塔(4m 牆,z=-4)
                    PaintLine(new Vector3(-3f, 0f, -26f), new Vector3(-3f, 0f, -12.2f), 2f);
                    PaintRect(new Vector3(-5f, 0f, -12.2f), new Vector3(-1f, 2.2f, -12.2f), 1.3f);
                    PaintLine(new Vector3(-3f, 2f, -12f), new Vector3(-3f, 2f, -4.2f), 2f);
                    PaintRect(new Vector3(-4.5f, 2f, -4.1f), new Vector3(-1.5f, 6.2f, -4.1f), 1.3f);
                    _me.Teleport(new Vector3(-3f, 0.1f, -25f), 0f);
                    SetView(0f, 4f);
                    yield return Hold(0.3f);
                    StartRec("move_climb");
                    yield return Hold(0.5f);
                    _in.Squid = true;
                    _in.Move = new Vector2(0f, 1f);
                    yield return Hold(6.5f, () => SetView(Mathf.LerpAngle(_rig.Yaw, 0f, 0.1f), loco.IsClimbing ? -10f : 4f), () => _me.transform.position.y > 6f && !loco.IsClimbing);
                    yield return Hold(0.6f);
                    _in.Move = Vector2.zero;
                    _in.Squid = false;
                    yield return Hold(1.2f);
                    break;
                }
                case "roll":
                {
                    PaintRect(new Vector3(-12f, 0f, -48f), new Vector3(4f, 0f, -20f), 2.2f);
                    _me.Teleport(new Vector3(-4f, 0.1f, -46f), 0f);
                    SetView(0f, 16f);
                    yield return Hold(0.3f);
                    StartRec("move_roll");
                    yield return Hold(0.4f);
                    _in.Squid = true;
                    _in.Move = new Vector2(0f, 1f);
                    yield return Hold(0.9f);
                    _in.Move = new Vector2(0f, -1f);
                    _in.PressJump();
                    yield return Hold(0.2f);
                    yield return Hold(1.5f, null, () => !moves.IsRolling);
                    yield return Hold(0.9f);
                    _in.Move = new Vector2(0f, 1f);
                    yield return Hold(0.9f);
                    _in.Move = new Vector2(0f, -1f);
                    _in.PressJump();
                    yield return Hold(0.2f);
                    yield return Hold(1.5f, null, () => !moves.IsRolling);
                    yield return Hold(0.8f);
                    _in.Move = Vector2.zero;
                    _in.Squid = false;
                    yield return Hold(0.8f);
                    break;
                }
                case "surge":
                {
                    PaintLine(new Vector3(-3f, 2f, -11f), new Vector3(-3f, 2f, -4.2f), 2f);
                    PaintRect(new Vector3(-4.5f, 2f, -4.1f), new Vector3(-1.5f, 6.2f, -4.1f), 1.3f);
                    _me.Teleport(new Vector3(-3f, 2.1f, -10.5f), 0f);
                    SetView(0f, -6f);
                    yield return Hold(0.3f);
                    StartRec("move_surge");
                    yield return Hold(0.4f);
                    _in.Squid = true;
                    _in.Move = new Vector2(0f, 1f);
                    yield return Hold(3f, null, () => _me.transform.position.y > 3.2f);
                    _in.Move = Vector2.zero;
                    _in.PressJump();
                    yield return Hold(1.0f, () => SetView(0f, -14f));
                    _in.JumpHold = false;
                    yield return Hold(2.2f, () => SetView(0f, Mathf.Lerp(Wrap(_rig.transform.eulerAngles.x), 10f, 0.05f)));
                    _in.JumpHold = true;
                    _in.Squid = false;
                    yield return Hold(1.0f);
                    break;
                }
                case "superjump":
                {
                    Vector3[] spots = { new Vector3(-2f, 2.1f, 6f), new Vector3(2.5f, 2.1f, 7f), new Vector3(-6f, 2.1f, 8f) };
                    for (int i = 0; i < _mates.Count && i < spots.Length; i++) _mates[i].Teleport(spots[i], 180f);
                    _me.Teleport(Origin, 0f);
                    SetView(0f, 8f);
                    yield return Hold(0.4f);
                    StartRec("move_superjump");
                    yield return Hold(0.6f);
                    _in.PressMap();
                    yield return Hold(0.6f);
                    _in.PressMenuStep(1);
                    yield return Hold(0.6f);
                    _in.PressMenuConfirm();
                    var jump = SuperJump.For(_me);
                    yield return Hold(0.5f);
                    yield return Hold(8f, null, () => !jump.IsBusy);
                    yield return Hold(1.5f);
                    break;
                }
            }
            ResetIntents();
        }
    }
}

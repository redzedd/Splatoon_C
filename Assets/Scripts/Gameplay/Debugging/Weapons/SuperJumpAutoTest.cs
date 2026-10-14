using System.Collections;
using SplatoonC.Core;
using SplatoonC.Core.Locomotion;
using SplatoonC.Gameplay.Player;
using SplatoonC.Gameplay.UI;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // A4 超級跳驗收(試射場;出生點 = 場景初始位置 (0, 0.1, 0);隊友 = 複製本機玩家):
    //   選單(SuperJumpHud):E 開啟 → 清單有出生點與隊友、選單開著角色不動 → 下一個 → 確定 → 開始蓄力
    //   時間軸:人形 98F / 烏賊 80F 蓄力(S3 80 + 18)、138F 飛行;空中開始要著地才計時
    //   蓄力:強制烏賊形態、浮出看得見、可被打(敵方爆炸有傷害)、移動被鎖;飛行:無敵(爆炸 0 傷害)、關閉碰撞、弧線頂點 ≥ 10 m;
    //   落地前 12F 變人形可開槍;落點誤差 < 0.3 m;落點標記:所有人看得到(名字 + 墨水流乾)、落地後消失
    //   隊友目標:落在隊友旁邊(不重疊)、蓄力中跟著隊友移動、起飛後鎖定;不能跳向「正跳向自己」的人;被跳向時 HUD 提示;
    //   蓄力中被擊倒 / 目標陣亡 → 取消。視覺:蓄力自轉、飛行姿勢、著地人形姿勢。
    [AutoTest(Scene = AutoTestAttribute.TrainingScene, Timeout = 150f, Order = 242)]
    public sealed class SuperJumpAutoTest : AutoTestBase
    {
        private GameObject _player;
        private Inkling _inkling;
        private InklingHealth _health;
        private InklingLifecycle _life;
        private SquidController _squid;
        private CharacterController _controller;
        private PlayerInputRouter _router;
        private SuperJump _jump;
        private CharacterVisual _visual;
        private ScriptedIntent _intent;
        private Inkling _mate;
        private ScriptedIntent _mateIntent;
        private int _exceptions;
        private string _firstException;

        private static SuperJumpParams P => MovementConfig.Current.SuperJump;

        public static void Run()
        {
            Launch<SuperJumpAutoTest>();
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
            if (type == LogType.Exception)
            {
                _exceptions++;
                _firstException ??= message;
            }
        }

        private IEnumerator Start()
        {
            _player = FindPlayer();
            _inkling = _player != null ? _player.GetComponent<Inkling>() : null;
            _health = _player != null ? _player.GetComponent<InklingHealth>() : null;
            _life = _player != null ? _player.GetComponent<InklingLifecycle>() : null;
            _squid = _player != null ? _player.GetComponent<SquidController>() : null;
            _controller = _player != null ? _player.GetComponent<CharacterController>() : null;
            _router = _player != null ? _player.GetComponent<PlayerInputRouter>() : null;
            _jump = _player != null ? _player.GetComponent<SuperJump>() : null;
            _visual = _player != null ? _player.GetComponent<CharacterVisual>() : null;
            if (_inkling == null || _jump == null || _life == null || _router == null)
            {
                FailSetup("找不到 Player 或 SuperJump(PlayerLocomotion.Awake 應自動掛上)");
                yield break;
            }
            Check("設定資產:MovementConfig 接好落點標記 prefab", MovementConfig.Current.MarkerPrefab != null,
                "先跑 M7 Setup(MovementSetup)");
            _intent = ScriptedIntent.Drive(_player.transform);
            yield return null;
            // 暖身一跳(第一次出現落點標記/護甲時編輯器編譯著色器、建字型貼圖會卡一下;卡頓幀會讓幀數量測失真)
            Teleport(_player.transform, new Vector3(-4f, 0.1f, -4f));
            yield return new WaitForSeconds(0.3f);
            _jump.TryBegin(SuperJumpTarget.Spawn);
            yield return WaitUntil(() => !_jump.IsBusy, 5f);
            yield return new WaitForSeconds(0.3f);

            yield return SpawnJumpViaMenu();
            yield return SquidStartAndAirborne();
            yield return CancelOnDeath();
            yield return TeammateJumps();

            Check("超級跳全程無例外", _exceptions == 0, $"例外 {_exceptions} 次 {_firstException}");
            if (_mate != null)
            {
                Destroy(_mate.gameObject);
            }
            _router.ClearOverrideSource();
            Finish();
        }

        private IEnumerator SpawnJumpViaMenu()
        {
            SuperJumpHud hud = SuperJumpHud.Instance;
            Check("超級跳選單(SuperJumpHud)在試射場自動建立", hud != null, "Instance");
            if (hud == null)
            {
                yield break;
            }
            Vector3 spawn = _life.SpawnPosition;
            Teleport(_player.transform, new Vector3(-15f, 0.1f, -12f));
            yield return new WaitForSeconds(0.3f);

            // 開選單:角色被鎖(推搖桿不動)
            _intent.PressMap();
            yield return null;
            yield return null;
            bool open = hud.IsOpen && _router.MenuOpen;
            bool spawnFirst = hud.Targets.Count > 0 && hud.Targets[0].Kind == SuperJumpTargetKind.Spawn;
            Vector3 before = _player.transform.position;
            _intent.Move = new Vector2(0f, 1f);
            yield return new WaitForSeconds(0.3f);
            float drift = MovementTestKit.Flat(_player.transform.position, before);
            _intent.Move = Vector2.zero;
            Check("按 E 開啟超級跳選單:第一個是出生點、選單開著角色不動",
                open && spawnFirst && drift < 0.05f, $"開啟={open} 出生點在首位={spawnFirst} 推搖桿位移 {drift:0.000} m");
            // 下一個(沒有隊友時繞回出生點)
            _intent.PressMenuStep(1);
            yield return null;
            yield return null;
            int count = hud.Targets.Count;
            int selected = hud.Selected;
            Check("選單換目標(+1,循環)", selected == (count > 1 ? 1 : 0), $"清單 {count} 項、選到 {selected}");
            _intent.PressMenuStep(-1);
            yield return null;
            yield return null;
            // 確定 → 從人形開始蓄力
            float y0 = _player.transform.position.y;
            _intent.PressMenuConfirm();
            yield return null;
            yield return null;
            bool charging = _jump.IsCharging && _jump.Target.Kind == SuperJumpTargetKind.Spawn && !hud.IsOpen;
            Check("確定 → 超級跳開始蓄力(選單關閉)", charging, $"階段 {_jump.Phase}、目標 {_jump.Target.Kind}、選單開={hud.IsOpen}");
            if (!charging)
            {
                yield break;
            }
            bool fromHuman = _jump.FromHumanoid;
            yield return new WaitForSeconds(0.4f);
            // 蓄力中:強制烏賊、浮出、標記顯示在出生點、可被打、不能動
            SuperJumpMarker marker = _jump.Marker;
            bool markerOk = _jump.MarkerShowing && marker != null && marker.LabelText == _inkling.DisplayName
                            && MovementTestKit.Flat(marker.transform.position, spawn) < 0.5f && marker.Fill > 0.99f;
            Check("落點標記:蓄力開始就出現在落點、顯示跳躍者名字、墨水全滿", markerOk,
                marker != null ? $"顯示={_jump.MarkerShowing} 名字「{marker.LabelText}」 位置 {marker.transform.position} 墨水 {marker.Fill:0.00}" : "沒有標記");
            Check("蓄力中:強制烏賊形態且浮出(看得見)", _squid.IsSquid && !_squid.IsSubmerged,
                $"烏賊={_squid.IsSquid} 潛入={_squid.IsSubmerged}");
            float damageBefore = _health.Damage;
            MovementTestKit.Poke(_player.transform.position + Vector3.up * 0.5f, Team.Bravo, 20f);
            float hurt = _health.Damage - damageBefore;
            Check("蓄力中可以被打(S3 蓄力會受傷)", hurt >= 19.9f, $"受到 {hurt:0.0}");
            before = _player.transform.position;
            _intent.Move = new Vector2(1f, 0f);
            _intent.PressJump();
            yield return new WaitForSeconds(0.3f);
            _intent.Move = Vector2.zero;
            Check("蓄力中移動/跳躍被鎖", MovementTestKit.Flat(_player.transform.position, before) < 0.05f
                                    && Mathf.Abs(_player.transform.position.y - y0) < 0.2f,
                $"位移 {MovementTestKit.Flat(_player.transform.position, before):0.000} m");

            yield return WaitUntil(() => _jump.InFlight, 3f);
            float chargeFrames = (_jump.LaunchedAt - _jump.BeganAt) * 60f;
            int expected = SuperJumpModel.ChargeFramesAt0Ap(P, true);
            Check($"人形開始:蓄力 {expected}F(S3 80 + 18)", fromHuman && Mathf.Abs(chargeFrames - expected) <= 3f,
                $"{chargeFrames:0.0}F(人形開始={fromHuman})");

            // 飛行:無敵、關碰撞、弧線
            yield return null;
            bool invulnerable = _health.Invulnerable && !_controller.enabled;
            damageBefore = _health.Damage;
            MovementTestKit.Poke(_player.transform.position + Vector3.up * 0.5f, Team.Bravo, 30f);
            float flightHurt = _health.Damage - damageBefore;
            Check("飛行中完全無敵、穿牆(碰撞關閉)", invulnerable && flightHurt < 0.01f,
                $"無敵={_health.Invulnerable} 碰撞={_controller.enabled} 受到 {flightHurt:0.0}");
            float peak = _player.transform.position.y;
            float midFill = -1f;
            float midWeight = 0f;
            bool spun = false;
            bool fireWindowOk = false;
            bool fireWindowSeen = false;
            float launchY = _jump.FlightStart.y;
            while (_jump.InFlight)
            {
                peak = Mathf.Max(peak, _player.transform.position.y);
                if (midFill < 0f && _jump.Flight01 >= 0.5f)
                {
                    midFill = marker != null ? marker.Fill : -1f;
                    midWeight = _visual != null ? _visual.SuperJumpFlightWeight : 0f;
                    spun = _squid.IsSquid;
                }
                if (!fireWindowSeen && _jump.InFireWindow)
                {
                    fireWindowSeen = true;
                    yield return null;
                    fireWindowOk = !_squid.IsSquid && (_router.ActionBlock & InputBlock.Attack) == 0
                                   && (_router.ActionBlock & InputBlock.Sub) != 0;
                    continue;
                }
                yield return null;
            }
            float flightFrames = (_jump.LandedAt - _jump.LaunchedAt) * 60f;
            Check($"飛行 {P.FlightFrames}F(與距離無關)", Mathf.Abs(flightFrames - P.FlightFrames) <= 3f, $"{flightFrames:0.0}F");
            Check("飛行弧線:頂點高出起點 10 m 以上", peak - launchY > 10f, $"頂點 +{peak - launchY:0.0} m");
            Check("落點標記的墨水隨飛行流乾(一半時約 0.5)", midFill > 0.35f && midFill < 0.65f, $"飛行一半時墨水 {midFill:0.00}");
            Check("飛行姿勢:烏賊形態沿弧線", spun && midWeight > 0.5f, $"烏賊={spun} 飛行姿勢權重 {midWeight:0.00}");
            Check($"落地前 {P.FireWindowFrames}F:變回人形、可開槍(副武器仍鎖)", fireWindowSeen && fireWindowOk,
                $"進入窗口={fireWindowSeen} 人形且攻擊解鎖={fireWindowOk}");
            yield return null;
            Vector3 landed = _player.transform.position;
            Check("落在出生點(誤差 < 0.3 m)、碰撞開回、不再無敵、標記消失",
                MovementTestKit.Flat(landed, spawn) < 0.3f && _controller.enabled && !_health.Invulnerable && !_jump.MarkerShowing
                && _jump.JumpsCompleted == 2 && _router.ActionBlock == InputBlock.None,
                $"落點 {landed}(出生點 {spawn})碰撞={_controller.enabled} 無敵={_health.Invulnerable} 標記={_jump.MarkerShowing}");
            yield return new WaitForSeconds(0.5f);
        }

        private IEnumerator SquidStartAndAirborne()
        {
            // 從烏賊態開始:80F
            Teleport(_player.transform, new Vector3(-12f, 0.1f, 12f));
            _intent.Squid = true;
            yield return new WaitForSeconds(0.4f);
            bool started = _jump.TryBegin(SuperJumpTarget.Spawn);
            yield return WaitUntil(() => _jump.InFlight, 3f);
            float chargeFrames = (_jump.LaunchedAt - _jump.BeganAt) * 60f;
            Check($"烏賊態開始:蓄力 {P.ChargeFrames}F", started && !_jump.FromHumanoid
                                               && Mathf.Abs(chargeFrames - P.ChargeFrames) <= 3f, $"{chargeFrames:0.0}F");
            yield return WaitUntil(() => !_jump.IsBusy, 4f);
            _intent.Squid = false;
            yield return new WaitForSeconds(0.3f);

            // 空中開始:著地前不計時(S3 不能拿來躲墜落)
            Teleport(_player.transform, new Vector3(-12f, 0.1f, 12f));
            yield return new WaitForSeconds(0.3f);
            _intent.PressJump();
            yield return new WaitForSeconds(0.15f);
            bool airborne = !_controller.isGrounded;
            started = _jump.TryBegin(SuperJumpTarget.Spawn);
            yield return new WaitForSeconds(0.4f);
            float elapsedInAir = _jump.ChargeElapsed;
            bool stillAir = !_controller.isGrounded;
            yield return WaitUntil(() => _jump.InFlight, 4f);
            Check("空中開始超級跳:著地前蓄力不計時,著地後照常起飛",
                started && airborne && stillAir && elapsedInAir < 0.02f && _jump.InFlight,
                $"空中={airborne} 0.4 秒後仍在空中={stillAir} 蓄力 {elapsedInAir:0.000}s 起飛={_jump.InFlight}");
            yield return WaitUntil(() => !_jump.IsBusy, 4f);
            yield return new WaitForSeconds(0.3f);
        }

        private IEnumerator CancelOnDeath()
        {
            Teleport(_player.transform, new Vector3(-12f, 0.1f, 12f));
            yield return new WaitForSeconds(0.3f);
            int cancels = SuperJump.CancelCount;
            _jump.TryBegin(SuperJumpTarget.Spawn);
            yield return new WaitForSeconds(0.3f);
            _health.ApplyDamage(new SplatoonC.Gameplay.Combat.DamageInfo(200f, _player.transform.position, Team.Bravo, null,
                SplatoonC.Gameplay.Combat.DamageKind.Main));
            yield return null;
            Check("蓄力中被擊倒 → 超級跳取消、標記消失、輸入鎖解除",
                !_jump.IsBusy && SuperJump.CancelCount == cancels + 1 && !_jump.MarkerShowing
                && _router.ActionBlock == InputBlock.None,
                $"階段 {_jump.Phase}、取消 {SuperJump.CancelCount - cancels} 次、標記={_jump.MarkerShowing}");
            _life.ForceRespawnNow();
            yield return new WaitForSeconds(0.5f);
        }

        private IEnumerator TeammateJumps()
        {
            var matePos = new Vector3(-6f, 0.1f, 14f);
            _mate = MovementTestKit.CloneCharacter(_player, matePos, "Mate", _inkling.Team, out _mateIntent);
            yield return new WaitForSeconds(0.5f);
            var mateJump = _mate.GetComponent<SuperJump>();
            Teleport(_player.transform, new Vector3(-16f, 0.1f, -14f));
            yield return new WaitForSeconds(0.3f);

            // 清單列出隊友
            var list = new System.Collections.Generic.List<SuperJumpTarget>();
            SuperJumpTargets.Collect(_inkling, list);
            bool listed = false;
            foreach (SuperJumpTarget t in list)
            {
                listed |= t.Kind == SuperJumpTargetKind.Teammate && t.Teammate == _mate;
            }
            Check("目標清單:出生點 + 活著的隊友", listed && list[0].Kind == SuperJumpTargetKind.Spawn, $"{list.Count} 項");

            // 跳向隊友:蓄力中隊友移動 → 落點跟著;起飛後鎖定
            bool started = _jump.TryBegin(SuperJumpTarget.ToTeammate(_mate));
            yield return new WaitForSeconds(0.5f);
            var moved = new Vector3(-2f, 0.1f, 16f);
            Teleport(_mate.transform, moved);
            yield return new WaitForSeconds(0.3f);
            bool followed = MovementTestKit.Flat(_jump.LandingPoint, moved) < 1.6f;
            yield return WaitUntil(() => _jump.InFlight, 3f);
            Vector3 locked = _jump.LandingPoint;
            Teleport(_mate.transform, matePos);
            yield return new WaitForSeconds(0.3f);
            bool stayed = (_jump.LandingPoint - locked).sqrMagnitude < 0.01f;
            yield return WaitUntil(() => !_jump.IsBusy, 4f);
            yield return null;
            float gap = MovementTestKit.Flat(_player.transform.position, moved);
            Check("跳向隊友:蓄力中落點跟著隊友、起飛後鎖定;落在他最後著地的位置旁邊(不重疊)",
                started && followed && stayed && gap > 0.8f && gap < 1.6f,
                $"跟隨={followed} 鎖定={stayed} 落點距隊友原位 {gap:0.00} m");
            yield return new WaitForSeconds(0.5f);

            // 隊友跳向我:我不能跳向他;HUD 提示「超級跳過來了」;落在我旁邊
            SuperJumpHud hud = SuperJumpHud.Instance;
            Teleport(_player.transform, new Vector3(-16f, 0.1f, -14f));
            yield return new WaitForSeconds(0.3f);
            bool mateStarted = mateJump.TryBegin(SuperJumpTarget.ToTeammate(_inkling));
            yield return null;
            yield return null;
            bool refused = !_jump.IsValidTarget(SuperJumpTarget.ToTeammate(_mate));
            string notice = hud != null ? hud.NoticeText : null;
            Check("不能跳向「正在跳向自己」的隊友;被跳向時 HUD 提示",
                mateStarted && refused && notice != null && notice.Contains("Mate"),
                $"隊友開始={mateStarted} 拒絕={refused} 提示「{notice}」");
            yield return WaitUntil(() => !mateJump.IsBusy, 4.5f);
            yield return null;
            float mateGap = MovementTestKit.Flat(_mate.transform.position, _player.transform.position);
            Check("隊友落在我旁邊", mateJump.JumpsCompleted >= 1 && mateGap > 0.8f && mateGap < 1.6f, $"距離 {mateGap:0.00} m");

            // 目標在蓄力中陣亡 → 取消
            int cancels = SuperJump.CancelCount;
            _jump.TryBegin(SuperJumpTarget.ToTeammate(_mate));
            yield return new WaitForSeconds(0.3f);
            _mate.GetComponent<InklingHealth>().ApplyDamage(new SplatoonC.Gameplay.Combat.DamageInfo(200f,
                _mate.transform.position, Team.Bravo, null, SplatoonC.Gameplay.Combat.DamageKind.Main));
            yield return null;
            yield return null;
            Check("蓄力中目標隊友陣亡 → 取消", !_jump.IsBusy && SuperJump.CancelCount == cancels + 1,
                $"階段 {_jump.Phase}、原因 {_jump.LastRefusal}");
            yield return new WaitForSeconds(0.3f);
        }
    }
}

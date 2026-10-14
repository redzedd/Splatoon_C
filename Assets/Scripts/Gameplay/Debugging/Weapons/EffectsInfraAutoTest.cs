using System.Collections;
using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Core.Match;
using SplatoonC.Gameplay.CameraRig;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using SplatoonC.Gameplay.UI;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 效果基礎建設驗收(試射場,真實輸入路徑量測):
    //   標記:只有標記方看得到(輪廓/頭上標記/指向線)、準時到期、再標記取長、被標記者 HUD「被標記!」、死亡清除
    //   InklingModifiers:RunSpeed×0.5 實測跑速 ≈ 2.88、到期恢復;能量站(RSU/SSU 29、墨抗 57)實測跑速 7.86、游速 13.62、
    //   敵墨走速 4.61、持續傷害上限 20;耗墨倍率、停止回墨、持續扣墨與上限、跳躍高度、停止回血、大招充能倍率
    //   死亡:能量站 QR 57 → 重生 3.5 s、Special Saver 57 → 大招全留,非 Persistent 效果清除;對照組無效果 7.5 s / 留一半
    //   Inkling.Teleport:到位、殘留速度清除、烏賊速度餘韻清除、鏡頭與面向立即對齊
    [AutoTest(Scene = AutoTestAttribute.TrainingScene, Timeout = 150f, Order = 210)]
    public sealed class EffectsInfraAutoTest : AutoTestBase
    {
        private const float BaseRun = 5.76f;
        private const float BaseSwim = 11.52f;

        private int _errors;
        private string _firstError;
        private float _measured;
        private bool _flagSeen;

        public static void Run()
        {
            Launch<EffectsInfraAutoTest>();
        }

        private void OnEnable()
        {
            Application.logMessageReceived += OnLog;
        }

        private void OnDisable()
        {
            Application.logMessageReceived -= OnLog;
        }

        private void OnLog(string message, string stackTrace, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error && type != LogType.Assert)
            {
                return;
            }
            if (message.StartsWith("[AUTOTEST]", System.StringComparison.Ordinal))
            {
                return;
            }
            _errors++;
            if (_firstError == null)
            {
                _firstError = message;
            }
        }

        private IEnumerator Start()
        {
            GameObject playerGo = FindPlayer();
            Inkling player = playerGo != null ? playerGo.GetComponent<Inkling>() : null;
            var rig = Camera.main != null ? Camera.main.GetComponent<ThirdPersonCameraRig>() : null;
            GameObject groundGo = GameObject.Find("Ground");
            var ground = groundGo != null ? groundGo.GetComponent<PaintableSurface>() : null;
            if (player == null || rig == null || ground == null)
            {
                FailSetup("找不到 Player / 相機 / Ground(需在 SampleScene)");
                yield break;
            }
            var intent = ScriptedIntent.Drive(player);
            InklingModifiers mods = player.Modifiers;
            yield return null;

            Check("InklingModifiers 由 Inkling 自動掛上(不改 prefab)",
                mods != null && player.GetComponent<InklingModifiers>() == mods && mods.Count == 0, $"count={mods?.Count}");
            Check("非對戰場景自動建立狀態列 HUD", StatusHud.Instance != null && StatusHud.Instance.Row != null,
                StatusHud.Instance != null ? "StatusHud 存在" : "沒有 StatusHud");

            // 標記靶:複製本機玩家改成 Bravo 非本機角色(站著不動)
            GameObject enemyGo = Instantiate(playerGo, new Vector3(6f, 0.1f, -14f), Quaternion.identity);
            enemyGo.name = "EffectsEnemy";
            var enemy = enemyGo.GetComponent<Inkling>();
            enemy.Configure(Team.Bravo, "標記靶", false);
            ScriptedIntent.Drive(enemy);
            yield return null;
            yield return null;

            yield return MarkingCases(player, enemy);
            yield return RunCases(player, intent, mods);
            yield return SwimAndEnemyInkCases(player, intent, mods, ground);
            yield return JumpCases(player, intent, mods);
            yield return InkCases(player, mods);
            yield return VitalsAndGaugeCases(player, mods);
            yield return TeleportCases(player, intent, rig);
            yield return DeathCases(player, enemy);

            Check("全程無例外/錯誤 log", _errors == 0, _errors == 0 ? "0" : $"{_errors} 筆,第一筆:{_firstError}");

            mods.ClearAll();
            Marking.ClearAll();
            Destroy(enemyGo);
            player.GetComponent<PlayerInputRouter>().ClearOverrideSource();
            player.Teleport(new Vector3(0f, 0.1f, 0f), 0f);
            Finish();
        }

        // ───────── 標記 ─────────

        private IEnumerator MarkingCases(Inkling player, Inkling enemy)
        {
            Marking.Apply(enemy, Team.None, 5f);
            Marking.Apply(enemy, Team.Bravo, 5f);
            Check("無隊伍/自己隊伍的標記無效", !Marking.IsMarked(enemy, Team.Alpha) && !Marking.IsMarked(enemy, Team.Bravo)
                                          && Marking.ActiveCount == 0, $"active={Marking.ActiveCount}");

            float t0 = Time.time;
            Marking.Apply(enemy, Team.Alpha, 1f);
            Check("標記:只有標記方(Alpha)看得到", Marking.IsMarked(enemy, Team.Alpha) && !Marking.IsMarked(enemy, Team.Bravo),
                $"Alpha={Marking.IsMarked(enemy, Team.Alpha)} Bravo={Marking.IsMarked(enemy, Team.Bravo)}");
            Marking.Apply(enemy, Team.Alpha, 0.3f);
            float remaining = Marking.Remaining(enemy, Team.Alpha);
            Check("再次標記取剩餘較長者(0.3s 不會縮短 1.0s)", remaining > 0.95f, $"剩 {remaining:0.000}s");
            yield return null;
            yield return null;
            var indicator = enemy.GetComponent<MarkIndicator>();
            bool ghostThroughWalls = indicator != null && indicator.GhostRenderer != null
                && Mathf.Approximately(indicator.GhostRenderer.sharedMaterial.GetFloat("_ZTest"),
                    (float)UnityEngine.Rendering.CompareFunction.Always);
            Check("標記方畫面:輪廓 + 頭上標記 + 指向線", indicator != null && indicator.Showing && indicator.LineShowing
                                                   && indicator.Marker.gameObject.activeInHierarchy,
                indicator == null ? "沒有 MarkIndicator" : $"showing={indicator.Showing} line={indicator.LineShowing}");
            Check("輪廓穿牆(ZTest Always)", ghostThroughWalls,
                indicator != null && indicator.GhostRenderer != null ? indicator.GhostRenderer.sharedMaterial.shader.name : "-");

            // 本機玩家被敵隊標記:自己畫面不畫自己的標記,HUD 顯示「被標記!」
            Marking.Apply(player, Team.Bravo, 1.5f);
            yield return null;
            yield return null;
            var selfIndicator = player.GetComponent<MarkIndicator>();
            Check("非標記方看不到(本機被 Bravo 標記,Alpha 畫面不畫)", selfIndicator != null && !selfIndicator.Showing,
                selfIndicator == null ? "沒有 MarkIndicator" : $"showing={selfIndicator.Showing}");
            Check("被標記者 HUD「被標記!」", Marking.IsMarkedByEnemy(player) && StatusHud.Instance.Row.IsShown(StatusFlags.Marked),
                $"row={StatusHud.Instance.Row.Shown}");

            yield return WaitUntil(() => !Marking.IsMarked(enemy, Team.Alpha), 3f);
            float lifetime = Time.time - t0;
            Check("標記 1.0s 準時到期", Mathf.Abs(lifetime - 1f) < 0.08f, $"實測 {lifetime:0.000}s");
            yield return null;
            yield return null;
            Check("到期後標記方畫面收起", indicator != null && !indicator.Showing, $"showing={indicator?.Showing}");
            yield return WaitUntil(() => !Marking.IsMarkedByEnemy(player), 2f);
            yield return null;
            Check("被標記結束 HUD 收起", !StatusHud.Instance.Row.IsShown(StatusFlags.Marked), $"row={StatusHud.Instance.Row.Shown}");
            Check("S3 標記秒數常數(Point Sensor 480F / Ink Mine 300F)",
                Mathf.Approximately(Marking.PointSensorSeconds, 8f) && Mathf.Approximately(Marking.InkMineSeconds, 5f),
                $"{Marking.PointSensorSeconds}s / {Marking.InkMineSeconds}s");
        }

        // ───────── 跑速 ─────────

        private IEnumerator RunCases(Inkling player, ScriptedIntent intent, InklingModifiers mods)
        {
            // 乾地跑道:z = −20,由 x = 0 往 −X
            var lane = new Vector3(0f, 0.1f, -20f);
            yield return MeasureMove(player, intent, lane, 270f, false, 0.5f, 1f, StatusFlags.None);
            float baseline = _measured;
            Check("基準跑速 5.76 m/s", Mathf.Abs(baseline - BaseRun) < 0.2f, $"實測 {baseline:0.00}");

            object slow = new object();
            mods.Add(slow, ModifierSpec.None.WithRunSpeed(0.5f).WithStatus(StatusFlags.Slowed), 2.4f);
            yield return MeasureMove(player, intent, lane, 270f, false, 0.5f, 1f, StatusFlags.Slowed);
            Check("RunSpeed×0.5:實測跑速 ≈ 2.88 m/s", Mathf.Abs(_measured - BaseRun * 0.5f) < 0.15f, $"實測 {_measured:0.00}");
            Check("減速中 HUD 顯示「減速」", _flagSeen, _flagSeen ? "有" : "沒有");
            yield return WaitUntil(() => !mods.Has(slow), 3f);
            yield return MeasureMove(player, intent, lane, 270f, false, 0.5f, 1f, StatusFlags.None);
            Check("到期後跑速恢復 5.76", !mods.Has(slow) && Mathf.Abs(_measured - BaseRun) < 0.2f, $"實測 {_measured:0.00}");

            // 能量站:RSU 29 AP → 0.096 → 0.1310 u/F(×1.365)= 7.86 m/s
            mods.Add(this, ModifierSpec.Tacticooler, ModifierSpec.TacticoolerSeconds);
            yield return MeasureMove(player, intent, lane, 270f, false, 0.5f, 1f, StatusFlags.Buffed);
            float expected = BaseRun * AbilityCurve.RunSpeedRatio(29f, 1);
            Check("能量站 RSU 29:跑速 5.76 → 7.86 m/s", Mathf.Abs(_measured - expected) < 0.25f,
                $"實測 {_measured:0.00}(期望 {expected:0.00})");
            Check("能量站中 HUD 顯示「強化」", _flagSeen, _flagSeen ? "有" : "沒有");
        }

        // ───────── 游速 / 敵墨 ─────────

        private IEnumerator SwimAndEnemyInkCases(Inkling player, ScriptedIntent intent, InklingModifiers mods, PaintableSurface ground)
        {
            // 自家墨跑道 x = −18(往 +Z)、敵墨跑道 x = −10(z 0..20)
            for (int i = 0; i <= 44; i += 2)
            {
                ground.Paint(new Vector3(-18f, 0f, -22f + i), 2.5f, Team.Alpha, 0.8f);
            }
            for (int i = 0; i <= 22; i += 2)
            {
                ground.Paint(new Vector3(-10f, 0f, i), 2.5f, Team.Bravo, 0.8f);
            }
            var swimLane = new Vector3(-18f, 0.1f, -20f);

            // 能量站仍在:SSU 29 → 0.192 → 0.2270 u/F = 13.62 m/s(先把常駐游速裝備歸 0,單獨驗能量站的 AP 曲線)
            int gearAp = mods.GearSwimSpeedAp;
            mods.GearSwimSpeedAp = 0;
            yield return MeasureMove(player, intent, swimLane, 0f, true, 0.6f, 1f, StatusFlags.None);
            float expected = BaseSwim * AbilityCurve.SwimSpeedRatio(29f, 1);
            Check("能量站 SSU 29:游速 11.52 → 13.62 m/s", Mathf.Abs(_measured - expected) < 0.45f,
                $"實測 {_measured:0.00}(期望 {expected:0.00})");
            // 常駐裝備 + 能量站:AP 相加、總和上限 57(S3 游速上限 0.24 u/F = 14.4 m/s)
            mods.GearSwimSpeedAp = gearAp;
            yield return MeasureMove(player, intent, swimLane, 0f, true, 0.6f, 1f, StatusFlags.None);
            float stacked = BaseSwim * AbilityCurve.SwimSpeedRatio(Mathf.Min(AbilityCurve.MaxAp, 29f + gearAp), 1);
            Check($"常駐游速 {gearAp} AP + 能量站 SSU 29:相加後上限 57 AP", Mathf.Abs(_measured - stacked) < 0.45f,
                $"實測 {_measured:0.00}(期望 {stacked:0.00})");

            // 能量站墨抗 57:敵墨走速 0.024 → 0.0768 u/F(1.44 → 4.61 m/s),持續傷害 6/s、上限 20
            var health = player.GetComponent<InklingHealth>();
            health.ResetForRespawn();
            yield return MeasureMove(player, intent, new Vector3(-10f, 0.1f, 2f), 0f, false, 0.5f, 1f, StatusFlags.None);
            float inkResWalk = 1.44f * AbilityCurve.EnemyInkWalkRatio(57f);
            Check("能量站墨抗 57:敵墨走速 1.44 → 4.61 m/s", Mathf.Abs(_measured - inkResWalk) < 0.3f,
                $"實測 {_measured:0.00}(期望 {inkResWalk:0.00})");
            yield return new WaitForSeconds(2.6f);
            float capped = health.Damage;
            Check("能量站墨抗 57:敵墨持續傷害上限 40 → 20", capped > 19f && capped <= 20.05f, $"累積 {capped:0.0}");

            mods.Remove(this);
            yield return new WaitForSeconds(1.5f);
            Check("移除能量站後回到 0 AP 上限 40", health.Damage > 30f && health.Damage <= 40.05f, $"累積 {health.Damage:0.0}");
            player.Teleport(new Vector3(-4f, 0.1f, -12f));
            health.ResetForRespawn();

            // 毒霧預設:游速 ×0.5(乘在含常駐游速裝備的游速上)
            float gearSwim = BaseSwim * AbilityCurve.SwimSpeedRatio(mods.GearSwimSpeedAp, 1);
            mods.Add(this, ModifierSpec.ToxicMist, 3f);
            yield return MeasureMove(player, intent, swimLane, 0f, true, 0.6f, 1f, StatusFlags.Slowed);
            Check($"毒霧預設:游速 {gearSwim:0.00} → {gearSwim * 0.5f:0.00} m/s", Mathf.Abs(_measured - gearSwim * 0.5f) < 0.3f,
                $"實測 {_measured:0.00}");
            mods.Remove(this);
        }

        // ───────── 跳躍 ─────────

        private IEnumerator JumpCases(Inkling player, ScriptedIntent intent, InklingModifiers mods)
        {
            player.Teleport(new Vector3(-4f, 0.1f, -12f), 0f);
            yield return new WaitForSeconds(0.4f);
            yield return MeasureJump(player.transform, intent);
            float baseline = _measured;
            object low = new object();
            mods.Add(low, ModifierSpec.None.WithJumpHeight(0.5f), 0f);
            yield return MeasureJump(player.transform, intent);
            mods.Remove(low);
            float ratio = baseline > 0.01f ? _measured / baseline : 0f;
            Check("JumpHeight×0.5:頂點高度減半", baseline > 1.85f && Mathf.Abs(ratio - 0.5f) < 0.06f,
                $"基準 {baseline:0.00}m → {_measured:0.00}m(×{ratio:0.00})");
        }

        // ───────── 墨槽 ─────────

        private IEnumerator InkCases(Inkling player, InklingModifiers mods)
        {
            var tank = player.GetComponent<PlayerInkTank>();
            tank.InfiniteInk = false;
            player.Teleport(new Vector3(-4f, 0.1f, -12f));

            // 耗墨倍率(Zipcaster ×0.67 同一路徑):×0.5 時 20% 的動作只扣 10%
            tank.RefillFull();
            object cost = new object();
            mods.Add(cost, ModifierSpec.None.WithInkCost(0.5f), 0f);
            float before = tank.Normalized;
            bool ok = tank.TryConsume(0.2f);
            float spent = before - tank.Normalized;
            mods.Remove(cost);
            Check("InkCost×0.5:20% 的動作只扣 10%", ok && Mathf.Abs(spent - 0.1f) < 0.002f, $"扣 {spent:0.000}");

            // 停止回墨:站立本該 10%/s
            tank.RefillFull();
            tank.TryConsume(0.5f);
            tank.LockRefill(0f);
            object noRefill = new object();
            mods.Add(noRefill, ModifierSpec.None.WithInkRefill(0f), 1f);
            float held = tank.Normalized;
            yield return new WaitForSeconds(0.8f);
            float heldAfter = tank.Normalized;
            Check("InkRefill×0:站著不回墨", Mathf.Abs(heldAfter - held) < 0.002f, $"{held:0.000} → {heldAfter:0.000}");
            yield return WaitUntil(() => !mods.Has(noRefill), 2f);
            float resumed = tank.Normalized;
            yield return new WaitForSeconds(1f);
            float perSecond = tank.Normalized - resumed;
            Check("到期後恢復站立回墨 ≈ 10%/s", Mathf.Abs(perSecond - 0.1f) < 0.02f, $"每秒 {perSecond:0.000}");

            // 持續扣墨 20%/s、上限 30%(搭配停止回墨,數字乾淨):1 秒後剩 80%,1.5 秒後停在 70%
            tank.RefillFull();
            object drain = new object();
            mods.Add(drain, ModifierSpec.None.WithInkRefill(0f).WithInkDrain(0.2f, 0.3f), 0f);
            float drainStart = Time.time;
            yield return new WaitForSeconds(1f);
            float afterOne = tank.Normalized;
            float oneElapsed = Time.time - drainStart;
            Check("持續扣墨 20%/s(站著不動也扣)", Mathf.Abs(afterOne - (1f - 0.2f * oneElapsed)) < 0.01f,
                $"{oneElapsed:0.00}s 後剩 {afterOne:0.000}");
            yield return new WaitForSeconds(1.5f);
            Check("單一來源扣墨上限 30%", Mathf.Abs(tank.Normalized - 0.7f) < 0.005f, $"剩 {tank.Normalized:0.000}");
            mods.Remove(drain);
            tank.RefillFull();
        }

        // ───────── 血量 / 大招 ─────────

        private IEnumerator VitalsAndGaugeCases(Inkling player, InklingModifiers mods)
        {
            var health = player.GetComponent<InklingHealth>();
            health.ResetForRespawn();
            object storm = new object();
            mods.Add(storm, ModifierSpec.None.WithoutHealthRegen(), 0f);
            health.ApplyDamage(new DamageInfo(30f, player.transform.position + Vector3.up, Team.Bravo, null, DamageKind.Special));
            yield return new WaitForSeconds(2.2f);
            float stuck = health.Damage;
            Check("NoHealthRegen:受傷 2.2s 後不回血(本該 1s 後 12.6/s)", Mathf.Abs(stuck - 30f) < 0.01f, $"傷害 {stuck:0.0}");
            mods.Remove(storm);
            yield return new WaitForSeconds(1.2f);
            Check("移除後恢復回血", health.Damage < 20f, $"傷害 {health.Damage:0.0}");
            health.ResetForRespawn();

            var gauge = player.GetComponent<PlayerSpecialGauge>();
            float area = 10f * TurfPoints.SquareMetresPerPoint;
            gauge.SetPointsForTest(0f);
            player.OnTurfInked(area, true);
            float plain = gauge.Points;
            gauge.SetPointsForTest(0f);
            object charge = new object();
            mods.Add(charge, ModifierSpec.None.WithSpecialCharge(1.5f), 0f);
            player.OnTurfInked(area, true);
            float boosted = gauge.Points;
            mods.Remove(charge);
            gauge.SetPointsForTest(0f);
            Check("SpecialCharge×1.5:同樣新塗 10p 多充 50%", plain > 0f && Mathf.Abs(boosted / plain - 1.5f) < 0.01f,
                $"{plain:0.00} → {boosted:0.00}");
        }

        // ───────── 瞬移 ─────────

        private IEnumerator TeleportCases(Inkling player, ScriptedIntent intent, ThirdPersonCameraRig rig)
        {
            player.Teleport(new Vector3(0f, 0.1f, -20f), 270f);
            yield return new WaitForSeconds(0.25f);
            intent.Move = new Vector2(0f, 1f);
            yield return new WaitForSeconds(0.7f);
            intent.Move = Vector2.zero;
            var target = new Vector3(-4f, 0.1f, 16f);
            player.Teleport(target, 90f);
            Check("Teleport 到位", Vector3.Distance(player.transform.position, target) < 0.02f,
                $"{player.transform.position}");
            yield return null;
            float camDistance = Vector3.Distance(rig.transform.position, player.transform.position);
            Check("鏡頭立即跟上(不從 36m 外滑過來)", camDistance < 9f, $"相機距角色 {camDistance:0.0}m");
            Transform visual = player.transform.Find("Visual");
            float facingError = Mathf.Abs(Mathf.DeltaAngle(visual != null ? visual.eulerAngles.y : 0f, 90f));
            float viewError = Mathf.Abs(Mathf.DeltaAngle(rig.Yaw, 90f));
            Check("面向與視角轉到指定 yaw 90°", facingError < 1f && viewError < 1f, $"面向誤差 {facingError:0.0}° 視角誤差 {viewError:0.0}°");
            Vector3 p0 = player.transform.position;
            yield return new WaitForSeconds(0.3f);
            float drift = Flat(player.transform.position - p0);
            Check("殘留跑速清除(瞬移前 5.76 m/s,瞬移後不滑行)", drift < 0.05f, $"0.3s 滑行 {drift:0.000}m(不清會滑 ≈0.46m)");

            var squid = player.GetComponent<SquidController>();
            intent.Squid = true;
            yield return new WaitForSeconds(0.25f);
            float squidBefore = squid.CurrentSpeedMultiplier;
            player.Teleport(target + Vector3.right * 2f, 90f, false);
            float squidAfter = squid.CurrentSpeedMultiplier;
            intent.Squid = false;
            Check("瞬移清掉烏賊速度餘韻", squidBefore < 0.9f && Mathf.Approximately(squidAfter, 1f),
                $"{squidBefore:0.00} → {squidAfter:0.00}");
            yield return new WaitForSeconds(0.3f);
        }

        // ───────── 死亡:能量站的 QR / Special Saver、效果與標記清除 ─────────

        private IEnumerator DeathCases(Inkling player, Inkling enemy)
        {
            var life = enemy.GetComponent<InklingLifecycle>();
            var health = enemy.GetComponent<InklingHealth>();
            var gauge = enemy.GetComponent<PlayerSpecialGauge>();
            InklingModifiers mods = enemy.Modifiers;
            object persistent = new object();
            mods.Add(this, ModifierSpec.Tacticooler, ModifierSpec.TacticoolerSeconds);
            mods.Add(persistent, ModifierSpec.None.WithSpecialCharge(1.1f).AsPersistent(), 0f);
            gauge.SetPointsForTest(80f);
            Marking.Apply(enemy, Team.Alpha, Marking.PointSensorSeconds);
            yield return null;
            health.ApplyDamage(new DamageInfo(150f, enemy.transform.position + Vector3.up, Team.Alpha, player, DamageKind.Main));
            yield return null;
            Check("被擊倒清除身上的標記", life.IsDead && !Marking.IsMarked(enemy, Team.Alpha), $"dead={life.IsDead}");
            Check("被擊倒清除非 Persistent 效果、保留 Persistent", !mods.Has(this) && mods.Has(persistent),
                $"能量站={mods.Has(this)} persistent={mods.Has(persistent)}");
            float quick = life.RespawnRemaining;
            Check("能量站 QR 57:重生等待 7.5 → 3.5 s(450F → 210F)", Mathf.Abs(quick - 3.5f) < 0.1f, $"剩 {quick:0.00}s");
            Check("能量站 Special Saver 57:大招 80 全留", Mathf.Abs(gauge.Points - 80f) < 0.5f, $"{gauge.Points:0.0}");

            // 對照組:沒有效果時 7.5 s、留一半(重生後有 30 HP 出生護甲 → 用 250 破甲)
            yield return WaitUntil(() => !life.IsDead, 5f);
            mods.Remove(persistent);
            gauge.SetPointsForTest(80f);
            yield return null;
            health.ApplyDamage(new DamageInfo(250f, enemy.transform.position + Vector3.up, Team.Alpha, player, DamageKind.Main));
            yield return null;
            Check("對照:無效果重生等待 7.5 s、大招留 50%", life.IsDead && Mathf.Abs(life.RespawnRemaining - 7.5f) < 0.1f
                                                     && Mathf.Abs(gauge.Points - 40f) < 0.5f,
                $"dead={life.IsDead} 剩 {life.RespawnRemaining:0.00}s 大招 {gauge.Points:0.0}");
        }

        // ───────── 量測工具 ─────────

        // 瞬移到起點(面向 yaw)→ 靜置 → 推搖桿往前:先跑 settle 秒達穩定,再量 window 秒的水平平均速度 → _measured;
        // 期間看過狀態列的 flag 就記 _flagSeen
        private IEnumerator MeasureMove(Inkling player, ScriptedIntent intent, Vector3 start, float yaw, bool squidForm,
            float settle, float window, StatusFlags flag)
        {
            player.Teleport(start, yaw);
            intent.Squid = squidForm;
            _flagSeen = false;
            yield return new WaitForSeconds(0.25f);
            intent.Move = new Vector2(0f, 1f);
            yield return new WaitForSeconds(settle);
            Vector3 a = player.transform.position;
            float ta = Time.time;
            yield return new WaitForSeconds(window);
            if (flag != StatusFlags.None && StatusHud.Instance != null)
            {
                _flagSeen = StatusHud.Instance.Row.IsShown(flag);
            }
            Vector3 b = player.transform.position;
            float dt = Mathf.Max(0.001f, Time.time - ta);
            intent.Move = Vector2.zero;
            intent.Squid = false;
            _measured = Flat(b - a) / dt;
            yield return new WaitForSeconds(0.2f);
        }

        private IEnumerator MeasureJump(Transform player, ScriptedIntent intent)
        {
            float baseY = player.position.y;
            intent.JumpHold = true;
            intent.PressJump();
            float peak = baseY;
            float end = Time.time + 1.6f;
            while (Time.time < end)
            {
                peak = Mathf.Max(peak, player.position.y);
                yield return null;
            }
            _measured = peak - baseY;
            yield return new WaitForSeconds(0.3f);
        }

        private static float Flat(Vector3 v)
        {
            v.y = 0f;
            return v.magnitude;
        }
    }
}

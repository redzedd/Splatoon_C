using System.Collections;
using System.Collections.Generic;
using SplatoonC.Core;
using SplatoonC.Core.Match;
using SplatoonC.Core.Match.Ranked;
using SplatoonC.Gameplay.AI;
using SplatoonC.Gameplay.CameraRig;
using SplatoonC.Gameplay.Match;
using SplatoonC.Gameplay.Player;
using SplatoonC.Gameplay.UI;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 真格區域驗收(對戰場景,以真實塗墨路徑 InkPainter.PaintSphere 驅動,全員輸入鎖住不讓 bot 干擾):
    //   0. 直接開場景 = 塗地:沒有模式總管、3:00、沒有區域物件(塗地完全不變)
    //   1. MatchSettings.Mode = 真格區域重載:5:00、焦岩峽谷中央 1 個區域 140~200 m²、外觀/HUD/bot 建議就位
    //   2. Alpha 塗到 70% 的前一發不佔領、跨過 70% 的那一幀佔領 → 整個區域瞬間變成 Alpha 墨(歸屬 100% + 畫面墨色大增)
    //   3. 控制中計數每 36F 減 1(規則幀數 vs 計數精確比對、規則以 60 Hz 推進);Bravo 被動充能 4.5 p/s、Alpha 0
    //   4. Bravo 塗到 50% → 中立、計數停止、不給罰分;中立且 Bravo 落後 → Bravo 1.5 p/s
    //   5. Bravo 塗到 70% 奪走 → Alpha 罰分 = ROUND(0.75·(100 − end)) + 1;HUD 顯示 +N
    //   6. Alpha 奪回 → 先扣罰分,最後 1 點與本計數同時減(40+2 → 40+1 → 39)
    //   7. 落後方持有時時間到 → 延長戰(計時停在 0、加時框、播報);領先方奪回 → 加時結束 → 判定 = 計數
    //   8. 再重載:計數 2 時佔領 → 72F 後 KNOCKOUT,對戰提前結束、判定 KO
    // 截圖:Logs/shots/zones_*.png(中立區域、佔領重塗、HUD、延長戰、判定、KNOCKOUT、結算)
    [AutoTest(Scene = AutoTestAttribute.MatchScene, Timeout = 200f, Order = 400)]
    public sealed class SplatZonesAutoTest : AutoTestBase
    {
        public static void Run()
        {
            Launch<SplatZonesAutoTest>();
        }

        private void Awake()
        {
            // 跨場景重載
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            MatchSettings.Mode = MatchMode.TurfWar;
        }

        private IEnumerator Start()
        {
            // ── 0 塗地不變 ──
            yield return WaitUntil(() => MatchController.Instance != null && MatchController.Instance.Roster.Count > 0, 10f);
            MatchController match = MatchController.Instance;
            if (match == null)
            {
                FailSetup("沒有 MatchController(需在對戰場景)");
                yield break;
            }
            Check("直接開場景 = 塗地對戰(沒有模式總管、3:00、沒有區域物件)",
                match.Mode == MatchMode.TurfWar && match.Objective == null && ObjectiveModeController.Instance == null
                && Mathf.Approximately(match.Timeline.Settings.BattleSeconds, 180f)
                && Mathf.Approximately(match.Timeline.Settings.LastMinuteSeconds, 60f)
                && FindAnyObjectByType<SplatZone>() == null && FindAnyObjectByType<ObjectiveHud>() == null
                && match.Timeline.OvertimeGate == null,
                $"mode={match.Mode} objective={(match.Objective != null)} battle={match.Timeline.Settings.BattleSeconds}");

            // ── 1 真格區域開賽 ──
            yield return ModeTestKit.ReloadWithMode(MatchMode.SplatZones);
            match = MatchController.Instance;
            ObjectiveModeController objective = ObjectiveModeController.Instance;
            SplatZonesDriver driver = objective != null ? objective.SplatZones : null;
            if (match == null || driver == null || driver.Zones.Count == 0)
            {
                FailSetup($"真格區域沒有開起來 mode={(match != null ? match.Mode.ToString() : "-")}");
                yield break;
            }
            SplatZonesRules rules = driver.ZoneRules;
            SplatZone zone = driver.Zones[0];
            Check("真格區域:模式總管 + 5:00 + 無最後一分鐘 + 加時閘門",
                match.Mode == MatchMode.SplatZones && match.IsRanked && Mathf.Approximately(match.Timeline.Settings.BattleSeconds, 300f)
                && match.Timeline.Settings.LastMinuteSeconds <= 0f && ReferenceEquals(match.Timeline.OvertimeGate, objective),
                $"mode={match.Mode} battle={match.Timeline.Settings.BattleSeconds} lastMinute={match.Timeline.Settings.LastMinuteSeconds}");
            Check("焦岩峽谷中央 1 個區域,地面投影 140~200 m²(S3 常見大小)",
                driver.Zones.Count == 1 && zone.Area >= 140f && zone.Area <= 200f && Mathf.Abs(zone.Center.x) < 1f
                && Mathf.Abs(zone.Center.z) < 1f && Mathf.Abs(zone.Center.y - 1.5f) < 0.2f,
                $"zones={driver.Zones.Count} area={zone.Area:0.0}m² cells={zone.CellCount} center={zone.Center}");
            var hud = match.GetComponent<ObjectiveHud>();
            Check("區域外觀(邊框 / 光幕 / 穿牆虛線)、重塗材質、蠻頹 HUD、bot 目標建議就位",
                zone.FillReady && zone.Visual != null && zone.Visual.BorderRenderer != null && zone.Visual.CurtainRenderer != null
                && zone.GetComponentsInChildren<MeshRenderer>().Length == 3 && hud != null && hud.Canvas != null
                && BotDirector.Instance != null && ReferenceEquals(BotDirector.Instance.Objective, driver),
                $"visual={(zone.Visual != null)} renderers={zone.GetComponentsInChildren<MeshRenderer>().Length} hud={(hud != null)} " +
                $"botObjective={(BotDirector.Instance != null && BotDirector.Instance.Objective != null)}");
            Check("中立外觀 = 配色組中立色", ColorClose(zone.Visual.CurrentColor, match.Colors.Neutral),
                $"zone={zone.Visual.CurrentColor} neutral={match.Colors.Neutral}");

            // 快轉到對戰、全員鎖住
            match.DebugSetRemaining(290f);
            yield return null;
            ModeTestKit.LockAll(match, true);
            yield return null;
            zone.Sample();
            Check("開戰時區域無墨、雙方 100", zone.AlphaRatio < 0.01f && zone.BravoRatio < 0.01f && rules.Alpha.Count == 100
                                            && rules.Bravo.Count == 100 && rules.OwnerOf(0) == Team.None,
                $"alpha={zone.AlphaRatio:0.000} bravo={zone.BravoRatio:0.000}");
            yield return ModeTestKit.ShotZone(zone, "zones_1_neutral", true, null);

            Inkling alphaMember = FirstOf(match, Team.Alpha);
            Inkling bravoMember = FirstOf(match, Team.Bravo);
            var bravoGauge = bravoMember.GetComponent<PlayerSpecialGauge>();
            var alphaGauge = alphaMember.GetComponent<PlayerSpecialGauge>();
            bravoGauge.SetPointsForTest(0f);
            alphaGauge.SetPointsForTest(0f);

            // ── 2 佔領門檻 70% + 瞬間重塗 ──
            List<Vector3> points = ModeTestKit.PaintPoints(zone);
            float belowRatio = 0f;
            float captureRatio = 0f;
            Texture2D beforeView = null;
            Camera cam = Camera.main;
            var rig = cam.GetComponent<ThirdPersonCameraRig>();
            var source = new Painting.InkSource(Team.Alpha);
            int next = 0;
            while (next < points.Count && rules.OwnerOf(0) == Team.None)
            {
                Painting.InkPainter.PaintSphere(points[next++], 0.8f, 1f, source);
                zone.Sample();
                float after = zone.AlphaRatio;
                yield return null;
                if (rules.OwnerOf(0) == Team.None)
                {
                    belowRatio = after;
                    if (beforeView == null && after >= 0.64f)
                    {
                        beforeView = RenderZoneView(zone, cam, rig);
                    }
                }
                else
                {
                    captureRatio = after;
                }
            }
            zone.Sample();
            Check("佔領門檻 70%:前一發未滿不佔領、跨過 70% 的那一幀佔領",
                rules.OwnerOf(0) == Team.Alpha && belowRatio < SplatZonesRules.CaptureRatio
                && captureRatio >= SplatZonesRules.CaptureRatio - 1e-4f && belowRatio > 0.6f,
                $"未佔領時最高 {belowRatio:0.000} → 佔領時 {captureRatio:0.000},塗 {next} 發");
            // 畫面驗證:佔領時還沒噴到的區域內點,佔領前不是 Alpha 色、佔領後是;區域外 1.5m 以上的台地保持乾淨
            Texture2D afterView = RenderZoneView(zone, cam, rig);
            ShotCapture.Save(afterView, "zones_2a_recolor_check");
            List<Vector3> outside = OutsidePoints(zone);
            float beforeInside = beforeView != null ? AlphaFraction(beforeView, zone, cam, points, next, out int nBefore) : 1f;
            float afterInside = AlphaFraction(afterView, zone, cam, points, next, out int nAfter);
            float afterOutside = AlphaFraction(afterView, zone, cam, outside, 0, out int nOutside);
            Check("佔領瞬間整個區域重塗成 Alpha:歸屬 100%;畫面上區域內未噴點變 Alpha 色、區域外台地不變",
                zone.AlphaRatio > 0.999f && zone.BravoRatio < 0.001f && nAfter >= 10 && nOutside >= 10
                && beforeInside < 0.3f && afterInside > 0.85f && afterOutside < 0.05f,
                $"歸屬 {zone.AlphaRatio:0.0000};區域內未噴點 Alpha 色 {beforeInside:P0} → {afterInside:P0}(n={nAfter})," +
                $"區域外 {afterOutside:P0}(n={nOutside})");
            if (beforeView != null)
            {
                Destroy(beforeView);
            }
            Destroy(afterView);
            Check("佔領後外觀 / HUD 區域圖示 = Alpha 色、播報「我方佔領了區域!」",
                ColorClose(zone.Visual.CurrentColor, Painting.TeamPalette.ColorOf(Team.Alpha))
                && ColorClose(hud.ZoneIconColor(0), Painting.TeamPalette.ColorOf(Team.Alpha)) && hud.LastCallout == "我方佔領了區域!",
                $"visual={zone.Visual.CurrentColor} icon={hud.ZoneIconColor(0)} callout={hud.LastCallout}");
            yield return ModeTestKit.ShotZone(zone, "zones_2_captured", true, null);

            // ── 3 計數每 36F 減 1、被動充能 ──
            int f0 = rules.Frame;
            int c0 = rules.Alpha.Count;
            int p0 = rules.TickProgress(Team.Alpha);
            float t0 = Time.time;
            float g0 = bravoGauge.Points;
            float a0 = alphaGauge.Points;
            yield return new WaitForSeconds(3.6f);
            int f1 = rules.Frame;
            int c1 = rules.Alpha.Count;
            float t1 = Time.time;
            int expectedTicks = (p0 + (f1 - f0)) / SplatZonesRules.FramesPerCount;
            float fps = (f1 - f0) / Mathf.Max(0.001f, t1 - t0);
            Check("控制中每 36F 計數 −1(規則以 60 Hz 固定步推進)",
                c0 - c1 == expectedTicks && expectedTicks >= 5 && Mathf.Abs(fps - 60f) < 2.5f && rules.Bravo.Count == 100,
                $"{c0}→{c1}(−{c0 - c1}),規則幀 {f1 - f0} / {t1 - t0:0.00}s = {fps:0.0} Hz,預期 −{expectedTicks}");
            float expectedCharge = SplatZonesRules.EnemyControlChargePerSecond * (t1 - t0) * 100f / bravoGauge.CostPoints;
            float charged = bravoGauge.Points - g0;
            Check("被動充能:敵方控制全部區域 → Bravo 4.5 p/s、Alpha 0",
                Mathf.Abs(charged - expectedCharge) <= expectedCharge * 0.08f && Mathf.Abs(alphaGauge.Points - a0) < 1e-3f,
                $"Bravo 能量 +{charged:0.00}(預期 +{expectedCharge:0.00},{bravoGauge.CostPoints}p 大招)Alpha +{alphaGauge.Points - a0:0.000}");
            Check("HUD 計數顯示 = 規則計數", hud.AllyCountText == rules.Alpha.Count.ToString() && hud.EnemyCountText == "100",
                $"HUD {hud.AllyCountText} / {hud.EnemyCountText} 規則 {rules.Alpha.Count} / {rules.Bravo.Count}");

            yield return ModeTestKit.Capture(cam, "zones_2b_player_view", true, null);

            // ── 4 敵墨 50% → 中立 ──
            var bravoSource = new Painting.InkSource(Team.Bravo);
            float belowNeutral = 0f;
            float atNeutral = 0f;
            next = 0;
            while (next < points.Count && rules.OwnerOf(0) == Team.Alpha)
            {
                Painting.InkPainter.PaintSphere(points[next++], 0.8f, 1f, bravoSource);
                zone.Sample();
                float after = zone.BravoRatio;
                yield return null;
                if (rules.OwnerOf(0) == Team.Alpha)
                {
                    belowNeutral = after;
                }
                else
                {
                    atNeutral = after;
                }
            }
            int stalledCount = rules.Alpha.Count;
            Check("敵墨 50%:前一發未滿維持佔領、跨過 50% 變中立(不給罰分)",
                rules.OwnerOf(0) == Team.None && belowNeutral < SplatZonesRules.NeutralizeRatio && belowNeutral > 0.4f
                && atNeutral >= SplatZonesRules.NeutralizeRatio - 1e-4f && atNeutral < SplatZonesRules.CaptureRatio
                && rules.Alpha.Penalty == 0 && hud.LastCallout == "區域失守!",
                $"Bravo {belowNeutral:0.000} → {atNeutral:0.000} penalty={rules.Alpha.Penalty} callout={hud.LastCallout}");
            float n0 = bravoGauge.Points;
            float na0 = alphaGauge.Points;
            float nt0 = Time.time;
            yield return new WaitForSeconds(1.5f);
            float nt1 = Time.time;
            float neutralCharge = bravoGauge.Points - n0;
            float expectedNeutral = SplatZonesRules.BehindChargePerSecond * (nt1 - nt0) * 100f / bravoGauge.CostPoints;
            Check("中立:計數停止;無人控制且 Bravo 落後 → Bravo 1.5 p/s、Alpha 0",
                rules.Alpha.Count == stalledCount && Mathf.Abs(neutralCharge - expectedNeutral) <= expectedNeutral * 0.12f
                && Mathf.Abs(alphaGauge.Points - na0) < 1e-3f,
                $"計數 {stalledCount}→{rules.Alpha.Count} Bravo 能量 +{neutralCharge:0.000}(預期 +{expectedNeutral:0.000})");

            // ── 5 奪走 → 罰分 ──
            int stealCount = rules.Alpha.Count;
            while (next < points.Count && rules.OwnerOf(0) != Team.Bravo)
            {
                Painting.InkPainter.PaintSphere(points[next++], 0.8f, 1f, bravoSource);
                yield return null;
            }
            yield return null;
            int expectedPenalty = SplatZonesRules.PenaltyFor(100, stealCount);
            Check("Bravo 塗到 70% 奪走 → Alpha 罰分 = ROUND(0.75·(100 − end)) + 1、HUD 顯示 +N、播報「區域被奪走!」",
                rules.OwnerOf(0) == Team.Bravo && rules.Alpha.Penalty == expectedPenalty && expectedPenalty >= 2
                && hud.AllyPenaltyText == "+" + expectedPenalty && hud.LastCallout == "區域被奪走!",
                $"100→{stealCount} 罰分 {rules.Alpha.Penalty}(預期 {expectedPenalty})HUD={hud.AllyPenaltyText} callout={hud.LastCallout}");
            zone.Sample();
            Check("奪走瞬間區域重塗成 Bravo", zone.BravoRatio > 0.999f, $"Bravo {zone.BravoRatio:0.0000}");

            // ── 6 奪回:先扣罰分 ──
            next = 0;
            while (next < points.Count && rules.OwnerOf(0) != Team.Alpha)
            {
                Painting.InkPainter.PaintSphere(points[next++], 0.8f, 1f, source);
                yield return null;
            }
            int penaltyStart = rules.Alpha.Penalty;
            int countStart = rules.Alpha.Count;
            bool onlyPenaltyMoved = true;
            bool lastTickTogether = false;
            int lastPenalty = penaltyStart;
            int lastCount = countStart;
            float burnEnd = Time.time + (penaltyStart + 2) * 0.6f + 1f;
            while (Time.time < burnEnd && !lastTickTogether)
            {
                yield return null;
                int p = rules.Alpha.Penalty;
                int c = rules.Alpha.Count;
                if (p != lastPenalty || c != lastCount)
                {
                    if (p == 0 && lastPenalty == 1)
                    {
                        lastTickTogether = c == lastCount - 1;
                    }
                    else if (c != lastCount || p != lastPenalty - 1)
                    {
                        onlyPenaltyMoved = false;
                    }
                    lastPenalty = p;
                    lastCount = c;
                }
            }
            Check("奪回後先扣罰分,最後 1 點罰分歸 0 的同一刻本計數 −1(40+2 → 40+1 → 39)",
                penaltyStart >= 2 && onlyPenaltyMoved && lastTickTogether && rules.Alpha.Count == countStart - 1,
                $"罰分 {penaltyStart}→{rules.Alpha.Penalty} 計數 {countStart}→{rules.Alpha.Count} onlyPenalty={onlyPenaltyMoved}");

            // ── 7 延長戰 ──
            rules.DebugSetScore(Team.Alpha, 40, 0);
            rules.DebugSetScore(Team.Bravo, 30, 0);
            match.DebugSetRemaining(0.6f);
            yield return WaitUntil(() => match.Timeline.InOvertime || match.Phase != MatchPhase.Battle, 3f);
            yield return null;
            Check("落後方(Alpha)持有區域時時間到 → 延長戰(計時停在 0、仍在對戰、加時框、播報)",
                match.Timeline.InOvertime && match.Phase == MatchPhase.Battle && rules.InOvertime && rules.OvertimeLoser == Team.Alpha
                && match.Timeline.BattleRemaining <= 0f && hud.OvertimeBoxVisible && hud.LastCallout == "延長戰!"
                && rules.OvertimeGrace01 > 0.99f,
                $"overtime={match.Timeline.InOvertime} phase={match.Phase} loser={rules.OvertimeLoser} box={hud.OvertimeBoxVisible} " +
                $"callout={hud.LastCallout}");
            rig.enabled = true;
            yield return ModeTestKit.Capture(cam, "zones_3_overtime_hud", true, null);
            // 領先方(Bravo)塗:先中立(寬限開始流乾)再佔領 → 加時結束
            next = 0;
            while (next < points.Count && rules.OwnerOf(0) == Team.Alpha)
            {
                Painting.InkPainter.PaintSphere(points[next++], 0.8f, 1f, bravoSource);
                yield return null;
            }
            yield return new WaitForSeconds(0.5f);
            bool stillOvertime = match.Phase == MatchPhase.Battle && match.Timeline.InOvertime && rules.OvertimeGrace01 < 0.99f;
            while (next < points.Count && rules.OwnerOf(0) != Team.Bravo && match.Phase == MatchPhase.Battle)
            {
                Painting.InkPainter.PaintSphere(points[next++], 0.8f, 1f, bravoSource);
                yield return null;
            }
            yield return WaitUntil(() => match.Phase != MatchPhase.Battle, 2f);
            Check("加時:中立時寬限流乾但不結束;領先方佔領 → 加時結束(GAME!)",
                stillOvertime && match.Phase >= MatchPhase.Finish && !match.EndedByKnockout && match.Timeline.WentToOvertime,
                $"中立時仍加時={stillOvertime} phase={match.Phase} KO={match.EndedByKnockout}");
            yield return ModeTestKit.WaitPhase(match, MatchPhase.Judging, 6f);
            yield return new WaitForSeconds(5f);
            AnarchyResult result = match.RankedResult;
            Check("判定 = 計數:Bravo 30 勝、延長戰標記、Verdict 勝方一致",
                match.HasVerdict && result.Winner == Team.Bravo && result.BravoCount <= 30 && !result.Knockout && result.WentToOvertime
                && match.Verdict.Winner == Team.Bravo,
                $"winner={result.Winner} {result.AlphaCount} vs {result.BravoCount} OT={result.WentToOvertime}");
            yield return ModeTestKit.Capture(cam, "zones_4_judge", true, null);

            // ── 8 KNOCKOUT ──
            yield return ModeTestKit.ReloadWithMode(MatchMode.SplatZones);
            match = MatchController.Instance;
            objective = ObjectiveModeController.Instance;
            driver = objective != null ? objective.SplatZones : null;
            if (match == null || driver == null)
            {
                FailSetup("第二場真格區域沒有開起來");
                yield break;
            }
            rules = driver.ZoneRules;
            zone = driver.Zones[0];
            match.DebugSetRemaining(280f);
            yield return null;
            ModeTestKit.LockAll(match, true);
            rules.DebugSetScore(Team.Alpha, 2, 0);
            points = ModeTestKit.PaintPoints(zone);
            source = new Painting.InkSource(Team.Alpha);
            next = 0;
            while (next < points.Count && rules.OwnerOf(0) != Team.Alpha)
            {
                Painting.InkPainter.PaintSphere(points[next++], 0.8f, 1f, source);
                yield return null;
            }
            int captureFrame = rules.Frame;
            yield return WaitUntil(() => match.Phase != MatchPhase.Battle, 4f);
            int koFrames = rules.Frame - captureFrame;
            Check("計數 2 佔領 → 72F 內 KNOCKOUT,對戰提前結束",
                match.EndedByKnockout && match.Phase == MatchPhase.Finish && rules.KnockoutTeam == Team.Alpha && rules.Alpha.Count == 0
                && match.Timeline.BattleRemaining > 250f && koFrames <= 2 * SplatZonesRules.FramesPerCount,
                $"KO={match.EndedByKnockout} phase={match.Phase} 剩餘 {match.Timeline.BattleRemaining:0.0}s 佔領後 {koFrames}F");
            cam = Camera.main;
            yield return new WaitForSeconds(0.35f);
            yield return ModeTestKit.Capture(cam, "zones_5_knockout", true, null);
            yield return ModeTestKit.WaitPhase(match, MatchPhase.Judging, 6f);
            result = match.RankedResult;
            Check("判定 KNOCKOUT:Alpha 勝", result.Knockout && result.Winner == Team.Alpha && match.Verdict.Winner == Team.Alpha,
                $"KO={result.Knockout} winner={result.Winner} {result.AlphaCount} vs {result.BravoCount}");
            yield return ModeTestKit.WaitPhase(match, MatchPhase.Results, 12f);
            yield return new WaitForSeconds(0.5f);
            Check("結算畫面出現(蠻頹比分)", match.Phase == MatchPhase.Results, $"phase={match.Phase}");
            yield return ModeTestKit.Capture(cam, "zones_6_results", true, null);
            Finish();
        }

        private static Inkling FirstOf(MatchController match, Team team)
        {
            foreach (Inkling inkling in match.Roster)
            {
                if (inkling != null && inkling.Team == team)
                {
                    return inkling;
                }
            }
            return null;
        }

        private static bool ColorClose(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) < 0.02f && Mathf.Abs(a.g - b.g) < 0.02f && Mathf.Abs(a.b - b.b) < 0.02f;
        }

        private const int ViewWidth = 640;
        private const int ViewHeight = 360;

        private static void PoseZoneView(SplatZone zone, Camera cam)
        {
            Vector3 eye = zone.Center + new Vector3(-14f, 15f, -19f);
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(zone.Center + Vector3.up * 0.5f - eye));
        }

        // 從區域斜上方(固定機位)渲染一張;區域外觀暫時關掉,只看地面的墨
        private static Texture2D RenderZoneView(SplatZone zone, Camera cam, ThirdPersonCameraRig rig)
        {
            bool rigWasEnabled = rig != null && rig.enabled;
            if (rig != null)
            {
                rig.enabled = false;
            }
            Renderer[] parts = zone.GetComponentsInChildren<Renderer>();
            foreach (Renderer part in parts)
            {
                part.enabled = false;
            }
            PoseZoneView(zone, cam);
            Texture2D texture = ShotCapture.Render(cam, ViewWidth, ViewHeight);
            foreach (Renderer part in parts)
            {
                part.enabled = true;
            }
            if (rig != null)
            {
                rig.enabled = rigWasEnabled;
            }
            return texture;
        }

        // points[from..] 裡(從機位看得到的)在畫面上呈 Alpha 色的比例
        private static float AlphaFraction(Texture2D view, SplatZone zone, Camera cam, List<Vector3> points, int from, out int total)
        {
            Vector3 savedPos = cam.transform.position;
            Quaternion savedRot = cam.transform.rotation;
            PoseZoneView(zone, cam);
            cam.aspect = ViewWidth / (float)ViewHeight;
            Color.RGBToHSV(Painting.TeamPalette.ColorOf(Team.Alpha), out float targetHue, out _, out _);
            int hits = 0;
            total = 0;
            int mask = ~GameLayers.InklingMask;
            for (int i = from; i < points.Count; i++)
            {
                Vector3 p = points[i];
                Vector3 v = cam.WorldToViewportPoint(p);
                if (v.z <= 0f || v.x < 0.02f || v.x > 0.98f || v.y < 0.02f || v.y > 0.98f
                    || Physics.Linecast(cam.transform.position, p + Vector3.up * 0.08f, mask, QueryTriggerInteraction.Ignore))
                {
                    continue;
                }
                total++;
                Color c = view.GetPixel((int)(v.x * ViewWidth), (int)(v.y * ViewHeight));
                Color.RGBToHSV(c, out float h, out float sat, out float val);
                float d = Mathf.Abs(h - targetHue);
                d = Mathf.Min(d, 1f - d);
                if (sat > 0.35f && val > 0.15f && d < 0.06f)
                {
                    hits++;
                }
            }
            cam.ResetAspect();
            cam.transform.SetPositionAndRotation(savedPos, savedRot);
            return total > 0 ? hits / (float)total : 0f;
        }

        // 區域外的台地取樣點:距區域邊界 1.5m 以上、同一層地面(從上方射線打到 y≈區域高度)
        private static List<Vector3> OutsidePoints(SplatZone zone)
        {
            var points = new List<Vector3>();
            int mask = ~GameLayers.InklingMask;
            for (float z = zone.Center.z - 11f; z <= zone.Center.z + 11f; z += 1.5f)
            {
                for (float x = zone.Center.x - 19f; x <= zone.Center.x + 19f; x += 1.5f)
                {
                    var p = new Vector3(x, zone.Center.y, z);
                    if (zone.Contains(p) || zone.DistanceToEdge(p) < 1.5f)
                    {
                        continue;
                    }
                    if (Physics.Raycast(new Vector3(x, zone.Center.y + 10f, z), Vector3.down, out RaycastHit hit, 12f, mask,
                            QueryTriggerInteraction.Ignore) && Mathf.Abs(hit.point.y - zone.Center.y) < 0.1f)
                    {
                        points.Add(hit.point + Vector3.up * 0.02f);
                    }
                }
            }
            return points;
        }
    }
}

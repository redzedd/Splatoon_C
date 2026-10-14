using System.Collections;
using SplatoonC.Core;
using SplatoonC.Core.Match;
using SplatoonC.Gameplay.AI;
using SplatoonC.Gameplay.CameraRig;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Match;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SplatoonC.Gameplay.Debugging
{
    // V1 角色外觀驗收(試射場 → 對戰場景):
    //   接線:人形/烏賊模型在、舊膠囊移除、Muzzle 原位、槍掛在右手;
    //   隊伍色:MPB 染色讀回 = TeamPalette、換隊/換配色即時更新;墨水槽液面跟著墨量(清空 → ~0、回滿 → 1);
    //   動作:跑步時腿骨真的在轉且面向移動方向、待機不是 T/A-pose、開火舉槍對準準心 + 後座力、
    //        按住副武器左手後拉並拿著副武器、大招發動高舉;烏賊態換模型且不被 Visual 壓扁、乾地觸手擺動、
    //        貼牆時頭朝上;擊倒隱藏、重生恢復人形;本元件每幀零配置。
    //   對戰:8 角色(本機玩家也交給 bot)跑 60 秒遊戲時間無例外,兩隊顏色正確,記錄 CharacterVisual 總成本。
    [AutoTest(Scene = AutoTestAttribute.TrainingScene, Timeout = 200f, Order = 300)]
    public sealed class CharacterVisualAutoTest : AutoTestBase
    {
        private int _exceptions;
        private string _firstException;

        public static void Run()
        {
            Launch<CharacterVisualAutoTest>();
        }

        private void OnEnable()
        {
            Application.logMessageReceived += OnLog;
        }

        private void OnDisable()
        {
            Application.logMessageReceived -= OnLog;
        }

        private void OnDestroy()
        {
            Time.timeScale = 1f;
            CharacterVisual.MeasureAllocations = false;
        }

        private void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Exception)
            {
                _exceptions++;
                if (_firstException == null)
                {
                    _firstException = message;
                }
            }
        }

        private IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            GameObject player = FindPlayer();
            var visual = player != null ? player.GetComponent<CharacterVisual>() : null;
            var rig = Camera.main != null ? Camera.main.GetComponent<ThirdPersonCameraRig>() : null;
            if (visual == null || rig == null)
            {
                FailSetup("Player 上沒有 CharacterVisual 或沒有相機 rig");
                yield break;
            }
            var intent = ScriptedIntent.Drive(player.transform);
            var inkling = player.GetComponent<Inkling>();
            var tank = player.GetComponent<PlayerInkTank>();
            var squid = player.GetComponent<SquidController>();
            var locomotion = player.GetComponent<PlayerLocomotion>();
            var thrower = player.GetComponent<ThrowController>();
            var gauge = player.GetComponent<PlayerSpecialGauge>();
            var lifecycle = player.GetComponent<InklingLifecycle>();
            var health = player.GetComponent<InklingHealth>();
            var loadout = player.GetComponent<WeaponLoadout>();
            Transform visualRoot = player.transform.Find("Visual");
            HumanoidRig humanoid = visual.Rig;

            // ── 1. 接線 ──
            Transform gun = HumanoidRig.FindDeep(visualRoot, "GunBarrel");
            Transform muzzle = visualRoot.Find("Muzzle");
            Check("模型接線", visual.IsReady && humanoid.IsValid && visual.InklingModel != null && visual.SquidModel != null
                                && visual.InklingModel.gameObject.activeSelf && !visual.SquidModel.gameObject.activeSelf,
                $"ready={visual.IsReady} 人形啟用={visual.InklingModel.gameObject.activeSelf} " +
                $"烏賊啟用={visual.SquidModel.gameObject.activeSelf}");
            Check("舊膠囊移除、Muzzle 原位",
                visualRoot.Find("Body") == null && muzzle != null
                && (muzzle.localPosition - new Vector3(0.52f, 1.05f, 1.05f)).sqrMagnitude < 1e-4f,
                $"Body={(visualRoot.Find("Body") != null ? "還在" : "無")} Muzzle={(muzzle != null ? muzzle.localPosition.ToString("F2") : "無")}");
            Check("槍握在右手", gun != null && gun.parent == humanoid.WeaponSocket
                               && humanoid.WeaponSocket.parent == humanoid.BoneTransform(HumanoidRig.Bone.HandR)
                               && visual.WeaponModel == gun,
                $"GunBarrel 父物件={(gun != null && gun.parent != null ? gun.parent.name : "無")} " +
                $"WeaponModel={(visual.WeaponModel != null ? visual.WeaponModel.name : "無")}");

            Teleport(player.transform, new Vector3(-10f, 0.1f, -10f));
            rig.SetAngles(0f, 8f);
            yield return new WaitForSeconds(0.8f);

            // ── 2. 隊伍色 ──
            SkinnedMeshRenderer body = FindBodyRenderer(visual.InklingModel);
            int tentacleIndex = MaterialIndex(body, "Tentacle");
            bool readOk = CharacterTint.TryReadBaseColor(body, tentacleIndex, out Color tentacle);
            Check("觸手染成隊伍色", readOk && ColorClose(tentacle, TeamPalette.ColorOf(inkling.Team)) && visual.TintSlotCount >= 4,
                $"讀回={tentacle} 隊伍色={TeamPalette.ColorOf(inkling.Team)} 染色子網格={visual.TintSlotCount}(觸手/墨水/烏賊/槍墨罐)");
            Color oldAlpha = TeamPalette.Alpha;
            Color oldBravo = TeamPalette.Bravo;
            var testColor = new Color(0.2f, 0.85f, 0.3f, 1f);
            TeamPalette.Set(testColor, oldBravo);
            yield return null;
            yield return null;
            CharacterTint.TryReadBaseColor(body, tentacleIndex, out Color afterPalette);
            TeamPalette.Set(oldAlpha, oldBravo);
            inkling.Configure(Team.Bravo, inkling.DisplayName, inkling.IsLocalPlayer);
            yield return null;
            yield return null;
            CharacterTint.TryReadBaseColor(body, tentacleIndex, out Color afterTeam);
            inkling.Configure(Team.Alpha, inkling.DisplayName, inkling.IsLocalPlayer);
            yield return null;
            Check("換配色/換隊即時更新", ColorClose(afterPalette, testColor) && ColorClose(afterTeam, oldBravo),
                $"換配色後={afterPalette}(期望 {testColor}) 換隊後={afterTeam}(期望 {oldBravo})");

            // ── 3. 墨水槽 ──
            Transform fill = humanoid.InkTankFill;
            Drain(tank);
            tank.LockRefill(5f);
            yield return new WaitForSeconds(0.4f);
            float emptyScale = fill != null ? fill.localScale.y : -1f;
            tank.RefillFull();
            tank.LockRefill(0f);
            yield return new WaitForSeconds(0.4f);
            float fullScale = fill != null ? fill.localScale.y : -1f;
            Check("墨水槽液面跟墨量", fill != null && emptyScale < 0.08f && fullScale > 0.95f,
                $"清空後 y 縮放={emptyScale:F3}(墨量 {tank.Normalized:F2} 前)回滿後={fullScale:F3}");

            // ── 4. 待機不是 A-pose:手垂在身側 ──
            Vector3 shoulderL = humanoid.BoneTransform(HumanoidRig.Bone.UpperArmL).position;
            Vector3 handL = humanoid.BoneTransform(HumanoidRig.Bone.HandL).position;
            Check("待機手臂下垂(非 T/A-pose)", shoulderL.y - handL.y > 0.3f,
                $"左肩 y={shoulderL.y:F2} 左手 y={handL.y:F2}(A-pose 時差約 0.22)");

            // ── 5. 跑步:腿骨真的在轉、面向移動方向 ──
            Transform thigh = humanoid.BoneTransform(HumanoidRig.Bone.UpperLegL);
            Transform shin = humanoid.BoneTransform(HumanoidRig.Bone.LowerLegL);
            intent.Move = new Vector2(0f, 1f);
            yield return new WaitForSeconds(0.4f);
            Quaternion thighRef = thigh.localRotation;
            float thighRange = 0f;
            float kneeMin = 999f;
            float kneeMax = -999f;
            float facingMin = 1f;
            CharacterController controller = player.GetComponent<CharacterController>();
            float end = Time.time + 0.7f;
            while (Time.time < end)
            {
                thighRange = Mathf.Max(thighRange, Quaternion.Angle(thighRef, thigh.localRotation));
                float knee = Vector3.Angle(thigh.up, shin.up);
                kneeMin = Mathf.Min(kneeMin, knee);
                kneeMax = Mathf.Max(kneeMax, knee);
                Vector3 v = controller.velocity;
                v.y = 0f;
                if (v.sqrMagnitude > 1f)
                {
                    facingMin = Mathf.Min(facingMin, Vector3.Dot(visual.InklingModel.forward, v.normalized));
                }
                yield return null;
            }
            intent.Move = Vector2.zero;
            Check("跑步腿骨擺動", thighRange > 30f && kneeMax - kneeMin > 25f,
                $"大腿轉動幅度={thighRange:F1}° 膝蓋彎曲 {kneeMin:F0}°~{kneeMax:F0}°");
            Check("面向移動方向", facingMin > 0.9f, $"模型前方 · 速度方向 最小={facingMin:F2}");
            yield return new WaitForSeconds(0.6f);

            // ── 6. 開火:舉槍對準準心、後座力 ──
            Teleport(player.transform, new Vector3(-10f, 0.1f, -10f));
            rig.SetAngles(0f, 5f);
            tank.InfiniteInk = true;
            MainWeapon main = loadout != null ? loadout.Main : player.GetComponent<MainWeapon>();
            int shotsBefore = main != null ? main.AttackCount : 0;
            intent.Attack = true;
            yield return new WaitForSeconds(0.1f);
            float aimAtRaise = visual.AimWeight;
            float maxRecoil = 0f;
            float worstGunAngle = 0f;
            end = Time.time + 0.6f;
            while (Time.time < end)
            {
                maxRecoil = Mathf.Max(maxRecoil, visual.Recoil);
                // 準心點(視線 12 m 處)相對胸口的方向;允許後座力的槍口上揚
                Vector3 chest = player.transform.position + Vector3.up * 1.15f;
                Vector3 aimDir = rig.AimOrigin + rig.AimForward * 12f - chest;
                worstGunAngle = Mathf.Max(worstGunAngle, Vector3.Angle(gun.forward, aimDir));
                yield return null;
            }
            Vector3 handR = humanoid.BoneTransform(HumanoidRig.Bone.HandR).position;
            int shots = main != null ? main.AttackCount - shotsBefore : 0;
            intent.Attack = false;
            Check("開火 0.1 秒內舉槍", aimAtRaise > 0.9f, $"舉槍權重={aimAtRaise:F2}(S3 人形第一發 3F)");
            Check("槍口對準準心", worstGunAngle < 12f && handR.y - player.transform.position.y > 0.85f,
                $"槍管與視線夾角最大={worstGunAngle:F1}° 右手高度={handR.y - player.transform.position.y:F2} m");
            Check("每發後座力", shots >= 4 && maxRecoil > 0.3f,
                $"0.7 秒出手 {shots} 發(S3 6F 一發)後座力峰值={maxRecoil:F2}");
            tank.InfiniteInk = false;
            yield return new WaitForSeconds(0.8f);

            // ── 7. 副武器:按住左手後拉拿著副武器、放開甩出 ──
            tank.RefillFull();
            intent.Sub = true;
            yield return new WaitForSeconds(0.45f);
            Transform head = humanoid.BoneTransform(HumanoidRig.Bone.Head);
            Vector3 handHold = humanoid.BoneTransform(HumanoidRig.Bone.HandL).position;
            GameObject held = HumanoidRig.FindDeep(visual.InklingModel, "HeldSub") != null
                ? HumanoidRig.FindDeep(visual.InklingModel, "HeldSub").gameObject : null;
            bool heldShown = held != null && held.activeInHierarchy;
            float holdWeight = visual.ThrowWeight;
            float handAboveHead = handHold.y - head.position.y;
            int throwsBefore = thrower.ThrowCount;
            intent.Sub = false;
            yield return new WaitForSeconds(0.1f);
            bool releasing = visual.ThrowWeight > 0.5f;
            bool heldHidden = held == null || !held.activeInHierarchy;
            Check("按住副武器蓄勢", holdWeight > 0.9f && heldShown && handAboveHead > -0.05f,
                $"權重={holdWeight:F2} 手上副武器={heldShown} 左手比頭高={handAboveHead:F2} m");
            Check("放開甩手", thrower.ThrowCount == throwsBefore + 1 && releasing && heldHidden,
                $"投擲數 {throwsBefore}→{thrower.ThrowCount} 甩手中={releasing} 手上副武器已丟出={heldHidden}");
            yield return new WaitForSeconds(0.8f);

            // ── 8. 大招發動高舉 ──
            gauge.SetPointsForTest(gauge.CostPoints);
            yield return null;
            intent.PressSpecial();
            yield return new WaitForSeconds(0.25f);
            bool flourish = visual.SpecialFlourishing;
            float leftHandUp = humanoid.BoneTransform(HumanoidRig.Bone.HandL).position.y - head.position.y;
            Check("大招發動高舉雙手", gauge.IsActive && flourish && leftHandUp > -0.05f,
                $"啟動={gauge.IsActive} 高舉中={flourish} 左手比頭高={leftHandUp:F2} m");
            gauge.EndActive();
            yield return new WaitForSeconds(0.6f);

            // ── 9. 烏賊態:換模型、不被壓扁、乾地觸手擺動 ──
            // 乾地(前面的射擊/炸彈/大招可能已經塗到附近):找一塊腳下沒墨的地方
            Vector3[] drySpots = { new Vector3(20f, 0.1f, -20f), new Vector3(-20f, 0.1f, 20f), new Vector3(20f, 0.1f, 20f) };
            for (int i = 0; i < drySpots.Length; i++)
            {
                Teleport(player.transform, drySpots[i]);
                yield return new WaitForSeconds(0.2f);
                if (squid.GroundOwner == 0)
                {
                    break;
                }
            }
            yield return new WaitForSeconds(0.1f);
            intent.Squid = true;
            yield return null;
            yield return null;
            bool swapped = visual.IsSquidForm && !visual.InklingModel.gameObject.activeSelf
                           && visual.SquidModel.gameObject.activeSelf;
            yield return new WaitForSeconds(0.5f);
            float squidHeight = visual.SquidMesh.lossyScale.y;
            float visualY = visualRoot.localScale.y;
            Check("按住烏賊鍵換成烏賊模型", swapped, $"烏賊形態={visual.IsSquidForm}");
            Check("烏賊不被 Visual 壓扁", squidHeight > 0.8f && squidHeight < 1.3f && visualY < 0.5f,
                $"烏賊世界 y 縮放={squidHeight:F2}(Visual={visualRoot.localScale:F2} SquidModel={visual.SquidModel.localScale:F2} " +
                $"Squid={visual.SquidMesh.localScale:F2} 腳下自家墨={squid.OnOwnInk} 下沉={squid.DiveProgress:F2})");
            Transform tentacle3 = HumanoidRig.FindDeep(visual.SquidMesh, "Tentacle_3");
            Quaternion t0 = tentacle3.localRotation;
            float tentacleSwing = 0f;
            intent.Move = new Vector2(0f, 1f);
            end = Time.time + 0.5f;
            float hopMax = 0f;
            while (Time.time < end)
            {
                tentacleSwing = Mathf.Max(tentacleSwing, Quaternion.Angle(t0, tentacle3.localRotation));
                hopMax = Mathf.Max(hopMax, visual.SquidMesh.localPosition.y);
                yield return null;
            }
            intent.Move = Vector2.zero;
            Check("烏賊觸手擺動、乾地跳著走", tentacleSwing > 8f && hopMax > 0.04f,
                $"觸手擺動={tentacleSwing:F1}° 跳躍高度={hopMax:F2} m");
            intent.Squid = false;
            yield return new WaitForSeconds(0.4f);
            Check("放開變回人形", !visual.IsSquidForm && visual.InklingModel.gameObject.activeSelf,
                $"烏賊形態={visual.IsSquidForm}");

            // ── 10. 爬牆:頭朝上、背朝牆外 ──
            PaintableSurface wall = null;
            foreach (PaintableSurface surface in PaintableSurface.Active)
            {
                if (surface.name == "ClimbWall_High")
                {
                    wall = surface;
                }
            }
            if (wall != null)
            {
                for (int i = 0; i <= 4; i++)
                {
                    wall.Paint(new Vector3(12f, i, 0f), 1.2f, Team.Alpha, 0.7f);
                }
                Teleport(player.transform, new Vector3(10.2f, 0.1f, 0f));
                rig.SetAngles(90f, 10f);
                yield return null;
                intent.Squid = true;
                intent.Move = new Vector2(0f, 1f);
                end = Time.time + 2f;
                while (!locomotion.IsInsideInkedWall && Time.time < end)
                {
                    yield return null;
                }
                // 朝向平滑半衰期 0.02 s:等 0.1 秒
                yield return new WaitForSeconds(0.1f);
                bool onWall = locomotion.IsInsideInkedWall;
                float up = Vector3.Dot(visual.SquidMesh.forward, Vector3.up);
                float back = Vector3.Dot(visual.SquidMesh.up, locomotion.ClimbWallNormal);
                intent.Move = Vector2.zero;
                intent.Squid = false;
                Check("爬牆頭朝上背朝外", onWall && up > 0.85f && back > 0.85f,
                    $"貼牆={onWall} 頭·上={up:F2} 背·牆法線={back:F2}");
                yield return new WaitForSeconds(1.2f);
            }
            else
            {
                Check("爬牆頭朝上背朝外", false, "找不到 ClimbWall_High");
            }

            // ── 11. 擊倒隱藏、重生恢復 ──
            Teleport(player.transform, new Vector3(-10f, 0.1f, -10f));
            yield return new WaitForSeconds(0.3f);
            health.ApplyDamage(new DamageInfo(500f, player.transform.position, Team.Bravo, null, DamageKind.Environment));
            yield return null;
            bool hidden = lifecycle.IsDead && !visualRoot.gameObject.activeInHierarchy;
            lifecycle.ForceRespawnNow();
            yield return new WaitForSeconds(0.2f);
            bool back2 = !lifecycle.IsDead && visualRoot.gameObject.activeInHierarchy && !visual.IsSquidForm
                         && visual.InklingModel.gameObject.activeSelf;
            Check("擊倒隱藏、重生恢復人形", hidden && back2, $"擊倒時隱藏={hidden} 重生後可見人形={back2}");

            // ── 12. 零配置(本元件 LateUpdate 內)──
            CharacterVisual.ResetCostCounters();
            CharacterVisual.MeasureAllocations = true;
            tank.InfiniteInk = true;
            intent.Move = new Vector2(0.3f, 1f);
            intent.Attack = true;
            yield return new WaitForSeconds(0.8f);
            intent.Attack = false;
            intent.Squid = true;
            yield return new WaitForSeconds(0.5f);
            intent.Squid = false;
            intent.Move = Vector2.zero;
            yield return new WaitForSeconds(0.3f);
            CharacterVisual.MeasureAllocations = false;
            tank.InfiniteInk = false;
            Check("每幀零配置", CharacterVisual.AllocatedBytes == 0 && CharacterVisual.TotalSamples > 30,
                $"{CharacterVisual.TotalSamples} 次更新共配置 {CharacterVisual.AllocatedBytes} bytes");
            Check("試射場無例外", _exceptions == 0, $"例外 {_exceptions} 次 {_firstException}");
            player.GetComponent<PlayerInputRouter>().ClearOverrideSource();

            // ── 13. 對戰場景:8 角色 60 秒 ──
            yield return MatchSoak();
            Finish();
        }

        private IEnumerator MatchSoak()
        {
            SceneManager.LoadScene(AutoTestAttribute.MatchScene);
            yield return null;
            float until = Time.time + 20f;
            MatchController match;
            while ((match = MatchController.Instance) == null && Time.time < until)
            {
                yield return null;
            }
            if (match == null)
            {
                Check("對戰場景載入", false, "20 秒內沒有 MatchController");
                yield break;
            }
            while (match.LocalPlayer == null && Time.time < until)
            {
                yield return null;
            }
            if (match.LocalPlayer != null && match.LocalPlayer.GetComponent<BotBrain>() == null)
            {
                match.LocalPlayer.gameObject.AddComponent<BotBrain>().Configure(BotBrain.Difficulty.Normal);
            }
            int ready = 0;
            foreach (Inkling inkling in match.Roster)
            {
                var cv = inkling != null ? inkling.GetComponent<CharacterVisual>() : null;
                if (cv != null && cv.IsReady)
                {
                    ready++;
                }
            }
            Check("8 個角色都有角色外觀(bot 同 prefab)", ready == match.Roster.Count && ready == 8,
                $"就緒 {ready}/{match.Roster.Count}");
            while (match.Phase < MatchPhase.Battle)
            {
                yield return null;
            }
            int exceptionsBefore = _exceptions;
            Time.timeScale = 2f;
            CharacterVisual.ResetCostCounters();
            CharacterVisual.MeasureAllocations = true;
            int frames = 0;
            float battleEnd = Time.time + 60f;
            while (Time.time < battleEnd)
            {
                frames++;
                yield return null;
            }
            CharacterVisual.MeasureAllocations = false;
            Time.timeScale = 1f;
            double totalMs = CharacterVisual.TicksToMilliseconds(CharacterVisual.TotalTicks);
            double perFrame = frames > 0 ? totalMs / frames : 0;
            double perCharacter = CharacterVisual.TotalSamples > 0 ? totalMs * 1000.0 / CharacterVisual.TotalSamples : 0;
            Debug.Log($"[AUTOTEST] CharacterVisual 成本:{frames} 幀、{CharacterVisual.TotalSamples} 次角色更新,"
                      + $"平均每幀 {perFrame:F3} ms(全部角色)、每角色每次 {perCharacter:F1} µs;配置 {CharacterVisual.AllocatedBytes} bytes");
            Check("對戰 60 秒無例外", _exceptions == exceptionsBefore, $"例外 {_exceptions - exceptionsBefore} 次 {_firstException}");
            Check("對戰中 CharacterVisual 成本與配置", perFrame < 1.5 && CharacterVisual.AllocatedBytes == 0,
                $"平均每幀 {perFrame:F3} ms、每角色 {perCharacter:F1} µs、配置 {CharacterVisual.AllocatedBytes} bytes");

            int alphaOk = 0;
            int bravoOk = 0;
            int alphaCount = 0;
            int bravoCount = 0;
            foreach (Inkling inkling in match.Roster)
            {
                var cv = inkling.GetComponent<CharacterVisual>();
                bool ok = cv != null && ColorClose(cv.TintColor, TeamPalette.ColorOf(inkling.Team));
                if (inkling.Team == Team.Alpha)
                {
                    alphaCount++;
                    alphaOk += ok ? 1 : 0;
                }
                else
                {
                    bravoCount++;
                    bravoOk += ok ? 1 : 0;
                }
            }
            Check("兩隊隊伍色", alphaOk == alphaCount && bravoOk == bravoCount && alphaCount == 4 && bravoCount == 4,
                $"Alpha {alphaOk}/{alphaCount}({TeamPalette.Alpha})Bravo {bravoOk}/{bravoCount}({TeamPalette.Bravo})");
        }

        private static SkinnedMeshRenderer FindBodyRenderer(Transform model)
        {
            foreach (SkinnedMeshRenderer smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.name == "Body")
                {
                    return smr;
                }
            }
            return null;
        }

        private static int MaterialIndex(Renderer renderer, string materialName)
        {
            if (renderer == null)
            {
                return -1;
            }
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] != null && materials[i].name == materialName)
                {
                    return i;
                }
            }
            return -1;
        }

        // MaterialPropertyBlock 讀回的顏色可能經過色彩空間轉換:gamma 或 linear 任一接近都算
        private static bool ColorClose(Color a, Color b)
        {
            return Diff(a, b) < 0.03f || Diff(a, b.linear) < 0.03f || Diff(a.linear, b) < 0.03f;
        }

        private static float Diff(Color a, Color b)
        {
            return Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b)));
        }
    }
}

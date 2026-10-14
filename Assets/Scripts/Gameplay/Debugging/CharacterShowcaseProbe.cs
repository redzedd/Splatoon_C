using System.Collections;
using SplatoonC.Core;
using SplatoonC.Core.Match;
using SplatoonC.Gameplay.AI;
using SplatoonC.Gameplay.CameraRig;
using SplatoonC.Gameplay.Match;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SplatoonC.Gameplay.Debugging
{
    // 角色外觀截圖探針:試射場(待機/跑步/開火/投擲/跳躍/烏賊乾地/烏賊出墨跳/爬牆/大招)→ 載入對戰場景
    //(兩隊 bot 同框、隊伍色特寫)。PNG 存在 <專案>/Logs/shots/,給人眼(與 Read 工具)逐張檢查。
    // 斷言只做健全性:每張主角鏡頭「關掉角色 renderer 前後」差異像素要夠多(角色真的畫出來了)、
    // 對戰特寫要看得到兩隊的隊伍色。
    [AutoTest(Scene = AutoTestAttribute.TrainingScene, Timeout = 240f, Order = 960)]
    public sealed class CharacterShowcaseProbe : AutoTestBase
    {
        private Camera _studio;
        private GameObject _player;

        public static void Run()
        {
            Launch<CharacterShowcaseProbe>();
        }

        private void OnDestroy()
        {
            Time.captureDeltaTime = 0f;
        }

        private IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            // 固定 60 Hz 步長(批次模式實際幀率上千、截圖那幀又會卡幾百毫秒;固定步長連拍才有代表性)
            Time.captureDeltaTime = 1f / 60f;
            _player = FindPlayer();
            var rig = Camera.main != null ? Camera.main.GetComponent<ThirdPersonCameraRig>() : null;
            PaintableSurface ground = FindSurface("Ground");
            PaintableSurface wall = FindSurface("ClimbWall_High");
            if (_player == null || rig == null || ground == null || wall == null
                || _player.GetComponent<CharacterVisual>() == null)
            {
                FailSetup("Player / 相機 rig / Ground / ClimbWall_High / CharacterVisual 缺一");
                yield break;
            }
            CreateStudio();
            var intent = ScriptedIntent.Drive(_player.transform);
            var tank = _player.GetComponent<PlayerInkTank>();
            tank.InfiniteInk = true;

            // ── 待機 ──
            Teleport(_player.transform, new Vector3(-10f, 0.1f, -10f));
            SetVisualYaw(0f);
            rig.SetAngles(0f, 12f);
            yield return new WaitForSeconds(1.2f);
            GameShot("c01_idle_game");
            StudioShot("c02_idle_front", new Vector3(0f, 1.0f, 3.0f), 0.8f, 32f, true);
            StudioShot("c03_idle_side", new Vector3(3.0f, 1.0f, 0f), 0.8f, 32f, true);
            StudioShot("c04_idle_back34", new Vector3(-2.2f, 1.5f, -2.4f), 0.85f, 32f, true);
            StudioShot("c05_face", new Vector3(0.35f, 1.35f, 1.1f), 1.25f, 30f, false);

            // ── 跑步(往前 1.2 秒)+ 一個步態週期的連拍 ──
            intent.Move = new Vector2(0f, 1f);
            yield return new WaitForSeconds(1.0f);
            GameShot("c06_run_game");
            yield return Filmstrip("c07_run_side_strip", new Vector3(3.4f, 1.0f, 0.2f), 0.75f, 34f, 8, 0.06f);
            StudioShot("c09_run_front", new Vector3(0.6f, 1.1f, 3.4f), 0.8f, 34f, true);
            intent.Move = Vector2.zero;
            yield return new WaitForSeconds(0.25f);
            StudioShot("c08_run_stop_side", new Vector3(3.2f, 1.0f, 0.2f), 0.8f, 34f, true);
            yield return new WaitForSeconds(0.6f);

            // ── 開火(站定)──
            Teleport(_player.transform, new Vector3(-10f, 0.1f, -10f));
            rig.SetAngles(0f, 8f);
            intent.Attack = true;
            yield return new WaitForSeconds(0.5f);
            GameShot("c10_fire_game");
            LogMuzzleOffset();
            StudioShot("c11_fire_side", new Vector3(3.0f, 1.2f, 0.6f), 0.95f, 32f, true);
            StudioShot("c12_fire_front34", new Vector3(-1.9f, 1.3f, 2.3f), 0.95f, 32f, true);
            StudioShot("c13_fire_back34", new Vector3(-1.4f, 1.6f, -2.2f), 0.95f, 32f, true);
            // 邊走邊射
            intent.Move = new Vector2(0.4f, 1f);
            yield return new WaitForSeconds(0.7f);
            StudioShot("c14_fire_walk_side", new Vector3(3.2f, 1.1f, 0.5f), 0.85f, 34f, true);
            intent.Move = Vector2.zero;
            intent.Attack = false;
            yield return new WaitForSeconds(0.9f);

            // ── 投擲(按住蓄勢 → 放開)──
            Teleport(_player.transform, new Vector3(-10f, 0.1f, -10f));
            rig.SetAngles(0f, 10f);
            intent.Sub = true;
            yield return new WaitForSeconds(0.6f);
            GameShot("c15_throw_hold_game");
            StudioShot("c16_throw_hold_side", new Vector3(3.0f, 1.2f, 0.4f), 0.95f, 34f, true);
            StudioShot("c17_throw_hold_front34", new Vector3(-1.8f, 1.3f, 2.4f), 0.95f, 34f, true);
            intent.Sub = false;
            yield return Filmstrip("c18_throw_release_strip", new Vector3(-1.9f, 1.3f, 2.3f), 0.95f, 36f, 8, 0.04f);
            yield return new WaitForSeconds(0.8f);
            // 遊戲鏡頭看投擲:蓄勢 → 放開(副武器從右前方的投擲原點飛出,左手甩過身體)
            intent.Sub = true;
            yield return new WaitForSeconds(0.5f);
            intent.Sub = false;
            yield return GameFilmstrip("c18b_throw_release_game_strip", 8, 0.035f);
            yield return new WaitForSeconds(0.8f);

            // ── 跳躍(上升 / 下落)──
            intent.Move = new Vector2(0f, 0.6f);
            intent.PressJump();
            yield return new WaitForSeconds(0.25f);
            StudioShot("c19_jump_rise_side", new Vector3(3.4f, 0.8f, 0.3f), 1.0f, 36f, true);
            yield return new WaitForSeconds(0.45f);
            StudioShot("c20_jump_fall_side", new Vector3(3.4f, 0.8f, 0.3f), 1.0f, 36f, true);
            intent.Move = Vector2.zero;
            yield return new WaitForSeconds(1.2f);

            // ── 烏賊:乾地待機 / 乾地跳著走(前面的射擊散布可能塗到附近:找腳下沒墨的地方)──
            var squidCtrl = _player.GetComponent<SquidController>();
            Vector3[] drySpots = { new Vector3(20f, 0.1f, -20f), new Vector3(-20f, 0.1f, 20f), new Vector3(20f, 0.1f, 20f) };
            for (int i = 0; i < drySpots.Length; i++)
            {
                Teleport(_player.transform, drySpots[i]);
                yield return new WaitForSeconds(0.2f);
                if (squidCtrl.GroundOwner == 0)
                {
                    break;
                }
            }
            SetVisualYaw(0f);
            rig.SetAngles(0f, 18f);
            intent.Squid = true;
            yield return new WaitForSeconds(0.6f);
            GameShot("c21_squid_dry_game");
            StudioShot("c22_squid_dry_34", new Vector3(1.4f, 1.1f, 1.6f), 0.15f, 30f, true);
            intent.Move = new Vector2(0f, 1f);
            yield return new WaitForSeconds(0.55f);
            StudioShot("c23_squid_hop_side", new Vector3(2.4f, 0.7f, 0.3f), 0.2f, 32f, true);
            intent.Move = Vector2.zero;
            intent.Squid = false;
            yield return new WaitForSeconds(0.6f);

            // ── 烏賊:鋪墨 → 潛游 → 跳出墨面(空中烏賊)──
            Teleport(_player.transform, new Vector3(-18f, 0.1f, -18f));
            for (int i = 0; i <= 12; i += 2)
            {
                ground.Paint(new Vector3(-18f, 0f, -18f + i), 2.4f, Team.Alpha, 0.7f);
            }
            yield return null;
            rig.SetAngles(0f, 14f);
            intent.Squid = true;
            intent.Move = new Vector2(0f, 1f);
            yield return new WaitForSeconds(0.5f);
            GameShot("c24_swim_game");
            intent.PressJump();
            yield return new WaitForSeconds(0.2f);
            GameShot("c25_squid_jump_game");
            StudioShot("c26_squid_jump_side", new Vector3(3.2f, 0.4f, 0.5f), 0.2f, 36f, true);
            yield return new WaitForSeconds(0.9f);
            intent.Move = Vector2.zero;
            intent.Squid = false;
            yield return new WaitForSeconds(0.6f);

            // ── 爬牆:牆面鋪墨,烏賊衝向牆 → 貼上牆的瞬間(下沉過場)與翻越 ──
            for (int i = 0; i <= 4; i++)
            {
                wall.Paint(new Vector3(12f, i, 0f), 1.2f, Team.Alpha, 0.7f);
            }
            Teleport(_player.transform, new Vector3(9.4f, 0.1f, 0f));
            SetVisualYaw(90f);
            rig.SetAngles(90f, 10f);
            yield return null;
            intent.Squid = true;
            intent.Move = new Vector2(0f, 1f);
            var locomotion = _player.GetComponent<PlayerLocomotion>();
            var squid = _player.GetComponent<SquidController>();
            float until = Time.time + 2f;
            while (!locomotion.IsClimbing && Time.time < until)
            {
                yield return null;
            }
            // 貼牆後 2 幀:下沉過場(0.18 s)剛開始,看得到頭朝上、半沒入牆面的烏賊
            for (int i = 0; i < 2; i++)
            {
                yield return null;
            }
            WorldShot("c27_climb_enter_side", new Vector3(-1.6f, 1.4f, 3.2f), new Vector3(0.5f, 0.9f, 0f), 40f);
            GameShot("c28_climb_enter_game");
            until = Time.time + 2f;
            while (locomotion.IsInsideInkedWall && Time.time < until)
            {
                yield return null;
            }
            // 翻越:浮出牆面的過場 0.12 s 走完後,烏賊躍過牆緣
            for (int i = 0; i < 9; i++)
            {
                yield return null;
            }
            WorldShot("c29_climb_mantle_side", new Vector3(-1.8f, 1.2f, 3.4f), new Vector3(0.3f, 0.3f, 0f), 42f);
            Debug.Log($"[PROBE] 爬牆:submerged={squid.IsSubmerged} climbing={locomotion.IsClimbing}");
            intent.Move = Vector2.zero;
            intent.Squid = false;
            yield return new WaitForSeconds(1f);

            // ── 大招發動:高舉雙手 + 發光 ──
            Teleport(_player.transform, new Vector3(-10f, 0.1f, -10f));
            SetVisualYaw(0f);
            rig.SetAngles(0f, 10f);
            var gauge = _player.GetComponent<PlayerSpecialGauge>();
            gauge.SetPointsForTest(gauge.CostPoints);
            yield return new WaitForSeconds(0.4f);
            StudioShot("c30_special_ready_front", new Vector3(0.4f, 1.1f, 2.8f), 0.9f, 34f, true);
            intent.PressSpecial();
            yield return new WaitForSeconds(0.28f);
            StudioShot("c31_special_flourish_front", new Vector3(0.4f, 1.1f, 2.8f), 0.9f, 34f, true);
            yield return new WaitForSeconds(0.8f);
            gauge.EndActive();
            tank.InfiniteInk = false;

            // ── 對戰場景:兩隊同框 ──
            Destroy(_studio.gameObject);
            SceneManager.LoadScene(AutoTestAttribute.MatchScene);
            yield return null;
            yield return null;
            MatchController match = null;
            until = Time.time + 20f;
            while ((match = MatchController.Instance) == null && Time.time < until)
            {
                yield return null;
            }
            if (match == null)
            {
                Check("載入對戰場景", false, "20 秒內沒有 MatchController");
                Finish();
                yield break;
            }
            CreateStudio();
            // 本機玩家也交給 bot 腦,8 個角色一起跑
            while (match.LocalPlayer == null)
            {
                yield return null;
            }
            if (match.LocalPlayer.GetComponent<BotBrain>() == null)
            {
                match.LocalPlayer.gameObject.AddComponent<BotBrain>().Configure(BotBrain.Difficulty.Normal);
            }
            while (match.Phase < MatchPhase.Battle)
            {
                yield return null;
            }
            yield return new WaitForSeconds(2.5f);
            _player = match.LocalPlayer.gameObject;
            GameShot("m01_match_game");
            GroupShot("m02_alpha_group", Team.Alpha);
            GroupShot("m03_bravo_group", Team.Bravo);
            yield return CloseupTeamCheck("m04_alpha_close", Team.Alpha);
            yield return CloseupTeamCheck("m05_bravo_close", Team.Bravo);
            // 對戰中段:兩隊在中場交戰(開打約 25 秒後兩隊才在中線附近碰頭)
            yield return new WaitForSeconds(22f);
            GameShot("m06_match_mid_game");
            yield return ClashShot("m09_clash");
            GroupShot("m07_alpha_mid", Team.Alpha);
            GroupShot("m08_bravo_mid", Team.Bravo);
            Destroy(_studio.gameObject);
            Finish();
        }

        private static PaintableSurface FindSurface(string surfaceName)
        {
            foreach (PaintableSurface surface in PaintableSurface.Active)
            {
                if (surface.name == surfaceName)
                {
                    return surface;
                }
            }
            return null;
        }

        private void SetVisualYaw(float yaw)
        {
            Transform visual = _player.transform.Find("Visual");
            if (visual != null)
            {
                visual.rotation = Quaternion.Euler(0f, yaw, 0f);
            }
        }

        private static Inkling FindNearest(Team team, Vector3 from, GameObject exclude)
        {
            Inkling best = null;
            float bestDistance = float.MaxValue;
            foreach (Inkling inkling in Inkling.All)
            {
                var life = inkling.GetComponent<InklingLifecycle>();
                if (inkling.Team != team || inkling.gameObject == exclude || (life != null && life.IsDead))
                {
                    continue;
                }
                float d = (inkling.transform.position - from).sqrMagnitude;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = inkling;
                }
            }
            return best;
        }

        // 連拍:每隔 interval 秒拍一張小圖,拼成一張(看動作連續性)
        private IEnumerator Filmstrip(string shotName, Vector3 localOffset, float lookHeight, float fov, int frames,
            float interval)
        {
            var textures = new Texture2D[frames];
            for (int i = 0; i < frames; i++)
            {
                if (i > 0)
                {
                    yield return new WaitForSeconds(interval);
                }
                Transform visual = _player.transform.Find("Visual");
                Quaternion facing = Quaternion.Euler(0f, visual != null ? visual.eulerAngles.y : 0f, 0f);
                Vector3 origin = _player.transform.position;
                Vector3 position = origin + facing * localOffset;
                _studio.transform.SetPositionAndRotation(position,
                    Quaternion.LookRotation(origin + Vector3.up * lookHeight - position, Vector3.up));
                _studio.fieldOfView = fov;
                textures[i] = ShotCapture.Render(_studio, 480, 270);
                var cv = _player.GetComponent<CharacterVisual>();
                Debug.Log($"[PROBE] {shotName}#{i} frame={Time.frameCount} t={Time.time:F3} dt={Time.deltaTime:F4} " +
                          $"phase={cv.GaitPhase:F2} move={cv.MoveAmount:F2} throw={cv.ThrowWeight:F2}");
            }
            Texture2D sheet = ShotCapture.Stitch(textures, 4);
            ShotCapture.Save(sheet, shotName);
            Destroy(sheet);
            for (int i = 0; i < textures.Length; i++)
            {
                Destroy(textures[i]);
            }
        }

        // 遊戲相機連拍(玩家實際看到的畫面)
        private IEnumerator GameFilmstrip(string shotName, int frames, float interval)
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                yield break;
            }
            var textures = new Texture2D[frames];
            for (int i = 0; i < frames; i++)
            {
                if (i > 0)
                {
                    yield return new WaitForSeconds(interval);
                }
                textures[i] = ShotCapture.Render(cam, 480, 270);
            }
            Texture2D sheet = ShotCapture.Stitch(textures, 4);
            ShotCapture.Save(sheet, shotName);
            Destroy(sheet);
            for (int i = 0; i < textures.Length; i++)
            {
                Destroy(textures[i]);
            }
        }

        // 開火中:槍模型的槍口(視覺)與玩法的 Muzzle(子彈實際出發點)相差多少——只記錄,給人看
        private void LogMuzzleOffset()
        {
            var cv = _player.GetComponent<CharacterVisual>();
            Transform muzzle = _player.transform.Find("Visual/Muzzle");
            if (cv == null || cv.WeaponModel == null || muzzle == null)
            {
                return;
            }
            // 槍口環在槍模型座標 (0, 0.064, 0.235)
            Vector3 tip = cv.WeaponModel.TransformPoint(new Vector3(0f, 0.064f, 0.235f));
            Vector3 local = _player.transform.Find("Visual").InverseTransformVector(muzzle.position - tip);
            Debug.Log($"[PROBE] 槍口(視覺)→ Muzzle(玩法)偏移(Visual 座標)={local:F2} 距離={local.magnitude:F2} m");
        }

        // 世界座標偏移的鏡頭(爬牆這類角色朝向不代表觀察方向的鏡頭)
        private void WorldShot(string shotName, Vector3 worldOffset, Vector3 lookOffset, float fov)
        {
            Vector3 origin = _player.transform.position;
            Destroy(ShotAt(shotName, origin + worldOffset, origin + lookOffset, fov));
        }

        // 一隊的合照:找「12 m 內隊友最多」的成員,以那群人的重心為中心,從他們平均朝向的後上方拍
        private void GroupShot(string shotName, Team team)
        {
            Inkling anchor = null;
            int best = -1;
            foreach (Inkling a in Inkling.All)
            {
                if (a.Team != team)
                {
                    continue;
                }
                int near = 0;
                foreach (Inkling b in Inkling.All)
                {
                    if (b.Team == team && (b.transform.position - a.transform.position).sqrMagnitude < 144f)
                    {
                        near++;
                    }
                }
                if (near > best)
                {
                    best = near;
                    anchor = a;
                }
            }
            if (anchor == null)
            {
                return;
            }
            Vector3 center = Vector3.zero;
            Vector3 forward = Vector3.zero;
            float spread = 0f;
            int count = 0;
            foreach (Inkling inkling in Inkling.All)
            {
                if (inkling.Team != team || (inkling.transform.position - anchor.transform.position).sqrMagnitude >= 144f)
                {
                    continue;
                }
                center += inkling.transform.position;
                Transform visual = inkling.transform.Find("Visual");
                forward += visual != null ? visual.forward : inkling.transform.forward;
                count++;
            }
            center /= count;
            foreach (Inkling inkling in Inkling.All)
            {
                if (inkling.Team == team && (inkling.transform.position - anchor.transform.position).sqrMagnitude < 144f)
                {
                    spread = Mathf.Max(spread, (inkling.transform.position - center).magnitude);
                }
            }
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.01f ? forward.normalized : Vector3.forward;
            float distance = 4f + spread * 1.6f;
            Debug.Log($"[PROBE] {shotName}:{count} 人,散佈 {spread:F1} m");
            Destroy(ShotAt(shotName, center - forward * distance + Vector3.up * (1.5f + distance * 0.45f),
                center + Vector3.up * 0.6f, 50f));
        }

        // 兩隊同框:等到距離 14 m 內、都看得見(沒被擊倒、沒潛入墨裡)的一對 Alpha–Bravo,從側上方拍兩人
        //(場地四周是高牆,鏡頭放高才不會被擋)
        private IEnumerator ClashShot(string shotName)
        {
            Inkling bestA = null;
            Inkling bestB = null;
            float best = float.MaxValue;
            float until = Time.time + 30f;
            while (Time.time < until)
            {
                best = float.MaxValue;
                foreach (Inkling a in Inkling.All)
                {
                    if (a.Team != Team.Alpha || !Visible(a))
                    {
                        continue;
                    }
                    foreach (Inkling b in Inkling.All)
                    {
                        if (b.Team != Team.Bravo || !Visible(b))
                        {
                            continue;
                        }
                        float d = (a.transform.position - b.transform.position).sqrMagnitude;
                        if (d < best)
                        {
                            best = d;
                            bestA = a;
                            bestB = b;
                        }
                    }
                }
                if (best < 14f * 14f)
                {
                    break;
                }
                yield return new WaitForSeconds(0.25f);
            }
            if (bestA == null)
            {
                yield break;
            }
            Vector3 pa = bestA.transform.position;
            Vector3 pb = bestB.transform.position;
            Vector3 mid = (pa + pb) * 0.5f;
            Vector3 line = pb - pa;
            line.y = 0f;
            float distance = line.magnitude;
            Vector3 side = distance > 0.01f ? Vector3.Cross(Vector3.up, line / distance) : Vector3.right;
            float back = 3f + distance * 0.55f;
            Debug.Log($"[PROBE] {shotName}:{bestA.name} ↔ {bestB.name} 相距 {distance:F1} m");
            Destroy(ShotAt(shotName, mid + side * back + Vector3.up * (3f + distance * 0.45f), mid + Vector3.up * 0.6f, 55f));
        }

        private static bool Visible(Inkling inkling)
        {
            var life = inkling.GetComponent<InklingLifecycle>();
            var sq = inkling.GetComponent<SquidController>();
            return (life == null || !life.IsDead) && (sq == null || !sq.IsSubmerged);
        }

        // 某隊一個看得見的人形角色(沒被擊倒、不在烏賊態),正面特寫;只數角色像素中的隊伍色(地上的墨不算)。
        // bot 常常在游泳(隱形),最多等 10 秒。
        private IEnumerator CloseupTeamCheck(string shotName, Team team)
        {
            Inkling who = null;
            float until = Time.time + 10f;
            while (who == null && Time.time < until)
            {
                foreach (Inkling inkling in Inkling.All)
                {
                    var life = inkling.GetComponent<InklingLifecycle>();
                    var sq = inkling.GetComponent<SquidController>();
                    if (inkling.Team == team && (life == null || !life.IsDead) && (sq == null || !sq.IsSquid))
                    {
                        who = inkling;
                        break;
                    }
                }
                if (who == null)
                {
                    yield return null;
                }
            }
            if (who == null)
            {
                Check($"{shotName} 找到角色", false, $"{team} 隊 10 秒內沒有看得見的人形角色");
                yield break;
            }
            Transform visual = who.transform.Find("Visual");
            Vector3 fwd = visual != null ? visual.forward : who.transform.forward;
            Vector3 right = visual != null ? visual.right : who.transform.right;
            Vector3 origin = who.transform.position;
            Texture2D shot = ShotAt(shotName, origin + fwd * 2.3f + right * 0.6f + Vector3.up * 1.3f,
                origin + Vector3.up * 0.85f, 34f);
            Renderer[] renderers = who.GetComponentsInChildren<Renderer>(false);
            var states = new bool[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                states[i] = renderers[i].enabled;
                renderers[i].enabled = false;
            }
            Texture2D hidden = ShotCapture.Render(_studio);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].enabled = states[i];
            }
            Color color = TeamPalette.ColorOf(team);
            int teamPixels = ShotCapture.HuePixelsInMask(shot, hidden, color);
            int characterPixels = ShotCapture.DiffPixels(shot, hidden);
            Check($"{shotName} 角色身上有 {team} 隊伍色", teamPixels > 300 && characterPixels > 2000,
                $"角色像素={characterPixels} 其中隊伍色={teamPixels}(色 {color})");
            Destroy(shot);
            Destroy(hidden);
        }

        private void CreateStudio()
        {
            var go = new GameObject("CharacterStudioCamera");
            DontDestroyOnLoad(go);
            _studio = go.AddComponent<Camera>();
            if (Camera.main != null)
            {
                _studio.CopyFrom(Camera.main);
            }
            _studio.targetTexture = null;
            _studio.nearClipPlane = 0.05f;
            _studio.enabled = false;
        }

        private void GameShot(string shotName)
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                return;
            }
            Destroy(ShotCapture.Capture(cam, shotName));
        }

        // 攝影棚鏡頭:相對角色(Visual 朝向)的偏移,看向角色的某個高度;checkVisible = 角色必須明顯出現在畫面上
        private void StudioShot(string shotName, Vector3 localOffset, float lookHeight, float fov, bool checkVisible)
        {
            Transform visual = _player.transform.Find("Visual");
            Quaternion facing = Quaternion.Euler(0f, visual != null ? visual.eulerAngles.y : 0f, 0f);
            Vector3 origin = _player.transform.position;
            Vector3 position = origin + facing * localOffset;
            Texture2D shot = ShotAt(shotName, position, origin + Vector3.up * lookHeight, fov);
            var cv = _player.GetComponent<CharacterVisual>();
            Debug.Log($"[PROBE] {shotName} frame={Time.frameCount} t={Time.time:F3} dt={Time.deltaTime:F4} " +
                      $"phase={cv.GaitPhase:F2} move={cv.MoveAmount:F2} aim={cv.AimWeight:F2} throw={cv.ThrowWeight:F2} " +
                      $"squid={cv.IsSquidForm} speed={_player.GetComponent<CharacterController>().velocity.magnitude:F2}");
            if (checkVisible)
            {
                Renderer[] renderers = _player.GetComponentsInChildren<Renderer>(false);
                var states = new bool[renderers.Length];
                for (int i = 0; i < renderers.Length; i++)
                {
                    states[i] = renderers[i].enabled;
                    renderers[i].enabled = false;
                }
                Texture2D hidden = ShotCapture.Render(_studio);
                for (int i = 0; i < renderers.Length; i++)
                {
                    renderers[i].enabled = states[i];
                }
                int diff = ShotCapture.DiffPixels(shot, hidden);
                Check($"{shotName} 角色可見", diff > 2500, $"角色像素差={diff}");
                Destroy(hidden);
            }
            Destroy(shot);
        }

        private Texture2D ShotAt(string shotName, Vector3 position, Vector3 lookAt, float fov)
        {
            _studio.transform.SetPositionAndRotation(position, Quaternion.LookRotation(lookAt - position, Vector3.up));
            _studio.fieldOfView = fov;
            return ShotCapture.Capture(_studio, shotName);
        }
    }
}

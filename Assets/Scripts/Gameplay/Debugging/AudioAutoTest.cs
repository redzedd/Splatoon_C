using System.Collections;
using System.Collections.Generic;
using SplatoonC.Core;
using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.CameraRig;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 聲音驗收(試射場):清單載入、各動作實際發出對應聲音(以 GameAudio.Played 攔截)、同組限流、循環與音樂切換。
    [AutoTest(Scene = AutoTestAttribute.TrainingScene, Timeout = 90f, Order = 50)]
    public sealed class AudioAutoTest : AutoTestBase
    {
        private readonly List<string> _heard = new List<string>();

        public static void Run()
        {
            Launch<AudioAutoTest>();
        }

        private void OnEnable()
        {
            GameAudio.Played += OnPlayed;
        }

        private void OnDisable()
        {
            GameAudio.Played -= OnPlayed;
        }

        private void OnPlayed(string id)
        {
            _heard.Add(id);
        }

        private bool Heard(string prefix)
        {
            foreach (string id in _heard)
            {
                if (id.StartsWith(prefix, System.StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private IEnumerator Start()
        {
            GameObject player = FindPlayer();
            var tank = player != null ? player.GetComponent<PlayerInkTank>() : null;
            var rig = Camera.main != null ? Camera.main.GetComponent<ThirdPersonCameraRig>() : null;
            GameObject groundGo = GameObject.Find("Ground");
            var ground = groundGo != null ? groundGo.GetComponent<PaintableSurface>() : null;
            if (player == null || tank == null || rig == null || ground == null || player.GetComponent<InklingAudio>() == null)
            {
                FailSetup("找不到 Player(需掛 InklingAudio,先跑 Tools/SplatoonC/Audio Setup)/ 相機 / Ground");
                yield break;
            }
            var intent = ScriptedIntent.Drive(player.transform);

            // 1) 清單
            string[] required =
            {
                "shoot_shooter", "ink_hit_floor", "ink_hit_wall", "footstep", "jump", "land", "squid_dive", "squid_surface",
                "swim_loop", "bomb_throw", "bomb_explode", "special_ready", "special_activate", "player_splatted",
                "battle_theme", "last_minute", "results_theme", "ready", "go", "countdown_tick", "judge_reveal",
            };
            var missing = new List<string>();
            foreach (string id in required)
            {
                if (!GameAudio.Has(id))
                {
                    missing.Add(id);
                }
            }
            Check("聲音清單載入(含變體組)", missing.Count == 0, missing.Count == 0 ? $"{required.Length} 個 id 都在" : "缺 " + string.Join(",", missing));

            // 2) 射擊:槍聲 + 墨落地聲
            Teleport(player.transform, new Vector3(-18f, 0.1f, -20f));
            rig.SetAngles(0f, 20f);
            tank.RefillFull();
            yield return new WaitForSeconds(0.4f);
            _heard.Clear();
            intent.Attack = true;
            yield return new WaitForSeconds(0.6f);
            intent.Attack = false;
            yield return new WaitForSeconds(0.5f);
            Check("射擊發出槍聲與墨落地聲", Heard("shoot_shooter") && Heard("ink_hit_"), string.Join(",", _heard.GetRange(0, Mathf.Min(8, _heard.Count))));

            // 3) 跑步腳步聲
            _heard.Clear();
            Teleport(player.transform, new Vector3(-10f, 0.1f, -20f));
            yield return new WaitForSeconds(0.3f);
            intent.Move = new Vector2(0f, 1f);
            yield return new WaitForSeconds(1.2f);
            intent.Move = Vector2.zero;
            Check("跑步有腳步聲", Heard("footstep"), $"聽到 {_heard.Count} 個聲音");

            // 4) 跳躍與落地
            yield return new WaitForSeconds(0.4f);
            _heard.Clear();
            intent.PressJump();
            yield return new WaitForSeconds(1.4f);
            Check("起跳與落地聲", Heard("jump") && Heard("land"), string.Join(",", _heard));

            // 5) 潛入自家墨、游動循環、浮出
            for (int i = 0; i <= 12; i += 2)
            {
                ground.Paint(new Vector3(-4f, 0f, -22f + i), 2.5f, Team.Alpha, 0.8f);
            }
            Teleport(player.transform, new Vector3(-4f, 0.1f, -21f));
            yield return new WaitForSeconds(0.3f);
            _heard.Clear();
            intent.Squid = true;
            yield return new WaitForSeconds(0.3f);
            intent.Move = new Vector2(0f, 1f);
            yield return new WaitForSeconds(0.6f);
            intent.Move = Vector2.zero;
            intent.Squid = false;
            yield return new WaitForSeconds(0.3f);
            Check("潛入 / 游動循環 / 浮出", Heard("squid_dive") && Heard("swim_loop") && Heard("squid_surface"), string.Join(",", _heard));

            // 6) 墨不足提示
            _heard.Clear();
            Drain(tank);
            yield return new WaitForSeconds(0.1f);
            intent.Attack = true;
            yield return new WaitForSeconds(0.3f);
            intent.Attack = false;
            Check("墨不足提示音", Heard("low_ink"), string.Join(",", _heard));
            tank.RefillFull();

            // 7) 同組限流:同一幀 30 次只響 1 次
            int before = GameAudio.PlayCount;
            for (int i = 0; i < 30; i++)
            {
                GameAudio.Play("bomb_bounce", player.transform.position);
            }
            Check("同組最短間隔限流", GameAudio.PlayCount - before == 1, $"發聲 {GameAudio.PlayCount - before} 次");

            // 8) 超出可聽距離不發聲
            before = GameAudio.PlayCount;
            yield return new WaitForSeconds(0.1f);
            GameAudio.Play("bomb_explode", Camera.main.transform.position + Vector3.right * (GameAudio.MaxAudibleDistance + 20f));
            Check("超出可聽距離略過", GameAudio.PlayCount == before, $"發聲 {GameAudio.PlayCount - before} 次");

            // 9) 循環把手
            AudioLoop loop = GameAudio.StartLoop("tornado_loop", player.transform);
            bool alive = loop != null && loop.IsAlive;
            loop?.Stop(0.05f);
            Check("循環音效開始/停止", alive && loop != null && !loop.IsAlive, $"alive={alive}");

            // 10) 音樂切換(交叉淡入淡出、從指定位置起播、停止)
            GameAudio.PlayMusic("battle_theme", 0.1f);
            yield return new WaitForSeconds(0.2f);
            string first = GameAudio.CurrentMusicId;
            GameAudio.PlayMusic("last_minute", 0.1f, 30f);
            yield return new WaitForSeconds(0.2f);
            string second = GameAudio.CurrentMusicId;
            GameAudio.StopMusic(0.1f);
            yield return new WaitForSeconds(0.3f);
            string third = GameAudio.CurrentMusicId;
            Check("音樂切換與停止", first == "battle_theme" && second == "last_minute" && third == null,
                $"{first} → {second} → {third ?? "(無)"}");

            Finish();
        }
    }
}

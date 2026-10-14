using System.Collections;
using SplatoonC.Gameplay.CameraRig;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Player;
using SplatoonC.Gameplay.Scoring;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // M4 步驟 1/2 驗收:假人受傷 / 擊殺累計 120 / 急墜段衰減 / 重生 / 閒置回血 / 動態來回 + 頭頂傷害數字 HUD。
    // 站位在 x=21、24 的靶場帶(x ≥ 17),與 -Z 射道的塗地測試互不污染。需新 play session(會耗墨、殺假人)。
    public sealed class CombatAutoTest : MonoBehaviour
    {
        private sealed class TestIntentSource : IPlayerIntentSource
        {
            public Vector2 MoveInput => Vector2.zero;
            public Vector2 LookDeltaDeg => Vector2.zero;
            public bool JumpPressedThisFrame => false;
            public bool AttackHeld { get; set; }
            public bool SquidHeld => false;
        }

        public static void Run()
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("[AUTOTEST] 需在 Play mode 中執行");
                return;
            }
            var host = new GameObject("CombatAutoTest");
            host.AddComponent<CombatAutoTest>();
        }

        private int _passed;
        private int _failed;

        private IEnumerator Start()
        {
            GameObject player = GameObject.Find("Player");
            PlayerInputRouter router = player != null ? player.GetComponent<PlayerInputRouter>() : null;
            CharacterController controller = player != null ? player.GetComponent<CharacterController>() : null;
            PlayerInkTank tank = player != null ? player.GetComponent<PlayerInkTank>() : null;
            ThirdPersonCameraRig rig = Camera.main != null
                ? Camera.main.GetComponent<ThirdPersonCameraRig>() : null;
            TargetDummy near = FindDummy("Dummy_Near");
            TargetDummy far = FindDummy("Dummy_Far");
            TargetDummy moving = FindDummy("Dummy_Moving");
            DamagePopupHud hud = DamagePopupHud.Instance;
            if (router == null || controller == null || tank == null || rig == null
                || near == null || far == null || moving == null || hud == null)
            {
                Debug.LogError("[AUTOTEST] FAIL 前置:找不到 Player/Router/Tank/CameraRig/DamagePopupHud 或靶場假人(先跑 Tools/SplatoonC/M4 Setup)");
                Debug.Log("[AUTOTEST] DONE passed=0 failed=1");
                Destroy(gameObject);
                yield break;
            }

            tank.InfiniteInk = true;
            var intent = new TestIntentSource();
            router.SetOverrideSource(intent);
            Vector3 spawnPos = player.transform.position;

            // 站位 1:x=21 面向 +Z(yaw 0)微俯,近假人 6m(直飛段)。相機有平滑,以時間等它跟上
            //(批次模式幀率極高,30 幀只有幾十毫秒,相機還在出生點附近 → 第一發打偏;與 BombAutoTest 同一個坑)。
            Teleport(player, controller, new Vector3(21f, 0.1f, -16f));
            rig.SetAngles(0f, 4f);
            yield return new WaitForSeconds(0.5f);

            // 案 1:單發 30.0
            int version = near.PopupVersion;
            intent.AttackHeld = true;
            yield return null;
            intent.AttackHeld = false;
            float deadline = Time.time + 0.6f;
            while (near.PopupVersion == version && Time.time < deadline)
            {
                yield return null;
            }
            bool hit = near.PopupVersion != version;
            // S3 Splattershot:直擊 36(三發擊倒);期望值取自武器設定,調參不必改測試
            float shotDamage = player.GetComponent<InkShooter>().Config.Damage;
            int shotsToKill = Mathf.CeilToInt(100f / shotDamage - 1e-4f);
            float killTotal = shotsToKill * shotDamage;
            Check($"近距單發傷害 {shotDamage:0.0}",
                hit && Mathf.Abs(near.LastDamage - shotDamage) < 0.01f && Mathf.Abs(near.CurrentHp - (100f - shotDamage)) < 0.01f,
                $"命中={hit} 傷害={near.LastDamage:0.00} 血量={near.CurrentHp:0.0}");

            // 案 1b:HUD 顯示白色 30.0(HUD 在 LateUpdate 更新,多等一幀)
            yield return null;
            bool shown = hud.TryGetShown(near, out string shownText, out Color shownColor, out int shownSize);
            Check($"頭頂數字:命中後顯示累計 {shotDamage:0.0}(一般樣式)",
                shown && shownText == shotDamage.ToString("0.0") && shownSize == hud.FontSize && shownColor == hud.NormalColor,
                $"顯示={shown} 文字={shownText} 字級={shownSize} 顏色={shownColor}");

            // 案 2:連射至死,累計顯示全額(S3:3 發 × 36 = 108,最後一發不夾到剩餘血量)
            intent.AttackHeld = true;
            deadline = Time.time + 2f;
            while (near.IsAlive && Time.time < deadline)
            {
                yield return null;
            }
            intent.AttackHeld = false;
            float killTime = Time.time;
            Check($"連射擊殺:累計顯示全額 {killTotal:0.0} 且標記為擊殺",
                !near.IsAlive && near.KillCount == 1 && near.PopupIsKill
                && Mathf.Abs(near.PopupTotal - killTotal) < 0.01f && near.PopupVisible,
                $"存活={near.IsAlive} 擊殺數={near.KillCount} 累計={near.PopupTotal:0.0} 紅字={near.PopupIsKill}");

            // 案 2b:HUD 顯示紅色放大的擊殺累計
            yield return null;
            shown = hud.TryGetShown(near, out shownText, out shownColor, out shownSize);
            Check($"頭頂數字:擊殺後顯示 {killTotal:0.0}、放大、變紅",
                shown && shownText == killTotal.ToString("0.0") && shownSize == hud.KillFontSize && shownColor == hud.KillColor,
                $"顯示={shown} 文字={shownText} 字級={shownSize} 顏色={shownColor}");

            // 站位 2:x=24 面向 +Z,遠假人 14m(射程外)。S3 墨彈依飛行幀數衰減(Splattershot 第 8F 起 36 → 第 40F 18);
            // 舊的兩段式彈依飛行距離衰減到下限 0.1。
            // 俯角要 6°:相機在 y≈2.8,平視時準心射線目標點也在 2.8m 高,墨彈會從假人頭上飛過(2026-09-08 實測)。
            Teleport(player, controller, new Vector3(24f, 0.1f, -16f));
            rig.SetAngles(0f, 6f);
            yield return new WaitForSeconds(0.5f);
            version = far.PopupVersion;
            float minDamage = float.MaxValue;
            float maxDamage = 0f;
            int hits = 0;
            intent.AttackHeld = true;
            deadline = Time.time + 1.0f;
            while (Time.time < deadline)
            {
                if (far.PopupVersion != version)
                {
                    version = far.PopupVersion;
                    hits++;
                    minDamage = Mathf.Min(minDamage, far.LastDamage);
                    maxDamage = Mathf.Max(maxDamage, far.LastDamage);
                }
                yield return null;
            }
            intent.AttackHeld = false;
            float farHitTime = Time.time;
            WeaponConfig shooterConfig = player.GetComponent<InkShooter>() != null ? player.GetComponent<InkShooter>().Config : null;
            bool s3Bullet = shooterConfig != null && shooterConfig.UsesInkBullet;
            float fullDamage = s3Bullet ? shooterConfig.Bullet.DamageMax : 30f;
            float floorDamage = s3Bullet ? shooterConfig.Bullet.DamageMin : 0.1f;
            Check($"射程外傷害衰減({floorDamage:0.#} ≤ 傷害 < {fullDamage:0.#})",
                hits > 0 && minDamage >= floorDamage - 0.0001f && maxDamage < fullDamage - 0.5f,
                hits > 0 ? $"命中 {hits} 發,傷害 {minDamage:0.00}~{maxDamage:0.00}" : "1 秒內零命中");

            // 案 4:死後重生滿血、數字清空
            while (Time.time < killTime + near.Config.RespawnDelay + 0.3f)
            {
                yield return null;
            }
            Check("死後重生滿血、數字清空",
                near.IsAlive && Mathf.Abs(near.CurrentHp - near.Config.MaxHp) < 0.01f
                && near.PopupTotal == 0f && !near.PopupVisible,
                $"存活={near.IsAlive} 血量={near.CurrentHp:0.0} 累計={near.PopupTotal:0.0} 顯示={near.PopupVisible}");
            yield return null;
            shown = hud.TryGetShown(near, out shownText, out _, out _);
            Check("頭頂數字:重生後隱藏", !shown, $"顯示={shown} 文字={shownText}");

            // 案 5:閒置回滿血並清空數字(遠假人只被打掉幾點)
            while (Time.time < farHitTime + far.Config.IdleResetDelay + 0.3f)
            {
                yield return null;
            }
            Check("閒置回滿血並清空數字",
                far.IsAlive && Mathf.Abs(far.CurrentHp - far.Config.MaxHp) < 0.01f && far.PopupTotal == 0f,
                $"血量={far.CurrentHp:0.0} 累計={far.PopupTotal:0.0}");

            // 案 6:動態假人來回(速度 2、行程 5m → 每 2.5 秒折返一次)
            float lastX = moving.transform.position.x;
            float minX = lastX;
            float maxX = lastX;
            int reversals = 0;
            int lastSign = 0;
            float end = Time.time + 3f;
            while (Time.time < end)
            {
                yield return null;
                float x = moving.transform.position.x;
                float dx = x - lastX;
                int sign = dx > 0.0005f ? 1 : (dx < -0.0005f ? -1 : 0);
                if (sign != 0)
                {
                    if (lastSign != 0 && sign != lastSign)
                    {
                        reversals++;
                    }
                    lastSign = sign;
                }
                lastX = x;
                minX = Mathf.Min(minX, x);
                maxX = Mathf.Max(maxX, x);
            }
            Check("動態假人來回移動", maxX - minX > 2f && reversals >= 1,
                $"3 秒內位移範圍 {maxX - minX:0.00}m、折返 {reversals} 次");

            // 送回出生點:其他 AutoTest(Shooting/Locomotion)假設玩家在出生點附近,留在靶場邊緣會讓它們假紅
            Teleport(player, controller, spawnPos);
            rig.SetAngles(0f, 12f);
            router.ClearOverrideSource();
            tank.InfiniteInk = false;
            Debug.Log($"[AUTOTEST] DONE passed={_passed} failed={_failed}");
            Destroy(gameObject);
        }

        private static TargetDummy FindDummy(string name)
        {
            GameObject go = GameObject.Find(name);
            return go != null ? go.GetComponent<TargetDummy>() : null;
        }

        private static void Teleport(GameObject player, CharacterController controller, Vector3 position)
        {
            controller.enabled = false;
            player.transform.position = position;
            controller.enabled = true;
        }

        // 至少 frames 幀、且至少 frames/60 秒:批次模式幀率可達數百,30 幀只有幾十毫秒,
        // 角色瞬移後還在空中(首發套跳射散布而打偏)、相機也還沒到位
        private static IEnumerator WaitFrames(int frames)
        {
            float until = Time.time + frames / 60f;
            for (int i = 0; i < frames || Time.time < until; i++)
            {
                yield return null;
            }
        }

        private void Check(string caseName, bool pass, string evidence)
        {
            if (pass)
            {
                _passed++;
                Debug.Log($"[AUTOTEST] PASS {caseName}: {evidence}");
            }
            else
            {
                _failed++;
                Debug.LogError($"[AUTOTEST] FAIL {caseName}: {evidence}");
            }
        }
    }
}

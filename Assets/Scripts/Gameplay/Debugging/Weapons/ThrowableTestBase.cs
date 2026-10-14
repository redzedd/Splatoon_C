using System.Collections;
using System.Collections.Generic;
using SplatoonC.Core;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 投擲物 II(Torpedo / Fizzy Bomb / Point Sensor / Angle Shooter)AutoTest 的共用底(建立在 BombTestBase 上):
    //   敵方角色靶(複製本機玩家改成敵隊非本機角色、ScriptedIntent 讓它站著不動)——標記(Marking)只作用於角色;
    //   模擬 BotBrain 的副武器用法(看 SubWeaponConfig.BotAimPoint、按住 BotHoldSeconds 後放開)。
    public abstract class ThrowableTestBase : BombTestBase
    {
        private readonly List<GameObject> _enemies = new List<GameObject>();

        protected Team EnemyTeam => Player.Team == Team.Alpha ? Team.Bravo : Team.Alpha;

        // 敵方角色靶:站在 position(腳下),不動;invulnerable = 不受傷(只測標記時)
        protected Inkling SpawnEnemy(string name, Vector3 position, bool invulnerable)
        {
            GameObject go = Instantiate(PlayerGo, position, Quaternion.identity);
            go.name = name;
            var enemy = go.GetComponent<Inkling>();
            enemy.Configure(EnemyTeam, name, false);
            enemy.AimView = null;
            ScriptedIntent.Drive(enemy);
            var health = go.GetComponent<InklingHealth>();
            if (health != null)
            {
                health.Invulnerable = invulnerable;
            }
            _enemies.Add(go);
            enemy.Teleport(new Vector3(position.x, Mathf.Max(position.y, 0.1f), position.z), 0f, false);
            return enemy;
        }

        protected void RemoveEnemies()
        {
            foreach (GameObject go in _enemies)
            {
                if (go != null)
                {
                    Destroy(go);
                }
            }
            _enemies.Clear();
            Marking.ClearAll();
        }

        protected static T LiveOf<T>() where T : SimulatedBomb
        {
            foreach (T item in FindObjectsByType<T>(FindObjectsSortMode.None))
            {
                if (item.IsActive)
                {
                    return item;
                }
            }
            return null;
        }

        // 模擬(改過的)BotBrain.TryThrowSub / AimThrowAt:距離在 BotRange 內且墨量 ≥ 耗墨 + 0.08 才丟;
        // 視線從槍口看 config.BotAimPoint(腳下, 目標腳下),按住 BotHoldSeconds(距離) 後放開。回傳是否真的丟出
        protected IEnumerator BotAimThrow(SubWeaponConfig config, Vector3 standAt, Vector3 direction, float distance,
            System.Action<bool, float> report)
        {
            Vector3 flat = new Vector3(direction.x, 0f, direction.z).normalized;
            yield return MoveTo(standAt, flat);
            Tank.RefillFull();
            Vector3 feet = new Vector3(standAt.x, 0f, standAt.z) + flat * distance;
            Vector2 range = config.BotRange;
            bool wouldThrow = config.BotUsage == BotSubUsage.ThrowAtEnemy && distance >= range.x && distance <= range.y
                              && Tank.Normalized >= config.InkCost + 0.08f;
            float hold = config.BotHoldSeconds(distance);
            FaceTowards(flat);
            Aim.Origin = Muzzle.position;
            Aim.Forward = (config.BotAimPoint(PlayerGo.transform.position, feet) - Aim.Origin).normalized;
            Intent.Sub = true;
            yield return WaitSeconds(hold);
            yield return Release();
            report(wouldThrow && LastThrew, hold);
        }

        protected new void Cleanup()
        {
            RemoveEnemies();
            base.Cleanup();
        }
    }
}

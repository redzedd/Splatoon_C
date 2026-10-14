using SplatoonC.Core;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 大招 AutoTest 共用:換上只改大招的武器組合、複製本機玩家當敵方靶(Bravo、非本機、意圖由測試驅動)。
    public static class SpecialTestKit
    {
        // 保留目前的主/副武器,只換大招(集滿點數 200p)
        public static bool EquipSpecial(Inkling inkling, SpecialWeaponConfig special)
        {
            var loadout = inkling != null ? inkling.GetComponent<WeaponLoadout>() : null;
            if (loadout == null || loadout.Kit == null || special == null)
            {
                return false;
            }
            WeaponKit current = loadout.Kit;
            var kit = ScriptableObject.CreateInstance<WeaponKit>();
            kit.Setup("autotest_" + special.name, current.DisplayName, current.EnglishName, "AutoTest", current.Main,
                current.Sub, special, 200f, current.Weight, "射程", 50, "威力", 50, "射速", 50);
            loadout.Equip(kit);
            return inkling.GetComponent<PlayerSpecialGauge>().SpecialConfig == special;
        }

        // 複製本機玩家成敵方(Bravo)角色:站著不動(ScriptedIntent 由呼叫端保留)
        public static Inkling CloneEnemy(GameObject playerGo, Vector3 position, string name, out ScriptedIntent intent)
        {
            GameObject go = Object.Instantiate(playerGo, position, Quaternion.identity);
            go.name = name;
            var inkling = go.GetComponent<Inkling>();
            inkling.Configure(Team.Bravo, name, false);
            intent = ScriptedIntent.Drive(inkling);
            return inkling;
        }

        // 平地上的地面點(往下打射線)
        public static Vector3 Ground(Vector3 point)
        {
            return Physics.Raycast(point + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 10f, CombatMasks.Terrain,
                QueryTriggerInteraction.Ignore)
                ? hit.point
                : new Vector3(point.x, 0f, point.z);
        }
    }
}

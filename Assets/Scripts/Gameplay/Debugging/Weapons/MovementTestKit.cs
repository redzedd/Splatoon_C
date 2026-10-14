using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // A4 移動 AutoTest 共用:鋪自家墨地面、換副武器/主武器、複製本機玩家當隊友、量測小工具。
    public static class MovementTestKit
    {
        public const string BeakonConfigPath = "Weapons/Sub_squid_beakon";

        // 在矩形範圍(XZ)內鋪滿 owner 的墨(不充大招)
        public static void PaintFloor(Inkling owner, Vector2 min, Vector2 max, float y = 0.05f)
        {
            InkSource source = owner.InkSource(false);
            for (float x = min.x; x <= max.x + 0.01f; x += 2.5f)
            {
                for (float z = min.y; z <= max.y + 0.01f; z += 2.5f)
                {
                    InkPainter.PaintSphere(new Vector3(x, y, z), 2.2f, 1f, source);
                }
            }
        }

        // 換成「目前主武器/大招 + 指定副武器」的組合
        public static bool EquipSub(Inkling inkling, SubWeaponConfig sub)
        {
            var loadout = inkling != null ? inkling.GetComponent<WeaponLoadout>() : null;
            if (loadout == null || loadout.Kit == null || sub == null)
            {
                return false;
            }
            WeaponKit current = loadout.Kit;
            var kit = ScriptableObject.CreateInstance<WeaponKit>();
            kit.Setup("autotest_sub_" + sub.name, current.DisplayName, current.EnglishName, "AutoTest", current.Main, sub,
                current.Special, current.SpecialPoints, current.Weight, "射程", 50, "威力", 50, "射速", 50);
            loadout.Equip(kit);
            return inkling.GetComponent<ThrowController>().SubConfig == sub;
        }

        // 換成「指定主武器 + 目前副武器/大招」的組合
        public static bool EquipMain(Inkling inkling, MainWeaponConfig main)
        {
            var loadout = inkling != null ? inkling.GetComponent<WeaponLoadout>() : null;
            if (loadout == null || loadout.Kit == null || main == null)
            {
                return false;
            }
            WeaponKit current = loadout.Kit;
            var kit = ScriptableObject.CreateInstance<WeaponKit>();
            kit.Setup("autotest_main_" + main.name, current.DisplayName, current.EnglishName, "AutoTest", main, current.Sub,
                current.Special, current.SpecialPoints, current.Weight, "射程", 50, "威力", 50, "射速", 50);
            loadout.Equip(kit);
            return loadout.Main != null && loadout.Main.BaseConfig == main;
        }

        public static void EquipKit(Inkling inkling, WeaponKit kit)
        {
            var loadout = inkling != null ? inkling.GetComponent<WeaponLoadout>() : null;
            if (loadout != null && kit != null)
            {
                loadout.Equip(kit);
            }
        }

        // 複製本機玩家成同隊(或指定隊伍)的非本機角色;意圖由回傳的 ScriptedIntent 驅動(預設站著不動)
        public static Inkling CloneCharacter(GameObject playerGo, Vector3 position, string name, Team team,
            out ScriptedIntent intent)
        {
            GameObject go = Object.Instantiate(playerGo, position, Quaternion.identity);
            go.name = name;
            var inkling = go.GetComponent<Inkling>();
            inkling.Configure(team, name, false);
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

        public static float Flat(Vector3 a, Vector3 b)
        {
            return new Vector2(a.x - b.x, a.z - b.z).magnitude;
        }

        // 敵方(team 以外)一次小爆炸打在 center:只算傷害(不塗墨、不擊退)
        public static ExplosionResult Poke(Vector3 center, Team attackerTeam, float damage)
        {
            var spec = new ExplosionSpec
            {
                NearDamage = damage,
                NearRadius = 2f,
                FarDamage = damage,
                FarRadius = 2.5f,
                AttackClass = AttackClass.Other,
                Kind = DamageKind.Sub,
                IgnoreLineOfSight = true,
            };
            return Explosion.Detonate(spec, center, attackerTeam, null);
        }
    }
}

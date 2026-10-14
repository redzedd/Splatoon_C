using System.Collections.Generic;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Combat;
using UnityEditor;
using UnityEngine;

namespace SplatoonC.EditorBuild
{
    // 依 S3 武器組合總表(Core/Combat/S3KitTable)建立 WeaponKit 資產,順序 = S3 遊戲內清單。
    // 主/副/大招的設定資產都存在才建;缺零件的組合跳過並列在 log(零件做好後重跑 M7 Setup 自動加入)。
    // 某把主武器的組合全都缺零件時,用 S3KitTable.StandIns 的暫代零件,讓每把已實作的主武器都選得到。
    public static class S3KitCatalogBuilder
    {
        // Player.prefab 的預設組合:維持改版前的配置(射擊槍 + Splat Bomb + Triple Inkstrike),
        // 直接開試射場 / 對戰場景(AutoTest、編輯器 Play)時的行為不變
        public const string PlayerDefaultKitId = "tentatek_splattershot";

        public static void AddAll(List<WeaponKit> kits)
        {
            int before = kits.Count;
            var skipped = new List<string>();
            var standIns = new List<string>();
            foreach (S3KitTable.Row row in S3KitTable.Rows)
            {
                if (!S3KitTable.TryGetFamily(row.Family, out S3KitTable.Family family))
                {
                    Debug.LogError($"[M7SETUP] ERROR {row.RowId} 沒有主武器系列");
                    continue;
                }
                MainWeaponConfig main = AssetDatabase.LoadAssetAtPath<MainWeaponConfig>(MainPath(family.MainId));
                if (main == null)
                {
                    Debug.LogError($"[M7SETUP] ERROR {row.RowId} 找不到主武器 {MainPath(family.MainId)}");
                    continue;
                }
                SubWeaponConfig sub = LoadSub(row.Sub);
                SpecialWeaponConfig special = LoadSpecial(row.Special);
                string note = null;
                if ((sub == null || special == null) && S3KitTable.TryGetStandIn(row.RowId, out S3KitTable.StandIn standIn))
                {
                    note = ApplyStandIn(row, standIn, ref sub, ref special);
                    if (note != null && sub != null && special != null)
                    {
                        standIns.Add(row.English);
                    }
                }
                if (sub == null || special == null)
                {
                    skipped.Add($"{row.English}(缺 {(sub == null ? EnglishSub(row.Sub) : EnglishSpecial(row.Special))})");
                    continue;
                }
                string description = S3KitTable.Description(row) + (note != null ? "\n" + note : "");
                M7Setup.AddKit(kits, row.KitId, row.Name, row.English, description, main, sub, special, row.SpecialPoints,
                    (WeightClass)family.Weight, S3KitTable.StatLabel(row.Stat1), row.Value1, S3KitTable.StatLabel(row.Stat2),
                    row.Value2, S3KitTable.StatLabel(row.Stat3), row.Value3);
            }
            Debug.Log($"[M7SETUP] S3 武器組合 {kits.Count - before} 套(暫代零件 {standIns.Count}:{string.Join("、", standIns)});" +
                      $"缺零件未建 {skipped.Count} 套:{string.Join("、", skipped)}");
        }

        // 原零件不存在時換成暫代零件;回傳說明文字(沒套用 = null)
        private static string ApplyStandIn(in S3KitTable.Row row, in S3KitTable.StandIn standIn, ref SubWeaponConfig sub,
            ref SpecialWeaponConfig special)
        {
            S3KitTable.TryGetRow(standIn.SourceRowId, out S3KitTable.Row source);
            if (row.Sub == standIn.Missing && sub == null)
            {
                sub = LoadSub(standIn.Replacement);
                return sub != null
                    ? $"(暫代:S3 原配副武器 {EnglishSub(standIn.Missing)} 尚未實作,先配 {source.Name} 的{sub.DisplayName}。)"
                    : null;
            }
            if (row.Special == standIn.Missing && special == null)
            {
                special = LoadSpecial(standIn.Replacement);
                return special != null
                    ? $"(暫代:S3 原配大招 {EnglishSpecial(standIn.Missing)} 尚未實作,先配 {source.Name} 的{special.SpecialName}。)"
                    : null;
            }
            return null;
        }

        public static WeaponKit FindPlayerDefault(List<WeaponKit> kits)
        {
            foreach (WeaponKit kit in kits)
            {
                if (kit != null && kit.Id == PlayerDefaultKitId)
                {
                    return kit;
                }
            }
            Debug.LogWarning($"[M7SETUP] 沒有 {PlayerDefaultKitId} 組合,Player.prefab 改用清單第一組");
            return kits.Count > 0 ? kits[0] : null;
        }

        // 早期武器的設定在 Assets/Data;之後的模組照約定放 Resources/Weapons
        private static string MainPath(string id)
        {
            return id == "splattershot" ? "Assets/Data/WeaponConfig.asset" : $"Assets/Resources/Weapons/Main_{id}.asset";
        }

        private static SubWeaponConfig LoadSub(string internalName)
        {
            if (!S3KitTable.TryGetSub(internalName, out S3KitTable.Part part))
            {
                Debug.LogError($"[M7SETUP] ERROR 副武器 {internalName} 沒有對照");
                return null;
            }
            string path = part.AssetId == "splat_bomb" ? "Assets/Data/SplatBombConfig.asset"
                : $"Assets/Resources/Weapons/Sub_{part.AssetId}.asset";
            return AssetDatabase.LoadAssetAtPath<SubWeaponConfig>(path);
        }

        private static SpecialWeaponConfig LoadSpecial(string internalName)
        {
            if (!S3KitTable.TryGetSpecial(internalName, out S3KitTable.Part part))
            {
                Debug.LogError($"[M7SETUP] ERROR 大招 {internalName} 沒有對照");
                return null;
            }
            string path = part.AssetId == "triple_inkstrike" ? "Assets/Data/TripleInkstrikeConfig.asset"
                : $"Assets/Resources/Weapons/Special_{part.AssetId}.asset";
            return AssetDatabase.LoadAssetAtPath<SpecialWeaponConfig>(path);
        }

        private static string EnglishSub(string internalName)
        {
            return S3KitTable.TryGetSub(internalName, out S3KitTable.Part part) ? part.English : internalName;
        }

        private static string EnglishSpecial(string internalName)
        {
            return S3KitTable.TryGetSpecial(internalName, out S3KitTable.Part part) ? part.English : internalName;
        }
    }
}

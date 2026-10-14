using System.Collections.Generic;

namespace SplatoonC.Core.Combat
{
    // Splatoon 3 武器組合總表(對戰用,Leanny v11.3.0 WeaponInfoMain.json 中 Type == "Versus" 的列:
    // SubWeapon / SpecialWeapon / SpecialPoint / UIParam;英文名 USen.json;重量 DESIGN_REFERENCE §3.4)。
    // 只收本專案已實作主武器的全部組合(含副武器/大招尚未實作的):setup 依零件資產是否存在決定要不要建,
    // 缺的零件做好之後重跑 M7 Setup 就會自動加入選單。列的順序 = S3 武器 Id 順序(遊戲內清單順序)。
    public static class S3KitTable
    {
        public readonly struct Row
        {
            // WeaponInfoMain 的 __RowId(例 Shooter_Normal_01)
            public readonly string RowId;
            // 組合資產 id(Kit_<id>.asset,英文 snake_case)
            public readonly string KitId;
            // 繁中名稱(S3 繁中版)
            public readonly string Name;
            public readonly string English;
            // S3 內部名(WeaponInfoSub / WeaponInfoSpecial 的列名,例 Bomb_Suction、SpUltraShot)
            public readonly string Sub;
            public readonly string Special;
            public readonly int SpecialPoints;
            // 選單三條能力值(UIParam 的 Type / Value)
            public readonly string Stat1;
            public readonly int Value1;
            public readonly string Stat2;
            public readonly int Value2;
            public readonly string Stat3;
            public readonly int Value3;
            // 說明的補充(複製品等);null = 只用主武器說明
            public readonly string Note;

            public Row(string rowId, string kitId, string name, string english, string sub, string special, int points,
                string stat1, int value1, string stat2, int value2, string stat3, int value3, string note = null)
            {
                RowId = rowId;
                KitId = kitId;
                Name = name;
                English = english;
                Sub = sub;
                Special = special;
                SpecialPoints = points;
                Stat1 = stat1;
                Value1 = value1;
                Stat2 = stat2;
                Value2 = value2;
                Stat3 = stat3;
                Value3 = value3;
                Note = note;
            }

            // 主武器系列(去掉最後一段變體編號):Shooter_Normal_01 → Shooter_Normal
            public string Family => FamilyOf(RowId);
        }

        // 主武器系列:本專案的主武器設定 id(Main_<id>.asset)、重量級別(0 輕 / 1 中 / 2 重,與 WeightClass 一致)、選單說明
        public readonly struct Family
        {
            public readonly string Name;
            public readonly string MainId;
            public readonly int Weight;
            public readonly string Blurb;

            public Family(string name, string mainId, int weight, string blurb)
            {
                Name = name;
                MainId = mainId;
                Weight = weight;
                Blurb = blurb;
            }
        }

        // 零件(副武器/大招):S3 內部名 → 本專案資產 id(Sub_<id>.asset / Special_<id>.asset)與英文名。
        // 尚未實作的零件也列出(id 照模組打包約定),做好後組合會自動成立。
        public readonly struct Part
        {
            public readonly string Internal;
            public readonly string AssetId;
            public readonly string English;

            public Part(string internalName, string assetId, string english)
            {
                Internal = internalName;
                AssetId = assetId;
                English = english;
            }
        }

        // 暫代零件:某把主武器的所有組合都還缺零件時,先用同系列其他組合的零件頂替,讓每把已實作的主武器都選得到。
        // 只在原本的零件資產不存在時套用;原零件做好後自動換回 S3 正式組合。
        public readonly struct StandIn
        {
            public readonly string RowId;
            public readonly string Missing;
            public readonly string Replacement;
            // 頂替零件的來源組合(說明文字用)
            public readonly string SourceRowId;

            public StandIn(string rowId, string missing, string replacement, string sourceRowId)
            {
                RowId = rowId;
                Missing = missing;
                Replacement = replacement;
                SourceRowId = sourceRowId;
            }
        }

        public static readonly Family[] Families =
        {
            new Family("Shooter_First", "splattershot_jr", 0, "墨槽容量 110%、散布大、塗地快的入門射擊槍;四發擊倒。"),
            new Family("Shooter_Normal", "splattershot", 1, "萬用射擊槍:射程、威力、射速均衡,三發擊倒。"),
            new Family("Shooter_Gravity", "52_gal", 1, "單發傷害高的射擊槍,兩發擊倒;射速慢、散布大。"),
            new Family("Shooter_QuickMiddle", "n_zap_85", 0, "射速快的輕量射擊槍,四發擊倒;跑得快,擅長塗地與繞後。"),
            new Family("Shooter_Expert", "splattershot_pro", 1, "射程長、三發擊倒的射擊槍;射速較慢、耗墨較多。"),
            new Family("Blaster_Short", "luna_blaster", 0, "近距爆破槍:爆炸範圍大、射程短;直擊一發擊倒。"),
            new Family("Blaster_Middle", "blaster", 1, "爆破槍:直擊一發擊倒,墨彈飛到盡頭或碰到地形就爆炸,能隔著掩體炸人。"),
            new Family("Roller_Compact", "carbon_roller", 0, "輕量滾筒:舉起快、機動性高,甩墨射程短;適合伏擊。"),
            new Family("Roller_Normal", "splat_roller", 1, "按住推滾塗地、點按甩墨一擊擊倒;跳起縱甩射程更長。"),
            new Family("Roller_Heavy", "dynamo_roller", 2, "重量級滾筒:舉起慢,但甩墨範圍廣、射程遠,塗地量驚人。"),
            new Family("Charger_Normal", "splat_charger", 1, "蓄滿一擊擊倒的狙擊槍,蓄力約 1 秒;射程長,瞄準時有雷射。"),
            new Family("Charger_Long", "e_liter_4k", 2, "射程最長的狙擊槍,蓄滿一擊擊倒;蓄力約 1.5 秒、重量級。"),
            new Family("Maneuver_Normal", "splat_dualies", 1, "雙槍:開火中可翻滾閃避,翻滾後轉為準度更高的定點射擊;四發擊倒。"),
        };

        public static readonly Part[] Subs =
        {
            new Part("Bomb_Splash", "splat_bomb", "Splat Bomb"),
            new Part("Bomb_Suction", "suction_bomb", "Suction Bomb"),
            new Part("Bomb_Quick", "burst_bomb", "Burst Bomb"),
            new Part("Bomb_Curling", "curling_bomb", "Curling Bomb"),
            new Part("Bomb_Robot", "autobomb", "Autobomb"),
            new Part("Bomb_Torpedo", "torpedo", "Torpedo"),
            new Part("Bomb_Fizzy", "fizzy_bomb", "Fizzy Bomb"),
            new Part("PointSensor", "point_sensor", "Point Sensor"),
            new Part("LineMarker", "angle_shooter", "Angle Shooter"),
            new Part("Beacon", "squid_beakon", "Squid Beakon"),
            new Part("Shield", "splash_wall", "Splash Wall"),
            new Part("Sprinkler", "sprinkler", "Sprinkler"),
            new Part("Trap", "ink_mine", "Ink Mine"),
            new Part("PoisonMist", "toxic_mist", "Toxic Mist"),
        };

        public static readonly Part[] Specials =
        {
            new Part("SpUltraShot", "trizooka", "Trizooka"),
            new Part("SpGreatBarrier", "big_bubbler", "Big Bubbler"),
            new Part("SpMicroLaser", "killer_wail", "Killer Wail 5.1"),
            new Part("SpShockSonar", "wave_breaker", "Wave Breaker"),
            new Part("SpEnergyStand", "tacticooler", "Tacticooler"),
            new Part("SpChariot", "crab_tank", "Crab Tank"),
            new Part("SpBlower", "ink_vac", "Ink Vac"),
            new Part("SpUltraStamp", "ultra_stamp", "Ultra Stamp"),
            new Part("SpTripleTornado", "triple_inkstrike", "Triple Inkstrike"),
            new Part("SpSuperHook", "zipcaster", "Zipcaster"),
            new Part("SpNiceBall", "booyah_bomb", "Booyah Bomb"),
            new Part("SpMultiMissile", "tenta_missiles", "Tenta Missiles"),
            new Part("SpInkStorm", "ink_storm", "Ink Storm"),
            new Part("SpSkewer", "reefslider", "Reefslider"),
            new Part("SpJetpack", "inkjet", "Inkjet"),
            new Part("SpCastle", "kraken_royale", "Kraken Royale"),
            new Part("SpFirework", "super_chump", "Super Chump"),
            new Part("SpPogo", "triple_splashdown", "Triple Splashdown"),
            new Part("SpChimney", "splattercolor_screen", "Splattercolor Screen"),
        };

        // .52 Gal / E-liter 4K 的所有組合都缺零件(Splash Wall、Ink Mine、Splattercolor Screen、Kraken Royale 未實作)
        public static readonly StandIn[] StandIns =
        {
            new StandIn("Shooter_Gravity_00", "Shield", "Bomb_Curling", "Shooter_Gravity_01"),
            new StandIn("Charger_Long_00", "Trap", "Beacon", "Charger_Long_01"),
        };

        private const string Replica = "外觀複製品,性能與原版相同。";

        public static readonly Row[] Rows =
        {
            new Row("Shooter_First_00", "splattershot_jr", "新葉射擊槍", "Splattershot Jr.", "Bomb_Splash", "SpGreatBarrier", 180,
                "Range", 36, "Power", 22, "Blaze", 75),
            new Row("Shooter_First_01", "custom_splattershot_jr", "紅葉射擊槍", "Custom Splattershot Jr.", "Bomb_Torpedo", "SpShockSonar", 190,
                "Range", 36, "Power", 22, "Blaze", 75),
            new Row("Shooter_Normal_00", "splattershot", "斯普拉射擊槍", "Splattershot", "Bomb_Suction", "SpUltraShot", 200,
                "Range", 52, "Power", 47, "Blaze", 60),
            new Row("Shooter_Normal_01", "tentatek_splattershot", "斯普拉射擊槍 聯名", "Tentatek Splattershot", "Bomb_Splash", "SpTripleTornado", 190,
                "Range", 52, "Power", 47, "Blaze", 60),
            new Row("Shooter_Normal_02", "glamorz_splattershot", "斯普拉射擊槍 煌", "Glamorz Splattershot", "Bomb_Quick", "SpCastle", 190,
                "Range", 52, "Power", 47, "Blaze", 60),
            new Row("Shooter_Normal_H", "hero_shot_replica", "英雄射擊槍 複製品", "Hero Shot Replica", "Bomb_Suction", "SpUltraShot", 200,
                "Range", 52, "Power", 47, "Blaze", 60, Replica),
            new Row("Shooter_Normal_Oct", "octo_shot_replica", "章魚射擊槍 複製品", "Octo Shot Replica", "Bomb_Splash", "SpTripleTornado", 190,
                "Range", 52, "Power", 47, "Blaze", 60, Replica),
            new Row("Shooter_Normal_O", "order_shot_replica", "秩序射擊槍 複製品", "Order Shot Replica", "Bomb_Suction", "SpUltraShot", 200,
                "Range", 52, "Power", 47, "Blaze", 60, Replica),
            new Row("Shooter_Normal_S", "plastic_bottle_shot_replica", "寶特瓶射擊槍 複製品", "Plastic-Bottle Shot Replica", "Bomb_Splash",
                "SpTripleTornado", 190, "Range", 52, "Power", 47, "Blaze", 60, Replica),
            new Row("Shooter_Gravity_00", "52_gal", ".52加侖", ".52 Gal", "Shield", "SpMicroLaser", 210,
                "Range", 55, "Power", 75, "Blaze", 25),
            new Row("Shooter_Gravity_01", "52_gal_deco", ".52加侖 裝飾", ".52 Gal Deco", "Bomb_Curling", "SpChimney", 190,
                "Range", 55, "Power", 75, "Blaze", 25),
            new Row("Shooter_QuickMiddle_00", "n_zap_85", "N-ZAP85", "N-ZAP '85", "Bomb_Suction", "SpEnergyStand", 200,
                "Range", 50, "Power", 29, "Blaze", 75),
            new Row("Shooter_QuickMiddle_01", "n_zap_89", "N-ZAP89", "N-ZAP '89", "Bomb_Robot", "SpFirework", 170,
                "Range", 50, "Power", 29, "Blaze", 75),
            new Row("Shooter_Expert_00", "splattershot_pro", "頂尖射擊槍", "Splattershot Pro", "LineMarker", "SpChariot", 190,
                "Range", 70, "Power", 64, "Blaze", 30),
            new Row("Shooter_Expert_01", "forge_splattershot_pro", "頂尖射擊槍 聯名", "Forge Splattershot Pro", "Bomb_Suction", "SpNiceBall", 200,
                "Range", 70, "Power", 64, "Blaze", 30),
            new Row("Shooter_Expert_02", "splattershot_pro_frz_n", "頂尖射擊槍 FRZN", "Splattershot Pro FRZ-N", "Bomb_Splash", "SpMultiMissile", 180,
                "Range", 70, "Power", 64, "Blaze", 30),
            new Row("Blaster_Short_00", "luna_blaster", "新星爆破槍", "Luna Blaster", "Bomb_Splash", "SpSuperHook", 170,
                "Range", 19, "Explosion", 70, "Blaze", 30),
            new Row("Blaster_Short_01", "luna_blaster_neo", "新星爆破槍 新型", "Luna Blaster Neo", "Bomb_Fizzy", "SpUltraStamp", 200,
                "Range", 19, "Explosion", 70, "Blaze", 30),
            new Row("Blaster_Short_O", "order_blaster_replica", "秩序爆破槍 複製品", "Order Blaster Replica", "Bomb_Splash", "SpSuperHook", 170,
                "Range", 19, "Explosion", 70, "Blaze", 30, Replica),
            new Row("Blaster_Middle_00", "blaster", "火熱爆破槍", "Blaster", "Bomb_Robot", "SpGreatBarrier", 190,
                "Range", 27, "Explosion", 70, "Blaze", 20),
            new Row("Blaster_Middle_01", "custom_blaster", "火熱爆破槍 改裝", "Custom Blaster", "PointSensor", "SpPogo", 190,
                "Range", 27, "Explosion", 70, "Blaze", 20),
            new Row("Blaster_Middle_02", "gleamz_blaster", "火熱爆破槍 艷", "Gleamz Blaster", "Beacon", "SpChariot", 180,
                "Range", 27, "Explosion", 70, "Blaze", 20),
            new Row("Roller_Compact_00", "carbon_roller", "碳纖維滾筒", "Carbon Roller", "Bomb_Robot", "SpSuperHook", 160,
                "Range", 20, "PaintSpeed", 68, "Weight", 65),
            new Row("Roller_Compact_01", "carbon_roller_deco", "碳纖維滾筒 裝飾", "Carbon Roller Deco", "Bomb_Quick", "SpUltraShot", 190,
                "Range", 20, "PaintSpeed", 68, "Weight", 65),
            new Row("Roller_Compact_02", "carbon_roller_ang_l", "碳纖維滾筒 ANGL", "Carbon Roller ANG-L", "Bomb_Fizzy", "SpFirework", 170,
                "Range", 20, "PaintSpeed", 68, "Weight", 65),
            new Row("Roller_Normal_00", "splat_roller", "斯普拉滾筒", "Splat Roller", "Bomb_Curling", "SpGreatBarrier", 180,
                "Range", 48, "PaintSpeed", 45, "Weight", 55),
            new Row("Roller_Normal_01", "krak_on_splat_roller", "斯普拉滾筒 聯名", "Krak-On Splat Roller", "Beacon", "SpCastle", 170,
                "Range", 48, "PaintSpeed", 45, "Weight", 55),
            new Row("Roller_Normal_O", "order_roller_replica", "秩序滾筒 複製品", "Order Roller Replica", "Bomb_Curling", "SpGreatBarrier", 180,
                "Range", 48, "PaintSpeed", 45, "Weight", 55, Replica),
            new Row("Roller_Heavy_00", "dynamo_roller", "電動馬達滾筒", "Dynamo Roller", "Sprinkler", "SpEnergyStand", 210,
                "Range", 76, "PaintSpeed", 25, "Weight", 20),
            new Row("Roller_Heavy_01", "gold_dynamo_roller", "電動馬達滾筒 特斯拉", "Gold Dynamo Roller", "Bomb_Splash", "SpFirework", 190,
                "Range", 76, "PaintSpeed", 25, "Weight", 20),
            new Row("Roller_Heavy_02", "starz_dynamo_roller", "電動馬達滾筒 冥", "Starz Dynamo Roller", "PointSensor", "SpMicroLaser", 180,
                "Range", 76, "PaintSpeed", 25, "Weight", 20),
            new Row("Charger_Normal_00", "splat_charger", "斯普拉狙擊槍", "Splat Charger", "Bomb_Splash", "SpBlower", 190,
                "Range", 88, "Charge", 50, "Mobility", 40),
            new Row("Charger_Normal_01", "z_f_splat_charger", "斯普拉狙擊槍 聯名", "Z+F Splat Charger", "Shield", "SpTripleTornado", 200,
                "Range", 88, "Charge", 50, "Mobility", 40),
            new Row("Charger_Normal_02", "splat_charger_cam_o", "斯普拉狙擊槍 FRST", "Splat Charger CAM-O", "Sprinkler", "SpChariot", 200,
                "Range", 88, "Charge", 50, "Mobility", 40),
            new Row("Charger_Normal_O", "order_charger_replica", "秩序狙擊槍 複製品", "Order Charger Replica", "Bomb_Splash", "SpBlower", 190,
                "Range", 88, "Charge", 50, "Mobility", 40, Replica),
            new Row("Charger_Long_00", "e_liter_4k", "公升4K", "E-liter 4K", "Trap", "SpShockSonar", 210,
                "Range", 96, "Charge", 20, "Mobility", 15),
            new Row("Charger_Long_01", "custom_e_liter_4k", "公升4K 改裝", "Custom E-liter 4K", "Beacon", "SpCastle", 210,
                "Range", 96, "Charge", 20, "Mobility", 15),
            new Row("Maneuver_Normal_00", "splat_dualies", "斯普拉雙槍", "Splat Dualies", "Bomb_Suction", "SpChariot", 200,
                "Range", 50, "Power", 29, "Mobility", 60),
            new Row("Maneuver_Normal_01", "enperry_splat_dualies", "斯普拉雙槍 聯名", "Enperry Splat Dualies", "Bomb_Curling", "SpPogo", 190,
                "Range", 50, "Power", 29, "Mobility", 60),
            new Row("Maneuver_Normal_02", "twinklez_splat_dualies", "斯普拉雙槍 耀", "Twinklez Splat Dualies", "Bomb_Fizzy", "SpGreatBarrier", 190,
                "Range", 50, "Power", 29, "Mobility", 60),
            new Row("Maneuver_Normal_O", "order_dualie_replicas", "秩序雙槍 複製品", "Order Dualie Replicas", "Bomb_Suction", "SpChariot", 200,
                "Range", 50, "Power", 29, "Mobility", 60, Replica),
        };

        public static string FamilyOf(string rowId)
        {
            int cut = rowId != null ? rowId.LastIndexOf('_') : -1;
            return cut > 0 ? rowId.Substring(0, cut) : rowId;
        }

        public static bool TryGetFamily(string name, out Family family)
        {
            for (int i = 0; i < Families.Length; i++)
            {
                if (Families[i].Name == name)
                {
                    family = Families[i];
                    return true;
                }
            }
            family = default;
            return false;
        }

        public static bool TryGetRow(string rowId, out Row row)
        {
            for (int i = 0; i < Rows.Length; i++)
            {
                if (Rows[i].RowId == rowId)
                {
                    row = Rows[i];
                    return true;
                }
            }
            row = default;
            return false;
        }

        public static bool TryGetSub(string internalName, out Part part)
        {
            return TryFind(Subs, internalName, out part);
        }

        public static bool TryGetSpecial(string internalName, out Part part)
        {
            return TryFind(Specials, internalName, out part);
        }

        // 暫代零件(沒有 = false)
        public static bool TryGetStandIn(string rowId, out StandIn standIn)
        {
            for (int i = 0; i < StandIns.Length; i++)
            {
                if (StandIns[i].RowId == rowId)
                {
                    standIn = StandIns[i];
                    return true;
                }
            }
            standIn = default;
            return false;
        }

        // 選單能力條名稱(UIParam 的 Type;英文對照 USen WeaponParamName)
        public static string StatLabel(string uiParamType)
        {
            switch (uiParamType)
            {
                case "Range": return "射程";
                case "Power": return "威力";
                case "Blaze": return "射速";
                case "Explosion": return "爆破力";
                case "Charge": return "蓄力速度";
                case "Mobility": return "機動性";
                case "PaintSpeed": return "塗墨速度";
                case "Weight": return "操作性";
                case "Defence": return "耐久度";
                default: return uiParamType;
            }
        }

        // 選單說明:主武器說明 + 補充
        public static string Description(in Row row)
        {
            string blurb = TryGetFamily(row.Family, out Family family) ? family.Blurb : "";
            return string.IsNullOrEmpty(row.Note) ? blurb : blurb + "\n" + row.Note;
        }

        private static bool TryFind(IReadOnlyList<Part> parts, string internalName, out Part part)
        {
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i].Internal == internalName)
                {
                    part = parts[i];
                    return true;
                }
            }
            part = default;
            return false;
        }
    }
}

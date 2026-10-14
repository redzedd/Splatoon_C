namespace SplatoonC.Core.Combat
{
    // 攻擊類別:決定對「物件」的傷害倍率(S3 spl__DamageRateInfoConfig 的列)。對玩家一律 ×1.0。
    // 數值固定(資產以整數序列化),新增類別只能往後加,不可改既有編號。
    // 對照 datamine 列名寫在 DamageRates 表的註解裡。
    public enum AttackClass
    {
        // 未分類:對所有物件 ×1.0
        Other = 0,

        // ── 主武器 ──
        Shooter = 1,        // Splattershot / N-ZAP(Shooter)
        HeavyShooter = 2,   // .52 Gal(Shooter_Gravity);.96 近似
        ShooterJr = 3,      // Splattershot Jr.(Shooter_First)
        ShooterPro = 4,     // Splattershot Pro(Shooter_Expert)
        Blaster = 10,       // Blaster(Blaster_BlasterMiddle)
        LunaBlaster = 11,   // Luna Blaster(Blaster_BlasterShort)
        RollerContact = 20, // 推滾接觸(RollerCore)
        RollerFlick = 21,   // Splat / Carbon 甩墨(RollerSplash)
        DynamoFlick = 22,   // Dynamo 甩墨(RollerSplash_Heavy)
        BrushContact = 23,  // 筆刷奔跑接觸(BrushCore)
        BrushFlick = 24,    // 筆刷甩墨(BrushSplash)
        Charger = 30,       // Splat Charger 部分蓄力(Charger)
        ChargerFull = 31,   // Splat Charger 滿蓄(ChargerFull)
        ELiter = 32,        // E-liter 部分蓄力(Charger_Long)
        ELiterFull = 33,    // E-liter 滿蓄(ChargerFull_Long)
        Slosher = 40,       // Slosher / Tri-Slosher(Slosher)
        Splatling = 41,     // Heavy / Mini Splatling(Spinner)
        Dualies = 42,       // Splat Dualies(Maneuver)
        BrellaShot = 43,    // 傘的彈丸(ShelterShot)
        BrellaCanopy = 44,  // 傘面接觸/射出傘面(ShelterCanopy)
        Stringer = 45,      // Tri-Stringer(Stringer)
        SplatanaWave = 46,       // 刮水刀橫波(Saber_Shot)
        SplatanaChargeWave = 47, // 刮水刀蓄力縱波(Saber_ChargeShot)
        SplatanaSlash = 48,      // 刮水刀刀身(Saber_Slash)
        SplatanaChargeSlash = 49, // 刮水刀蓄力刀身(Saber_ChargeSlash)

        // ── 副武器 ──
        Bomb = 60,           // Splat / Suction / Auto / Curling 爆炸(Bomb)
        BombDirectHit = 61,  // Burst Bomb 直擊(Bomb_DirectHit)
        Fizzy = 62,          // Fizzy Bomb(Bomb_Fizzy)
        CurlingContact = 63, // Curling 滑行接觸(Bomb_CurlingBullet)
        InkMine = 64,        // Ink Mine(Bomb_Trap)
        Torpedo = 65,        // Torpedo 爆炸(Bomb_TorpedoBullet)
        TorpedoDrop = 66,    // Torpedo 墨滴(Bomb_TorpedoSplashBurst)
        Sprinkler = 67,      // Sprinkler 墨滴(Sprinkler)
        SplashWallContact = 68, // Splash Wall 接觸(Shield)

        // ── 大招 ──
        Special = 80,        // 未細分的大招(Default 列:全部 ×1.0)
        Inkstrike = 81,      // Triple Inkstrike 龍捲(TripleTornado)
        Trizooka = 82,       // Trizooka(UltraShot)
        InkStorm = 83,       // Ink Storm 雨(InkStormRain:對所有物件 0)
        KillerWail = 84,     // Killer Wail 5.1(MicroLaser)
        BooyahBomb = 85,     // Booyah Bomb(NiceBall)
        UltraStampSwing = 86, // Ultra Stamp 敲擊(UltraStamp_Swing)
        UltraStampThrow = 87, // Ultra Stamp 丟出(UltraStamp_Throw_BombCore)
        Reefslider = 88,     // Reefslider(Skewer)
        TentaMissiles = 89,  // Tenta Missiles 爆炸(MultiMissile_BombCore)
        CrabTankCannon = 90, // Crab Tank 砲台/砲彈(Chariot_Cannon)
        CrabTankBody = 91,   // Crab Tank 滾球接觸(Chariot_Body)
        Inkjet = 92,         // Inkjet 砲彈(Jetpack_BombCore)
        InkVac = 93,         // Ink Vac 射出(BlowerExhale_BombCore)
        WaveBreaker = 94,    // Wave Breaker 波(ShockSonar_Wave)
        Zipcaster = 95,      // Zipcaster 落地(SuperHook)
        Kraken = 96,         // Kraken Royale(Castle)
        TripleSplashdown = 97, // Triple Splashdown(Pogo)
    }

    // 受擊物件類別(S3 spl__DamageRateInfoConfig 的欄)。Player 永遠 ×1.0。
    public enum TargetClass
    {
        Player = 0,
        SplashWall = 1,       // Wsb_Shield
        Sprinkler = 2,        // Wsb_Sprinkler
        Beakon = 3,           // Wsb_Flag
        BrellaCanopy = 4,     // BulletUmbrellaCanopyNormal
        BubblerShield = 5,    // GreatBarrier_Barrier
        BubblerWeakPoint = 6, // GreatBarrier_WeakPoint
        CrabTank = 7,         // Chariot
        BooyahArmor = 8,      // NiceBall_Armor
        RainmakerBarrier = 9, // Gachihoko_Barrier
        Torpedo = 10,         // Bomb_TorpedoBullet(變身中的 Torpedo)
        WaveBreaker = 11,     // ShockSonar(Wave Breaker 裝置)
        Decoy = 12,           // Super Chump 誘餌
        // 其他裝置/物件(Default 欄;例:Ink Storm 雨 0)
        Device = 13,
    }

    // 對物件傷害倍率矩陣(S3 v11.3.0 spl__DamageRateInfoConfig,Leanny datamine;DESIGN_REFERENCE §3.6)。
    // 用法:受擊物件自己套倍率——damage × DamageRates.Get(info.AttackClass, 自己的 TargetClass)。
    // 規則:對玩家一律 ×1.0;表中沒有的格子 ×1.0(datamine 缺 DamageRate 的格子也是預設 1.0)。
    // 本表由 research/gap07/dmgrate.json 以腳本產生(只列 ≠ 1.0 的格子),不要手改數值。
    public static class DamageRates
    {
        private const int AttackSlots = 128;
        private const int TargetSlots = 16;

        private static readonly float[] Table = BuildTable();

        public static float Get(AttackClass attack, TargetClass target)
        {
            if (target == TargetClass.Player)
            {
                return 1f;
            }
            int a = (int)attack;
            int t = (int)target;
            if (a < 0 || a >= AttackSlots || t < 0 || t >= TargetSlots)
            {
                return 1f;
            }
            return Table[a * TargetSlots + t];
        }

        // 套倍率後向下取整到 0.1(S3:所有倍率算完才取整)
        public static float Apply(float damage, AttackClass attack, TargetClass target)
        {
            return VitalsModel.Floor01(damage * Get(attack, target));
        }

        private static float[] _building;

        private static void Set(AttackClass attack, TargetClass target, float rate)
        {
            _building[(int)attack * TargetSlots + (int)target] = rate;
        }

        private static float[] BuildTable()
        {
            _building = new float[AttackSlots * TargetSlots];
            for (int i = 0; i < _building.Length; i++)
            {
                _building[i] = 1f;
            }

            // Shooter <- Shooter
            Set(AttackClass.Shooter, TargetClass.BrellaCanopy, 0.7f);
            Set(AttackClass.Shooter, TargetClass.BooyahArmor, 0.7f);
            // HeavyShooter <- Shooter_Gravity
            Set(AttackClass.HeavyShooter, TargetClass.SplashWall, 1.1f);
            Set(AttackClass.HeavyShooter, TargetClass.BrellaCanopy, 0.85f);
            Set(AttackClass.HeavyShooter, TargetClass.BooyahArmor, 0.7f);
            Set(AttackClass.HeavyShooter, TargetClass.WaveBreaker, 1.1f);
            // ShooterJr <- Shooter_First
            Set(AttackClass.ShooterJr, TargetClass.SplashWall, 1.1f);
            Set(AttackClass.ShooterJr, TargetClass.BrellaCanopy, 0.7f);
            Set(AttackClass.ShooterJr, TargetClass.BooyahArmor, 0.7f);
            // ShooterPro <- Shooter_Expert
            Set(AttackClass.ShooterPro, TargetClass.BrellaCanopy, 0.7f);
            Set(AttackClass.ShooterPro, TargetClass.BooyahArmor, 0.6f);
            // Blaster <- Blaster_BlasterMiddle
            Set(AttackClass.Blaster, TargetClass.SplashWall, 2f);
            Set(AttackClass.Blaster, TargetClass.Sprinkler, 2.4f);
            Set(AttackClass.Blaster, TargetClass.Beakon, 2.4f);
            Set(AttackClass.Blaster, TargetClass.BrellaCanopy, 2.2f);
            Set(AttackClass.Blaster, TargetClass.BubblerShield, 1.9f);
            Set(AttackClass.Blaster, TargetClass.BubblerWeakPoint, 1.9f);
            Set(AttackClass.Blaster, TargetClass.RainmakerBarrier, 1.9f);
            Set(AttackClass.Blaster, TargetClass.WaveBreaker, 2f);
            // LunaBlaster <- Blaster_BlasterShort
            Set(AttackClass.LunaBlaster, TargetClass.SplashWall, 2.2f);
            Set(AttackClass.LunaBlaster, TargetClass.Sprinkler, 2.4f);
            Set(AttackClass.LunaBlaster, TargetClass.Beakon, 2.4f);
            Set(AttackClass.LunaBlaster, TargetClass.BrellaCanopy, 3.6f);
            Set(AttackClass.LunaBlaster, TargetClass.BubblerShield, 2.1f);
            Set(AttackClass.LunaBlaster, TargetClass.BubblerWeakPoint, 2.1f);
            Set(AttackClass.LunaBlaster, TargetClass.RainmakerBarrier, 2.1f);
            Set(AttackClass.LunaBlaster, TargetClass.WaveBreaker, 2f);
            // RollerContact <- RollerCore
            Set(AttackClass.RollerContact, TargetClass.Sprinkler, 100f);
            Set(AttackClass.RollerContact, TargetClass.Beakon, 100f);
            Set(AttackClass.RollerContact, TargetClass.RainmakerBarrier, 0f);
            // RollerFlick <- RollerSplash
            Set(AttackClass.RollerFlick, TargetClass.SplashWall, 2.64f);
            Set(AttackClass.RollerFlick, TargetClass.Sprinkler, 2.88f);
            Set(AttackClass.RollerFlick, TargetClass.Beakon, 2.88f);
            Set(AttackClass.RollerFlick, TargetClass.BrellaCanopy, 1.7f);
            Set(AttackClass.RollerFlick, TargetClass.BubblerShield, 1.8f);
            Set(AttackClass.RollerFlick, TargetClass.BubblerWeakPoint, 1.8f);
            Set(AttackClass.RollerFlick, TargetClass.CrabTank, 1.4f);
            Set(AttackClass.RollerFlick, TargetClass.BooyahArmor, 1.43f);
            Set(AttackClass.RollerFlick, TargetClass.RainmakerBarrier, 1.8f);
            Set(AttackClass.RollerFlick, TargetClass.WaveBreaker, 1.2f);
            // DynamoFlick <- RollerSplash_Heavy
            Set(AttackClass.DynamoFlick, TargetClass.SplashWall, 2.4f);
            Set(AttackClass.DynamoFlick, TargetClass.Sprinkler, 2.88f);
            Set(AttackClass.DynamoFlick, TargetClass.Beakon, 2.88f);
            Set(AttackClass.DynamoFlick, TargetClass.BrellaCanopy, 1.5f);
            Set(AttackClass.DynamoFlick, TargetClass.BubblerShield, 1.8f);
            Set(AttackClass.DynamoFlick, TargetClass.BubblerWeakPoint, 1.35f);
            Set(AttackClass.DynamoFlick, TargetClass.CrabTank, 1.6f);
            Set(AttackClass.DynamoFlick, TargetClass.BooyahArmor, 1.6f);
            Set(AttackClass.DynamoFlick, TargetClass.RainmakerBarrier, 1.8f);
            Set(AttackClass.DynamoFlick, TargetClass.WaveBreaker, 1.2f);
            // BrushContact <- BrushCore
            Set(AttackClass.BrushContact, TargetClass.Sprinkler, 100f);
            Set(AttackClass.BrushContact, TargetClass.Beakon, 100f);
            Set(AttackClass.BrushContact, TargetClass.RainmakerBarrier, 0f);
            // BrushFlick <- BrushSplash
            Set(AttackClass.BrushFlick, TargetClass.SplashWall, 1.98f);
            Set(AttackClass.BrushFlick, TargetClass.Sprinkler, 1.8f);
            Set(AttackClass.BrushFlick, TargetClass.Beakon, 1.8f);
            Set(AttackClass.BrushFlick, TargetClass.BrellaCanopy, 1.5f);
            Set(AttackClass.BrushFlick, TargetClass.BubblerShield, 1.8f);
            Set(AttackClass.BrushFlick, TargetClass.BubblerWeakPoint, 1.8f);
            Set(AttackClass.BrushFlick, TargetClass.CrabTank, 1.4f);
            Set(AttackClass.BrushFlick, TargetClass.BooyahArmor, 1.4f);
            Set(AttackClass.BrushFlick, TargetClass.RainmakerBarrier, 1.8f);
            Set(AttackClass.BrushFlick, TargetClass.WaveBreaker, 1.8f);
            // Charger <- Charger
            Set(AttackClass.Charger, TargetClass.SplashWall, 2f);
            Set(AttackClass.Charger, TargetClass.Sprinkler, 2f);
            Set(AttackClass.Charger, TargetClass.Beakon, 2f);
            Set(AttackClass.Charger, TargetClass.BrellaCanopy, 3f);
            Set(AttackClass.Charger, TargetClass.BubblerShield, 2f);
            Set(AttackClass.Charger, TargetClass.BubblerWeakPoint, 2f);
            Set(AttackClass.Charger, TargetClass.CrabTank, 1.3f);
            Set(AttackClass.Charger, TargetClass.BooyahArmor, 1.3f);
            Set(AttackClass.Charger, TargetClass.RainmakerBarrier, 2f);
            Set(AttackClass.Charger, TargetClass.WaveBreaker, 2f);
            // ChargerFull <- ChargerFull
            Set(AttackClass.ChargerFull, TargetClass.SplashWall, 2f);
            Set(AttackClass.ChargerFull, TargetClass.Sprinkler, 2f);
            Set(AttackClass.ChargerFull, TargetClass.Beakon, 2f);
            Set(AttackClass.ChargerFull, TargetClass.BrellaCanopy, 3f);
            Set(AttackClass.ChargerFull, TargetClass.BubblerShield, 2f);
            Set(AttackClass.ChargerFull, TargetClass.BubblerWeakPoint, 1.5f);
            Set(AttackClass.ChargerFull, TargetClass.CrabTank, 1.3f);
            Set(AttackClass.ChargerFull, TargetClass.BooyahArmor, 1.3f);
            Set(AttackClass.ChargerFull, TargetClass.RainmakerBarrier, 2f);
            Set(AttackClass.ChargerFull, TargetClass.WaveBreaker, 2f);
            // ELiter <- Charger_Long
            Set(AttackClass.ELiter, TargetClass.SplashWall, 2.2f);
            Set(AttackClass.ELiter, TargetClass.Sprinkler, 2f);
            Set(AttackClass.ELiter, TargetClass.Beakon, 2f);
            Set(AttackClass.ELiter, TargetClass.BrellaCanopy, 3.5f);
            Set(AttackClass.ELiter, TargetClass.BubblerShield, 2f);
            Set(AttackClass.ELiter, TargetClass.BubblerWeakPoint, 2f);
            Set(AttackClass.ELiter, TargetClass.CrabTank, 1.3f);
            Set(AttackClass.ELiter, TargetClass.BooyahArmor, 1.35f);
            Set(AttackClass.ELiter, TargetClass.RainmakerBarrier, 2f);
            Set(AttackClass.ELiter, TargetClass.WaveBreaker, 2.2f);
            // ELiterFull <- ChargerFull_Long
            Set(AttackClass.ELiterFull, TargetClass.SplashWall, 2.2f);
            Set(AttackClass.ELiterFull, TargetClass.Sprinkler, 2f);
            Set(AttackClass.ELiterFull, TargetClass.Beakon, 2f);
            Set(AttackClass.ELiterFull, TargetClass.BrellaCanopy, 3.5f);
            Set(AttackClass.ELiterFull, TargetClass.BubblerShield, 2.3f);
            Set(AttackClass.ELiterFull, TargetClass.BubblerWeakPoint, 1.725f);
            Set(AttackClass.ELiterFull, TargetClass.CrabTank, 1.3f);
            Set(AttackClass.ELiterFull, TargetClass.BooyahArmor, 1.35f);
            Set(AttackClass.ELiterFull, TargetClass.RainmakerBarrier, 2.3f);
            Set(AttackClass.ELiterFull, TargetClass.WaveBreaker, 2.2f);
            // Slosher <- Slosher
            Set(AttackClass.Slosher, TargetClass.SplashWall, 2.5f);
            Set(AttackClass.Slosher, TargetClass.Sprinkler, 2.5f);
            Set(AttackClass.Slosher, TargetClass.Beakon, 2.5f);
            Set(AttackClass.Slosher, TargetClass.BrellaCanopy, 2f);
            Set(AttackClass.Slosher, TargetClass.BubblerShield, 2.4f);
            Set(AttackClass.Slosher, TargetClass.BubblerWeakPoint, 2.4f);
            Set(AttackClass.Slosher, TargetClass.RainmakerBarrier, 2.4f);
            Set(AttackClass.Slosher, TargetClass.WaveBreaker, 1.25f);
            // Splatling <- Spinner
            Set(AttackClass.Splatling, TargetClass.BrellaCanopy, 0.7f);
            Set(AttackClass.Splatling, TargetClass.BooyahArmor, 0.6f);
            // Dualies <- Maneuver
            Set(AttackClass.Dualies, TargetClass.BrellaCanopy, 0.7f);
            Set(AttackClass.Dualies, TargetClass.BooyahArmor, 0.6f);
            // BrellaShot <- ShelterShot
            Set(AttackClass.BrellaShot, TargetClass.SplashWall, 2.4f);
            Set(AttackClass.BrellaShot, TargetClass.Sprinkler, 2.4f);
            Set(AttackClass.BrellaShot, TargetClass.Beakon, 2.4f);
            Set(AttackClass.BrellaShot, TargetClass.BrellaCanopy, 2f);
            Set(AttackClass.BrellaShot, TargetClass.BubblerShield, 2.4f);
            Set(AttackClass.BrellaShot, TargetClass.BubblerWeakPoint, 2.4f);
            Set(AttackClass.BrellaShot, TargetClass.CrabTank, 1.3f);
            Set(AttackClass.BrellaShot, TargetClass.BooyahArmor, 1.3f);
            Set(AttackClass.BrellaShot, TargetClass.RainmakerBarrier, 2.4f);
            Set(AttackClass.BrellaShot, TargetClass.Torpedo, 2.4f);
            Set(AttackClass.BrellaShot, TargetClass.WaveBreaker, 2.4f);
            // BrellaCanopy <- ShelterCanopy
            Set(AttackClass.BrellaCanopy, TargetClass.Sprinkler, 100f);
            Set(AttackClass.BrellaCanopy, TargetClass.Beakon, 100f);
            Set(AttackClass.BrellaCanopy, TargetClass.BrellaCanopy, 5f);
            Set(AttackClass.BrellaCanopy, TargetClass.BubblerShield, 2.4f);
            Set(AttackClass.BrellaCanopy, TargetClass.BubblerWeakPoint, 2.4f);
            Set(AttackClass.BrellaCanopy, TargetClass.RainmakerBarrier, 2.4f);
            Set(AttackClass.BrellaCanopy, TargetClass.Torpedo, 5f);
            Set(AttackClass.BrellaCanopy, TargetClass.WaveBreaker, 5f);
            // Stringer <- Stringer
            Set(AttackClass.Stringer, TargetClass.SplashWall, 2f);
            Set(AttackClass.Stringer, TargetClass.Sprinkler, 2f);
            Set(AttackClass.Stringer, TargetClass.Beakon, 2f);
            Set(AttackClass.Stringer, TargetClass.BrellaCanopy, 3f);
            Set(AttackClass.Stringer, TargetClass.BubblerShield, 2f);
            Set(AttackClass.Stringer, TargetClass.BubblerWeakPoint, 2f);
            Set(AttackClass.Stringer, TargetClass.CrabTank, 1.3f);
            Set(AttackClass.Stringer, TargetClass.BooyahArmor, 1.3f);
            Set(AttackClass.Stringer, TargetClass.RainmakerBarrier, 2f);
            Set(AttackClass.Stringer, TargetClass.WaveBreaker, 2f);
            // SplatanaWave <- Saber_Shot
            Set(AttackClass.SplatanaWave, TargetClass.SplashWall, 4.8f);
            Set(AttackClass.SplatanaWave, TargetClass.Sprinkler, 4.8f);
            Set(AttackClass.SplatanaWave, TargetClass.Beakon, 4.8f);
            Set(AttackClass.SplatanaWave, TargetClass.BrellaCanopy, 4.8f);
            Set(AttackClass.SplatanaWave, TargetClass.BubblerShield, 4.8f);
            Set(AttackClass.SplatanaWave, TargetClass.BubblerWeakPoint, 3.6f);
            Set(AttackClass.SplatanaWave, TargetClass.CrabTank, 2.6f);
            Set(AttackClass.SplatanaWave, TargetClass.BooyahArmor, 1.82f);
            Set(AttackClass.SplatanaWave, TargetClass.RainmakerBarrier, 3.2f);
            Set(AttackClass.SplatanaWave, TargetClass.Torpedo, 4.8f);
            Set(AttackClass.SplatanaWave, TargetClass.WaveBreaker, 2.4f);
            // SplatanaChargeWave <- Saber_ChargeShot
            Set(AttackClass.SplatanaChargeWave, TargetClass.SplashWall, 4.8f);
            Set(AttackClass.SplatanaChargeWave, TargetClass.Sprinkler, 4.8f);
            Set(AttackClass.SplatanaChargeWave, TargetClass.Beakon, 4.8f);
            Set(AttackClass.SplatanaChargeWave, TargetClass.BrellaCanopy, 4.8f);
            Set(AttackClass.SplatanaChargeWave, TargetClass.BubblerShield, 4.8f);
            Set(AttackClass.SplatanaChargeWave, TargetClass.BubblerWeakPoint, 2.4f);
            Set(AttackClass.SplatanaChargeWave, TargetClass.CrabTank, 2.6f);
            Set(AttackClass.SplatanaChargeWave, TargetClass.BooyahArmor, 1.82f);
            Set(AttackClass.SplatanaChargeWave, TargetClass.RainmakerBarrier, 3.2f);
            Set(AttackClass.SplatanaChargeWave, TargetClass.Torpedo, 4.8f);
            Set(AttackClass.SplatanaChargeWave, TargetClass.WaveBreaker, 2.4f);
            // SplatanaSlash <- Saber_Slash
            Set(AttackClass.SplatanaSlash, TargetClass.SplashWall, 2.4f);
            Set(AttackClass.SplatanaSlash, TargetClass.Sprinkler, 2.4f);
            Set(AttackClass.SplatanaSlash, TargetClass.Beakon, 2.4f);
            Set(AttackClass.SplatanaSlash, TargetClass.BrellaCanopy, 2.4f);
            Set(AttackClass.SplatanaSlash, TargetClass.BubblerShield, 2.4f);
            Set(AttackClass.SplatanaSlash, TargetClass.BubblerWeakPoint, 1.8f);
            Set(AttackClass.SplatanaSlash, TargetClass.CrabTank, 1.3f);
            Set(AttackClass.SplatanaSlash, TargetClass.BooyahArmor, 0.91f);
            Set(AttackClass.SplatanaSlash, TargetClass.RainmakerBarrier, 1.6f);
            Set(AttackClass.SplatanaSlash, TargetClass.Torpedo, 2.4f);
            Set(AttackClass.SplatanaSlash, TargetClass.WaveBreaker, 2.4f);
            // SplatanaChargeSlash <- Saber_ChargeSlash
            Set(AttackClass.SplatanaChargeSlash, TargetClass.SplashWall, 1.2f);
            Set(AttackClass.SplatanaChargeSlash, TargetClass.Sprinkler, 1.2f);
            Set(AttackClass.SplatanaChargeSlash, TargetClass.Beakon, 1.2f);
            Set(AttackClass.SplatanaChargeSlash, TargetClass.BrellaCanopy, 1.2f);
            Set(AttackClass.SplatanaChargeSlash, TargetClass.BubblerShield, 1.2f);
            Set(AttackClass.SplatanaChargeSlash, TargetClass.BubblerWeakPoint, 0.9f);
            Set(AttackClass.SplatanaChargeSlash, TargetClass.CrabTank, 0.65f);
            Set(AttackClass.SplatanaChargeSlash, TargetClass.BooyahArmor, 0.455f);
            Set(AttackClass.SplatanaChargeSlash, TargetClass.RainmakerBarrier, 0.8f);
            Set(AttackClass.SplatanaChargeSlash, TargetClass.Torpedo, 1.2f);
            Set(AttackClass.SplatanaChargeSlash, TargetClass.WaveBreaker, 1.2f);
            // Bomb <- Bomb
            Set(AttackClass.Bomb, TargetClass.SplashWall, 2f);
            Set(AttackClass.Bomb, TargetClass.Sprinkler, 2f);
            Set(AttackClass.Bomb, TargetClass.Beakon, 2f);
            Set(AttackClass.Bomb, TargetClass.BrellaCanopy, 2f);
            Set(AttackClass.Bomb, TargetClass.BubblerShield, 2f);
            Set(AttackClass.Bomb, TargetClass.BubblerWeakPoint, 0.5f);
            Set(AttackClass.Bomb, TargetClass.BooyahArmor, 1.5f);
            Set(AttackClass.Bomb, TargetClass.RainmakerBarrier, 2f);
            Set(AttackClass.Bomb, TargetClass.Torpedo, 0.6f);
            // BombDirectHit <- Bomb_DirectHit
            Set(AttackClass.BombDirectHit, TargetClass.SplashWall, 2f);
            Set(AttackClass.BombDirectHit, TargetClass.Sprinkler, 2f);
            Set(AttackClass.BombDirectHit, TargetClass.Beakon, 2f);
            Set(AttackClass.BombDirectHit, TargetClass.BrellaCanopy, 2f);
            Set(AttackClass.BombDirectHit, TargetClass.BubblerShield, 2f);
            Set(AttackClass.BombDirectHit, TargetClass.BubblerWeakPoint, 1.5f);
            Set(AttackClass.BombDirectHit, TargetClass.BooyahArmor, 1.5f);
            Set(AttackClass.BombDirectHit, TargetClass.RainmakerBarrier, 2f);
            Set(AttackClass.BombDirectHit, TargetClass.Torpedo, 0.6f);
            // Fizzy <- Bomb_Fizzy
            Set(AttackClass.Fizzy, TargetClass.SplashWall, 3.6f);
            Set(AttackClass.Fizzy, TargetClass.Sprinkler, 3.2f);
            Set(AttackClass.Fizzy, TargetClass.Beakon, 3.2f);
            Set(AttackClass.Fizzy, TargetClass.BrellaCanopy, 3.6f);
            Set(AttackClass.Fizzy, TargetClass.BubblerShield, 3.6f);
            Set(AttackClass.Fizzy, TargetClass.BubblerWeakPoint, 0.9f);
            Set(AttackClass.Fizzy, TargetClass.CrabTank, 1.6f);
            Set(AttackClass.Fizzy, TargetClass.BooyahArmor, 2.4f);
            Set(AttackClass.Fizzy, TargetClass.RainmakerBarrier, 3.6f);
            Set(AttackClass.Fizzy, TargetClass.Torpedo, 0.6f);
            Set(AttackClass.Fizzy, TargetClass.WaveBreaker, 1.8f);
            // CurlingContact <- Bomb_CurlingBullet
            Set(AttackClass.CurlingContact, TargetClass.Sprinkler, 100f);
            Set(AttackClass.CurlingContact, TargetClass.Beakon, 100f);
            Set(AttackClass.CurlingContact, TargetClass.RainmakerBarrier, 0f);
            Set(AttackClass.CurlingContact, TargetClass.Torpedo, 0.5f);
            // InkMine <- Bomb_Trap
            Set(AttackClass.InkMine, TargetClass.SplashWall, 2f);
            Set(AttackClass.InkMine, TargetClass.Sprinkler, 2f);
            Set(AttackClass.InkMine, TargetClass.Beakon, 2f);
            Set(AttackClass.InkMine, TargetClass.BrellaCanopy, 2f);
            Set(AttackClass.InkMine, TargetClass.BubblerShield, 2f);
            Set(AttackClass.InkMine, TargetClass.BubblerWeakPoint, 2f);
            Set(AttackClass.InkMine, TargetClass.BooyahArmor, 1.5f);
            Set(AttackClass.InkMine, TargetClass.RainmakerBarrier, 2f);
            Set(AttackClass.InkMine, TargetClass.Torpedo, 0.6f);
            // Torpedo <- Bomb_TorpedoBullet
            Set(AttackClass.Torpedo, TargetClass.SplashWall, 4f);
            Set(AttackClass.Torpedo, TargetClass.Sprinkler, 3f);
            Set(AttackClass.Torpedo, TargetClass.Beakon, 3f);
            Set(AttackClass.Torpedo, TargetClass.BrellaCanopy, 4f);
            Set(AttackClass.Torpedo, TargetClass.BubblerShield, 4f);
            Set(AttackClass.Torpedo, TargetClass.CrabTank, 2f);
            Set(AttackClass.Torpedo, TargetClass.BooyahArmor, 2.5f);
            Set(AttackClass.Torpedo, TargetClass.RainmakerBarrier, 4f);
            Set(AttackClass.Torpedo, TargetClass.WaveBreaker, 2f);
            // TorpedoDrop <- Bomb_TorpedoSplashBurst
            // Sprinkler <- Sprinkler
            Set(AttackClass.Sprinkler, TargetClass.BubblerShield, 0.6f);
            Set(AttackClass.Sprinkler, TargetClass.BubblerWeakPoint, 0.6f);
            Set(AttackClass.Sprinkler, TargetClass.RainmakerBarrier, 0.6f);
            Set(AttackClass.Sprinkler, TargetClass.Torpedo, 0.5f);
            // SplashWallContact <- Shield
            Set(AttackClass.SplashWallContact, TargetClass.SplashWall, 0f);
            Set(AttackClass.SplashWallContact, TargetClass.BrellaCanopy, 0f);
            Set(AttackClass.SplashWallContact, TargetClass.BubblerShield, 0.2f);
            Set(AttackClass.SplashWallContact, TargetClass.BubblerWeakPoint, 0.2f);
            Set(AttackClass.SplashWallContact, TargetClass.BooyahArmor, 3.75f);
            Set(AttackClass.SplashWallContact, TargetClass.RainmakerBarrier, 0.2f);
            // Special <- Default
            // Inkstrike <- TripleTornado
            Set(AttackClass.Inkstrike, TargetClass.SplashWall, 2f);
            Set(AttackClass.Inkstrike, TargetClass.Sprinkler, 2f);
            Set(AttackClass.Inkstrike, TargetClass.Beakon, 2f);
            Set(AttackClass.Inkstrike, TargetClass.BrellaCanopy, 2f);
            Set(AttackClass.Inkstrike, TargetClass.CrabTank, 3f);
            Set(AttackClass.Inkstrike, TargetClass.BooyahArmor, 2f);
            Set(AttackClass.Inkstrike, TargetClass.RainmakerBarrier, 2f);
            Set(AttackClass.Inkstrike, TargetClass.Torpedo, 2f);
            Set(AttackClass.Inkstrike, TargetClass.WaveBreaker, 2f);
            // Trizooka <- UltraShot
            Set(AttackClass.Trizooka, TargetClass.SplashWall, 6f);
            Set(AttackClass.Trizooka, TargetClass.BrellaCanopy, 6f);
            Set(AttackClass.Trizooka, TargetClass.BubblerShield, 1.3f);
            Set(AttackClass.Trizooka, TargetClass.BubblerWeakPoint, 0.975f);
            Set(AttackClass.Trizooka, TargetClass.CrabTank, 1.8f);
            Set(AttackClass.Trizooka, TargetClass.BooyahArmor, 2f);
            Set(AttackClass.Trizooka, TargetClass.RainmakerBarrier, 2f);
            // InkStorm <- InkStormRain
            Set(AttackClass.InkStorm, TargetClass.SplashWall, 0f);
            Set(AttackClass.InkStorm, TargetClass.Sprinkler, 0f);
            Set(AttackClass.InkStorm, TargetClass.Beakon, 0f);
            Set(AttackClass.InkStorm, TargetClass.BrellaCanopy, 0f);
            Set(AttackClass.InkStorm, TargetClass.BubblerShield, 0f);
            Set(AttackClass.InkStorm, TargetClass.BubblerWeakPoint, 0f);
            Set(AttackClass.InkStorm, TargetClass.CrabTank, 0f);
            Set(AttackClass.InkStorm, TargetClass.BooyahArmor, 0f);
            Set(AttackClass.InkStorm, TargetClass.RainmakerBarrier, 0f);
            Set(AttackClass.InkStorm, TargetClass.Torpedo, 0f);
            Set(AttackClass.InkStorm, TargetClass.WaveBreaker, 0f);
            Set(AttackClass.InkStorm, TargetClass.Device, 0f);
            // KillerWail <- MicroLaser
            Set(AttackClass.KillerWail, TargetClass.SplashWall, 2.4f);
            Set(AttackClass.KillerWail, TargetClass.BrellaCanopy, 2f);
            Set(AttackClass.KillerWail, TargetClass.BubblerShield, 2.4f);
            Set(AttackClass.KillerWail, TargetClass.BubblerWeakPoint, 2.4f);
            Set(AttackClass.KillerWail, TargetClass.CrabTank, 1.5f);
            Set(AttackClass.KillerWail, TargetClass.RainmakerBarrier, 2.4f);
            Set(AttackClass.KillerWail, TargetClass.WaveBreaker, 2.4f);
            // BooyahBomb <- NiceBall
            Set(AttackClass.BooyahBomb, TargetClass.SplashWall, 3f);
            Set(AttackClass.BooyahBomb, TargetClass.BrellaCanopy, 2.5f);
            Set(AttackClass.BooyahBomb, TargetClass.BubblerShield, 2f);
            Set(AttackClass.BooyahBomb, TargetClass.BubblerWeakPoint, 2f);
            Set(AttackClass.BooyahBomb, TargetClass.CrabTank, 4f);
            Set(AttackClass.BooyahBomb, TargetClass.BooyahArmor, 1.5f);
            Set(AttackClass.BooyahBomb, TargetClass.RainmakerBarrier, 3f);
            Set(AttackClass.BooyahBomb, TargetClass.WaveBreaker, 3f);
            // UltraStampSwing <- UltraStamp_Swing
            Set(AttackClass.UltraStampSwing, TargetClass.SplashWall, 20f);
            Set(AttackClass.UltraStampSwing, TargetClass.Sprinkler, 3f);
            Set(AttackClass.UltraStampSwing, TargetClass.Beakon, 3f);
            Set(AttackClass.UltraStampSwing, TargetClass.BrellaCanopy, 20f);
            Set(AttackClass.UltraStampSwing, TargetClass.BubblerShield, 1.5f);
            Set(AttackClass.UltraStampSwing, TargetClass.BubblerWeakPoint, 1.5f);
            Set(AttackClass.UltraStampSwing, TargetClass.CrabTank, 4.5f);
            Set(AttackClass.UltraStampSwing, TargetClass.BooyahArmor, 1.8f);
            Set(AttackClass.UltraStampSwing, TargetClass.RainmakerBarrier, 3f);
            Set(AttackClass.UltraStampSwing, TargetClass.WaveBreaker, 20f);
            // UltraStampThrow <- UltraStamp_Throw_BombCore
            Set(AttackClass.UltraStampThrow, TargetClass.SplashWall, 2.182f);
            Set(AttackClass.UltraStampThrow, TargetClass.Sprinkler, 1.636f);
            Set(AttackClass.UltraStampThrow, TargetClass.Beakon, 1.636f);
            Set(AttackClass.UltraStampThrow, TargetClass.BrellaCanopy, 4.091f);
            Set(AttackClass.UltraStampThrow, TargetClass.BubblerShield, 1.909f);
            Set(AttackClass.UltraStampThrow, TargetClass.BubblerWeakPoint, 1.4318f);
            Set(AttackClass.UltraStampThrow, TargetClass.CrabTank, 3.273f);
            Set(AttackClass.UltraStampThrow, TargetClass.BooyahArmor, 1.364f);
            Set(AttackClass.UltraStampThrow, TargetClass.RainmakerBarrier, 1.909f);
            Set(AttackClass.UltraStampThrow, TargetClass.Torpedo, 0.545f);
            Set(AttackClass.UltraStampThrow, TargetClass.WaveBreaker, 2.182f);
            // Reefslider <- Skewer
            Set(AttackClass.Reefslider, TargetClass.SplashWall, 5f);
            Set(AttackClass.Reefslider, TargetClass.Sprinkler, 5f);
            Set(AttackClass.Reefslider, TargetClass.Beakon, 5f);
            Set(AttackClass.Reefslider, TargetClass.BrellaCanopy, 9.5f);
            Set(AttackClass.Reefslider, TargetClass.BubblerShield, 5f);
            Set(AttackClass.Reefslider, TargetClass.BubblerWeakPoint, 5f);
            Set(AttackClass.Reefslider, TargetClass.CrabTank, 7.28f);
            Set(AttackClass.Reefslider, TargetClass.BooyahArmor, 7.5f);
            Set(AttackClass.Reefslider, TargetClass.RainmakerBarrier, 5f);
            Set(AttackClass.Reefslider, TargetClass.WaveBreaker, 5f);
            // TentaMissiles <- MultiMissile_BombCore
            Set(AttackClass.TentaMissiles, TargetClass.SplashWall, 2f);
            Set(AttackClass.TentaMissiles, TargetClass.Sprinkler, 2f);
            Set(AttackClass.TentaMissiles, TargetClass.Beakon, 2f);
            Set(AttackClass.TentaMissiles, TargetClass.BrellaCanopy, 2f);
            Set(AttackClass.TentaMissiles, TargetClass.BubblerShield, 0.5f);
            Set(AttackClass.TentaMissiles, TargetClass.BubblerWeakPoint, 0.375f);
            Set(AttackClass.TentaMissiles, TargetClass.CrabTank, 1.5f);
            Set(AttackClass.TentaMissiles, TargetClass.BooyahArmor, 1.5f);
            Set(AttackClass.TentaMissiles, TargetClass.Torpedo, 0.5f);
            Set(AttackClass.TentaMissiles, TargetClass.WaveBreaker, 2f);
            // CrabTankCannon <- Chariot_Cannon
            Set(AttackClass.CrabTankCannon, TargetClass.SplashWall, 3f);
            Set(AttackClass.CrabTankCannon, TargetClass.Sprinkler, 3f);
            Set(AttackClass.CrabTankCannon, TargetClass.Beakon, 3f);
            Set(AttackClass.CrabTankCannon, TargetClass.BrellaCanopy, 3f);
            Set(AttackClass.CrabTankCannon, TargetClass.BubblerShield, 3f);
            Set(AttackClass.CrabTankCannon, TargetClass.BubblerWeakPoint, 1.5f);
            Set(AttackClass.CrabTankCannon, TargetClass.CrabTank, 1.5f);
            Set(AttackClass.CrabTankCannon, TargetClass.BooyahArmor, 2f);
            Set(AttackClass.CrabTankCannon, TargetClass.RainmakerBarrier, 3f);
            Set(AttackClass.CrabTankCannon, TargetClass.WaveBreaker, 3f);
            // CrabTankBody <- Chariot_Body
            Set(AttackClass.CrabTankBody, TargetClass.SplashWall, 100f);
            Set(AttackClass.CrabTankBody, TargetClass.Sprinkler, 100f);
            Set(AttackClass.CrabTankBody, TargetClass.Beakon, 100f);
            Set(AttackClass.CrabTankBody, TargetClass.BrellaCanopy, 0f);
            Set(AttackClass.CrabTankBody, TargetClass.BubblerShield, 0f);
            Set(AttackClass.CrabTankBody, TargetClass.Torpedo, 0f);
            // Inkjet <- Jetpack_BombCore
            Set(AttackClass.Inkjet, TargetClass.SplashWall, 2.8f);
            Set(AttackClass.Inkjet, TargetClass.Sprinkler, 2.8f);
            Set(AttackClass.Inkjet, TargetClass.Beakon, 2.8f);
            Set(AttackClass.Inkjet, TargetClass.BrellaCanopy, 2.5f);
            Set(AttackClass.Inkjet, TargetClass.BubblerShield, 1.82f);
            Set(AttackClass.Inkjet, TargetClass.BubblerWeakPoint, 1.365f);
            Set(AttackClass.Inkjet, TargetClass.CrabTank, 3.5f);
            Set(AttackClass.Inkjet, TargetClass.BooyahArmor, 2f);
            Set(AttackClass.Inkjet, TargetClass.RainmakerBarrier, 2.8f);
            Set(AttackClass.Inkjet, TargetClass.Torpedo, 0.5f);
            Set(AttackClass.Inkjet, TargetClass.WaveBreaker, 2.8f);
            // InkVac <- BlowerExhale_BombCore
            Set(AttackClass.InkVac, TargetClass.SplashWall, 5.6f);
            Set(AttackClass.InkVac, TargetClass.Sprinkler, 5.6f);
            Set(AttackClass.InkVac, TargetClass.Beakon, 5.6f);
            Set(AttackClass.InkVac, TargetClass.BrellaCanopy, 5f);
            Set(AttackClass.InkVac, TargetClass.BubblerShield, 4.2f);
            Set(AttackClass.InkVac, TargetClass.BubblerWeakPoint, 2.1f);
            Set(AttackClass.InkVac, TargetClass.CrabTank, 2.8f);
            Set(AttackClass.InkVac, TargetClass.BooyahArmor, 4f);
            Set(AttackClass.InkVac, TargetClass.RainmakerBarrier, 5.6f);
            Set(AttackClass.InkVac, TargetClass.WaveBreaker, 5.6f);
            // WaveBreaker <- ShockSonar_Wave
            Set(AttackClass.WaveBreaker, TargetClass.SplashWall, 100f);
            Set(AttackClass.WaveBreaker, TargetClass.Sprinkler, 100f);
            Set(AttackClass.WaveBreaker, TargetClass.Beakon, 100f);
            Set(AttackClass.WaveBreaker, TargetClass.BubblerShield, 8f);
            Set(AttackClass.WaveBreaker, TargetClass.BubblerWeakPoint, 8f);
            Set(AttackClass.WaveBreaker, TargetClass.CrabTank, 2f);
            Set(AttackClass.WaveBreaker, TargetClass.RainmakerBarrier, 8f);
            Set(AttackClass.WaveBreaker, TargetClass.WaveBreaker, 2f);
            // Zipcaster <- SuperHook
            Set(AttackClass.Zipcaster, TargetClass.SplashWall, 2f);
            Set(AttackClass.Zipcaster, TargetClass.Sprinkler, 2f);
            Set(AttackClass.Zipcaster, TargetClass.Beakon, 2f);
            Set(AttackClass.Zipcaster, TargetClass.BrellaCanopy, 2f);
            Set(AttackClass.Zipcaster, TargetClass.BubblerShield, 2f);
            Set(AttackClass.Zipcaster, TargetClass.BubblerWeakPoint, 2f);
            Set(AttackClass.Zipcaster, TargetClass.BooyahArmor, 1.5f);
            Set(AttackClass.Zipcaster, TargetClass.RainmakerBarrier, 2f);
            Set(AttackClass.Zipcaster, TargetClass.Torpedo, 0.6f);
            // Kraken <- Castle
            Set(AttackClass.Kraken, TargetClass.SplashWall, 7.5f);
            Set(AttackClass.Kraken, TargetClass.Sprinkler, 100f);
            Set(AttackClass.Kraken, TargetClass.Beakon, 100f);
            Set(AttackClass.Kraken, TargetClass.BrellaCanopy, 5f);
            Set(AttackClass.Kraken, TargetClass.BubblerShield, 2.5f);
            Set(AttackClass.Kraken, TargetClass.BubblerWeakPoint, 2.5f);
            Set(AttackClass.Kraken, TargetClass.CrabTank, 4f);
            Set(AttackClass.Kraken, TargetClass.BooyahArmor, 5f);
            Set(AttackClass.Kraken, TargetClass.RainmakerBarrier, 5f);
            Set(AttackClass.Kraken, TargetClass.WaveBreaker, 4f);
            // TripleSplashdown <- Pogo
            Set(AttackClass.TripleSplashdown, TargetClass.SplashWall, 2.5f);
            Set(AttackClass.TripleSplashdown, TargetClass.Sprinkler, 5f);
            Set(AttackClass.TripleSplashdown, TargetClass.Beakon, 5f);
            Set(AttackClass.TripleSplashdown, TargetClass.BrellaCanopy, 4.75f);
            Set(AttackClass.TripleSplashdown, TargetClass.BubblerShield, 2.5f);
            Set(AttackClass.TripleSplashdown, TargetClass.BubblerWeakPoint, 2.5f);
            Set(AttackClass.TripleSplashdown, TargetClass.CrabTank, 3.64f);
            Set(AttackClass.TripleSplashdown, TargetClass.BooyahArmor, 3.75f);
            Set(AttackClass.TripleSplashdown, TargetClass.RainmakerBarrier, 2.5f);
            Set(AttackClass.TripleSplashdown, TargetClass.Torpedo, 0.5f);
            Set(AttackClass.TripleSplashdown, TargetClass.WaveBreaker, 2.5f);

            float[] table = _building;
            _building = null;
            return table;
        }
    }
}

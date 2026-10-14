using SplatoonC.Core.Combat;

namespace SplatoonC.Gameplay.Debugging
{
    // Splat Charger:60F 滿蓄 160 貫穿、部分 40 → 80、含槍口 10.75 → 25.75 m、2.4 → 4.8 u/F、2.25% → 18%、走速 0.02 u/F。
    // 量測流程見 ChargerAutoTest(外觀 / bot 掛勾 / 滿蓄 / 點一下 / 假人 / 貫穿 / 起跳 / 墨不足 / 儲存蓄力 / 潛墨限制 /
    // 雷射撞牆 / 物件倍率 / bot 式 / 拉近與潛墨)。
    [AutoTest(Scene = AutoTestAttribute.TrainingScene, Timeout = 200f, Order = 240)]
    public sealed class SplatChargerAutoTest : ChargerAutoTest
    {
        public static void Run()
        {
            Launch<SplatChargerAutoTest>();
        }

        protected override string ConfigId => "splat_charger";

        protected override S3ChargerParams S3 => S3ChargerParams.SplatCharger;
    }
}

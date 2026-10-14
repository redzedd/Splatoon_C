using SplatoonC.Core.Combat;

namespace SplatoonC.Gameplay.Debugging
{
    // E-liter 4K:92F 滿蓄 180 貫穿、部分 40 → 80、含槍口 10.75 → 30.76 m、2.4 → 6.4 u/F(滿蓄 9.6)、2.25% → 25%、
    // 走速 0.015 u/F、重量級。量測流程見 ChargerAutoTest。
    [AutoTest(Scene = AutoTestAttribute.TrainingScene, Timeout = 220f, Order = 241)]
    public sealed class ELiter4KAutoTest : ChargerAutoTest
    {
        public static void Run()
        {
            Launch<ELiter4KAutoTest>();
        }

        protected override string ConfigId => "e_liter_4k";

        protected override S3ChargerParams S3 => S3ChargerParams.ELiter4K;
    }
}

using SplatoonC.Core.Combat;

namespace SplatoonC.Gameplay.Debugging
{
    // Carbon Roller(輕量級):舉起只要 10F、推滾 70 + 擊退、20F 就衝刺 9.6 m/s、3.96%。
    // 量測流程見 RollerAutoTest(外觀 / bot 掛勾 / 橫甩 / 直甩 / 甩動走速 / 推滾 / 接觸 / 物件倍率 / 弱甩 / bot 式 / 潛墨)。
    [AutoTest(Scene = AutoTestAttribute.TrainingScene, Timeout = 180f, Order = 221)]
    public sealed class CarbonRollerAutoTest : RollerAutoTest
    {
        public static void Run()
        {
            Launch<CarbonRollerAutoTest>();
        }

        protected override string ConfigId => "carbon_roller";

        protected override S3RollerParams S3 => S3RollerParams.CarbonRoller;
    }
}

using SplatoonC.Core.Combat;

namespace SplatoonC.Gameplay.Debugging
{
    // Splat Roller:推滾 125 一擊、橫甩 7.2 m 內一擊(150)、12+1 顆 ±18°、8.5% / 43F。
    // 量測流程見 RollerAutoTest(外觀 / bot 掛勾 / 橫甩 / 直甩 / 甩動走速 / 推滾 / 接觸 / 物件倍率 / 弱甩 / bot 式 / 潛墨)。
    [AutoTest(Scene = AutoTestAttribute.TrainingScene, Timeout = 180f, Order = 220)]
    public sealed class SplatRollerAutoTest : RollerAutoTest
    {
        public static void Run()
        {
            Launch<SplatRollerAutoTest>();
        }

        protected override string ConfigId => "splat_roller";

        protected override S3RollerParams S3 => S3RollerParams.SplatRoller;
    }
}

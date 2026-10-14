using SplatoonC.Core.Combat;

namespace SplatoonC.Gameplay.Debugging
{
    // Dynamo Roller(重量級):舉起 45F、橫甩 12.9 m 內一擊(180)、16+2 顆、21% / 55F、推滾 5.28 → 6.48 m/s。
    // 量測流程見 RollerAutoTest(外觀 / bot 掛勾 / 橫甩 / 直甩 / 甩動走速 / 推滾 / 接觸 / 物件倍率 / 弱甩 / bot 式 / 潛墨)。
    [AutoTest(Scene = AutoTestAttribute.TrainingScene, Timeout = 180f, Order = 222)]
    public sealed class DynamoRollerAutoTest : RollerAutoTest
    {
        public static void Run()
        {
            Launch<DynamoRollerAutoTest>();
        }

        protected override string ConfigId => "dynamo_roller";

        protected override S3RollerParams S3 => S3RollerParams.DynamoRoller;
    }
}

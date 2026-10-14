using SplatoonC.Core.Combat;

namespace SplatoonC.Gameplay.Debugging
{
    // Splattershot Jr.:28 傷四發擊倒、5F 連射、110% 墨槽(一槽 255 發)、15F 墨量鎖、輕量級。
    // 量測流程見 ShooterVariantAutoTest(射速 / 散布 / TTK / 有效射程 / 走速 / 塗地 / 耗墨 / 墨量鎖 / 外觀 / bot)。
    [AutoTest(Scene = AutoTestAttribute.TrainingScene, Timeout = 150f, Order = 210)]
    public sealed class SplattershotJrAutoTest : ShooterVariantAutoTest
    {
        public static void Run()
        {
            Launch<SplattershotJrAutoTest>();
        }

        protected override string ConfigId => "splattershot_jr";

        protected override S3ShooterParams S3 => S3ShooterParams.SplattershotJr;
    }
}

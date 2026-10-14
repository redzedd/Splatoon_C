using SplatoonC.Core.Combat;

namespace SplatoonC.Gameplay.Debugging
{
    // Splattershot Pro:45 傷三發擊倒(TTK 16F)、有效射程 15.09、2% 墨(一槽 50 發)、沒有腳前墨滴。
    // 量測流程見 ShooterVariantAutoTest(射速 / 散布 / TTK / 有效射程 / 走速 / 塗地 / 耗墨 / 墨量鎖 / 外觀 / bot)。
    [AutoTest(Scene = AutoTestAttribute.TrainingScene, Timeout = 150f, Order = 213)]
    public sealed class SplattershotProAutoTest : ShooterVariantAutoTest
    {
        public static void Run()
        {
            Launch<SplattershotProAutoTest>();
        }

        protected override string ConfigId => "splattershot_pro";

        protected override S3ShooterParams S3 => S3ShooterParams.SplattershotPro;
    }
}

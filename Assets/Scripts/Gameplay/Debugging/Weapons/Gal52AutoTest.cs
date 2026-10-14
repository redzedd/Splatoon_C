using SplatoonC.Core.Combat;

namespace SplatoonC.Gameplay.Debugging
{
    // .52 Gal:52 傷兩發擊倒(TTK 9F)、9F 射速、1.5% 墨、大口徑彈。
    // 量測流程見 ShooterVariantAutoTest(射速 / 散布 / TTK / 有效射程 / 走速 / 塗地 / 耗墨 / 墨量鎖 / 外觀 / bot)。
    [AutoTest(Scene = AutoTestAttribute.TrainingScene, Timeout = 150f, Order = 211)]
    public sealed class Gal52AutoTest : ShooterVariantAutoTest
    {
        public static void Run()
        {
            Launch<Gal52AutoTest>();
        }

        protected override string ConfigId => "52_gal";

        protected override S3ShooterParams S3 => S3ShooterParams.Gal52;
    }
}

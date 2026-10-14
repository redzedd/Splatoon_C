using SplatoonC.Core.Combat;

namespace SplatoonC.Gameplay.Debugging
{
    // N-ZAP '85:30 傷四發擊倒、5F 連射、0.8% 墨、開火走速 5.28、輕量級。
    // 量測流程見 ShooterVariantAutoTest(射速 / 散布 / TTK / 有效射程 / 走速 / 塗地 / 耗墨 / 墨量鎖 / 外觀 / bot)。
    [AutoTest(Scene = AutoTestAttribute.TrainingScene, Timeout = 150f, Order = 212)]
    public sealed class NZap85AutoTest : ShooterVariantAutoTest
    {
        public static void Run()
        {
            Launch<NZap85AutoTest>();
        }

        protected override string ConfigId => "n_zap_85";

        protected override S3ShooterParams S3 => S3ShooterParams.NZap85;
    }
}

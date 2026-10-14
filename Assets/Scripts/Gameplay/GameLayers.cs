namespace SplatoonC.Gameplay
{
    // 專案圖層槽(TagManager):設定碼與執行期遮罩共用,避免各處散落字面值。
    public static class GameLayers
    {
        public const int Default = 0;
        public const int Target = 6;
        // 所有角色(玩家與 bot)
        public const int Inkling = 8;

        public const int InklingMask = 1 << Inkling;
    }
}

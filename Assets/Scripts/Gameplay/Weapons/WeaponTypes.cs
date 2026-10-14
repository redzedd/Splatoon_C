namespace SplatoonC.Gameplay.Combat
{
    // Splatoon 3 主武器種類(決定操作方式與 bot 行為)
    public enum WeaponClass
    {
        Shooter = 0,
        Roller = 1,
        Charger = 2,
        Slosher = 3,
        Splatling = 4,
        Dualies = 5,
        Brella = 6,
        Blaster = 7,
        Brush = 8,
        Stringer = 9,
        Splatana = 10,
    }

    // 重量級別:影響跑速與游速(S3:輕 +8.3% 跑 / +5% 游;重 −8.3% 跑 / −10% 游)
    public enum WeightClass
    {
        Light = 0,
        Mid = 1,
        Heavy = 2,
    }

    // 副武器的出手方式
    public enum ThrowStyle
    {
        // 按住顯示拋物線、放開丟出(炸彈、灑水器、信標…)
        Arc = 0,
        // 直線射出(Angle Shooter)
        Straight = 1,
        // 放在腳邊(Ink Mine)
        PlaceAtFeet = 2,
        // 按住蓄力、放開從腰部低拋後貼地滑行(Curling Bomb):出手與預覽由設定的 TryComputeLaunch / BuildPreview 決定
        GroundRoll = 3,
    }

    public static class WeightClassUtil
    {
        public static float RunMultiplier(WeightClass weight)
        {
            switch (weight)
            {
                case WeightClass.Light: return 1.083f;
                case WeightClass.Heavy: return 0.917f;
                default: return 1f;
            }
        }

        public static float SwimMultiplier(WeightClass weight)
        {
            switch (weight)
            {
                case WeightClass.Light: return 1.05f;
                case WeightClass.Heavy: return 0.9f;
                default: return 1f;
            }
        }
    }
}

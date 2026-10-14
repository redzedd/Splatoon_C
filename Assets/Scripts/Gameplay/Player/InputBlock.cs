namespace SplatoonC.Gameplay.Player
{
    // PlayerInputRouter.ActionBlock 的意圖頻道(A4 超級跳:蓄力/飛行中鎖住,落地前開放開火;目標選單開著時全鎖)
    [System.Flags]
    public enum InputBlock
    {
        None = 0,
        Move = 1 << 0,
        Jump = 1 << 1,
        Attack = 1 << 2,
        Sub = 1 << 3,
        Special = 1 << 4,
        // 除了視角與烏賊以外的全部行動
        Actions = Move | Jump | Attack | Sub | Special,
    }
}

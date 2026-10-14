using SplatoonC.Core.Match.Ranked;
using SplatoonC.Gameplay.AI;
using SplatoonC.Gameplay.Combat;
using UnityEngine;

namespace SplatoonC.Gameplay.Match
{
    // 從主選單啟動的方式(回到主選單時決定落在哪一頁)
    public enum LaunchKind
    {
        None = 0,
        Battle = 1,
        Training = 2,
    }

    // 主選單選好的「下一場」設定(跨場景的靜態狀態,domain reload 即重設)。
    // 預設值 = 場景自己的設定:直接開對戰/試射場景(AutoTest、編輯器 Play)時完全不受影響。
    // MatchController 讀本機武器、隊伍人數、色彩鎖定;bot 難度直接寫進 BotDirector.DifficultyOverride。
    public static class MatchSettings
    {
        public const int MinTeamSize = 1;
        public const int MaxTeamSize = 4;
        public const string TurfWarModeId = MatchModes.TurfWarId;

        // 本機玩家的武器組合(null = Player.prefab 的預設)
        public static WeaponKit LocalKit { get; set; }

        // 選單載入的場景名稱(對戰場地或試射場)
        public static string StageScene { get; set; }

        // 對戰模式 id(選單寫入;MatchModes.FromId 對照列舉)
        public static string ModeId { get; set; } = TurfWarModeId;

        // 對戰模式(ModeId 的列舉形式;MatchController 開賽時讀)
        public static MatchMode Mode
        {
            get => MatchModes.FromId(ModeId);
            set => ModeId = MatchModes.IdOf(value);
        }

        // 每隊人數(0 = 場景設定;S3 固定 4 對 4,離線版可選 1~4)
        public static int TeamSize { get; set; }

        // S3「色彩鎖定」選項:固定黃 vs 藍紫配色(設定頁寫入)
        public static bool ColorLock { get; set; }

        // bot 隨機配武器組合(兩隊種類對稱,BotDirector.AssignRandomKits)。主選單開賽時打開;
        // 預設關 = bot 用 Player.prefab 的預設組合(直接開場景的 AutoTest 不受影響)
        public static bool RandomBotKits { get; set; }

        public static LaunchKind LastLaunch { get; set; }

        // bot 難度(null = BotDirector 的 Inspector 值,我方/敵方可不同)
        public static BotBrain.Difficulty? BotDifficulty
        {
            get => BotDirector.DifficultyOverride;
            set => BotDirector.DifficultyOverride = value;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            LocalKit = null;
            StageScene = null;
            ModeId = TurfWarModeId;
            TeamSize = 0;
            ColorLock = false;
            RandomBotKits = false;
            LastLaunch = LaunchKind.None;
            BotDirector.DifficultyOverride = null;
        }

        // 場景預設人數 → 實際人數
        public static int ResolveTeamSize(int sceneDefault)
        {
            return TeamSize > 0 ? Mathf.Clamp(TeamSize, MinTeamSize, MaxTeamSize) : sceneDefault;
        }
    }
}

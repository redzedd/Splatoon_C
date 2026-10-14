namespace SplatoonC.Core.Match.Ranked
{
    // 蠻頹對戰模式的純規則介面(DESIGN_REFERENCE §6.9):執行期以 60 Hz 固定步 Step() 推進,
    // 時間到的那一刻問 IsOvertimeTriggered(),進入加時後每步問 IsOvertimeOver()。
    // 各模式(區域 / 塔樓 / 魚虎 / 蛤蜊)各自實作;世界狀態(墨量、塔位置…)由執行期在 Step 前寫入。
    public interface IObjectiveMode
    {
        MatchMode Mode { get; }

        TeamScore Alpha { get; }

        TeamScore Bravo { get; }

        // 已推進的幀數(60 Hz)
        int Frame { get; }

        // 目前持有目標的隊伍(區域:控制全部區域的隊伍;沒有 = None)——加時警報、HUD 用
        Team ObjectiveHolder { get; }

        // 計數歸 0 的隊伍(KNOCKOUT;沒有 = None)
        Team KnockoutTeam { get; }

        bool InOvertime { get; }

        // 加時框的剩餘寬限(1 = 落後方正持有目標、0 = 寬限用完);沒有加時 = 0
        float OvertimeGrace01 { get; }

        // 加時開始時被判定為落後的隊伍(加時框用它的顏色)
        Team OvertimeLoser { get; }

        void Step();

        // 時間到的那一刻評估:是否進入加時
        bool IsOvertimeTriggered();

        // 進入加時(IsOvertimeTriggered 為真後由執行期呼叫)
        void BeginOvertime();

        // 加時中每步評估:是否結束
        bool IsOvertimeOver();

        // 被動大招充能速率(p/s,§6.4 表)
        float PassiveChargeRate(Team team);

        AnarchyResult Resolve();
    }
}

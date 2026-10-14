using SplatoonC.Core.Match.Ranked;
using UnityEngine;

namespace SplatoonC.Gameplay.Match
{
    // 一個蠻頹模式的執行期驅動(純規則 IObjectiveMode ↔ 場上物件):ObjectiveModeController 依模式建立,
    // 開賽時 Build 生成目標物件,對戰中每幀 BeforeStep 把世界狀態(墨量、塔位置…)寫進規則,再由控制器以 60 Hz 推進規則。
    // 新模式(塔樓 / 魚虎 / 蛤蜊)實作這個介面並在 ObjectiveModeController.CreateDriver 補一筆。
    public interface IObjectiveDriver
    {
        IObjectiveMode Rules { get; }

        // 生成目標物件(root 之下);回傳 false = 場地資料不足,退回塗地
        bool Build(ModeLayout layout, Transform root, ObjectiveAssets assets);

        // 每幀、推進規則之前:世界 → 規則
        void BeforeStep();

        // 每幀、推進規則之後:外觀
        void AfterStep();

        void Dispose();
    }
}

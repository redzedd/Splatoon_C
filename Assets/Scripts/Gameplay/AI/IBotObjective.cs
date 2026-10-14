using SplatoonC.Core;
using UnityEngine;

namespace SplatoonC.Gameplay.AI
{
    // 蠻頹對戰的 bot 目標建議(由模式執行期實作並掛到 BotDirector.Objective):
    // bot 要挑塗地目標時先問這裡;回傳 TurfMap 取樣點索引,-1 = 交回一般塗地邏輯。
    // 不可回傳「已經是本隊墨」的取樣點(BotBrain 會當作目標已完成而立刻重挑)。
    public interface IBotObjective
    {
        int PickPaintTarget(BotBrain requester, Team team, Vector3 position, TurfMap map);
    }
}

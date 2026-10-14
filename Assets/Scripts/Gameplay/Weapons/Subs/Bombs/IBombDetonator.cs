using SplatoonC.Core;

namespace SplatoonC.Gameplay.Combat
{
    // 炸彈碰到就立刻爆炸的物件(S3 §4.3:敵方 Splash Wall、傘面、Big Bubbler、Rainmaker 屏障、變身中的 Torpedo)。
    // 放在碰撞體所在物件或其父物件上。沒有實作此介面的場上裝置(DeployableObject)預設規則:
    // BlocksAttacksFrom(炸彈隊伍) 為真(擋得住這隊攻擊的屏障)就會引爆——見 BombRules.Detonates。
    public interface IBombDetonator
    {
        bool DetonatesBombsFrom(Team bombTeam);
    }
}

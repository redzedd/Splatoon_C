using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // bot 對武器模組的控制介面:武器/副武器/大招模組各自描述「bot 該怎麼用我」,BotBrain 只提供這組操作與情報,
    // 不需要認識每一種武器。實作者:SplatoonC.Gameplay.AI.BotBrain。
    public interface IBotControl
    {
        // 情報
        Vector3 Position { get; }
        bool HasTarget { get; }
        bool TargetVisible { get; }
        Vector3 TargetPosition { get; }
        Vector3 TargetVelocity { get; }
        float TargetDistance { get; }
        // 往敵方基地的水平方向(沒有目標時的大招/投擲方向)
        Vector3 EnemyBaseDirection { get; }
        // 目前視線與「想看的方向」差幾度
        float AimError { get; }
        float InkRatio { get; }

        // 操作(每幀重設;模組在自己的 Bot* 方法裡設定)
        void LookAt(Vector3 worldPoint);
        void HoldAttack(bool held);
        void HoldSub(bool held);
        void PressSpecial();
        // 世界方向移動(magnitude 0~1)
        void Move(Vector3 worldDirection, float magnitude);
        // 按住烏賊鍵(Crab Tank 的滾球模式 = ZL)/ 這一幀按跳躍(滾球小跳、Ultra Stamp 空中揮擊)
        void HoldSquid(bool held);
        void PressJump();
    }

    // 交戰時主武器決定是否按住攻擊的情報
    public struct BotCombatContext
    {
        public float Distance;
        public bool TargetVisible;
        // 看到目標後的反應時間是否已過
        public bool Reacted;
        public float AimError;
        public float FireTolerance;
    }

    // 交戰時主武器決定視線對準點的情報(MainWeapon.BotAimPoint)
    public struct BotAimContext
    {
        // bot 的視點(BotAimView.AimOrigin,槍口高度)
        public Vector3 Eye;
        // BotBrain 的預設瞄準點:看得到 = 目標胸口(腳底 + 0.9 m)+ 移動提前量;看不到 = 最後看見的位置 + 0.9 m
        public Vector3 AimPoint;
        // 目標腳底(同上,含提前量)
        public Vector3 TargetFeet;
        // 目標目前的速度(m/s;看不到 = 0):出膛前有預備時間的武器(爆破槍 14F)自己再補提前量
        public Vector3 TargetVelocity;
        public bool TargetVisible;
        // bot 到目標的距離(公尺)
        public float Distance;
        // 距離最後一次看見目標的秒數(看得到 = 0)
        public float SecondsSinceSeen;
    }

    // 副武器的 bot 用法
    public enum BotSubUsage
    {
        // 丟向交戰中的敵人(炸彈類)
        ThrowAtEnemy = 0,
        // 放在自己附近/前方(灑水器、護盾、信標)
        PlaceNearby = 1,
        // 丟到前方塗地(塗地性強的炸彈在沒敵人時也用)
        ThrowToPaint = 2,
    }
}

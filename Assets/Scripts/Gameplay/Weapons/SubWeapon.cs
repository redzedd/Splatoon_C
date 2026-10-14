using SplatoonC.Core;
using SplatoonC.Gameplay.Player;
using Unity.Collections;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 副武器設定基底:投擲參數(ThrowableConfig)+ 墨量消耗 + 投擲物 prefab。
    // 每種副武器(斯普拉炸彈、吸盤炸彈、灑水器…)一個子類與一個 SubProjectile 行為。
    // 可選掛勾(預設 = 一般拋物線炸彈):蓄力(MaxChargeFrames)、自訂出手/預覽(TryComputeLaunch / BuildPreview)、
    // 預覽碰撞半徑(PreviewRadius)、依蓄力的墨量鎖、蓄力連打加速(ChargeMash*)、達上限不能丟(BlockWhileMaxActive)、
    // bot 按住秒數、手持模型(HeldModel)。
    public abstract class SubWeaponConfig : ThrowableConfig
    {
        [Header("副武器")]
        [SerializeField, Tooltip("顯示名稱")]
        protected string _displayName = "副武器";

        [SerializeField, Range(0f, 1.1f), Tooltip("墨量消耗(0~1;S3 斯普拉炸彈 0.70)")]
        protected float _inkCost = 0.7f;

        [SerializeField, Tooltip("投擲物 prefab(根物件需有對應的 SubProjectile 元件)")]
        protected SubProjectile _prefab;

        [SerializeField, Tooltip("同時存在上限(0 = 不限;灑水器/護盾 1、信標 3…);超過時最舊的消失")]
        protected int _maxActive;

        [SerializeField, Tooltip("投擲/放置音效 id")]
        protected string _throwSound = "bomb_throw";

        [SerializeField, Tooltip("投擲姿勢時左手拿著的模型 prefab(CharacterVisual 換掉預設示意球;留空 = 示意球)。"
                                 + "名稱以 Ink 開頭的子網格用角色的隊伍色材質(TankInk)")]
        protected GameObject _heldModel;

        [Header("bot 用法")]
        [SerializeField, Tooltip("bot 什麼時候用這個副武器")]
        protected BotSubUsage _botUsage = BotSubUsage.ThrowAtEnemy;

        [SerializeField, Tooltip("bot 使用距離下限/上限(公尺)")]
        protected Vector2 _botRange = new Vector2(5f, 14f);

        public string DisplayName => _displayName;
        public float InkCost => _inkCost;
        public SubProjectile Prefab => _prefab;
        public int MaxActive => _maxActive;
        public string ThrowSound => _throwSound;
        public GameObject HeldModel => _heldModel;
        public BotSubUsage BotUsage => _botUsage;
        public Vector2 BotRange => _botRange;
        public virtual ThrowStyle Style => ThrowStyle.Arc;

        // ── 蓄力(預設沒有;Curling Bomb 60F、之後的 Fizzy Bomb)──

        // 蓄力上限(F);0 = 不蓄力
        public virtual int MaxChargeFrames => 0;

        // 按下後幾 F 才開始蓄力(炸彈出現在手上;S3 人形 5F)
        public virtual int ChargeWindupFrames => 0;

        // 蓄滿時播一次的音效 id(null = 無)
        public virtual string ChargedSound => null;

        // 放開時的墨量鎖(秒);charge01 = 蓄力比例。預設固定 InkLockSeconds(Curling:70F → 滿蓄 30F)
        public virtual float InkLockSecondsAt(float charge01)
        {
            return InkLockSeconds;
        }

        // 蓄力加速(Fizzy Bomb:按住副武器時每按一次跳躍折抵 8F、搖桿從中間甩出折抵 11F;
        // 折抵量每幀最多兌現 ChargeMashMaxPerFrame F)。預設 0 = 不支援連打加速
        public virtual float ChargeMashJumpFrames => 0f;
        public virtual float ChargeMashStickFrames => 0f;
        public virtual float ChargeMashMaxPerFrame => 0f;

        // ── 同時存在上限的規則 ──

        // true = 達到 MaxActive 時「不能再丟」(S3 Torpedo:自己的還在場上就顯示使用不可,不扣墨);
        // false(預設)= 丟新的、最舊的消失(Sprinkler、Beakon…)
        public virtual bool BlockWhileMaxActive => false;

        // ── 自訂出手與預覽(預設走 ThrowController 的拋物線:視線目標 + 上抬角 × ThrowSpeed)──

        // 預設拋物線預覽的碰撞半徑(公尺):> 0 時預覽改用同半徑的 sphere cast,落點 = 投擲物實際的接觸點
        // (S3 的軌跡導引也是 GuideRadius 的 sphere cast);0 = 中心線射線(預設,斯普拉炸彈沿用)
        public virtual float PreviewRadius => 0f;

        // origin:投擲原點(可改,例:Curling 從腰部低拋);aimDirection:ThrowController 算好的投擲方向(單位向量);
        // ownerVelocity:投擲者目前速度(m/s);velocity:出手初速(m/s)。回傳 true = 採用,預覽改走 BuildPreview。
        public virtual bool TryComputeLaunch(ref Vector3 origin, Vector3 aimDirection, Vector3 ownerVelocity,
            float charge01, out Vector3 velocity)
        {
            velocity = Vector3.zero;
            return false;
        }

        // 自訂預覽路徑(TryComputeLaunch 回傳 true 時由 ThrowController 呼叫):把世界座標點寫進 points,回傳點數;
        // hasLanding / landing / landingNormal = 落點標記。必須與投擲物實際行為用同一套步進(預覽 = 實際)。
        // team = 投擲者隊伍(預覽也要照「碰到敵方屏障就爆」停下)。
        public virtual int BuildPreview(Vector3 origin, Vector3 velocity, float charge01, Team team,
            NativeArray<Vector3> points, out bool hasLanding, out Vector3 landing, out Vector3 landingNormal)
        {
            hasLanding = false;
            landing = origin;
            landingNormal = Vector3.up;
            if (points.Length > 0)
            {
                points[0] = origin;
                return 1;
            }
            return 0;
        }

        // ── bot ──

        // bot 按住副武器幾秒再放開(預設 0.45;蓄力型依目標距離決定蓄多少)
        public virtual float BotHoldSeconds(float targetDistance)
        {
            return 0.45f;
        }

        // bot 投擲時視線要看的點:thrower = 投擲者腳下位置,target = 想丟到的點(目標腳下)。
        // 預設 = 目標腳下上方 0.3 m(專案校準的拋物線自己會補);走 S3 阻力彈道的副武器解出仰角(S3ThrownSubConfig)
        public virtual Vector3 BotAimPoint(Vector3 thrower, Vector3 target)
        {
            return target + Vector3.up * 0.3f;
        }

        // BotUsage = PlaceNearby 的副武器:bot 在塗地/移動中問「此刻要不要在這裡放」(預設:有墨就放)。
        // 裝置類覆寫成自己的條件(例:信標要離基地夠遠、附近沒有自己的信標)
        public virtual bool BotShouldPlace(Inkling self)
        {
            return true;
        }
    }

    // 副武器投擲物基底:由 ThrowController 從 SharedPool 取出並 Launch;結束時呼叫 Despawn 回池。
    public abstract class SubProjectile : MonoBehaviour
    {
        public Inkling Owner { get; protected set; }

        public abstract void Launch(Vector3 position, Vector3 velocity, SubWeaponConfig config, Inkling owner);

        // 蓄力型副武器出手(ThrowController 一律呼叫這個):charge01 = 蓄力比例 0~1。預設忽略蓄力、走一般 Launch
        public virtual void LaunchCharged(Vector3 position, Vector3 velocity, SubWeaponConfig config, Inkling owner,
            float charge01)
        {
            Launch(position, velocity, config, owner);
        }

        // 被新丟的同種副武器擠掉(同時存在上限)、或大招/規則要求移除:預設直接回池,子類可先播消失效果
        public virtual void Expire()
        {
            Despawn();
        }

        protected void Despawn()
        {
            Owner = null;
            SharedPool.Despawn(this);
        }
    }
}

using SplatoonC.Core.Combat;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 大招「Tacticooler」(S3 SpEnergyStand,research/04 §2.15、JP wiki エナジースタンド、DESIGN_REFERENCE §5.2;v11.3.0 0 AP):
    // 發動後手上舉著罐子(時間不限、主武器不能用、不能潛游),按住副武器鍵看拋物線、放開 → 13F 後丟出
    //(0.4 u/F、上 0.07 u/F、重力 0.009 u/F²,不繼承慣性)。著地坡度 ≤ 30° 就立起飲料機:打不壞、擋雙方的墨(掩體)、
    // 存在 900F;展開後隊友(含自己)以人形靠近(水平 7、往上 3)就各拿一罐(共 4 罐、一人一罐),
    // 得到 1020F 的增益(RSU/SSU 29 AP、Intensify/QSJ/QR/Special Saver/墨抗 57 AP,不與裝備疊加、死亡失效),飲用者發光。
    // 能量:展開後鎖定 600F(死亡不中斷),飲料機消失即歸 0。
    [CreateAssetMenu(fileName = "Special_tacticooler", menuName = "SplatoonC/大招/Tacticooler")]
    public sealed class TacticoolerConfig : SpecialWeaponConfig
    {
        [Header("預製物")]
        [SerializeField, Tooltip("飲料機 prefab(TacticoolerDevice;碰撞體在 Default 圖層 = 雙方都擋的掩體)")]
        private TacticoolerDevice _devicePrefab;

        [SerializeField, Tooltip("手上舉著的罐子模型(名稱 Ink* 的部位上隊伍色)")]
        private GameObject _heldPrefab;

        [SerializeField, Tooltip("飲用者身上的發光效果 prefab(TacticoolerAura;潛墨時也看得到)")]
        private TacticoolerAura _auraPrefab;

        [Header("時間(幀,60 Hz)")]
        [SerializeField, Tooltip("放開副武器鍵到罐子離手(投擲前隙 13F)")]
        private int _throwLagFrames = TacticoolerModel.ThrowLagFrames;

        [SerializeField, Tooltip("飲料機存在時間(PutFrame 900F = 15 s;塔上 450F)")]
        private int _standFrames = TacticoolerModel.StandFrames;

        [SerializeField, Tooltip("展開動畫(罐子打開、手臂伸出;展開完才拿得到)[EST 30F]")]
        private int _unfoldFrames = TacticoolerModel.UnfoldFrames;

        [SerializeField, Tooltip("能量鎖定(SpecialReduceFrame 600F,v5.0.0;展開起算)")]
        private int _lockFrames = TacticoolerModel.LockFrames;

        [SerializeField, Tooltip("增益持續(PowerUpFrame 1020F;SPU 最高 1500F)")]
        private int _buffFrames = TacticoolerModel.BuffFrames;

        [Header("飲料")]
        [SerializeField, Tooltip("罐數(4:一隊一人一罐)")]
        private int _drinks = TacticoolerModel.Drinks;

        [SerializeField, Tooltip("供應圓柱:水平半徑 / 往上高度(ServeAreaRadius 7、ServeAreaHeightUp 3;v3.1.0 起水平約 2 倍)")]
        private Vector2 _serveArea = new Vector2(TacticoolerModel.ServeRadius, TacticoolerModel.ServeHeightUp);

        [SerializeField, Tooltip("新的飲料機在這個距離內立起 → 舊的消失(VanishDistance 1.8)")]
        private float _vanishDistance = TacticoolerModel.VanishDistance;

        [Header("音效")]
        [SerializeField, Tooltip("舉起罐子")]
        private string _activateSound = "tacticooler_activate";

        [SerializeField, Tooltip("丟出")]
        private string _throwSound = "tacticooler_throw";

        [SerializeField, Tooltip("展開(S3:隊友與附近敵人聽得到的輕快音樂)")]
        private string _deploySound = "tacticooler_deploy";

        [SerializeField, Tooltip("拿到飲料")]
        private string _drinkSound = "tacticooler_drink";

        [SerializeField, Tooltip("飲料機消失")]
        private string _vanishSound = "tacticooler_vanish";

        public override System.Type BehaviourType => typeof(TacticoolerSpecial);

        // 舉著不限時(S3:能量在展開前一直是滿的),丟出後改走鎖定倒數
        public override float ActiveDuration => 0f;

        public TacticoolerDevice DevicePrefab => _devicePrefab;
        public GameObject HeldPrefab => _heldPrefab;
        public TacticoolerAura AuraPrefab => _auraPrefab;
        public int ThrowLagFrames => _throwLagFrames;
        public float StandSeconds => _standFrames / 60f;
        public float UnfoldSeconds => _unfoldFrames / 60f;
        public float LockSeconds => _lockFrames / 60f;
        public float BuffSeconds => _buffFrames / 60f;
        public int Drinks => _drinks;
        public float ServeRadius => _serveArea.x;
        public float ServeHeightUp => _serveArea.y;
        public float VanishDistance => _vanishDistance;
        public string ActivateSound => _activateSound;
        public string ThrowSound => _throwSound;
        public string DeploySound => _deploySound;
        public string DrinkSound => _drinkSound;
        public string VanishSound => _vanishSound;
    }
}

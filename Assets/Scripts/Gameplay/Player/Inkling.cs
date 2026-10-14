using System;
using System.Collections.Generic;
using SplatoonC.Core;
using SplatoonC.Gameplay.CameraRig;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Painting;
using UnityEngine;

namespace SplatoonC.Gameplay.Player
{
    // 角色身分:隊伍、名字、是否本機玩家、視線來源;也是塗墨與傷害的歸屬對象(IInkPainter / IDamageDealer)。
    // 玩家與 bot 掛同一套元件,差別只在意圖來源(PlayerInputRouter 的 override)與視線來源(IAimView)。
    [DisallowMultipleComponent]
    public sealed class Inkling : MonoBehaviour, IInkPainter, IDamageDealer
    {
        // OnEnable/OnDisable 自維護
        public static readonly List<Inkling> All = new List<Inkling>();

        [SerializeField, Tooltip("所屬隊伍")]
        private Team _team = Team.Alpha;

        [SerializeField, Tooltip("顯示名稱(HUD、擊殺訊息)")]
        private string _displayName = "Player";

        [SerializeField, Tooltip("本機玩家(相機跟隨、HUD 顯示對象);bot 關閉")]
        private bool _isLocalPlayer = true;

        private IAimView _aimView;
        private InklingModifiers _modifiers;
        private Transform _visualRoot;

        // 本機玩家(HUD、標記顯示的觀看方);沒有(8 bot 觀戰)= null
        public static Inkling Local
        {
            get
            {
                for (int i = 0; i < All.Count; i++)
                {
                    if (All[i] != null && All[i].IsLocalPlayer)
                    {
                        return All[i];
                    }
                }
                return null;
            }
        }

        // 暫時效果(毒霧、能量站…);Awake 自動掛上
        public InklingModifiers Modifiers => _modifiers != null ? _modifiers : (_modifiers = InklingModifiers.For(this));

        public Team Team => _team;
        public string DisplayName => _displayName;
        public bool IsLocalPlayer => _isLocalPlayer;

        // 本場累計塗地面積(m²);結算畫面換算成點數
        public float TurfInkedArea { get; private set; }

        // 新塗到地盤(面積, 是否充大招)——PlayerSpecialGauge 訂閱
        public event Action<float, bool> TurfInked;

        // 造成傷害(目標, 傷害量, 是否擊倒)——準星命中標記、統計訂閱
        public event Action<IDamageable, float, bool> DamageDealt;

        public int Splats { get; private set; }
        public int Assists { get; private set; }
        public int Deaths { get; private set; }
        public int SpecialsUsed { get; private set; }

        // 視線來源:bot 由腦袋指定;玩家預設找 Main Camera 上的 ThirdPersonCameraRig。
        public IAimView AimView
        {
            get
            {
                if (_aimView == null && _isLocalPlayer && Camera.main != null)
                {
                    _aimView = Camera.main.GetComponent<ThirdPersonCameraRig>();
                }
                return _aimView;
            }
            set => _aimView = value;
        }

        private void Awake()
        {
            // 效果元件不放進 prefab:執行期補上,舊場景/prefab 不必重新接線
            _modifiers = InklingModifiers.For(this);
            _visualRoot = transform.Find("Visual");
        }

        private void OnEnable()
        {
            All.Add(this);
        }

        private void OnDisable()
        {
            All.Remove(this);
        }

        public void Configure(Team team, string displayName, bool isLocalPlayer)
        {
            _team = team;
            _displayName = displayName;
            _isLocalPlayer = isLocalPlayer;
        }

        public void ResetMatchStats()
        {
            TurfInkedArea = 0f;
            Splats = 0;
            Assists = 0;
            Deaths = 0;
            SpecialsUsed = 0;
        }

        public void RegisterDeath()
        {
            Deaths++;
        }

        public void RegisterAssist()
        {
            Assists++;
        }

        public void RegisterSpecialUse()
        {
            SpecialsUsed++;
        }

        public void OnTurfInked(float areaSquareMetres, bool chargesSpecial)
        {
            TurfInkedArea += areaSquareMetres;
            TurfInked?.Invoke(areaSquareMetres, chargesSpecial);
        }

        public void OnDamageDealt(IDamageable target, float amount, bool splatted)
        {
            if (splatted)
            {
                Splats++;
            }
            DamageDealt?.Invoke(target, amount, splatted);
        }

        public InkSource InkSource(bool chargesSpecial = true)
        {
            return new InkSource(_team, this, chargesSpecial);
        }

        // 瞬移(重生、超級跳落地、Zipcaster/Inkjet 召回):放到 position、面向 yaw,並清掉殘留運動狀態——
        // 速度與跳躍動量、爬牆/翻越、烏賊游速餘韻與落地擠壓;CharacterController 開著時直接改 position
        // 會被下一幀的 Move 蓋回去,所以先關再開(原本就關著的 —— 例如陣亡中 —— 維持關閉)。
        // snapView:視線一併轉到 yaw 並讓鏡頭立刻跟上(不從舊位置滑過來);false = 保留目前視角。
        public void Teleport(Vector3 position, float yaw, bool snapView = true)
        {
            Place(position, true, yaw);
            if (!snapView)
            {
                return;
            }
            IAimView view = AimView;
            if (view is ThirdPersonCameraRig rig)
            {
                rig.SetAngles(yaw, 12f);
                rig.SnapToTarget();
            }
            else if (view is SplatoonC.Gameplay.AI.BotAimView botView)
            {
                botView.Snap(yaw, 10f);
            }
        }

        // 瞬移但保留目前面向與視角(除錯/測試搬位置用)
        public void Teleport(Vector3 position)
        {
            Place(position, false, 0f);
        }

        private void Place(Vector3 position, bool setYaw, float yaw)
        {
            // 超級跳進行中被外部瞬移(重生、測試搬位置):先中止跳躍——飛行中關掉的 CharacterController 由它開回來,
            // 下面才讀得到正確的「原本開著」狀態。超級跳自己落地時的瞬移不受影響
            var superJump = GetComponent<SuperJump>();
            if (superJump != null)
            {
                superJump.Abort();
            }
            var controller = GetComponent<CharacterController>();
            bool controllerWasEnabled = controller != null && controller.enabled;
            if (controller != null)
            {
                controller.enabled = false;
            }
            if (setYaw)
            {
                Quaternion facing = Quaternion.Euler(0f, yaw, 0f);
                transform.SetPositionAndRotation(position, facing);
                if (_visualRoot != null)
                {
                    _visualRoot.rotation = facing;
                }
            }
            else
            {
                transform.position = position;
            }
            var locomotion = GetComponent<PlayerLocomotion>();
            if (locomotion != null)
            {
                locomotion.ResetMotion();
            }
            var squid = GetComponent<SquidController>();
            if (squid != null)
            {
                squid.ResetMotionState();
            }
            if (controller != null)
            {
                controller.enabled = controllerWasEnabled;
            }
        }
    }
}

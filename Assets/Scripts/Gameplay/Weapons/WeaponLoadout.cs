using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 角色的武器組合:換裝時替換主武器元件(不同種類 = 不同 MainWeapon 子類)、設定副武器與大招、套用重量級別。
    // 比賽管理在生成角色後呼叫 Equip;沒指定就用 _defaultKit(prefab 上的預設)。
    [DisallowMultipleComponent]
    public sealed class WeaponLoadout : MonoBehaviour
    {
        [SerializeField, Tooltip("預設武器組合(未由比賽/選單指定時使用)")]
        private WeaponKit _defaultKit;

        public WeaponKit Kit { get; private set; }
        public MainWeapon Main { get; private set; }

        // 換裝完成(HUD 圖示、bot 行為更新)
        public event System.Action<WeaponKit> Equipped;

        private void Awake()
        {
            Main = GetComponent<MainWeapon>();
            if (_defaultKit != null && _defaultKit.IsValid)
            {
                Equip(_defaultKit);
            }
        }

        public void Equip(WeaponKit kit)
        {
            if (kit == null || !kit.IsValid)
            {
                Debug.LogError($"WeaponLoadout:武器組合無效({(kit != null ? kit.name : "null")})", this);
                return;
            }
            Kit = kit;

            // 主武器:種類不同就換元件(舊元件的池與狀態一併丟棄)
            System.Type wanted = kit.Main.BehaviourType;
            MainWeapon current = GetComponent<MainWeapon>();
            if (current != null && current.GetType() != wanted)
            {
                current.enabled = false;
                DestroyImmediate(current);
                current = null;
            }
            if (current == null)
            {
                current = (MainWeapon)gameObject.AddComponent(wanted);
            }
            current.Configure(kit.Main);
            Main = current;

            // 墨槽容量跟著主武器(S3 Splattershot Jr. 110%);副武器耗墨同樣以標準墨槽為單位
            var tank = GetComponent<PlayerInkTank>();
            if (tank != null)
            {
                tank.SetCapacityScale(kit.Main.InkTankScale);
            }

            var thrower = GetComponent<ThrowController>();
            if (thrower != null)
            {
                thrower.SetSub(kit.Sub);
            }
            var gauge = GetComponent<PlayerSpecialGauge>();
            if (gauge != null)
            {
                gauge.Equip(kit.Special, kit.SpecialPoints);
            }
            var locomotion = GetComponent<PlayerLocomotion>();
            if (locomotion != null)
            {
                locomotion.SetWeight(kit.Weight);
            }
            Equipped?.Invoke(kit);
        }
    }
}

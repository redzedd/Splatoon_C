using SplatoonC.Gameplay.Combat;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 選單版面測試專用的「假主武器設定」:可指定任意武器種類,讓武器選擇頁在只有射擊槍的現在
    // 就能排出 11 種分頁與 30 張卡片來檢查版面。只在執行期建立,永遠不存成資產、不會被裝備。
    public sealed class MenuPreviewMainConfig : MainWeaponConfig
    {
        private WeaponClass _class;

        public override WeaponClass Class => _class;
        public override System.Type BehaviourType => typeof(InkShooter);
        public override float EffectiveRange => 10f;

        public static MenuPreviewMainConfig Create(WeaponClass weaponClass)
        {
            var config = CreateInstance<MenuPreviewMainConfig>();
            config._class = weaponClass;
            config.hideFlags = HideFlags.DontUnloadUnusedAsset;
            config.name = "MenuPreview_" + weaponClass;
            return config;
        }
    }
}

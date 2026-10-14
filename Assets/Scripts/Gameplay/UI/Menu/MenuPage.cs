using UnityEngine;
using UnityEngine.UI;

namespace SplatoonC.Gameplay.UI.Menu
{
    // 選單的一頁(標題、主選單、武器選擇、設定、確認對話框…):由 MenuNavigator 以堆疊管理。
    // 頁面自己用程式碼建 UI(Build),記住上次選到的項目,返回時回到原位。
    public abstract class MenuPage
    {
        public RectTransform Root { get; private set; }
        public CanvasGroup Group { get; private set; }
        public MenuNavigator Navigator { get; private set; }

        // 上次選取的項目(回到本頁時恢復)
        public Selectable LastSelected { get; set; }

        // 疊在上一頁之上(確認對話框):下面那頁保持可見但不可操作
        public virtual bool IsOverlay => false;

        // 底部按鍵提示,格式「按鍵:說明|按鍵:說明」
        public virtual string Hints => "Enter / A:決定|Esc / B:返回";

        // 進入本頁時預設選取的項目
        public abstract Selectable DefaultSelection { get; }

        public void Create(RectTransform parent, MenuNavigator navigator)
        {
            Navigator = navigator;
            Root = Ui.Stretch(GetType().Name, parent);
            Group = Root.gameObject.AddComponent<CanvasGroup>();
            Build(Root);
            Root.gameObject.SetActive(false);
        }

        protected abstract void Build(RectTransform root);

        public virtual void OnShow()
        {
        }

        public virtual void OnHide()
        {
        }

        // 返回鍵:回傳 true = 本頁自行處理(不彈出)
        public virtual bool OnBack()
        {
            return false;
        }

        // 每幀(unscaled);頁面自己的輸入(切換類別的 Q/E 等)
        public virtual void Tick()
        {
        }
    }
}

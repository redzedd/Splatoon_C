using System.Collections.Generic;
using SplatoonC.Core.Match.Ranked;
using SplatoonC.Gameplay.Match;
using UnityEngine;
using UnityEngine.UI;

namespace SplatoonC.Gameplay.UI.Menu
{
    // 模式選擇:一般對戰(塗地)+ 蠻頹對戰四種模式;已實作的模式(ObjectiveModes.IsImplemented)可選,
    // 其餘鎖住「即將推出」。名稱與規則說明來自模式註冊表 ObjectiveModes。
    public sealed class ModeSelectPage : MenuPage
    {
        public struct ModeInfo
        {
            public string Id;
            public string Name;
            public string English;
            public string Rules;
            public bool Available;
        }

        private readonly MainMenuController _menu;
        private readonly List<MenuButton> _cards = new List<MenuButton>();
        private readonly List<ModeInfo> _modes = new List<ModeInfo>();
        private Text _rules;
        private Text _ruleTitle;

        public ModeSelectPage(MainMenuController menu)
        {
            _menu = menu;
        }

        public IReadOnlyList<MenuButton> Cards => _cards;
        public IReadOnlyList<ModeInfo> ModeList => _modes;

        public override Selectable DefaultSelection => _cards.Count > 0 ? _cards[0] : null;

        public static List<ModeInfo> Modes()
        {
            // S3 繁中版模式名稱;規則摘要取自 S3 規則說明(ObjectiveModes);順序 = 塗地、區域、塔樓、魚虎、蛤蜊
            MatchMode[] order =
            {
                MatchMode.TurfWar, MatchMode.SplatZones, MatchMode.TowerControl, MatchMode.Rainmaker, MatchMode.ClamBlitz,
            };
            var modes = new List<ModeInfo>(order.Length);
            foreach (MatchMode mode in order)
            {
                modes.Add(new ModeInfo
                {
                    Id = MatchModes.IdOf(mode),
                    Name = ObjectiveModes.DisplayName(mode),
                    English = ObjectiveModes.EnglishName(mode),
                    Rules = ObjectiveModes.Rules(mode),
                    Available = ObjectiveModes.IsImplemented(mode),
                });
            }
            return modes;
        }

        protected override void Build(RectTransform root)
        {
            MenuTheme.TapeTitle("Title", root, new Vector2(0f, 1f), new Vector2(90f, -60f), "選擇模式", 56, MenuTheme.Accent);
            _modes.AddRange(Modes());

            Section(root, "一般對戰", "REGULAR BATTLE", 130f, 196f, MenuTheme.Accent);
            bool allRanked = true;
            for (int i = 1; i < _modes.Count; i++)
            {
                allRanked &= _modes[i].Available;
            }
            Section(root, "蠻頹對戰", allRanked ? "ANARCHY BATTLE" : "ANARCHY BATTLE ・ 陸續開放", 790f, 196f, MenuTheme.Secondary);

            // 塗地:大卡
            MenuButton turf = MenuWidgets.Card("Mode_" + _modes[0].Id, root, 130f, 250f, 560f, 560f, _modes[0].Name,
                _modes[0].English, UiSprites.Splat(256, 9), 52);
            AddCard(turf, 0);

            // 蠻頹:2 × 2 小卡(未實作的鎖住)
            Sprite[] icons = { MenuSprites.ZoneIcon(), MenuSprites.TowerIcon(), MenuSprites.RainmakerIcon(), MenuSprites.ClamIcon() };
            const float w = 420f;
            const float h = 268f;
            for (int i = 1; i < _modes.Count; i++)
            {
                int k = i - 1;
                float x = 790f + (k % 2) * (w + 24f);
                float y = 250f + (k / 2) * (h + 24f);
                MenuButton card = MenuWidgets.Card("Mode_" + _modes[i].Id, root, x, y, w, h, _modes[i].Name, _modes[i].English,
                    icons[k], 38);
                AddCard(card, i);
            }

            Image rulesBack = MenuTheme.Panel9("RulesBack", root, new Vector2(0f, 0f), new Vector2(130f, 96f),
                new Vector2(1524f, 110f), MenuTheme.Panel, 20);
            _ruleTitle = MenuTheme.Label("RuleTitle", rulesBack.transform, new Vector2(0f, 0.5f), new Vector2(30f, 0f),
                new Vector2(260f, 60f), 34, TextAnchor.MiddleLeft, MenuTheme.Accent, 0f);
            _rules = MenuTheme.Paragraph("Rules", rulesBack.transform, new Vector2(0f, 0.5f), new Vector2(300f, 0f),
                new Vector2(1200f, 80f), 28, Color.white);
            _rules.alignment = TextAnchor.MiddleLeft;
        }

        private static void Section(Transform root, string title, string english, float x, float y, Color color)
        {
            Text label = MenuTheme.Label("Section_" + title, root, new Vector2(0f, 1f), new Vector2(x, -y),
                new Vector2(620f, 44f), 32, TextAnchor.MiddleLeft, Color.white, 2f);
            label.text = title;
            Text sub = MenuTheme.Label("SectionSub_" + title, root, new Vector2(0f, 1f), new Vector2(x + 150f, -y - 6f),
                new Vector2(520f, 36f), 20, TextAnchor.MiddleLeft, color, 0f);
            sub.text = english;
        }

        private void AddCard(MenuButton card, int index)
        {
            ModeInfo mode = _modes[index];
            card.Locked = !mode.Available;
            if (card.Locked)
            {
                // 「即將推出」戳章
                Image stamp = MenuTheme.Panel9("Stamp", card.Content, new Vector2(0.5f, 0.5f), new Vector2(0f, 18f),
                    new Vector2(250f, 56f), new Color(0.85f, 0.2f, 0.45f, 0.92f), 12);
                stamp.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                stamp.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 8f);
                Text text = MenuTheme.Label("StampText", stamp.transform, new Vector2(0.5f, 0.5f), Vector2.zero,
                    new Vector2(250f, 56f), 30, TextAnchor.MiddleCenter, Color.white, 0f);
                text.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                text.text = "即將推出";
            }
            else
            {
                card.IconColor = MenuTheme.Accent;
            }
            card.Focused += () =>
            {
                _ruleTitle.text = mode.Name;
                _rules.text = mode.Available ? mode.Rules : mode.Rules + "(尚未開放)";
            };
            card.Activated += () =>
            {
                _menu.ModeId = mode.Id;
                _menu.Navigator.Push(_menu.Stages);
            };
            _cards.Add(card);
        }
    }
}

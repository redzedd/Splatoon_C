using SplatoonC.Core;
using SplatoonC.Core.Match;
using SplatoonC.Core.Match.Ranked;
using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.Match;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.UI.Menu;
using UnityEngine;
using UnityEngine.UI;

namespace SplatoonC.Gameplay.UI
{
    // 蠻頹對戰 HUD(ObjectiveModeController 掛上;疊在 MatchHud 之上,不改動塗地的 HUD):
    //   - 計時器兩側、隊員圖示下方:兩隊計數(隊伍色方塊 + 白字),罰分「+N」顯示在計數下方
    //   - 計時器正下方:區域狀態圖示(佔領方顏色 / 中立色)+ 區域內墨量分配條(70% 佔領線)
    //   - 加時:計時器位置換成「延長戰!」框,底色 = 落後方顏色並隨寬限時間流乾(S3 加時框)
    //   - 播報:我方佔領了區域! / 區域被奪走! / 區域失守! / 阻止了對手! / 延長戰!;罰分彈出「+N」
    // 我方 = 本機玩家的隊伍(觀戰 = Alpha),永遠在左。每幀只改數值,字串在數值變了才重建。
    public sealed class ObjectiveHud : MonoBehaviour
    {
        private sealed class CountPanel
        {
            public RectTransform Root;
            public Image Box;
            public Image Glow;
            public Text Count;
            public Text Penalty;
            public Text Popup;
            public float PopupUntil;
            public float PulseUntil;
            public int ShownCount = -1;
            public int ShownPenalty = -1;
        }

        private sealed class ZoneIcon
        {
            public RectTransform Root;
            public Image Icon;
            public Image BarAlly;
            public Image BarEnemy;
        }

        private const float CalloutSeconds = 2.2f;
        private const float PopupSeconds = 1.8f;
        private const float OvertimeFillWidth = 164f;

        private MatchController _match;
        private ObjectiveModeController _objective;
        private IObjectiveMode _rules;
        private SplatZonesRules _zones;
        private Team _ally = Team.Alpha;
        private Team _enemy = Team.Bravo;

        private Canvas _canvas;
        private RectTransform _root;
        private CountPanel _allyPanel;
        private CountPanel _enemyPanel;
        private ZoneIcon[] _zoneIcons = new ZoneIcon[0];
        private RectTransform _overtimeRoot;
        private Image _overtimeFill;
        private Text _overtimeText;
        private RectTransform _calloutRoot;
        private Image _calloutBack;
        private Text _calloutText;
        private float _calloutStart = -99f;
        private bool _penaltyThisChange;

        public string LastCallout { get; private set; }
        public int CalloutCount { get; private set; }
        public bool OvertimeBoxVisible => _overtimeRoot != null && _overtimeRoot.gameObject.activeSelf;
        public string AllyCountText => _allyPanel != null ? _allyPanel.Count.text : null;
        public string EnemyCountText => _enemyPanel != null ? _enemyPanel.Count.text : null;
        public string AllyPenaltyText => _allyPanel != null && _allyPanel.Penalty.enabled ? _allyPanel.Penalty.text : "";
        public string EnemyPenaltyText => _enemyPanel != null && _enemyPanel.Penalty.enabled ? _enemyPanel.Penalty.text : "";
        public Color ZoneIconColor(int zone) => zone < _zoneIcons.Length ? _zoneIcons[zone].Icon.color : Color.clear;
        public Canvas Canvas => _canvas;

        private void Start()
        {
            _match = MatchController.Instance;
            _objective = ObjectiveModeController.Instance;
            _rules = _objective != null ? _objective.Rules : null;
            if (_match == null || _rules == null)
            {
                enabled = false;
                return;
            }
            if (_match.LocalPlayer != null)
            {
                _ally = _match.LocalPlayer.Team;
            }
            _enemy = TeamUtil.Opponent(_ally);
            _zones = _rules as SplatZonesRules;
            Build();
            if (_zones != null)
            {
                _zones.ControlChanged += OnControlChanged;
                _zones.ZoneNeutralized += OnZoneNeutralized;
                _zones.ZoneCaptured += OnZoneCaptured;
                _zones.PenaltyAdded += OnPenaltyAdded;
                _zones.CountChanged += OnCountChanged;
            }
            _objective.OvertimeStarted += OnOvertimeStarted;
            _match.PhaseChanged += OnPhaseChanged;
            OnPhaseChanged(_match.Phase);
        }

        private void OnDestroy()
        {
            if (_zones != null)
            {
                _zones.ControlChanged -= OnControlChanged;
                _zones.ZoneNeutralized -= OnZoneNeutralized;
                _zones.ZoneCaptured -= OnZoneCaptured;
                _zones.PenaltyAdded -= OnPenaltyAdded;
                _zones.CountChanged -= OnCountChanged;
            }
            if (_objective != null)
            {
                _objective.OvertimeStarted -= OnOvertimeStarted;
            }
            if (_match != null)
            {
                _match.PhaseChanged -= OnPhaseChanged;
            }
        }

        // ───────────────────────── 建立 ─────────────────────────

        private void Build()
        {
            _canvas = Ui.CreateCanvas("ObjectiveHudCanvas", 11);
            _canvas.transform.SetParent(transform, false);
            _root = Ui.Stretch("Objective", _canvas.transform);
            // 計數方塊:對齊 MatchHud 的隊員圖示列(我方 −110 − k·78、敵方 110 + k·78)的中央
            _allyPanel = CreatePanel("AllyCount", -227f);
            _enemyPanel = CreatePanel("EnemyCount", 227f);

            int zoneCount = _zones != null ? _zones.ZoneCount : 0;
            _zoneIcons = new ZoneIcon[zoneCount];
            for (int i = 0; i < zoneCount; i++)
            {
                float x = zoneCount == 1 ? 0f : (i - (zoneCount - 1) * 0.5f) * 84f;
                _zoneIcons[i] = CreateZoneIcon(i, x);
            }

            // 加時框:蓋在 MatchHud 的計時器上(同位置,稍寬)
            _overtimeRoot = Ui.Rect("Overtime", _root, new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(176f, 78f));
            Ui.Image("Back", _overtimeRoot, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(176f, 78f),
                UiSprites.RoundedRect(18), new Color(0f, 0f, 0f, 0.9f)).rectTransform.pivot = new Vector2(0.5f, 0.5f);
            // 流乾的色塊:九宮格圓角,寬度 = 寬限比例(左側固定,往左縮)
            _overtimeFill = Ui.Image("Fill", _overtimeRoot, new Vector2(0f, 0.5f), new Vector2(6f, 0f),
                new Vector2(OvertimeFillWidth, 66f), UiSprites.RoundedRect(14), Color.white);
            _overtimeText = Ui.Text("Text", _overtimeRoot, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(176f, 78f), 38,
                TextAnchor.MiddleCenter, Color.white, true, 2.5f);
            _overtimeText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _overtimeText.text = "延長戰!";
            _overtimeRoot.gameObject.SetActive(false);

            // 播報橫幅(區域狀態下方)
            _calloutRoot = Ui.Rect("Callout", _root, new Vector2(0.5f, 1f), new Vector2(0f, -250f), new Vector2(760f, 76f));
            _calloutRoot.pivot = new Vector2(0.5f, 0.5f);
            _calloutBack = Ui.Image("Back", _calloutRoot, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 76f),
                UiSprites.RoundedRect(22), Color.white);
            _calloutBack.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _calloutText = Ui.Text("Text", _calloutRoot, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 76f), 46,
                TextAnchor.MiddleCenter, Color.white, true, 3f);
            _calloutText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _calloutRoot.gameObject.SetActive(false);
        }

        private CountPanel CreatePanel(string name, float x)
        {
            var panel = new CountPanel();
            panel.Root = Ui.Rect(name, _root, new Vector2(0.5f, 1f), new Vector2(x, -148f), new Vector2(150f, 74f));
            panel.Root.pivot = new Vector2(0.5f, 0.5f);
            panel.Glow = Ui.Image("Glow", panel.Root, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(176f, 100f),
                UiSprites.RoundedRect(24), new Color(1f, 1f, 1f, 0f));
            panel.Glow.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            panel.Box = Ui.Image("Box", panel.Root, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(150f, 74f),
                UiSprites.RoundedRect(18), Color.white);
            panel.Box.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            panel.Count = Ui.Text("Count", panel.Root, new Vector2(0.5f, 0.5f), new Vector2(0f, 2f), new Vector2(150f, 74f), 60,
                TextAnchor.MiddleCenter, Color.white, true, 3f);
            panel.Count.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            panel.Penalty = Ui.Text("Penalty", panel.Root, new Vector2(0.5f, 0.5f), new Vector2(0f, -58f), new Vector2(150f, 40f),
                32, TextAnchor.MiddleCenter, Color.white, true, 2.5f);
            panel.Penalty.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            panel.Penalty.enabled = false;
            panel.Popup = Ui.Text("Popup", panel.Root, new Vector2(0.5f, 0.5f), new Vector2(0f, -100f), new Vector2(260f, 50f),
                40, TextAnchor.MiddleCenter, new Color(1f, 0.35f, 0.3f), true, 3f);
            panel.Popup.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            panel.Popup.enabled = false;
            return panel;
        }

        private ZoneIcon CreateZoneIcon(int index, float x)
        {
            var icon = new ZoneIcon();
            icon.Root = Ui.Rect("Zone" + index, _root, new Vector2(0.5f, 1f), new Vector2(x, -140f), new Vector2(80f, 96f));
            icon.Root.pivot = new Vector2(0.5f, 0.5f);
            Ui.Image("Back", icon.Root, new Vector2(0.5f, 0.5f), new Vector2(0f, 8f), new Vector2(76f, 76f),
                UiSprites.RoundedRect(16), new Color(0f, 0f, 0f, 0.7f)).rectTransform.pivot = new Vector2(0.5f, 0.5f);
            icon.Icon = Ui.Image("Icon", icon.Root, new Vector2(0.5f, 0.5f), new Vector2(0f, 8f), new Vector2(62f, 62f),
                MenuSprites.ZoneIcon(), Color.white);
            icon.Icon.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            // 墨量分配條:我方從左長、敵方從右長;中間兩條細線 = 各自的 70% 佔領線
            Image back = Ui.Image("InkBar", icon.Root, new Vector2(0.5f, 0.5f), new Vector2(0f, -40f), new Vector2(80f, 12f),
                UiSprites.RoundedRect(5), new Color(0f, 0f, 0f, 0.75f));
            back.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            icon.BarAlly = CreateBar(icon.Root, Image.OriginHorizontal.Left);
            icon.BarEnemy = CreateBar(icon.Root, Image.OriginHorizontal.Right);
            for (int side = -1; side <= 1; side += 2)
            {
                Ui.Image("Line70", icon.Root, new Vector2(0.5f, 0.5f), new Vector2(side * (0.7f - 0.5f) * 76f, -40f),
                    new Vector2(2f, 14f), null, new Color(1f, 1f, 1f, 0.8f)).rectTransform.pivot = new Vector2(0.5f, 0.5f);
            }
            return icon;
        }

        private static Image CreateBar(Transform parent, Image.OriginHorizontal origin)
        {
            Image bar = Ui.Image("Bar" + origin, parent, new Vector2(0.5f, 0.5f), new Vector2(0f, -40f), new Vector2(76f, 8f),
                UiSprites.RoundedRect(4), Color.white);
            bar.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            bar.type = Image.Type.Filled;
            bar.fillMethod = Image.FillMethod.Horizontal;
            bar.fillOrigin = (int)origin;
            return bar;
        }

        // ───────────────────────── 事件 ─────────────────────────

        private void OnPhaseChanged(MatchPhase phase)
        {
            bool show = phase < MatchPhase.Judging;
            _root.gameObject.SetActive(show);
        }

        private void OnPenaltyAdded(Team team, int amount)
        {
            _penaltyThisChange = true;
            CountPanel panel = team == _ally ? _allyPanel : _enemyPanel;
            panel.Popup.text = "+" + amount;
            panel.Popup.enabled = true;
            panel.PopupUntil = Time.time + PopupSeconds;
        }

        private void OnControlChanged(Team next, Team previous)
        {
            bool stolen = _penaltyThisChange;
            _penaltyThisChange = false;
            if (next == _ally)
            {
                Callout("我方佔領了區域!", _ally);
                GameAudio.Play2D("zone_captured");
            }
            else if (next == _enemy)
            {
                Callout(stolen ? "區域被奪走!" : "對手佔領了區域!", _enemy);
                GameAudio.Play2D("zone_lost");
            }
        }

        private void OnZoneNeutralized(int zone, Team former)
        {
            if (former == _ally)
            {
                Callout("區域失守!", Team.None);
            }
            else if (former == _enemy)
            {
                Callout("阻止了對手!", Team.None);
            }
            GameAudio.Play2D("zone_neutral");
        }

        // 2 個區域時:佔到其中一個(控制權還沒變)也播報
        private void OnZoneCaptured(int zone, Team team)
        {
            if (_zones == null || _zones.ZoneCount < 2)
            {
                return;
            }
            Callout(team == _ally ? "我方佔領了一個區域!" : "對手佔領了一個區域!", team);
        }

        private void OnCountChanged(Team team)
        {
            CountPanel panel = team == _ally ? _allyPanel : _enemyPanel;
            panel.PulseUntil = Time.time + 0.25f;
        }

        private void OnOvertimeStarted()
        {
            Callout("延長戰!", _rules.OvertimeLoser);
        }

        private void Callout(string text, Team colorTeam)
        {
            LastCallout = text;
            CalloutCount++;
            _calloutText.text = text;
            Color color = SplatZonesDriver.ColorOf(colorTeam);
            _calloutBack.color = Ui.WithAlpha(Color.Lerp(color, Color.black, 0.25f), 0.92f);
            _calloutStart = Time.time;
            _calloutRoot.gameObject.SetActive(true);
        }

        // ───────────────────────── 每幀 ─────────────────────────

        private void Update()
        {
            if (_rules == null || _canvas == null || !_root.gameObject.activeSelf)
            {
                return;
            }
            Color allyColor = TeamPalette.ColorOf(_ally);
            Color enemyColor = TeamPalette.ColorOf(_enemy);
            Team holder = _rules.ObjectiveHolder;
            UpdatePanel(_allyPanel, _rules.Alpha.Team == _ally ? _rules.Alpha : _rules.Bravo, allyColor, holder == _ally);
            UpdatePanel(_enemyPanel, _rules.Alpha.Team == _enemy ? _rules.Alpha : _rules.Bravo, enemyColor, holder == _enemy);
            UpdateZoneIcons(allyColor, enemyColor);
            UpdateOvertime();
            UpdateCallout();
        }

        private void UpdatePanel(CountPanel panel, TeamScore score, Color color, bool holding)
        {
            if (panel.ShownCount != score.Count)
            {
                panel.ShownCount = score.Count;
                panel.Count.text = score.Count.ToString();
            }
            if (panel.ShownPenalty != score.Penalty)
            {
                panel.ShownPenalty = score.Penalty;
                panel.Penalty.enabled = score.Penalty > 0;
                panel.Penalty.text = "+" + score.Penalty;
            }
            panel.Box.color = color;
            // 持有區域:外框呼吸發光;計數跳動時放大一下
            float glow = holding ? 0.45f + 0.3f * Mathf.Sin(Time.time * 6f) : 0f;
            panel.Glow.color = Ui.WithAlpha(Color.Lerp(color, Color.white, 0.6f), glow);
            float pulse = Mathf.Clamp01((panel.PulseUntil - Time.time) / 0.25f);
            panel.Root.localScale = Vector3.one * (1f + pulse * 0.12f);
            if (panel.Popup.enabled)
            {
                float t = 1f - Mathf.Clamp01((panel.PopupUntil - Time.time) / PopupSeconds);
                panel.Popup.rectTransform.anchoredPosition = new Vector2(0f, -100f - t * 30f);
                Ui.SetAlpha(panel.Popup, 1f - t * t);
                if (t >= 1f)
                {
                    panel.Popup.enabled = false;
                }
            }
        }

        private void UpdateZoneIcons(Color allyColor, Color enemyColor)
        {
            if (_zones == null)
            {
                return;
            }
            for (int i = 0; i < _zoneIcons.Length; i++)
            {
                ZoneIcon icon = _zoneIcons[i];
                Team owner = _zones.OwnerOf(i);
                icon.Icon.color = SplatZonesDriver.ColorOf(owner);
                icon.BarAlly.color = allyColor;
                icon.BarEnemy.color = enemyColor;
                icon.BarAlly.fillAmount = _zones.InkOf(i, _ally);
                icon.BarEnemy.fillAmount = _zones.InkOf(i, _enemy);
            }
        }

        private void UpdateOvertime()
        {
            bool overtime = _rules.InOvertime && _match.Phase == MatchPhase.Battle;
            if (_overtimeRoot.gameObject.activeSelf != overtime)
            {
                _overtimeRoot.gameObject.SetActive(overtime);
            }
            if (!overtime)
            {
                return;
            }
            // S3:框的底色 = 落後方顏色,隨寬限時間流乾;落後方持有時滿格閃爍
            float grace = _rules.OvertimeGrace01;
            _overtimeFill.rectTransform.sizeDelta = new Vector2(Mathf.Max(28f, OvertimeFillWidth * grace), 66f);
            _overtimeFill.enabled = grace > 0.01f;
            Color loser = TeamPalette.ColorOf(_rules.OvertimeLoser);
            bool holding = _rules.ObjectiveHolder == _rules.OvertimeLoser;
            _overtimeFill.color = holding ? Color.Lerp(loser, Color.white, 0.2f + 0.2f * Mathf.Sin(Time.time * 9f)) : loser;
        }

        private void UpdateCallout()
        {
            if (!_calloutRoot.gameObject.activeSelf)
            {
                return;
            }
            float t = (Time.time - _calloutStart) / CalloutSeconds;
            if (t >= 1f)
            {
                _calloutRoot.gameObject.SetActive(false);
                return;
            }
            _calloutRoot.localScale = Vector3.one * Ui.PopScale(Mathf.Clamp01(t * 6f));
            float fade = t > 0.8f ? 1f - (t - 0.8f) / 0.2f : 1f;
            Ui.SetAlpha(_calloutText, fade);
            Ui.SetAlpha(_calloutBack, 0.92f * fade);
        }
    }
}

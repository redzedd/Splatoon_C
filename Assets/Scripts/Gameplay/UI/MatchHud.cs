using System.Collections.Generic;
using SplatoonC.Core;
using SplatoonC.Core.Match;
using SplatoonC.Core.Match.Ranked;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Match;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SplatoonC.Gameplay.UI
{
    // 對戰 HUD(S3 版面):上方計時 + 兩隊各 4 個烏賊頭圖示、右上塗地點數 + 分段圓環大招量表、
    // 中央準星 + 命中標記、低墨提示與墨量條、受傷暗角、開場/倒數/最後一分鐘/GAME! 標題、擊倒訊息與重生倒數、
    // 判定畫面(兩側色條同步增長後分出勝負)與結算表。全部執行期用程式碼建立,不需要場景接線。
    public sealed class MatchHud : MonoBehaviour
    {
        private sealed class IconSlot
        {
            public RectTransform Root;
            public Image Glow;
            public Image Body;
            public Image Eyes;
            public Image Cross;
            public Inkling Inkling;
        }

        private MatchController _match;
        private Inkling _player;
        private InklingHealth _health;
        private InklingLifecycle _life;
        private PlayerInkTank _tank;
        private PlayerSpecialGauge _special;
        private SquidController _squid;
        private Team _allyTeam = Team.Alpha;

        private Canvas _canvas;
        private RectTransform _battleRoot;
        private Text _timer;
        private Image _timerBox;
        private readonly List<IconSlot> _allyIcons = new List<IconSlot>();
        private readonly List<IconSlot> _enemyIcons = new List<IconSlot>();
        private Text _points;
        private Image _gaugeFill;
        private Image _gaugeGlow;
        private Text _gaugeHint;
        private Image _reticleRing;
        private Image _reticleDot;
        private Image _hitMarker;
        private float _hitMarkerUntil;
        private bool _hitMarkerKill;
        private RectTransform _inkBarRoot;
        private Image _inkBarFill;
        private Text _lowInk;
        private Image _vignette;
        private Text _feed;
        private float _feedUntil;
        private Text _deathText;
        private Text _respawnText;

        private RectTransform _calloutRoot;
        private Image _calloutSplat;
        private Text _calloutText;
        private Text _calloutSub;
        private float _calloutStart;
        private float _calloutDuration;
        private Text _bigCountdown;
        private int _lastCountdownSecond = -1;
        private bool _lastMinuteShown;

        private RectTransform _judgeRoot;
        private Image _judgeBarAlly;
        private Image _judgeBarEnemy;
        private Text _judgePercentAlly;
        private Text _judgePercentEnemy;
        private Text _judgeVerdict;
        private Image _juddAlly;
        private Image _juddEnemy;
        private float _judgeStart;

        private RectTransform _resultsRoot;
        private bool _resultsBuilt;

        private StatusIconRow _statusRow;

        private void Start()
        {
            _match = MatchController.Instance;
            if (_match == null)
            {
                Debug.LogError("MatchHud:場景沒有 MatchController", this);
                enabled = false;
                return;
            }
            BindPlayer(_match.LocalPlayer);
            Build();
            _match.PhaseChanged += OnPhaseChanged;
            OnPhaseChanged(_match.Phase);
        }

        private void OnDestroy()
        {
            if (_match != null)
            {
                _match.PhaseChanged -= OnPhaseChanged;
            }
            if (_player != null)
            {
                _player.DamageDealt -= OnDamageDealt;
            }
        }

        private void BindPlayer(Inkling player)
        {
            _player = player;
            if (player == null)
            {
                return;
            }
            _allyTeam = player.Team;
            _health = player.GetComponent<InklingHealth>();
            _life = player.GetComponent<InklingLifecycle>();
            _tank = player.GetComponent<PlayerInkTank>();
            _special = player.GetComponent<PlayerSpecialGauge>();
            _squid = player.GetComponent<SquidController>();
            player.DamageDealt += OnDamageDealt;
        }

        // ───────────────────────── 建立 ─────────────────────────

        private void Build()
        {
            _canvas = Ui.CreateCanvas("MatchHudCanvas", 10);
            _canvas.transform.SetParent(transform, false);
            Transform root = _canvas.transform;

            _vignette = Ui.Image("DamageVignette", root, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1920f, 1080f),
                UiSprites.Vignette(), new Color(1f, 1f, 1f, 0f));
            ((RectTransform)_vignette.transform).anchorMin = Vector2.zero;
            ((RectTransform)_vignette.transform).anchorMax = Vector2.one;
            ((RectTransform)_vignette.transform).sizeDelta = Vector2.zero;

            _battleRoot = Ui.Stretch("Battle", root);
            BuildTopBar(_battleRoot);
            BuildGauge(_battleRoot);
            BuildReticle(_battleRoot);
            BuildInkBar(_battleRoot);
            BuildStatusRow(_battleRoot);

            _feed = Ui.Text("Feed", _battleRoot, new Vector2(0.5f, 0.28f), Vector2.zero, new Vector2(800f, 60f), 40,
                TextAnchor.MiddleCenter, Color.white, true, 3f);
            _deathText = Ui.Text("Death", root, new Vector2(0.5f, 0.62f), Vector2.zero, new Vector2(900f, 80f), 54,
                TextAnchor.MiddleCenter, Color.white, true, 3f);
            _respawnText = Ui.Text("Respawn", root, new Vector2(0.5f, 0.52f), Vector2.zero, new Vector2(600f, 80f), 44,
                TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.9f), true, 3f);

            _calloutRoot = Ui.Rect("Callout", root, new Vector2(0.5f, 0.58f), Vector2.zero, new Vector2(900f, 420f));
            _calloutSplat = Ui.Image("Splat", _calloutRoot, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 760f),
                UiSprites.Splat(256, 7), new Color(0f, 0f, 0f, 0.82f));
            _calloutText = Ui.Text("Text", _calloutRoot, new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(900f, 160f),
                120, TextAnchor.MiddleCenter, Color.white, true, 4f);
            _calloutSub = Ui.Text("Sub", _calloutRoot, new Vector2(0.5f, 0.5f), new Vector2(0f, -80f), new Vector2(900f, 60f),
                38, TextAnchor.MiddleCenter, Color.white, true, 2f);
            _calloutRoot.gameObject.SetActive(false);

            _bigCountdown = Ui.Text("FinalCountdown", root, new Vector2(0.5f, 0.78f), Vector2.zero, new Vector2(300f, 160f),
                130, TextAnchor.MiddleCenter, Color.white, true, 4f);
            _bigCountdown.gameObject.SetActive(false);

            BuildJudge(root);
            _resultsRoot = Ui.Stretch("Results", root);
            _resultsRoot.gameObject.SetActive(false);

            // 初始狀態:沒有綁到玩家(觀戰)時這些都不該出現
            _lowInk.enabled = false;
            _inkBarRoot.gameObject.SetActive(false);
            _gaugeFill.fillAmount = 0f;
            _deathText.enabled = false;
            _respawnText.enabled = false;
            _feed.enabled = false;
        }

        private void BuildTopBar(Transform parent)
        {
            _timerBox = Ui.Image("TimerBox", parent, new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(150f, 70f),
                UiSprites.RoundedRect(18), new Color(0f, 0f, 0f, 0.8f));
            _timer = Ui.Text("Timer", _timerBox.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(150f, 70f), 46,
                TextAnchor.MiddleCenter, Color.white, true, 0f);

            // 我方永遠在左、敵方在右;本機玩家排在我方最靠近計時器的位置
            var allies = new List<Inkling>();
            var enemies = new List<Inkling>();
            foreach (Inkling inkling in _match.Roster)
            {
                if (inkling == null)
                {
                    continue;
                }
                (inkling.Team == _allyTeam ? allies : enemies).Add(inkling);
            }
            for (int i = 0; i < allies.Count; i++)
            {
                float x = -110f - (allies.Count - 1 - i) * 78f;
                _allyIcons.Add(CreateIcon(parent, new Vector2(x, -30f), allies[i]));
            }
            for (int i = 0; i < enemies.Count; i++)
            {
                float x = 110f + i * 78f;
                _enemyIcons.Add(CreateIcon(parent, new Vector2(x, -30f), enemies[i]));
            }
        }

        private IconSlot CreateIcon(Transform parent, Vector2 pos, Inkling inkling)
        {
            var slot = new IconSlot { Inkling = inkling };
            slot.Root = Ui.Rect("Icon_" + inkling.DisplayName, parent, new Vector2(0.5f, 1f), pos, new Vector2(70f, 70f));
            slot.Glow = Ui.Image("Glow", slot.Root, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(96f, 96f),
                UiSprites.Circle(), new Color(1f, 1f, 1f, 0f));
            slot.Body = Ui.Image("Body", slot.Root, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(70f, 70f),
                UiSprites.SquidHead(), Color.white);
            slot.Eyes = Ui.Image("Eyes", slot.Root, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(70f, 70f),
                UiSprites.SquidEyes(), Color.white);
            slot.Cross = Ui.Image("Cross", slot.Root, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(56f, 56f),
                UiSprites.Cross(), new Color(0.75f, 0.75f, 0.75f, 0.95f));
            if (inkling.IsLocalPlayer)
            {
                // 本機玩家:圖示下方小三角標記
                Ui.Text("You", slot.Root, new Vector2(0.5f, 0f), new Vector2(0f, -16f), new Vector2(70f, 24f), 18,
                    TextAnchor.MiddleCenter, Color.white, true, 1.5f).text = "▲";
            }
            return slot;
        }

        private void BuildGauge(Transform parent)
        {
            _points = Ui.Text("Points", parent, new Vector2(1f, 1f), new Vector2(-230f, -40f), new Vector2(220f, 70f), 54,
                TextAnchor.MiddleRight, Color.white, true, 3f);
            Vector2 center = new Vector2(-100f, -100f);
            Sprite ring = UiSprites.SegmentedRing(192, 26f, 23, 3.2f);
            Ui.Image("GaugeBack", parent, new Vector2(1f, 1f), center + new Vector2(0f, 0f), new Vector2(180f, 180f),
                UiSprites.Circle(), new Color(0f, 0f, 0f, 0.55f)).rectTransform.pivot = new Vector2(0.5f, 0.5f);
            Image track = Ui.Image("GaugeTrack", parent, new Vector2(1f, 1f), center, new Vector2(170f, 170f), ring,
                new Color(1f, 1f, 1f, 0.18f));
            track.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _gaugeFill = Ui.Image("GaugeFill", parent, new Vector2(1f, 1f), center, new Vector2(170f, 170f), ring, Color.white);
            _gaugeFill.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _gaugeFill.type = Image.Type.Filled;
            _gaugeFill.fillMethod = Image.FillMethod.Radial360;
            _gaugeFill.fillOrigin = (int)Image.Origin360.Top;
            _gaugeFill.fillClockwise = true;
            _gaugeGlow = Ui.Image("GaugeGlow", parent, new Vector2(1f, 1f), center, new Vector2(200f, 200f), UiSprites.Circle(),
                new Color(1f, 1f, 1f, 0f));
            _gaugeGlow.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _gaugeGlow.transform.SetSiblingIndex(0);
            _gaugeHint = Ui.Text("GaugeHint", parent, new Vector2(1f, 1f), center, new Vector2(170f, 60f), 34,
                TextAnchor.MiddleCenter, Color.white, true, 2f);
            _gaugeHint.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        }

        private void BuildReticle(Transform parent)
        {
            Sprite ring = UiSprites.SegmentedRing(96, 7f, 4, 30f);
            _reticleRing = Ui.Image("ReticleRing", parent, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(46f, 46f), ring,
                new Color(1f, 1f, 1f, 0.9f));
            _reticleRing.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _reticleDot = Ui.Image("ReticleDot", parent, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(7f, 7f),
                UiSprites.Circle(32), Color.white);
            _reticleDot.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _hitMarker = Ui.Image("HitMarker", parent, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40f, 40f),
                UiSprites.Cross(), new Color(1f, 1f, 1f, 0f));
            _hitMarker.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _lowInk = Ui.Text("LowInk", parent, new Vector2(0.5f, 0.5f), new Vector2(0f, -70f), new Vector2(400f, 50f), 30,
                TextAnchor.MiddleCenter, new Color(1f, 0.35f, 0.3f), true, 2f);
            _lowInk.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _lowInk.text = "墨水不足!";
        }

        private void BuildInkBar(Transform parent)
        {
            _inkBarRoot = Ui.Rect("InkBar", parent, new Vector2(0.5f, 0.5f), new Vector2(120f, -10f), new Vector2(22f, 120f));
            Ui.Image("Back", _inkBarRoot, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(22f, 120f),
                UiSprites.RoundedRect(10), new Color(0f, 0f, 0f, 0.6f));
            _inkBarFill = Ui.Image("Fill", _inkBarRoot, new Vector2(0.5f, 0f), new Vector2(0f, 3f), new Vector2(16f, 114f),
                UiSprites.RoundedRect(7), Color.white);
            _inkBarFill.type = Image.Type.Filled;
            _inkBarFill.fillMethod = Image.FillMethod.Vertical;
            _inkBarFill.fillOrigin = (int)Image.OriginVertical.Bottom;
            // 副武器門檻線(斯普拉炸彈 70%)
            Ui.Image("SubLine", _inkBarRoot, new Vector2(0.5f, 0f), new Vector2(0f, 3f + 114f * 0.7f), new Vector2(26f, 3f),
                null, new Color(1f, 0.2f, 0.2f, 0.95f));
        }

        private void BuildJudge(Transform parent)
        {
            _judgeRoot = Ui.Stretch("Judge", parent);
            var shade = _judgeRoot.gameObject.AddComponent<Image>();
            shade.color = new Color(0f, 0f, 0f, 0.25f);
            shade.raycastTarget = false;
            Transform r = _judgeRoot;
            // 底部黑帶 + 兩側色條
            Ui.Image("Band", r, new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(1920f, 150f), null,
                new Color(0f, 0f, 0f, 0.85f));
            _judgeBarAlly = Ui.Image("BarAlly", r, new Vector2(0f, 0f), new Vector2(0f, 70f), new Vector2(0f, 90f),
                UiSprites.RoundedRect(20), Color.white);
            _judgeBarEnemy = Ui.Image("BarEnemy", r, new Vector2(1f, 0f), new Vector2(0f, 70f), new Vector2(0f, 90f),
                UiSprites.RoundedRect(20), Color.white);
            _judgePercentAlly = Ui.Text("PctAlly", r, new Vector2(0.25f, 0f), new Vector2(0f, 250f), new Vector2(500f, 120f),
                96, TextAnchor.MiddleCenter, Color.white, true, 4f);
            _judgePercentEnemy = Ui.Text("PctEnemy", r, new Vector2(0.75f, 0f), new Vector2(0f, 250f), new Vector2(500f, 120f),
                96, TextAnchor.MiddleCenter, Color.white, true, 4f);
            _juddAlly = Ui.Image("Judd", r, new Vector2(0.08f, 0f), new Vector2(0f, 200f), new Vector2(170f, 170f),
                UiSprites.CatHead(), new Color(0.97f, 0.97f, 0.97f));
            Ui.Image("Face", _juddAlly.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(170f, 170f),
                UiSprites.CatFace(), new Color(0.15f, 0.15f, 0.15f));
            _juddEnemy = Ui.Image("LilJudd", r, new Vector2(0.92f, 0f), new Vector2(0f, 190f), new Vector2(120f, 120f),
                UiSprites.CatHead(), new Color(0.93f, 0.78f, 0.55f));
            Ui.Image("Face", _juddEnemy.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(120f, 120f),
                UiSprites.CatFace(), new Color(0.2f, 0.15f, 0.1f));
            _judgeVerdict = Ui.Text("Verdict", r, new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(1400f, 180f),
                150, TextAnchor.MiddleCenter, Color.white, true, 5f);
            _judgeRoot.gameObject.SetActive(false);
        }

        // ───────────────────────── 流程 ─────────────────────────

        private void OnPhaseChanged(MatchPhase phase)
        {
            switch (phase)
            {
                case MatchPhase.Intro:
                    // 模式標題卡(塗地:「塗地對戰 / 塗得比對手多就能獲勝!」)
                    ShowCallout(ObjectiveModes.DisplayName(_match.Mode), ObjectiveModes.Objective(_match.Mode), 3.8f, 96,
                        Color.black);
                    break;
                case MatchPhase.Countdown:
                    ShowCallout("Ready?", "", 1.6f, 120, Color.black);
                    break;
                case MatchPhase.Battle:
                    ShowCallout("GO!", "", 1.0f, 150, TeamPalette.ColorOf(_allyTeam));
                    break;
                case MatchPhase.Finish:
                    _bigCountdown.gameObject.SetActive(false);
                    // 蠻頹對戰計數歸 0 → KNOCKOUT!
                    ShowCallout(_match.EndedByKnockout ? "KNOCKOUT!" : "GAME!", "", 2.4f, _match.EndedByKnockout ? 128 : 150,
                        _match.Colors.Neutral);
                    break;
                case MatchPhase.Judging:
                    _battleRoot.gameObject.SetActive(false);
                    _calloutRoot.gameObject.SetActive(false);
                    _judgeRoot.gameObject.SetActive(true);
                    _judgeStart = Time.time;
                    _judgeBarAlly.color = TeamPalette.ColorOf(_allyTeam);
                    _judgeBarEnemy.color = TeamPalette.ColorOf(TeamUtil.Opponent(_allyTeam));
                    _judgeVerdict.text = "";
                    break;
                case MatchPhase.Results:
                    _judgeRoot.gameObject.SetActive(false);
                    BuildResults();
                    _resultsRoot.gameObject.SetActive(true);
                    break;
            }
        }

        private void ShowCallout(string text, string sub, float duration, int fontSize, Color splatColor)
        {
            _calloutRoot.gameObject.SetActive(true);
            _calloutText.text = text;
            _calloutText.fontSize = fontSize;
            _calloutSub.text = sub;
            _calloutSplat.color = Ui.WithAlpha(splatColor, 0.85f);
            _calloutStart = Time.time;
            _calloutDuration = duration;
        }

        private void Update()
        {
            if (_match == null || _canvas == null)
            {
                return;
            }
            UpdateCallout();
            MatchPhase phase = _match.Phase;
            if (phase == MatchPhase.Judging)
            {
                UpdateJudge();
                return;
            }
            if (phase == MatchPhase.Results)
            {
                UpdateResultsInput();
                return;
            }
            UpdateTimer();
            UpdateIcons();
            UpdatePlayerWidgets();
        }

        private void UpdateCallout()
        {
            if (!_calloutRoot.gameObject.activeSelf)
            {
                return;
            }
            float t = (Time.time - _calloutStart) / Mathf.Max(0.01f, _calloutDuration);
            if (t >= 1f)
            {
                _calloutRoot.gameObject.SetActive(false);
                return;
            }
            float pop = Ui.PopScale(Mathf.Clamp01(t * 5f));
            float fade = t > 0.8f ? 1f - (t - 0.8f) / 0.2f : 1f;
            _calloutRoot.localScale = Vector3.one * pop;
            Ui.SetAlpha(_calloutText, fade);
            Ui.SetAlpha(_calloutSub, fade);
            Ui.SetAlpha(_calloutSplat, 0.85f * fade);
        }

        private void UpdateTimer()
        {
            MatchTimeline timeline = _match.Timeline;
            // KNOCKOUT 提前結束:計時停在結束那一刻(不跳 0:00)
            float remaining = timeline.Phase == MatchPhase.Battle ? timeline.BattleRemaining
                : timeline.Phase < MatchPhase.Battle ? timeline.Settings.BattleSeconds
                : timeline.EndedEarly ? timeline.BattleRemaining : 0f;
            MatchTimeline.SplitClock(remaining, out int minutes, out int seconds);
            _timer.text = $"{minutes}:{seconds:00}";
            bool lastMinute = timeline.IsLastMinute;
            _timer.color = timeline.IsFinalCountdown ? new Color(1f, 0.35f, 0.3f) : lastMinute ? new Color(1f, 0.9f, 0.25f) : Color.white;

            if (lastMinute && !_lastMinuteShown && timeline.BattleRemaining > timeline.Settings.FinalCountdownSeconds)
            {
                _lastMinuteShown = true;
                ShowCallout("剩下 1 分鐘!", "", 2f, 80, TeamPalette.ColorOf(_allyTeam));
            }
            if (timeline.IsFinalCountdown && remaining > 0f)
            {
                int second = Mathf.CeilToInt(remaining);
                if (second != _lastCountdownSecond)
                {
                    _lastCountdownSecond = second;
                    _bigCountdown.gameObject.SetActive(true);
                    _bigCountdown.text = second.ToString();
                }
                float frac = remaining - Mathf.Floor(remaining);
                _bigCountdown.transform.localScale = Vector3.one * (1f + frac * 0.35f);
                Ui.SetAlpha(_bigCountdown, 0.4f + frac * 0.6f);
            }
        }

        private void UpdateIcons()
        {
            UpdateIconGroup(_allyIcons, TeamPalette.ColorOf(_allyTeam));
            UpdateIconGroup(_enemyIcons, TeamPalette.ColorOf(TeamUtil.Opponent(_allyTeam)));
        }

        private static void UpdateIconGroup(List<IconSlot> icons, Color teamColor)
        {
            for (int i = 0; i < icons.Count; i++)
            {
                IconSlot slot = icons[i];
                if (slot.Inkling == null)
                {
                    continue;
                }
                var life = slot.Inkling.GetComponent<InklingLifecycle>();
                var special = slot.Inkling.GetComponent<PlayerSpecialGauge>();
                bool dead = life != null && life.IsDead;
                bool ready = !dead && special != null && special.IsFull && !special.IsActive;
                slot.Body.color = dead ? new Color(0.25f, 0.25f, 0.25f, 0.9f) : teamColor;
                slot.Eyes.color = dead ? new Color(0.4f, 0.4f, 0.4f, 0.9f) : Color.white;
                slot.Cross.enabled = dead;
                float pulse = ready ? 0.45f + 0.35f * Mathf.Sin(Time.time * 8f) : 0f;
                slot.Glow.color = Ui.WithAlpha(Color.Lerp(teamColor, Color.white, 0.5f), pulse);
                slot.Root.localScale = Vector3.one * (ready ? 1.08f : 1f);
            }
        }

        private void UpdatePlayerWidgets()
        {
            if (_player == null)
            {
                return;
            }
            Color ally = TeamPalette.ColorOf(_allyTeam);
            _points.text = $"{TurfPoints.Rounded(_player.TurfInkedArea):0000}p";

            float gauge = _special != null ? _special.Normalized : 0f;
            bool full = _special != null && _special.IsFull && !_special.IsActive;
            // 鎖定型大招(Big Bubbler、Wave Breaker…)放出後的自動倒數也照發動中顯示(S3:能量條發亮往下扣)
            bool active = _special != null && (_special.IsActive || _special.IsLocked);
            _gaugeFill.fillAmount = gauge;
            _gaugeFill.color = full || active ? Color.Lerp(ally, Color.white, 0.25f + 0.25f * Mathf.Sin(Time.time * 10f)) : ally;
            _gaugeGlow.color = Ui.WithAlpha(ally, full ? 0.35f + 0.25f * Mathf.Sin(Time.time * 6f) : 0f);
            string activeHint = _special != null && _special.ActiveSpecial != null ? _special.ActiveSpecial.HudHint : null;
            _gaugeHint.text = full ? "R 大招!" : active ? (activeHint ?? "發動中") : $"{Mathf.FloorToInt(gauge * 100f)}%";
            _gaugeHint.fontSize = full ? 32 : 30;

            bool dead = _life != null && _life.IsDead;
            UpdateStatusRow(dead);
            _reticleRing.enabled = !dead;
            _reticleDot.enabled = !dead;

            // 命中標記:一般命中白色小叉;擊倒放大轉紅
            float hitT = _hitMarkerUntil - Time.time;
            if (hitT > 0f)
            {
                _hitMarker.color = _hitMarkerKill ? new Color(1f, 0.25f, 0.2f, Mathf.Clamp01(hitT / 0.25f))
                    : new Color(1f, 1f, 1f, Mathf.Clamp01(hitT / 0.12f));
                _hitMarker.rectTransform.sizeDelta = Vector2.one * (_hitMarkerKill ? 64f : 40f);
            }
            else if (_hitMarker.color.a > 0f)
            {
                _hitMarker.color = new Color(1f, 1f, 1f, 0f);
            }

            float ink = _tank != null ? _tank.Normalized : 1f;
            bool submerged = _squid != null && _squid.IsSubmerged;
            bool showInk = !dead && (submerged ? ink < 0.999f : ink < 0.3f);
            _inkBarRoot.gameObject.SetActive(showInk);
            _inkBarFill.fillAmount = ink;
            _inkBarFill.color = ink < 0.15f ? new Color(1f, 0.3f, 0.25f) : ally;
            _lowInk.enabled = !dead && ink < 0.08f && Mathf.Repeat(Time.time, 0.5f) < 0.3f;

            // 受傷暗角:累積傷害 > 20 才出現,敵方墨色
            float damage = _health != null ? _health.Damage : 0f;
            float vignette = dead ? 0f : Mathf.Clamp01((damage - 20f) / 60f);
            _vignette.color = Ui.WithAlpha(TeamPalette.ColorOf(TeamUtil.Opponent(_allyTeam)), vignette * 0.85f);

            _feed.enabled = Time.time < _feedUntil;

            if (dead)
            {
                string killer = _life.Killer != null ? _life.Killer.DisplayName : null;
                _deathText.text = killer != null ? $"被 {killer} 擊倒了" : "掉落場外";
                _respawnText.text = _life.RespawnSuspended ? "" : $"{Mathf.CeilToInt(_life.RespawnRemaining)} 秒後重生";
            }
            _deathText.enabled = dead;
            _respawnText.enabled = dead;
        }

        // ───────────────────────── 狀態圖示 ─────────────────────────

        // 本機玩家的狀態列(被標記/減速/強化),左上角;來源見 StatusIconRow
        private void BuildStatusRow(Transform parent)
        {
            _statusRow = new StatusIconRow(parent, new Vector2(0f, 1f), new Vector2(36f, -36f));
        }

        private void UpdateStatusRow(bool dead)
        {
            if (_statusRow != null)
            {
                _statusRow.Update(_player, dead);
            }
        }

        // 外部推送一個狀態圖示顯示 seconds 秒(沒有對應效果/標記的臨時提示)
        public void ShowStatus(SplatoonC.Core.Combat.StatusFlags flags, float seconds)
        {
            if (_statusRow != null)
            {
                _statusRow.Push(flags, seconds);
            }
        }

        // 這一幀狀態列顯示中的狀態(測試/除錯)
        public SplatoonC.Core.Combat.StatusFlags ShownStatus =>
            _statusRow != null ? _statusRow.Shown : SplatoonC.Core.Combat.StatusFlags.None;

        private void OnDamageDealt(IDamageable target, float amount, bool splatted)
        {
            if (amount <= 0f && !splatted)
            {
                return;
            }
            _hitMarkerKill = splatted;
            _hitMarkerUntil = Time.time + (splatted ? 0.35f : 0.12f);
            if (splatted && target is InklingHealth victim)
            {
                var inkling = victim.GetComponent<Inkling>();
                _feed.text = $"擊倒了 {(inkling != null ? inkling.DisplayName : "敵人")}!";
                _feed.color = Color.white;
                _feedUntil = Time.time + 2.2f;
            }
        }

        // ───────────────────────── 判定 ─────────────────────────

        private void UpdateJudge()
        {
            if (!_match.HasVerdict)
            {
                return;
            }
            TurfVerdict verdict = _match.Verdict;
            // 蠻頹對戰:長條 = 計數進度(100 - 計數;KO = 100 抵達畫面中央),數字 = 剩餘計數
            bool ranked = _match.IsRanked;
            AnarchyResult ranking = _match.RankedResult;
            float ally = ranked ? ranking.ProgressOf(_allyTeam) : verdict.PercentOf(_allyTeam);
            float enemy = ranked ? ranking.ProgressOf(TeamUtil.Opponent(_allyTeam)) : verdict.PercentOf(TeamUtil.Opponent(_allyTeam));
            float barScale = ranked ? 0.5f : 1f;
            float t = Time.time - _judgeStart;
            // 0.6~3.2s:兩側同步增長到較小者(製造懸念);3.2~4.4s 分出高下;之後宣告
            float shared = Mathf.Min(ally, enemy);
            float grow = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 3.2f, t));
            float split = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(3.4f, 4.4f, t));
            float shownAlly = Mathf.Lerp(0f, shared, grow) + (ally - shared) * split;
            float shownEnemy = Mathf.Lerp(0f, shared, grow) + (enemy - shared) * split;
            // 兩側各 50% 時剛好在畫面正中相接
            const float screenWidth = 1920f;
            _judgeBarAlly.rectTransform.sizeDelta = new Vector2(screenWidth * shownAlly / 100f * barScale, 90f);
            _judgeBarEnemy.rectTransform.sizeDelta = new Vector2(screenWidth * shownEnemy / 100f * barScale, 90f);
            _judgePercentAlly.text = ranked ? RankedJudgeLabel(shownAlly) : $"{shownAlly:0.0}%";
            _judgePercentEnemy.text = ranked ? RankedJudgeLabel(shownEnemy) : $"{shownEnemy:0.0}%";
            bool decided = t > 4.6f;
            bool win = verdict.Winner == _allyTeam;
            if (decided && _judgeVerdict.text.Length == 0)
            {
                _judgeVerdict.text = win ? "VICTORY" : "DEFEAT";
                _judgeVerdict.color = win ? Color.Lerp(TeamPalette.ColorOf(_allyTeam), Color.white, 0.35f) : new Color(0.85f, 0.85f, 0.85f);
            }
            if (decided)
            {
                float k = Mathf.Clamp01((t - 4.6f) * 4f);
                _judgeVerdict.transform.localScale = Vector3.one * Ui.PopScale(k);
                // 勝方的貓跳起來、敗方的貓倒下
                Image winner = win ? _juddAlly : _juddEnemy;
                Image loser = win ? _juddEnemy : _juddAlly;
                winner.rectTransform.localRotation = Quaternion.identity;
                winner.rectTransform.anchoredPosition = new Vector2(0f, (win ? 200f : 190f) + Mathf.Abs(Mathf.Sin(t * 6f)) * 30f);
                loser.rectTransform.localRotation = Quaternion.Euler(0f, 0f, win ? -80f : 80f);
            }
        }

        // 蠻頹判定的數字:剩餘計數(進度 100 = KNOCKOUT)
        private static string RankedJudgeLabel(float progress)
        {
            int count = TeamScore.StartCount - Mathf.RoundToInt(progress);
            return count <= 0 ? "KO!" : $"計數 {count}";
        }

        // 結算標題下的比分:塗地 = 佔比;蠻頹 = 計數(我方在前)
        private string ScoreLine()
        {
            Team enemy = TeamUtil.Opponent(_allyTeam);
            if (!_match.IsRanked)
            {
                return $"{_match.Verdict.PercentOf(_allyTeam):0.0}%  vs  {_match.Verdict.PercentOf(enemy):0.0}%";
            }
            AnarchyResult r = _match.RankedResult;
            string tag = r.Knockout ? "KNOCKOUT!  " : r.NoProgress ? "(雙方無進度)" : r.WentToOvertime ? "(延長戰)" : "";
            string counts = $"{ObjectiveModes.DisplayName(_match.Mode)}  計數 {r.CountOf(_allyTeam)}  vs  {r.CountOf(enemy)}";
            return r.Knockout ? tag + counts : counts + tag;
        }

        // ───────────────────────── 結算 ─────────────────────────

        private void BuildResults()
        {
            if (_resultsBuilt)
            {
                return;
            }
            _resultsBuilt = true;
            var bg = _resultsRoot.gameObject.AddComponent<Image>();
            bg.color = new Color(0.04f, 0.04f, 0.06f, 0.88f);
            bg.raycastTarget = false;
            bool win = _match.HasVerdict && _match.Verdict.Winner == _allyTeam;
            Color ally = TeamPalette.ColorOf(_allyTeam);
            Color enemy = TeamPalette.ColorOf(TeamUtil.Opponent(_allyTeam));
            Ui.Image("Splat", _resultsRoot, new Vector2(0.18f, 0.5f), Vector2.zero, new Vector2(900f, 900f), UiSprites.Splat(256, 3),
                Ui.WithAlpha(win ? ally : enemy, 0.55f)).rectTransform.pivot = new Vector2(0.5f, 0.5f);
            Ui.Text("Title", _resultsRoot, new Vector2(0.05f, 0.9f), Vector2.zero, new Vector2(700f, 140f), 110,
                TextAnchor.MiddleLeft, Color.white, true, 4f).text = win ? "VICTORY" : "DEFEAT";
            if (_match.HasVerdict)
            {
                Ui.Text("Score", _resultsRoot, new Vector2(0.05f, 0.78f), Vector2.zero, new Vector2(800f, 70f), 44,
                    TextAnchor.MiddleLeft, Color.white, true, 2f).text = ScoreLine();
            }

            // 右半:兩隊成績表(依塗地點數排序)
            float top = -150f;
            top = BuildTeamTable(_allyTeam, ally, top, win);
            BuildTeamTable(TeamUtil.Opponent(_allyTeam), enemy, top - 40f, !win);
            Ui.Text("Continue", _resultsRoot, new Vector2(0.5f, 0.05f), Vector2.zero, new Vector2(1200f, 60f), 34,
                TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.9f), true, 2f).text = "按 Enter / 手把 A 繼續";
        }

        private float BuildTeamTable(Team team, Color color, float top, bool won)
        {
            var members = new List<Inkling>();
            foreach (Inkling inkling in _match.Roster)
            {
                if (inkling != null && inkling.Team == team)
                {
                    members.Add(inkling);
                }
            }
            members.Sort((a, b) => b.TurfInkedArea.CompareTo(a.TurfInkedArea));
            Vector2 anchor = new Vector2(1f, 1f);
            Ui.Image("TeamHeader" + team, _resultsRoot, anchor, new Vector2(-60f, top), new Vector2(900f, 56f),
                UiSprites.RoundedRect(14), color);
            Ui.Text("TeamTitle" + team, _resultsRoot, anchor, new Vector2(-70f, top - 4f), new Vector2(880f, 50f), 32,
                TextAnchor.MiddleLeft, Color.white, true, 2f).text =
                $"{(team == _allyTeam ? "我方" : "對手")}  {(won ? "勝利" : "落敗")}          塗地    擊倒(助攻)   陣亡   大招";
            top -= 66f;
            foreach (Inkling m in members)
            {
                bool me = m.IsLocalPlayer;
                Ui.Image("Row_" + m.DisplayName, _resultsRoot, anchor, new Vector2(-60f, top), new Vector2(900f, 52f),
                    UiSprites.RoundedRect(12), me ? new Color(1f, 1f, 1f, 0.22f) : new Color(1f, 1f, 1f, 0.08f));
                Ui.Text("Name_" + m.DisplayName, _resultsRoot, anchor, new Vector2(-620f, top - 2f), new Vector2(330f, 50f), 30,
                    TextAnchor.MiddleLeft, me ? Color.Lerp(color, Color.white, 0.5f) : Color.white, true, 1.5f).text = m.DisplayName;
                Ui.Text("Stats_" + m.DisplayName, _resultsRoot, anchor, new Vector2(-70f, top - 2f), new Vector2(540f, 50f), 30,
                    TextAnchor.MiddleRight, Color.white, true, 1.5f).text =
                    $"{TurfPoints.Rounded(m.TurfInkedArea),5}p      {m.Splats + m.Assists,2}({m.Assists})      {m.Deaths,2}      {m.SpecialsUsed,2}";
                top -= 58f;
            }
            return top;
        }

        private void UpdateResultsInput()
        {
            bool confirm = (Keyboard.current != null && (Keyboard.current.enterKey.wasPressedThisFrame
                                                         || Keyboard.current.spaceKey.wasPressedThisFrame))
                           || (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame);
            if (!confirm)
            {
                return;
            }
            _match.ConfirmResults();
            SceneFlow.AfterMatch();
        }
    }

    // 場景流程:結算後去哪裡。主選單場景存在就回主選單,否則重新開一場。
    public static class SceneFlow
    {
        public const string MenuScene = "MainMenu";

        public static void AfterMatch()
        {
            if (Application.CanStreamedLevelBeLoaded(MenuScene))
            {
                SceneManager.LoadScene(MenuScene);
                return;
            }
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex >= 0
                ? SceneManager.GetActiveScene().name
                : SceneManager.GetActiveScene().path);
        }
    }
}

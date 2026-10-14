using System.Collections.Generic;
using SplatoonC.Gameplay.Match;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SplatoonC.Gameplay.UI
{
    // 超級跳目標選單(A4;S3 的地圖選點簡化成清單 —— 完整地圖 UI 不在範圍內):
    //   按 E / 手把北鍵(Player/Interact)開關;W/S、搖桿上下、1/2、十字鍵左右換目標;空白鍵/左鍵確定(S3 按 A);
    //   清單 = 出生點、活著的隊友(距離、交戰中提示)、自家信標(剩餘次數)、自家 Big Bubbler。
    //   選單開著時角色不動(PlayerInputRouter.MenuOpen);確定後由 SuperJump 接手(蓄力/飛行期間顯示狀態列)。
    //   有隊友正跳向自己時上方提示「▼ 名字 超級跳過來了」(S3 被跳向的隊友會收到箭頭提示)。
    // 本機玩家專用、自建 overlay canvas,試射場與對戰場景都會自動建立(不需要場景接線)。
    public sealed class SuperJumpHud : MonoBehaviour
    {
        private const int MaxRows = 12;
        private const float RowHeight = 40f;

        private sealed class Row
        {
            public RectTransform Root;
            public Image Background;
            public Image Swatch;
            public Text Label;
            public Text Info;
            public int CachedDistance = -1;
            public int CachedExtra = -99;
            public Object CachedKey;
            public int CachedKind = -1;
        }

        public static SuperJumpHud Instance { get; private set; }

        private readonly List<SuperJumpTarget> _targets = new List<SuperJumpTarget>(16);
        private readonly Row[] _rows = new Row[MaxRows];
        private Canvas _canvas;
        private RectTransform _panel;
        private Text _refusal;
        private Text _hint;
        private Text _status;
        private Text _notice;
        private Inkling _player;
        private PlayerInputRouter _router;
        private SuperJump _jump;
        private InklingLifecycle _life;
        private bool _open;
        private int _selected;
        private float _lastNavY;
        private float _refusalUntil;
        private SuperJump _noticeFrom;
        private SuperJump.JumpPhase _statusPhase = (SuperJump.JumpPhase)255;
        private int _panelRows = -1;

        public bool IsOpen => _open;
        public int Selected => _selected;
        public IReadOnlyList<SuperJumpTarget> Targets => _targets;
        public string NoticeText => _notice != null && _notice.gameObject.activeSelf ? _notice.text : null;
        public string RefusalText => _refusal != null && _refusal.gameObject.activeSelf ? _refusal.text : null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            TryCreate();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            TryCreate();
        }

        // 有本機玩家(試射場)或有對戰控制(對戰場景:角色稍後才生成)才建
        private static void TryCreate()
        {
            if (Instance != null || (Inkling.Local == null && MatchController.Instance == null))
            {
                return;
            }
            new GameObject("SuperJumpHud").AddComponent<SuperJumpHud>();
        }

        private void Awake()
        {
            Instance = this;
            Build();
            SetOpen(false);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
            if (_router != null)
            {
                _router.MenuOpen = false;
            }
        }

        private void Build()
        {
            _canvas = Ui.CreateCanvas("SuperJumpHudCanvas", 12);
            _canvas.transform.SetParent(transform, false);
            Transform root = _canvas.transform;

            // 畫面中央偏左(開著時角色不動,擋住準星沒關係;不蓋到左上角的墨量/大招條),高度隨項目數伸縮
            _panel = Ui.Rect("Picker", root, new Vector2(0.5f, 0.5f), new Vector2(-380f, 10f), new Vector2(540f, PanelHeight(2)));
            var bg = _panel.gameObject.AddComponent<Image>();
            bg.sprite = UiSprites.RoundedRect();
            bg.type = Image.Type.Sliced;
            bg.color = new Color(0.05f, 0.05f, 0.08f, 0.82f);
            bg.raycastTarget = false;
            Text title = Ui.Text("Title", _panel, new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(500f, 50f), 34,
                TextAnchor.MiddleCenter, Color.white);
            title.text = "超級跳";
            for (int i = 0; i < MaxRows; i++)
            {
                var row = new Row();
                row.Root = Ui.Rect("Row" + i, _panel, new Vector2(0.5f, 1f), new Vector2(0f, -80f - i * (RowHeight + 3f)),
                    new Vector2(500f, RowHeight));
                row.Background = row.Root.gameObject.AddComponent<Image>();
                row.Background.sprite = UiSprites.RoundedRect();
                row.Background.type = Image.Type.Sliced;
                row.Background.raycastTarget = false;
                row.Swatch = Ui.Image("Swatch", row.Root, new Vector2(0f, 0.5f), new Vector2(12f, 0f),
                    new Vector2(22f, 22f), UiSprites.Circle(), Color.white);
                row.Label = Ui.Text("Label", row.Root, new Vector2(0f, 0.5f), new Vector2(46f, 0f), new Vector2(300f, 36f),
                    24, TextAnchor.MiddleLeft, Color.white, true, 1.5f);
                row.Info = Ui.Text("Info", row.Root, new Vector2(1f, 0.5f), new Vector2(-14f, 0f), new Vector2(170f, 36f),
                    20, TextAnchor.MiddleRight, new Color(0.85f, 0.88f, 0.95f), false, 1.5f);
                _rows[i] = row;
            }
            Text footer = Ui.Text("Footer", _panel, new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(520f, 34f), 18,
                TextAnchor.MiddleCenter, new Color(0.8f, 0.82f, 0.9f), false, 1.5f);
            footer.text = "W/S・1/2・十字鍵 選擇   空白鍵/左鍵(A)跳   E(Y)關閉";
            _refusal = Ui.Text("Refusal", _panel, new Vector2(0.5f, 0f), new Vector2(0f, 52f), new Vector2(520f, 34f), 22,
                TextAnchor.MiddleCenter, new Color(1f, 0.45f, 0.4f));
            _refusal.gameObject.SetActive(false);

            _hint = Ui.Text("Hint", root, new Vector2(0f, 0f), new Vector2(28f, 150f), new Vector2(300f, 32f), 20,
                TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.8f), true, 1.5f);
            _hint.text = "[E / Y] 超級跳";
            _status = Ui.Text("Status", root, new Vector2(0.5f, 0f), new Vector2(0f, 190f), new Vector2(700f, 44f), 30,
                TextAnchor.MiddleCenter, Color.white, true, 2f);
            _status.gameObject.SetActive(false);
            _notice = Ui.Text("Notice", root, new Vector2(0.5f, 1f), new Vector2(0f, -170f), new Vector2(800f, 44f), 30,
                TextAnchor.MiddleCenter, Color.white, true, 2f);
            _notice.gameObject.SetActive(false);
        }

        private void Bind()
        {
            Inkling local = Inkling.Local;
            if (local == _player)
            {
                return;
            }
            if (_router != null)
            {
                _router.MenuOpen = false;
            }
            _player = local;
            _router = local != null ? local.GetComponent<PlayerInputRouter>() : null;
            _jump = local != null ? local.GetComponent<SuperJump>() : null;
            _life = local != null ? local.GetComponent<InklingLifecycle>() : null;
            SetOpen(false);
        }

        private void Update()
        {
            Bind();
            if (_player == null || _router == null || _jump == null)
            {
                SetOpen(false);
                SetActive(_hint, false);
                SetActive(_status, false);
                SetActive(_notice, false);
                return;
            }
            bool alive = _life == null || !_life.IsDead;
            MatchController match = MatchController.Instance;
            bool canAct = alive && (match == null || match.PlayersCanAct);

            if (_router.MapPressedThisFrame)
            {
                if (_open)
                {
                    SetOpen(false);
                }
                else if (canAct && !_jump.IsBusy)
                {
                    SetOpen(true);
                    _selected = 0;
                }
            }
            if (_open && (!canAct || _jump.IsBusy))
            {
                SetOpen(false);
            }
            if (_open)
            {
                RunPicker();
            }
            _router.MenuOpen = _open;

            SetActive(_hint, !_open && canAct && !_jump.IsBusy && _player.IsLocalPlayer);
            UpdateStatus();
            UpdateNotice();
            if (_refusal.gameObject.activeSelf && Time.unscaledTime > _refusalUntil)
            {
                _refusal.gameObject.SetActive(false);
            }
        }

        private void RunPicker()
        {
            int count = SuperJumpTargets.Collect(_player, _targets);
            if (count == 0)
            {
                SetOpen(false);
                return;
            }
            int step = _router.MenuStepThisFrame;
            float navY = _router.MenuNavigate.y;
            if (navY > 0.5f && _lastNavY <= 0.5f)
            {
                step--;
            }
            else if (navY < -0.5f && _lastNavY >= -0.5f)
            {
                step++;
            }
            _lastNavY = navY;
            _selected = ((_selected + step) % count + count) % count;
            if (_router.MenuConfirmPressedThisFrame)
            {
                if (_jump.TryBegin(_targets[_selected]))
                {
                    SetOpen(false);
                    return;
                }
                _refusal.text = "無法超級跳:" + _jump.LastRefusal;
                _refusal.gameObject.SetActive(true);
                _refusalUntil = Time.unscaledTime + 1.6f;
            }
            RefreshRows(count);
        }

        private static float PanelHeight(int rows)
        {
            // 標題 80 + 每列 43 + 底部(拒絕原因 + 操作說明)100
            return 80f + Mathf.Clamp(rows, 1, MaxRows) * (RowHeight + 3f) + 100f;
        }

        private void RefreshRows(int count)
        {
            if (count != _panelRows)
            {
                _panelRows = count;
                _panel.sizeDelta = new Vector2(540f, PanelHeight(count));
            }
            Vector3 me = _player.transform.position;
            Color team = TeamPalette.ColorOf(_player.Team);
            Color selectedBack = Color.Lerp(team, Color.white, 0.25f);
            selectedBack.a = 0.5f;
            for (int i = 0; i < MaxRows; i++)
            {
                Row row = _rows[i];
                bool used = i < count;
                SetActive(row.Root, used);
                if (!used)
                {
                    continue;
                }
                SuperJumpTarget target = _targets[i];
                bool selected = i == _selected;
                row.Background.color = selected ? selectedBack : new Color(1f, 1f, 1f, 0.06f);
                row.Swatch.color = SwatchColor(target);
                Object key = target.Teammate != null ? target.Teammate : target.Beakon != null
                    ? target.Beakon : (Object)target.Bubbler;
                Vector3 anchor = target.Kind == SuperJumpTargetKind.Spawn && _life != null
                    ? _life.SpawnPosition
                    : target.AnchorPosition;
                int distance = Mathf.RoundToInt(Vector3.Distance(me, anchor));
                int extra = Extra(target);
                if (row.CachedKey != key || row.CachedKind != (int)target.Kind)
                {
                    row.CachedKey = key;
                    row.CachedKind = (int)target.Kind;
                    row.Label.text = target.Label;
                    row.CachedDistance = -1;
                }
                if (row.CachedDistance != distance || row.CachedExtra != extra)
                {
                    row.CachedDistance = distance;
                    row.CachedExtra = extra;
                    row.Info.text = InfoText(target, distance, extra);
                }
                row.Label.color = selected ? Color.white : new Color(0.9f, 0.9f, 0.92f);
            }
        }

        // 隊友:1 = 交戰中(最近 3 秒內受傷);信標:剩餘次數
        private static int Extra(in SuperJumpTarget target)
        {
            switch (target.Kind)
            {
                case SuperJumpTargetKind.Teammate:
                {
                    var health = target.Teammate != null ? target.Teammate.GetComponent<InklingHealth>() : null;
                    return health != null && Time.time - health.LastDamageTime < 3f ? 1 : 0;
                }
                case SuperJumpTargetKind.Beakon:
                    return target.Beakon != null ? target.Beakon.UsesLeft : 0;
                default:
                    return 0;
            }
        }

        private static string InfoText(in SuperJumpTarget target, int distance, int extra)
        {
            switch (target.Kind)
            {
                case SuperJumpTargetKind.Teammate:
                    return extra == 1 ? $"交戰中  {distance} m" : $"{distance} m";
                case SuperJumpTargetKind.Beakon:
                    return $"剩 {extra} 次  {distance} m";
                default:
                    return $"{distance} m";
            }
        }

        private Color SwatchColor(in SuperJumpTarget target)
        {
            Color team = TeamPalette.ColorOf(_player.Team);
            switch (target.Kind)
            {
                case SuperJumpTargetKind.Spawn:
                    return Color.Lerp(team, Color.white, 0.6f);
                case SuperJumpTargetKind.Beakon:
                case SuperJumpTargetKind.Bubbler:
                    return Color.Lerp(team, Color.black, 0.25f);
                default:
                    return team;
            }
        }

        // 自己超級跳中的狀態列(只在階段切換時改字)
        private void UpdateStatus()
        {
            SuperJump.JumpPhase phase = _jump.Phase;
            if (phase == _statusPhase)
            {
                return;
            }
            _statusPhase = phase;
            switch (phase)
            {
                case SuperJump.JumpPhase.Charging:
                    _status.text = "超級跳準備中… → " + _jump.Target.Label;
                    SetActive(_status, true);
                    break;
                case SuperJump.JumpPhase.Flight:
                    _status.text = "超級跳!";
                    SetActive(_status, true);
                    break;
                default:
                    SetActive(_status, false);
                    break;
            }
        }

        // 有隊友正跳向我(S3 被選為目標的隊友會收到提示)
        private void UpdateNotice()
        {
            SuperJump from = null;
            var all = SuperJump.All;
            for (int i = 0; i < all.Count; i++)
            {
                SuperJump sj = all[i];
                if (sj != null && sj != _jump && sj.IsBusy && sj.Target.Kind == SuperJumpTargetKind.Teammate
                    && sj.Target.Teammate == _player)
                {
                    from = sj;
                    break;
                }
            }
            if (from != _noticeFrom)
            {
                _noticeFrom = from;
                if (from != null)
                {
                    var jumper = from.GetComponent<Inkling>();
                    _notice.text = "▼ " + (jumper != null ? jumper.DisplayName : "隊友") + " 超級跳過來了";
                    _notice.color = Color.Lerp(TeamPalette.ColorOf(_player.Team), Color.white, 0.4f);
                }
            }
            SetActive(_notice, from != null);
        }

        private void SetOpen(bool open)
        {
            _open = open;
            if (_panel != null && _panel.gameObject.activeSelf != open)
            {
                _panel.gameObject.SetActive(open);
            }
            // 開啟那一刻的搖桿位置當基準(按著 W 開選單不會立刻跳一格)
            _lastNavY = open && _router != null ? _router.MenuNavigate.y : 0f;
            if (_router != null)
            {
                _router.MenuOpen = open;
            }
        }

        private static void SetActive(Component component, bool active)
        {
            if (component != null && component.gameObject.activeSelf != active)
            {
                component.gameObject.SetActive(active);
            }
        }
    }
}

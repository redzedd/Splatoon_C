using System;
using System.Collections.Generic;
using SplatoonC.Core;
using SplatoonC.Core.Match;
using SplatoonC.Core.Match.Ranked;
using SplatoonC.Gameplay.CameraRig;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Match
{
    // 一場對戰的總管:配色、清場、生成 4v4 名單、推進時間軸、鎖/解輸入、判定、結算資料。
    // 名單中 bot 的 AI 由 BotDirector 經 RosterSpawned 掛上(本類不直接依賴 AI,方便單獨測流程)。
    // 執行順序提前:HUD、演出鏡頭、bot 總管的 Start 都要看得到已生成的名單與本機玩家。
    [DefaultExecutionOrder(-100)]
    public sealed class MatchController : MonoBehaviour
    {
        public static MatchController Instance { get; private set; }

        [SerializeField, Tooltip("角色 prefab(Assets/Prefabs/Player.prefab;根物件需有 Inkling)")]
        private Inkling _inklingPrefab;

        [SerializeField, Tooltip("每隊人數")]
        private int _teamSize = 4;

        [SerializeField, Tooltip("本機玩家所屬隊伍")]
        private Team _localTeam = Team.Alpha;

        [SerializeField, Tooltip("是否產生本機玩家(關閉 = 8 個 bot 觀戰,除錯用)")]
        private bool _spawnLocalPlayer = true;

        [SerializeField, Tooltip("隨機挑選 S3 配色(關閉 = 固定 OrangeBlue)")]
        private bool _randomColors = true;

        [SerializeField, Tooltip("時間軸(塗地:開場 4s、倒數 2.2s、對戰 180s、GAME 2.5s、判定 7s)")]
        private MatchTimelineSettings _timelineSettings = MatchTimelineSettings.TurfWar;

        [SerializeField, Tooltip("本機玩家顯示名稱")]
        private string _localName = "你";

        private static readonly string[] AlphaBotNames = { "Callie", "Marie", "Pearl", "Marina", "Shiver", "Frye", "Big Man" };
        private static readonly string[] BravoBotNames = { "Agent 3", "Agent 4", "Agent 8", "Cap'n", "Sheldon", "Judd", "Annie" };

        private readonly List<Inkling> _roster = new List<Inkling>();
        private MatchTimeline _timeline;
        private int _effectiveTeamSize;
        private MatchPhase _lastPhase = (MatchPhase)(-1);

        public MatchTimeline Timeline => _timeline;
        public MatchPhase Phase => _timeline != null ? _timeline.Phase : MatchPhase.Intro;
        public IReadOnlyList<Inkling> Roster => _roster;
        public Inkling LocalPlayer { get; private set; }
        public InkColorSet Colors { get; private set; }
        public TurfVerdict Verdict { get; private set; }
        public bool HasVerdict { get; private set; }

        // 本場模式(主選單 MatchSettings.Mode;場地沒有該模式的目標配置時退回塗地)
        public MatchMode Mode { get; private set; } = MatchMode.TurfWar;

        // 蠻頹對戰的模式總管(塗地 = null)
        public ObjectiveModeController Objective { get; private set; }

        public bool IsRanked => Objective != null;

        // 蠻頹對戰的結果(判定時決定;Verdict.Winner 與它一致)
        public AnarchyResult RankedResult { get; private set; }

        // 對戰以 KNOCKOUT 提前結束
        public bool EndedByKnockout { get; private set; }
        public bool PlayersCanAct => _timeline != null && _timeline.PlayersCanAct;
        // 實際每隊人數(主選單可改 1~4,見 MatchSettings.TeamSize)
        public int TeamSize => _effectiveTeamSize > 0 ? _effectiveTeamSize : _teamSize;

        // 進入新階段(時間軸)
        public event Action<MatchPhase> PhaseChanged;

        // 名單生成完畢(bot AI 在此掛載)
        public event Action<IReadOnlyList<Inkling>> RosterSpawned;

        // 由外部(選單)在開賽前指定玩家要用的設定;null = 用 prefab 預設
        public static Action<Inkling> ConfigureLocalPlayer { get; set; }

        private void Awake()
        {
            Instance = this;
            _effectiveTeamSize = MatchSettings.ResolveTeamSize(_teamSize);
            // 蠻頹對戰:模式總管 + 5:00 時間軸 + 加時閘門;塗地(預設、直接開場景)完全照舊
            Objective = ObjectiveModeController.TryCreate(this, MatchSettings.Mode, gameObject.scene.name);
            Mode = Objective != null ? Objective.Mode : MatchMode.TurfWar;
            _timeline = new MatchTimeline(Objective != null ? MatchTimelineSettings.Anarchy(_timelineSettings) : _timelineSettings);
            if (Objective != null)
            {
                _timeline.OvertimeGate = Objective;
            }
            // 對戰流程的聲音與音樂(觀察 PhaseChanged,不影響流程)
            if (GetComponent<Audio.MatchAudio>() == null)
            {
                gameObject.AddComponent<Audio.MatchAudio>();
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Start()
        {
            // 色彩鎖定(S3 選項,主選單設定頁):不隨機,固定黃 vs 藍紫
            Colors = MatchSettings.ColorLock ? InkColorSets.ColorLock
                : _randomColors ? InkColorSets.Pick(UnityEngine.Random.Range(0, InkColorSets.All.Length))
                : InkColorSets.Default;
            TeamPalette.Set(Colors.Alpha, Colors.Bravo);
            TurfJudge.ClearAll();
            if (Objective != null && !Objective.OnMatchStart())
            {
                // 目標生成失敗(場地資料錯誤):退回塗地
                _timeline.OvertimeGate = null;
                Destroy(Objective);
                Objective = null;
                Mode = MatchMode.TurfWar;
            }
            SpawnRoster();
            SetInputLock(true);
            EnterPhase(_timeline.Phase);
        }

        private void SpawnRoster()
        {
            if (_inklingPrefab == null)
            {
                Debug.LogError("MatchController:缺少角色 prefab,無法開賽", this);
                return;
            }
            var used = new HashSet<string>();
            SpawnTeam(Team.Alpha, used);
            SpawnTeam(Team.Bravo, used);
            RosterSpawned?.Invoke(_roster);
        }

        private void SpawnTeam(Team team, HashSet<string> usedNames)
        {
            TeamBase teamBase = TeamBase.Of(team);
            if (teamBase == null)
            {
                Debug.LogError($"MatchController:場景缺少 {team} 隊的 TeamBase", this);
                return;
            }
            string[] names = team == Team.Alpha ? AlphaBotNames : BravoBotNames;
            for (int slot = 0; slot < TeamSize; slot++)
            {
                bool local = _spawnLocalPlayer && team == _localTeam && slot == 0;
                Transform spawn = teamBase.GetSpawn(slot);
                Inkling inkling = Instantiate(_inklingPrefab, spawn.position, spawn.rotation);
                string displayName = local ? _localName : PickName(names, slot, usedNames);
                inkling.name = local ? "Player" : $"{team}_{slot}_{displayName}";
                inkling.Configure(team, displayName, local);
                inkling.ResetMatchStats();
                var lifecycle = inkling.GetComponent<InklingLifecycle>();
                if (lifecycle != null)
                {
                    lifecycle.SpawnSlot = slot;
                }
                if (local)
                {
                    LocalPlayer = inkling;
                    EquipMenuKit(inkling);
                    ConfigureLocalPlayer?.Invoke(inkling);
                    BindCamera(inkling);
                }
                _roster.Add(inkling);
            }
        }

        // 主選單選的武器(沒選就保留 prefab 預設)
        private static void EquipMenuKit(Inkling player)
        {
            if (MatchSettings.LocalKit == null || !MatchSettings.LocalKit.IsValid)
            {
                return;
            }
            var loadout = player.GetComponent<Combat.WeaponLoadout>();
            if (loadout != null)
            {
                loadout.Equip(MatchSettings.LocalKit);
            }
        }

        private static string PickName(string[] names, int slot, HashSet<string> used)
        {
            for (int i = 0; i < names.Length; i++)
            {
                string candidate = names[(slot + i) % names.Length];
                if (used.Add(candidate))
                {
                    return candidate;
                }
            }
            return "Bot" + slot;
        }

        private void BindCamera(Inkling player)
        {
            Camera cam = Camera.main;
            var rig = cam != null ? cam.GetComponent<ThirdPersonCameraRig>() : null;
            if (rig == null)
            {
                Debug.LogError("MatchController:Main Camera 缺少 ThirdPersonCameraRig", this);
                return;
            }
            Transform pivot = player.transform.Find("CameraPivot");
            rig.SetTarget(pivot != null ? pivot : player.transform, player.GetComponent<PlayerInputRouter>());
            rig.SetAngles(player.transform.eulerAngles.y, 12f);
            player.AimView = rig;
        }

        private void Update()
        {
            if (_timeline == null)
            {
                return;
            }
            _timeline.Tick(Time.deltaTime);
            if (_timeline.Phase != _lastPhase)
            {
                EnterPhase(_timeline.Phase);
            }
        }

        private void EnterPhase(MatchPhase phase)
        {
            _lastPhase = phase;
            switch (phase)
            {
                case MatchPhase.Battle:
                    SetInputLock(false);
                    break;
                case MatchPhase.Finish:
                    SetInputLock(true);
                    SetRespawnSuspended(true);
                    break;
                case MatchPhase.Judging:
                    if (Objective != null)
                    {
                        // 蠻頹:勝負看計數(塗地佔比只當參考資料)
                        RankedResult = Objective.Resolve();
                        TurfVerdict turf = TurfJudge.Judge();
                        Verdict = new TurfVerdict(turf.AlphaPercent, turf.BravoPercent, RankedResult.Winner);
                        HasVerdict = true;
                        Debug.Log($"[MATCH] 判定({Mode})計數 Alpha {RankedResult.AlphaCount} vs Bravo {RankedResult.BravoCount} " +
                                  $"勝方={RankedResult.Winner}{(RankedResult.Knockout ? " KNOCKOUT" : "")}" +
                                  $"{(RankedResult.NoProgress ? " 無進度" : "")}{(RankedResult.WentToOvertime ? " 延長戰" : "")}");
                        break;
                    }
                    Verdict = TurfJudge.Judge();
                    HasVerdict = true;
                    Debug.Log($"[MATCH] 判定 Alpha {Verdict.AlphaPercent:0.0}% vs Bravo {Verdict.BravoPercent:0.0}% 勝方={Verdict.Winner}");
                    break;
            }
            PhaseChanged?.Invoke(phase);
        }

        private void SetInputLock(bool locked)
        {
            for (int i = 0; i < _roster.Count; i++)
            {
                if (_roster[i] == null)
                {
                    continue;
                }
                var router = _roster[i].GetComponent<PlayerInputRouter>();
                if (router != null)
                {
                    router.InputLocked = locked;
                }
            }
        }

        private void SetRespawnSuspended(bool suspended)
        {
            for (int i = 0; i < _roster.Count; i++)
            {
                var lifecycle = _roster[i] != null ? _roster[i].GetComponent<InklingLifecycle>() : null;
                if (lifecycle != null)
                {
                    lifecycle.RespawnSuspended = suspended;
                }
            }
        }

        // 蠻頹對戰計數歸 0:對戰立刻結束(KNOCKOUT!)
        public void EndBattleByKnockout()
        {
            if (_timeline == null || !_timeline.EndBattle())
            {
                return;
            }
            EndedByKnockout = true;
            EnterPhase(_timeline.Phase);
        }

        // 結算畫面確認
        public void ConfirmResults()
        {
            _timeline.Finish();
        }

        // 某隊目前(即時)的地面佔比——HUD 小地圖/除錯用;判定以 Verdict 為準
        public static float LiveTurfPercent(Team team)
        {
            TurfJudge.Tally tally = TurfJudge.Count();
            return tally.Total > 0f ? tally.Of(team) / tally.Total * 100f : 0f;
        }

        // 除錯:把對戰時間快轉到剩 seconds 秒
        public void DebugSetRemaining(float seconds)
        {
            if (_timeline.Phase != MatchPhase.Battle)
            {
                _timeline.SkipTo(MatchPhase.Battle, seconds);
                _lastPhase = MatchPhase.Battle;
                SetInputLock(false);
                PhaseChanged?.Invoke(MatchPhase.Battle);
                return;
            }
            _timeline.SkipTo(MatchPhase.Battle, seconds);
        }
    }
}

using System;
using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Core.Match;
using SplatoonC.Core.Match.Ranked;
using SplatoonC.Gameplay.AI;
using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Match
{
    // 蠻頹對戰的模式總管(MatchController 在選了真格模式且場地有資料時自動掛上;塗地時完全不存在):
    //   - 依 ModeLayout 生成目標物件(IObjectiveDriver),對戰中以 60 Hz 固定步推進純規則(與幀率無關)
    //   - 當 MatchTimeline 的加時閘門:時間到問規則要不要加時、加時中問何時結束
    //   - 計數歸 0 → 通知 MatchController 以 KNOCKOUT 立即結束
    //   - 被動大招充能(§6.4:區域 敵方控制全部 4.5 p/s、無人控制且落後 1.5 p/s)
    //   - 掛上 ObjectiveHud(計數、罰分、區域狀態、加時框、播報)與 bot 目標建議
    [DisallowMultipleComponent]
    public sealed class ObjectiveModeController : MonoBehaviour, IOvertimeGate
    {
        public static ObjectiveModeController Instance { get; private set; }

        private MatchController _match;
        private ModeLayout _layout;
        private IObjectiveDriver _driver;
        private IObjectiveMode _rules;
        private FixedStepClock _clock;
        private bool _started;
        private bool _knockoutSent;
        private AudioLoop _alarm;
        // 被動充能對象(名單生成後快取,不每幀 GetComponent)
        private Inkling[] _members;
        private PlayerSpecialGauge[] _gauges;
        private InklingLifecycle[] _lives;

        public MatchMode Mode { get; private set; }
        public ModeLayout Layout => _layout;
        public IObjectiveDriver Driver => _driver;
        public IObjectiveMode Rules => _rules;

        // 真格區域專用(其他模式為 null)
        public SplatZonesDriver SplatZones => _driver as SplatZonesDriver;

        // 本次對戰累計推進的規則幀數
        public int StepsRun { get; private set; }

        // 加時開始(HUD 播報、音效)
        public event Action OvertimeStarted;

        // 計數歸 0(參數 = KO 勝方)
        public event Action<Team> KnockedOut;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
        }

        // MatchController.Awake 呼叫:模式是蠻頹、有實作、場地有資料 → 建立並回傳;否則 null(照常塗地)
        public static ObjectiveModeController TryCreate(MatchController match, MatchMode mode, string sceneName)
        {
            if (!MatchModes.IsRanked(mode))
            {
                return null;
            }
            if (!ObjectiveModes.IsImplemented(mode))
            {
                Debug.LogWarning($"[MODE] 模式 {mode} 尚未實作,改為塗地對戰");
                return null;
            }
            ModeLayout layout = ModeLayout.Load(sceneName);
            if (layout == null || !layout.Supports(mode))
            {
                Debug.LogWarning($"[MODE] 場地 {sceneName} 沒有 {mode} 的目標配置(Resources/{ModeLayout.ResourceFolder}),改為塗地對戰");
                return null;
            }
            var controller = match.gameObject.AddComponent<ObjectiveModeController>();
            controller.Initialize(match, mode, layout);
            return controller;
        }

        private void Initialize(MatchController match, MatchMode mode, ModeLayout layout)
        {
            _match = match;
            Mode = mode;
            _layout = layout;
            _driver = CreateDriver(mode);
            _rules = _driver != null ? _driver.Rules : null;
        }

        private static IObjectiveDriver CreateDriver(MatchMode mode)
        {
            switch (mode)
            {
                case MatchMode.SplatZones:
                    return new SplatZonesDriver();
                default:
                    return null;
            }
        }

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
            StopAlarm();
            if (_driver != null)
            {
                _driver.Dispose();
            }
            if (BotDirector.Instance != null && ReferenceEquals(BotDirector.Instance.Objective, _driver))
            {
                BotDirector.Instance.Objective = null;
            }
        }

        // MatchController.Start 在清場(排除被壓住的地面格)之後呼叫:生成目標、HUD、bot 建議
        public bool OnMatchStart()
        {
            if (_started)
            {
                return _rules != null;
            }
            _started = true;
            var root = new GameObject("Objectives").transform;
            root.SetParent(transform, false);
            if (_driver == null || !_driver.Build(_layout, root, ObjectiveAssets.Load()))
            {
                Debug.LogError($"[MODE] {Mode} 目標生成失敗");
                return false;
            }
            _rules = _driver.Rules;
            if (_driver is IBotObjective advisor && BotDirector.Instance != null)
            {
                BotDirector.Instance.Objective = advisor;
            }
            if (GetComponent<UI.ObjectiveHud>() == null)
            {
                gameObject.AddComponent<UI.ObjectiveHud>();
            }
            Debug.Log($"[MODE] {Mode} 開賽:5:00 + 加時");
            return true;
        }

        private void Update()
        {
            if (_rules == null || _match == null)
            {
                return;
            }
            if (_match.Phase != MatchPhase.Battle)
            {
                _clock.Reset();
                StopAlarm();
                return;
            }
            _driver.BeforeStep();
            int steps = _clock.Advance(Time.deltaTime);
            for (int i = 0; i < steps && _rules.KnockoutTeam == Team.None; i++)
            {
                _rules.Step();
                StepsRun++;
            }
            _driver.AfterStep();
            if (_rules.KnockoutTeam != Team.None)
            {
                SendKnockout();
                return;
            }
            ChargeSpecials(Time.deltaTime);
            UpdateAlarm();
        }

        private void SendKnockout()
        {
            if (_knockoutSent)
            {
                return;
            }
            _knockoutSent = true;
            StopAlarm();
            Team winner = _rules.KnockoutTeam;
            Debug.Log($"[MODE] KNOCKOUT! 勝方={winner} Alpha {_rules.Alpha.Count} vs Bravo {_rules.Bravo.Count}");
            KnockedOut?.Invoke(winner);
            _match.EndBattleByKnockout();
        }

        // §6.4 被動大招充能(只給活著的角色;大招啟動中本來就不充能)
        private void ChargeSpecials(float dt)
        {
            if (dt <= 0f)
            {
                return;
            }
            float alphaRate = _rules.PassiveChargeRate(Team.Alpha);
            float bravoRate = _rules.PassiveChargeRate(Team.Bravo);
            if (alphaRate <= 0f && bravoRate <= 0f)
            {
                return;
            }
            if (_members == null || _members.Length != _match.Roster.Count)
            {
                CacheMembers();
            }
            for (int i = 0; i < _members.Length; i++)
            {
                Inkling inkling = _members[i];
                if (inkling == null || _gauges[i] == null)
                {
                    continue;
                }
                float rate = inkling.Team == Team.Alpha ? alphaRate : inkling.Team == Team.Bravo ? bravoRate : 0f;
                if (rate <= 0f || (_lives[i] != null && _lives[i].IsDead))
                {
                    continue;
                }
                _gauges[i].ChargePassive(rate * dt);
            }
        }

        private void CacheMembers()
        {
            var roster = _match.Roster;
            _members = new Inkling[roster.Count];
            _gauges = new PlayerSpecialGauge[roster.Count];
            _lives = new InklingLifecycle[roster.Count];
            for (int i = 0; i < roster.Count; i++)
            {
                _members[i] = roster[i];
                if (roster[i] != null)
                {
                    _gauges[i] = roster[i].GetComponent<PlayerSpecialGauge>();
                    _lives[i] = roster[i].GetComponent<InklingLifecycle>();
                }
            }
        }

        // 加時中落後方持有目標:警報聲(S3:領先方要趕快阻止)
        private void UpdateAlarm()
        {
            bool want = _rules.InOvertime && _rules.ObjectiveHolder == _rules.OvertimeLoser && _rules.OvertimeLoser != Team.None;
            if (want && _alarm == null)
            {
                _alarm = GameAudio.StartLoop("overtime_alarm", null, false, 0.6f);
            }
            else if (!want)
            {
                StopAlarm();
            }
        }

        private void StopAlarm()
        {
            if (_alarm != null)
            {
                _alarm.Stop();
                _alarm = null;
            }
        }

        // ───────── IOvertimeGate ─────────

        public bool TryStartOvertime()
        {
            if (_rules == null || !_rules.IsOvertimeTriggered())
            {
                return false;
            }
            _rules.BeginOvertime();
            Debug.Log($"[MODE] 延長戰!落後方={_rules.OvertimeLoser} Alpha {_rules.Alpha.Count} vs Bravo {_rules.Bravo.Count}");
            GameAudio.Play2D("overtime_start");
            OvertimeStarted?.Invoke();
            return true;
        }

        public bool IsOvertimeOver()
        {
            return _rules == null || _rules.IsOvertimeOver();
        }

        public AnarchyResult Resolve()
        {
            return _rules != null ? _rules.Resolve() : AnarchyRules.Resolve(new TeamScore(Team.Alpha), new TeamScore(Team.Bravo), Team.None, false);
        }
    }
}

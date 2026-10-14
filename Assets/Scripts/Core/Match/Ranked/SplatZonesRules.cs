using System;

namespace SplatoonC.Core.Match.Ranked
{
    // 真格區域(Splat Zones)純規則(DESIGN_REFERENCE §6.5;research/05 §3.1;jpwiki ガチエリア):
    //   - 1 或 2 個區域;任一隊塗到 ≥ 70% 就佔領,整個區域立刻重塗成該隊顏色(這裡把墨量設成 100%,執行期負責實際重塗)
    //   - 佔領中的區域被敵墨塗到 ≥ 50% → 變回中立(計數停止);2 個區域時要兩個都佔才計數,任一個中立就停
    //   - 控制全部區域的隊伍每 36F 計數 −1(100 計數 = 連續控制 60 秒);歸 0 = KNOCKOUT
    //   - 罰分:只有「被敵方佔領」時給(變中立不給),penalty += ROUND(0.75·(start − end)) + (start == 100 ? 1 : 0),
    //     start / end = 被罰方「開始控制時」與「被奪走時」的含罰分計數;重新控制時先扣罰分,
    //     最後 1 點罰分歸 0 的同一刻本計數也減 1(jpwiki:40+2 → 40+1 → 39)
    //   - 平手迴避:後追平的一方失去控制時計數 +1(Inkipedia Overtime 註)
    //   - 加時(時間到的那一刻,落後方):控制全部區域,或失去控制未滿 10 秒且領先方尚未奪回 → 加時;
    //     結束:落後方反超 / 領先方控制全部區域 / 落後方連續 10 秒沒有控制 / 加時滿 5:00
    //   - 被動大招充能:敵方控制全部區域 4.5 p/s;無人完全控制且我方落後 1.5 p/s
    // 每隊的「本計數進度」(36F 內已累積幾幀)跨控制權保留——對應 S3 連續計數的小數部分。
    public sealed class SplatZonesRules : IObjectiveMode
    {
        // S1/S2 datamine 70%;S3 jpwiki「7割ほど」
        public const float CaptureRatio = 0.70f;
        // S2/S3 50%(S1 40%);jpwiki「5割塗り返す」
        public const float NeutralizeRatio = 0.50f;
        // 每 36F 減 1(0.6 秒)
        public const int FramesPerCount = 36;
        // 加時:落後方失去控制後的寬限 10 秒(600F)
        public const int OvertimeGraceFrames = 600;
        // 加時上限 5:00(18000F)
        public const int OvertimeCapFrames = 18000;
        // §6.4 被動大招充能
        public const float EnemyControlChargePerSecond = 4.5f;
        public const float BehindChargePerSecond = 1.5f;

        // 比例比較容差(網格面積加總的浮點誤差)
        private const float RatioEpsilon = 1e-5f;

        private readonly Team[] _owners;
        private readonly float[] _alphaInk;
        private readonly float[] _bravoInk;
        // 依 Team 值索引(0 不用)
        private readonly int[] _tickFrames = new int[3];
        private readonly int[] _lostControlFrame = { -1, -1, -1 };

        private int _frame;
        private Team _controller = Team.None;
        private Team _lastController = Team.None;
        private int _holdStart = TeamScore.StartCount;
        private Team _knockout = Team.None;
        private bool _inOvertime;
        private bool _wentToOvertime;
        private Team _overtimeLoser = Team.None;
        private int _overtimeFrames;
        private int _overtimeAbsentFrames;

        public SplatZonesRules(int zoneCount)
        {
            int n = Math.Max(1, zoneCount);
            _owners = new Team[n];
            _alphaInk = new float[n];
            _bravoInk = new float[n];
            Alpha = new TeamScore(Team.Alpha);
            Bravo = new TeamScore(Team.Bravo);
        }

        // (區域, 佔領隊伍):執行期據此把區域瞬間重塗
        public event Action<int, Team> ZoneCaptured;

        // (區域, 原本的佔領隊伍)
        public event Action<int, Team> ZoneNeutralized;

        // (新的完全控制隊伍或 None, 原本的)
        public event Action<Team, Team> ControlChanged;

        // (被罰的隊伍, 這次加的罰分)
        public event Action<Team, int> PenaltyAdded;

        // 某隊本計數減 1
        public event Action<Team> CountChanged;

        public event Action<Team> KnockedOut;

        public MatchMode Mode => MatchMode.SplatZones;
        public TeamScore Alpha { get; }
        public TeamScore Bravo { get; }
        public int Frame => _frame;
        public int ZoneCount => _owners.Length;

        // 控制「全部」區域的隊伍
        public Team Controller => _controller;

        // 最後一個完全控制過的隊伍(罰分歸屬)
        public Team LastController => _lastController;

        public Team ObjectiveHolder => _controller;
        public Team KnockoutTeam => _knockout;
        public bool InOvertime => _inOvertime;
        public bool WentToOvertime => _wentToOvertime;
        public Team OvertimeLoser => _overtimeLoser;
        public int OvertimeFrames => _overtimeFrames;
        public int OvertimeAbsentFrames => _overtimeAbsentFrames;

        public float OvertimeGrace01
        {
            get
            {
                if (!_inOvertime)
                {
                    return 0f;
                }
                if (_controller == _overtimeLoser)
                {
                    return 1f;
                }
                return Math.Max(0f, 1f - _overtimeAbsentFrames / (float)OvertimeGraceFrames);
            }
        }

        public TeamScore ScoreOf(Team team)
        {
            return team == Team.Bravo ? Bravo : Alpha;
        }

        public Team OwnerOf(int zone)
        {
            return _owners[zone];
        }

        public float InkOf(int zone, Team team)
        {
            return team == Team.Alpha ? _alphaInk[zone] : team == Team.Bravo ? _bravoInk[zone] : 0f;
        }

        // 本計數目前這一格已累積的幀數(0~35)
        public int TickProgress(Team team)
        {
            return team == Team.None ? 0 : _tickFrames[(int)team];
        }

        // 某隊最近一次失去完全控制的幀(-1 = 從未)
        public int LostControlFrame(Team team)
        {
            return team == Team.None ? -1 : _lostControlFrame[(int)team];
        }

        // 每步之前由執行期寫入各區域的墨量比例(地面投影面積佔比 0~1)
        public void SetZoneInk(int zone, float alphaRatio, float bravoRatio)
        {
            _alphaInk[zone] = Clamp01(alphaRatio);
            _bravoInk[zone] = Clamp01(bravoRatio);
        }

        // 罰分公式(Inkipedia):ROUND(0.75 × (start − end)) + (start == 100 ? 1 : 0),四捨五入取遠離 0
        //(100→70 = 22.5 → 23 + 1 = 24;100→50 = 37.5 → 38 + 1 = 39;94→81 = 9.75 → 10)
        public static int PenaltyFor(int start, int end)
        {
            int progressed = Math.Max(0, start - end);
            int penalty = (int)Math.Round(0.75 * progressed, MidpointRounding.AwayFromZero);
            if (start == TeamScore.StartCount)
            {
                penalty += 1;
            }
            return penalty;
        }

        public void Step()
        {
            if (_knockout != Team.None)
            {
                return;
            }
            _frame++;
            for (int z = 0; z < _owners.Length; z++)
            {
                UpdateZone(z);
            }
            Team controller = ComputeController();
            if (controller != _controller)
            {
                ChangeControl(controller);
            }
            if (_controller != Team.None)
            {
                AdvanceCount(_controller);
                if (_knockout != Team.None)
                {
                    return;
                }
            }
            if (_inOvertime)
            {
                _overtimeFrames++;
                _overtimeAbsentFrames = _controller == _overtimeLoser ? 0 : _overtimeAbsentFrames + 1;
            }
        }

        private void UpdateZone(int z)
        {
            Team owner = _owners[z];
            if (owner != Team.None)
            {
                float enemy = owner == Team.Alpha ? _bravoInk[z] : _alphaInk[z];
                if (enemy >= NeutralizeRatio - RatioEpsilon)
                {
                    Team former = owner;
                    _owners[z] = Team.None;
                    owner = Team.None;
                    ZoneNeutralized?.Invoke(z, former);
                }
            }
            if (owner != Team.None)
            {
                return;
            }
            // 同一步內從佔領直接被塗到 70%(大招一口氣塗滿)也能直接易主
            if (_alphaInk[z] >= CaptureRatio - RatioEpsilon)
            {
                Capture(z, Team.Alpha);
            }
            else if (_bravoInk[z] >= CaptureRatio - RatioEpsilon)
            {
                Capture(z, Team.Bravo);
            }
        }

        private void Capture(int z, Team team)
        {
            _owners[z] = team;
            // S3:佔領的瞬間整個區域重塗成佔領方顏色
            _alphaInk[z] = team == Team.Alpha ? 1f : 0f;
            _bravoInk[z] = team == Team.Bravo ? 1f : 0f;
            ZoneCaptured?.Invoke(z, team);
        }

        private Team ComputeController()
        {
            Team first = _owners[0];
            if (first == Team.None)
            {
                return Team.None;
            }
            for (int z = 1; z < _owners.Length; z++)
            {
                if (_owners[z] != first)
                {
                    return Team.None;
                }
            }
            return first;
        }

        private void ChangeControl(Team next)
        {
            Team previous = _controller;
            _controller = next;
            if (previous != Team.None)
            {
                _lostControlFrame[(int)previous] = _frame;
                // 平手迴避:後追平的一方(達到同一計數較晚)失去控制時計數 +1
                TeamScore lost = ScoreOf(previous);
                TeamScore other = ScoreOf(TeamUtil.Opponent(previous));
                if (lost.Count == other.Count && lost.ReachedFrame > other.ReachedFrame)
                {
                    lost.BumpForTie();
                }
            }
            if (next != Team.None)
            {
                // 罰分:上一個完全控制的是對手 → 對手這段控制進了多少計數,依公式罰回去
                Team victim = TeamUtil.Opponent(next);
                if (_lastController == victim)
                {
                    TeamScore score = ScoreOf(victim);
                    int amount = PenaltyFor(_holdStart, score.CountWithPenalty);
                    if (amount > 0)
                    {
                        score.AddPenalty(amount);
                        PenaltyAdded?.Invoke(victim, amount);
                    }
                }
                _lastController = next;
                _holdStart = ScoreOf(next).CountWithPenalty;
            }
            ControlChanged?.Invoke(next, previous);
        }

        private void AdvanceCount(Team team)
        {
            int index = (int)team;
            _tickFrames[index]++;
            if (_tickFrames[index] < FramesPerCount)
            {
                return;
            }
            _tickFrames[index] = 0;
            TeamScore score = ScoreOf(team);
            // 罰分先扣;最後 1 點扣完的同一刻本計數也減 1
            bool countMoves = score.Penalty <= 0 || score.BurnPenalty();
            if (!countMoves)
            {
                return;
            }
            bool knockout = score.Decrement(_frame);
            CountChanged?.Invoke(team);
            if (knockout)
            {
                _knockout = team;
                KnockedOut?.Invoke(team);
            }
        }

        public bool IsOvertimeTriggered()
        {
            if (_knockout != Team.None || !AnarchyRules.AnyProgress(Alpha, Bravo))
            {
                return false;
            }
            Team leader = AnarchyRules.Leader(Alpha, Bravo);
            if (leader == Team.None)
            {
                return false;
            }
            Team loser = TeamUtil.Opponent(leader);
            if (_controller == loser)
            {
                return true;
            }
            // 失去控制未滿 10 秒,且領先方還沒奪回
            return FramesWithoutControl(loser) < OvertimeGraceFrames && _controller != leader;
        }

        public void BeginOvertime()
        {
            if (_inOvertime)
            {
                return;
            }
            Team leader = AnarchyRules.Leader(Alpha, Bravo);
            _overtimeLoser = leader == Team.None ? Team.Bravo : TeamUtil.Opponent(leader);
            _inOvertime = true;
            _wentToOvertime = true;
            _overtimeFrames = 0;
            // 寬限從「失去控制」那一刻起算(時間到前 4 秒丟掉 → 加時只剩 6 秒)
            _overtimeAbsentFrames = Math.Min(OvertimeGraceFrames, FramesWithoutControl(_overtimeLoser));
        }

        // 某隊連續沒有完全控制的幀數(失去控制那一步算第 1 幀;正控制中 = 0;從未控制 = int.MaxValue)
        private int FramesWithoutControl(Team team)
        {
            if (_controller == team)
            {
                return 0;
            }
            int lost = _lostControlFrame[(int)team];
            return lost < 0 ? int.MaxValue : _frame - lost + 1;
        }

        public bool IsOvertimeOver()
        {
            if (!_inOvertime || _knockout != Team.None)
            {
                return true;
            }
            // 1. 落後方反超
            if (AnarchyRules.Leader(Alpha, Bravo) == _overtimeLoser)
            {
                return true;
            }
            // 2. 領先方控制全部區域
            if (_controller == TeamUtil.Opponent(_overtimeLoser))
            {
                return true;
            }
            // 3. 落後方連續 10 秒沒有控制(9 秒時奪回可以繼續)
            if (_overtimeAbsentFrames >= OvertimeGraceFrames)
            {
                return true;
            }
            // 4. 加時滿 5:00
            return _overtimeFrames >= OvertimeCapFrames;
        }

        public float PassiveChargeRate(Team team)
        {
            if (team == Team.None || _knockout != Team.None)
            {
                return 0f;
            }
            if (_controller == TeamUtil.Opponent(team))
            {
                return EnemyControlChargePerSecond;
            }
            if (_controller == Team.None && AnarchyRules.IsBehind(team, Alpha, Bravo))
            {
                return BehindChargePerSecond;
            }
            return 0f;
        }

        public AnarchyResult Resolve()
        {
            return AnarchyRules.Resolve(Alpha, Bravo, _knockout, _wentToOvertime);
        }

        // 測試/除錯:直接設定某隊計數(達到幀 = 目前幀)
        public void DebugSetScore(Team team, int count, int penalty)
        {
            ScoreOf(team).Set(count, penalty, _frame);
        }

        private static float Clamp01(float v)
        {
            return v < 0f ? 0f : v > 1f ? 1f : v;
        }
    }
}

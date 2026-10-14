using System.Collections.Generic;
using SplatoonC.Core;
using SplatoonC.Gameplay.Match;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.AI
{
    // bot 總管:比賽名單生成後幫非本機角色掛上 BotBrain;持有共用 TurfMap;協調同隊塗地目標(不要全擠同一點)。
    public sealed class BotDirector : MonoBehaviour
    {
        public static BotDirector Instance { get; private set; }

        [SerializeField, Tooltip("隊友 bot 難度")]
        private BotBrain.Difficulty _allyDifficulty = BotBrain.Difficulty.Normal;

        [SerializeField, Tooltip("敵隊 bot 難度")]
        private BotBrain.Difficulty _enemyDifficulty = BotBrain.Difficulty.Normal;

        [SerializeField, Tooltip("塗地目標選擇時,同隊其他 bot 目標周圍的排斥半徑(公尺)")]
        private float _teammateSpread = 10f;

        [SerializeField, Tooltip("每次挑選塗地目標時隨機抽樣的候選點數")]
        private int _candidateCount = 70;

        private readonly Dictionary<BotBrain, int> _claims = new Dictionary<BotBrain, int>();
        private readonly List<BotBrain> _bots = new List<BotBrain>();
        private MatchController _match;
        private bool _attached;

        public TurfMap Map { get; private set; }
        public IReadOnlyList<BotBrain> Bots => _bots;

        // 由選單在開賽前設定(null = 用 Inspector 值)
        public static BotBrain.Difficulty? DifficultyOverride { get; set; }

        // 蠻頹對戰的目標建議(真格區域:優先塗/守區域);null = 純塗地邏輯
        public IBotObjective Objective { get; set; }

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
            if (_match != null)
            {
                _match.RosterSpawned -= OnRosterSpawned;
            }
        }

        private void Start()
        {
            Map = TurfMap.Build();
            _match = MatchController.Instance;
            if (_match == null)
            {
                return;
            }
            _match.RosterSpawned += OnRosterSpawned;
            // MatchController.Start 可能先跑完(事件已錯過):名單已存在就直接掛
            if (_match.Roster.Count > 0)
            {
                OnRosterSpawned(_match.Roster);
            }
        }

        private void OnRosterSpawned(IReadOnlyList<Inkling> roster)
        {
            if (_attached)
            {
                return;
            }
            _attached = true;
            // 主選單開的對戰:bot 隨機配武器組合(直接開場景時維持 prefab 預設,AutoTest 不受影響)
            if (MatchSettings.RandomBotKits)
            {
                var catalog = SplatoonC.Gameplay.Combat.WeaponCatalog.Load();
                if (catalog != null)
                {
                    AssignRandomKits(roster, catalog.Kits, new System.Random());
                }
            }
            Team localTeam = _match.LocalPlayer != null ? _match.LocalPlayer.Team : Team.None;
            for (int i = 0; i < roster.Count; i++)
            {
                Inkling inkling = roster[i];
                if (inkling == null || inkling.IsLocalPlayer)
                {
                    continue;
                }
                var brain = inkling.gameObject.AddComponent<BotBrain>();
                BotBrain.Difficulty difficulty = DifficultyOverride
                    ?? (inkling.Team == localTeam ? _allyDifficulty : _enemyDifficulty);
                brain.Configure(difficulty);
                _bots.Add(brain);
            }
            Debug.Log($"[BOT] 掛上 {_bots.Count} 個 bot,地盤取樣點 {Map.Samples.Count}");
        }

        // 替 bot 隨機配武器組合:兩隊同位置同種類、隊內種類盡量不重複、同種類盡量不同套(Core BotKitDraft)。
        // 位置 = 名單中該隊的順序(生成順序)。includeLocal = false 時本機玩家的組合保留,對面同位置跟著它的種類。
        // 回傳換裝人數
        public int AssignRandomKits(IReadOnlyList<Inkling> roster, IReadOnlyList<SplatoonC.Gameplay.Combat.WeaponKit> kits,
            System.Random rng, bool includeLocal = false)
        {
            var pool = new List<SplatoonC.Gameplay.Combat.WeaponKit>();
            var classes = new List<int>();
            if (kits != null)
            {
                for (int i = 0; i < kits.Count; i++)
                {
                    if (kits[i] != null && kits[i].IsValid)
                    {
                        pool.Add(kits[i]);
                        classes.Add((int)kits[i].Main.Class);
                    }
                }
            }
            if (pool.Count == 0 || roster == null)
            {
                return 0;
            }
            var alpha = new List<Inkling>();
            var bravo = new List<Inkling>();
            for (int i = 0; i < roster.Count; i++)
            {
                if (roster[i] == null)
                {
                    continue;
                }
                if (roster[i].Team == Team.Alpha)
                {
                    alpha.Add(roster[i]);
                }
                else if (roster[i].Team == Team.Bravo)
                {
                    bravo.Add(roster[i]);
                }
            }
            int[] alphaSlots = KitSlots(alpha, pool, classes, includeLocal);
            int[] bravoSlots = KitSlots(bravo, pool, classes, includeLocal);
            SplatoonC.Core.Match.BotKitDraft.Fill(classes, alphaSlots, bravoSlots, rng);
            int equipped = EquipSlots(alpha, alphaSlots, pool, includeLocal) + EquipSlots(bravo, bravoSlots, pool, includeLocal);
            Debug.Log($"[BOT] 武器組合 Alpha:{KitNames(alpha)} / Bravo:{KitNames(bravo)}");
            return equipped;
        }

        // 各位置的組合索引:本機玩家(不參與抽選時)填它目前的組合,其餘待抽
        private static int[] KitSlots(List<Inkling> members, List<SplatoonC.Gameplay.Combat.WeaponKit> pool, List<int> classes,
            bool includeLocal)
        {
            var slots = new int[members.Count];
            for (int i = 0; i < members.Count; i++)
            {
                slots[i] = SplatoonC.Core.Match.BotKitDraft.Free;
                if (includeLocal || !members[i].IsLocalPlayer)
                {
                    continue;
                }
                var loadout = members[i].GetComponent<SplatoonC.Gameplay.Combat.WeaponLoadout>();
                SplatoonC.Gameplay.Combat.WeaponKit kit = loadout != null ? loadout.Kit : null;
                if (kit == null || !kit.IsValid)
                {
                    continue;
                }
                int index = pool.IndexOf(kit);
                if (index < 0)
                {
                    // 不在清單裡的組合(測試執行期建立的):加進候選才能讓對面跟著它的種類
                    pool.Add(kit);
                    classes.Add((int)kit.Main.Class);
                    index = pool.Count - 1;
                }
                slots[i] = index;
            }
            return slots;
        }

        private static int EquipSlots(List<Inkling> members, int[] slots, List<SplatoonC.Gameplay.Combat.WeaponKit> pool,
            bool includeLocal)
        {
            int equipped = 0;
            for (int i = 0; i < members.Count; i++)
            {
                if ((!includeLocal && members[i].IsLocalPlayer) || slots[i] < 0)
                {
                    continue;
                }
                var loadout = members[i].GetComponent<SplatoonC.Gameplay.Combat.WeaponLoadout>();
                if (loadout != null && loadout.Kit != pool[slots[i]])
                {
                    loadout.Equip(pool[slots[i]]);
                    equipped++;
                }
            }
            return equipped;
        }

        private static string KitNames(List<Inkling> members)
        {
            var names = new System.Text.StringBuilder();
            for (int i = 0; i < members.Count; i++)
            {
                var loadout = members[i].GetComponent<SplatoonC.Gameplay.Combat.WeaponLoadout>();
                names.Append(i > 0 ? "、" : "").Append(loadout != null && loadout.Kit != null ? loadout.Kit.EnglishName : "?");
            }
            return names.ToString();
        }

        // 挑塗地目標:有模式目標時先問目標建議;否則沒塗/敵墨優先、偏向前線、近的優先、避開隊友已選的點
        public int PickPaintTarget(BotBrain requester, Team team, Vector3 position)
        {
            if (Map == null || Map.Samples.Count == 0)
            {
                return -1;
            }
            if (Objective != null)
            {
                int objectivePick = Objective.PickPaintTarget(requester, team, position, Map);
                if (objectivePick >= 0 && objectivePick < Map.Samples.Count)
                {
                    _claims[requester] = objectivePick;
                    return objectivePick;
                }
            }
            int best = -1;
            float bestScore = float.MinValue;
            int count = Map.Samples.Count;
            float spreadSq = _teammateSpread * _teammateSpread;
            // 主武器偏好高處(狙擊槍:往高台、長視線的位置塗 / 走);0 = 不影響分數(其他武器與原本相同)
            float highGround = HighGroundBias(requester);
            for (int c = 0; c < _candidateCount; c++)
            {
                int index = Random.Range(0, count);
                byte owner = Map.OwnerAt(index);
                if (owner == (byte)team)
                {
                    continue;
                }
                Vector3 p = Map.Samples[index].Position;
                float distance = Vector3.Distance(position, p);
                float progress = Map.ProgressFor(index, team);
                // 敵墨比空地更值得(翻盤 + 縮敵方地盤);前線(推進度 0.35~0.75)加分;太遠扣分
                float score = (owner == 0 ? 1f : 1.4f)
                    - Mathf.Abs(progress - 0.55f) * 1.2f
                    - distance / 45f
                    + Random.value * 0.35f;
                if (highGround != 0f)
                {
                    score += highGround * Mathf.Clamp((p.y - position.y) / 3f, -1f, 1f);
                }
                foreach (var claim in _claims)
                {
                    if (claim.Key == requester || claim.Key == null || claim.Value < 0 || claim.Key.Team != team)
                    {
                        continue;
                    }
                    if ((Map.Samples[claim.Value].Position - p).sqrMagnitude < spreadSq)
                    {
                        score -= 0.6f;
                    }
                }
                if (score > bestScore)
                {
                    bestScore = score;
                    best = index;
                }
            }
            _claims[requester] = best;
            return best;
        }

        private static float HighGroundBias(BotBrain requester)
        {
            var loadout = requester != null ? requester.GetComponent<SplatoonC.Gameplay.Combat.WeaponLoadout>() : null;
            SplatoonC.Gameplay.Combat.MainWeapon main = loadout != null ? loadout.Main : null;
            return main != null ? main.BotHighGroundBias : 0f;
        }

        // 同隊其他 bot 已選的塗地目標裡,離 point 在 radius 內的有幾個(目標建議用來分散站位)
        public int ClaimsNear(BotBrain requester, Team team, Vector3 point, float radius)
        {
            if (Map == null)
            {
                return 0;
            }
            int n = 0;
            float radiusSq = radius * radius;
            foreach (var claim in _claims)
            {
                if (claim.Key == requester || claim.Key == null || claim.Value < 0 || claim.Value >= Map.Samples.Count
                    || claim.Key.Team != team)
                {
                    continue;
                }
                if ((Map.Samples[claim.Value].Position - point).sqrMagnitude < radiusSq)
                {
                    n++;
                }
            }
            return n;
        }
    }
}

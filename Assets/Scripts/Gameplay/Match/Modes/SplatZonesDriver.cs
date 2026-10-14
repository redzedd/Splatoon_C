using System.Collections.Generic;
using SplatoonC.Core;
using SplatoonC.Core.Match.Ranked;
using SplatoonC.Gameplay.AI;
using SplatoonC.Gameplay.Painting;
using UnityEngine;

namespace SplatoonC.Gameplay.Match
{
    // 真格區域的執行期驅動:依 ModeLayout 生成 SplatZone、每幀把各區域墨量佔比寫進 SplatZonesRules、
    // 規則回報佔領時瞬間重塗、中立/佔領時換外觀顏色;同時是 bot 的目標建議(區域不在手上 → 去塗;在手上 → 補洞 / 外圍防守)。
    public sealed class SplatZonesDriver : IObjectiveDriver, IBotObjective
    {
        // 外圍防守帶:距區域邊界 1~14m、高度差 4m 內
        private const float RingMin = 1f;
        private const float RingMax = 14f;
        private const float RingHeight = 4f;

        private readonly List<SplatZone> _zones = new List<SplatZone>();
        private SplatZonesRules _rules;

        // bot 取樣點分類(TurfMap 建好後第一次被問時計算)
        private TurfMap _samplesFor;
        private List<int>[] _zoneSamples;
        private readonly List<int> _ringSamples = new List<int>();

        public IObjectiveMode Rules => _rules;
        public SplatZonesRules ZoneRules => _rules;
        public IReadOnlyList<SplatZone> Zones => _zones;

        // bot 目標建議統計(soak 驗收:bot 真的在打區域)
        public int BotPicksInZone { get; private set; }
        public int BotPicksAround { get; private set; }
        public int BotPicksDeferred { get; private set; }

        public bool Build(ModeLayout layout, Transform root, ObjectiveAssets assets)
        {
            IReadOnlyList<ModeLayout.ZoneArea> areas = layout != null ? layout.SplatZones : null;
            if (areas == null || areas.Count == 0)
            {
                Debug.LogWarning("[MODE] 場地沒有真格區域資料");
                return false;
            }
            Shader fill = assets != null ? assets.ZoneFillShader : null;
            if (fill == null)
            {
                Debug.LogWarning("[MODE] 缺少 ObjectiveAssets / InkZoneFill shader:佔領時只改歸屬格,不重畫墨");
            }
            _rules = new SplatZonesRules(areas.Count);
            for (int i = 0; i < areas.Count; i++)
            {
                var go = new GameObject("SplatZone_" + i);
                go.transform.SetParent(root, false);
                var zone = go.AddComponent<SplatZone>();
                zone.Build(i, areas[i], fill, assets);
                if (zone.CellCount == 0)
                {
                    Debug.LogError($"[MODE] 區域 {zone.ZoneName} 沒有任何可塗地面格(多邊形/高度範圍錯誤?)");
                    return false;
                }
                _zones.Add(zone);
                Debug.Log($"[MODE] 真格區域 {zone.ZoneName}:地面投影面積 {zone.Area:0.0} m²、格數 {zone.CellCount}、" +
                          $"中心 ({zone.Center.x:0.0}, {zone.Center.y:0.0}, {zone.Center.z:0.0})");
            }
            _rules.ZoneCaptured += OnZoneCaptured;
            _rules.ZoneNeutralized += OnZoneNeutralized;
            TeamPalette.ColorsChanged += RefreshColors;
            RefreshColors();
            return true;
        }

        public void Dispose()
        {
            TeamPalette.ColorsChanged -= RefreshColors;
            if (_rules != null)
            {
                _rules.ZoneCaptured -= OnZoneCaptured;
                _rules.ZoneNeutralized -= OnZoneNeutralized;
            }
        }

        public void BeforeStep()
        {
            for (int i = 0; i < _zones.Count; i++)
            {
                SplatZone zone = _zones[i];
                zone.Sample();
                _rules.SetZoneInk(i, zone.AlphaRatio, zone.BravoRatio);
            }
        }

        public void AfterStep()
        {
            for (int i = 0; i < _zones.Count; i++)
            {
                Team owner = _rules.OwnerOf(i);
                if (_zones[i].Owner != owner)
                {
                    _zones[i].Owner = owner;
                    ApplyColor(_zones[i]);
                }
            }
        }

        private void OnZoneCaptured(int index, Team team)
        {
            SplatZone zone = _zones[index];
            zone.Owner = team;
            zone.Recolor(team);
            ApplyColor(zone);
            if (zone.Visual != null)
            {
                zone.Visual.Flash();
            }
        }

        private void OnZoneNeutralized(int index, Team former)
        {
            SplatZone zone = _zones[index];
            zone.Owner = Team.None;
            ApplyColor(zone);
        }

        private void RefreshColors()
        {
            for (int i = 0; i < _zones.Count; i++)
            {
                ApplyColor(_zones[i]);
            }
        }

        // 中立色 = 本場配色組的中立色(S3:未佔領的目標物用中立色)
        public static Color ColorOf(Team owner)
        {
            if (owner != Team.None)
            {
                return TeamPalette.ColorOf(owner);
            }
            MatchController match = MatchController.Instance;
            return match != null ? match.Colors.Neutral : new Color(0.8f, 0.8f, 0.8f);
        }

        private static void ApplyColor(SplatZone zone)
        {
            if (zone.Visual != null)
            {
                zone.Visual.SetColor(ColorOf(zone.Owner));
            }
        }

        // ───────────────────────── bot 目標建議 ─────────────────────────

        public int PickPaintTarget(BotBrain requester, Team team, Vector3 position, TurfMap map)
        {
            if (_rules == null || _zones.Count == 0 || map == null || map.Samples.Count == 0)
            {
                return -1;
            }
            EnsureSamples(map);
            float roll = Random.value;
            int pick;
            if (_rules.Controller != team)
            {
                // 區域不在手上:大多數時候去塗區域(敵墨優先,再來無墨);取樣點都已是我方墨 → 穿越區域到對面,沿途掃射
                if (roll < 0.88f)
                {
                    int zone = ZoneToTake(team);
                    pick = PickInZone(requester, team, position, map, zone);
                    if (pick < 0)
                    {
                        pick = PickAcross(requester, team, position, map, zone);
                    }
                    if (pick >= 0)
                    {
                        BotPicksInZone++;
                        return pick;
                    }
                }
                BotPicksDeferred++;
                return -1;
            }
            // 守區域:補區域內被塗掉的地方 → 區域外圍(偏敵方那側)攔截 → 偶爾一般塗地
            if (roll < 0.55f)
            {
                for (int z = 0; z < _zones.Count; z++)
                {
                    pick = PickInZone(requester, team, position, map, z);
                    if (pick >= 0)
                    {
                        BotPicksInZone++;
                        return pick;
                    }
                }
            }
            if (roll < 0.88f)
            {
                pick = PickRing(requester, team, position, map);
                if (pick >= 0)
                {
                    BotPicksAround++;
                    return pick;
                }
            }
            BotPicksDeferred++;
            return -1;
        }

        // 要搶的區域:敵方佔著的優先,其次中立,都不是就第一個不屬於我方的
        private int ZoneToTake(Team team)
        {
            int best = 0;
            int bestScore = int.MinValue;
            for (int z = 0; z < _zones.Count; z++)
            {
                Team owner = _rules.OwnerOf(z);
                int score = owner == team ? -1 : owner == Team.None ? 1 : 2;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = z;
                }
            }
            return best;
        }

        private void EnsureSamples(TurfMap map)
        {
            if (_samplesFor == map)
            {
                return;
            }
            _samplesFor = map;
            _zoneSamples = new List<int>[_zones.Count];
            for (int z = 0; z < _zones.Count; z++)
            {
                _zoneSamples[z] = new List<int>();
            }
            _ringSamples.Clear();
            for (int i = 0; i < map.Samples.Count; i++)
            {
                Vector3 p = map.Samples[i].Position;
                bool inside = false;
                for (int z = 0; z < _zones.Count; z++)
                {
                    if (_zones[z].Contains(p))
                    {
                        _zoneSamples[z].Add(i);
                        inside = true;
                        break;
                    }
                }
                if (inside)
                {
                    continue;
                }
                for (int z = 0; z < _zones.Count; z++)
                {
                    float edge = _zones[z].DistanceToEdge(p);
                    if (edge >= RingMin && edge <= RingMax && Mathf.Abs(p.y - _zones[z].Center.y) <= RingHeight)
                    {
                        _ringSamples.Add(i);
                        break;
                    }
                }
            }
            int total = 0;
            for (int z = 0; z < _zones.Count; z++)
            {
                total += _zoneSamples[z].Count;
            }
            Debug.Log($"[MODE] bot 區域取樣點 {total}、外圍取樣點 {_ringSamples.Count}");
        }

        // 區域內「不是我方墨」的取樣點:敵墨 > 無墨,近的優先,避開隊友已選的點
        private int PickInZone(BotBrain requester, Team team, Vector3 position, TurfMap map, int zone)
        {
            List<int> samples = _zoneSamples[zone];
            int best = -1;
            float bestScore = float.MinValue;
            BotDirector director = BotDirector.Instance;
            for (int k = 0; k < samples.Count; k++)
            {
                int index = samples[k];
                byte owner = map.OwnerAt(index);
                if (owner == (byte)team)
                {
                    continue;
                }
                Vector3 p = map.Samples[index].Position;
                float score = (owner == 0 ? 1f : 1.4f) - Vector3.Distance(position, p) / 40f + Random.value * 0.3f;
                if (director != null)
                {
                    score -= 0.5f * director.ClaimsNear(requester, team, p, 3f);
                }
                if (score > bestScore)
                {
                    bestScore = score;
                    best = index;
                }
            }
            return best;
        }

        // 區域取樣點全是我方墨(但佔比未達 70%):挑區域另一側、不是我方墨的外圍點,走過去的路上會掃過區域
        private int PickAcross(BotBrain requester, Team team, Vector3 position, TurfMap map, int zone)
        {
            Vector3 center = _zones[zone].Center;
            Vector3 approach = center - position;
            approach.y = 0f;
            if (approach.sqrMagnitude < 0.01f)
            {
                approach = Vector3.forward;
            }
            approach.Normalize();
            int best = -1;
            float bestScore = float.MinValue;
            for (int k = 0; k < _ringSamples.Count; k++)
            {
                int index = _ringSamples[k];
                if (map.OwnerAt(index) == (byte)team)
                {
                    continue;
                }
                Vector3 offset = map.Samples[index].Position - center;
                offset.y = 0f;
                float score = Vector3.Dot(offset.normalized, approach) - offset.magnitude / 30f + Random.value * 0.3f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = index;
                }
            }
            return best;
        }

        // 外圍防守:偏敵方那側、不是我方墨的點
        private int PickRing(BotBrain requester, Team team, Vector3 position, TurfMap map)
        {
            int best = -1;
            float bestScore = float.MinValue;
            BotDirector director = BotDirector.Instance;
            for (int k = 0; k < _ringSamples.Count; k++)
            {
                int index = _ringSamples[k];
                byte owner = map.OwnerAt(index);
                if (owner == (byte)team)
                {
                    continue;
                }
                Vector3 p = map.Samples[index].Position;
                float score = map.ProgressFor(index, team) * 1.2f + (owner == 0 ? 0f : 0.4f)
                              - Vector3.Distance(position, p) / 50f + Random.value * 0.3f;
                if (director != null)
                {
                    score -= 0.5f * director.ClaimsNear(requester, team, p, 4f);
                }
                if (score > bestScore)
                {
                    bestScore = score;
                    best = index;
                }
            }
            return best;
        }
    }
}

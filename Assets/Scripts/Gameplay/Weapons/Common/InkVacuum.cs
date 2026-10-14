using System.Collections.Generic;
using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // Ink Vac 的吸引漩渦(S3 SpBlower 吸引モード,research/04 §2.8、JP wiki キューインキ):
    //   吸引中登記在 Active;攻擊端查詢(靜態、零配置)——
    //     InkBullet / InkProjectile:這一步的路徑進入敵方吸引範圍 → 墨彈被吸走(不傷人、不塗墨、不爆炸),容量 += 該發傷害;
    //     Explosion.Detonate:爆心在敵方吸引範圍內 → 整個爆炸被吸走(罩在漩渦後的使用者與隊友不受傷)。
    //   吸不到的:已落地的墨、滾筒推滾、刀身、光束(Killer Wail)、衝擊波(Wave Breaker)——它們本來就不經過上面三條路徑。
    // 形狀 = 錐(槍口起沿準心方向,長度隨按住時間長到 15 u,半徑 0.8 → 3.3)+ 槍口球(r 1.2);數學在 InkVacModel。
    // 同一次攻擊(attackId)的多顆彈(Trizooka 三顆墨團、傘的彈丸)只算一次最大值(近 16 次攻擊的去重環)。
    public sealed class InkVacuum
    {
        private const int RecentSize = 16;

        public static readonly List<InkVacuum> Active = new List<InkVacuum>();

        // 累計被吸走的攻擊數(測試用)
        public static int TotalAbsorbed { get; private set; }

        private readonly int[] _recentIds = new int[RecentSize];
        private readonly float[] _recentAmounts = new float[RecentSize];
        private int _recentNext;
        private bool _registered;

        public InkVacuum(Inkling owner)
        {
            Owner = owner;
        }

        public Inkling Owner { get; }
        public Team Team { get; private set; } = Team.None;

        // 槍口位置、單位方向、目前錐長(每幀由 Ink Vac 更新)
        public Vector3 Apex { get; set; }
        public Vector3 Axis { get; set; } = Vector3.forward;
        public float Length { get; set; }

        // 這次吸引累計吸進的傷害量(容量換算由擁有者做)
        public float Absorbed { get; private set; }
        public int AbsorbedHits { get; private set; }
        public Vector3 LastAbsorbPoint { get; private set; }

        // 吸到東西時的回呼(吸入特效;擁有者快取委派)
        public System.Action<Vector3, float> Sucked;

        public bool IsAlive => _registered && Length > 0f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetOnLoad()
        {
            Active.Clear();
            TotalAbsorbed = 0;
        }

        public void Enable(Team team)
        {
            Team = team;
            if (_registered)
            {
                return;
            }
            _registered = true;
            Active.Add(this);
        }

        public void Disable()
        {
            Length = 0f;
            if (!_registered)
            {
                return;
            }
            _registered = false;
            Active.Remove(this);
        }

        // 新的一次吸引(發動時):容量與去重環歸零
        public void ResetAbsorbed()
        {
            Absorbed = 0f;
            AbsorbedHits = 0;
            for (int i = 0; i < RecentSize; i++)
            {
                _recentIds[i] = 0;
                _recentAmounts[i] = 0f;
            }
        }

        // 吸這一隊的攻擊嗎(自己隊伍不吸;中立也吸)
        public bool AbsorbsFrom(Team attackerTeam)
        {
            return IsAlive && attackerTeam != Team;
        }

        public bool Contains(Vector3 point, float pad = 0f)
        {
            return InkVacModel.Contains(Apex, Axis, Length, point, pad);
        }

        // 吸進一次攻擊(amount = 該次傷害量;attackId ≠ 0 時同一次攻擊只算最大那一發)。回傳實際加進容量的量
        public float Absorb(Vector3 point, float amount, int attackId)
        {
            if (amount < 0f)
            {
                amount = 0f;
            }
            float added = amount;
            if (attackId != 0)
            {
                int slot = -1;
                for (int i = 0; i < RecentSize; i++)
                {
                    if (_recentIds[i] == attackId)
                    {
                        slot = i;
                        break;
                    }
                }
                if (slot >= 0)
                {
                    added = Mathf.Max(0f, amount - _recentAmounts[slot]);
                    _recentAmounts[slot] = Mathf.Max(_recentAmounts[slot], amount);
                }
                else
                {
                    _recentIds[_recentNext] = attackId;
                    _recentAmounts[_recentNext] = amount;
                    _recentNext = (_recentNext + 1) % RecentSize;
                }
            }
            Absorbed += added;
            AbsorbedHits++;
            TotalAbsorbed++;
            LastAbsorbPoint = point;
            Sucked?.Invoke(point, added);
            return added;
        }

        // 直接加容量(吸到敵方角色每幀 +1.5)
        public void AddFill(float amount)
        {
            if (amount > 0f)
            {
                Absorbed += amount;
            }
        }

        // ── 攻擊端查詢 ──

        // 半徑 pad 的攻擊沿 from + dir·t(t ≤ maxDistance)進入 attackerTeam 的敵方吸引範圍:回傳最近的一個
        public static bool Segment(Team attackerTeam, Vector3 from, Vector3 direction, float maxDistance, float pad,
            out InkVacuum vacuum, out float distance)
        {
            vacuum = null;
            distance = 0f;
            if (Active.Count == 0)
            {
                return false;
            }
            float best = float.MaxValue;
            for (int i = 0; i < Active.Count; i++)
            {
                InkVacuum candidate = Active[i];
                if (!candidate.AbsorbsFrom(attackerTeam))
                {
                    continue;
                }
                if (InkVacModel.SegmentEntry(candidate.Apex, candidate.Axis, candidate.Length, from, direction, maxDistance,
                        pad, out float d) && d < best)
                {
                    best = d;
                    vacuum = candidate;
                }
            }
            if (vacuum == null)
            {
                return false;
            }
            distance = best;
            return true;
        }

        // point 在不在 attackerTeam 的敵方吸引範圍裡(爆心)
        public static InkVacuum Containing(Team attackerTeam, Vector3 point)
        {
            for (int i = 0; i < Active.Count; i++)
            {
                InkVacuum candidate = Active[i];
                if (candidate.AbsorbsFrom(attackerTeam) && candidate.Contains(point))
                {
                    return candidate;
                }
            }
            return null;
        }
    }
}

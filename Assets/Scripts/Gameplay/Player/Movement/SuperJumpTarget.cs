using System.Collections.Generic;
using SplatoonC.Gameplay.Combat;
using UnityEngine;

namespace SplatoonC.Gameplay.Player
{
    public enum SuperJumpTargetKind : byte
    {
        // 自家出生點附近(S3 不能跳「到」出生器,落在基地出生點)
        Spawn = 0,
        // 活著的隊友(取他最後著地的位置;他正在超級跳 → 跟他同一個落點)
        Teammate = 1,
        // 自家的 Squid Beakon(每個 2 次)
        Beakon = 2,
        // 自家的 Big Bubbler(S3:展開中兼作信標)
        Bubbler = 3,
    }

    // 超級跳目標(值型別;不持有位置,落點在需要時由 SuperJump 解析 —— 隊友會移動)
    public readonly struct SuperJumpTarget
    {
        public readonly SuperJumpTargetKind Kind;
        public readonly Inkling Teammate;
        public readonly SquidBeakon Beakon;
        public readonly BigBubblerDevice Bubbler;

        private SuperJumpTarget(SuperJumpTargetKind kind, Inkling teammate, SquidBeakon beakon, BigBubblerDevice bubbler)
        {
            Kind = kind;
            Teammate = teammate;
            Beakon = beakon;
            Bubbler = bubbler;
        }

        public static SuperJumpTarget Spawn => new SuperJumpTarget(SuperJumpTargetKind.Spawn, null, null, null);

        public static SuperJumpTarget ToTeammate(Inkling teammate)
        {
            return new SuperJumpTarget(SuperJumpTargetKind.Teammate, teammate, null, null);
        }

        public static SuperJumpTarget ToBeakon(SquidBeakon beakon)
        {
            return new SuperJumpTarget(SuperJumpTargetKind.Beakon, null, beakon, null);
        }

        public static SuperJumpTarget ToBubbler(BigBubblerDevice bubbler)
        {
            return new SuperJumpTarget(SuperJumpTargetKind.Bubbler, null, null, bubbler);
        }

        // 目標物件目前的世界位置(出生點由呼叫端另外解析)
        public Vector3 AnchorPosition
        {
            get
            {
                switch (Kind)
                {
                    case SuperJumpTargetKind.Teammate:
                        return Teammate != null ? Teammate.transform.position : Vector3.zero;
                    case SuperJumpTargetKind.Beakon:
                        return Beakon != null ? Beakon.transform.position : Vector3.zero;
                    case SuperJumpTargetKind.Bubbler:
                        return Bubbler != null ? Bubbler.transform.position : Vector3.zero;
                    default:
                        return Vector3.zero;
                }
            }
        }

        public bool SameAs(in SuperJumpTarget other)
        {
            return Kind == other.Kind && Teammate == other.Teammate && Beakon == other.Beakon && Bubbler == other.Bubbler;
        }

        // 顯示名稱(選單列表)
        public string Label
        {
            get
            {
                switch (Kind)
                {
                    case SuperJumpTargetKind.Teammate:
                        return Teammate != null ? Teammate.DisplayName : "?";
                    case SuperJumpTargetKind.Beakon:
                        return Beakon != null && Beakon.Owner != null ? $"跳躍信標({Beakon.Owner.DisplayName})" : "跳躍信標";
                    case SuperJumpTargetKind.Bubbler:
                        return "巨大防護罩";
                    default:
                        return "出生點";
                }
            }
        }
    }

    // 某角色此刻可以超級跳的目標清單(呼叫端提供清單重複使用,零配置):
    // 出生點 → 活著的隊友 → 自家信標 → 自家 Big Bubbler(S3 地圖上的順序:出生點、隊友,其次信標/裝置)
    public static class SuperJumpTargets
    {
        public static int Collect(Inkling jumper, List<SuperJumpTarget> results, bool includeSpawn = true)
        {
            results.Clear();
            if (jumper == null)
            {
                return 0;
            }
            SuperJump self = jumper.GetComponent<SuperJump>();
            if (includeSpawn)
            {
                results.Add(SuperJumpTarget.Spawn);
            }
            var all = Inkling.All;
            for (int i = 0; i < all.Count; i++)
            {
                Inkling other = all[i];
                if (other == null || other == jumper || other.Team != jumper.Team)
                {
                    continue;
                }
                var target = SuperJumpTarget.ToTeammate(other);
                if (self == null || self.IsValidTarget(target))
                {
                    results.Add(target);
                }
            }
            var beakons = SquidBeakon.Active;
            for (int i = 0; i < beakons.Count; i++)
            {
                SquidBeakon beakon = beakons[i];
                if (beakon != null && beakon.CanBeJumpedBy(jumper))
                {
                    results.Add(SuperJumpTarget.ToBeakon(beakon));
                }
            }
            var damageables = SplatoonC.Gameplay.Combat.DamageableRegistry.Active;
            for (int i = 0; i < damageables.Count; i++)
            {
                if (damageables[i] is BigBubblerDevice bubbler && bubbler.IsSuperJumpBeacon && bubbler.Team == jumper.Team)
                {
                    results.Add(SuperJumpTarget.ToBubbler(bubbler));
                }
            }
            return results.Count;
        }
    }
}

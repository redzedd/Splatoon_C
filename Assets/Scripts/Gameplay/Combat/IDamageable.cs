using System.Collections.Generic;
using SplatoonC.Core;
using SplatoonC.Core.Combat;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    public enum DamageKind : byte
    {
        Main = 0,
        Sub = 1,
        Special = 2,
        EnemyInk = 3,
        Environment = 4,
    }

    // 一次傷害的完整描述:誰打的(擊殺歸屬、擊殺鏡頭)、哪一隊(友軍判定)、哪一類(結算統計)、
    // 攻擊類別(AttackClass:物件受擊時查 DamageRates 倍率;對玩家一律 ×1.0)。
    public readonly struct DamageInfo
    {
        public readonly float Amount;
        public readonly Vector3 Point;
        public readonly Team Team;
        public readonly IDamageDealer Attacker;
        public readonly DamageKind Kind;
        public readonly AttackClass AttackClass;

        public DamageInfo(float amount, Vector3 point, Team team, IDamageDealer attacker, DamageKind kind,
            AttackClass attackClass = AttackClass.Other)
        {
            Amount = amount;
            Point = point;
            Team = team;
            Attacker = attacker;
            Kind = kind;
            AttackClass = attackClass;
        }

        public DamageInfo WithAmount(float amount)
        {
            return new DamageInfo(amount, Point, Team, Attacker, Kind, AttackClass);
        }
    }

    // 造成傷害的一方(角色):命中回饋(準星命中標記)與擊殺計數。
    public interface IDamageDealer
    {
        Team Team { get; }
        string DisplayName { get; }
        Transform transform { get; }
        void OnDamageDealt(IDamageable target, float amount, bool splatted);
    }

    // 可受傷目標。直擊(墨彈)由 collider 所在物件上的元件實作;範圍傷害(炸彈、龍捲風)走 DamageableRegistry 逐一判定。
    public interface IDamageable
    {
        bool IsAlive { get; }

        // Team.None = 中立目標(試射場假人):任何隊伍都打得到
        Team Team { get; }

        Vector3 ClosestPoint(Vector3 from);
        void ApplyDamage(in DamageInfo info);
    }

    public static class DamageRules
    {
        // 友軍無傷(Splatoon 沒有 friendly fire);中立目標誰都能打
        public static bool CanDamage(Team attacker, IDamageable target)
        {
            return target.Team == Team.None || target.Team != attacker;
        }
    }

    // 場上可受傷目標清單(仿 PaintableSurface.Active):範圍傷害不用 OverlapSphere——零配置、圖層無關、可測。
    public static class DamageableRegistry
    {
        public static readonly List<IDamageable> Active = new List<IDamageable>();
    }
}

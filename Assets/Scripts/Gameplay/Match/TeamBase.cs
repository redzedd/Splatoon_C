using System.Collections.Generic;
using SplatoonC.Core;
using UnityEngine;

namespace SplatoonC.Gameplay.Match
{
    // 隊伍基地:出生點(子物件 SpawnPoint_*)、朝向、以及「敵人進不來」的保護區(S1/S2 重生台隱形牆)。
    // 保護區只擋敵方角色與敵方攻擊判定由各系統自行查 IsInsideProtectedZone;這裡不放實體碰撞,
    // 避免同隊也被擋(Unity 的碰撞無法依隊伍過濾同一個 collider)。
    public sealed class TeamBase : MonoBehaviour
    {
        public static readonly List<TeamBase> All = new List<TeamBase>();

        [SerializeField, Tooltip("所屬隊伍")]
        private Team _team = Team.Alpha;

        [SerializeField, Tooltip("出生點(依序分配給隊員);留空自動抓子物件")]
        private Transform[] _spawnPoints;

        [SerializeField, Tooltip("保護區半徑(公尺,水平);敵人不可進入")]
        private float _protectedRadius = 6f;

        [SerializeField, Tooltip("保護區高度(公尺,自基地原點向上)")]
        private float _protectedHeight = 4f;

        public Team Team => _team;
        public float ProtectedRadius => _protectedRadius;

        public void Configure(Team team, Transform[] spawnPoints, float protectedRadius)
        {
            _team = team;
            _spawnPoints = spawnPoints;
            _protectedRadius = protectedRadius;
        }

        private void OnEnable()
        {
            All.Add(this);
        }

        private void OnDisable()
        {
            All.Remove(this);
        }

        public static TeamBase Of(Team team)
        {
            for (int i = 0; i < All.Count; i++)
            {
                if (All[i]._team == team)
                {
                    return All[i];
                }
            }
            return null;
        }

        public int SpawnCount => _spawnPoints != null ? _spawnPoints.Length : 0;

        public Transform GetSpawn(int slot)
        {
            if (_spawnPoints == null || _spawnPoints.Length == 0)
            {
                return transform;
            }
            return _spawnPoints[((slot % _spawnPoints.Length) + _spawnPoints.Length) % _spawnPoints.Length];
        }

        public bool IsInsideProtectedZone(Vector3 worldPosition)
        {
            Vector3 local = worldPosition - transform.position;
            if (local.y < -1f || local.y > _protectedHeight)
            {
                return false;
            }
            local.y = 0f;
            return local.sqrMagnitude <= _protectedRadius * _protectedRadius;
        }

        // 某隊的角色能否站在這個位置(不在敵方基地保護區內)
        public static bool IsBlockedFor(Team team, Vector3 worldPosition, out TeamBase blocking)
        {
            for (int i = 0; i < All.Count; i++)
            {
                TeamBase b = All[i];
                if (b._team != team && b.IsInsideProtectedZone(worldPosition))
                {
                    blocking = b;
                    return true;
                }
            }
            blocking = null;
            return false;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = _team == Team.Bravo ? new Color(0.2f, 0.3f, 1f, 0.6f) : new Color(1f, 0.5f, 0f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, _protectedRadius);
            if (_spawnPoints != null)
            {
                foreach (Transform t in _spawnPoints)
                {
                    if (t != null)
                    {
                        Gizmos.DrawSphere(t.position + Vector3.up * 0.2f, 0.3f);
                        Gizmos.DrawRay(t.position + Vector3.up * 0.2f, t.forward * 1.5f);
                    }
                }
            }
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 所有可用武器組合的清單(放在 Resources,選單與 bot 用 Resources.Load 取得)
    [CreateAssetMenu(fileName = "WeaponCatalog", menuName = "SplatoonC/武器清單")]
    public sealed class WeaponCatalog : ScriptableObject
    {
        public const string ResourcePath = "WeaponCatalog";

        [SerializeField, Tooltip("可選的武器組合(第一個是預設)")]
        private List<WeaponKit> _kits = new List<WeaponKit>();

        public IReadOnlyList<WeaponKit> Kits => _kits;

        public WeaponKit Default => _kits.Count > 0 ? _kits[0] : null;

        private static WeaponCatalog _cached;

        public static WeaponCatalog Load()
        {
            if (_cached == null)
            {
                _cached = Resources.Load<WeaponCatalog>(ResourcePath);
            }
            return _cached;
        }

        public WeaponKit Find(string id)
        {
            for (int i = 0; i < _kits.Count; i++)
            {
                if (_kits[i] != null && _kits[i].Id == id)
                {
                    return _kits[i];
                }
            }
            return null;
        }

        public void SetKits(List<WeaponKit> kits)
        {
            _kits = kits;
        }
    }
}

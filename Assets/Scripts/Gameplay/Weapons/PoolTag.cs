using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 池物件的歸屬標記(SharedPool 內部用):記住自己屬於哪個 prefab 的池
    public sealed class PoolTag : MonoBehaviour
    {
        public int Key;
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SplatoonC.Gameplay.Combat
{
    // 全場共用的物件池(以 prefab 為鍵):炸彈、灑水器、大招物件等「低頻但多人會用」的東西,
    // 8 個角色共用一個池,不再每人各開一份。場景卸載時整批作廢(物件跟著場景銷毀)。
    public static class SharedPool
    {
        private sealed class Bucket
        {
            public readonly Stack<GameObject> Free = new Stack<GameObject>();
            public int Active;
        }

        private static readonly Dictionary<int, Bucket> Buckets = new Dictionary<int, Bucket>();
        private static Transform _root;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetOnLoad()
        {
            Buckets.Clear();
            _root = null;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        private static void OnSceneUnloaded(Scene scene)
        {
            Buckets.Clear();
            _root = null;
        }

        private static Transform Root
        {
            get
            {
                if (_root == null)
                {
                    _root = new GameObject("SharedPool").transform;
                }
                return _root;
            }
        }

        public static T Spawn<T>(T prefab) where T : Component
        {
            if (prefab == null)
            {
                return null;
            }
            int key = prefab.gameObject.GetInstanceID();
            if (!Buckets.TryGetValue(key, out Bucket bucket))
            {
                bucket = new Bucket();
                Buckets[key] = bucket;
            }
            GameObject go = null;
            // 已被銷毀的物件在 Unity 端 == null,跳過
            while (bucket.Free.Count > 0 && go == null)
            {
                go = bucket.Free.Pop();
            }
            if (go == null)
            {
                go = Object.Instantiate(prefab.gameObject, Root);
                go.AddComponent<PoolTag>().Key = key;
            }
            go.SetActive(true);
            bucket.Active++;
            return go.GetComponent<T>();
        }

        public static void Despawn(Component instance)
        {
            if (instance == null)
            {
                return;
            }
            var tag = instance.GetComponent<PoolTag>();
            if (tag == null || !Buckets.TryGetValue(tag.Key, out Bucket bucket))
            {
                Object.Destroy(instance.gameObject);
                return;
            }
            instance.gameObject.SetActive(false);
            bucket.Free.Push(instance.gameObject);
            bucket.Active = Mathf.Max(0, bucket.Active - 1);
        }

        public static void Prewarm<T>(T prefab, int count) where T : Component
        {
            if (prefab == null || count <= 0)
            {
                return;
            }
            var temp = new List<T>(count);
            for (int i = 0; i < count; i++)
            {
                temp.Add(Spawn(prefab));
            }
            for (int i = 0; i < temp.Count; i++)
            {
                Despawn(temp[i]);
            }
        }

        public static int ActiveCount(Component prefab)
        {
            if (prefab == null)
            {
                return 0;
            }
            return Buckets.TryGetValue(prefab.gameObject.GetInstanceID(), out Bucket bucket) ? bucket.Active : 0;
        }
    }
}

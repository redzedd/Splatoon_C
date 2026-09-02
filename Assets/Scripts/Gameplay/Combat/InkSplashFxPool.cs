using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

namespace SplatoonC.Gameplay.Combat
{
    // 命中噴濺粒子的池(掛在 GameSystems):burst 型 ParticleSystem,計時回收,穩態零配置。
    public sealed class InkSplashFxPool : MonoBehaviour
    {
        public static InkSplashFxPool Instance { get; private set; }

        [SerializeField, Tooltip("噴濺粒子 prefab(Assets/Prefabs/InkSplashFX)")]
        private ParticleSystem _prefab;

        [SerializeField, Tooltip("物件池預熱數量")]
        private int _prewarm = 16;

        [SerializeField, Tooltip("回收延遲(秒),需大於粒子最長壽命")]
        private float _recycleAfter = 0.6f;

        private struct ActiveFx
        {
            public ParticleSystem System;
            public float DueTime;
        }

        private ObjectPool<ParticleSystem> _pool;
        private readonly List<ActiveFx> _active = new List<ActiveFx>(32);

        // 供 AutoTest 驗證「有沒有真的濺出水花」——視覺特效沒有其他可量測的痕跡。
        public int ActiveCount => _active.Count;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                enabled = false;
                return;
            }
            Instance = this;

            if (_prefab == null)
            {
                Debug.LogError("InkSplashFxPool:缺少噴濺粒子 prefab,命中無特效", this);
                return;
            }
            _pool = new ObjectPool<ParticleSystem>(
                () => Instantiate(_prefab, transform),
                ps => ps.gameObject.SetActive(true),
                ps => ps.gameObject.SetActive(false),
                ps => Destroy(ps.gameObject),
                collectionCheck: true,
                defaultCapacity: _prewarm);

            var warm = new ParticleSystem[_prewarm];
            for (int i = 0; i < _prewarm; i++)
            {
                warm[i] = _pool.Get();
            }
            for (int i = 0; i < _prewarm; i++)
            {
                _pool.Release(warm[i]);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                if (Time.time >= _active[i].DueTime)
                {
                    _pool.Release(_active[i].System);
                    _active.RemoveAt(i);
                }
            }
        }

        public void Spawn(Vector3 position, Vector3 normal)
        {
            if (_pool == null)
            {
                return;
            }
            var ps = _pool.Get();
            ps.transform.SetPositionAndRotation(
                position + normal * 0.05f, Quaternion.LookRotation(normal));
            ps.Play();
            _active.Add(new ActiveFx { System = ps, DueTime = Time.time + _recycleAfter });
        }
    }
}

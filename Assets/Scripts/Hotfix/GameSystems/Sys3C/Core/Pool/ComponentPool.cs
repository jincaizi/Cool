using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core.Pool
{
    public class ComponentPool<T> where T : Component
    {
        private readonly Queue<T> _pool = new();
        private readonly T _prefab;
        private readonly Transform _parent;

        public ComponentPool(T prefab, Transform parent = null, int prewarmCount = 0)
        {
            _prefab = prefab;
            _parent = parent;
            for (int i = 0; i < prewarmCount; i++)
                _pool.Enqueue(CreateNew());
        }

        public T Get()
        {
            T instance;
            if (_pool.Count > 0)
            {
                instance = _pool.Dequeue();
                instance.gameObject.SetActive(true);
            }
            else
            {
                instance = CreateNew();
            }
            return instance;
        }

        public void Return(T instance)
        {
            if (instance == null) return;
            instance.gameObject.SetActive(false);
            _pool.Enqueue(instance);
        }

        private T CreateNew()
        {
            return Object.Instantiate(_prefab, _parent);
        }
    }
}

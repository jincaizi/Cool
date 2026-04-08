using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.UI.Framework.Core
{
    /// <summary>
    /// Object pool for UI panels with reference counting.
    /// Supports pre-warm and auto-release on reference count reaching 0.
    /// </summary>
    public class UIPool
    {
        private readonly Dictionary<string, PoolData> _pools = new();
        private readonly Dictionary<UIPanel, string> _panelToPool = new();

        /// <summary>
        /// Register a prefab path for pooling.
        /// </summary>
        public void Register(string prefabPath, int preLoadCount = 0)
        {
            if (_pools.ContainsKey(prefabPath))
            {
                Debug.LogWarning($"Pool already registered for: {prefabPath}");
                return;
            }

            var poolData = new PoolData(prefabPath);
            _pools[prefabPath] = poolData;

            // Pre-warm
            for (int i = 0; i < preLoadCount; i++)
            {
                var panel = poolData.Instantiate();
                panel.gameObject.SetActive(false);
                poolData.Return(panel);
            }

            Debug.Log($"UIPool registered: {prefabPath}, preloaded: {preLoadCount}");
        }

        /// <summary>
        /// Get a panel from pool (increments reference count).
        /// </summary>
        public T Get<T>(string prefabPath) where T : UIPanel
        {
            if (!_pools.TryGetValue(prefabPath, out var poolData))
            {
                Debug.LogError($"Pool not registered: {prefabPath}");
                return null;
            }

            var panel = poolData.Get();
            if (panel == null)
            {
                panel = poolData.Instantiate();
            }

            panel.gameObject.SetActive(true);
            _panelToPool[panel] = prefabPath;
            poolData.IncrementRef();
            return panel as T;
        }

        /// <summary>
        /// Return a panel to pool (decrements reference count).
        /// </summary>
        public void Release(UIPanel panel)
        {
            if (panel == null) return;

            if (!_panelToPool.TryGetValue(panel, out var prefabPath))
            {
                Debug.LogWarning($"Panel not from pool: {panel.name}");
                return;
            }

            if (!_pools.TryGetValue(prefabPath, out var poolData))
            {
                Debug.LogWarning($"Pool not found: {prefabPath}");
                return;
            }

            panel.gameObject.SetActive(false);
            poolData.Return(panel);
            poolData.DecrementRef();
            _panelToPool.Remove(panel);
        }

        /// <summary>
        /// Get current reference count for a prefab path.
        /// </summary>
        public int GetRefCount(string prefabPath)
        {
            if (_pools.TryGetValue(prefabPath, out var poolData))
            {
                return poolData.RefCount;
            }
            return 0;
        }

        private class PoolData
        {
            private readonly string _prefabPath;
            private readonly Queue<UIPanel> _available = new();
            private int _refCount;
            private readonly Transform _parent;

            public int RefCount => _refCount;

            public PoolData(string prefabPath)
            {
                _prefabPath = prefabPath;

                // Create invisible parent for pooled objects
                var go = new GameObject($"Pool_{System.IO.Path.GetFileNameWithoutExtension(prefabPath)}");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.SetActive(false);
                _parent = go.transform;
            }

            public UIPanel Instantiate()
            {
                var prefab = Resources.Load<GameObject>(_prefabPath);
                if (prefab == null)
                {
                    // Try direct path without extension
                    prefab = Resources.Load<GameObject>(_prefabPath.Replace(".prefab", ""));
                }
                if (prefab == null)
                {
                    Debug.LogError($"Prefab not found: {_prefabPath}");
                    return null;
                }

                var go = UnityEngine.Object.Instantiate(prefab, _parent);
                var panel = go.GetComponent<UIPanel>();
                if (panel == null)
                {
                    Debug.LogError($"Prefab missing UIPanel component: {_prefabPath}");
                    UnityEngine.Object.Destroy(go);
                    return null;
                }
                return panel;
            }

            public UIPanel Get()
            {
                if (_available.Count > 0)
                {
                    return _available.Dequeue();
                }
                return null;
            }

            public void Return(UIPanel panel)
            {
                panel.transform.SetParent(_parent, false);
                _available.Enqueue(panel);
            }

            public void IncrementRef() => _refCount++;
            public void DecrementRef() => _refCount = Math.Max(0, _refCount - 1);
        }
    }
}

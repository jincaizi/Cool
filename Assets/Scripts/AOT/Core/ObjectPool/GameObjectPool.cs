using System;
using System.Collections.Generic;
using UnityEngine;

namespace AOT.Core.ObjectPool
{
    /// <summary>
    /// 游戏对象池，用于特效、子弹等对象复用
    /// </summary>
    public sealed class GameObjectPool
    {
        private static readonly Lazy<GameObjectPool> _instance = new Lazy<GameObjectPool>(() => new GameObjectPool());
        public static GameObjectPool Instance => _instance.Value;

        private readonly Dictionary<string, Queue<GameObject>> _pooledObjects = new Dictionary<string, Queue<GameObject>>();
        private readonly Dictionary<GameObject, string> _objectToKey = new Dictionary<GameObject, string>();
        private readonly Dictionary<string, GameObject> _prefabs = new Dictionary<string, GameObject>();
        private readonly Transform _poolRoot;
        private bool _disposed;

        private GameObjectPool()
        {
            var go = new GameObject("[ObjectPool_Root]");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _poolRoot = go.transform;
        }

        /// <summary>
        /// 注册预制体
        /// </summary>
        /// <param name="prefab">预制体</param>
        /// <param name="preloadCount">预加载数量</param>
        /// <param name="autoExpand">自动扩展</param>
        public void RegisterPrefab(GameObject prefab, int preloadCount = 0, bool autoExpand = true)
        {
            if (prefab == null) return;

            string key = prefab.name;
            if (_prefabs.ContainsKey(key))
            {
                _prefabs[key] = prefab;
            }
            else
            {
                _prefabs.Add(key, prefab);
            }

            if (!_pooledObjects.ContainsKey(key))
            {
                _pooledObjects[key] = new Queue<GameObject>();
            }

            for (int i = 0; i < preloadCount; i++)
            {
                var obj = UnityEngine.Object.Instantiate(prefab, _poolRoot);
                obj.name = key;
                obj.SetActive(false);
                _pooledObjects[key].Enqueue(obj);
                _objectToKey[obj] = key;
            }
        }

        /// <summary>
        /// 生成对象
        /// </summary>
        /// <param name="prefab">预制体</param>
        /// <param name="position">位置</param>
        /// <param name="rotation">旋转</param>
        /// <returns>生成的对象</returns>
        public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (prefab == null) return null;
            return Spawn(prefab.name, position, rotation);
        }

        /// <summary>
        /// 通过预制体键生成对象
        /// </summary>
        /// <param name="prefabKey">预制体键</param>
        /// <param name="position">位置</param>
        /// <param name="rotation">旋转</param>
        /// <returns>生成的对象</returns>
        public GameObject Spawn(string prefabKey, Vector3 position, Quaternion rotation)
        {
            if (string.IsNullOrEmpty(prefabKey)) return null;
            if (!_prefabs.TryGetValue(prefabKey, out var prefab)) return null;

            GameObject obj;

            if (_pooledObjects.TryGetValue(prefabKey, out var queue) && queue.Count > 0)
            {
                obj = queue.Dequeue();
            }
            else
            {
                obj = UnityEngine.Object.Instantiate(prefab, _poolRoot);
                obj.name = prefabKey;
                _objectToKey[obj] = prefabKey;
            }

            obj.transform.position = position;
            obj.transform.rotation = rotation;
            obj.SetActive(true);
            return obj;
        }

        /// <summary>
        /// 回收对象
        /// </summary>
        /// <param name="obj">对象</param>
        public void Recycle(GameObject obj)
        {
            if (obj == null) return;

            if (_objectToKey.TryGetValue(obj, out var key))
            {
                obj.SetActive(false);
                obj.transform.SetParent(_poolRoot);

                if (!_pooledObjects.ContainsKey(key))
                {
                    _pooledObjects[key] = new Queue<GameObject>();
                }
                _pooledObjects[key].Enqueue(obj);
            }
            else
            {
                UnityEngine.Object.Destroy(obj);
            }
        }

        /// <summary>
        /// 预加载指定数量的对象
        /// </summary>
        /// <param name="prefab">预制体</param>
        /// <param name="count">数量</param>
        public void Preload(GameObject prefab, int count)
        {
            RegisterPrefab(prefab, count);
        }

        /// <summary>
        /// 清空指定键的对象池
        /// </summary>
        /// <param name="key">键</param>
        public void Clear(string key)
        {
            if (_pooledObjects.TryGetValue(key, out var queue))
            {
                while (queue.Count > 0)
                {
                    var obj = queue.Dequeue();
                    if (obj != null)
                    {
                        UnityEngine.Object.Destroy(obj);
                    }
                }
                _pooledObjects.Remove(key);
            }
        }

        /// <summary>
        /// 清空所有对象池
        /// </summary>
        public void ClearAll()
        {
            foreach (var kvp in _pooledObjects)
            {
                while (kvp.Value.Count > 0)
                {
                    var obj = kvp.Value.Dequeue();
                    if (obj != null)
                    {
                        UnityEngine.Object.Destroy(obj);
                    }
                }
            }
            _pooledObjects.Clear();
            _objectToKey.Clear();
        }

        /// <summary>
        /// 获取池中可用对象数量
        /// </summary>
        public int GetPoolCount(string key)
        {
            if (_pooledObjects.TryGetValue(key, out var queue))
            {
                return queue.Count;
            }
            return 0;
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ClearAll();

            if (_poolRoot != null)
            {
                UnityEngine.Object.Destroy(_poolRoot.gameObject);
            }
        }
    }
}

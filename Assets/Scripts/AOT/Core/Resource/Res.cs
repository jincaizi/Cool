using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.SceneManagement;

namespace Core.Resource
{
    public static class Res
    {
        // ===== Async Load =====

        public static UniTask<T> LoadAsync<T>(string key) where T : UnityEngine.Object
        {
            return ResourceManager.Instance.LoadAsync<T>(key);
        }

        public static UniTask<T> LoadAsync<T>(AssetReference reference) where T : UnityEngine.Object
        {
            return ResourceManager.Instance.LoadAsync<T>(reference);
        }

        // ===== Sync Load =====

        public static T Load<T>(string key) where T : UnityEngine.Object
        {
            return ResourceManager.Instance.Load<T>(key);
        }

        public static T Load<T>(AssetReference reference) where T : UnityEngine.Object
        {
            return ResourceManager.Instance.Load<T>(reference);
        }

        // ===== Release =====

        public static void Release(string key)
        {
            ResourceManager.Instance.Release(key);
        }

        public static void Release(AssetReference reference)
        {
            ResourceManager.Instance.Release(reference);
        }

        // ===== Scene =====

        public static UniTask LoadSceneAsync(string key, LoadSceneMode mode = LoadSceneMode.Single)
        {
            return ResourceManager.Instance.LoadSceneAsync(key, mode);
        }

        public static UniTask UnloadSceneAsync(string key)
        {
            return ResourceManager.Instance.UnloadSceneAsync(key);
        }

        // ===== Debug =====

        public static int GetRefCount(string key)
        {
            return ResourceManager.Instance.GetRefCount(key);
        }

        public static IReadOnlyList<string> GetLoadedKeys()
        {
            return ResourceManager.Instance.GetLoadedKeys();
        }
    }
}

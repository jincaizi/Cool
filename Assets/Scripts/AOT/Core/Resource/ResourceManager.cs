using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace Core.Resource
{
    internal sealed class ResourceManager
    {
        private static ResourceManager _instance;
        internal static ResourceManager Instance => _instance ?? (_instance = new ResourceManager());

        private readonly Dictionary<string, HandleEntry> _handles = new Dictionary<string, HandleEntry>();
        private readonly Dictionary<string, SceneInstance> _scenes = new Dictionary<string, SceneInstance>();

        private ResourceManager() { }

        // ===== Async Load =====

        internal async UniTask<T> LoadAsync<T>(string key) where T : UnityEngine.Object
        {
            return await LoadInternalAsync<T>(key);
        }

        internal async UniTask<T> LoadAsync<T>(AssetReference reference) where T : UnityEngine.Object
        {
            if (reference == null)
                throw new ArgumentNullException(nameof(reference));
            return await LoadInternalAsync<T>(reference.RuntimeKey.ToString());
        }

        private async UniTask<T> LoadInternalAsync<T>(string key) where T : UnityEngine.Object
        {
            if (_handles.TryGetValue(key, out var entry))
            {
                entry.RefCount++;
                return (T)entry.Handle.Result;
            }

            try
            {
                var handle = Addressables.LoadAssetAsync<T>(key);
                await handle.Task;

                if (handle.Status == AsyncOperationStatus.Succeeded)
                {
                    _handles[key] = new HandleEntry(handle, typeof(T), key);
                    return handle.Result;
                }

                throw new ResourceLoadException(key,
                    new Exception($"Addressables load failed with status: {handle.Status}"));
            }
            catch (ResourceLoadException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw new ResourceLoadException(key, e);
            }
        }

        // ===== Sync Load =====

        internal T Load<T>(string key) where T : UnityEngine.Object
        {
            return LoadInternalSync<T>(key);
        }

        internal T Load<T>(AssetReference reference) where T : UnityEngine.Object
        {
            if (reference == null)
                throw new ArgumentNullException(nameof(reference));
            return LoadInternalSync<T>(reference.RuntimeKey.ToString());
        }

        private T LoadInternalSync<T>(string key) where T : UnityEngine.Object
        {
            if (_handles.TryGetValue(key, out var entry))
            {
                entry.RefCount++;
                return (T)entry.Handle.Result;
            }

            try
            {
                var handle = Addressables.LoadAssetAsync<T>(key);
                handle.WaitForCompletion();

                if (handle.Status == AsyncOperationStatus.Succeeded)
                {
                    _handles[key] = new HandleEntry(handle, typeof(T), key);
                    return handle.Result;
                }

                throw new ResourceLoadException(key,
                    new Exception($"Addressables load failed with status: {handle.Status}"));
            }
            catch (ResourceLoadException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw new ResourceLoadException(key, e);
            }
        }

        // ===== Release =====

        internal void Release(string key)
        {
            if (!_handles.TryGetValue(key, out var entry))
                return;

            entry.RefCount--;
            if (entry.RefCount <= 0)
            {
                Addressables.Release(entry.Handle);
                _handles.Remove(key);
            }
        }

        internal void Release(AssetReference reference)
        {
            if (reference == null)
                return;
            Release(reference.RuntimeKey.ToString());
        }

        // ===== Scene =====

        internal async UniTask LoadSceneAsync(string key, LoadSceneMode mode)
        {
            var handle = Addressables.LoadSceneAsync(key, mode);
            await handle.Task;

            if (handle.Status != AsyncOperationStatus.Succeeded)
                throw new ResourceLoadException(key,
                    new Exception($"Scene load failed with status: {handle.Status}"));

            _scenes[key] = handle.Result;
        }

        internal async UniTask UnloadSceneAsync(string key)
        {
            if (!_scenes.TryGetValue(key, out var sceneInstance))
                return;

            var handle = Addressables.UnloadSceneAsync(sceneInstance);
            await handle.Task;
            _scenes.Remove(key);
        }

        // ===== Debug =====

        internal int GetRefCount(string key)
        {
            return _handles.TryGetValue(key, out var entry) ? entry.RefCount : -1;
        }

        internal IReadOnlyList<string> GetLoadedKeys()
        {
            return new List<string>(_handles.Keys);
        }
    }
}

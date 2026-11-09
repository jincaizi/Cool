using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace Core.Resource
{
    // Calls and reference bookkeeping belong to Unity's main thread.
    internal sealed class ResourceManager
    {
        private static ResourceManager _instance;
        internal static ResourceManager Instance => _instance ?? (_instance = new ResourceManager());
        private readonly Dictionary<string, HandleEntry> _handles = new Dictionary<string, HandleEntry>();
        private readonly Dictionary<string, SceneInstance> _scenes = new Dictionary<string, SceneInstance>();
        private readonly IResourceAssetBackend _backend;

        internal ResourceManager(IResourceAssetBackend backend = null)
        {
            _backend = backend ?? new AddressableAssetBackend();
        }

        internal UniTask<T> LoadAsync<T>(string key, CancellationToken cancellationToken = default) where T : UnityEngine.Object
            => LoadInternalAsync<T>(key, cancellationToken);

        internal UniTask<T> LoadAsync<T>(AssetReference reference, CancellationToken cancellationToken = default) where T : UnityEngine.Object
        {
            if (reference == null) throw new ArgumentNullException(nameof(reference));
            return LoadInternalAsync<T>(reference.RuntimeKey.ToString(), cancellationToken);
        }

        private async UniTask<T> LoadInternalAsync<T>(string key, CancellationToken cancellationToken) where T : UnityEngine.Object
        {
            cancellationToken.ThrowIfCancellationRequested();
            HandleEntry entry;
            if (_handles.TryGetValue(key, out entry))
            {
                // Base/derived declarations can share the pending operation. Validate the actual result below.
                if (!typeof(T).IsAssignableFrom(entry.AssetType) && !entry.AssetType.IsAssignableFrom(typeof(T)))
                    throw TypeMismatch(key, entry.AssetType, typeof(T));
                entry.RefCount++;
            }
            else
            {
                try
                {
                    var handle = _backend.Load<T>(key);
                    entry = new HandleEntry(handle, typeof(T), key);
                    _handles.Add(key, entry); // Publish before completion/any consumer continuation.
                    var flight = entry;
                    if (handle.IsDone) Complete(flight);
                    else handle.Completed += _ => Complete(flight);
                }
                catch (Exception e)
                {
                    throw new ResourceLoadException(key, e);
                }
            }

            bool acquired = false;
            try
            {
                var asset = await entry.Completion.Task.AttachExternalCancellation(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (!(asset is T typedAsset)) throw TypeMismatch(key, asset == null ? entry.AssetType : asset.GetType(), typeof(T));
                acquired = true;
                return typedAsset;
            }
            finally
            {
                if (!acquired)
                {
                    // Cancellation may originate on a worker; never touch Addressables/dictionaries there.
                    await UniTask.SwitchToMainThread();
                    ReleaseEntry(entry);
                }
            }
        }

        private static ResourceLoadException TypeMismatch(string key, Type loaded, Type requested)
            => new ResourceLoadException(key, new InvalidCastException("Asset '" + key + "' loaded as " + loaded.Name + ", cannot return as " + requested.Name));

        private void Complete(HandleEntry entry)
        {
            if (entry.Finished) return;
            entry.Finished = true;
            if (entry.Handle.Status != AsyncOperationStatus.Succeeded)
            {
                var failure = new ResourceLoadException(entry.Key, entry.Handle.OperationException ?? new Exception("Addressables load failed"));
                RemoveAndRelease(entry); // Evict before resuming a caller that might retry immediately.
                entry.Completion.TrySetException(failure);
                return;
            }

            var asset = entry.Handle.Result as UnityEngine.Object;
            if (asset != null) entry.AssetType = asset.GetType();
            entry.Completion.TrySetResult(asset);
            if (entry.RefCount == 0) RemoveAndRelease(entry);
        }

        private void ReleaseEntry(HandleEntry entry)
        {
            if (entry.RefCount > 0) entry.RefCount--;
            // All canceled consumers still leave the shared operation observed until it finishes.
            if (entry.RefCount == 0 && entry.Finished) RemoveAndRelease(entry);
        }

        private void RemoveAndRelease(HandleEntry entry)
        {
            if (entry.Released) return;
            entry.Released = true;
            if (_handles.TryGetValue(entry.Key, out var current) && ReferenceEquals(current, entry))
                _handles.Remove(entry.Key);
            if (entry.Handle.IsValid()) _backend.Release(entry.Handle);
        }

        // Legacy synchronous API remains for existing callers; new player paths must use LoadAsync.
        internal T Load<T>(string key) where T : UnityEngine.Object
        {
            if (_handles.TryGetValue(key, out var entry))
            {
                if (!entry.Finished) throw new ResourceLoadException(key, new InvalidOperationException("An asynchronous load is still pending."));
                if (!(entry.Handle.Result is T asset)) throw TypeMismatch(key, entry.AssetType, typeof(T));
                entry.RefCount++;
                return asset;
            }
            var handle = _backend.Load<T>(key);
            try
            {
                handle.WaitForCompletion();
                if (handle.Status != AsyncOperationStatus.Succeeded)
                    throw new ResourceLoadException(key, handle.OperationException ?? new Exception("Addressables load failed"));
                var result = (T)handle.Result;
                var loaded = new HandleEntry(handle, result == null ? typeof(T) : result.GetType(), key) { Finished = true };
                loaded.Completion.TrySetResult(result);
                _handles.Add(key, loaded);
                return result;
            }
            catch (Exception e)
            {
                if (handle.IsValid()) _backend.Release(handle);
                if (e is ResourceLoadException) throw;
                throw new ResourceLoadException(key, e);
            }
        }

        internal T Load<T>(AssetReference reference) where T : UnityEngine.Object
        {
            if (reference == null) throw new ArgumentNullException(nameof(reference));
            return Load<T>(reference.RuntimeKey.ToString());
        }

        internal void Release(string key)
        {
            if (_handles.TryGetValue(key, out var entry)) ReleaseEntry(entry);
        }

        internal void Release(AssetReference reference)
        {
            if (reference != null) Release(reference.RuntimeKey.ToString());
        }

        // Native UniTask completion bridge: no System.Threading.Tasks dependency on WebGL.
        private static UniTask AwaitCompletion(AsyncOperationHandle handle)
        {
            if (handle.IsDone) return UniTask.CompletedTask;
            var completion = new UniTaskCompletionSource();
            handle.Completed += _ => completion.TrySetResult();
            return completion.Task;
        }

        internal async UniTask LoadSceneAsync(string key, LoadSceneMode mode)
        {
            var handle = Addressables.LoadSceneAsync(key, mode);
            await AwaitCompletion(handle);
            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                var failure = handle.OperationException ?? new Exception("Scene load failed");
                if (handle.IsValid()) Addressables.Release(handle);
                throw new ResourceLoadException(key, failure);
            }
            if (_scenes.ContainsKey(key))
                throw new ResourceLoadException(key, new InvalidOperationException("Scene '" + key + "' is already loaded. Unload it first."));
            _scenes[key] = handle.Result;
        }

        internal async UniTask UnloadSceneAsync(string key)
        {
            if (!_scenes.TryGetValue(key, out var sceneInstance)) return;
            _scenes.Remove(key);
            var handle = Addressables.UnloadSceneAsync(sceneInstance, autoReleaseHandle: false);
            try { await AwaitCompletion(handle); }
            finally { if (handle.IsValid()) Addressables.Release(handle); }
        }

        internal int GetRefCount(string key) => _handles.TryGetValue(key, out var entry) ? entry.RefCount : -1;
        internal IReadOnlyList<string> GetLoadedKeys() => new List<string>(_handles.Keys);
    }
}

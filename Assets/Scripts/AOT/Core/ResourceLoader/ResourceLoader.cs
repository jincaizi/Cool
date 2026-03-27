using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;

namespace AOT.Core.ResourceLoader
{
    /// <summary>
    /// 资源加载器，封装Addressable异步加载
    /// </summary>
    public sealed class ResourceLoader
    {
        private static readonly Lazy<ResourceLoader> _instance = new Lazy<ResourceLoader>(() => new ResourceLoader());
        public static ResourceLoader Instance => _instance.Value;

        private bool _disposed;

        private ResourceLoader() { }

        /// <summary>
        /// 异步加载游戏对象
        /// </summary>
        /// <param name="address">资源地址</param>
        /// <returns>加载任务</returns>
        public Task<GameObject> LoadGameObjectAsync(string address)
        {
            var tcs = new TaskCompletionSource<GameObject>();

            Addressables.LoadAssetAsync<GameObject>(address).Completed += (handle) =>
            {
                if (handle.Status == AsyncOperationStatus.Succeeded)
                {
                    tcs.TrySetResult(handle.Result);
                }
                else
                {
                    tcs.TrySetException(new Exception($"Failed to load GameObject at {address}: {handle.OperationException}"));
                }
            };

            return tcs.Task;
        }

        /// <summary>
        /// 异步加载游戏对象（带回调）
        /// </summary>
        /// <param name="address">资源地址</param>
        /// <param name="onComplete">完成回调</param>
        public void LoadGameObjectAsync(string address, Action<GameObject> onComplete)
        {
            Addressables.LoadAssetAsync<GameObject>(address).Completed += (handle) =>
            {
                if (handle.Status == AsyncOperationStatus.Succeeded)
                {
                    onComplete?.Invoke(handle.Result);
                }
                else
                {
                    Debug.LogError($"[ResourceLoader] Failed to load GameObject at {address}: {handle.OperationException}");
                    onComplete?.Invoke(null);
                }
            };
        }

        /// <summary>
        /// 异步加载Sprite
        /// </summary>
        /// <param name="address">资源地址</param>
        /// <returns>加载任务</returns>
        public Task<Sprite> LoadSpriteAsync(string address)
        {
            var tcs = new TaskCompletionSource<Sprite>();

            Addressables.LoadAssetAsync<Sprite>(address).Completed += (handle) =>
            {
                if (handle.Status == AsyncOperationStatus.Succeeded)
                {
                    tcs.TrySetResult(handle.Result);
                }
                else
                {
                    tcs.TrySetException(new Exception($"Failed to load Sprite at {address}: {handle.OperationException}"));
                }
            };

            return tcs.Task;
        }

        /// <summary>
        /// 异步加载Sprite（带回调）
        /// </summary>
        /// <param name="address">资源地址</param>
        /// <param name="onComplete">完成回调</param>
        public void LoadSpriteAsync(string address, Action<Sprite> onComplete)
        {
            Addressables.LoadAssetAsync<Sprite>(address).Completed += (handle) =>
            {
                if (handle.Status == AsyncOperationStatus.Succeeded)
                {
                    onComplete?.Invoke(handle.Result);
                }
                else
                {
                    Debug.LogError($"[ResourceLoader] Failed to load Sprite at {address}: {handle.OperationException}");
                    onComplete?.Invoke(null);
                }
            };
        }

        /// <summary>
        /// 异步加载动画控制器
        /// </summary>
        /// <param name="address">资源地址</param>
        /// <returns>加载任务</returns>
        public Task<RuntimeAnimatorController> LoadAnimatorControllerAsync(string address)
        {
            var tcs = new TaskCompletionSource<RuntimeAnimatorController>();

            Addressables.LoadAssetAsync<RuntimeAnimatorController>(address).Completed += (handle) =>
            {
                if (handle.Status == AsyncOperationStatus.Succeeded)
                {
                    tcs.TrySetResult(handle.Result);
                }
                else
                {
                    tcs.TrySetException(new Exception($"Failed to load AnimatorController at {address}: {handle.OperationException}"));
                }
            };

            return tcs.Task;
        }

        /// <summary>
        /// 异步实例化游戏对象
        /// </summary>
        /// <param name="address">资源地址</param>
        /// <param name="position">位置</param>
        /// <param name="rotation">旋转</param>
        /// <param name="parent">父对象</param>
        /// <returns>实例化任务</returns>
        public Task<GameObject> InstantiateAsync(string address, Vector3 position, Quaternion rotation, Transform? parent = null)
        {
            var tcs = new TaskCompletionSource<GameObject>();

            Addressables.LoadAssetAsync<GameObject>(address).Completed += (handle) =>
            {
                if (handle.Status == AsyncOperationStatus.Succeeded)
                {
                    var instance = UnityEngine.Object.Instantiate(handle.Result, position, rotation, parent);
                    tcs.TrySetResult(instance);
                }
                else
                {
                    tcs.TrySetException(new Exception($"Failed to instantiate GameObject at {address}: {handle.OperationException}"));
                }
            };

            return tcs.Task;
        }

        /// <summary>
        /// 释放资源实例
        /// </summary>
        /// <param name="obj">对象</param>
        public void ReleaseInstance(GameObject obj)
        {
            if (obj == null) return;
            Addressables.ReleaseInstance(obj);
        }

        /// <summary>
        /// 释放指定资源
        /// </summary>
        /// <param name="obj">对象</param>
        public void Release<T>(T obj) where T : UnityEngine.Object
        {
            if (obj == null) return;
            Addressables.Release(obj);
        }

        /// <summary>
        /// 预加载资源
        /// </summary>
        /// <param name="addresses">地址列表</param>
        public async Task PreloadAsync(string[] addresses)
        {
            var tasks = new Task[addresses.Length];
            for (int i = 0; i < addresses.Length; i++)
            {
                tasks[i] = LoadGameObjectAsync(addresses[i]);
            }
            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}

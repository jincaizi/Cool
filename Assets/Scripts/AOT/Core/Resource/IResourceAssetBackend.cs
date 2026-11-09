using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Core.Resource
{
    // Owns only the external asset operation; lifecycle and consumer references stay in ResourceManager.
    internal interface IResourceAssetBackend
    {
        AsyncOperationHandle Load<T>(string key) where T : UnityEngine.Object;
        void Release(AsyncOperationHandle handle);
    }

    internal sealed class AddressableAssetBackend : IResourceAssetBackend
    {
        public AsyncOperationHandle Load<T>(string key) where T : UnityEngine.Object
            => Addressables.LoadAssetAsync<T>(key);
        public void Release(AsyncOperationHandle handle) => Addressables.Release(handle);
    }
}

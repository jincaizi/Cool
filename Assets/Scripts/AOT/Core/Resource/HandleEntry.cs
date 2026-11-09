using System;
using Cysharp.Threading.Tasks;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Core.Resource
{
    internal sealed class HandleEntry
    {
        public readonly AsyncOperationHandle Handle;
        public readonly UniTaskCompletionSource<UnityEngine.Object> Completion = new UniTaskCompletionSource<UnityEngine.Object>();
        public int RefCount = 1;
        public Type AssetType;
        public readonly string Key;
        public bool Finished;
        public bool Released;

        public HandleEntry(AsyncOperationHandle handle, Type assetType, string key)
        {
            Handle = handle;
            AssetType = assetType;
            Key = key;
        }
    }
}

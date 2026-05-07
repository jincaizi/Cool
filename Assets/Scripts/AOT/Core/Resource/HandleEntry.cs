using System;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Core.Resource
{
    internal sealed class HandleEntry
    {
        public AsyncOperationHandle Handle;
        public int RefCount;
        public Type AssetType;
        public string Key;

        public HandleEntry(AsyncOperationHandle handle, Type assetType, string key)
        {
            Handle = handle;
            AssetType = assetType;
            Key = key;
            RefCount = 1;
        }
    }
}

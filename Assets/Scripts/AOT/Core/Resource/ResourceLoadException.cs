using System;

namespace Core.Resource
{
    public class ResourceLoadException : Exception
    {
        public string Key { get; }

        public ResourceLoadException(string key, Exception inner)
            : base($"Failed to load resource: {key}", inner)
        {
            Key = key;
        }
    }
}

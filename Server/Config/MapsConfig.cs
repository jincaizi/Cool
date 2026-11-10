using System.Collections.Generic;

namespace KcpServer.Config
{
    public sealed class MapsConfig
    {
        public List<MapEntry> maps { get; set; } = new();
    }

    public sealed class MapEntry
    {
        public string id = string.Empty;
        public string navFile = string.Empty;
        public Vector3? playerSpawn;
    }
}

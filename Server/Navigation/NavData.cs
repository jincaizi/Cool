namespace KcpServer.Navigation
{
    public sealed class NavData
    {
        public string MapId = string.Empty;
        public ulong SourceHash;
        public ulong CanonicalHash;
        public float MinX, MinZ, MaxX, MaxZ;
        public float TileSize;
        public float[] AreaCosts = System.Array.Empty<float>();
        public Vector3[] Vertices = System.Array.Empty<Vector3>();
        public NavTileData[] Tiles = System.Array.Empty<NavTileData>();
    }

    public sealed class NavTileData
    {
        public short TileX, TileZ;
        public int[] Indices = System.Array.Empty<int>(); // 全局顶点索引，长度 = triangleCount * 3
        public byte[] Areas = System.Array.Empty<byte>(); // 长度 = triangleCount
    }
}

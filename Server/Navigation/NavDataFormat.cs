using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace KcpServer.Navigation
{
    public static class NavDataFormat
    {
        private static readonly byte[] Magic = { (byte)'N', (byte)'A', (byte)'V', (byte)'1' };
        private const ushort Version = 1;

        public static void Write(string path, NavData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            Validate(data); // 先校验：任何一行抛错都不应该留下半截文件
            data.CanonicalHash = ComputeCanonicalHash(data.Vertices, data.Tiles);

            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // temp + move：崩溃/磁盘满不会把已提交的 navbin 覆盖成半文件。
            string tempPath = path + ".tmp";
            try
            {
                using (FileStream stream = File.Create(tempPath))
                using (var w = new BinaryWriter(stream))
                {
                    WriteContent(w, data);
                }
                File.Move(tempPath, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        private static void WriteContent(BinaryWriter w, NavData data)
        {
            w.Write(Magic);
            w.Write(Version);

            byte[] map = Encoding.UTF8.GetBytes(data.MapId ?? string.Empty);
            w.Write((ushort)map.Length);
            w.Write(map);
            w.Write(data.SourceHash);
            w.Write(data.CanonicalHash);
            w.Write(data.MinX); w.Write(data.MinZ); w.Write(data.MaxX); w.Write(data.MaxZ);
            w.Write(data.TileSize);
            w.Write((ushort)data.Tiles.Length);
            w.Write((byte)data.AreaCosts.Length);
            foreach (float cost in data.AreaCosts) w.Write(cost);

            w.Write((uint)data.Vertices.Length);
            foreach (Vector3 v in data.Vertices)
            {
                w.Write(v.X); w.Write(v.Y); w.Write(v.Z);
            }

            foreach (NavTileData tile in data.Tiles)
            {
                w.Write(tile.TileX);
                w.Write(tile.TileZ);
                w.Write((uint)(tile.Indices.Length / 3));
                foreach (int index in tile.Indices) w.Write((uint)index);
                foreach (byte area in tile.Areas) w.Write(area);
            }
        }

        /// <summary>写入前的完整校验（索引范围/计数上限/数组形状）。</summary>
        private static void Validate(NavData data)
        {
            if (data.Vertices == null) throw new InvalidDataException("vertices null");
            if (data.Tiles == null) throw new InvalidDataException("tiles null");
            if (data.AreaCosts == null) throw new InvalidDataException("areaCosts null");
            if (Encoding.UTF8.GetByteCount(data.MapId ?? string.Empty) > ushort.MaxValue)
                throw new InvalidDataException("mapId too long");
            if (data.Tiles.Length > ushort.MaxValue) throw new InvalidDataException("too many tiles");
            if (data.AreaCosts.Length > byte.MaxValue) throw new InvalidDataException("areaCosts too many");

            int vertexCount = data.Vertices.Length;
            foreach (NavTileData tile in data.Tiles)
            {
                if (tile.Indices == null || tile.Areas == null) throw new InvalidDataException("tile arrays null");
                if (tile.Indices.Length % 3 != 0) throw new InvalidDataException("indices not multiple of 3");
                if (tile.Areas.Length != tile.Indices.Length / 3) throw new InvalidDataException("areas mismatch");
                foreach (int index in tile.Indices)
                {
                    if (index < 0 || index >= vertexCount) throw new InvalidDataException("index out of range");
                }
            }
        }

        public static NavData Read(string path)
        {
            using FileStream stream = File.OpenRead(path);
            using var r = new BinaryReader(stream);

            byte[] magic = r.ReadBytes(4);
            if (magic.Length != 4 || magic[0] != Magic[0] || magic[1] != Magic[1]
                || magic[2] != Magic[2] || magic[3] != Magic[3])
                throw new InvalidDataException("bad magic");

            ushort version = r.ReadUInt16();
            if (version != Version) throw new InvalidDataException($"unsupported version {version}");

            int mapLength = r.ReadUInt16();
            string mapId = Encoding.UTF8.GetString(r.ReadBytes(mapLength));

            var data = new NavData
            {
                MapId = mapId,
                SourceHash = r.ReadUInt64(),
                CanonicalHash = r.ReadUInt64(),
                MinX = r.ReadSingle(),
                MinZ = r.ReadSingle(),
                MaxX = r.ReadSingle(),
                MaxZ = r.ReadSingle(),
                TileSize = r.ReadSingle()
            };

            int tileCount = r.ReadUInt16();
            int areaCount = r.ReadByte();
            var costs = new float[areaCount];
            for (int i = 0; i < areaCount; i++) costs[i] = r.ReadSingle();
            data.AreaCosts = costs;

            uint vertexCount = r.ReadUInt32();
            if (vertexCount > 5_000_000) throw new InvalidDataException("vertexCount too large");
            var vertices = new Vector3[vertexCount];
            for (int i = 0; i < vertices.Length; i++)
                vertices[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            data.Vertices = vertices;

            var tiles = new NavTileData[tileCount];
            for (int i = 0; i < tileCount; i++)
            {
                short tileX = r.ReadInt16();
                short tileZ = r.ReadInt16();
                uint triangleCount = r.ReadUInt32();
                if (triangleCount > 5_000_000) throw new InvalidDataException("triangleCount too large");
                var indices = new int[checked((int)(triangleCount * 3))];
                for (int k = 0; k < indices.Length; k++)
                {
                    uint index = r.ReadUInt32();
                    if (index >= vertexCount) throw new InvalidDataException("index out of range");
                    indices[k] = (int)index;
                }
                var areas = new byte[triangleCount];
                for (int k = 0; k < areas.Length; k++) areas[k] = r.ReadByte();
                tiles[i] = new NavTileData { TileX = tileX, TileZ = tileZ, Indices = indices, Areas = areas };
            }
            data.Tiles = tiles;

            ulong actual = ComputeCanonicalHash(data.Vertices, data.Tiles);
            if (actual != data.CanonicalHash)
                throw new InvalidDataException($"canonical hash mismatch: file={data.CanonicalHash} actual={actual}");

            return data;
        }

        public static ulong ComputeCanonicalHash(Vector3[] vertices, NavTileData[] tiles)
        {
            ulong hash = 14695981039346656037UL;
            foreach (Vector3 v in vertices)
            {
                hash = Mix(hash, BitConverter.SingleToInt32Bits(v.X));
                hash = Mix(hash, BitConverter.SingleToInt32Bits(v.Y));
                hash = Mix(hash, BitConverter.SingleToInt32Bits(v.Z));
            }
            foreach (NavTileData tile in tiles)
            {
                hash = Mix(hash, tile.Indices.Length);
                foreach (int index in tile.Indices) hash = Mix(hash, index);
                foreach (byte area in tile.Areas) hash = Mix(hash, area);
            }
            return hash;
        }

        private static ulong Mix(ulong hash, int value)
        {
            uint u = (uint)value;
            for (int shift = 0; shift < 32; shift += 8)
            {
                hash ^= (byte)(u >> shift);
                hash *= 1099511628211UL;
            }
            return hash;
        }

        private static ulong Mix(ulong hash, byte value)
        {
            hash ^= value;
            hash *= 1099511628211UL;
            return hash;
        }
    }
}

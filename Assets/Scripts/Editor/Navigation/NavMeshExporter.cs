using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace GameSys.EditorNavigation
{
    public static class NavMeshExporter
    {
        public const string DemoDayScenePath = "Assets/SimpleLowPolyNature/Scenes/DemoDay.unity";
        public const string DemoDayMapId = "demoday";
        public const string OutputPath = "Server/Config/nav/demoday.navbin";
        private const float Quantize = 1000f;
        private const float TileSize = 64f;
        private const byte AreaCount = 32;
        private const ulong FnvOffset = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        private sealed class ExportData
        {
            public string MapId = string.Empty;
            public ulong SourceHash;
            public ulong CanonicalHash;
            public float MinX, MinZ, MaxX, MaxZ;
            public float[] AreaCosts = Array.Empty<float>();
            public Vector3[] Vertices = Array.Empty<Vector3>();
            public List<TileData> Tiles = new();
        }

        private sealed class TileData
        {
            public short TileX, TileZ;
            public List<int> Indices = new();
            public List<byte> Areas = new();
        }

        [MenuItem("Tools/Navigation/Export Server NavMesh")]
        public static void ExportActiveScene()
        {
            if (SceneManager.GetActiveScene().path != DemoDayScenePath)
            {
                Debug.LogError($"[NavMeshExporter] 请先打开 {DemoDayScenePath}");
                return;
            }
            Export(DemoDayMapId, Path.Combine(Directory.GetCurrentDirectory(), OutputPath));
        }

        [MenuItem("Tools/Navigation/Rebake and Export Server NavMesh")]
        public static void RebakeAndExport()
        {
            if (SceneManager.GetActiveScene().path != DemoDayScenePath)
            {
                Debug.LogError($"[NavMeshExporter] 请先打开 {DemoDayScenePath}");
                return;
            }
            NavMeshSurface[] surfaces = UnityEngine.Object.FindObjectsOfType<NavMeshSurface>(true);
            foreach (NavMeshSurface surface in surfaces)
            {
                surface.BuildNavMesh();
                EditorUtility.SetDirty(surface);
            }
            AssetDatabase.SaveAssets();
            ExportActiveScene();
        }

        public static void ExportDemoDay()
        {
            EditorSceneManager.OpenScene(DemoDayScenePath, OpenSceneMode.Single);
            ExportActiveScene();
        }

        /// <summary>EditMode 新鲜度测试入口：不写文件，只算当前场景的规范 hash。</summary>
        public static ulong ComputeCanonicalHashForActiveScene()
            => ComputeCanonicalHash(BuildNavData());

        /// <summary>场景 + navmesh 资产 hash；用于“场景改了但没重烘”的 warning。</summary>
        public static ulong ComputeSourceHashForActiveScene() => ComputeSourceHash();

        public static void Export(string mapId, string path)
        {
            ExportData data = BuildNavData();
            data.MapId = mapId;
            data.CanonicalHash = ComputeCanonicalHash(data);
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            using FileStream stream = File.Create(path);
            using var w = new BinaryWriter(stream);
            w.Write(new[] { (byte)'N', (byte)'A', (byte)'V', (byte)'1' });
            w.Write((ushort)1);
            byte[] map = Encoding.UTF8.GetBytes(mapId);
            w.Write((ushort)map.Length); w.Write(map);
            w.Write(data.SourceHash); w.Write(data.CanonicalHash);
            w.Write(data.MinX); w.Write(data.MinZ); w.Write(data.MaxX); w.Write(data.MaxZ);
            w.Write(TileSize);
            w.Write((ushort)data.Tiles.Count);
            w.Write(AreaCount);
            foreach (float cost in data.AreaCosts) w.Write(cost);
            w.Write((uint)data.Vertices.Length);
            foreach (Vector3 v in data.Vertices) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
            foreach (TileData tile in data.Tiles)
            {
                w.Write(tile.TileX); w.Write(tile.TileZ);
                w.Write((uint)(tile.Indices.Count / 3));
                foreach (int i in tile.Indices) w.Write((uint)i);
                foreach (byte a in tile.Areas) w.Write(a);
            }

            Debug.Log($"[NavMeshExporter] {path} triangles={CountTriangles(data)} vertices={data.Vertices.Length} " +
                $"tiles={data.Tiles.Count} hash={data.CanonicalHash:X16}");
        }

        public static ulong ReadCanonicalHash(string path)
        {
            using FileStream stream = File.OpenRead(path);
            using var r = new BinaryReader(stream);
            r.ReadBytes(4);
            r.ReadUInt16();
            int len = r.ReadUInt16();
            r.ReadBytes(len);
            r.ReadUInt64();
            return r.ReadUInt64();
        }

        public static ulong ReadSourceHash(string path)
        {
            using FileStream stream = File.OpenRead(path);
            using var r = new BinaryReader(stream);
            r.ReadBytes(4);
            r.ReadUInt16();
            int len = r.ReadUInt16();
            r.ReadBytes(len);
            return r.ReadUInt64();
        }

        private static ExportData BuildNavData()
        {
            NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
            if (tri.vertices == null || tri.indices == null || tri.indices.Length == 0)
                throw new InvalidOperationException("No navmesh triangulation. Open DemoDay and check NavMeshSurface.");

            var unique = new Dictionary<(int, int, int), int>();
            var vertices = new List<Vector3>();
            int[] remap = new int[tri.vertices.Length];
            for (int i = 0; i < tri.vertices.Length; i++)
            {
                Vector3 v = tri.vertices[i];
                int qx = Mathf.RoundToInt(v.x * Quantize);
                int qy = Mathf.RoundToInt(v.y * Quantize);
                int qz = Mathf.RoundToInt(v.z * Quantize);
                if (!unique.TryGetValue((qx, qy, qz), out int index))
                {
                    index = vertices.Count;
                    vertices.Add(new Vector3(qx / Quantize, qy / Quantize, qz / Quantize));
                    unique[(qx, qy, qz)] = index;
                }
                remap[i] = index;
            }

            var triangles = new List<(int A, int B, int C, byte Area)>();
            for (int i = 0; i < tri.indices.Length; i += 3)
            {
                int a = remap[tri.indices[i]];
                int b = remap[tri.indices[i + 1]];
                int c = remap[tri.indices[i + 2]];
                if (a == b || b == c || c == a) continue;
                Vector3 va = vertices[a], vb = vertices[b], vc = vertices[c];
                float area2 = (vb.x - va.x) * (vc.z - va.z) - (vb.z - va.z) * (vc.x - va.x);
                if (Mathf.Abs(area2) < 1e-6f) continue;
                if (area2 < 0f) (b, c) = (c, b);
                byte area = tri.areas != null && tri.areas.Length > i / 3
                    ? (byte)Mathf.Clamp(tri.areas[i / 3], 0, AreaCount - 1)
                    : (byte)0;
                triangles.Add((a, b, c, area));
            }

            var order = new List<int>(vertices.Count);
            for (int i = 0; i < vertices.Count; i++) order.Add(i);
            order.Sort((x, y) =>
            {
                Vector3 a = vertices[x], b = vertices[y];
                int cmp = a.x.CompareTo(b.x);
                if (cmp != 0) return cmp;
                cmp = a.z.CompareTo(b.z);
                return cmp != 0 ? cmp : a.y.CompareTo(b.y);
            });

            var oldToNew = new int[vertices.Count];
            var sortedVertices = new Vector3[vertices.Count];
            for (int i = 0; i < order.Count; i++)
            {
                oldToNew[order[i]] = i;
                sortedVertices[i] = vertices[order[i]];
            }

            for (int i = 0; i < triangles.Count; i++)
            {
                var t = triangles[i];
                triangles[i] = (oldToNew[t.A], oldToNew[t.B], oldToNew[t.C], t.Area);
            }

            triangles.Sort((x, y) =>
            {
                int cmp = Min3(x.A, x.B, x.C).CompareTo(Min3(y.A, y.B, y.C));
                if (cmp != 0) return cmp;
                cmp = Max3(x.A, x.B, x.C).CompareTo(Max3(y.A, y.B, y.C));
                return cmp != 0 ? cmp : x.Area.CompareTo(y.Area);
            });

            var min = new Vector3(float.MaxValue, 0f, float.MaxValue);
            var max = new Vector3(float.MinValue, 0f, float.MinValue);
            foreach (Vector3 v in sortedVertices)
            {
                min.x = Mathf.Min(min.x, v.x); min.z = Mathf.Min(min.z, v.z);
                max.x = Mathf.Max(max.x, v.x); max.z = Mathf.Max(max.z, v.z);
            }

            var tileMap = new Dictionary<(int, int), TileData>();
            foreach (var t in triangles)
            {
                Vector3 a = sortedVertices[t.A], b = sortedVertices[t.B], c = sortedVertices[t.C];
                float cx = (a.x + b.x + c.x) / 3f;
                float cz = (a.z + b.z + c.z) / 3f;
                int tx = Mathf.FloorToInt(cx / TileSize);
                int tz = Mathf.FloorToInt(cz / TileSize);
                if (!tileMap.TryGetValue((tx, tz), out TileData? tile))
                {
                    tile = new TileData { TileX = (short)tx, TileZ = (short)tz };
                    tileMap[(tx, tz)] = tile;
                }
                tile.Indices.Add(t.A); tile.Indices.Add(t.B); tile.Indices.Add(t.C);
                tile.Areas.Add(t.Area);
            }

            var tiles = new List<TileData>(tileMap.Values);
            tiles.Sort((x, y) => x.TileX != y.TileX
                ? x.TileX.CompareTo(y.TileX)
                : x.TileZ.CompareTo(y.TileZ));

            return new ExportData
            {
                MapId = DemoDayMapId,
                SourceHash = ComputeSourceHash(),
                MinX = min.x, MinZ = min.z, MaxX = max.x, MaxZ = max.z,
                AreaCosts = ReadAreaCosts(),
                Vertices = sortedVertices,
                Tiles = tiles
            };
        }

        private static ulong ComputeCanonicalHash(ExportData data)
        {
            ulong hash = FnvOffset;
            foreach (Vector3 v in data.Vertices)
            {
                hash = Mix(hash, BitConverter.SingleToInt32Bits(v.x));
                hash = Mix(hash, BitConverter.SingleToInt32Bits(v.y));
                hash = Mix(hash, BitConverter.SingleToInt32Bits(v.z));
            }
            foreach (TileData tile in data.Tiles)
            {
                hash = Mix(hash, tile.Indices.Count);
                foreach (int i in tile.Indices) hash = Mix(hash, i);
                foreach (byte a in tile.Areas) hash = Mix(hash, a);
            }
            return hash;
        }

        private static float[] ReadAreaCosts()
        {
            var costs = new float[AreaCount];
            for (int i = 0; i < costs.Length; i++)
            {
                try { costs[i] = NavMesh.GetAreaCost(i); }
                catch { costs[i] = 1f; }
            }
            return costs;
        }

        private static ulong ComputeSourceHash()
        {
            ulong hash = FnvOffset;
            NavMeshSurface? surface = UnityEngine.Object.FindObjectOfType<NavMeshSurface>();
            if (surface != null && surface.navMeshData != null)
            {
                string navPath = AssetDatabase.GetAssetPath(surface.navMeshData);
                if (!string.IsNullOrEmpty(navPath) && File.Exists(navPath))
                {
                    foreach (byte b in File.ReadAllBytes(navPath)) { hash ^= b; hash *= FnvPrime; }
                }
            }

            string scenePath = SceneManager.GetActiveScene().path;
            if (!string.IsNullOrEmpty(scenePath) && File.Exists(scenePath))
            {
                foreach (byte b in File.ReadAllBytes(scenePath)) { hash ^= b; hash *= FnvPrime; }
            }
            return hash;
        }

        private static ulong Mix(ulong hash, int value)
        {
            uint u = (uint)value;
            for (int shift = 0; shift < 32; shift += 8)
            {
                hash ^= (byte)(u >> shift);
                hash *= FnvPrime;
            }
            return hash;
        }

        // 与服务端 NavDataFormat.Mix(ulong, byte) 保持同构；缺此重载时 byte 会走 int 重载，hash 与 navbin 校验不一致。
        private static ulong Mix(ulong hash, byte value)
        {
            hash ^= value;
            hash *= FnvPrime;
            return hash;
        }

        private static int CountTriangles(ExportData data)
        {
            int count = 0;
            foreach (TileData tile in data.Tiles) count += tile.Indices.Count / 3;
            return count;
        }

        private static int Min3(int a, int b, int c) => Mathf.Min(a, Mathf.Min(b, c));
        private static int Max3(int a, int b, int c) => Mathf.Max(a, Mathf.Max(b, c));
    }
}

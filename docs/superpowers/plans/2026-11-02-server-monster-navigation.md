# 服务端怪物寻路 P1 实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 让服务端怪物在 DemoDay 的烘焙 NavMesh 上绕静态地形巡逻/追击/回巢，客户端仍只做表现。

**架构：** Unity Editor 导出 NavMesh 三角面到 `Server/Config/nav/demoday.navbin`；服务端 `Server/Navigation` 构建三角面邻接图，用 A* + 漏斗求路，`PathFollower` 把路点转成期望方向，`SimpleMoveSystem` 再走 ORCA。无导航数据时回退现有直线移动。

**技术栈：** .NET 8、xUnit、Unity 2022.3、AI Navigation 1.1.7、NavMesh 三角面、自研 A* + 漏斗。

**规格：** `docs/superpowers/specs/2026-11-02-server-monster-navigation-ai-design.md`

---

## 文件结构

### 新增（服务端）

| 文件 | 职责 |
|---|---|
| `Server/Navigation/NavData.cs` | `NavData` / `NavTileData` 数据载体 |
| `Server/Navigation/NavDataFormat.cs` | 二进制读写 + 规范 hash |
| `Server/Navigation/NavMeshGraph.cs` | 三角面邻接、边界边、clearance、空间索引、投影、LOS |
| `Server/Navigation/NavPath.cs` | 路径结果（路点/长度/失败原因） |
| `Server/Navigation/NavMeshPathfinder.cs` | A* + 漏斗 + 路径校验 |
| `Server/Navigation/NavigationService.cs` | 地图注册、查询预算、`TryFindPath`/`TryProject`/`HasLineOfSight` |
| `Server/Monster/Movement/PathFollower.cs` | 路点跟随、重寻、卡死检测 |
| `Server/Config/MapsConfig.cs` | `maps.json` 载体 |
| `Server/Config/maps.json` | `demoday → Config/nav/demoday.navbin` |
| `Server/Config/nav/demoday.navbin` | Unity 导出的导航数据（Task 9 生成） |

### 新增（Unity）

| 文件 | 职责 |
|---|---|
| `Assets/Scripts/Editor/Navigation/GameSys.EditorNavigation.asmdef` | Editor-only 程序集，引用 `Unity.AI.Navigation` |
| `Assets/Scripts/Editor/Navigation/NavMeshProbe.cs` | Task 0 spike：打印三角面数/bounds |
| `Assets/Scripts/Editor/Navigation/NavMeshExporter.cs` | 规范化 + 写 navbin + 菜单/batchmode |
| `Assets/Scripts/Tests/Editor/Navigation/NavMeshExportFreshnessTests.cs` | 已提交 navbin 与当前 navmesh hash 一致 |
| `Assets/Scripts/Tests/Editor/Navigation/DemoDayNavBinCoverageTests.cs` | 固定布局点可投影到 navbin |

### 修改

| 文件 | 改动 |
|---|---|
| `Server/Monster/MonsterModule.cs` | 注入 `NavigationService`；生成/巡逻点投影；tick 开始 `BeginTick` |
| `Server/Monster/MonsterState.cs` | 加 `PathFollower? Follower` |
| `Server/Monster/MonsterBtActions.cs` | Patrol/Chase/Return 改走 `PathFollower` |
| `Server/Monster/SimpleMoveSystem.cs` | 新增 `MoveTowards`；`MoveTo` 变薄包装 |
| `Server/Movement/AvoidanceSystem.cs` | 邻居速度改为上一 tick 实际位移 |
| `Server/Server.Tests/Movement/AvoidanceSystemTests.cs` | 对称收敛用例的 liveness 断言按实际位移语义更新（旧断言依赖 `Speed × 朝向` 的永久漩涡） |
| `Server/Server3C.cs` | `EnableMonsters(count, NavigationService?)` |
| `Server/Program.cs` | `--map <id>`、`--no-nav` |
| `Server/Server3C.csproj` | 复制 `Config/maps.json`、`Config/nav/*.navbin` |
| `Server/Server.Tests/Server.Tests.csproj` | 同上 |
| `Assets/Scripts/Tests/Editor/GameSys.EditorTests.asmdef` | 引用 `GameSys.EditorNavigation` |
| `Server/Monster/README.md` / `Server/README.md` / `CLAUDE.md` | 文档同步 |

### 新增（测试）

| 文件 | 覆盖 |
|---|---|
| `Server/Server.Tests/Navigation/NavMeshFixture.cs` | 合成网格/U 形墙/窄通道 |
| `Server/Server.Tests/Navigation/NavDataFormatTests.cs` | 读写、hash、截断 |
| `Server/Server.Tests/Navigation/NavMeshGraphTests.cs` | 邻接、边界、clearance、投影、LOS |
| `Server/Server.Tests/Navigation/NavMeshPathfinderTests.cs` | 直线、绕墙、窄通道、不可达、确定性 |
| `Server/Server.Tests/Navigation/NavigationServiceTests.cs` | 预算、配置加载、LOS |
| `Server/Server.Tests/Navigation/PathFollowerTests.cs` | 路点推进、重寻节流、卡死、无 nav 回退 |
| `Server/Server.Tests/Movement/AvoidanceVelocityTests.cs` | 实际位移快照 |
| `Server/Server.Tests/Monster/MonsterNavigationTests.cs` | 合成 navmesh 上绕墙追击 |
| `Server/Server.Tests/Navigation/DemoDayNavBinTests.cs` | 已提交 navbin 加载 + 出生点覆盖（Task 10） |

---

## 任务 0：验证 EditMode 能取到 DemoDay 的烘焙 NavMesh

**文件：**
- 创建：`Assets/Scripts/Editor/Navigation/GameSys.EditorNavigation.asmdef`
- 创建：`Assets/Scripts/Editor/Navigation/NavMeshProbe.cs`

- [ ] **步骤 1：创建 Editor 程序集**

`Assets/Scripts/Editor/Navigation/GameSys.EditorNavigation.asmdef`：

```json
{
    "name": "GameSys.EditorNavigation",
    "rootNamespace": "",
    "references": [
        "Unity.AI.Navigation"
    ],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": false,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **步骤 2：创建探针**

`Assets/Scripts/Editor/Navigation/NavMeshProbe.cs`：

```csharp
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace GameSys.EditorNavigation
{
    public static class NavMeshProbe
    {
        public const string DemoDayScenePath = "Assets/SimpleLowPolyNature/Scenes/DemoDay.unity";

        [MenuItem("Tools/Navigation/Probe NavMesh")]
        public static void Probe()
        {
            NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
            int triangles = tri.indices == null ? 0 : tri.indices.Length / 3;
            Debug.Log($"[NavMeshProbe] vertices={tri.vertices?.Length ?? 0} triangles={triangles} areas={tri.areas?.Length ?? 0}");
            if (triangles == 0)
            {
                Debug.LogError("[NavMeshProbe] 没有取到 NavMesh。确认 DemoDay 已打开、NavMeshSurface 处于启用状态。");
                return;
            }

            var min = new Vector3(float.MaxValue, 0f, float.MaxValue);
            var max = new Vector3(float.MinValue, 0f, float.MinValue);
            foreach (var v in tri.vertices)
            {
                min.x = Mathf.Min(min.x, v.x); min.z = Mathf.Min(min.z, v.z);
                max.x = Mathf.Max(max.x, v.x); max.z = Mathf.Max(max.z, v.z);
            }
            Debug.Log($"[NavMeshProbe] bounds x[{min.x:F1},{max.x:F1}] z[{min.z:F1},{max.z:F1}]");
        }
    }
}
```

- [ ] **步骤 3：在 Unity 里执行探针**

打开 `Assets/SimpleLowPolyNature/Scenes/DemoDay.unity`，菜单 `Tools > Navigation > Probe NavMesh`，查看 Console。

预期：`triangles > 0`，bounds 覆盖 DemoDay 地形（大约 x/z 在 [-100, 100] 量级）。

如果 `triangles == 0`：停止后续任务，先确认 `NavMeshSurface` 的 `navMeshData` 非空、surface 处于启用状态；把实际输出记录到计划执行备注。

- [ ] **步骤 4：Commit**

```bash
git add Assets/Scripts/Editor/Navigation/GameSys.EditorNavigation.asmdef Assets/Scripts/Editor/Navigation/NavMeshProbe.cs Assets/Scripts/Editor/Navigation/GameSys.EditorNavigation.asmdef.meta Assets/Scripts/Editor/Navigation/NavMeshProbe.cs.meta
git commit -m "chore(nav): add editor navmesh probe"
```

---

## 任务 1：NavData 二进制格式与 hash

**文件：**
- 创建：`Server/Navigation/NavData.cs`
- 创建：`Server/Navigation/NavDataFormat.cs`
- 测试：`Server/Server.Tests/Navigation/NavDataFormatTests.cs`

- [ ] **步骤 1：写失败的测试**

`NavDataFormatTests.cs`：

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using KcpServer;
using KcpServer.Navigation;
using Xunit;

namespace KcpServer.Tests.Navigation;

public class NavDataFormatTests
{
    private static NavData Sample()
    {
        return new NavData
        {
            MapId = "test",
            SourceHash = 42UL,
            MinX = 0f, MinZ = 0f, MaxX = 2f, MaxZ = 2f,
            TileSize = 64f,
            AreaCosts = new[] { 1f, 1f, 2f },
            Vertices = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(2f, 0f, 0f),
                new Vector3(2f, 0f, 2f),
                new Vector3(0f, 0f, 2f)
            },
            Tiles = new[]
            {
                new NavTileData
                {
                    TileX = 0, TileZ = 0,
                    Indices = new[] { 0, 1, 2, 0, 2, 3 },
                    Areas = new byte[] { 0, 0 }
                }
            }
        };
    }

    [Fact]
    public void Write_Read_RoundTrip_Preserves_Geometry_And_Hash()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".navbin");
        try
        {
            NavData data = Sample();
            NavDataFormat.Write(path, data);
            Assert.True(File.Exists(path));

            NavData read = NavDataFormat.Read(path);
            Assert.Equal("test", read.MapId);
            Assert.Equal(42UL, read.SourceHash);
            Assert.Equal(4, read.Vertices.Length);
            Assert.Single(read.Tiles);
            Assert.Equal(new[] { 0, 1, 2, 0, 2, 3 }, read.Tiles[0].Indices);
            Assert.Equal(data.CanonicalHash, read.CanonicalHash);
            Assert.Equal(3, read.AreaCosts.Length);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Read_Truncated_File_Throws()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".navbin");
        try
        {
            NavDataFormat.Write(path, Sample());
            byte[] all = File.ReadAllBytes(path);
            File.WriteAllBytes(path, all[..(all.Length / 2)]);
            Assert.ThrowsAny<Exception>(() => NavDataFormat.Read(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void CanonicalHash_Changes_When_Geometry_Changes()
    {
        NavData a = Sample();
        NavData b = Sample();
        b.Vertices = new[]
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(2.1f, 0f, 0f),
            new Vector3(2f, 0f, 2f),
            new Vector3(0f, 0f, 2f)
        };

        Assert.NotEqual(
            NavDataFormat.ComputeCanonicalHash(a.Vertices, a.Tiles),
            NavDataFormat.ComputeCanonicalHash(b.Vertices, b.Tiles));
    }
}
```

- [ ] **步骤 2：运行测试确认失败**

```bash
bash tools/test-server.sh --filter "FullyQualifiedName~NavDataFormatTests"
```

预期：编译失败，`NavData` / `NavDataFormat` / `NavTileData` 不存在。

- [ ] **步骤 3：实现数据与格式**

`Server/Navigation/NavData.cs`：

```csharp
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
```

`Server/Navigation/NavDataFormat.cs`：

```csharp
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
            data.CanonicalHash = ComputeCanonicalHash(data.Vertices, data.Tiles);

            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            using FileStream stream = File.Create(path);
            using var w = new BinaryWriter(stream);
            w.Write(Magic);
            w.Write(Version);

            byte[] map = Encoding.UTF8.GetBytes(data.MapId ?? string.Empty);
            if (map.Length > ushort.MaxValue) throw new InvalidDataException("mapId too long");
            w.Write((ushort)map.Length);
            w.Write(map);
            w.Write(data.SourceHash);
            w.Write(data.CanonicalHash);
            w.Write(data.MinX); w.Write(data.MinZ); w.Write(data.MaxX); w.Write(data.MaxZ);
            w.Write(data.TileSize);
            w.Write((ushort)data.Tiles.Length);

            if (data.AreaCosts.Length > byte.MaxValue) throw new InvalidDataException("areaCosts too many");
            w.Write((byte)data.AreaCosts.Length);
            foreach (float cost in data.AreaCosts) w.Write(cost);

            w.Write((uint)data.Vertices.Length);
            foreach (Vector3 v in data.Vertices)
            {
                w.Write(v.X); w.Write(v.Y); w.Write(v.Z);
            }

            foreach (NavTileData tile in data.Tiles)
            {
                if (tile.Indices.Length % 3 != 0) throw new InvalidDataException("indices not multiple of 3");
                if (tile.Areas.Length != tile.Indices.Length / 3) throw new InvalidDataException("areas mismatch");
                w.Write(tile.TileX);
                w.Write(tile.TileZ);
                w.Write((uint)(tile.Indices.Length / 3));
                foreach (int index in tile.Indices) w.Write((uint)index);
                foreach (byte area in tile.Areas) w.Write(area);
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
```

- [ ] **步骤 4：运行测试确认通过**

```bash
bash tools/test-server.sh --filter "FullyQualifiedName~NavDataFormatTests"
```

预期：3 个用例 PASS。

- [ ] **步骤 5：Commit**

```bash
git add Server/Navigation/NavData.cs Server/Navigation/NavDataFormat.cs Server/Server.Tests/Navigation/NavDataFormatTests.cs
git commit -m "feat(nav): add nav data format and canonical hash"
```

---

## 任务 2：三角面图、邻接、clearance、投影、LOS

**文件：**
- 创建：`Server/Navigation/NavMeshGraph.cs`
- 测试：`Server/Server.Tests/Navigation/NavMeshFixture.cs`
- 测试：`Server/Server.Tests/Navigation/NavMeshGraphTests.cs`

- [ ] **步骤 1：写合成网格测试夹具**

`NavMeshFixture.cs`：

```csharp
using System;
using System.Collections.Generic;
using KcpServer;
using KcpServer.Navigation;

namespace KcpServer.Tests.Navigation;

internal static class NavMeshFixture
{
    /// <summary>生成 1m 网格；walkable(x,z) 返回 false 的格子被挖空。顶点按坐标去重。</summary>
    public static NavMeshGraph Grid(int width, int depth, Func<int, int, bool> walkable)
    {
        var vertices = new List<Vector3>();
        var lookup = new Dictionary<(int, int), int>();

        int Vertex(float x, float z)
        {
            int qx = (int)MathF.Round(x * 1000f);
            int qz = (int)MathF.Round(z * 1000f);
            if (lookup.TryGetValue((qx, qz), out int index)) return index;
            index = vertices.Count;
            vertices.Add(new Vector3(x, 0f, z));
            lookup[(qx, qz)] = index;
            return index;
        }

        var indices = new List<int>();
        var areas = new List<byte>();

        for (int z = 0; z < depth; z++)
        for (int x = 0; x < width; x++)
        {
            if (!walkable(x, z)) continue;
            int a = Vertex(x, z);
            int b = Vertex(x + 1, z);
            int c = Vertex(x + 1, z + 1);
            int d = Vertex(x, z + 1);
            indices.AddRange(new[] { a, b, c }); areas.Add(0);
            indices.AddRange(new[] { a, c, d }); areas.Add(0);
        }

        return NavMeshGraph.FromTriangles(vertices.ToArray(), indices.ToArray(), areas.ToArray(), new[] { 1f, 1f, 2f });
    }
}
```

- [ ] **步骤 2：写失败的图测试**

`NavMeshGraphTests.cs`：

```csharp
using KcpServer;
using KcpServer.Navigation;
using Xunit;

namespace KcpServer.Tests.Navigation;

public class NavMeshGraphTests
{
    [Fact]
    public void Grid_Internal_Edges_Have_Two_Triangles_And_Boundary_Is_Closed()
    {
        NavMeshGraph graph = NavMeshFixture.Grid(3, 3, (_, _) => true);
        Assert.Equal(18, graph.TriangleCount); // 9 格 × 2
        Assert.True(graph.BoundaryEdgeCount > 0);
        Assert.True(graph.FindNearestTriangle(new Vector3(1.5f, 0f, 1.5f), 0.01f, out int center, out _));
        Assert.Equal(3, graph.GetNeighborCount(center));
    }

    [Fact]
    public void Center_Triangle_Has_Large_Clearance()
    {
        NavMeshGraph graph = NavMeshFixture.Grid(20, 20, (_, _) => true);
        graph.FindNearestTriangle(new Vector3(10.5f, 0f, 10.5f), 0.01f, out int tri, out _);
        Assert.True(tri >= 0);
        Assert.True(graph.GetClearance(tri) > 5f, $"clearance={graph.GetClearance(tri)}");
    }

    [Fact]
    public void FindNearestTriangle_Projects_Point_Outside_Mesh()
    {
        NavMeshGraph graph = NavMeshFixture.Grid(5, 5, (_, _) => true);
        bool found = graph.FindNearestTriangle(new Vector3(-0.5f, 0f, 2.5f), 2f, out int tri, out float distance);
        Assert.True(found);
        Assert.True(tri >= 0);
        Assert.InRange(distance, 0.45f, 0.55f);
    }

    [Fact]
    public void LineOfSight_Is_Blocked_By_Missing_Cells()
    {
        // z=5 一整排挖空，形成一堵墙；x=10 留缺口
        NavMeshGraph graph = NavMeshFixture.Grid(12, 12, (x, z) => !(z == 5 && x < 10));
        Assert.False(graph.HasLineOfSight(new Vector3(1f, 0f, 1f), new Vector3(1f, 0f, 9f), 0.5f));
        Assert.True(graph.HasLineOfSight(new Vector3(10.5f, 0f, 1f), new Vector3(10.5f, 0f, 9f), 0.4f));
    }

    [Fact]
    public void NavMeshGraph_From_Data_Matches_From_Triangles()
    {
        NavMeshGraph graph = NavMeshFixture.Grid(4, 4, (_, _) => true);
        var data = new NavData
        {
            MapId = "test",
            MinX = 0, MinZ = 0, MaxX = 4, MaxZ = 4,
            TileSize = 64,
            AreaCosts = new[] { 1f, 1f, 2f },
            Vertices = graph.CopyVertices(),
            Tiles = new[]
            {
                new NavTileData { TileX = 0, TileZ = 0, Indices = graph.CopyIndices(), Areas = graph.CopyAreas() }
            }
        };
        var fromData = NavMeshGraph.FromData(data);
        Assert.Equal(graph.TriangleCount, fromData.TriangleCount);
    }
}
```

- [ ] **步骤 3：运行测试确认失败**

```bash
bash tools/test-server.sh --filter "FullyQualifiedName~NavMeshGraphTests"
```

预期：编译失败，`NavMeshGraph` 不存在。

- [ ] **步骤 4：实现 NavMeshGraph**

`Server/Navigation/NavMeshGraph.cs`：

```csharp
using System;
using System.Collections.Generic;

namespace KcpServer.Navigation
{
    public sealed class NavMeshGraph
    {
        private const float ClearanceCap = 8f;
        private const float CellSize = 4f;

        private readonly Vector3[] _vertices;
        private readonly int[] _indices;
        private readonly byte[] _areas;
        private readonly float[] _areaCosts;
        private readonly Triangle[] _triangles;
        private readonly int[][] _neighbors;
        private readonly List<(Vector3 A, Vector3 B)> _boundaryEdges;
        private readonly Dictionary<long, List<int>> _triangleGrid;
        private readonly Dictionary<long, List<int>> _boundaryGrid;

        public int TriangleCount => _triangles.Length;
        public int VertexCount => _vertices.Length;
        public int BoundaryEdgeCount => _boundaryEdges.Count;
        public float MinX { get; }
        public float MinZ { get; }
        public float MaxX { get; }
        public float MaxZ { get; }

        private readonly struct Triangle
        {
            public readonly int A, B, C;
            public readonly float CentroidX, CentroidZ;
            public readonly float MinX, MinZ, MaxX, MaxZ;
            public readonly float Clearance;
            public Triangle(int a, int b, int c, float cx, float cz, float minX, float minZ,
                float maxX, float maxZ, float clearance)
            {
                A = a; B = b; C = c; CentroidX = cx; CentroidZ = cz;
                MinX = minX; MinZ = minZ; MaxX = maxX; MaxZ = maxZ; Clearance = clearance;
            }
        }

        public static NavMeshGraph FromData(NavData data)
        {
            var allIndices = new List<int>();
            var allAreas = new List<byte>();
            foreach (NavTileData tile in data.Tiles)
            {
                allIndices.AddRange(tile.Indices);
                allAreas.AddRange(tile.Areas);
            }
            return new NavMeshGraph(data.Vertices, allIndices.ToArray(), allAreas.ToArray(), data.AreaCosts);
        }

        public static NavMeshGraph FromTriangles(Vector3[] vertices, int[] indices, byte[] areas, float[] areaCosts)
            => new NavMeshGraph(vertices, indices, areas, areaCosts);

        private NavMeshGraph(Vector3[] vertices, int[] indices, byte[] areas, float[] areaCosts)
        {
            if (vertices == null || vertices.Length == 0) throw new ArgumentException("no vertices", nameof(vertices));
            if (indices == null || indices.Length == 0 || indices.Length % 3 != 0)
                throw new ArgumentException("indices must be non-empty multiple of 3", nameof(indices));
            if (areas == null || areas.Length != indices.Length / 3)
                throw new ArgumentException("areas mismatch", nameof(areas));

            _vertices = vertices;
            _indices = indices;
            _areas = areas;
            _areaCosts = areaCosts ?? new[] { 1f };

            int triangleCount = indices.Length / 3;
            var triangles = new Triangle[triangleCount];
            _boundaryEdges = new List<(Vector3, Vector3)>();
            _triangleGrid = new Dictionary<long, List<int>>();
            _boundaryGrid = new Dictionary<long, List<int>>();

            var edgeMap = new Dictionary<long, (int A, int B, int T0, int T1)>();
            var neighborLists = new List<int>[triangleCount];

            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            for (int i = 0; i < triangleCount; i++)
            {
                int a = indices[i * 3], b = indices[i * 3 + 1], c = indices[i * 3 + 2];
                if (a < 0 || b < 0 || c < 0 || a >= vertices.Length || b >= vertices.Length || c >= vertices.Length)
                    throw new ArgumentException("index out of range");

                Vector3 va = vertices[a], vb = vertices[b], vc = vertices[c];
                if (SignedArea2(va, vb, vc) < 0f)
                {
                    (b, c) = (c, b);
                    vb = vertices[b]; vc = vertices[c];
                    indices[i * 3 + 1] = b;
                    indices[i * 3 + 2] = c;
                }

                float triMinX = MathF.Min(va.X, MathF.Min(vb.X, vc.X));
                float triMinZ = MathF.Min(va.Z, MathF.Min(vb.Z, vc.Z));
                float triMaxX = MathF.Max(va.X, MathF.Max(vb.X, vc.X));
                float triMaxZ = MathF.Max(va.Z, MathF.Max(vb.Z, vc.Z));
                float cx = (va.X + vb.X + vc.X) / 3f;
                float cz = (va.Z + vb.Z + vc.Z) / 3f;
                triangles[i] = new Triangle(a, b, c, cx, cz, triMinX, triMinZ, triMaxX, triMaxZ, ClearanceCap);
                neighborLists[i] = new List<int>(3);

                AddEdge(edgeMap, a, b, i);
                AddEdge(edgeMap, b, c, i);
                AddEdge(edgeMap, c, a, i);

                InsertAabb(_triangleGrid, triMinX, triMinZ, triMaxX, triMaxZ, i);

                minX = MathF.Min(minX, triMinX); minZ = MathF.Min(minZ, triMinZ);
                maxX = MathF.Max(maxX, triMaxX); maxZ = MathF.Max(maxZ, triMaxZ);
            }

            foreach (var kv in edgeMap)
            {
                var (a, b, t0, t1) = kv.Value;
                if (t1 < 0)
                {
                    _boundaryEdges.Add((vertices[a], vertices[b]));
                    continue;
                }
                neighborLists[t0].Add(t1);
                neighborLists[t1].Add(t0);
            }

            for (int i = 0; i < triangleCount; i++)
                neighborLists[i].Sort();

            _neighbors = new int[triangleCount][];
            for (int i = 0; i < triangleCount; i++) _neighbors[i] = neighborLists[i].ToArray();

            for (int i = 0; i < _boundaryEdges.Count; i++)
            {
                var (a, b) = _boundaryEdges[i];
                InsertAabb(_boundaryGrid, MathF.Min(a.X, b.X), MathF.Min(a.Z, b.Z),
                    MathF.Max(a.X, b.X), MathF.Max(a.Z, b.Z), i);
            }

            for (int i = 0; i < triangleCount; i++)
            {
                Triangle t = triangles[i];
                float clearance = MathF.Min(
                    MathF.Min(ClearanceAt(t.CentroidX, t.CentroidZ),
                        ClearanceAt((_vertices[t.A].X + _vertices[t.B].X) * 0.5f, (_vertices[t.A].Z + _vertices[t.B].Z) * 0.5f)),
                    MathF.Min(ClearanceAt((_vertices[t.B].X + _vertices[t.C].X) * 0.5f, (_vertices[t.B].Z + _vertices[t.C].Z) * 0.5f),
                        ClearanceAt((_vertices[t.C].X + _vertices[t.A].X) * 0.5f, (_vertices[t.C].Z + _vertices[t.A].Z) * 0.5f)));
                triangles[i] = new Triangle(t.A, t.B, t.C, t.CentroidX, t.CentroidZ, t.MinX, t.MinZ, t.MaxX, t.MaxZ, clearance);
            }

            _triangles = triangles;
            MinX = minX; MinZ = minZ; MaxX = maxX; MaxZ = maxZ;
        }

        public int[] CopyIndices() => (int[])_indices.Clone();
        public byte[] CopyAreas() => (byte[])_areas.Clone();
        public Vector3[] CopyVertices() => (Vector3[])_vertices.Clone();
        public float GetClearance(int triangle) => _triangles[triangle].Clearance;
        public int GetTriangleCount() => _triangles.Length;
        public int GetNeighborCount(int triangle) => _neighbors[triangle].Length;
        public int GetNeighbor(int triangle, int index) => _neighbors[triangle][index];
        public float AreaCost(int triangle)
        {
            byte area = _areas[triangle];
            return area < _areaCosts.Length ? _areaCosts[area] : 1f;
        }

        public void GetTriangleCentroid(int triangle, out float x, out float z)
        {
            x = _triangles[triangle].CentroidX;
            z = _triangles[triangle].CentroidZ;
        }

        public bool FindNearestTriangle(Vector3 point, float maxDistance, out int triangle, out float distance)
        {
            triangle = -1;
            distance = float.PositiveInfinity;
            int cellX = CellOf(point.X), cellZ = CellOf(point.Z);
            if (ContainsInCell(cellX, cellZ, point, out triangle))
            {
                distance = 0f;
                return true;
            }

            int radiusCells = Math.Max(1, (int)MathF.Ceiling(maxDistance / CellSize));
            for (int dz = -radiusCells; dz <= radiusCells; dz++)
            for (int dx = -radiusCells; dx <= radiusCells; dx++)
            {
                long key = CellKey(cellX + dx, cellZ + dz);
                if (!_triangleGrid.TryGetValue(key, out List<int>? candidates)) continue;
                foreach (int candidate in candidates)
                {
                    float d = DistanceToTriangle(point, candidate);
                    if (d < distance)
                    {
                        distance = d;
                        triangle = candidate;
                    }
                }
            }

            if (triangle >= 0 && distance <= maxDistance) return true;
            triangle = -1;
            distance = float.PositiveInfinity;
            return false;
        }

        public bool FindNearestPoint(Vector3 point, float maxDistance, out Vector3 projected,
            out float distance, out int triangle)
        {
            projected = point;
            distance = float.PositiveInfinity;
            triangle = -1;
            if (!FindNearestTriangle(point, maxDistance, out int candidate, out _)) return false;

            triangle = candidate;
            if (PointInTriangle(point, candidate))
            {
                projected = point;
                distance = 0f;
                return true;
            }

            projected = ClosestPointOnTriangleXZ(point, candidate);
            distance = DistanceXZ(point, projected);
            return distance <= maxDistance;
        }

        private Vector3 ClosestPointOnTriangleXZ(Vector3 p, int triangle)
        {
            Triangle t = _triangles[triangle];
            Vector3 a = _vertices[t.A], b = _vertices[t.B], c = _vertices[t.C];
            Vector3 ab = ClosestPointOnSegment(p, a, b);
            Vector3 bc = ClosestPointOnSegment(p, b, c);
            Vector3 ca = ClosestPointOnSegment(p, c, a);
            float dab = DistanceXZ(p, ab), dbc = DistanceXZ(p, bc), dca = DistanceXZ(p, ca);
            if (dab <= dbc && dab <= dca) return ab;
            if (dbc <= dca) return bc;
            return ca;
        }

        private static Vector3 ClosestPointOnSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            float dx = b.X - a.X, dz = b.Z - a.Z;
            float lenSq = dx * dx + dz * dz;
            float t = lenSq <= 0.000001f ? 0f : ((p.X - a.X) * dx + (p.Z - a.Z) * dz) / lenSq;
            t = Math.Clamp(t, 0f, 1f);
            return new Vector3(a.X + dx * t, p.Y, a.Z + dz * t);
        }

        private static float DistanceXZ(Vector3 a, Vector3 b)
        {
            float dx = a.X - b.X, dz = a.Z - b.Z;
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        public bool HasLineOfSight(Vector3 from, Vector3 to, float bodyRadius)
        {
            float dx = to.X - from.X, dz = to.Z - from.Z;
            float length = MathF.Sqrt(dx * dx + dz * dz);
            int steps = Math.Max(1, (int)MathF.Ceiling(length / 0.25f));
            for (int i = 0; i <= steps; i++)
            {
                float t = steps == 0 ? 0f : i / (float)steps;
                float x = from.X + dx * t;
                float z = from.Z + dz * t;
                if (!IsPointNavigable(new Vector3(x, from.Y, z), bodyRadius)) return false;
            }
            return true;
        }

        public bool IsPointNavigable(Vector3 point, float bodyRadius)
        {
            if (!FindNearestTriangle(point, 0.25f, out int triangle, out float distance)) return false;
            return distance <= 0.25f && ClearanceAt(point.X, point.Z) >= bodyRadius;
        }

        public bool GetPortal(int fromTriangle, int toTriangle, out Vector3 left, out Vector3 right)
        {
            left = default; right = default;
            Triangle a = _triangles[fromTriangle];
            Triangle b = _triangles[toTriangle];
            int shared0 = -1, shared1 = -1;
            if (!TryShared(a, b, ref shared0, ref shared1)) return false;
            if (shared0 < 0 || shared1 < 0) return false;

            Vector3 p0 = _vertices[shared0];
            Vector3 p1 = _vertices[shared1];
            float dx = b.CentroidX - a.CentroidX;
            float dz = b.CentroidZ - a.CentroidZ;
            float cross = dx * (p0.Z - a.CentroidZ) - dz * (p0.X - a.CentroidX);
            if (cross > 0f) { left = p0; right = p1; }
            else { left = p1; right = p0; }
            return true;
        }

        private bool ContainsInCell(int cellX, int cellZ, Vector3 point, out int triangle)
        {
            triangle = -1;
            if (!_triangleGrid.TryGetValue(CellKey(cellX, cellZ), out List<int>? candidates)) return false;
            foreach (int candidate in candidates)
            {
                if (PointInTriangle(point, candidate))
                {
                    triangle = candidate;
                    return true;
                }
            }
            return false;
        }

        private bool PointInTriangle(Vector3 p, int triangle)
        {
            Triangle t = _triangles[triangle];
            Vector3 a = _vertices[t.A], b = _vertices[t.B], c = _vertices[t.C];
            float d1 = Sign(p, a, b);
            float d2 = Sign(p, b, c);
            float d3 = Sign(p, c, a);
            bool hasNeg = d1 < 0f || d2 < 0f || d3 < 0f;
            bool hasPos = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(hasNeg && hasPos);
        }

        private float DistanceToTriangle(Vector3 p, int triangle)
        {
            Triangle t = _triangles[triangle];
            Vector3 a = _vertices[t.A], b = _vertices[t.B], c = _vertices[t.C];
            float d0 = DistancePointSegment(p, a, b);
            float d1 = DistancePointSegment(p, b, c);
            float d2 = DistancePointSegment(p, c, a);
            return MathF.Min(d0, MathF.Min(d1, d2));
        }

        private float ClearanceAt(float x, float z)
        {
            int cellX = CellOf(x), cellZ = CellOf(z);
            int radius = (int)MathF.Ceiling(ClearanceCap / CellSize);
            float best = ClearanceCap;
            for (int dz = -radius; dz <= radius; dz++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (!_boundaryGrid.TryGetValue(CellKey(cellX + dx, cellZ + dz), out List<int>? edges)) continue;
                foreach (int edgeIndex in edges)
                {
                    var (a, b) = _boundaryEdges[edgeIndex];
                    float d = DistancePointSegmentXZ(x, z, a, b);
                    if (d < best) best = d;
                }
            }
            return best;
        }

        private static void AddEdge(Dictionary<long, (int A, int B, int T0, int T1)> map, int a, int b, int triangle)
        {
            int min = Math.Min(a, b), max = Math.Max(a, b);
            long key = ((long)min << 32) | (uint)max;
            if (map.TryGetValue(key, out var record))
            {
                if (record.T1 >= 0) throw new InvalidOperationException("non-manifold edge");
                map[key] = (record.A, record.B, record.T0, triangle);
            }
            else
            {
                map[key] = (min, max, triangle, -1);
            }
        }

        private static bool TryShared(Triangle a, Triangle b, ref int shared0, ref int shared1)
        {
            Span<int> av = stackalloc[] { a.A, a.B, a.C };
            Span<int> bv = stackalloc[] { b.A, b.B, b.C };
            int found = 0;
            foreach (int va in av)
            foreach (int vb in bv)
            {
                if (va != vb) continue;
                if (found == 0) shared0 = va;
                else if (found == 1) shared1 = va;
                found++;
            }
            return found == 2;
        }

        private static float SignedArea2(Vector3 a, Vector3 b, Vector3 c)
            => (b.X - a.X) * (c.Z - a.Z) - (b.Z - a.Z) * (c.X - a.X);

        private static float Sign(Vector3 p, Vector3 a, Vector3 b)
            => (p.X - b.X) * (a.Z - b.Z) - (a.X - b.X) * (p.Z - b.Z);

        private static float DistancePointSegment(Vector3 p, Vector3 a, Vector3 b)
            => DistancePointSegmentXZ(p.X, p.Z, a, b);

        private static float DistancePointSegmentXZ(float px, float pz, Vector3 a, Vector3 b)
        {
            float dx = b.X - a.X, dz = b.Z - a.Z;
            float lenSq = dx * dx + dz * dz;
            float t = lenSq <= 0.000001f ? 0f : ((px - a.X) * dx + (pz - a.Z) * dz) / lenSq;
            t = Math.Clamp(t, 0f, 1f);
            float x = a.X + dx * t, z = a.Z + dz * t;
            float ex = px - x, ez = pz - z;
            return MathF.Sqrt(ex * ex + ez * ez);
        }

        private static void InsertAabb(Dictionary<long, List<int>> grid, float minX, float minZ, float maxX, float maxZ, int value)
        {
            int x0 = CellOf(minX), x1 = CellOf(maxX), z0 = CellOf(minZ), z1 = CellOf(maxZ);
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                long key = CellKey(x, z);
                if (!grid.TryGetValue(key, out List<int>? list))
                {
                    list = new List<int>();
                    grid[key] = list;
                }
                list.Add(value);
            }
        }

        private static int CellOf(float value) => (int)MathF.Floor(value / CellSize);
        private static long CellKey(int x, int z) => ((long)x << 32) ^ (uint)z;
    }
}
```

- [ ] **步骤 5：运行测试确认通过**

```bash
bash tools/test-server.sh --filter "FullyQualifiedName~NavMeshGraphTests"
```

预期：5 个用例 PASS。若 `LineOfSight_Is_Blocked` 失败，先检查 `ClearanceAt` 的边界网格与 `HasLineOfSight` 采样；不要通过放宽断言来掩盖。

- [ ] **步骤 6：Commit**

```bash
git add Server/Navigation/NavMeshGraph.cs Server/Server.Tests/Navigation/NavMeshFixture.cs Server/Server.Tests/Navigation/NavMeshGraphTests.cs
git commit -m "feat(nav): build triangle graph with clearance and projection"
```

---

## 任务 3：A* + 漏斗 + 路径校验

**文件：**
- 创建：`Server/Navigation/NavPath.cs`
- 创建：`Server/Navigation/NavMeshPathfinder.cs`
- 测试：`Server/Server.Tests/Navigation/NavMeshPathfinderTests.cs`

- [ ] **步骤 1：写失败的寻路测试**

`NavMeshPathfinderTests.cs`：

```csharp
using System;
using KcpServer;
using KcpServer.Navigation;
using Xunit;

namespace KcpServer.Tests.Navigation;

public class NavMeshPathfinderTests
{
    private static NavMeshGraph WallGraph()
        => NavMeshFixture.Grid(12, 12, (x, z) => !(z == 5 && x < 10));

    [Fact]
    public void Straight_Path_On_Open_Grid_Is_Short_And_Direct()
    {
        NavMeshGraph graph = NavMeshFixture.Grid(12, 12, (_, _) => true);
        var finder = new NavMeshPathfinder(graph);
        NavPath path = finder.FindPath(new Vector3(1f, 0f, 1f), new Vector3(8f, 0f, 8f), 0.4f);
        Assert.True(path.Success, path.FailReason);
        Assert.True(path.Waypoints.Length >= 2);
        Assert.InRange(path.Length, 9.8f, 10.2f);
    }

    [Fact]
    public void Wall_Path_Detours_Through_Gap()
    {
        NavMeshGraph graph = WallGraph();
        var finder = new NavMeshPathfinder(graph);
        NavPath path = finder.FindPath(new Vector3(1f, 0f, 1f), new Vector3(1f, 0f, 9f), 0.4f);
        Assert.True(path.Success, path.FailReason);
        Assert.True(path.Length > 8.5f, $"path should detour, length={path.Length}");
        bool usesGap = false;
        foreach (var p in path.Waypoints)
            if (p.X > 9.5f) usesGap = true;
        Assert.True(usesGap, "path should pass the gap at x>=10");
    }

    [Fact]
    public void Narrow_Gap_Is_Blocked_For_Large_Body_But_Open_For_Small()
    {
        // z=5 一排墙，只在 x=5 留 1m 缺口
        NavMeshGraph graph = NavMeshFixture.Grid(12, 12, (x, z) => !(z == 5 && x != 5));
        var finder = new NavMeshPathfinder(graph);
        NavPath small = finder.FindPath(new Vector3(5.5f, 0f, 1f), new Vector3(5.5f, 0f, 9f), 0.3f);
        NavPath large = finder.FindPath(new Vector3(5.5f, 0f, 1f), new Vector3(5.5f, 0f, 9f), 1.0f);
        Assert.True(small.Success, small.FailReason);
        Assert.False(large.Success, "1.0m body should not fit through a 1m gap");
    }

    [Fact]
    public void Start_Off_Navmesh_Is_Projected()
    {
        NavMeshGraph graph = NavMeshFixture.Grid(12, 12, (_, _) => true);
        var finder = new NavMeshPathfinder(graph);
        NavPath path = finder.FindPath(new Vector3(-0.5f, 0f, 5.5f), new Vector3(8f, 0f, 8f), 0.3f);
        Assert.True(path.Success, path.FailReason);
        Assert.InRange(path.Waypoints[0].X, 0f, 0.1f);
    }

    [Fact]
    public void Unreachable_Island_Fails_With_Reason()
    {
        // 左右两块网格完全断开
        NavMeshGraph graph = NavMeshFixture.Grid(12, 12, (x, _) => x < 5 || x > 8);
        var finder = new NavMeshPathfinder(graph);
        NavPath path = finder.FindPath(new Vector3(1f, 0f, 6f), new Vector3(10f, 0f, 6f), 0.4f);
        Assert.False(path.Success);
        Assert.Equal("blocked", path.FailReason);
    }

    [Fact]
    public void Path_Is_Deterministic()
    {
        NavMeshGraph graph = WallGraph();
        var finder = new NavMeshPathfinder(graph);
        NavPath a = finder.FindPath(new Vector3(1f, 0f, 1f), new Vector3(1f, 0f, 9f), 0.4f);
        NavPath b = finder.FindPath(new Vector3(1f, 0f, 1f), new Vector3(1f, 0f, 9f), 0.4f);
        Assert.True(a.Success);
        Assert.Equal(a.Waypoints.Length, b.Waypoints.Length);
        for (int i = 0; i < a.Waypoints.Length; i++)
            Assert.Equal(a.Waypoints[i].X, b.Waypoints[i].X, 5);
    }
}
```

- [ ] **步骤 2：运行确认失败**

```bash
bash tools/test-server.sh --filter "FullyQualifiedName~NavMeshPathfinderTests"
```

预期：编译失败，`NavMeshPathfinder` / `NavPath` 不存在。

- [ ] **步骤 3：实现 NavPath**

`Server/Navigation/NavPath.cs`：

```csharp
using System;

namespace KcpServer.Navigation
{
    public sealed class NavPath
    {
        public bool Success;
        public string FailReason = string.Empty;
        public Vector3[] Waypoints = Array.Empty<Vector3>();
        public float Length;

        public static NavPath Fail(string reason) => new NavPath { Success = false, FailReason = reason };

        public static NavPath Create(Vector3[] waypoints)
        {
            float length = 0f;
            for (int i = 1; i < waypoints.Length; i++)
            {
                float dx = waypoints[i].X - waypoints[i - 1].X;
                float dz = waypoints[i].Z - waypoints[i - 1].Z;
                length += MathF.Sqrt(dx * dx + dz * dz);
            }
            return new NavPath { Success = true, Waypoints = waypoints, Length = length };
        }
    }
}
```

- [ ] **步骤 4：实现 NavMeshPathfinder**

`Server/Navigation/NavMeshPathfinder.cs`：

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace KcpServer.Navigation
{
    public sealed class NavMeshPathfinder
    {
        private const float DefaultProjectionRadius = 2f;
        private const float ValidationStep = 0.5f;
        private const float ClearanceHardFactor = 0.9f;
        private const float ClearanceSoftFactor = 1.1f;
        private const float ClearancePenalty = 2f;

        private readonly NavMeshGraph _graph;

        public NavMeshPathfinder(NavMeshGraph graph) => _graph = graph ?? throw new ArgumentNullException(nameof(graph));

        public NavPath FindPath(Vector3 start, Vector3 goal, float bodyRadius, int maxExpandedNodes = 50000)
            => FindPathInternal(start, goal, bodyRadius, maxExpandedNodes, 0);

        private NavPath FindPathInternal(Vector3 start, Vector3 goal, float bodyRadius, int maxExpandedNodes, int attempt)
        {
            if (!_graph.FindNearestPoint(start, DefaultProjectionRadius, out Vector3 projectedStart, out _, out int startTri))
                return NavPath.Fail("start-off-navmesh");
            if (!_graph.FindNearestPoint(goal, DefaultProjectionRadius, out Vector3 projectedGoal, out _, out int goalTri))
                return NavPath.Fail("goal-off-navmesh");

            if (startTri == goalTri)
            {
                var direct = new[] { projectedStart, projectedGoal };
                if (IsValidSegment(projectedStart, projectedGoal, bodyRadius)) return NavPath.Create(direct);
            }

            int n = _graph.TriangleCount;
            var gScore = new float[n];
            var cameFrom = new int[n];
            var closed = new bool[n];
            for (int i = 0; i < n; i++) { gScore[i] = float.PositiveInfinity; cameFrom[i] = -1; }

            var open = new PriorityQueue<int, (float F, int Index)>();
            gScore[startTri] = 0f;
            open.Enqueue(startTri, (Heuristic(startTri, goalTri), startTri));

            int expanded = 0;
            while (open.TryDequeue(out int current, out _))
            {
                if (closed[current]) continue;
                closed[current] = true;
                if (current == goalTri) break;
                if (++expanded > maxExpandedNodes) return NavPath.Fail("too-complex");

                int count = _graph.GetNeighborCount(current);
                for (int k = 0; k < count; k++)
                {
                    int next = _graph.GetNeighbor(current, k);
                    if (closed[next]) continue;
                    bool exempt = next == goalTri || current == startTri;
                    if (!exempt && _graph.GetClearance(next) < bodyRadius * ClearanceHardFactor) continue;

                    float step = DistanceXZ(current, next) * _graph.AreaCost(next);
                    step += ClearancePenalty * MathF.Max(0f, bodyRadius * ClearanceSoftFactor - _graph.GetClearance(next));
                    float tentative = gScore[current] + step;
                    if (tentative + 1e-5f >= gScore[next]) continue;

                    gScore[next] = tentative;
                    cameFrom[next] = current;
                    open.Enqueue(next, (tentative + Heuristic(next, goalTri), next));
                }
            }

            if (cameFrom[goalTri] < 0 && startTri != goalTri) return NavPath.Fail("blocked");

            var triPath = new List<int>();
            for (int tri = goalTri; tri >= 0; tri = cameFrom[tri])
            {
                triPath.Add(tri);
                if (tri == startTri) break;
            }
            triPath.Reverse();
            if (triPath.Count == 0 || triPath[0] != startTri) return NavPath.Fail("blocked");

            var points = new List<Vector3>(triPath.Count * 2 + 2) { projectedStart };
            for (int i = 0; i + 1 < triPath.Count; i++)
            {
                if (!_graph.GetPortal(triPath[i], triPath[i + 1], out Vector3 left, out Vector3 right))
                    return NavPath.Fail("portal-missing");
                points.Add(left);
                points.Add(right);
            }
            points.Add(projectedGoal);

            List<Vector3> waypoints = Funnel(points);
            if (!ValidatePath(waypoints, bodyRadius))
            {
                if (attempt == 0)
                    return FindPathInternal(start, goal, bodyRadius * 1.15f, maxExpandedNodes, 1);
                return NavPath.Fail("blocked");
            }
            return NavPath.Create(waypoints.ToArray());
        }

        public bool HasLineOfSight(Vector3 a, Vector3 b, float bodyRadius)
            => _graph.HasLineOfSight(a, b, bodyRadius);

        private bool ValidatePath(List<Vector3> points, float bodyRadius)
        {
            for (int i = 1; i < points.Count; i++)
                if (!IsValidSegment(points[i - 1], points[i], bodyRadius)) return false;
            return true;
        }

        private bool IsValidSegment(Vector3 a, Vector3 b, float bodyRadius)
        {
            float dx = b.X - a.X, dz = b.Z - a.Z;
            float length = MathF.Sqrt(dx * dx + dz * dz);
            int steps = Math.Max(1, (int)MathF.Ceiling(length / ValidationStep));
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                // 起终点 1m 内放宽：怪可能贴着墙/岩石出生
                float along = t * length;
                float radius = (along < 1f || length - along < 1f) ? bodyRadius * 0.5f : bodyRadius;
                var p = new Vector3(a.X + dx * t, a.Y, a.Z + dz * t);
                if (!_graph.IsPointNavigable(p, radius)) return false;
            }
            return true;
        }

        private float Heuristic(int from, int goal)
        {
            _graph.GetTriangleCentroid(from, out float fx, out float fz);
            _graph.GetTriangleCentroid(goal, out float gx, out float gz);
            float dx = gx - fx, dz = gz - fz;
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        private float DistanceXZ(int a, int b)
        {
            _graph.GetTriangleCentroid(a, out float ax, out float az);
            _graph.GetTriangleCentroid(b, out float bx, out float bz);
            float dx = bx - ax, dz = bz - az;
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        private static List<Vector3> Funnel(List<Vector3> points)
        {
            // points = [start, L0,R0, L1,R1, ..., goal]
            var result = new List<Vector3>();
            if (points.Count < 2) return result;

            Vector3 apex = points[0];
            Vector3 left = points[0];
            Vector3 right = points[0];
            int apexIndex = 0, leftIndex = 0, rightIndex = 0;
            result.Add(apex);

            int portalCount = (points.Count - 2) / 2;
            for (int i = 0; i < portalCount; i++)
            {
                Vector3 newLeft = points[1 + i * 2];
                Vector3 newRight = points[2 + i * 2];

                if (TriArea2(apex, right, newRight) <= 0f)
                {
                    if (apex == right || TriArea2(apex, left, newRight) > 0f)
                    {
                        right = newRight;
                        rightIndex = i;
                    }
                    else
                    {
                        result.Add(left);
                        apex = left;
                        apexIndex = leftIndex;
                        left = apex; right = apex;
                        leftIndex = apexIndex; rightIndex = apexIndex;
                        i = apexIndex;
                        continue;
                    }
                }

                if (TriArea2(apex, left, newLeft) >= 0f)
                {
                    if (apex == left || TriArea2(apex, right, newLeft) < 0f)
                    {
                        left = newLeft;
                        leftIndex = i;
                    }
                    else
                    {
                        result.Add(right);
                        apex = right;
                        apexIndex = rightIndex;
                        left = apex; right = apex;
                        leftIndex = apexIndex; rightIndex = apexIndex;
                        i = apexIndex;
                        continue;
                    }
                }
            }

            result.Add(points[^1]);
            return result;
        }

        private static float TriArea2(Vector3 a, Vector3 b, Vector3 c)
            => (b.X - a.X) * (c.Z - a.Z) - (b.Z - a.Z) * (c.X - a.X);
    }
}
```

- [ ] **步骤 5：运行测试确认通过**

```bash
bash tools/test-server.sh --filter "FullyQualifiedName~NavMeshPathfinderTests"
```

预期：5 个用例 PASS。若 `Wall_Path_Detours` 或窄通道用例失败，优先检查漏斗 portal 的左右顺序与 `TriArea2` 符号一致性；用测试坐标打印中间 `points` 排查。

- [ ] **步骤 6：Commit**

```bash
git add Server/Navigation/NavPath.cs Server/Navigation/NavMeshPathfinder.cs Server/Server.Tests/Navigation/NavMeshPathfinderTests.cs
git commit -m "feat(nav): add A-star and funnel pathfinding"
```

---

## 任务 4：NavigationService、查询预算与地图配置

**文件：**
- 创建：`Server/Navigation/NavigationService.cs`
- 创建：`Server/Config/MapsConfig.cs`
- 修改：`Server/Config/ConfigLoader.cs`
- 创建：`Server/Config/maps.json`
- 测试：`Server/Server.Tests/Navigation/NavigationServiceTests.cs`

- [ ] **步骤 1：写失败的测试**

`NavigationServiceTests.cs`：

```csharp
using System;
using System.IO;
using KcpServer;
using KcpServer.Config;
using KcpServer.Navigation;
using Xunit;

namespace KcpServer.Tests.Navigation;

public class NavigationServiceTests
{
    private static NavigationService MakeService(int maxQueries = 16)
    {
        NavMeshGraph graph = NavMeshFixture.Grid(12, 12, (_, _) => true);
        return new NavigationService(graph, "test") { MaxQueriesPerTick = maxQueries };
    }

    [Fact]
    public void Budget_Limits_Queries_Per_Tick_And_Resets_Next_Tick()
    {
        NavigationService service = MakeService(maxQueries: 2);
        service.BeginTick(0);
        var query = new NavQuery("test", new Vector3(1f, 0f, 1f), new Vector3(8f, 0f, 8f), 0.4f);
        Assert.True(service.TryFindPath(query, out _));
        Assert.True(service.TryFindPath(query, out _));
        Assert.False(service.TryFindPath(query, out _));

        service.BeginTick(1);
        Assert.True(service.TryFindPath(query, out _));
    }

    [Fact]
    public void TryProject_Clamps_To_Mesh_And_Reports_Clearance()
    {
        NavigationService service = MakeService();
        Assert.True(service.TryProject(new Vector3(0.5f, 0f, 5.5f), 0.5f, out var projected, out float clearance));
        Assert.InRange(clearance, 0.4f, 1.0f);
        Assert.InRange(projected.X, 0.0f, 1.0f);
    }

    [Fact]
    public void LoadFromConfig_Resolves_Map_File()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "Config", "nav"));
        try
        {
            string navPath = Path.Combine(dir, "Config", "nav", "test.navbin");
            NavData data = new NavData
            {
                MapId = "test",
                MinX = 0, MinZ = 0, MaxX = 1, MaxZ = 1,
                TileSize = 64,
                AreaCosts = new[] { 1f },
                Vertices = new[]
                {
                    new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 0, 1), new Vector3(0, 0, 1)
                },
                Tiles = new[]
                {
                    new NavTileData { TileX = 0, TileZ = 0, Indices = new[] { 0, 1, 2, 0, 2, 3 }, Areas = new byte[] { 0, 0 } }
                }
            };
            NavDataFormat.Write(navPath, data);
            string mapsPath = Path.Combine(dir, "Config", "maps.json");
            File.WriteAllText(mapsPath, "{\"maps\":[{\"id\":\"test\",\"navFile\":\"Config/nav/test.navbin\"}]}");

            NavigationService service = NavigationService.LoadFromConfig(mapsPath, "test", dir);
            Assert.Equal("test", service.MapId);
            Assert.True(service.TryFindPath(new NavQuery("test", new Vector3(0.2f, 0, 0.2f), new Vector3(0.8f, 0, 0.8f), 0.1f), out var path));
            Assert.True(path.Success);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
```

- [ ] **步骤 2：运行确认失败**

```bash
bash tools/test-server.sh --filter "FullyQualifiedName~NavigationServiceTests"
```

预期：编译失败，`NavigationService` / `NavQuery` / `MapsConfig` 不存在。

- [ ] **步骤 3：实现 MapsConfig 与 ConfigLoader**

`Server/Config/MapsConfig.cs`：

```csharp
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
    }
}
```

在 `Server/Config/ConfigLoader.cs` 增加：

```csharp
public static MapsConfig LoadMaps(string path)
{
    if (!File.Exists(path)) throw new FileNotFoundException("maps config not found", path);
    string json = File.ReadAllText(path);
    var config = JsonSerializer.Deserialize<MapsConfig>(json, JsonOptions);
    return config ?? new MapsConfig();
}
```

`Server/Config/maps.json`：

```json
{
  "maps": [
    { "id": "demoday", "navFile": "Config/nav/demoday.navbin" }
  ]
}
```

- [ ] **步骤 4：实现 NavigationService**

`Server/Navigation/NavigationService.cs`：

```csharp
using System;
using System.IO;
using System.Linq;
using KcpServer.Config;

namespace KcpServer.Navigation
{
    public readonly struct NavQuery
    {
        public readonly string MapId;
        public readonly Vector3 Start;
        public readonly Vector3 Goal;
        public readonly float BodyRadius;
        public readonly int MaxExpandedNodes;

        public NavQuery(string mapId, Vector3 start, Vector3 goal, float bodyRadius, int maxExpandedNodes = 50000)
        {
            MapId = mapId;
            Start = start;
            Goal = goal;
            BodyRadius = bodyRadius;
            MaxExpandedNodes = maxExpandedNodes;
        }
    }

    public sealed class NavigationService
    {
        private readonly NavMeshGraph _graph;
        private readonly NavMeshPathfinder _pathfinder;
        private int _queriesThisTick;

        public string MapId { get; }
        public int MaxQueriesPerTick { get; set; } = 16;
        public int QueryCount { get; private set; }

        public NavigationService(NavMeshGraph graph, string mapId)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _pathfinder = new NavMeshPathfinder(graph);
            MapId = mapId ?? string.Empty;
        }

        public NavigationService(NavData data)
            : this(NavMeshGraph.FromData(data), data.MapId)
        {
        }

        public static NavigationService LoadFromConfig(string mapsConfigPath, string mapId, string baseDirectory)
        {
            MapsConfig maps = ConfigLoader.LoadMaps(mapsConfigPath);
            MapEntry? entry = maps.maps.FirstOrDefault(m => m.id == mapId);
            if (entry == null) throw new InvalidOperationException($"map '{mapId}' not found in {mapsConfigPath}");

            string navPath = Path.Combine(baseDirectory, entry.navFile);
            NavData data = NavDataFormat.Read(navPath);
            if (data.MapId != mapId)
                throw new InvalidDataException($"map id mismatch: config={mapId} file={data.MapId}");
            return new NavigationService(data);
        }

        public void BeginTick(uint tick) => _queriesThisTick = 0;

        public bool TryFindPath(NavQuery query, out NavPath path)
        {
            if (query.MapId != MapId)
            {
                path = NavPath.Fail("wrong-map");
                return false;
            }
            if (_queriesThisTick >= MaxQueriesPerTick)
            {
                path = NavPath.Fail("budget");
                return false;
            }

            _queriesThisTick++;
            QueryCount++;
            path = _pathfinder.FindPath(query.Start, query.Goal, query.BodyRadius, query.MaxExpandedNodes);
            return path.Success;
        }

        public bool TryProject(Vector3 point, float maxDistance, out Vector3 projected, out float clearance)
        {
            projected = point;
            clearance = 0f;
            if (!_graph.FindNearestPoint(point, maxDistance, out projected, out _, out int triangle))
                return false;
            clearance = _graph.GetClearance(triangle);
            return true;
        }

        public bool HasLineOfSight(Vector3 from, Vector3 to, float bodyRadius)
            => _graph.HasLineOfSight(from, to, bodyRadius);
    }
}
```

- [ ] **步骤 5：运行测试确认通过**

```bash
bash tools/test-server.sh --filter "FullyQualifiedName~NavigationServiceTests"
```

预期：3 个用例 PASS。

- [ ] **步骤 6：Commit**

```bash
git add Server/Navigation/NavigationService.cs Server/Config/MapsConfig.cs Server/Config/ConfigLoader.cs Server/Config/maps.json Server/Server.Tests/Navigation/NavigationServiceTests.cs
git commit -m "feat(nav): add navigation service and map config"
```

---

## 任务 5：`MoveTowards` 与 ORCA 实际位移

**文件：**
- 修改：`Server/Monster/SimpleMoveSystem.cs`
- 修改：`Server/Movement/AvoidanceSystem.cs`
- 修改：`Server/Server.Tests/Movement/AvoidanceSystemTests.cs`（旧 liveness 断言依赖旧 `Speed × 朝向`
  口径下“对称四体永远绕漩涡”；新语义下四体停在最优环形，停住才是正确收敛解，断言按新语义更新）
- 测试：`Server/Server.Tests/Movement/AvoidanceVelocityTests.cs`

- [ ] **步骤 1：写失败的 ORCA 速度测试**

`AvoidanceVelocityTests.cs`：

```csharp
using KcpServer;
using KcpServer.Monster;
using KcpServer.Movement;
using Xunit;

namespace KcpServer.Tests.Movement;

public class AvoidanceVelocityTests
{
    [Fact]
    public void Snapshot_Uses_Actual_Displacement_Not_Facing()
    {
        var store = new EntityStore();
        long seed = MonsterModule.NpcIdSeed;
        var module = new MonsterModule(store, () => ++seed);
        long npcId = module.SpawnMonster(1, new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 0f));
        var entity = store.GetPlayer(npcId)!;

        var avoidance = new AvoidanceSystem(store, _ => 0.5f);
        avoidance.EnsureSnapshot(0);
        Assert.Equal(0f, avoidance.SnapshotVelocityOf(npcId).VX, 4);

        entity.UpdatePosition(new Vector3(0.5f, 0f, 0f), 90f, 0f, 1L, 0U); // 朝向 +X，实际也 +X
        avoidance.EnsureSnapshot(1);
        Assert.Equal(0.5f, avoidance.SnapshotVelocityOf(npcId).VX, 4);
        Assert.Equal(0f, avoidance.SnapshotVelocityOf(npcId).VZ, 4);

        entity.UpdatePosition(new Vector3(0.5f, 0f, 0.5f), 90f, 0f, 1L, 0U); // 朝向 +X，实际 +Z
        avoidance.EnsureSnapshot(2);
        Assert.Equal(0f, avoidance.SnapshotVelocityOf(npcId).VX, 4);
        Assert.Equal(0.5f, avoidance.SnapshotVelocityOf(npcId).VZ, 4);
    }
}
```

- [ ] **步骤 2：运行确认失败**

```bash
bash tools/test-server.sh --filter "FullyQualifiedName~AvoidanceVelocityTests"
```

预期：编译失败，`SnapshotVelocityOf` 不存在（或行为断言失败）。

- [ ] **步骤 3：实现 AvoidanceSystem 实际位移**

在 `AvoidanceSystem` 增加字段：

```csharp
private readonly Dictionary<long, (float X, float Z)> _lastPositions = new();
private readonly List<long> _stalePositions = new();
```

把 `EnsureSnapshot` 中 `participates` 分支的速度计算替换为：

```csharp
float vx = 0f, vz = 0f, speedPerTick = 0f;
if (participates)
{
    if (_lastPositions.TryGetValue(e.PlayerId, out var last))
    {
        vx = e.Position.X - last.X;
        vz = e.Position.Z - last.Z;
        speedPerTick = MathF.Sqrt(vx * vx + vz * vz);
    }
    _lastPositions[e.PlayerId] = (e.Position.X, e.Position.Z);
}
else
{
    _lastPositions.Remove(e.PlayerId);
}
```

在 `EnsureSnapshot` 末尾（排序后）增加清理：

```csharp
if (_snapshotTick % 300 == 0)
{
    _stalePositions.Clear();
    foreach (var kv in _lastPositions)
        if (_store.GetPlayer(kv.Key) == null)
            _stalePositions.Add(kv.Key);
    foreach (long id in _stalePositions) _lastPositions.Remove(id);
}
```

增加测试访问器：

```csharp
internal (float VX, float VZ) SnapshotVelocityOf(long id)
{
    for (int i = 0; i < _agents.Count; i++)
        if (_agents[i].Id == id)
            return (_agents[i].VX, _agents[i].VZ);
    return (0f, 0f);
}
```

同时把 `EnsureSnapshot` 里“怪物/机器人取上一 tick flush 的 Speed × 朝向”的注释改成“上一 tick 的实际位移”；`Speed` 字段仍为 `|v|`。

- [ ] **步骤 4：实现 SimpleMoveSystem.MoveTowards**

`Server/Monster/SimpleMoveSystem.cs` 把 `MoveTo` 改为：

```csharp
public void MoveTo(MonsterState m, Vector3 targetPosition, float deltaTime)
{
    Vector3 direction = targetPosition - m.Position;
    direction.Y = 0;
    MoveTowards(m, direction, deltaTime);
}

public void MoveTowards(MonsterState m, Vector3 direction, float deltaTime)
{
    direction.Y = 0;
    if (direction.X * direction.X + direction.Z * direction.Z < 0.001f)
    {
        m.CurrentVelocity = default;
        return;
    }

    direction = direction.Normalize();
    Quaternion targetRotation = Quaternion.LookRotation(direction);
    m.Rotation = RotateTowards(m.Rotation, targetRotation, RotationSpeed * deltaTime);

    Vector3 movement = direction * m.Config.moveSpeed * deltaTime;
    if (_avoidance != null)
    {
        float movementLength = (float)Math.Sqrt(movement.X * movement.X + movement.Z * movement.Z);
        float configuredLength = m.Config.moveSpeed * deltaTime;
        float maxStep = movementLength > configuredLength ? movementLength : configuredLength;
        movement = _avoidance.Solve(m.NpcId, m.TargetId, m.Position, m.CurrentVelocity,
            movement, m.Config.bodyRadius, maxStep);
    }

    m.CurrentVelocity = movement;
    m.Position = m.Position + movement;
}
```

保持 `direction * m.Config.moveSpeed * deltaTime` 的结合顺序与注释，勿改成 `direction * (speed*dt)`。

- [ ] **步骤 5：运行测试与全量服务端测试**

```bash
bash tools/test-server.sh --filter "FullyQualifiedName~AvoidanceVelocityTests"
bash tools/test-server.sh
```

预期：新用例 PASS；全量服务端测试不红。若既有 ORCA 用例因邻居速度口径变化失败，先检查失败断言是否依赖 `Speed × 朝向`；不要直接改期望值，确认语义后再改。

- [ ] **步骤 6：Commit**

```bash
git add Server/Monster/SimpleMoveSystem.cs Server/Movement/AvoidanceSystem.cs Server/Server.Tests/Movement/AvoidanceVelocityTests.cs
git commit -m "refactor(movement): use actual displacement for ORCA velocity"
```

---

## 任务 6：PathFollower

**文件：**
- 创建：`Server/Monster/Movement/PathFollower.cs`
- 测试：`Server/Server.Tests/Navigation/PathFollowerTests.cs`

> **计划修正（2026-11-02，审查第 1 轮）**：
> - 路径成功后只推进 `_nextRepathTick`，**不再推进 `_nextAttemptTick`**——否则目标移动 >2m 的立即重寻被 15 tick 闸门挡成死代码。
> - 周期重寻相位改为 `npcId % RepathIntervalTicks` 错峰，避免多只怪同 tick 重寻压垮每 tick 查询预算。
> - 成功获得路径时初始化卡死采样点/tick 并清零窗口；索引 0 跳过偏离判定（`Waypoints[0]` 即投影起点，没有“上一路点”）。
> - 测试补充：目标移动立即重寻、npcId 错峰、blocked 30 tick 退避、卡死 3 窗口强制重寻、无导航 LOS/投影；`Follows_Path_And_Reaches_Goal` 每 tick 调 `BeginTick`。

- [ ] **步骤 1：写失败的测试**

`PathFollowerTests.cs`：

```csharp
using KcpServer;
using KcpServer.Config;
using KcpServer.Monster;
using KcpServer.Navigation;
using Xunit;

namespace KcpServer.Tests.Navigation;

public class PathFollowerTests
{
    // npcId 默认取 15 的倍数：重寻相位 = npcId % 15 = 0，周期重寻正好落在 tick 15/30...
    private static (MonsterState Monster, PathFollower Follower) Setup(NavigationService? nav, long npcId = 15)
    {
        var config = new MonsterData
        {
            templateId = 1,
            name = "Slime",
            hp = 100f,
            moveSpeed = 2f,
            bodyRadius = 0.5f,
            detectionRadius = 8f,
            visionAngle = 120f,
            attackRange = 1.5f,
            patrolRadius = 5f
        };
        var monster = new MonsterState(npcId, 1, config, 50001)
        {
            Position = new Vector3(1f, 0f, 1f),
            SpawnPosition = new Vector3(1f, 0f, 1f),
            PatrolCenter = new Vector3(1f, 0f, 1f)
        };
        return (monster, new PathFollower(nav));
    }

    [Fact]
    public void No_Navigation_Falls_Back_To_Direct_Move()
    {
        var (monster, follower) = Setup(null);
        var mover = new SimpleMoveSystem();
        follower.Tick(monster, new Vector3(5f, 0f, 1f), mover, 0.066f, 0);
        Assert.True(monster.Position.X > 1f);
    }

    [Fact]
    public void No_Navigation_Passes_Through_Los_And_Projection()
    {
        var (_, follower) = Setup(null);
        Assert.True(follower.HasLineOfSight(new Vector3(1f, 0f, 1f), new Vector3(9f, 0f, 9f), 0.5f));

        var point = new Vector3(3.5f, 0f, -2f);
        Assert.False(follower.TryProject(point, out Vector3 projected));
        Assert.Equal(point.X, projected.X);
        Assert.Equal(point.Y, projected.Y);
        Assert.Equal(point.Z, projected.Z);
    }

    [Fact]
    public void Follows_Path_And_Reaches_Goal()
    {
        NavigationService nav = new NavigationService(NavMeshFixture.Grid(12, 12, (_, _) => true), "test");
        var (monster, follower) = Setup(nav);
        var mover = new SimpleMoveSystem();
        var goal = new Vector3(8f, 0f, 8f);
        for (uint t = 0; t < 300; t++)
        {
            nav.BeginTick(t);
            follower.Tick(monster, goal, mover, 0.066f, t);
        }
        float dx = monster.Position.X - goal.X, dz = monster.Position.Z - goal.Z;
        Assert.True(dx * dx + dz * dz < 1.0f, $"dist={System.MathF.Sqrt(dx * dx + dz * dz)}");
    }

    [Fact]
    public void Off_Path_Deviation_Forces_Repath()
    {
        NavigationService nav = new NavigationService(NavMeshFixture.Grid(20, 20, (_, _) => true), "test");
        var (monster, follower) = Setup(nav);
        var mover = new SimpleMoveSystem();
        var goal = new Vector3(10f, 0f, 10f);
        follower.Tick(monster, goal, mover, 0.066f, 0);
        int before = nav.QueryCount;

        monster.Position = new Vector3(6f, 0f, 1f); // 远离 (1,1)→(10,10) 的路径 >1.5m
        follower.Tick(monster, goal, mover, 0.066f, 1);
        follower.Tick(monster, goal, mover, 0.066f, 2);
        Assert.True(nav.QueryCount > before, "off-path monster should request a new path");
    }

    [Fact]
    public void Budget_Rejection_Retries_Next_Tick()
    {
        NavigationService nav = new NavigationService(NavMeshFixture.Grid(12, 12, (_, _) => true), "test")
        {
            MaxQueriesPerTick = 0
        };
        var (monster, follower) = Setup(nav);
        var mover = new SimpleMoveSystem();
        follower.Tick(monster, new Vector3(8f, 0f, 8f), mover, 0.066f, 0);
        Assert.Equal(0, nav.QueryCount);

        nav.MaxQueriesPerTick = 1;
        follower.Tick(monster, new Vector3(8f, 0f, 8f), mover, 0.066f, 1);
        Assert.Equal(1, nav.QueryCount);
    }

    [Fact]
    public void Repath_Is_Throttled_Within_Interval()
    {
        NavigationService nav = new NavigationService(NavMeshFixture.Grid(12, 12, (_, _) => true), "test");
        var (monster, follower) = Setup(nav); // npcId 15 → 相位 0，首次周期重寻在 tick 15
        var mover = new SimpleMoveSystem();
        var goal = new Vector3(8f, 0f, 8f);
        follower.Tick(monster, goal, mover, 0.066f, 0);
        int afterFirst = nav.QueryCount;
        for (uint t = 1; t < 15; t++) follower.Tick(monster, goal, mover, 0.066f, t);
        Assert.Equal(afterFirst, nav.QueryCount);

        follower.Tick(monster, goal, mover, 0.066f, 15);
        Assert.Equal(afterFirst + 1, nav.QueryCount);
    }

    [Fact]
    public void Goal_Move_Forces_Repath_Before_Interval()
    {
        NavigationService nav = new NavigationService(NavMeshFixture.Grid(12, 12, (_, _) => true), "test");
        var (monster, follower) = Setup(nav);
        var mover = new SimpleMoveSystem();
        follower.Tick(monster, new Vector3(8f, 0f, 8f), mover, 0.066f, 0);
        int afterFirst = nav.QueryCount;

        // 周期重寻要到 tick 15，但目标移动 >2m 必须立即触发重寻。
        follower.Tick(monster, new Vector3(8f, 0f, 2f), mover, 0.066f, 5);
        Assert.True(nav.QueryCount > afterFirst, "moved goal should repath before the 15-tick interval");
    }

    [Fact]
    public void Repath_Phase_Is_Staggered_By_NpcId()
    {
        NavigationService nav = new NavigationService(NavMeshFixture.Grid(12, 12, (_, _) => true), "test");
        var (monsterA, followerA) = Setup(nav, 1); // 相位 1 → tick 16
        var (monsterB, followerB) = Setup(nav, 2); // 相位 2 → tick 17
        var mover = new SimpleMoveSystem();
        var goal = new Vector3(8f, 0f, 8f);

        nav.BeginTick(0);
        followerA.Tick(monsterA, goal, mover, 0.066f, 0);
        followerB.Tick(monsterB, goal, mover, 0.066f, 0);
        int afterInitial = nav.QueryCount;

        for (uint t = 1; t < 16; t++)
        {
            nav.BeginTick(t);
            followerA.Tick(monsterA, goal, mover, 0.066f, t);
            followerB.Tick(monsterB, goal, mover, 0.066f, t);
        }
        Assert.Equal(afterInitial, nav.QueryCount);

        nav.BeginTick(16);
        followerA.Tick(monsterA, goal, mover, 0.066f, 16);
        followerB.Tick(monsterB, goal, mover, 0.066f, 16);
        Assert.Equal(afterInitial + 1, nav.QueryCount); // 只有相位 1 的 A 重寻

        nav.BeginTick(17);
        followerA.Tick(monsterA, goal, mover, 0.066f, 17);
        followerB.Tick(monsterB, goal, mover, 0.066f, 17);
        Assert.Equal(afterInitial + 2, nav.QueryCount); // 相位 2 的 B 在下一 tick 重寻
    }

    [Fact]
    public void Blocked_Path_Backs_Off_30_Ticks()
    {
        // x == 6 整列不可走：起点（左）与目标（右）分属两个连通分量 → "blocked"。
        NavigationService nav = new NavigationService(NavMeshFixture.Grid(12, 12, (x, _) => x != 6), "test");
        var (monster, follower) = Setup(nav);
        var mover = new SimpleMoveSystem();
        var goal = new Vector3(8f, 0f, 8f);

        follower.Tick(monster, goal, mover, 0.066f, 0);
        Assert.Equal(1, nav.QueryCount);

        for (uint t = 1; t < 30; t++)
        {
            nav.BeginTick(t);
            follower.Tick(monster, goal, mover, 0.066f, t);
        }
        Assert.Equal(1, nav.QueryCount);

        nav.BeginTick(30);
        follower.Tick(monster, goal, mover, 0.066f, 30);
        Assert.Equal(2, nav.QueryCount);
    }

    [Fact]
    public void Stuck_For_Three_Windows_Forces_Repath()
    {
        NavigationService nav = new NavigationService(NavMeshFixture.Grid(12, 12, (_, _) => true), "test");
        var (monster, follower) = Setup(nav);
        monster.Config.moveSpeed = 0f; // 每 tick 位移为 0 → 卡死窗口累积
        var mover = new SimpleMoveSystem();
        var goal = new Vector3(8f, 0f, 8f);

        nav.BeginTick(0);
        follower.Tick(monster, goal, mover, 0.066f, 0);
        Assert.Equal(1, nav.QueryCount);
        Assert.True(follower.HasPath);

        // 预算耗尽：周期重寻被拒绝但保留旧路径，卡死窗口才能连续累积到 3。
        nav.MaxQueriesPerTick = 0;
        for (uint t = 1; t <= 30; t++)
        {
            nav.BeginTick(t);
            follower.Tick(monster, goal, mover, 0.066f, t);
        }
        Assert.Equal(1, nav.QueryCount);
        Assert.False(follower.HasPath); // tick 30 第三次卡死窗口清掉路径

        nav.MaxQueriesPerTick = 1;
        nav.BeginTick(31);
        follower.Tick(monster, goal, mover, 0.066f, 31);
        Assert.Equal(2, nav.QueryCount); // 下 tick 立即重寻
    }
}
```

- [ ] **步骤 2：运行确认失败**

```bash
bash tools/test-server.sh --filter "FullyQualifiedName~PathFollowerTests"
```

预期：编译失败，`PathFollower` 不存在。

- [ ] **步骤 3：实现 PathFollower**

`Server/Monster/Movement/PathFollower.cs`：

```csharp
using System;
using KcpServer.Navigation;

namespace KcpServer.Monster
{
    // P1 用同步预算（NavigationService.TryFindPath 按 tick 计数），请求不排队，
    // 因此没有“过期请求”需要丢弃；失败后按 FailureBackoffTicks=30 退避。
    public sealed class PathFollower
    {
        private const uint RepathIntervalTicks = 15;
        private const uint FailureBackoffTicks = 30;
        private const float TargetMoveThreshold = 2f;
        private const float StuckWindowTicks = 10;
        private const int StuckWindowLimit = 3;

        private readonly NavigationService? _navigation;
        private NavPath? _path;
        private int _index;
        private Vector3 _goal;
        private uint _nextRepathTick;
        private uint _nextAttemptTick;
        private Vector3 _stuckCheckPosition;
        private uint _stuckCheckTick;
        private int _stuckWindows;

        public PathFollower(NavigationService? navigation) => _navigation = navigation;

        /// <summary>无导航时保持旧行为（不阻断攻击）。</summary>
        public bool HasLineOfSight(Vector3 from, Vector3 to, float bodyRadius)
            => _navigation == null || _navigation.HasLineOfSight(from, to, bodyRadius);

        /// <summary>停步判定用 navmesh 投影点；无导航时原样返回。</summary>
        public bool TryProject(Vector3 point, out Vector3 projected)
        {
            projected = point;
            return _navigation != null && _navigation.TryProject(point, 2f, out projected, out _);
        }

        // 状态观察口（Task 7 状态机接入前暂无调用者；HasPath 供卡死/重寻测试观察）。
        public bool HasPath => _path != null;
        public int WaypointIndex => _index;

        public void Tick(MonsterState monster, Vector3 goal, SimpleMoveSystem mover, float deltaTime, uint tick)
        {
            if (_navigation == null)
            {
                mover.MoveTo(monster, goal, deltaTime);
                return;
            }

            bool goalMoved = DistanceXZ(goal, _goal) > TargetMoveThreshold;
            bool due = tick >= _nextRepathTick;
            bool noPath = _path == null;
            if ((noPath || due || goalMoved) && tick >= _nextAttemptTick)
            {
                var query = new NavQuery(_navigation.MapId, monster.Position, goal, monster.Config.bodyRadius);
                if (_navigation.TryFindPath(query, out NavPath path))
                {
                    _path = path;
                    _goal = goal;
                    _index = 0;
                    // 相位错峰：200 只怪按 npcId 分散首次周期重寻，避免预算被同步波压垮。
                    // 不推进 _nextAttemptTick——否则 goalMoved 会被 15 tick 间隔闸门挡成死代码；
                    // goalMoved 自身有 2m 阈值，成功后 _goal 更新即复位，不会每 tick 重寻。
                    uint phase = (uint)(monster.NpcId % RepathIntervalTicks);
                    _nextRepathTick = tick + RepathIntervalTicks + phase;
                    _stuckCheckPosition = monster.Position;
                    _stuckCheckTick = tick;
                    _stuckWindows = 0;
                    SkipPassedWaypoints(monster);
                }
                else
                {
                    // 预算拒绝不是寻路失败：下 tick 立刻重试；真正失败才退避。
                    _nextAttemptTick = path.FailReason == "budget" ? tick + 1 : tick + FailureBackoffTicks;
                }
            }

            if (_path == null)
            {
                mover.MoveTo(monster, goal, deltaTime);
                return;
            }

            AdvanceWaypoints(monster);
            if (_index >= _path.Waypoints.Length)
            {
                mover.MoveTo(monster, goal, deltaTime);
                return;
            }

            Vector3 waypoint = _path.Waypoints[_index];
            // index 0 没有“上一路点”（Waypoints[0] 就是投影起点），线段退化成点：
            // 起点在 navmesh 外 1.5–2m 时会每 tick 重寻。此时跳过偏离判定，先走向投影起点。
            if (_index > 0 && DistancePointSegment(monster.Position, _path.Waypoints[_index - 1], waypoint) > 1.5f)
            {
                _path = null;
                _nextRepathTick = tick;
                _nextAttemptTick = tick;
                mover.MoveTo(monster, goal, deltaTime);
                return;
            }

            Vector3 direction = waypoint - monster.Position;
            direction.Y = 0f;
            mover.MoveTowards(monster, direction, deltaTime);

            CheckStuck(monster, goal, tick);
        }

        // 执行偏差（Task 7 集成暴露）：原定 max(bodyRadius*0.5, moveSpeed*0.25) 对 Wolf（3.5 m/s）
        // 是 0.875m，会在墙角路点前约 0.86m 提前跳点、直线切过墙格（MonsterNavigationTests 的
        // 绕墙追击用例在 t=48 进墙）。改为只看 bodyRadius*0.5。若将来要恢复 moveSpeed 项，
        // 必须证明/实现额外的墙角保护（例如推进前校验当前点到下一路点的 LOS）。
        // 不变量：moveSpeed*dt < bodyRadius（Wolf 3.5*0.066=0.231 < 0.5），否则单 tick 位移
        // 可能跨过推进半径、终点路点绕圈。
        private void AdvanceWaypoints(MonsterState monster)
        {
            float radius = monster.Config.bodyRadius * 0.5f;
            int guard = 0;
            while (_index < _path!.Waypoints.Length && guard++ < _path.Waypoints.Length + 1)
            {
                Vector3 point = _path.Waypoints[_index];
                if (DistanceXZ(monster.Position, point) <= radius)
                {
                    _index++;
                    continue;
                }

                Vector3 next = _index + 1 < _path.Waypoints.Length ? _path.Waypoints[_index + 1] : _path.Waypoints[^1];
                float vx = monster.Position.X - point.X, vz = monster.Position.Z - point.Z;
                float wx = next.X - point.X, wz = next.Z - point.Z;
                if (vx * wx + vz * wz > 0f)
                {
                    _index++;
                    continue;
                }
                break;
            }
        }

        private void SkipPassedWaypoints(MonsterState monster)
        {
            float radius = monster.Config.bodyRadius * 0.5f;
            while (_index < _path!.Waypoints.Length && DistanceXZ(monster.Position, _path.Waypoints[_index]) <= radius)
                _index++;
        }

        private void CheckStuck(MonsterState monster, Vector3 goal, uint tick)
        {
            if (tick - _stuckCheckTick < StuckWindowTicks) return;

            float moved = DistanceXZ(monster.Position, _stuckCheckPosition);
            bool inAttackRange = DistanceXZ(monster.Position, goal) <= monster.Config.attackRange;
            if (moved < 0.2f && !inAttackRange) _stuckWindows++;
            else _stuckWindows = 0;

            _stuckCheckPosition = monster.Position;
            _stuckCheckTick = tick;

            if (_stuckWindows >= StuckWindowLimit)
            {
                _path = null;
                _nextRepathTick = tick;
                _nextAttemptTick = tick;
                _stuckWindows = 0;
            }
        }

        private static float DistanceXZ(Vector3 a, Vector3 b)
        {
            float dx = a.X - b.X, dz = a.Z - b.Z;
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        private static float DistancePointSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            float dx = b.X - a.X, dz = b.Z - a.Z;
            float lenSq = dx * dx + dz * dz;
            float t = lenSq <= 0.000001f ? 0f : ((p.X - a.X) * dx + (p.Z - a.Z) * dz) / lenSq;
            t = Math.Clamp(t, 0f, 1f);
            float x = a.X + dx * t, z = a.Z + dz * t;
            float ex = p.X - x, ez = p.Z - z;
            return MathF.Sqrt(ex * ex + ez * ez);
        }
    }
}
```

- [ ] **步骤 4：运行测试确认通过**

```bash
bash tools/test-server.sh --filter "FullyQualifiedName~PathFollowerTests"
```

预期：10 个用例 PASS。

- [ ] **步骤 5：Commit**

```bash
git add Server/Monster/Movement/PathFollower.cs Server/Server.Tests/Navigation/PathFollowerTests.cs
git commit -m "feat(monster): add navmesh path follower"
```

---

## 任务 7：MonsterModule / 行为树接入寻路

**文件：**
- 修改：`Server/Monster/MonsterModule.cs`
- 修改：`Server/Monster/MonsterState.cs`
- 修改：`Server/Monster/MonsterBtActions.cs`
- 测试：`Server/Server.Tests/Monster/MonsterNavigationTests.cs`

- [ ] **步骤 1：写失败的集成测试**

`MonsterNavigationTests.cs`：

```csharp
using KcpServer;
using KcpServer.Monster;
using KcpServer.Navigation;
using KcpServer.Tests.Navigation;
using Xunit;

namespace KcpServer.Tests.Monster;

public class MonsterNavigationTests
{
    [Fact]
    public void Wolf_Chases_Player_Around_Wall_Without_Entering_Blocked_Cells()
    {
        NavMeshGraph graph = NavMeshFixture.Grid(12, 12, (x, z) => !(z == 5 && x < 10));
        var nav = new NavigationService(graph, "test");

        var store = new EntityStore();
        long seed = MonsterModule.NpcIdSeed;
        var module = new MonsterModule(store, () => ++seed, navigation: nav);
        long wolfId = module.SpawnMonster(2, new Vector3(1f, 0f, 1f), new Vector3(1f, 0f, 1f));
        var wolf = store.GetPlayer(wolfId)!;
        wolf.Rotation = 0f; // 面向 +Z

        var player = store.AddPlayer(1, "P1");
        player.IsLoggedIn = true;
        player.UpdatePosition(new Vector3(1f, 0f, 9f), 0f, 0f, 1L, 0U);

        for (uint t = 0; t < 400; t++)
        {
            module.Tick(t);
            // 不允许穿过 z=5 的墙
            Assert.False(wolf.Position.Z >= 5f && wolf.Position.Z < 6f && wolf.Position.X < 10f,
                $"t={t} wolf entered wall at ({wolf.Position.X:F2},{wolf.Position.Z:F2})");
        }

        float dx = wolf.Position.X - player.Position.X;
        float dz = wolf.Position.Z - player.Position.Z;
        Assert.True(dx * dx + dz * dz <= 2.5f * 2.5f,
            $"wolf should reach attack range, pos=({wolf.Position.X:F2},{wolf.Position.Z:F2})");
    }

    [Fact]
    public void Target_Is_Kept_When_Out_Of_Vision_But_Within_Drop_Radius()
    {
        NavMeshGraph graph = NavMeshFixture.Grid(30, 30, (_, _) => true);
        var nav = new NavigationService(graph, "test");
        var store = new EntityStore();
        long seed = MonsterModule.NpcIdSeed;
        var module = new MonsterModule(store, () => ++seed, navigation: nav);
        long wolfId = module.SpawnMonster(2, new Vector3(10f, 0f, 10f), new Vector3(10f, 0f, 10f));
        var wolf = store.GetPlayer(wolfId)!;
        wolf.Rotation = 0f;

        var player = store.AddPlayer(1, "P1");
        player.IsLoggedIn = true;
        player.UpdatePosition(new Vector3(10f, 0f, 20f), 0f, 0f, 1L, 0U);
        for (uint t = 0; t < 5; t++) module.Tick(t);
        Assert.Equal(1L, module.GetState(wolfId)!.TargetId);

        // 目标走到怪物背后（视野锥外），但仍在 drop radius 内
        player.UpdatePosition(new Vector3(10f, 0f, 0f), 0f, 0f, 2L, 0U);
        for (uint t = 5; t < 10; t++) module.Tick(t);
        Assert.Equal(1L, module.GetState(wolfId)!.TargetId);
    }

    [Fact]
    public void Monster_Does_Not_Attack_Through_Wall()
    {
        NavMeshGraph graph = NavMeshFixture.Grid(12, 12, (x, z) => !(z == 5 && x < 10));
        var nav = new NavigationService(graph, "test");
        var store = new EntityStore();
        long seed = MonsterModule.NpcIdSeed;
        var module = new MonsterModule(store, () => ++seed, navigation: nav);
        long wolfId = module.SpawnMonster(2, new Vector3(1f, 0f, 4.5f), new Vector3(1f, 0f, 4.5f));
        var wolf = store.GetPlayer(wolfId)!;
        wolf.Rotation = 0f;

        var player = store.AddPlayer(1, "P1");
        player.IsLoggedIn = true;
        player.UpdatePosition(new Vector3(1f, 0f, 6.4f), 0f, 0f, 1L, 0U);

        for (uint t = 0; t < 60; t++)
        {
            module.Tick(t);
            Assert.Empty(module.DrainPendingCasts());
        }
    }

    [Fact]
    public void Spawn_Off_Navmesh_Throws()
    {
        // 4x4 大洞的中心距最近 navmesh 2m，超过 0.5m 投影容差
        NavMeshGraph graph = NavMeshFixture.Grid(12, 12, (x, z) => !(x >= 4 && x < 8 && z >= 4 && z < 8));
        var nav = new NavigationService(graph, "test");
        var store = new EntityStore();
        long seed = MonsterModule.NpcIdSeed;
        var module = new MonsterModule(store, () => ++seed, navigation: nav);

        Assert.Throws<System.ArgumentException>(() =>
            module.SpawnMonster(1, new Vector3(6f, 0f, 6f), new Vector3(6f, 0f, 6f)));
    }

    [Fact]
    public void Without_Navigation_Monster_Still_Moves_Legacy()
    {
        var store = new EntityStore();
        long seed = MonsterModule.NpcIdSeed;
        var module = new MonsterModule(store, () => ++seed);
        long id = module.SpawnMonster(1, new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 0f));
        var entity = store.GetPlayer(id)!;
        Vector3 start = entity.Position;
        for (uint t = 0; t < 60; t++) module.Tick(t);
        Assert.NotEqual(start, entity.Position);
    }
}
```

- [ ] **步骤 2：运行确认失败**

```bash
bash tools/test-server.sh --filter "FullyQualifiedName~MonsterNavigationTests"
```

预期：编译失败，`MonsterModule` 构造函数没有 `navigation` 参数 / `MonsterState.Follower` 不存在。

- [ ] **步骤 3：修改 MonsterState**

在 `Server/Monster/MonsterState.cs` 增加：

```csharp
/// <summary>P1：路点跟随器（无导航时为 null 回退直线移动）</summary>
public PathFollower? Follower { get; set; }
```

- [ ] **步骤 4：修改 MonsterModule**

构造函数增加参数：

```csharp
public MonsterModule(EntityStore store, Func<long> nextNpcId, string? configPath = null,
    AvoidanceSystem? avoidance = null, NavigationService? navigation = null)
{
    // 原有 EntityStore / 模板加载 / 避让系统初始化保持不变
    _navigation = navigation;
}
```

字段与 using：

```csharp
using KcpServer.Navigation;
private readonly NavigationService? _navigation;
```

`Tick` 开头：

```csharp
_navigation?.BeginTick(tick);
```

`SpawnMonster` 中投影出生点与巡逻点：

```csharp
Vector3 spawn = position;
if (_navigation != null)
{
    const float spawnProjectionTolerance = 0.5f;
    if (!_navigation.TryProject(position, spawnProjectionTolerance, out Vector3 projected, out _))
        throw new ArgumentException($"Spawn position {position} is not on the navmesh");
    spawn = projected;
}

// 原有模板校验、技能校验、AddNpc、config.bodyRadius 归一化保持不变
var state = new MonsterState(npcId, templateId, config, attackSkillId)
{
    Position = spawn,
    Rotation = Quaternion.LookRotation(ForwardOf(entity.Rotation)),
    SpawnPosition = spawn,
    PatrolCenter = patrolCenter,
    PatrolPoints = GeneratePatrolPoints(patrolCenter, config.patrolRadius, config.bodyRadius),
    LastHp = config.hp,
    Follower = new PathFollower(_navigation)
};
```

把原 `GeneratePatrolPoints` 改名为 `GeneratePatrolPointsLegacy`，新增：

```csharp
private Vector3[] GeneratePatrolPoints(Vector3 center, float radius, float bodyRadius)
{
    if (_navigation == null) return GeneratePatrolPointsLegacy(center, radius);

    var points = new List<Vector3>(4);
    for (int i = 0; i < 4; i++)
    {
        float rad = i * (360f / 4) * (float)(Math.PI / 180.0);
        var candidate = new Vector3(
            center.X + (float)Math.Cos(rad) * radius,
            center.Y,
            center.Z + (float)Math.Sin(rad) * radius);

        if (TryFindPatrolPoint(candidate, bodyRadius, out Vector3 point))
            points.Add(point);
    }

    return points.Count > 0 ? points.ToArray() : GeneratePatrolPointsLegacy(center, radius);
}

private bool TryFindPatrolPoint(Vector3 candidate, float bodyRadius, out Vector3 point)
{
    point = candidate;
    const float step = 0.5f;
    for (int i = 0; i < 9; i++)
    {
        float angle = i * 45f * (float)(Math.PI / 180.0);
        Vector3 probe = i == 0
            ? candidate
            : new Vector3(candidate.X + (float)Math.Cos(angle) * step, candidate.Y,
                candidate.Z + (float)Math.Sin(angle) * step);
        if (_navigation!.TryProject(probe, 0.5f, out Vector3 projected, out float clearance)
            && clearance >= bodyRadius)
        {
            point = projected;
            return true;
        }
    }
    return false;
}
```

- [ ] **步骤 4b：最小目标保持（P1 绕墙追击需要；P2 由 ThreatTable 替换）**

把 `MonsterModule.DetectTarget` 整体替换为：

```csharp
private void DetectTarget(MonsterState m, PlayerState entity)
{
    // P1 最小目标保持：绕墙追击时目标离开视野锥不能立刻丢；P2 由 ThreatTable 替换。
    // 前摇期挂起保持（D-10 前摇 = 承诺）：沿用旧视野锥语义，保住 MonsterAcceptanceTests A2
    // 的「丢目标 ≠ 打不中」；否则怪会追着视野外的目标转向，65° 空挥变命中。
    if (m.TargetId.HasValue && !m.AttackResolveTick.HasValue)
    {
        PlayerState? current = _store.GetPlayer(m.TargetId.Value);
        if (current != null && current.IsAlive && current.IsLoggedIn
            && DistanceXZ(current.Position, m.Position) <= DropAggroRadius(m.Config))
        {
            m.LastKnownTargetPosition = current.Position;
            return;
        }
        m.TargetId = null;
    }

    long? closestId = null;
    Vector3? closestPos = null;
    float closestDistSqr = float.MaxValue;
    foreach (var p in _store.All)
    {
        if (p.Kind != (byte)EntityKind.Player || !p.IsAlive || !p.IsLoggedIn) continue;
        if (!TargetDetector.CanDetect(entity.Rotation, m.Position, p.Position,
                m.Config.detectionRadius, m.Config.visionAngle)) continue;
        float dx = p.Position.X - m.Position.X, dz = p.Position.Z - m.Position.Z;
        float distSqr = dx * dx + dz * dz;
        if (distSqr < closestDistSqr) { closestDistSqr = distSqr; closestId = p.PlayerId; closestPos = p.Position; }
    }

    if (closestId.HasValue)
    {
        m.TargetId = closestId;
        m.AlertLevel = AlertLevel.HOSTILE;
        m.LastKnownTargetPosition = closestPos;
    }
    else if (m.TargetId.HasValue)
    {
        m.TargetId = null; // 前摇期按旧语义丢失目标（保持「丢目标 ≠ 打不中」的判定锥验收）
    }
}

private static float DropAggroRadius(MonsterData config)
    => config.detectionRadius + config.patrolRadius + 10f;

private static float DistanceXZ(Vector3 a, Vector3 b)
{
    float dx = a.X - b.X, dz = a.Z - b.Z;
    return MathF.Sqrt(dx * dx + dz * dz);
}
```

> **执行偏差（Task 7 修复轮 1/5）**：原步骤为**无条件** drop-radius 保持。集成发现它会让
> 前摇期（`AttackResolveTick`）的怪追着视野外目标转向，破坏 `MonsterAcceptanceTests.A2`
> （「丢目标 ≠ 打不中」：65° 从空挥变命中、55° 的 `TargetId` 断言失败）。现改为：windup 期
> **挂起** drop-radius 保持、沿用旧视野锥语义（扫描失败即清目标），非 windup 期按 P1 保持；
> P2 由 ThreatTable 替换。计划代码已同步加上该条件与扫描失败清目标。

- [ ] **步骤 5：修改 MonsterBtActions**

`Patrol`：

```csharp
var m = ctx.State;
if (m.PatrolPoints.Length == 0) return BtStatus.Success;

int idx = m.CurrentPatrolIndex % m.PatrolPoints.Length;
Vector3 target = m.PatrolPoints[idx];

if (m.Follower != null) m.Follower.Tick(m, target, ctx.Mover, ctx.DeltaTime, ctx.Tick);
else ctx.Mover.MoveTo(m, target, ctx.DeltaTime);

if (ctx.Mover.HasReached(m, target, 0.5f))
{
    m.CurrentPatrolIndex++;
    m.Moving = false;
    ctx.Entity.ActionState = (byte)NpcAnimationState.Idle;
}
else
{
    m.Moving = true;
    ctx.Entity.ActionState = (byte)NpcAnimationState.Run;
}
return BtStatus.Running;
```

`Chase`：

```csharp
if (m.AttackResolveTick.HasValue) return BtStatus.Success;

Vector3 target = m.LastKnownTargetPosition ?? m.Position;
// 停步判定用 navmesh 投影点；实际命中裁决仍用玩家真实位置。
// 距离内但隔墙（无 LOS）不能停步：继续 Follower.Tick 绕行，否则会冻结在墙角
// 且上一 tick 的 Run/速度继续广播（客户端“跑动但不动”）。
Vector3 stopTarget = target;
if (m.Follower != null) m.Follower.TryProject(target, out stopTarget);
bool inRange = ctx.Mover.HasReached(m, stopTarget, m.Config.attackRange);
if (inRange && (m.Follower == null
    || m.Follower.HasLineOfSight(m.Position, target, m.Config.bodyRadius)))
    return BtStatus.Success;

if (m.Follower != null) m.Follower.Tick(m, target, ctx.Mover, ctx.DeltaTime, ctx.Tick);
else ctx.Mover.MoveTo(m, target, ctx.DeltaTime);
m.Moving = true;
ctx.Entity.ActionState = (byte)NpcAnimationState.Run;
return BtStatus.Running;
```

`Return`：

```csharp
var m = ctx.State;
if (m.Follower != null) m.Follower.Tick(m, m.SpawnPosition, ctx.Mover, ctx.DeltaTime, ctx.Tick);
else ctx.Mover.MoveTo(m, m.SpawnPosition, ctx.DeltaTime);

if (ctx.Mover.HasReached(m, m.SpawnPosition, 0.5f))
{
    m.AlertLevel = AlertLevel.PEACE;
    m.Moving = false;
    ctx.Entity.ActionState = (byte)NpcAnimationState.Idle;
    return BtStatus.Success;
}
m.Moving = true;
ctx.Entity.ActionState = (byte)NpcAnimationState.Run;
return BtStatus.Running;
```

`Attack` 在原有的距离检查之后、`m.Moving = false` 之前加 LOS 检查（失败时同时落回 Idle，
避免沿用上一 tick 的 Run 广播）：

```csharp
if (m.Follower != null && !m.Follower.HasLineOfSight(m.Position, target, m.Config.bodyRadius))
{
    m.Moving = false;
    ctx.Entity.ActionState = (byte)NpcAnimationState.Idle;
    return BtStatus.Failure;
}
```

> **执行偏差（Task 7 修复轮 1/5，计划缺口）**：原 Chase 只按投影距离停步、Attack 的 LOS 失败
> 直接 `return Failure`。结果是「攻击距离内但隔墙」时 Chase 每 tick 返回 Success、不再寻路，
> Attack 又因 LOS 失败返回，怪冻结在墙角，而上一 tick 的 Run/速度继续广播。现改为：
> Chase 的成功条件同时要求 LOS，范围内无 LOS 时继续 `Follower.Tick` 绕行；Attack LOS 失败
> 时置 `Moving=false` + Idle 再返回。验收见扩展后的
> `Monster_Does_Not_Attack_Through_Wall`（60 tick 无施法且相对起点移动 > 1m）。

- [ ] **步骤 6：运行新测试与既有怪物测试**

```bash
bash tools/test-server.sh --filter "FullyQualifiedName~MonsterNavigationTests"
bash tools/test-server.sh --filter "FullyQualifiedName~MonsterModuleTests"
```

预期：新用例 PASS；既有怪物测试不红。若 `Target_Lost_Triggers_Return...` 因巡逻/回巢阈值 0.5m 变化失败，先确认是阈值还是投影导致的，修实现而不是放宽断言。

- [ ] **步骤 7：Commit**

```bash
git add Server/Monster/MonsterModule.cs Server/Monster/MonsterState.cs Server/Monster/MonsterBtActions.cs Server/Server.Tests/Monster/MonsterNavigationTests.cs
git commit -m "feat(monster): follow navmesh paths for patrol chase and return"
```

---

## 任务 8：服务端 CLI 与配置接线

**文件：**
- 修改：`Server/Server3C.cs`
- 修改：`Server/Program.cs`
- 修改：`Server/Server3C.csproj`
- 修改：`Server/Server.Tests/Server.Tests.csproj`

- [ ] **步骤 1：修改 Server3C**

```csharp
public void EnableMonsters(int count, NavigationService? navigation = null)
{
    _monsterModule = new MonsterModule(_entityStore, () => Interlocked.Increment(ref _monsterIdSeed),
        avoidance: _avoidance, navigation: navigation);
    _monsterModule.SpawnDefaultLayout(count);
}
```

顶部加 `using KcpServer.Navigation;`。

- [ ] **步骤 2：修改 Program.cs**

在 `--monsters` 解析后增加：

```csharp
string? mapId = null;
for (int i = 1; i < args.Length - 1; i++)
{
    if (args[i] == "--map" && args[i + 1].Length > 0)
    {
        mapId = args[i + 1];
        break;
    }
}

bool noNav = false;
for (int i = 1; i < args.Length; i++)
{
    if (args[i] == "--no-nav") { noNav = true; break; }
}
```

在 `if (monsterCount > 0)` 之前构造导航：

```csharp
KcpServer.Navigation.NavigationService? navigation = null;
if (monsterCount > 0 && !noNav && mapId != null)
{
    string mapsPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Config", "maps.json");
    navigation = KcpServer.Navigation.NavigationService.LoadFromConfig(mapsPath, mapId, AppContext.BaseDirectory);
    Console.WriteLine($"Navigation: map={mapId}");
}
```

调用改为：

```csharp
_server.EnableMonsters(monsterCount, navigation);
```

如果 `mapId != null` 但 `monsterCount == 0`，仍打印一行提示“--map 需要配合 --monsters”；不启动导航。

- [ ] **步骤 3：修改两个 csproj 的复制项**

`Server/Server3C.csproj` 加：

```xml
<ItemGroup>
  <None Include="Config\maps.json" Link="Config\maps.json" CopyToOutputDirectory="PreserveNewest" />
  <None Include="Config\nav\*.navbin" Link="Config\nav\%(Filename)%(Extension)" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

`Server/Server.Tests/Server.Tests.csproj` 加（路径相对 `Server.Tests`）：

```xml
<ItemGroup>
  <None Include="..\Config\maps.json" Link="Config\maps.json" CopyToOutputDirectory="PreserveNewest" />
  <None Include="..\Config\nav\*.navbin" Link="Config\nav\%(Filename)%(Extension)" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

- [ ] **步骤 4：构建并跑全量服务端测试**

```bash
dotnet build Server/Server3C.csproj
bash tools/test-server.sh
```

预期：构建成功；服务端测试全绿。`Config/nav/demoday.navbin` 尚未生成时，复制项匹配空集合不报错。

- [ ] **步骤 5：Commit**

```bash
git add Server/Server3C.cs Server/Program.cs Server/Server3C.csproj Server/Server.Tests/Server.Tests.csproj
git commit -m "feat(server): load navmesh map for monster mode"
```

---

## 任务 9：Unity 导出器、新鲜度与覆盖测试

**文件：**
- 创建：`Assets/Scripts/Editor/Navigation/NavMeshExporter.cs`
- 创建：`Assets/Scripts/Tests/Editor/Navigation/NavMeshExportFreshnessTests.cs`
- 创建：`Assets/Scripts/Tests/Editor/Navigation/DemoDayNavBinCoverageTests.cs`
- 修改：`Assets/Scripts/Tests/Editor/GameSys.EditorTests.asmdef`

- [ ] **步骤 1：在测试 asmdef 引用导出器**

`Assets/Scripts/Tests/Editor/GameSys.EditorTests.asmdef` 的 `references` 增加 `"GameSys.EditorNavigation"`。

- [ ] **步骤 2：实现导出器**

`Assets/Scripts/Editor/Navigation/NavMeshExporter.cs`：

```csharp
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
```

- [ ] **步骤 3：生成 navbin**

在 Unity 打开 DemoDay，运行 `Tools > Navigation > Export Server NavMesh`。Console 预期输出 triangles/vertices/tiles/hash，并生成 `Server/Config/nav/demoday.navbin`。

- [ ] **步骤 4：写新鲜度与覆盖测试**

`NavMeshExportFreshnessTests.cs`：

```csharp
using System.IO;
using GameSys.EditorNavigation;
using NUnit.Framework;
using UnityEngine.SceneManagement;

namespace Hotfix.GameSystems.Tests.Navigation
{
    public class NavMeshExportFreshnessTests
    {
        [Test]
        public void Committed_NavBin_Matches_Current_DemoDay_NavMesh()
        {
            if (SceneManager.GetActiveScene().path != NavMeshExporter.DemoDayScenePath)
                Assert.Ignore("Open DemoDay and rerun this test.");

            string path = Path.Combine(Directory.GetCurrentDirectory(), NavMeshExporter.OutputPath);
            Assert.IsTrue(File.Exists(path), "Run Tools/Navigation/Export Server NavMesh first.");

            ulong freshHash = NavMeshExporter.ComputeCanonicalHashForActiveScene();
            ulong committedHash = NavMeshExporter.ReadCanonicalHash(path);
            Assert.AreEqual(freshHash, committedHash,
                "Committed navbin is stale. Re-export with Tools/Navigation/Export Server NavMesh.");

            // 场景改动但 navmesh 没重烘：给 warning，不硬红（避免灯光等无关改动噪音）
            if (NavMeshExporter.ReadSourceHash(path) != NavMeshExporter.ComputeSourceHashForActiveScene())
                UnityEngine.Debug.LogWarning("DemoDay scene changed since the navbin was exported; rebake the navmesh if geometry changed.");
        }
    }
}
```

`DemoDayNavBinCoverageTests.cs`：

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace Hotfix.GameSystems.Tests.Navigation
{
    public class DemoDayNavBinCoverageTests
    {
        private static readonly (float X, float Z)[] Spawns =
        {
            (-30f, 0f), (0f, -20f), (0f, -35f), (10f, -15f), (10f, 15f), (18.333f, 18.389f)
        };

        [Test]
        public void Committed_NavBin_Contains_All_Fixed_Spawn_Points()
        {
            string path = Path.Combine(Directory.GetCurrentDirectory(), "Server/Config/nav/demoday.navbin");
            Assert.IsTrue(File.Exists(path), "Export the DemoDay navmesh first.");

            (List<(float X, float Z)> verts, List<(int A, int B, int C)> tris) = ReadTriangles(path);
            Assert.Greater(tris.Count, 0);

            foreach (var spawn in Spawns)
            {
                bool inside = false;
                foreach (var tri in tris)
                {
                    var a = verts[tri.A]; var b = verts[tri.B]; var c = verts[tri.C];
                    if (PointInTriangle(spawn.X, spawn.Z, a, b, c)) { inside = true; break; }
                }
                if (!inside)
                {
                    // 允许投影到 0.5m 内的三角面
                    float best = float.MaxValue;
                    foreach (var tri in tris)
                    {
                        var a = verts[tri.A]; var b = verts[tri.B]; var c = verts[tri.C];
                        best = Math.Min(best, DistanceToTriangle(spawn.X, spawn.Z, a, b, c));
                    }
                    Assert.LessOrEqual(best, 0.5f, $"spawn ({spawn.X},{spawn.Z}) not on navmesh, nearest={best}");
                }
            }
        }

        private static (List<(float, float)>, List<(int, int, int)>) ReadTriangles(string path)
        {
            using var stream = File.OpenRead(path);
            using var r = new BinaryReader(stream);
            r.ReadBytes(4); r.ReadUInt16();
            int mapLen = r.ReadUInt16(); r.ReadBytes(mapLen);
            r.ReadUInt64(); r.ReadUInt64();
            r.ReadSingle(); r.ReadSingle(); r.ReadSingle(); r.ReadSingle();
            r.ReadSingle();
            int tileCount = r.ReadUInt16();
            int areaCount = r.ReadByte();
            for (int i = 0; i < areaCount; i++) r.ReadSingle();
            int vertexCount = (int)r.ReadUInt32();
            var verts = new List<(float, float)>(vertexCount);
            for (int i = 0; i < vertexCount; i++)
            {
                float x = r.ReadSingle(); float y = r.ReadSingle(); float z = r.ReadSingle();
                verts.Add((x, z));
            }
            var tris = new List<(int, int, int)>();
            for (int t = 0; t < tileCount; t++)
            {
                r.ReadInt16(); r.ReadInt16();
                int triCount = (int)r.ReadUInt32();
                for (int i = 0; i < triCount; i++)
                {
                    int a = (int)r.ReadUInt32(); int b = (int)r.ReadUInt32(); int c = (int)r.ReadUInt32();
                    tris.Add((a, b, c));
                }
                r.ReadBytes(triCount);
            }
            return (verts, tris);
        }

        private static bool PointInTriangle(float px, float pz, (float X, float Z) a,
            (float X, float Z) b, (float X, float Z) c)
        {
            float d1 = Sign(px, pz, a, b);
            float d2 = Sign(px, pz, b, c);
            float d3 = Sign(px, pz, c, a);
            bool hasNeg = d1 < 0f || d2 < 0f || d3 < 0f;
            bool hasPos = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(hasNeg && hasPos);
        }

        private static float Sign(float px, float pz, (float X, float Z) a, (float X, float Z) b)
            => (px - b.X) * (a.Z - b.Z) - (a.X - b.X) * (pz - b.Z);

        private static float DistanceToTriangle(float px, float pz, (float X, float Z) a,
            (float X, float Z) b, (float X, float Z) c)
            => Math.Min(DistanceToSegment(px, pz, a, b),
                Math.Min(DistanceToSegment(px, pz, b, c), DistanceToSegment(px, pz, c, a)));

        private static float DistanceToSegment(float px, float pz, (float X, float Z) a, (float X, float Z) b)
        {
            float dx = b.X - a.X, dz = b.Z - a.Z;
            float lenSq = dx * dx + dz * dz;
            float t = lenSq <= 0.000001f ? 0f : ((px - a.X) * dx + (pz - a.Z) * dz) / lenSq;
            t = Math.Clamp(t, 0f, 1f);
            float x = a.X + dx * t, z = a.Z + dz * t;
            float ex = px - x, ez = pz - z;
            return MathF.Sqrt(ex * ex + ez * ez);
        }
    }
}
```

- [ ] **步骤 5：运行 Unity EditMode 测试**

```bash
bash tools/unity-tests.sh EditMode NavMeshExportFreshnessTests
bash tools/unity-tests.sh EditMode DemoDayNavBinCoverageTests
```

预期：新鲜度测试在网络/场景条件满足时 PASS（否则 Ignore）；覆盖测试 PASS。若覆盖测试失败，先检查导出器的 `MapId`/tile 写入是否与读取顺序一致。

- [ ] **步骤 6：Commit**

```bash
git add Assets/Scripts/Editor/Navigation/NavMeshExporter.cs Assets/Scripts/Editor/Navigation/NavMeshExporter.cs.meta Assets/Scripts/Tests/Editor/Navigation Assets/Scripts/Tests/Editor/GameSys.EditorTests.asmdef Server/Config/nav/demoday.navbin
git commit -m "feat(nav): export DemoDay navmesh to server data"
```

---

## 任务 10：已提交 navbin 的服务端加载与 DemoDay 验收

**文件：**
- 创建：`Server/Server.Tests/Navigation/DemoDayNavBinTests.cs`

> **执行偏差（2026-11-03）**：本任务实际落为 3 个提交（`2bfbe52` 服务端 `--map` 加载、
> `8963d47` Unity 导出器 + 覆盖 / 新鲜度测试 + navbin、`1aec910` 验收修正）。
> 前者分属计划 Task 8 / Task 9，Task 10 单独提交为 `1aec910`（6 文件）：
> `Server/Server.Tests/Navigation/DemoDayNavBinTests.cs`（新增）、`Server/Monster/MonsterModule.cs`
> （SpiderBoss 槽位 (25,20) → (18.333,18.389)，原槽位 clearance 1.50 < bodyRadius 1.74）、
> `Assets/Scripts/Tests/Editor/Navigation/DemoDayNavBinCoverageTests.cs`（同步槽位）、
> `docs/superpowers/guides/p5-monster-server.md`、`docs/superpowers/guides/network-playtest.md`、
> 本计划（Task 9/10 代码块修正）。跨岛路径 `[0] -> [5]` 因 x≈0 河谷分断返回 `blocked`
> （Unity `CalculatePath` 亦 `PathPartial`），验收改为同岛 `[3] -> [5]`（34.977m）。

- [ ] **步骤 1：写测试**

```csharp
using System.IO;
using KcpServer;
using KcpServer.Navigation;
using Xunit;

namespace KcpServer.Tests.Navigation;

public class DemoDayNavBinTests
{
    private static readonly Vector3[] Spawns =
    {
        new(-30f, 0f, 0f),
        new(0f, 0f, -20f),
        new(0f, 0f, -35f),
        new(10f, 0f, -15f),
        new(10f, 0f, 15f),
        new(18.333f, 0f, 18.389f)
    };

    [Fact]
    public void Shipped_NavBin_Loads_And_Covers_Fixed_Spawns()
    {
        string path = Path.Combine(System.AppContext.BaseDirectory, "Config", "nav", "demoday.navbin");
        Assert.True(File.Exists(path), $"missing {path} - run the Unity exporter");

        NavData data = NavDataFormat.Read(path);
        Assert.Equal("demoday", data.MapId);
        Assert.True(data.Vertices.Length > 100);
        Assert.NotEmpty(data.Tiles);

        var graph = NavMeshGraph.FromData(data);
        foreach (Vector3 spawn in Spawns)
        {
            Assert.True(graph.FindNearestTriangle(spawn, 1f, out int triangle, out float distance),
                $"spawn {spawn} not on navmesh");
            Assert.True(distance <= 0.5f, $"spawn {spawn} nearest={distance:F2}");
            Assert.True(triangle >= 0);
        }

        Assert.True(graph.FindNearestPoint(Spawns[5], 1f, out _, out _, out int bossTriangle));
        Assert.True(graph.GetClearance(bossTriangle) >= 1.74f,
            $"SpiderBoss spawn clearance={graph.GetClearance(bossTriangle):F2} < 1.74");
    }

    [Fact]
    public void Shipped_NavBin_Can_Path_Between_Two_Layout_Points()
    {
        string path = Path.Combine(System.AppContext.BaseDirectory, "Config", "nav", "demoday.navbin");
        NavData data = NavDataFormat.Read(path);
        var finder = new NavMeshPathfinder(NavMeshGraph.FromData(data));

        // 同岛：Spawns[0] 与 Spawns[5] 被 x≈0 河谷分断（Unity CalculatePath 也是 PathPartial）。
        NavPath path1 = finder.FindPath(Spawns[3], Spawns[5], 0.5f);
        Assert.True(path1.Success, path1.FailReason);
        Assert.True(path1.Length > 0f);
    }
}
```

- [ ] **步骤 2：运行**

```bash
bash tools/test-server.sh --filter "FullyQualifiedName~DemoDayNavBinTests"
```

预期：两个用例 PASS。若 `Shipped_NavBin_Loads` 失败，先确认 csproj 的 `Config/nav/*.navbin` 复制项生效，再确认导出器格式与 `NavDataFormat` 一致。

- [ ] **步骤 3：全量服务端测试**

```bash
bash tools/test-server.sh
```

预期：全绿。

- [ ] **步骤 4：Commit**

```bash
git add Server/Server.Tests/Navigation/DemoDayNavBinTests.cs Server/Monster/MonsterModule.cs \
  Assets/Scripts/Tests/Editor/Navigation/DemoDayNavBinCoverageTests.cs \
  docs/superpowers/guides/p5-monster-server.md docs/superpowers/guides/network-playtest.md \
  docs/superpowers/plans/2026-11-02-server-monster-navigation.md
git commit -m "fix(nav): relocate SpiderBoss spawn for navmesh clearance"
```

---

## 任务 11：E2E、文档与收尾

**文件：**
- 修改：`Server/Monster/README.md`
- 修改：`Server/README.md`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Monster/README.md`
- 修改：`CLAUDE.md`
- 修改：`docs/superpowers/guides/p5-monster-server.md`

- [ ] **步骤 1：更新文档**

`Server/Monster/README.md` 增加：
- P1 寻路：`Server/Navigation`、`PathFollower`、`--map demoday`、`--no-nav`；
- 测试命令：`bash tools/test-server.sh --filter "FullyQualifiedName~Navigation"`；
- 已知限制：单张 DemoDay、无动态障碍、bot 不走 navmesh。

`Server/README.md` 模块列表加 `Navigation/`。

`Assets/Scripts/Hotfix/GameSystems/Monster/README.md` 说明联网时服务端寻路、客户端只表现。

`CLAUDE.md` 模块索引加一行“服务端导航 | Server/Navigation/ | 三角面 A* + 漏斗、地图加载”。

`docs/superpowers/guides/p5-monster-server.md` 的启动示例改为 `--monsters 4 --map demoday`，并注明 `--no-nav` 负对照。

- [ ] **步骤 2：启动服务端做 E2E**

按记忆里的约束用 `dotnet <dll>` 而不是 `dotnet run`：

```bash
dotnet build Server/Server3C.csproj
dotnet Server/bin/Debug/net8.0/Server3C.dll 8000 --monsters 4 --map demoday
```

预期日志包含 `Navigation: map=demoday`、`Monster mode: 4 monsters spawned`，无异常。

- [ ] **步骤 3：跑 PlayMode 验收**

另开终端：

```bash
bash tools/unity-tests.sh PlayMode MonsterWorldTests
```

预期：PASS。若怪物因绕路导致既有测试超时，先看是不是测试假设“直线距离”或“固定时间到攻击距离”；按实际路径调整等待 tick，不要退回直线移动。

- [ ] **步骤 4：人工抽帧（DemoDay）**

在 Unity Play Mode 连接本地服务器，观察：
- Wolf 在岩石/湖岸附近追击时绕行、不穿模；
- SpiderBoss（bodyRadius 1.74）不插进岩石；
- 脱战回巢时没有横穿地图的滑行（P1 仍是旧 Return，P2 才做 Evade；本步骤只确认路径正常）。

- [ ] **步骤 5：最终全量测试**

```bash
bash tools/test-server.sh
bash tools/unity-tests.sh EditMode NavMeshExportFreshnessTests
bash tools/unity-tests.sh EditMode DemoDayNavBinCoverageTests
```

- [ ] **步骤 6：Commit**

```bash
git add Server/Monster/README.md Server/README.md Assets/Scripts/Hotfix/GameSystems/Monster/README.md CLAUDE.md docs/superpowers/guides/p5-monster-server.md
git commit -m "docs(nav): document server monster navigation"
```

---

## 风险与回退

- **探针返回 0**：不要继续写导出器；先确认 DemoDay 的 `NavMeshSurface` 数据已激活。必要时在 `NavMeshProbe` 里显式 `surface.AddData()` 后再取 triangulation。
- **漏斗路径穿墙**：优先检查 `GetPortal` 的左右顺序与 `Funnel` 的 `TriArea2` 符号；用 `NavMeshPathfinderTests.Wall_Path_Detours_Through_Gap` 的坐标打印 `points`。不要用放宽路径校验来掩盖。
- **大怪仍贴边**：先确认 `bodyRadius` 从模板表读到（`MonsterModule.RadiusOfTemplate`），再调 `ClearanceHardFactor`/`ClearanceSoftFactor`；不要直接改 navmesh 烘焙半径。
- **既有测试因 0.5m 到达阈值变化**：检查是巡逻点投影还是阈值；修实现，不放宽断言。
- **导出文件与加载器 hash 不一致**：对比 exporter 与 `NavDataFormat` 的写入顺序/字段类型；以 `NavDataFormat.Read` 的异常信息定位。
- **E2E 因 bot 人墙失败**：用 `--bots 0`（默认）或低 bot 数；bot 不在本期 navmesh 约束内。

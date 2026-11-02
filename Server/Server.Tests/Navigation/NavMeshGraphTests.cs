using System;
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
        Assert.Equal(12, graph.BoundaryEdgeCount); // 3m × 4 条外边界
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
    public void FindNearestPoint_Projects_Outside_Point_And_Keeps_Inside_Point()
    {
        NavMeshGraph graph = NavMeshFixture.Grid(5, 5, (_, _) => true);
        bool found = graph.FindNearestPoint(new Vector3(-0.5f, 0f, 2.5f), 2f,
            out Vector3 projected, out float distance, out int triangle);
        Assert.True(found);
        Assert.True(triangle >= 0);
        Assert.InRange(projected.X, -0.001f, 0.001f);
        Assert.InRange(projected.Z, 2.45f, 2.55f);
        Assert.InRange(distance, 0.45f, 0.55f);

        bool inside = graph.FindNearestPoint(new Vector3(1.5f, 0f, 1.5f), 0.01f,
            out Vector3 identity, out float insideDistance, out int insideTriangle);
        Assert.True(inside);
        Assert.True(insideTriangle >= 0);
        Assert.Equal(1.5f, identity.X, 3);
        Assert.Equal(1.5f, identity.Z, 3);
        Assert.Equal(0f, insideDistance, 3);
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
        Assert.Equal(graph.BoundaryEdgeCount, fromData.BoundaryEdgeCount);
        Assert.Equal(graph.CopyIndices(), fromData.CopyIndices());
        Assert.Equal(graph.CopyAreas(), fromData.CopyAreas());
        for (int t = 0; t < graph.TriangleCount; t++)
        {
            Assert.Equal(graph.GetNeighborCount(t), fromData.GetNeighborCount(t));
            for (int i = 0; i < graph.GetNeighborCount(t); i++)
                Assert.Equal(graph.GetNeighbor(t, i), fromData.GetNeighbor(t, i));
            Assert.Equal(graph.GetClearance(t), fromData.GetClearance(t), 3);
        }
    }

    [Fact]
    public void FromData_Connects_Triangles_Across_Tile_Boundary()
    {
        var data = new NavData
        {
            MapId = "two-tiles",
            MinX = 0, MinZ = 0, MaxX = 2, MaxZ = 1,
            TileSize = 64,
            AreaCosts = new[] { 1f },
            Vertices = new[]
            {
                new Vector3(0f, 0f, 0f), // 0
                new Vector3(1f, 0f, 0f), // 1
                new Vector3(0f, 0f, 1f), // 2
                new Vector3(1f, 0f, 1f), // 3
                new Vector3(2f, 0f, 0f), // 4
                new Vector3(2f, 0f, 1f)  // 5
            },
            Tiles = new[]
            {
                new NavTileData
                {
                    TileX = 0, TileZ = 0,
                    Indices = new[] { 0, 1, 3, 0, 3, 2 }, Areas = new byte[] { 0, 0 }
                },
                new NavTileData
                {
                    TileX = 1, TileZ = 0,
                    Indices = new[] { 1, 4, 5, 1, 5, 3 }, Areas = new byte[] { 0, 0 }
                }
            }
        };

        NavMeshGraph graph = NavMeshGraph.FromData(data);
        Assert.Equal(4, graph.TriangleCount);
        Assert.Equal(6, graph.BoundaryEdgeCount); // 2m × 1m 外边界
        Assert.Equal(2, graph.GetNeighborCount(0));
        Assert.Equal(1, graph.GetNeighborCount(1));
        Assert.Equal(1, graph.GetNeighborCount(2));
        Assert.Equal(2, graph.GetNeighborCount(3));
        Assert.Contains(3, Neighbors(graph, 0)); // tile 0 的三角面 0 与 tile 1 的三角面 3 共享边
        Assert.Contains(0, Neighbors(graph, 3));
        Assert.Contains(1, Neighbors(graph, 0)); // tile 内对角边
        Assert.Contains(0, Neighbors(graph, 1));
    }

    [Fact]
    public void Constructor_Does_Not_Modify_Caller_Arrays_When_Normalizing_Winding()
    {
        var vertices = new[]
        {
            new Vector3(0f, 0f, 0f), // 0
            new Vector3(1f, 0f, 0f), // 1
            new Vector3(1f, 0f, 1f), // 2
            new Vector3(0f, 0f, 1f)  // 3
        };
        var indices = new[] { 0, 2, 1, 0, 3, 2 }; // 逆序 winding，构造期需要归一化
        var areas = new byte[] { 0, 0 };
        var areaCosts = new[] { 1f };

        NavMeshGraph graph = NavMeshGraph.FromTriangles(vertices, indices, areas, areaCosts);

        Assert.Equal(new[] { 0, 2, 1, 0, 3, 2 }, indices); // 调用方数组未被改写
        Assert.Equal(new[] { 0, 1, 2, 0, 2, 3 }, graph.CopyIndices()); // 图内部已归一化
        Assert.Equal(2, graph.TriangleCount);
        Assert.Equal(4, graph.BoundaryEdgeCount);
        Assert.Equal(1, graph.GetNeighborCount(0));
        Assert.Equal(1, graph.GetNeighborCount(1));
        Assert.Equal(1, graph.GetNeighbor(0, 0));
        Assert.Equal(0, graph.GetNeighbor(1, 0));

        // 图拥有自己的数组：构造后修改调用方数组不应影响内部状态
        indices[0] = 99;
        vertices[0] = new Vector3(5f, 0f, 5f);
        areas[0] = 1;
        areaCosts[0] = 7f;
        Assert.Equal(0, graph.CopyIndices()[0]);
        Assert.Equal(0f, graph.CopyVertices()[0].X);
        Assert.Equal(0, graph.CopyAreas()[0]);
        Assert.Equal(1f, graph.AreaCost(0));
    }

    [Fact]
    public void Constructor_Throws_On_NonManifold_Edge_And_Leaves_Caller_Arrays_Unchanged()
    {
        var vertices = new[]
        {
            new Vector3(0f, 0f, 0f),    // 0
            new Vector3(1f, 0f, 0f),    // 1
            new Vector3(0.5f, 0f, 1f),  // 2
            new Vector3(0.5f, 0f, -1f), // 3
            new Vector3(0.5f, 0f, 2f)   // 4
        };
        // 第一个三角面为逆序（构造期会归一化），第三个三角面与另两个共享边 0-1
        var indices = new[] { 0, 2, 1, 1, 0, 3, 0, 1, 4 };
        var areas = new byte[] { 0, 0, 0 };

        Assert.Throws<InvalidOperationException>(
            () => NavMeshGraph.FromTriangles(vertices, indices, areas, new[] { 1f }));
        Assert.Equal(new[] { 0, 2, 1, 1, 0, 3, 0, 1, 4 }, indices);
    }

    [Fact]
    public void GetPortal_Orders_Vertices_Left_And_Right()
    {
        var vertices = new[]
        {
            new Vector3(0f, 0f, 0f), // 0
            new Vector3(1f, 0f, 0f), // 1
            new Vector3(0f, 0f, 1f), // 2
            new Vector3(1f, 0f, 1f)  // 3
        };
        var indices = new[] { 0, 1, 2, 1, 3, 2 };
        NavMeshGraph graph = NavMeshGraph.FromTriangles(vertices, indices, new byte[] { 0, 0 }, new[] { 1f });

        // 共享边为顶点 1 (1,0) 与 2 (0,1)；从三角面 0 走向三角面 1 时，左为 (0,1)，右为 (1,0)
        Assert.True(graph.GetPortal(0, 1, out Vector3 left, out Vector3 right));
        Assert.Equal(0f, left.X, 3);
        Assert.Equal(1f, left.Z, 3);
        Assert.Equal(1f, right.X, 3);
        Assert.Equal(0f, right.Z, 3);

        Assert.True(graph.GetPortal(1, 0, out Vector3 backLeft, out Vector3 backRight));
        Assert.Equal(1f, backLeft.X, 3);
        Assert.Equal(0f, backLeft.Z, 3);
        Assert.Equal(0f, backRight.X, 3);
        Assert.Equal(1f, backRight.Z, 3);
    }

    private static int[] Neighbors(NavMeshGraph graph, int triangle)
    {
        var result = new int[graph.GetNeighborCount(triangle)];
        for (int i = 0; i < result.Length; i++) result[i] = graph.GetNeighbor(triangle, i);
        return result;
    }
}

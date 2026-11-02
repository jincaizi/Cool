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

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
    public void Wall_Path_Keeps_Body_Radius_Outside_Endpoint_Zones()
    {
        NavMeshGraph graph = WallGraph();
        var finder = new NavMeshPathfinder(graph);
        NavPath path = finder.FindPath(new Vector3(1f, 0f, 1f), new Vector3(1f, 0f, 9f), 0.4f);
        Assert.True(path.Success, path.FailReason);

        float pathLength = 0f;
        for (int i = 1; i < path.Waypoints.Length; i++)
            pathLength += DistanceXZ(path.Waypoints[i - 1], path.Waypoints[i]);

        float along = 0f;
        int sampled = 0;
        for (int i = 1; i < path.Waypoints.Length; i++)
        {
            Vector3 a = path.Waypoints[i - 1], b = path.Waypoints[i];
            float length = DistanceXZ(a, b);
            int steps = Math.Max(1, (int)MathF.Ceiling(length / 0.1f));
            for (int s = 0; s <= steps; s++)
            {
                float t = s / (float)steps;
                float distance = along + t * length;
                if (distance < 1f || pathLength - distance < 1f) continue;
                var p = new Vector3(a.X + (b.X - a.X) * t, a.Y, a.Z + (b.Z - a.Z) * t);
                Assert.True(graph.IsPointNavigable(p, 0.4f),
                    $"point {p} at {distance:F2}m lacks clearance for r=0.4");
                sampled++;
            }
            along += length;
        }
        Assert.True(sampled > 0);
    }

    [Fact]
    public void Diagonal_Corner_Waypoints_Keep_Body_Radius()
    {
        // L 形区域：3x1 横臂 + (0,1) 竖臂；凹角在 (1,1)，路径必须绕角。
        Vector3[] vertices =
        {
            new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(2f, 0f, 0f), new Vector3(3f, 0f, 0f),
            new Vector3(0f, 0f, 1f), new Vector3(1f, 0f, 1f), new Vector3(2f, 0f, 1f), new Vector3(3f, 0f, 1f),
            new Vector3(0f, 0f, 2f), new Vector3(1f, 0f, 2f),
        };
        int[] indices =
        {
            0, 1, 5, 0, 5, 4, // cell (0,0)
            1, 2, 6, 1, 6, 5, // cell (1,0)
            2, 3, 7, 2, 7, 6, // cell (2,0)
            4, 5, 9, 4, 9, 8, // cell (0,1)
        };
        NavMeshGraph graph = NavMeshGraph.FromTriangles(vertices, indices, new byte[8], new[] { 1f });
        var finder = new NavMeshPathfinder(graph);
        NavPath path = finder.FindPath(new Vector3(2.6f, 0f, 0.3f), new Vector3(0.3f, 0f, 1.7f), 0.3f);
        Assert.True(path.Success, path.FailReason);
        Assert.True(path.Waypoints.Length >= 3, "path should bend around the corner");
        for (int i = 1; i < path.Waypoints.Length - 1; i++)
            Assert.True(graph.IsPointNavigable(path.Waypoints[i], 0.3f),
                $"waypoint {path.Waypoints[i]} lacks clearance for r=0.3");
    }

    private static float DistanceXZ(Vector3 a, Vector3 b)
    {
        float dx = a.X - b.X, dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    [Fact]
    public void Same_Triangle_Endpoints_Route_Through_Neighbors_When_Direct_Is_Too_Close_To_Edge()
    {
        // 浅帐篷形 A 沿底部边界（y=0），起终点都在 A 内、离边界 0.2m：直连不满足 0.5m clearance。
        // A 的两条内边分别接 B/C，B-D-C 绕到上方再回到 A —— 必须允许 startTri==goalTri 时继续扩展。
        Vector3[] vertices =
        {
            new Vector3(0f, 0f, 0f), new Vector3(30f, 0f, 0f), new Vector3(15f, 0f, 6f),
            new Vector3(0f, 0f, 12f), new Vector3(30f, 0f, 12f)
        };
        int[] indices =
        {
            0, 1, 2,        // A
            0, 2, 3,        // B
            1, 4, 2,        // C
            2, 4, 3         // D
        };
        NavMeshGraph graph = NavMeshGraph.FromTriangles(vertices, indices, new byte[4], new[] { 1f });
        var finder = new NavMeshPathfinder(graph);

        var start = new Vector3(2f, 0f, 0.3f);
        var goal = new Vector3(28f, 0f, 0.3f);
        NavPath path = finder.FindPath(start, goal, 0.5f);
        Assert.True(path.Success, path.FailReason);
        Assert.True(path.Waypoints.Length >= 3, "path should detour through the upper triangles");
        Assert.True(path.Length > 26.2f, $"detour should exceed straight-line 26m, got {path.Length:F2}");
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

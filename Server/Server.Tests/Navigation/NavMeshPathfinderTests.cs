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

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

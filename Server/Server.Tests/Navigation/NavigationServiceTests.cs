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
